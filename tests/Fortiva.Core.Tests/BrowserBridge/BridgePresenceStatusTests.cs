using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgePresenceStatusTests
{
    [Theory]
    [InlineData("LOCKED", true)]
    [InlineData("locked", true)]
    [InlineData("UNLOCKED|BRIDGE_READY", false)]
    [InlineData("UNLOCKED|BRIDGE_DOWN", false)]
    [InlineData(null, false)]
    public void IsExplicitlyLocked_ClassifiesOnlyLocked(string? status, bool expected)
        => Assert.Equal(expected, BridgePresenceStatus.IsExplicitlyLocked(status));

    [Theory]
    [InlineData("UNLOCKED|BRIDGE_READY", true)]
    [InlineData("UNLOCKED|BRIDGE_DOWN", true)]
    [InlineData("LOCKED", false)]
    public void IsUnlocked_DetectsUnlockedPresence(string? status, bool expected)
        => Assert.Equal(expected, BridgePresenceStatus.IsUnlocked(status));

    [Theory]
    [InlineData("UNLOCKED|BRIDGE_READY", true)]
    [InlineData("UNLOCKED|BRIDGE_DOWN", false)]
    public void IsBridgeReady_DetectsReadyPipeHealth(string? status, bool expected)
        => Assert.Equal(expected, BridgePresenceStatus.IsBridgeReady(status));
}
