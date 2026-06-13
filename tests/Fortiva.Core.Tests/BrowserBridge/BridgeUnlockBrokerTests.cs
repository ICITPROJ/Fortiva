using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public class BridgeUnlockBrokerTests
{
    [Fact]
    public async Task ProcessRequest_InvalidLine_ReturnsInvalid()
    {
        var broker = new BridgeUnlockBroker(() => true, () => true, _ => Task.FromResult(true));

        var response = await broker.ProcessRequestAsync("nope", CancellationToken.None);

        Assert.Equal("INVALID", response);
    }

    [Fact]
    public async Task ProcessRequest_Status_ReturnsLockedWhenVaultExistsButLocked()
    {
        var broker = new BridgeUnlockBroker(() => false, () => true, _ => Task.FromResult(true));

        var response = await broker.ProcessRequestAsync("STATUS", CancellationToken.None);

        Assert.Equal("LOCKED", response);
    }

    [Fact]
    public async Task ProcessRequest_Status_ReturnsUnlockedBridgeReadyWhenHealthy()
    {
        var broker = new BridgeUnlockBroker(
            () => true,
            () => true,
            _ => Task.FromResult(true),
            () => true);

        var response = await broker.ProcessRequestAsync("STATUS", CancellationToken.None);

        Assert.Equal(BridgePresenceStatus.UnlockedBridgeReady, response);
    }

    [Fact]
    public async Task ProcessRequest_Status_ReturnsUnlockedBridgeDownWhenPipesNotReady()
    {
        var broker = new BridgeUnlockBroker(
            () => true,
            () => true,
            _ => Task.FromResult(true),
            () => false);

        var response = await broker.ProcessRequestAsync("STATUS", CancellationToken.None);

        Assert.Equal(BridgePresenceStatus.UnlockedBridgeDown, response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_NoVault_ReturnsNoVault()
    {
        var broker = new BridgeUnlockBroker(() => false, () => false, _ => Task.FromResult(true));

        var response = await broker.ProcessRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("NO_VAULT", response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_AlreadyUnlocked_ReturnsAlreadyUnlocked()
    {
        var broker = new BridgeUnlockBroker(() => true, () => true, _ => Task.FromResult(true));

        var response = await broker.ProcessRequestAsync("UNLOCK", CancellationToken.None);

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

        var response = await broker.ProcessRequestAsync("UNLOCK", CancellationToken.None);

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

        var response = await broker.ProcessRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("FAILED", response);
    }

    [Fact]
    public async Task ProcessUnlockRequest_RateLimited_ReturnsRateLimited()
    {
        var broker = new BridgeUnlockBroker(
            () => false,
            () => true,
            _ => Task.FromResult(false));

        string? last = null;
        for (var i = 0; i < 9; i++)
            last = await broker.ProcessUnlockRequestAsync("UNLOCK", CancellationToken.None);

        Assert.Equal("RATE_LIMITED", last);
    }
}
