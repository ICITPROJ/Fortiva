using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeLocalhostServerTests
{
    private const string TestSessionToken = "test-bridge-session-token-base64==";

    [Fact]
    public async Task StatusAndMatches_WhenUnlocked_RequiresAuthThenReturnsMatches()
    {
        var prefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
        var server = new BridgeLocalhostServer(
            TestSessionToken,
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            await WaitForPublicStatusAsync(http, prefix);

            var publicJson = await http.GetStringAsync(
                $"{prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            Assert.Contains("authRequired", publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user@test.com", publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("vaultUnlocked\":true", publicJson.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

            using var auth = new HttpRequestMessage(HttpMethod.Post, $"{prefix}auth/session");
            auth.Headers.TryAddWithoutValidation("Origin", BridgeLocalhostConstants.ExtensionOrigin);
            var authResponse = await http.SendAsync(auth);
            authResponse.EnsureSuccessStatusCode();
            var authJson = await authResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("bridgeToken", authJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("authRequired", authJson, StringComparison.OrdinalIgnoreCase);

            var json = await WaitForAuthedMatchesAsync(http, prefix, TestSessionToken, "user@test.com");
            Assert.Contains("vaultUnlocked", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("user@test.com", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bridgeToken", json, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task AuthSession_WithoutOrigin_ReturnsForbidden()
    {
        var prefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
        var server = new BridgeLocalhostServer(
            TestSessionToken,
            () => true,
            _ => new CredentialResponse(),
            _ => new CredentialResponse(),
            listenPrefix: prefix);

        if (!TryStartServer(server))
            return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await http.PostAsync($"{prefix}auth/session", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    [Fact]
    public async Task AuthSession_LoopbackWithoutOrigin_DoesNotIssueToken()
    {
        var prefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
        var server = new BridgeLocalhostServer(
            TestSessionToken,
            () => true,
            _ => new CredentialResponse(),
            _ => new CredentialResponse(),
            listenPrefix: prefix);

        if (!TryStartServer(server))
            return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{prefix}auth/session");
            var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private static async Task WaitForPublicStatusAsync(HttpClient http, string prefix)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var json = await http.GetStringAsync(
                    $"{prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
                if (json.Contains("authRequired", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }
            catch
            {
                // Listener still starting — retry.
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Bridge localhost server did not become ready.");
    }

    private static async Task<string> WaitForAuthedMatchesAsync(
        HttpClient http,
        string prefix,
        string token,
        string expectedUsername)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            using var authed = new HttpRequestMessage(
                HttpMethod.Get,
                $"{prefix}status-and-matches?domain=login.example.com&url=https://login.example.com/");
            authed.Headers.TryAddWithoutValidation("X-Fortiva-Bridge-Token", token);
            var authedResponse = await http.SendAsync(authed);
            authedResponse.EnsureSuccessStatusCode();
            var json = await authedResponse.Content.ReadAsStringAsync();
            if (json.Contains(expectedUsername, StringComparison.OrdinalIgnoreCase))
                return json;

            await Task.Delay(25);
        }

        throw new TimeoutException($"Authed status-and-matches did not return {expectedUsername}.");
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
}
