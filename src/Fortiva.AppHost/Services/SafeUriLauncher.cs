namespace Fortiva.AppHost.Services;

public static class SafeUriLauncher
{
    public static bool TryNormalizeHttpUri(string raw, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var candidate))
            return false;

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(candidate.Host))
            return false;

        uri = candidate;
        return true;
    }
}
