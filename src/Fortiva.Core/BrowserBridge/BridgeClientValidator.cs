using System.Diagnostics;

using System.IO.Pipes;

using System.Runtime.InteropServices;

using System.Text;

using Fortiva.Core.Platform;



namespace Fortiva.Core.BrowserBridge;



/// <summary>Validates named-pipe clients by process name, executable path, and browser parent chain.</summary>

public static class BridgeClientValidator

{

    public const string BridgeHostExecutableName = "Fortiva.BrowserBridge.Host.exe";



    private static readonly HashSet<string> AllowedExecutableNames = new(StringComparer.OrdinalIgnoreCase)

    {

        BridgeHostExecutableName,

        "Fortiva.Personal.exe",

        "Fortiva.Enterprise.exe"

    };



    private static readonly object Gate = new();

    private static string[] _allowedInstallRoots = [];



    public static void ConfigureAllowedInstallRoots(params string[] roots)

    {

        lock (Gate)

        {

            _allowedInstallRoots = roots

                .Where(r => !string.IsNullOrWhiteSpace(r))

                .Select(r => Path.GetFullPath(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToArray();

        }

    }



    public static bool IsAllowedExecutableName(string fileName) =>

        AllowedExecutableNames.Contains(fileName);



    public static bool IsAllowedBridgeHostClient(NamedPipeServerStream pipe, IReadOnlyList<string>? installRoots = null)

    {

        try

        {

            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0)

                return false;



            using var proc = Process.GetProcessById((int)pid);

            if (!string.Equals(proc.ProcessName, "Fortiva.BrowserBridge.Host", StringComparison.OrdinalIgnoreCase))

                return false;



            var imagePath = TryGetProcessImagePath(proc);

            if (!string.IsNullOrWhiteSpace(imagePath))

            {

                if (IsAllowedBridgeHostPath(imagePath, installRoots))

                    return true;

                var inferredRoot = TryInferInstallRootFromBridgeHostPath(imagePath);
                if (inferredRoot is not null && IsAllowedBridgeHostPath(imagePath, [inferredRoot]))
                    return true;

                // Path resolved but outside install root — reject even on Personal.

                return false;

            }

            // Image path is required — browser-parent heuristics are too weak for token/credential pipes.

            return false;

        }

        catch

        {

            return false;

        }

    }



    internal static string? TryGetProcessImagePath(Process process)

    {

        try

        {

            var path = process.MainModule?.FileName;

            if (!string.IsNullOrWhiteSpace(path))

                return path;

        }

        catch { /* fall through */ }



        try

        {

            const int maxChars = 1024;

            var buffer = new StringBuilder(maxChars);

            var size = maxChars;

            if (QueryFullProcessImageName(process.SafeHandle, 0, buffer, ref size))

                return buffer.ToString();

        }

        catch { /* best effort */ }



        return null;

    }



    /// <summary>True when the directory contains a shipped Fortiva entry executable.</summary>
    public static bool IsTrustedInstallRoot(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return false;

        try
        {
            installRoot = Path.GetFullPath(installRoot);
            return File.Exists(Path.Combine(installRoot, "Fortiva.Personal.exe"))
                || File.Exists(Path.Combine(installRoot, "Fortiva.Enterprise.exe"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Derives install root from a standard bridge host path ({root}/BrowserBridge/Fortiva.BrowserBridge.Host.exe).
    /// </summary>
    public static string? TryInferInstallRootFromBridgeHostPath(string? bridgeHostPath)
    {
        if (string.IsNullOrWhiteSpace(bridgeHostPath))
            return null;

        try
        {
            bridgeHostPath = Path.GetFullPath(bridgeHostPath);
            if (!string.Equals(Path.GetFileName(bridgeHostPath), BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))
                return null;

            var bridgeDir = Path.GetDirectoryName(bridgeHostPath);
            if (string.IsNullOrEmpty(bridgeDir))
                return null;

            if (!string.Equals(Path.GetFileName(bridgeDir), "BrowserBridge", StringComparison.OrdinalIgnoreCase))
                return null;

            var installRoot = Path.GetDirectoryName(bridgeDir);
            return string.IsNullOrEmpty(installRoot) ? null : installRoot;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Install path policy without SHA-256 sidecar (used for trust-on-first-use recording).</summary>
    internal static bool PassesBridgeHostInstallPath(string fullPath, string root)
    {
        try { fullPath = Path.GetFullPath(fullPath); }
        catch { return false; }

        if (!string.Equals(Path.GetFileName(fullPath), BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))
            return false;

        root = Path.GetFullPath(root);
        if (!IsUnderDirectory(fullPath, root) || !IsTrustedInstallRoot(root))
            return false;

        if (AuthenticodePolicy.RequireSignedExecutables && !AuthenticodeVerifier.IsSigned(fullPath))
            return false;

        var bridgeDir = Path.Combine(root, "BrowserBridge");
        return IsUnderDirectory(fullPath, bridgeDir);
    }

    public static bool IsAllowedBridgeHostPath(string? fullPath, IReadOnlyList<string>? installRoots = null)

    {

        if (string.IsNullOrWhiteSpace(fullPath))

            return false;



        try { fullPath = Path.GetFullPath(fullPath); }

        catch { return false; }



        if (!string.Equals(Path.GetFileName(fullPath), BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))

            return false;



        var roots = ResolveInstallRoots(installRoots);

        if (roots.Count == 0)

            return false;



        foreach (var root in roots)

        {

            if (!PassesBridgeHostInstallPath(fullPath, root))

                continue;

            if (!BridgeInstallIntegrity.VerifyBridgeHostHash(fullPath, [root]))
                continue;

            return true;

        }



        return false;

    }



    public static bool IsAllowedExecutablePath(string? fullPath, IReadOnlyList<string>? installRoots = null)

    {

        if (string.IsNullOrWhiteSpace(fullPath))

            return false;



        try { fullPath = Path.GetFullPath(fullPath); }

        catch { return false; }



        var fileName = Path.GetFileName(fullPath);

        if (!IsAllowedExecutableName(fileName))

            return false;



        var roots = ResolveInstallRoots(installRoots);

        if (roots.Count == 0)

            return false;



        foreach (var root in roots)

        {

            if (!IsUnderDirectory(fullPath, root) || !IsTrustedInstallRoot(root))

                continue;



            if (AuthenticodePolicy.RequireSignedExecutables && !AuthenticodeVerifier.IsSigned(fullPath))

                return false;



            if (fileName.Equals(BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))
                return IsAllowedBridgeHostPath(fullPath, installRoots);

            return true;

        }



        return false;

    }



    public static bool IsAllowedClient(NamedPipeServerStream pipe, IReadOnlyList<string>? installRoots = null)

        => IsAllowedBridgeHostClient(pipe, installRoots);



    internal static bool IsUnderDirectory(string filePath, string directoryPath)

    {

        try

        {

            var fullFile = Path.GetFullPath(filePath);

            var fullDir = Path.GetFullPath(directoryPath);

            if (!fullDir.EndsWith(Path.DirectorySeparatorChar))

                fullDir += Path.DirectorySeparatorChar;

            return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);

        }

        catch { return false; }

    }



    private static IReadOnlyList<string> ResolveInstallRoots(IReadOnlyList<string>? installRoots)

    {

        if (installRoots is { Count: > 0 })

            return installRoots;



        lock (Gate)

            return _allowedInstallRoots;

    }



    [DllImport("kernel32.dll", SetLastError = true)]

    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint clientProcessId);



    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]

    private static extern bool QueryFullProcessImageName(SafeHandle hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

}


