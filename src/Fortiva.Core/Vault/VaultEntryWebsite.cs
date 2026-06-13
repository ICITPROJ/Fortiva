using System.Text.RegularExpressions;

namespace Fortiva.Core.Vault;

/// <summary>Derives a website URL from vault entry fields for autofill matching and display.</summary>
public static class VaultEntryWebsite
{
    private static readonly Regex UrlInTextRegex = new(
        @"https?://[^\s\)>""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HostInTextRegex = new(
        @"\b([a-z0-9](?:[a-z0-9\-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?)+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Persists a website URL from title/notes when the URL field is empty.</summary>
    public static void NormalizeWebsite(VaultEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Url))
            return;

        var effective = GetEffectiveUrl(entry);
        if (effective is not null)
            entry.Url = effective;
    }

    /// <summary>Best URL for domain matching — uses URL field, then title, then notes.</summary>
    public static string? GetEffectiveUrl(VaultEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Url))
            return entry.Url.Trim();

        foreach (var text in new[] { entry.Title, entry.Notes })
        {
            var parsed = TryParseWebsiteFromText(text);
            if (parsed is not null)
                return parsed;
        }

        return null;
    }

    private static string? TryParseWebsiteFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        var urlMatch = UrlInTextRegex.Match(text);
        if (urlMatch.Success)
            return CleanUrlToken(urlMatch.Value);

        if (!text.Contains(' ') || text.Contains("://", StringComparison.Ordinal))
        {
            var candidate = text.Contains("://", StringComparison.Ordinal)
                ? text
                : "https://" + text.TrimStart('/');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && LooksLikeHost(uri.Host))
                return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        foreach (Match match in HostInTextRegex.Matches(text))
        {
            var host = match.Groups[1].Value;
            if (!LooksLikeHost(host))
                continue;
            return "https://" + host;
        }

        return null;
    }

    private static string CleanUrlToken(string token)
    {
        token = token.TrimEnd('.', ',', ';', ')', ']', '"', '\'');
        if (!Uri.TryCreate(token, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return token;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool LooksLikeHost(string host)
        => host.Contains('.') && host.Any(char.IsLetter);
}
