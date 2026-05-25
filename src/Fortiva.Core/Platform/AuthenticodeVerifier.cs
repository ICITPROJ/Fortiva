using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Fortiva.Core.Platform;

/// <summary>Validates Authenticode signatures on Windows executables.</summary>
public static class AuthenticodeVerifier
{
    /// <summary>
    /// Returns true when the file has an Authenticode signature, or when verification is skipped
    /// (non-Windows, debug builds, or FORTIVA_ALLOW_UNSIGNED_BRIDGE=1 for local development).
    /// </summary>
    public static bool IsSigned(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        if (string.Equals(Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE"), "1", StringComparison.Ordinal))
            return true;

#if DEBUG
        return true;
#else
        try
        {
            using var cert = X509Certificate2.CreateFromSignedFile(filePath);
            return cert is not null;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
#endif
    }
}
