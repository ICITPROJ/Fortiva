using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeFillNonceTests
{
    [Fact]
    public void IssueAndConsume_SingleUse()
    {
        var broker = new BridgeFillNonce();
        var nonce = broker.Issue();

        Assert.True(broker.TryConsume(nonce));
        Assert.False(broker.TryConsume(nonce));
    }

    [Fact]
    public void TryConsume_RejectsUnknownNonce()
    {
        var broker = new BridgeFillNonce();
        Assert.False(broker.TryConsume("deadbeef"));
    }
}
