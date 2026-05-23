using Fortiva.Core.Platform;

namespace Fortiva.Core.Updates;

public static class ReleaseManifestEvaluator
{
    public static UpdateCheckResult Evaluate(
        ReleaseManifest manifest,
        string currentVersion,
        bool fromNetwork)
    {
        UpdateUrlPolicy.ValidateInstallerUrl(manifest.InstallerUrl);

        var platform = WindowsPlatformInfo.Check(manifest.MinWindowsBuild, manifest.MaxWindowsBuildTested);
        if (!platform.IsSupported)
            return new UpdateCheckResult
            {
                Status = UpdateStatus.PlatformUnsupported,
                Manifest = manifest,
                Message = platform.Message,
                Platform = platform,
                IsOnlineManifest = fromNetwork
            };

        if (!AppVersion.IsRemoteNewer(manifest.Version, currentVersion))
            return new UpdateCheckResult
            {
                Status = UpdateStatus.UpToDate,
                Manifest = manifest,
                Message = fromNetwork
                    ? "You have the latest version."
                    : UpdateMessages.OfflineUpToDate(manifest.Version),
                Platform = platform,
                IsOnlineManifest = fromNetwork
            };

        if (platform.IsUntested)
            return new UpdateCheckResult
            {
                Status = UpdateStatus.PlatformUntested,
                Manifest = manifest,
                Message = fromNetwork
                    ? platform.Message
                    : UpdateMessages.OfflineUpdateAvailable(manifest.Version),
                Platform = platform,
                IsOnlineManifest = fromNetwork
            };

        return new UpdateCheckResult
        {
            Status = UpdateStatus.UpdateAvailable,
            Manifest = manifest,
            Message = fromNetwork
                ? $"Version {manifest.Version} is available."
                : UpdateMessages.OfflineUpdateAvailable(manifest.Version),
            Platform = platform,
            IsOnlineManifest = fromNetwork
        };
    }
}
