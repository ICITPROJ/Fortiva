using System.IO.Pipes;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Validates named-pipe clients before serving bridge IPC.</summary>
public static class BridgePipeGuard
{
    public static bool IsAllowedClient(NamedPipeServerStream pipe) =>
        BridgeClientValidator.IsAllowedBridgeHostClient(pipe);
}
