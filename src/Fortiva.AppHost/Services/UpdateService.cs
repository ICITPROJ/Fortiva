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

            var backup = PreUpdateVaultBackup.TryCreate(_vm.VaultDirectory, manifest.Version);
            if (!string.IsNullOrEmpty(backup.ErrorMessage))
                App.LogException("PreUpdateVaultBackup", new InvalidOperationException(backup.ErrorMessage));

            // Re-verify immediately before launch to narrow TOCTOU window on %TEMP%.
            var launchHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(dest)))
                .ToLowerInvariant();
            if (!launchHash.Equals(manifest.InstallerSha256.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidOperationException("Installer changed after verification.");

            if (!AuthenticodeVerifier.IsSigned(dest))
                throw new InvalidOperationException("Installer is not Authenticode-signed.");

            // The manual "Install now" path is reached from Settings while the vault is unlocked.
            // Lock here — only once download + verification have succeeded — so a download/verify
            // failure leaves the user unlocked with a visible error instead of silently locking.
            // Locking scrubs secrets and releases the vault file before the installer runs.
            await EnsureVaultLockedAsync().ConfigureAwait(false);
            TryStopBridgeHost();

            var installer = Process.Start(new ProcessStartInfo
            {
                FileName = dest,
                Arguments = UpdateUrlPolicy.ResolveInstallerArgs(manifest),
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath()
            });

            if (!await ConfirmInstallerStartedAsync(installer).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The updater did not start correctly. Your current version is unchanged. "
                    + "Please download and run the latest installer manually.");
            }

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
    /// Waits briefly for the Inno Setup process to stay alive (started successfully).
    /// Returns false if the process never started or exited immediately with an error.
    /// </summary>
    internal static async Task<bool> ConfirmInstallerStartedAsync(Process? installer)
    {
        if (installer is null)
            return false;

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            try
            {
                if (!installer.HasExited)
                    return true;
                if (installer.ExitCode != 0)
                    return false;
            }
            catch
            {
                // Process handle may be unavailable after exit — treat as started if we got past launch.
                return i > 0;
            }
        }

        return true;
    }

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
