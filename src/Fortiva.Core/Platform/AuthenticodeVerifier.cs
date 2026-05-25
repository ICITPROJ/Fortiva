using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Fortiva.Core.Platform;

/// <summary>Validates Authenticode signatures on Windows executables.</summary>
public static class AuthenticodeVerifier
{
    /// <summary>
    /// Returns true when the file has a verifiable Authenticode signature chain,
    /// or when verification is skipped (non-Windows or DEBUG builds).
    /// </summary>
    public static bool IsSigned(string filePath)
    {
        if (!AuthenticodePolicy.RequireSignedExecutables)
            return true;

        if (!OperatingSystem.IsWindows())
            return true;

#if DEBUG
        if (AllowUnsignedBridgeForDevelopment())
            return true;
        return VerifySignedFile(filePath);
#else
        if (AllowUnsignedBridgeForDevelopment())
            return true;

        return VerifySignedFile(filePath);
#endif
    }

    /// <summary>
    /// DEBUG or GitHub Actions test runs with FORTIVA_ALLOW_UNSIGNED_BRIDGE=1.
    /// Never enabled for shipped Release builds on end-user machines.
    /// </summary>
    internal static bool AllowUnsignedBridgeForDevelopment()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE"),
                "1",
                StringComparison.Ordinal))
            return false;

#if DEBUG
        return true;
#else
        return string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
#endif
    }

    private static bool VerifySignedFile(string filePath)
    {
        try
        {
            var raw = X509Certificate.CreateFromSignedFile(filePath);
            if (raw is null)
                return false;

            using var cert = raw as X509Certificate2 ?? new X509Certificate2(raw);
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = DateTime.UtcNow;
            if (!chain.Build(cert))
                return false;

#if !DEBUG
            if (!PublisherMatchesExpected(cert))
                return false;
#endif
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool PublisherMatchesExpected(X509Certificate2 cert)
    {
        var subject = cert.Subject ?? "";
        return subject.Contains("icmclab", StringComparison.OrdinalIgnoreCase);
    }
}
