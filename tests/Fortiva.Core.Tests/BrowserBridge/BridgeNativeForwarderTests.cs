using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public class BridgeNativeForwarderTests
{
    [Fact]
    public async Task GetStatusAndMatches_WithNoFortiva_ReturnsHostUnreachable()
    {
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        try
        {
            var json = """{"command":"get_status_and_matches","payload":{"domain":"login.example.com","url":"https://login.example.com/"}}""";
            using var doc = JsonDocument.Parse(json);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await BridgeNativeForwarder.GetStatusAndMatchesAsync(doc.RootElement);
            sw.Stop();
            Assert.Contains("host_unreachable", result, StringComparison.OrdinalIgnoreCase);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"get_status_and_matches took {sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", null);
        }
    }

    [Fact]
    public async Task HandleAsync_RoutesGetSessionToken()
    {
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        try
        {
            var json = """{"command":"get_session_token"}""";
            using var doc = JsonDocument.Parse(json);
            var result = await BridgeNativeForwarder.HandleAsync(doc.RootElement);
            using var response = JsonDocument.Parse(result);
            Assert.True(response.RootElement.TryGetProperty("bridgeToken", out _));
            Assert.True(response.RootElement.TryGetProperty("status", out var status));
            Assert.True(status.TryGetProperty("error", out var error));
            Assert.False(string.IsNullOrEmpty(error.GetString()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", null);
        }
    }

    [Fact]
    public async Task GetSessionTokenForExtension_WithNoSession_ReturnsStructuredResponse()
    {
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        try
        {
            var result = await BridgeNativeForwarder.GetSessionTokenForExtensionAsync();
            using var response = JsonDocument.Parse(result);
            Assert.True(response.RootElement.TryGetProperty("bridgeToken", out var token));
            Assert.Equal(JsonValueKind.Null, token.ValueKind);
            Assert.True(response.RootElement.TryGetProperty("status", out var status));
            Assert.True(status.TryGetProperty("error", out var error));
            Assert.False(string.IsNullOrEmpty(error.GetString()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", null);
        }
    }

    [Fact]
    public async Task HandleAsync_RoutesGetStatusAndMatches()
    {
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        try
        {
            var json = """{"command":"get_status_and_matches","payload":{"domain":"login.example.com","url":"https://login.example.com/"}}""";
            using var doc = JsonDocument.Parse(json);
            var result = await BridgeNativeForwarder.HandleAsync(doc.RootElement);
            Assert.Contains("status", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("matches", result, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", null);
        }
    }

    [Fact]
    public async Task PrepareFill_WithNoFortiva_ReturnsStatusWithoutUnlock()
    {
        var json = """{"command":"prepare_fill","payload":{"domain":"login.example.com","url":"https://login.example.com/"}}""";
        using var doc = JsonDocument.Parse(json);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await BridgeNativeForwarder.PrepareFillAsync(doc.RootElement);
        sw.Stop();
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(
            result.Contains("setup_required", StringComparison.OrdinalIgnoreCase)
            || result.Contains("\"status\":\"locked\"", StringComparison.OrdinalIgnoreCase),
            $"Expected setup_required or locked preview, got: {result}");
        Assert.DoesNotContain("matches", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"prepare_fill took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Theory]
    [InlineData("""{"cachedSessionToken":"push-token-root"}""", "push-token-root")]
    [InlineData("""{"sessionToken":"session-token-root"}""", "session-token-root")]
    [InlineData("""{"payload":{"cachedSessionToken":"push-token-payload"}}""", "push-token-payload")]
    [InlineData("""{"payload":{"sessionToken":"session-token-payload"}}""", "session-token-payload")]
    [InlineData("""{"command":"execute_fill"}""", null)]
    public void TryGetPushCachedToken_ReadsRootAndPayload(string json, string? expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, BridgeNativeForwarder.TryGetPushCachedToken(doc.RootElement));
    }

    [Fact]
    public async Task ExecuteFill_WithNoFortiva_ReturnsErrorJson()
    {
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        try
        {
            var json = """{"command":"execute_fill","payload":{"domain":"login.example.com","url":"https://login.example.com/"}}""";
            using var doc = JsonDocument.Parse(json);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await BridgeNativeForwarder.ExecuteFillAsync(doc.RootElement);
            sw.Stop();
            Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"execute_fill took {sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", null);
        }
    }
}
