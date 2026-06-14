using System.Net.Http;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeLocalhostServerTests
{
    [Fact]
    public async Task StatusAndMatches_WhenUnlocked_ReturnsMatches()
    {
        var server = new BridgeLocalhostServer(
            () => true,
            _ => new CredentialResponse
            {
                Matches =
                [
                    new CredentialMatchSummary
                    {
                        Id = Guid.NewGuid(),
                        Title = "Test",
                        Username = "user@test.com",
                        Releasable = true
                    }
                ],
                FillNonce = "nonce-1"
            },
            _ => new CredentialResponse { Error = "not_used" });

        try
        {
            server.Start();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await http.GetStringAsync(
                $"{BridgeLocalhostConstants.Prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            Assert.Contains("vaultUnlocked", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("user@test.com", json, StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase))
        {
            // HttpListener URL reservation unavailable in CI — skip.
        }
        finally
        {
            server.Dispose();
        }
    }
}
