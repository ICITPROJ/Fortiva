using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Validates named-pipe clients by process name and executable path.</summary>
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
                return IsAllowedBridgeHostPath(imagePath, installRoots);

            // Edge/Chrome-spawned native hosts can block MainModule; Personal unsigned builds
            // still require the expected process name above.
            return !AuthenticodePolicy.RequireSignedExecutables;
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
        catch
        {
            /* fall through */
        }

        try
        {
            const int maxChars = 1024;
            var buffer = new StringBuilder(maxChars);
            var size = maxChars;
            if (QueryFullProcessImageName(process.SafeHandle, 0, buffer, ref size))
                return buffer.ToString();
        }
        catch
        {
            /* best effort */
        }

        return null;
    }

    public static bool IsAllowedBridgeHostPath(string? fullPath, IReadOnlyList<string>? installRoots = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        try
        {
            fullPath = Path.GetFullPath(fullPath);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(fullPath), BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))
            return false;

        var roots = ResolveInstallRoots(installRoots);
        if (roots.Count == 0)
            return false;

        foreach (var root in roots)
        {
            if (!IsUnderDirectory(fullPath, root))
                continue;

            if (!AuthenticodeVerifier.IsSigned(fullPath))
                return false;

            var bridgeDir = Path.Combine(root, "BrowserBridge");
            return IsUnderDirectory(fullPath, bridgeDir) || IsUnderDirectory(fullPath, root);
        }

        return false;
    }

    public static bool IsAllowedExecutablePath(string? fullPath, IReadOnlyList<string>? installRoots = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        try
        {
            fullPath = Path.GetFullPath(fullPath);
        }
        catch
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!IsAllowedExecutableName(fileName))
            return false;

        var roots = ResolveInstallRoots(installRoots);
        if (roots.Count == 0)
            return false;

        foreach (var root in roots)
        {
            if (!IsUnderDirectory(fullPath, root))
                continue;

            if (!AuthenticodeVerifier.IsSigned(fullPath))
                return false;

            if (fileName.Equals(BridgeHostExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                var bridgeDir = Path.Combine(root, "BrowserBridge");
                if (IsUnderDirectory(fullPath, bridgeDir) || IsUnderDirectory(fullPath, root))
                    return true;
                continue;
            }

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
        catch
        {
            return false;
        }
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
