using System.Diagnostics;
using System.Security.Cryptography;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Platform;
using Fortiva.Core.Updates;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Personal edition only. HTTPS check + SHA-256 verified silent installer.
/// This is the only intentional network use in Fortiva Personal.
/// </summary>
public sealed class UpdateService
{
    public static UpdateService Current { get; } = new();

    private readonly UpdateChecker _checker = new();
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private bool _launchCheckStarted;

    public event Action<UpdateCheckResult>? CheckCompleted;

    public bool IsEnabled =>
        !(_vm.IsEnterprise || _vm.IsAdmin) && _vm.PersonalSettings.AutoUpdateEnabled;

    public async Task TryAutoUpdateOnLaunchAsync()
    {
        if (_launchCheckStarted || !IsEnabled) return;
        if (string.Equals(Environment.GetEnvironmentVariable("FORTIVA_SKIP_AUTO_UPDATE"), "1", StringComparison.Ordinal))
            return;
        _launchCheckStarted = true;

        if (!ShouldCheckNow(_vm.PersonalSettings.LastUpdateCheckUtc, TimeSpan.FromHours(24)))
            return;

        try
        {
            var result = await CheckAsync().ConfigureAwait(false);
            if (result.Status == UpdateStatus.UpdateAvailable && result.Manifest is not null && !_vm.IsUnlocked)
            {
                try
                {
                    await ApplyAsync(result.Manifest, silent: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _vm.RecordUpdateApplyFailure(UpdateMessages.ForApplyFailure(ex));
                }
            }
        }
        catch (Exception ex)
        {
            App.LogException("TryAutoUpdateOnLaunchAsync", ex);
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_vm.IsEnterprise || _vm.IsAdmin)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.CheckFailed,
                Message = "Enterprise editions are updated by IT (Intune), not from the public release feed."
            };
        }

        UpdateCheckResult result;
        try
        {
            result = await _checker.CheckAsync(
                ReleaseManifestUrls.PersonalLatest,
                AppVersion.Current,
                Path.Combine(AppContext.BaseDirectory, "releases", "latest.personal.json"),
                cancellationToken).ConfigureAwait(false);

            if (result.Status != UpdateStatus.CheckFailed)
            {
                _vm.PersonalSettings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
                _vm.SavePersonalSettings();
            }
        }
        catch (Exception ex)
        {
            result = new UpdateCheckResult
            {
                Status = UpdateStatus.CheckFailed,
                Message = UpdateMessages.ForCheckFailure(ex)
            };
        }

        CheckCompleted?.Invoke(result);
        return result;
    }

    public async Task<bool> ApplyAsync(ReleaseManifest manifest, bool silent = false)
    {
        try
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var dest = Path.Combine(
                Path.GetTempPath(),
                $"FortivaPersonal-{manifest.Version}-{nonce}-Setup.exe");

            await _checker.DownloadVerifiedAsync(manifest, dest).ConfigureAwait(false);

            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(dest)))
                .ToLowerInvariant();
            if (!hash.Equals(manifest.InstallerSha256.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidOperationException("Installer failed pre-launch integrity check.");

            // Re-verify immediately before launch to narrow TOCTOU window on %TEMP%.
            var launchHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(dest)))
                .ToLowerInvariant();
            if (!launchHash.Equals(manifest.InstallerSha256.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidOperationException("Installer changed after verification.");

            if (AuthenticodePolicy.RequireSignedExecutables && !AuthenticodeVerifier.IsSigned(dest))
                throw new InvalidOperationException("Installer is not Authenticode-signed.");

            // Lock before backup so vault.fva is quiesced on disk (manual path often starts unlocked).
            await EnsureVaultLockedAsync().ConfigureAwait(false);
            TryStopBridgeHost();

            var backup = PreUpdateVaultBackup.TryCreate(_vm.VaultDirectory, manifest.Version);
            if (!string.IsNullOrEmpty(backup.ErrorMessage))
            {
                throw new InvalidOperationException(
                    "Could not back up your vault before updating. " + backup.ErrorMessage);
            }

            if (_vm.VaultExists && !backup.VaultCopied)
            {
                throw new InvalidOperationException(
                    "Could not back up your vault before updating. Close other Fortiva windows and try again.");
            }

            var exePath = ResolveInstalledExePath();
            var installerArgs = UpdateUrlPolicy.ResolveInstallerArgs(manifest);
            if (!LaunchInstallerWithRelaunch(dest, installerArgs, exePath, manifest.Version))
            {
                throw new InvalidOperationException(
                    "The updater did not start correctly. Your current version is unchanged. "
                    + "Please download and run the latest installer manually.");
            }

            // Detached helper must survive App.Exit — brief pause so it begins waiting.
            await Task.Delay(800).ConfigureAwait(false);

            _vm.ClearUpdateApplyFailure();
            App.ExitForUpdate();
            return true;
        }
        catch (Exception ex)
        {
            App.LogException("ApplyAsync", ex);
            _vm.RecordUpdateApplyFailure(UpdateMessages.ForApplyFailure(ex));
            throw;
        }
    }

    /// <summary>
    /// Locks the vault and waits for it to take effect. <see cref="ShellViewModel.Lock"/> dispatches
    /// to the UI thread, so we poll briefly rather than assuming it completes synchronously.
    /// </summary>
    private async Task EnsureVaultLockedAsync()
    {
        if (!_vm.IsUnlocked)
            return;

        _vm.Lock();
        for (var i = 0; i < 150 && _vm.IsUnlocked; i++)
            await Task.Delay(20).ConfigureAwait(false);

        if (_vm.IsUnlocked)
            throw new InvalidOperationException("Could not lock the vault before updating. Lock the vault and try again.");
    }

    /// <summary>Stops the browser bridge so the installer can replace files under {app}.</summary>
    internal static void TryStopBridgeHost()
    {
        foreach (var process in Process.GetProcessesByName("Fortiva.BrowserBridge.Host"))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Waits briefly for the Inno Setup process to start (or finish successfully if very fast).
    /// Returns false if the process never started or exited immediately with an error.
    /// </summary>
    internal static async Task<bool> ConfirmInstallerStartedAsync(Process? installer)
    {
        if (installer is null)
            return false;

        await Task.Delay(600).ConfigureAwait(false);

        try
        {
            if (installer.HasExited)
                return installer.ExitCode == 0;
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < 24; i++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            try
            {
                if (installer.HasExited)
                    return installer.ExitCode == 0;
            }
            catch
            {
                return true;
            }
        }

        return true;
    }

    internal static async Task<bool> ConfirmUpdateHelperStartedAsync(Process helper)
    {
        await Task.Delay(400).ConfigureAwait(false);
        try
        {
            return !helper.HasExited || helper.ExitCode == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Detached cmd helper: wait for Fortiva to exit, run installer to completion, relaunch if needed.
    /// Must not be a child of Fortiva — the app process exits immediately after this returns.
    /// </summary>
    internal static bool LaunchInstallerWithRelaunch(
        string installerPath,
        string installerArgs,
        string exePath,
        string? targetVersion = null)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            return false;

        var batchId = Guid.NewGuid().ToString("N");
        var batchPath = Path.Combine(Path.GetTempPath(), $"fortiva-update-{batchId}.cmd");
        var logPath = ResolveUpdateLogPath();
        File.WriteAllText(
            batchPath,
            BuildUpdateBatchScript(installerPath, installerArgs, exePath, batchPath, logPath, targetVersion));

        WriteUpdateLog(logPath, $"Launching detached update helper (target {targetVersion ?? "unknown"}).");
        WriteUpdateLog(logPath, $"Installer: {installerPath}");
        WriteUpdateLog(logPath, $"Relaunch: {exePath}");

        // `start` via ShellExecute so the helper is not in Fortiva's job object and survives App.Exit.
        var started = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"FortivaUpdate\" /min {QuoteCmdPath(batchPath)}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        });

        return started is not null;
    }

    internal static string ResolveUpdateLogPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortivaPersonal", "logs", "update.log");

    internal static void WriteUpdateLog(string logPath, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Best effort — never block updates on logging.
        }
    }

    internal static string BuildUpdateBatchScript(
        string installerPath,
        string installerArgs,
        string exePath,
        string batchPath,
        string logPath,
        string? targetVersion = null)
    {
        var quotedInstaller = QuoteCmdPath(installerPath);
        var quotedExe = QuoteCmdPath(exePath);
        var quotedBatch = QuoteCmdPath(batchPath);
        var quotedLog = QuoteCmdPath(logPath);
        var args = string.IsNullOrWhiteSpace(installerArgs) ? string.Empty : installerArgs.Trim();
        var versionLabel = string.IsNullOrWhiteSpace(targetVersion) ? "unknown" : targetVersion.Trim();

        return
            "@echo off\r\n" +
            "setlocal EnableExtensions\r\n" +
            $"set \"FORTIVA_LOG={quotedLog}\"\r\n" +
            "if not exist \"%LOCALAPPDATA%\\FortivaPersonal\\logs\" mkdir \"%LOCALAPPDATA%\\FortivaPersonal\\logs\" >nul 2>&1\r\n" +
            $"echo [%date% %time%] helper started target {versionLabel}>>\"%FORTIVA_LOG%\"\r\n" +
            "set /a _tries=0\r\n" +
            ":waitfortiva\r\n" +
            "set /a _tries+=1\r\n" +
            "if %_tries% gtr 90 goto runsetup\r\n" +
            "tasklist /FI \"IMAGENAME eq Fortiva.Personal.exe\" 2>nul | find /I \"Fortiva.Personal.exe\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto waitfortiva\r\n" +
            ")\r\n" +
            "echo [%date% %time%] Fortiva exited, running installer>>\"%FORTIVA_LOG%\"\r\n" +
            ":runsetup\r\n" +
            $"start \"\" /wait {quotedInstaller} {args}\r\n" +
            "set _setup_err=%errorlevel%\r\n" +
            "echo [%date% %time%] installer finished exit %_setup_err%>>\"%FORTIVA_LOG%\"\r\n" +
            "timeout /t 3 /nobreak >nul\r\n" +
            "tasklist /FI \"IMAGENAME eq Fortiva.Personal.exe\" 2>nul | find /I \"Fortiva.Personal.exe\" >nul\r\n" +
            "if errorlevel 1 (\r\n" +
            "  echo [%date% %time%] relaunching Fortiva>>\"%FORTIVA_LOG%\"\r\n" +
            $"  start \"\" {quotedExe}\r\n" +
            ") else (\r\n" +
            "  echo [%date% %time%] Fortiva already running>>\"%FORTIVA_LOG%\"\r\n" +
            ")\r\n" +
            $"del /f /q {quotedBatch}\r\n" +
            "endlocal\r\n";
    }

    internal static string QuoteCmdPath(string path)
        => $"\"{path.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    internal static string ResolveInstalledExePath()
    {
        var fromProcess = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(fromProcess) && File.Exists(fromProcess))
            return fromProcess;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "icmclab studio", "Fortiva Personal", "Fortiva.Personal.exe");
    }

    public static bool ShouldCheckNow(DateTimeOffset? lastCheck, TimeSpan interval)
        => lastCheck is null || DateTimeOffset.UtcNow - lastCheck.Value >= interval;

    public static string FormatUpdateStatus(UpdateCheckResult result) => result.Status switch
    {
        UpdateStatus.UpToDate => result.Message ?? "Up to date.",
        UpdateStatus.UpdateAvailable => result.Message ?? "Update available.",
        UpdateStatus.PlatformUntested => result.Message ?? "Update recommended for this Windows version.",
        UpdateStatus.PlatformUnsupported => result.Message ?? "Windows version not supported.",
        UpdateStatus.CheckFailed => result.Message ?? UpdateMessages.ManifestUnavailable,
        _ => "Unknown"
    };
}
