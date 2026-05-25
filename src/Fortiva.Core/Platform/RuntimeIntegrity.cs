using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fortiva.Core.Platform;

/// <summary>Detects attached debuggers before sensitive operations (best effort, not kernel-proof).</summary>
public static class RuntimeIntegrity
{
    public static bool IsDebuggerAttached()
    {
        if (Debugger.IsAttached)
            return true;

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (IsDebuggerPresent())
                return true;

            var current = Process.GetCurrentProcess();
            if (CheckRemoteDebuggerPresent(current.Handle, out var remote) && remote)
                return true;
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    public static void EnsureSafeForSensitiveOperation()
    {
        if (IsDebuggerAttached())
            throw new InvalidOperationException("Fortiva refused a sensitive operation because a debugger is attached.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool isDebuggerPresent);
}
