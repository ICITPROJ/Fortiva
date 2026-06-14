using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeSnapshotPushTests
{
    [Theory]
    [InlineData(BridgeReadyState.Unlocked, "token", "ready", true)]
    [InlineData(BridgeReadyState.Locked, null, "locked", false)]
    [InlineData(BridgeReadyState.StartingInfrastructure, null, "bridge_warming", false)]
    public void FromSnapshot_MapsPingStatus(
        BridgeReadyState state,
        string? token,
        string expectedStatus,
        bool expectedOk)
    {
        var snapshot = new BridgePresenceSnapshot(
            state,
            true,
            state == BridgeReadyState.Unlocked,
            token,
            DateTime.UtcNow);

        var push = BridgeSnapshotPush.FromSnapshot(snapshot);

        Assert.Equal(BridgePushMessage.CurrentSchemaVersion, push.SchemaVersion);
        Assert.Equal("STATE_CHANGED", push.Type);
        Assert.Equal(state.ToString(), push.State);
        Assert.Equal(expectedStatus, push.Status);
        Assert.Equal(expectedOk, push.Ok);
        if (token is not null)
            Assert.Equal(token, push.CachedSessionToken);
    }
}
