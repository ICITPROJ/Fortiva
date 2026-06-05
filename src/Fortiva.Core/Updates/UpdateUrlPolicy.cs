using System.Text.RegularExpressions;

namespace Fortiva.Core.Updates;

public static class UpdateUrlPolicy
{
    /// <summary>Legacy icmclab.cloud update feed stops working after this UTC date.</summary>
    public static DateTimeOffset LegacyFeedSunsetUtc { get; } = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<string> AllowedLegacyHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "studio.icmclab.cloud",
        "icmclab.cloud"
    };

    private static readonly HashSet<string> AllowedGitHubRepositories = new(StringComparer.OrdinalIgnoreCase)
    {
        ReleaseManifestUrls.GitHubRepository
    };

    private static readonly HashSet<string> AllowedGitHubCdnHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "release-assets.githubusercontent.com"
    };

    private static readonly Regex GitHubReleaseAssetPath = new(
        @"^/[^/]+/[^/]+/releases/(?:latest/download|download/[^/]+)/[^/]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PersonalInstallerName = new(
        @"^FortivaPersonal-\d+(?:\.\d+){0,3}-Setup\.exe$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string DefaultInstallerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    /// <summary>Returns manifest installer args when every token is an allowed Inno Setup switch; otherwise the default.</summary>
    public static string ResolveInstallerArgs(ReleaseManifest manifest)
    {
        var raw = manifest.InstallerArgs?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultInstallerArgs;

        foreach (var token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsAllowedInstallerToken(token))
                return DefaultInstallerArgs;
        }

        return raw;
    }

    public static void ValidateManifestUrl(string manifestUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Update manifest URL is invalid.");
        RequireHttps(uri);

        if (IsAllowedGitHubManifest(uri))
            return;

        if (IsAllowedGitHubReleaseCdnManifest(uri))
            return;

        if (IsAllowedGitHubRawManifest(uri))
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

        if (IsAllowedGitHubReleaseCdnInstaller(uri))
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

    private static bool IsGitHubReleaseCdn(Uri uri)
    {
        if (!AllowedGitHubCdnHosts.Contains(uri.Host))
            return false;

        RequireHttps(uri);
        return uri.AbsolutePath.StartsWith("/github-production-release-asset/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedGitHubReleaseCdnManifest(Uri uri)
        => IsGitHubReleaseCdn(uri) &&
           string.Equals(ExtractGitHubAssetFileName(uri), ReleaseManifestUrls.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedGitHubRawManifest(Uri uri)
    {
        if (!string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return false;

        RequireHttps(uri);
        var expectedPath = $"/{ReleaseManifestUrls.GitHubRepository}/main/packaging/releases/{ReleaseManifestUrls.ManifestFileName}";
        return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedGitHubReleaseCdnInstaller(Uri uri)
        => IsGitHubReleaseCdn(uri) &&
           PersonalInstallerName.IsMatch(ExtractGitHubAssetFileName(uri));

    /// <summary>
    /// Silent-install switches that carry no free-form value. Value-bearing switches such as
    /// /DIR=, /LOG=, or /LOADINF= are intentionally excluded — a tampered/compromised manifest
    /// must not be able to redirect the installer to attacker-controlled paths or INF files.
    /// </summary>
    private static readonly HashSet<string> AllowedInstallerSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "/VERYSILENT",
        "/SILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CLOSEAPPLICATIONS",
        "/NOCLOSEAPPLICATIONS",
        "/FORCECLOSEAPPLICATIONS",
        "/RESTARTAPPLICATIONS",
        "/NOICONS"
    };

    private static bool IsAllowedInstallerToken(string token)
    {
        // Reject anything carrying a value (contains '=') — only bare silent switches are allowed.
        if (token.Contains('=', StringComparison.Ordinal))
            return false;
        return AllowedInstallerSwitches.Contains(token);
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
        => IsLegacyFeedActive() &&
           AllowedLegacyHosts.Contains(uri.Host) &&
           uri.AbsolutePath.Contains("/fortiva/", StringComparison.OrdinalIgnoreCase) &&
           uri.AbsolutePath.EndsWith(ReleaseManifestUrls.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedLegacyInstaller(Uri uri)
        => IsLegacyFeedActive() &&
           AllowedLegacyHosts.Contains(uri.Host) &&
           uri.AbsolutePath.Contains("/fortiva/", StringComparison.OrdinalIgnoreCase) &&
           PersonalInstallerName.IsMatch(Path.GetFileName(uri.LocalPath));

    internal static bool IsLegacyFeedActive(DateTimeOffset? utcNow = null)
        => (utcNow ?? DateTimeOffset.UtcNow) < LegacyFeedSunsetUtc;

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
