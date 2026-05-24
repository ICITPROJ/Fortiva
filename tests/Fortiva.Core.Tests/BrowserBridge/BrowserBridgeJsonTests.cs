using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BrowserBridgeJsonTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void CredentialRequest_DeserializesMixedCaseKeys()
    {
        const string json = """{"DOMAIN":"example.com","url":"https://example.com/login"}""";
        var req = JsonSerializer.Deserialize<CredentialRequest>(json, CaseInsensitive);
        Assert.NotNull(req);
        Assert.Equal("example.com", req.Domain);
        Assert.Equal("https://example.com/login", req.Url);
    }

    [Fact]
    public void BrowserBridgeMessage_DeserializesLowerCaseCommand()
    {
        const string json = """{"command":"getCredential","sessionToken":"abc","payload":{"domain":"x"}}""";
        var msg = JsonSerializer.Deserialize<BrowserBridgeMessage>(json, CaseInsensitive);
        Assert.NotNull(msg);
        Assert.Equal("getCredential", msg.Command);
        Assert.Equal("abc", msg.SessionToken);
    }
}
