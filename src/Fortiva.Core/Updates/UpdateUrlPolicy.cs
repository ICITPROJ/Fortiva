using System.Text.RegularExpressions;

namespace Fortiva.Core.Updates;

public static class UpdateUrlPolicy
{
    private static readonly HashSet<string> AllowedLegacyHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "studio.icmclab.cloud",
        "icmclab.cloud"
    };

    private static readonly HashSet<string> AllowedGitHubRepositories = new(StringComparer.OrdinalIgnoreCase)
    {
        ReleaseManifestUrls.GitHubRepository
    };

    private static readonly Regex GitHubReleaseAssetPath = new(
        @"^/[^/]+/[^/]+/releases/(?:latest/download|download/[^/]+)/[^/]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PersonalInstallerName = new(
        @"^FortivaPersonal-\d+\.\d+\.\d+-Setup\.exe$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string DefaultInstallerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    public static void ValidateManifestUrl(string manifestUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Update manifest URL is invalid.");
        RequireHttps(uri);

        if (IsAllowedGitHubManifest(uri))
            return;

        if (IsAllowedLegacyManifest(uri))
            return;

        throw new InvalidOperationException("Update manifest host is not allowed.");
    }

    public static void ValidateInstallerUrl(string installerUrl)
    {
        if (!Uri.TryCreate(installerUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Installer URL is invalid.");
        RequireHttps(uri);

        if (IsAllowedGitHubInstaller(uri))
            return;

        if (IsAllowedLegacyInstaller(uri))
            return;

        throw new InvalidOperationException("Installer URL path is not allowed.");
    }

    private static void RequireHttps(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update URLs must use HTTPS.");
    }

    private static bool IsAllowedGitHubManifest(Uri uri)
        => IsAllowedGitHubReleaseAsset(uri, ReleaseManifestUrls.ManifestFileName);

    private static bool IsAllowedGitHubInstaller(Uri uri)
    {
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!GitHubReleaseAssetPath.IsMatch(uri.AbsolutePath))
            return false;

        var repo = ExtractGitHubRepository(uri);
        if (repo is null || !AllowedGitHubRepositories.Contains(repo))
            return false;

        var fileName = ExtractGitHubAssetFileName(uri);
        return PersonalInstallerName.IsMatch(fileName);
    }

    private static bool IsAllowedGitHubReleaseAsset(Uri uri, string requiredFileName)
    {
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!GitHubReleaseAssetPath.IsMatch(uri.AbsolutePath))
            return false;

        var repo = ExtractGitHubRepository(uri);
        if (repo is null || !AllowedGitHubRepositories.Contains(repo))
            return false;

        var fileName = ExtractGitHubAssetFileName(uri);
        return string.Equals(fileName, requiredFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedLegacyManifest(Uri uri)
        => AllowedLegacyHosts.Contains(uri.Host) &&
           uri.AbsolutePath.Contains("/fortiva/", StringComparison.OrdinalIgnoreCase) &&
           uri.AbsolutePath.EndsWith(ReleaseManifestUrls.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedLegacyInstaller(Uri uri)
        => AllowedLegacyHosts.Contains(uri.Host) &&
           uri.AbsolutePath.Contains("/fortiva/", StringComparison.OrdinalIgnoreCase) &&
           PersonalInstallerName.IsMatch(Path.GetFileName(uri.LocalPath));

    private static string? ExtractGitHubRepository(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"{segments[0]}/{segments[1]}" : null;
    }

    private static string ExtractGitHubAssetFileName(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Uri.UnescapeDataString(segments[^1]);
    }
}
