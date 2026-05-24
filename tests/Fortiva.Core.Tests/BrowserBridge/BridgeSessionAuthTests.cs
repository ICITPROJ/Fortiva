using System.Security.Cryptography;
using System.Text;
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
        BridgeSessionAuth.ConfigureTokenDirectory(null!);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void CreateSessionToken_RoundTripsViaTryRead()
    {
        var token = BridgeSessionAuth.CreateSessionToken();
        Assert.True(BridgeSessionAuth.TryReadExpectedToken(out var read));
        Assert.True(BridgeSessionAuth.ValidateToken(token, read));
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
    public void ClearSessionToken_RemovesStoredFile()
    {
        BridgeSessionAuth.CreateSessionToken();
        Assert.True(File.Exists(BridgeSessionAuth.TokenPath));
        BridgeSessionAuth.ClearSessionToken();
        Assert.False(File.Exists(BridgeSessionAuth.TokenPath));
    }
}
