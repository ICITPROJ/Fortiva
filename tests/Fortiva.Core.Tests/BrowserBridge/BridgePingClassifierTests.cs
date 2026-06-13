using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgePingClassifierTests
{
    [Fact]
    public void Classify_WithToken_ReturnsReady()
    {
        var result = BridgePingClassifier.Classify("session-token", null);
        Assert.True(result.Ok);
        Assert.Equal("ready", result.Status);
    }

    [Fact]
    public void Classify_ExplicitLocked_ReturnsLocked()
    {
        var result = BridgePingClassifier.Classify(null, BridgePresenceStatus.Locked);
        Assert.False(result.Ok);
        Assert.Equal("locked", result.Status);
    }

    [Fact]
    public void Classify_NullPresence_ReturnsSetupRequiredWhenFortivaNotRunning()
    {
        var result = BridgePingClassifier.Classify(null, null, fortivaRunning: false);
        Assert.Equal("setup_required", result.Status);
    }

    [Fact]
    public void Classify_NullPresence_ReturnsBridgeWarmingWhenFortivaRunning()
    {
        var result = BridgePingClassifier.Classify(null, null, fortivaRunning: true);
        Assert.Equal("bridge_warming", result.Status);
    }

    [Fact]
    public void Classify_UnlockedBridgeDown_ReturnsBridgeWarming()
    {
        var result = BridgePingClassifier.Classify(null, BridgePresenceStatus.UnlockedBridgeDown);
        Assert.Equal("bridge_warming", result.Status);
    }

    [Fact]
    public void Classify_UnlockedBridgeReadyWithoutToken_ReturnsBridgeWarming()
    {
        var result = BridgePingClassifier.Classify(null, BridgePresenceStatus.UnlockedBridgeReady);
        Assert.Equal("bridge_warming", result.Status);
    }

    [Fact]
    public void Classify_NoVault_ReturnsSetupRequired()
    {
        var result = BridgePingClassifier.Classify(null, BridgePresenceStatus.NoVault);
        Assert.Equal("setup_required", result.Status);
    }

    [Fact]
    public void Classify_NeverMapsTimeoutToLocked()
    {
        foreach (var presence in new[] { null, BridgePresenceStatus.UnlockedBridgeDown, BridgePresenceStatus.UnlockedBridgeReady })
        {
            var result = BridgePingClassifier.Classify(null, presence);
            Assert.NotEqual("locked", result.Status);
        }
    }
}
