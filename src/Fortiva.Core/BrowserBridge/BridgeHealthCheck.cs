using System.IO.Pipes;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Bridge pipe health: token GET round-trip plus credential pipe connect.</summary>
public static class BridgeHealthCheck
{
    public static bool IsTokenPipeListening(int timeoutMs = 1500)
        => TryConnect(BridgeTokenBroker.PipeName, timeoutMs);

    public static bool IsCredentialPipeListening(int timeoutMs = 1500)
        => TryConnect(BrowserBridgeServer.PipeName, timeoutMs);

    /// <summary>Token broker returns a session token (full GET round-trip, bounded wait).</summary>
    public static bool IsTokenPipeResponsive(int timeoutMs = 2000)
    {
        try
        {
            var task = BridgeSessionAuth.RequestTokenFromBrokerAsync(timeoutMs);
            if (!task.Wait(timeoutMs + 300))
                return false;
            return !string.IsNullOrWhiteSpace(task.Result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fast probe for in-process health checks. Credential pipe only — do not connect to the
    /// token pipe without sending GET (that exhausts broker instances and blocks the extension).
    /// </summary>
    public static bool IsHealthy(int timeoutMs = 1500)
        => IsCredentialPipeListening(timeoutMs);

    /// <summary>Both bridge pipes accept connections (connect-only — does not consume token broker instances).</summary>
    public static bool AreListenersActive(int timeoutMs = 500)
        => IsTokenPipeListening(timeoutMs) && IsCredentialPipeListening(timeoutMs);

    internal static int StartupHealthTimeoutMs =>
        string.Equals(Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST"), "1", StringComparison.Ordinal)
            ? 1200
            : 500;

    /// <summary>
    /// Full token GET plus credential connect — for external probes (native host only;
    /// Fortiva.Personal cannot pass token-broker client validation).
    /// </summary>
    public static bool IsFullyResponsive(int timeoutMs = 2000)
        => IsTokenPipeResponsive(timeoutMs) && IsCredentialPipeListening(timeoutMs);

    private static bool TryConnect(string pipeName, int timeoutMs)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(timeoutMs);
            return client.IsConnected;
        }
        catch
        {
            return false;
        }
    }
}
