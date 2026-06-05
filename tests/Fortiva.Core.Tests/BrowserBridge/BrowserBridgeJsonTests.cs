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

    [Fact]
    public async Task ReadBoundedLineAsync_ReadsNormalLine()
    {
        using var reader = new StringReader("{\"command\":\"x\"}\n{\"command\":\"y\"}\n");
        var line = await BridgeJson.ReadBoundedLineAsync(reader);
        Assert.Equal("{\"command\":\"x\"}", line);
    }

    [Fact]
    public async Task ReadBoundedLineAsync_ReturnsNull_WhenOverCap()
    {
        var huge = new string('a', BridgeJson.MaxRequestBytes + 10); // no newline, exceeds cap
        using var reader = new StringReader(huge);
        var line = await BridgeJson.ReadBoundedLineAsync(reader);
        Assert.Null(line);
    }

    [Fact]
    public async Task ReadBoundedLineAsync_ReturnsNull_AtEndOfStream()
    {
        using var reader = new StringReader("");
        var line = await BridgeJson.ReadBoundedLineAsync(reader);
        Assert.Null(line);
    }
}
