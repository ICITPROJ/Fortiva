using System.Diagnostics;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

/// <summary>
/// Ensures bridge pipe tests do not compete with a running Fortiva desktop instance.
/// </summary>
public static class BridgeTestEnvironment
{
    private static readonly string[] FortivaProcessNames =
    [
        "Fortiva.Personal",
        "Fortiva.Enterprise",
        "Fortiva.AppHost",
        "Fortiva.BrowserBridge.Host"
    ];

    public static void PrepareExclusivePipes()
    {
        StopFortivaProcesses();
        BridgeHostProcessCleanup.StopAllHosts();
        BridgeSessionRegistry.ClearActiveSessionId(false);
        BridgePipeNaming.RotateSessionId(false);
        Thread.Sleep(400);
        if (BridgeProcessCheck.IsFortivaRunning())
        {
            throw new InvalidOperationException(
                "Bridge pipe tests require Fortiva to be stopped. Close Fortiva and retry.");
        }
    }

    /// <summary>Waits for in-process bridge listeners to release pipe names (after VaultSession tests).</summary>
    public static void EnsurePipeNamesAvailable()
    {
        StopFortivaProcesses();
        BridgeHostProcessCleanup.StopAllHosts();
        BridgeSessionRegistry.ClearActiveSessionId(false);
        BridgePipeNaming.SetInProcessSessionId(null);
        BridgeSessionAuth.ClearSessionToken();
        BridgeSessionAuth.ConfigureTokenDirectory(null!);

        for (var i = 0; i < 150; i++)
        {
            if (!BridgeHealthCheck.AreListenersActive())
            {
                Thread.Sleep(400);
                if (!BridgeHealthCheck.AreListenersActive())
                    return;
            }

            Thread.Sleep(100);
        }

        Thread.Sleep(1500);
    }

    public static void StopFortivaProcesses()
    {
        foreach (var name in FortivaProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                    /* best effort */
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        Thread.Sleep(200);
    }
}

public sealed class BrowserBridgeTestHost : IDisposable
{
    public BrowserBridgeTestHost() => BridgeTestEnvironment.StopFortivaProcesses();

    public void Dispose()
    {
        BridgeSessionAuth.ClearSessionToken();
        BridgeSessionAuth.ConfigureTokenDirectory(null!);
        BridgeHostProcessCleanup.StopAllHosts();
        Thread.Sleep(500);
    }
}
