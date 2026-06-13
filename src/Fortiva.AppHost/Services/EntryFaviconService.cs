using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

/// <summary>Best-effort site favicon cache (optional network; falls back to colored initials).</summary>
public static class EntryFaviconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly string CacheDir = Path.Combine(FortivaPaths.PersonalDataRoot, "favicons");

    public static string? TryGetCachedPath(string? url)
    {
        var host = TryGetHost(url);
        if (host is null)
            return null;

        var path = CacheFilePath(host);
        return File.Exists(path) ? path : null;
    }

    public static async Task PrefetchAsync(
        IEnumerable<string?> urls,
        Action<string, string?>? onHostLoaded = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        var hosts = urls
            .Select(TryGetHost)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var host in hosts)
        {
            if (ct.IsCancellationRequested)
                break;

            var cached = CacheFilePath(host);
            if (File.Exists(cached))
            {
                onHostLoaded?.Invoke(host, cached);
                continue;
            }

            try
            {
                var uri = new Uri($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64");
                using var response = await Http.GetAsync(uri, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 32)
                    continue;

                await File.WriteAllBytesAsync(cached, bytes, ct).ConfigureAwait(false);
                onHostLoaded?.Invoke(host, cached);
            }
            catch
            {
                /* offline or blocked — initials remain */
            }
        }
    }

    private static string CacheFilePath(string host)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host.ToLowerInvariant()))).ToLowerInvariant();
        return Path.Combine(CacheDir, hash[..16] + ".png");
    }

    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            var host = new Uri(url.Trim()).Host;
            return string.IsNullOrWhiteSpace(host) ? null : host.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
