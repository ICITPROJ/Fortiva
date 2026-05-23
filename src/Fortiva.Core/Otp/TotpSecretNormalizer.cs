namespace Fortiva.Core.Otp;

public static class TotpSecretNormalizer
{
    /// <summary>Accepts Base32 secrets or otpauth:// URIs from QR setup codes.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();
        if (raw.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                throw new FormatException("Invalid authenticator URI.");

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
                throw new FormatException("Authenticator URI is missing a secret.");
            raw = secret;
        }

        raw = raw.Replace(" ", "").Replace("-", "").TrimEnd('=');
        if (raw.Length == 0)
            return null;

        _ = Base32Encoding.Decode(raw);
        return raw.ToUpperInvariant();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(part[..idx]);
            var value = Uri.UnescapeDataString(part[(idx + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
