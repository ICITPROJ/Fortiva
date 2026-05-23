using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Validates named-pipe clients before serving credentials.</summary>
public static class BridgePipeGuard
{
    private static readonly HashSet<string> AllowedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fortiva.BrowserBridge.Host",
        "Fortiva.Personal",
        "Fortiva.Enterprise"
    };

    public static bool IsAllowedClient(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0)
                return false;

            using var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName;
            return AllowedProcessNames.Contains(name);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint clientProcessId);
}
