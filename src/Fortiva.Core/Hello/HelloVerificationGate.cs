namespace Fortiva.Core.Hello;

/// <summary>
/// Single-use gate tying DPAPI Hello bundle load to a recent UserConsentVerifier success.
/// Hardware-backed (v4) bundles use KeyCredential.RequestSignAsync instead.
/// </summary>
public static class HelloVerificationGate
{
    private static readonly object Gate = new();
    private static int _pendingVerifications;

    public static void MarkVerified()
    {
        lock (Gate)
            _pendingVerifications++;
    }

    public static void Reset()
    {
        lock (Gate)
            _pendingVerifications = 0;
    }

    /// <summary>Consumes one pending verification. Returns false when Hello UI was not completed.</summary>
    public static bool TryConsumeVerification()
    {
        lock (Gate)
        {
            if (_pendingVerifications <= 0)
                return false;
            _pendingVerifications--;
            return true;
        }
    }
}
