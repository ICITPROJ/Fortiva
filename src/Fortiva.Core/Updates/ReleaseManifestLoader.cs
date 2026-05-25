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
        var url = ReleaseManifestUrls.ResolvePersonalLatest(manifestUrl);
        try
        {
            UpdateUrlPolicy.ValidateManifestUrl(url);
            var json = await SecureUpdateHttp.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return (Deserialize(json), FromNetwork: true);
        }
        catch
        {
            var bundled = TryLoadBundled(bundledManifestPath);
            return bundled is null ? (null, false) : (bundled, FromNetwork: false);
        }
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
