using System.Text.Json;

namespace Fortiva.Core.Updates;

public sealed class ReleaseManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<(ReleaseManifest? Manifest, bool FromNetwork)> TryLoadAsync(
        string manifestUrl,
        string? bundledManifestPath = null,
        CancellationToken cancellationToken = default)
    {
        var primary = ReleaseManifestUrls.ResolvePersonalLatest(manifestUrl);
        foreach (var candidate in CandidateManifestUrls(primary))
        {
            try
            {
                UpdateUrlPolicy.ValidateManifestUrl(candidate);
                var json = await SecureUpdateHttp.GetStringAsync(candidate, cancellationToken).ConfigureAwait(false);
                var manifest = Deserialize(json);
                if (manifest is not null && manifest.IsValid)
                    return (manifest, FromNetwork: true);
            }
            catch
            {
                /* try next URL */
            }
        }

        var bundled = TryLoadBundled(bundledManifestPath);
        return bundled is null ? (null, false) : (bundled, FromNetwork: false);
    }

    private static IEnumerable<string> CandidateManifestUrls(string primary)
    {
        yield return primary;
        if (!string.Equals(primary, ReleaseManifestUrls.PersonalLatestRaw, StringComparison.OrdinalIgnoreCase))
            yield return ReleaseManifestUrls.PersonalLatestRaw;
    }

    public ReleaseManifest? TryLoadBundled(string? bundledManifestPath)
    {
        if (string.IsNullOrWhiteSpace(bundledManifestPath) || !File.Exists(bundledManifestPath))
            return null;

        try
        {
            return Deserialize(File.ReadAllText(bundledManifestPath));
        }
        catch
        {
            return null;
        }
    }

    private static ReleaseManifest? Deserialize(string json)
        => JsonSerializer.Deserialize<ReleaseManifest>(json, JsonOptions);
}
