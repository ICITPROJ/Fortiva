using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeFillNonceTests
{
    [Fact]
    public void IssueAndConsume_SingleUse()
    {
        var broker = new BridgeFillNonce();
        var nonce = broker.Issue("example.com");

        Assert.True(broker.TryConsume(nonce, "example.com"));
        Assert.False(broker.TryConsume(nonce, "example.com"));
    }

    [Fact]
    public void TryConsume_RejectsUnknownNonce()
    {
        var broker = new BridgeFillNonce();
        Assert.False(broker.TryConsume("deadbeef", "example.com"));
    }

    [Fact]
    public void TryConsume_RejectsNonceForDifferentHost()
    {
        var broker = new BridgeFillNonce();
        var nonce = broker.Issue("attacker.com");

        // A nonce issued for one host must not unlock credentials for another host.
        Assert.False(broker.TryConsume(nonce, "victim-bank.com"));
        // The nonce remains usable for the host it was issued for (no leak of single-use state).
        Assert.True(broker.TryConsume(nonce, "attacker.com"));
    }

    [Fact]
    public void TryConsume_HostMatchIsCaseInsensitive()
    {
        var broker = new BridgeFillNonce();
        var nonce = broker.Issue("Example.COM");
        Assert.True(broker.TryConsume(nonce, "example.com"));
    }

    [Fact]
    public void Issue_ReplacesPriorNonceForSameHost()
    {
        var broker = new BridgeFillNonce();
        var first = broker.Issue("example.com");
        var second = broker.Issue("example.com");

        Assert.False(broker.TryConsume(first, "example.com"));
        Assert.True(broker.TryConsume(second, "example.com"));
    }
}
