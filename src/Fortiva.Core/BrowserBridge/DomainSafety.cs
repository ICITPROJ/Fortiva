using System.Globalization;
using System.Net;

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



    /// <summary>
    /// True when the host uses punycode ACE labels (xn--), which are rejected for autofill.
    /// Also used by homograph checks alongside mixed-script detection.
    /// </summary>
    public static bool ContainsAceEncodedLabel(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        foreach (var label in host.Trim().TrimEnd('.').Split('.'))
        {
            if (label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ContainsSuspiciousCharacters(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (ContainsAceEncodedLabel(host))
            return true;

        host = NormalizeHost(host);
        if (HasMixedScriptHomograph(host))
            return true;

        return HasUntrustedScriptHost(host);
    }



    public static bool LooksLikeHomograph(string host)

        => HasMixedScriptHomograph(NormalizeHost(host));



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

    /// <summary>
    /// True when hosts are equal or share the same registrable domain (e.g. login.ionos.co.uk and www.ionos.co.uk).
    /// IP hosts require an exact match.
    /// </summary>
    public static bool HostsMatchForAutofill(string entryHost, string requestHost)
    {
        entryHost = NormalizeHost(entryHost);
        requestHost = NormalizeHost(requestHost);
        if (string.IsNullOrEmpty(entryHost) || string.IsNullOrEmpty(requestHost))
            return false;

        if (string.Equals(entryHost, requestHost, StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsIpHost(entryHost) || IsIpHost(requestHost))
            return false;

        return ShareRegistrableDomain(entryHost, requestHost);
    }

    /// <summary>
    /// Stricter match for password release — exact host only (after IDN normalization).
    /// Listing may still use <see cref="HostsMatchForAutofill"/>.
    /// </summary>
    public static bool HostsMatchForCredentialRelease(string entryHost, string requestHost)
    {
        entryHost = NormalizeHost(entryHost);
        requestHost = NormalizeHost(requestHost);
        return !string.IsNullOrEmpty(entryHost)
            && !string.IsNullOrEmpty(requestHost)
            && string.Equals(entryHost, requestHost, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShareRegistrableDomain(string hostA, string hostB)
    {
        var regA = GetRegistrableDomain(hostA);
        var regB = GetRegistrableDomain(hostB);
        return regA.Length > 0
            && regB.Length > 0
            && string.Equals(regA, regB, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRegistrableDomain(string host)
    {
        host = NormalizeHost(host);
        if (string.IsNullOrEmpty(host))
            return "";

        if (IsIpHost(host))
            return host;

        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return host;

        var lastTwo = $"{parts[^2]}.{parts[^1]}";
        if (parts.Length >= 3 && MultiPartTlds.Contains(lastTwo))
            return $"{parts[^3]}.{lastTwo}";

        return lastTwo;
    }

    private static bool IsIpHost(string host)
        => IPAddress.TryParse(host, out _);

    private static readonly HashSet<string> MultiPartTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk",
        "com.au", "net.au", "org.au",
        "co.nz", "co.jp", "co.kr", "com.br", "com.mx",
        "co.za", "com.tr", "com.sg", "com.hk", "co.in"
    };

    private static bool HasMixedScriptHomograph(string host)

    {

        foreach (var label in host.Split('.'))

        {

            if (string.IsNullOrEmpty(label))

                continue;



            var hasLatin = false;

            var hasCyrillic = false;

            var hasGreek = false;



            foreach (var ch in label)

            {

                if (ch is >= 'a' and <= 'z')

                {

                    hasLatin = true;

                    continue;

                }



                if (ch is >= '0' and <= '9' or '-')

                    continue;



                var code = (int)ch;

                if (code is >= 0x0400 and <= 0x04FF)

                    hasCyrillic = true;

                else if (code is >= 0x0370 and <= 0x03FF)

                    hasGreek = true;

            }



            if ((hasLatin && hasCyrillic) || (hasLatin && hasGreek))

                return true;

        }



        return false;

    }

    /// <summary>Flags hostnames with no ASCII letters (e.g. pure Cyrillic punycode decoded labels).</summary>
    private static bool HasUntrustedScriptHost(string host)
    {
        if (IsIpHost(host))
            return false;

        var hasAsciiLetter = false;
        var hasNonAsciiLetter = false;

        foreach (var label in host.Split('.'))
        {
            if (string.IsNullOrEmpty(label))
                continue;

            foreach (var ch in label)
            {
                if (ch is >= 'a' and <= 'z')
                {
                    hasAsciiLetter = true;
                    continue;
                }

                if (ch is >= '0' and <= '9' or '-')
                    continue;

                if (ch > 127 && char.IsLetter(ch))
                    hasNonAsciiLetter = true;
            }
        }

        return hasNonAsciiLetter && !hasAsciiLetter;
    }

}


