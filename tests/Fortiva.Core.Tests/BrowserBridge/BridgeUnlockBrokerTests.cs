using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeUnlockBrokerTests
{
    [Fact]
    public async Task ProcessUnlockRequest_InvalidLine_ReturnsInvalid()
    {
        var broker = new BridgeUnlockBroker(() => true, () => true, _ => Task.FromResult(true));

        var response = await broker.ProcessUnlockRequestAsync("nope", CancellationToken.None);

        Assert.Equal("INVALID", response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_NoVault_ReturnsNoVault()
    {
        var broker = new BridgeUnlockBroker(() => false, () => false, _ => Task.FromResult(true));

        var response = await broker.ProcessUnlockRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("NO_VAULT", response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_AlreadyUnlocked_ReturnsAlreadyUnlocked()
    {
        var broker = new BridgeUnlockBroker(() => true, () => true, _ => Task.FromResult(true));

        var response = await broker.ProcessUnlockRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("ALREADY_UNLOCKED", response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_WaitsForUnlockHandler()
    {
        var unlockCalled = false;
        var broker = new BridgeUnlockBroker(
            () => false,
            () => true,
            _ =>
            {
                unlockCalled = true;
                return Task.FromResult(true);
            });

        var response = await broker.ProcessUnlockRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("OK", response);
        Assert.True(unlockCalled);
    }

    [Fact]
    public async Task ProcessUnlockRequest_UnlockDenied_ReturnsFailed()
    {
        var broker = new BridgeUnlockBroker(
            () => false,
            () => true,
            _ => Task.FromResult(false));

        var response = await broker.ProcessUnlockRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("FAILED", response);
    }
}
