namespace Fortiva.Core.Hello;

/// <summary>
/// Single-use gate tying DPAPI Hello bundle load to a recent UserConsentVerifier success.
/// Hardware-backed (v4) bundles use KeyCredential.RequestSignAsync instead.
/// </summary>
public static class HelloVerificationGate
{
    private static int _verified;

    public static void MarkVerified() => Interlocked.Exchange(ref _verified, 1);

    public static void Reset() => Interlocked.Exchange(ref _verified, 0);

    /// <summary>Consumes the pending verification flag. Returns false when Hello UI was not completed.</summary>
    public static bool TryConsumeVerification()
        => Interlocked.CompareExchange(ref _verified, 0, 1) == 1;
}
