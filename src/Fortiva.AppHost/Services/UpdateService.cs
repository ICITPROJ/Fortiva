using System.Diagnostics;
using System.Text;
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
            if (result.Status == UpdateStatus.UpdateAvailable && result.Manifest is not null)
                await ApplyAsync(result.Manifest, silent: true).ConfigureAwait(false);
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
            var dest = Path.Combine(
                Path.GetTempPath(),
                $"FortivaPersonal-{manifest.Version}-Setup.exe");

            await _checker.DownloadVerifiedAsync(manifest, dest).ConfigureAwait(false);

            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(dest)))
                .ToLowerInvariant();
            if (!hash.Equals(manifest.InstallerSha256.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidOperationException("Installer failed pre-launch integrity check.");

            LaunchInstallerWithRestart(dest, ResolveInstalledExePath(), UpdateUrlPolicy.DefaultInstallerArgs);
            App.ExitForUpdate();
            return true;
        }
        catch (Exception ex)
        {
            App.LogException("ApplyAsync", ex);
            throw;
        }
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

    internal static void LaunchInstallerWithRestart(string installerPath, string appExePath, string installerArgs)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"fortiva-update-{Guid.NewGuid():N}.cmd");
        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine($"start /wait \"\" \"{installerPath}\" {installerArgs}")
            .AppendLine($"if exist \"{appExePath}\" start \"\" \"{appExePath}\"")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
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
