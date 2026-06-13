using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Services;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public class BridgeCoordinatorTests
{
    [Fact]
    public void GetAuthoritativeSnapshot_WhenNoVault_ReturnsUninitialized()
    {
        using var coordinator = new BridgeCoordinator(
            () => null,
            () => false,
            () => false,
            () => AppContext.BaseDirectory);

        var snapshot = coordinator.GetAuthoritativeSnapshot();

        Assert.False(snapshot.VaultExists);
        Assert.Equal(BridgeReadyState.Uninitialized, snapshot.State);
    }

    [Fact]
    public void GetAuthoritativeSnapshot_WhenVaultExistsButLocked_ReturnsLocked()
    {
        using var coordinator = new BridgeCoordinator(
            () => null,
            () => true,
            () => false,
            () => AppContext.BaseDirectory);

        var snapshot = coordinator.GetAuthoritativeSnapshot();

        Assert.True(snapshot.VaultExists);
        Assert.False(snapshot.IsVaultUnlocked);
        Assert.Equal(BridgeReadyState.Locked, snapshot.State);
    }

    [Fact]
    public void NotifyVaultLocked_TransitionsToLocked()
    {
        using var coordinator = new BridgeCoordinator(
            () => null,
            () => true,
            () => false,
            () => AppContext.BaseDirectory);

        coordinator.NotifyVaultLocked();

        Assert.Equal(BridgeReadyState.Locked, coordinator.CurrentState);
    }

    [Fact]
    public async Task ReconcileLifecycleAsync_WhenLocked_SetsLockedWithoutFaulting()
    {
        using var coordinator = new BridgeCoordinator(
            () => null,
            () => true,
            () => false,
            () => AppContext.BaseDirectory);

        await coordinator.ReconcileLifecycleAsync("TestLocked");

        Assert.Equal(BridgeReadyState.Locked, coordinator.CurrentState);
    }

    [Fact]
    public void ReadyStateChanged_FiresOnTransition()
    {
        using var coordinator = new BridgeCoordinator(
            () => null,
            () => true,
            () => false,
            () => AppContext.BaseDirectory);

        BridgeReadyState? observed = null;
        coordinator.ReadyStateChanged += state => observed = state;
        coordinator.NotifyVaultLocked();

        Assert.Equal(BridgeReadyState.Locked, observed);
    }
}
