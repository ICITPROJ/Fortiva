namespace Fortiva.Core.Platform;

/// <summary>
/// Controls whether Release builds require Authenticode-signed executables
/// (updates, browser bridge). Personal ships unsigned for now; see docs/CODESIGNING.md.
/// </summary>
public static class AuthenticodePolicy
{
    /// <summary>
    /// When false, <see cref="AuthenticodeVerifier.IsSigned"/> treats all files as signed.
    /// Set at app startup from edition / deployment policy.
    /// </summary>
    public static bool RequireSignedExecutables { get; set; }

    public static void ConfigureForEdition(string edition)
    {
        // Personal: unsigned GitHub Releases (SHA-256 manifest check still applies).
        // Enterprise: opt in later with FORTIVA_REQUIRE_CODESIGN=1 when IT signing is configured.
        RequireSignedExecutables =
            string.Equals(edition, "Enterprise", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Environment.GetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN"),
                "1",
                StringComparison.Ordinal);
    }
}
