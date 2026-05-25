using System.Text.Json.Serialization;



namespace Fortiva.Core.Updates;



/// <summary>Published as a GitHub Release asset (latest.personal.json).</summary>

public sealed class ReleaseManifest

{

    public int SchemaVersion { get; set; } = 1;

    public string Edition { get; set; } = "Personal";

    public string Version { get; set; } = "1.0.0";

    public DateTimeOffset ReleasedAt { get; set; }

    public int MinWindowsBuild { get; set; } = 19041;

    public int MaxWindowsBuildTested { get; set; } = 26100;

    public string InstallerUrl { get; set; } = "";

    public string InstallerSha256 { get; set; } = "";

    public string InstallerArgs { get; set; } = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    public string? ReleaseNotes { get; set; }



    [JsonIgnore]

    public bool IsValid =>

        SchemaVersion >= 1 &&

        !string.IsNullOrWhiteSpace(Version) &&

        !string.IsNullOrWhiteSpace(InstallerUrl) &&

        InstallerSha256.Length == 64 &&

        !IsPlaceholderSha256(InstallerSha256);



    private static bool IsPlaceholderSha256(string sha)

        => sha.All(static c => c is '0' or 'f' or 'F');

}



public static class ReleaseManifestUrls

{

    /// <summary>GitHub owner/repo that publishes Fortiva release assets.</summary>

    public const string GitHubRepository = "ICITPROJ/Fortiva";



    public const string ManifestFileName = "latest.personal.json";



    public static string PersonalLatest =>

        $"https://github.com/{GitHubRepository}/releases/latest/download/{ManifestFileName}";



    public static string ReleaseAssetUrl(string version, string fileName)

        => $"https://github.com/{GitHubRepository}/releases/download/v{version}/{fileName}";



    /// <summary>Override manifest URL (HTTPS) or local JSON path for QA via environment variables.</summary>

    public static string ResolvePersonalLatest(string? defaultUrl = null)
    {
#if DEBUG
        var fileOverride = Environment.GetEnvironmentVariable("FORTIVA_UPDATE_MANIFEST_FILE");
        if (!string.IsNullOrWhiteSpace(fileOverride))
            return Path.GetFullPath(fileOverride.Trim());

        var urlOverride = Environment.GetEnvironmentVariable("FORTIVA_UPDATE_MANIFEST_URL");
        if (!string.IsNullOrWhiteSpace(urlOverride))
            return urlOverride.Trim();
#endif
        return defaultUrl ?? PersonalLatest;
    }



    public static bool IsLocalManifestPath(string path)

        => (path.Length >= 2 && path[1] == ':') || path.StartsWith(@"\\", StringComparison.Ordinal);

}



public static class AppVersion

{

    public static string Current =>

        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";



    public static bool IsRemoteNewer(string remote, string? local = null)

    {

        local ??= Current;

        return ParseVersion(remote) > ParseVersion(local);

    }



    public static Version ParseVersion(string value)

    {

        var core = value.Split('-', '+')[0].Trim();

        var parts = core.Split('.');

        var nums = new int[4];

        for (var i = 0; i < Math.Min(parts.Length, 4); i++)

            int.TryParse(parts[i], out nums[i]);

        return new Version(nums[0], nums[1], nums[2], nums[3]);

    }

}


