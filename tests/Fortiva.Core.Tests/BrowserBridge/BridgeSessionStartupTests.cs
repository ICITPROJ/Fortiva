using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

/// <summary>
/// Mirrors locked-at-launch: unlock broker must bind before ReconcileBridgeLifecycleAsync runs.
/// </summary>
[Collection("BrowserBridgeSerial")]
public class BridgeSessionStartupTests
{
    [Fact]
    public async Task ColdStartLockedVault_UnlockBrokerBindsPlaceholderSession()
    {
        BridgeSessionRegistry.ClearActiveSessionId(enterprise: false);
        try
        {
            Assert.False(BridgePipeNaming.HasActiveSession(false));

            if (!BridgePipeNaming.HasActiveSession(false))
                BridgePipeNaming.RotateSessionId(false);

            Assert.True(BridgePipeNaming.HasActiveSession(false));
            var pipeName = BridgePipeNaming.UnlockPipeName(false);
            Assert.False(string.IsNullOrWhiteSpace(pipeName));

            using var broker = new BridgeUnlockBroker(
                () => BridgePresenceSnapshot.NoSession(vaultExists: true),
                _ => Task.FromResult(false),
                enterprise: false);

            broker.Start();

            using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(connectCts.Token);
            Assert.True(client.IsConnected);
        }
        finally
        {
            BridgeSessionRegistry.ClearActiveSessionId(enterprise: false);
        }
    }
}
