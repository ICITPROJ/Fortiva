using System.Net.Http;
using System.Text;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeLocalhostServerTests
{
    [Fact]
    public async Task StatusAndMatches_WhenUnlocked_RequiresAuthThenReturnsMatches()
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

            var publicJson = await http.GetStringAsync(
                $"{BridgeLocalhostConstants.Prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            Assert.Contains("authRequired", publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user@test.com", publicJson, StringComparison.OrdinalIgnoreCase);

            using var auth = new HttpRequestMessage(HttpMethod.Post, $"{BridgeLocalhostConstants.Prefix}auth/session");
            auth.Headers.Add("Origin", BridgeLocalhostConstants.ExtensionOrigin);
            var authResponse = await http.SendAsync(auth);
            authResponse.EnsureSuccessStatusCode();
            var authJson = await authResponse.Content.ReadAsStringAsync();
            Assert.Contains("bridgeToken", authJson, StringComparison.OrdinalIgnoreCase);

            var token = ExtractJsonString(authJson, "bridgeToken");
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var authed = new HttpRequestMessage(
                HttpMethod.Get,
                $"{BridgeLocalhostConstants.Prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            authed.Headers.Add("X-Fortiva-Bridge-Token", token);
            var authedResponse = await http.SendAsync(authed);
            authedResponse.EnsureSuccessStatusCode();
            var json = await authedResponse.Content.ReadAsStringAsync();
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

    private static string? ExtractJsonString(string json, string property)
    {
        var marker = $"\"{property}\":\"";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += marker.Length;
        var end = json.IndexOf('"', start);
        return end < 0 ? null : json[start..end];
    }
}
