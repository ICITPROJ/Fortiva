using System.Diagnostics;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Stops orphaned native-messaging host processes that can exhaust pipe instances.</summary>
public static class BridgeHostProcessCleanup
{
    /// <summary>Stops every bridge-host process (e.g. before bridge restart).</summary>
    public static void StopAllHosts()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("Fortiva.BrowserBridge.Host"))
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch { /* best effort */ }
                finally { proc.Dispose(); }
            }
        }
        catch { /* best effort */ }
    }

    public static void StopOrphanedHosts()
    {
        var now = DateTime.UtcNow;
        Process[] procs;
        try { procs = Process.GetProcessesByName("Fortiva.BrowserBridge.Host"); }
        catch { return; }

        var survivors = new List<(Process Proc, TimeSpan Age)>();
        foreach (var proc in procs)
        {
            try
            {
                if (proc.HasExited)
                    continue;

                var age = now - proc.StartTime.ToUniversalTime();
                survivors.Add((proc, age));
            }
            catch
            {
                proc.Dispose();
            }
        }

        if (survivors.Count <= 1)
        {
            foreach (var (proc, _) in survivors)
                proc.Dispose();
            return;
        }

        var keep = survivors
            .OrderByDescending(s => s.Proc.StartTime)
            .First()
            .Proc;

        foreach (var (proc, _) in survivors)
        {
            try
            {
                if (proc.Id != keep.Id)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* best effort */ }
            finally { proc.Dispose(); }
        }

        keep.Dispose();
    }
}
