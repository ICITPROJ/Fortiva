using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Validates native-messaging host executable path and Authenticode at runtime.</summary>
public static class NativeHostIntegrity
{
    public static bool VerifyCurrentProcess(string? expectedInstallRoot = null)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            string[]? roots = null;
            if (!string.IsNullOrWhiteSpace(expectedInstallRoot))
                roots = [expectedInstallRoot];
            else
            {
                var inferred = BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(exe);
                if (inferred is not null)
                    roots = [inferred];
            }

            if (!BridgeClientValidator.IsAllowedBridgeHostPath(exe, roots))
                return false;

            if (roots is { Length: > 0 } && !BridgeClientValidator.IsTrustedInstallRoot(roots[0]))
                return false;

            if (AuthenticodePolicy.RequireSignedExecutables && !AuthenticodeVerifier.IsSigned(exe))
                return false;

            if (!BridgeInstallIntegrity.VerifyBridgeHostHash(exe, roots))
                return false;

            return roots is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }
}
