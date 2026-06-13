using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Shared named-pipe accept loop used by bridge brokers.</summary>
internal static class BridgePipeListener
{
    /// <summary>Test-only bypass when FORTIVA_BRIDGE_DISABLE_PIPE_VALIDATION=1 and FORTIVA_BRIDGE_FAST_TEST=1.</summary>
    internal static bool IsPipeClientValidationEnabled
    {
        get
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_DISABLE_PIPE_VALIDATION"),
                    "1",
                    StringComparison.Ordinal))
            {
                return true;
            }

#if DEBUG
            return false;
#else
            return string.Equals(
                Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST"),
                "1",
                StringComparison.Ordinal);
#endif
        }
    }
    public static Task[] Start(
        string pipeName,
        int listenerCount,
        int maxInstances,
        Func<NamedPipeServerStream, CancellationToken, Task> handleClient,
        CancellationToken ct,
        bool validateClients = true)
    {
        return Enumerable.Range(0, listenerCount)
            .Select(_ => Task.Run(
                async () => await ListenAsync(pipeName, maxInstances, handleClient, ct, validateClients).ConfigureAwait(false),
                ct))
            .ToArray();
    }

    public static async Task WaitAllAsync(Task[] tasks, TimeSpan timeout)
    {
        foreach (var task in tasks)
        {
            try { await task.WaitAsync(timeout); }
            catch { /* best effort shutdown */ }
        }
    }

    /// <summary>
    /// Cancels listener tasks without blocking the caller. Used during vault lock on the UI thread.
    /// </summary>
    public static void ShutdownListeners(CancellationTokenSource cts, Task[] tasks)
        => ShutdownListeners(cts, tasks, waitForExit: false);

    /// <summary>
    /// Stops named-pipe listeners. When <paramref name="waitForExit"/> is true, blocks until tasks end
    /// so a new listener can bind the same pipe name (restart/unlock path).
    /// </summary>
    public static void ShutdownListeners(CancellationTokenSource cts, Task[] tasks, bool waitForExit)
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
        try { cts.Dispose(); } catch { /* best effort */ }
        if (tasks.Length == 0)
            return;

        if (waitForExit)
        {
            var timeout = string.Equals(
                Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST"),
                "1",
                StringComparison.Ordinal)
                ? TimeSpan.FromMilliseconds(2500)
                : TimeSpan.FromMilliseconds(500);
            try { WaitAllAsync(tasks, timeout).GetAwaiter().GetResult(); }
            catch { /* best effort */ }
            return;
        }

        _ = Task.Run(async () =>
        {
            try { await WaitAllAsync(tasks, TimeSpan.FromMilliseconds(800)); }
            catch { /* best effort */ }
        });
    }

    private static async Task ListenAsync(
        string pipeName,
        int maxInstances,
        Func<NamedPipeServerStream, CancellationToken, Task> handleClient,
        CancellationToken ct,
        bool validateClients)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateSecuredServerStream(pipeName, maxInstances);
                await server.WaitForConnectionAsync(ct);
                if (validateClients && IsPipeClientValidationEnabled && !BridgePipeGuard.IsAllowedClient(server))
                {
                    try { server.Disconnect(); } catch { /* release instance */ }
                    server.Dispose();
                    continue;
                }

                var connected = server;
                server = null;
                _ = Task.Run(async () =>
                {
                    try { await handleClient(connected, CancellationToken.None).ConfigureAwait(false); }
                    catch { /* client disconnect */ }
                    finally
                    {
                        try { connected.Dispose(); } catch { /* best effort */ }
                    }
                });
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                server?.Dispose();
                try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    internal static NamedPipeServerStream CreateSecuredServerStream(string pipeName, int maxInstances)
    {
        var pipeSecurity = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve current user SID for pipe ACL.");
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }
}
