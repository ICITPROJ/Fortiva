using System.Diagnostics;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Lightweight Fortiva desktop process presence (no pipe I/O).</summary>
public static class BridgeProcessCheck
{
    public static bool IsFortivaRunning()
    {
        try
        {
            return Process.GetProcessesByName("Fortiva.Personal").Length > 0
                || Process.GetProcessesByName("Fortiva.Enterprise").Length > 0
                || Process.GetProcessesByName("Fortiva.AppHost").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
