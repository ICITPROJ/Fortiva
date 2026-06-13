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
        _ = edition; // reserved for future per-edition defaults when Enterprise signing ships
        var allowUnsigned = string.Equals(
            Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE"),
            "1",
            StringComparison.Ordinal);
        var requireCodesign = string.Equals(
            Environment.GetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN"),
            "1",
            StringComparison.Ordinal);
#if DEBUG
        RequireSignedExecutables = false;
#else
        // Authenticode is opt-in only (FORTIVA_REQUIRE_CODESIGN=1) until an Enterprise customer
        // engages and signing is provisioned (Azure Trusted Signing or traditional .pfx).
        // Personal and Enterprise dev/CI builds use FORTIVA_ALLOW_UNSIGNED_BRIDGE=1 (Deploy script).
        RequireSignedExecutables = requireCodesign && !allowUnsigned;
#endif
    }
}
