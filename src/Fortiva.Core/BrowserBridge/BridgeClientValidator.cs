using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Validates named-pipe clients by process name and executable path.</summary>
public static class BridgeClientValidator
{
    private static readonly HashSet<string> AllowedExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fortiva.BrowserBridge.Host.exe",
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

            if (fileName.Equals("Fortiva.BrowserBridge.Host.exe", StringComparison.OrdinalIgnoreCase))
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
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0)
                return false;

            using var proc = Process.GetProcessById((int)pid);
            return IsAllowedExecutablePath(proc.MainModule?.FileName, installRoots);
        }
        catch
        {
            return false;
        }
    }

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
}
