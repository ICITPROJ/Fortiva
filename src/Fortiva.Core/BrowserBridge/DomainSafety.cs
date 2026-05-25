using System.Globalization;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Host normalization and homograph detection for bridge autofill.</summary>
public static class DomainSafety
{
    public static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        try
        {
            host = new IdnMapping().GetUnicode(host);
        }
        catch
        {
            /* keep ascii host */
        }

        return host;
    }

    public static bool ContainsSuspiciousCharacters(string host)
    {
        foreach (var ch in host)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.')
                continue;
            if (ch == ':')
                continue;
            return true;
        }

        return false;
    }

    public static bool LooksLikeHomograph(string host)
    {
        foreach (var ch in host)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.')
                continue;
            return true;
        }

        return false;
    }

    public static string DisplayHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";

        try
        {
            return new IdnMapping().GetAscii(host);
        }
        catch
        {
            return host;
        }
    }
}
