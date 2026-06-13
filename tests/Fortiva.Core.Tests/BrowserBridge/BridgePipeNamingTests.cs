using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgePipeNamingTests
{
    [Fact]
    public void RotateSessionId_WritesDistinctPipeNames()
    {
        try
        {
            BridgeSessionRegistry.ClearActiveSessionId(false);
            var id = BridgePipeNaming.RotateSessionId(false);

            Assert.Equal(32, id.Length);
            Assert.Equal($"Fortiva.BrowserBridge_{id}", BridgePipeNaming.CredentialPipeName(false));
            Assert.Equal($"Fortiva.Bridge.Token_{id}", BridgePipeNaming.TokenPipeName(false));
            Assert.Equal($"Fortiva.Bridge.UnlockRequest_{id}", BridgePipeNaming.UnlockPipeName(false));
            Assert.Equal(id, BridgeSessionRegistry.ReadActiveSessionId(false));
        }
        finally
        {
            BridgeSessionRegistry.ClearActiveSessionId(false);
        }
    }
}
