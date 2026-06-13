using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeCredentialProtectorTests
{
    [Fact]
    public void RoundTrip_SealsAndUnsealsUsernameAndPassword()
    {
        const string token = "test-session-token-base64==";
        var original = new CredentialResponse
        {
            Found = true,
            Username = "alice",
            Password = "s3cret!",
            Title = "Example"
        };

        var sealed_ = BridgeCredentialProtector.ProtectForPipe(original, token);
        Assert.True(sealed_.UsernameProtected);
        Assert.True(sealed_.PasswordProtected);
        Assert.NotEmpty(sealed_.UsernameSealed ?? "");
        Assert.NotEmpty(sealed_.PasswordSealed ?? "");
        Assert.Equal("", sealed_.Username);
        Assert.Equal("", sealed_.Password);

        var restored = BridgeCredentialProtector.UnprotectFromPipe(sealed_, token);
        Assert.Equal("alice", restored.Username);
        Assert.Equal("s3cret!", restored.Password);
        Assert.False(restored.UsernameProtected);
        Assert.False(restored.PasswordProtected);
    }

    [Fact]
    public void UnprotectJsonLine_RestoresPlaintextForExtension()
    {
        const string token = "pipe-token-v1";
        var json = BridgeJson.Serialize(BridgeCredentialProtector.ProtectForPipe(
            new CredentialResponse { Found = true, Password = "pw", Username = "u" },
            token));

        var plain = BridgeCredentialProtector.UnprotectJsonLine(json, token);
        var parsed = BridgeJson.Deserialize<CredentialResponse>(plain);
        Assert.NotNull(parsed);
        Assert.Equal("u", parsed!.Username);
        Assert.Equal("pw", parsed.Password);
    }

    [Fact]
    public void ProtectListForPipe_SealsMatchUsernames()
    {
        const string token = "list-pipe-token";
        var id = Guid.NewGuid();
        var response = new CredentialResponse
        {
            Found = true,
            Matches =
            [
                new CredentialMatchSummary
                {
                    Id = id,
                    Title = "Site",
                    Username = "alice@example.com",
                    Releasable = true
                }
            ]
        };

        var sealed_ = BridgeCredentialProtector.ProtectListForPipe(response, token);
        Assert.Single(sealed_.Matches!);
        Assert.True(sealed_.Matches![0].UsernameProtected);
        Assert.NotEmpty(sealed_.Matches![0].UsernameSealed ?? "");
        Assert.Equal("", sealed_.Matches![0].Username);

        var restored = BridgeCredentialProtector.UnprotectFromPipe(sealed_, token);
        Assert.Equal("alice@example.com", restored.Matches![0].Username);
    }

    [Fact]
    public void UnprotectJsonLine_InvalidSeal_ReturnsDecryptFailed()
    {
        const string token = "pipe-token-v1";
        var json = BridgeJson.Serialize(new CredentialResponse
        {
            Found = true,
            UsernameProtected = true,
            UsernameSealed = "not-valid-sealed-data",
            Password = ""
        });

        var plain = BridgeCredentialProtector.UnprotectJsonLine(json, token);
        var parsed = BridgeJson.Deserialize<CredentialResponse>(plain);
        Assert.NotNull(parsed);
        Assert.Equal("decrypt_failed", parsed!.Error);
    }

    [Fact]
    public void ProtectForPipe_NoOpWhenNotFound()
    {
        const string token = "t";
        var response = new CredentialResponse { Found = false, Username = "u", Password = "p" };
        var result = BridgeCredentialProtector.ProtectForPipe(response, token);
        Assert.False(result.UsernameProtected);
        Assert.False(result.PasswordProtected);
        Assert.Equal("u", result.Username);
        Assert.Equal("p", result.Password);
    }
}
