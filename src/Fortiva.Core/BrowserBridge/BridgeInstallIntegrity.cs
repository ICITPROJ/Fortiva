using System.Security.Cryptography;
using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// SHA-256 pin for the bridge host when Authenticode is off (Personal unsigned builds).
/// Detects same-user EXE swap under the install root.
/// </summary>
public static class BridgeInstallIntegrity
{
    public const string SidecarFileName = "bridge-host.sha256";

    public static string GetSidecarPath(string installRoot)
    {
        installRoot = Path.GetFullPath(installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(installRoot, "BrowserBridge", SidecarFileName);
    }

    public static void RecordBridgeHostHash(string bridgeExecutablePath)
    {
        if (!File.Exists(bridgeExecutablePath))
            return;

        var installRoot = BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(bridgeExecutablePath);
        if (installRoot is null)
            return;

        var sidecar = GetSidecarPath(installRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        var hash = ComputeFileSha256Hex(bridgeExecutablePath);
        File.WriteAllText(sidecar, hash);
    }

    /// <summary>
    /// When Authenticode is required, signing is the trust root. Otherwise require a matching sidecar hash.
    /// Missing sidecar fails closed — install/repair records the hash via <see cref="RecordBridgeHostHash"/>.
    /// </summary>
    public static bool VerifyBridgeHostHash(string bridgeExecutablePath, IReadOnlyList<string>? installRoots = null)
    {
        if (AuthenticodePolicy.RequireSignedExecutables)
            return true;

        if (string.IsNullOrWhiteSpace(bridgeExecutablePath) || !File.Exists(bridgeExecutablePath))
            return false;

        var roots = ResolveRoots(bridgeExecutablePath, installRoots);
        foreach (var root in roots)
        {
            if (!BridgeClientValidator.PassesBridgeHostInstallPath(bridgeExecutablePath, root))
                continue;

            var sidecar = GetSidecarPath(root);
            if (!File.Exists(sidecar))
                continue;

            var expected = File.ReadAllText(sidecar).Trim();
            var actual = ComputeFileSha256Hex(bridgeExecutablePath);
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<string> ResolveRoots(string bridgeExecutablePath, IReadOnlyList<string>? installRoots)
    {
        if (installRoots is { Count: > 0 })
            return installRoots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var inferred = BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(bridgeExecutablePath);
        return inferred is null ? [] : [inferred];
    }

    private static string ComputeFileSha256Hex(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
