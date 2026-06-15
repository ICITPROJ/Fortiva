using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeLocalhostServerTests
{
    [Fact]
    public async Task StatusAndMatches_WhenUnlocked_RequiresAuthThenReturnsMatches()
    {
        var prefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
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
            _ => new CredentialResponse { Error = "not_used" },
            listenPrefix: prefix);

        if (!TryStartServer(server))
            return;

        try
        {
            await Task.Delay(75);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            var publicJson = await http.GetStringAsync(
                $"{prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            Assert.Contains("authRequired", publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user@test.com", publicJson, StringComparison.OrdinalIgnoreCase);

            using var auth = new HttpRequestMessage(HttpMethod.Post, $"{prefix}auth/session");
            auth.Headers.TryAddWithoutValidation("Origin", BridgeLocalhostConstants.ExtensionOrigin);
            var authResponse = await http.SendAsync(auth);
            authResponse.EnsureSuccessStatusCode();
            var authJson = await authResponse.Content.ReadAsStringAsync();
            Assert.Contains("bridgeToken", authJson, StringComparison.OrdinalIgnoreCase);

            var token = ExtractJsonString(authJson, "bridgeToken");
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var authed = new HttpRequestMessage(
                HttpMethod.Get,
                $"{prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            authed.Headers.TryAddWithoutValidation("X-Fortiva-Bridge-Token", token);
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
            await Task.Delay(50);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool TryStartServer(BridgeLocalhostServer server)
    {
        try
        {
            server.Start();
            return true;
        }
        catch (HttpListenerException)
        {
            // Port in use or URL ACL missing — skip.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
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
