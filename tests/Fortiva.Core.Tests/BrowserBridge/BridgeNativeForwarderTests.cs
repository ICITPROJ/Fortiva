using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public class BridgeNativeForwarderTests
{
    [Fact]
    public async Task HandleAsync_RoutesPing()
    {
        var json = """{"command":"ping"}""";
        using var doc = JsonDocument.Parse(json);
        var result = await BridgeNativeForwarder.HandleAsync(doc.RootElement);
        Assert.Contains("status", result, StringComparison.OrdinalIgnoreCase);
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
