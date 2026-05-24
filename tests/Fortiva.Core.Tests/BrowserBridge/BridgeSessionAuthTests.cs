using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeSessionAuthTests : IDisposable
{
    private readonly string _dir;

    public BridgeSessionAuthTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FortivaBridgeAuth-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
        BridgeSessionAuth.ConfigureTokenDirectory(_dir);
    }

    public void Dispose()
    {
        BridgeSessionAuth.ClearSessionToken();
        BridgeSessionAuth.ConfigureTokenDirectory(null!);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void CreateSessionToken_StoredInMemoryOnly()
    {
        var token = BridgeSessionAuth.CreateSessionToken();
        Assert.True(BridgeSessionAuth.TryReadExpectedToken(out var read));
        Assert.True(BridgeSessionAuth.ValidateToken(token, read));
        Assert.False(File.Exists(BridgeSessionAuth.TokenPath));
    }

    [Fact]
    public void ValidateToken_RejectsMismatchedTokens()
    {
        var token = BridgeSessionAuth.CreateSessionToken();
        Assert.True(BridgeSessionAuth.TryReadExpectedToken(out var read));
        Assert.True(BridgeSessionAuth.ValidateToken(read, token));
        Assert.False(BridgeSessionAuth.ValidateToken(read + "x", token));
    }

    [Fact]
    public void ClearSessionToken_RemovesInMemoryToken()
    {
        BridgeSessionAuth.CreateSessionToken();
        BridgeSessionAuth.ClearSessionToken();
        Assert.False(BridgeSessionAuth.TryReadExpectedToken(out _));
        Assert.False(File.Exists(BridgeSessionAuth.TokenPath));
    }
}
