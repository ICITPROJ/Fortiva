using System.Net;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// In-process loopback HTTP server for the browser extension (GET status/matches, POST fill).
/// </summary>
public sealed class BridgeLocalhostServer : IDisposable
{
    private readonly Func<bool> _isUnlocked;
    private readonly Func<CredentialRequest, CredentialResponse> _listMatches;
    private readonly Func<CredentialRequest, CredentialResponse> _resolveCredentials;
    private readonly string _listenPrefix;
    private readonly string _bridgeToken;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public BridgeLocalhostServer(
        string sessionToken,
        Func<bool> isUnlocked,
        Func<CredentialRequest, CredentialResponse> listMatches,
        Func<CredentialRequest, CredentialResponse> resolveCredentials,
        string? listenPrefix = null)
    {
        _bridgeToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        _isUnlocked = isUnlocked ?? throw new ArgumentNullException(nameof(isUnlocked));
        _listMatches = listMatches ?? throw new ArgumentNullException(nameof(listMatches));
        _resolveCredentials = resolveCredentials ?? throw new ArgumentNullException(nameof(resolveCredentials));
        _listenPrefix = string.IsNullOrWhiteSpace(listenPrefix)
            ? BridgeLocalhostConstants.Prefix
            : listenPrefix;
    }

    public void Start()
    {
        if (_listener is not null)
            return;

        var listener = new HttpListener();
        listener.Prefixes.Add(_listenPrefix);
        listener.Start();

        _listener = listener;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* stopping */ }
        try { _listener?.Stop(); } catch { /* stopping */ }
        try { _listener?.Close(); } catch { /* stopping */ }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true } listener)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(ctx), CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                FortivaDiagnosticLog.Write("BridgeLocalhostServer.Listen", ex);
                try { ctx?.Response?.Abort(); } catch { /* best effort */ }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            AddCors(ctx.Response);

            if (string.Equals(ctx.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";

            if (string.Equals(path, "/auth/session", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAuthSessionAsync(ctx).ConfigureAwait(false);
                return;
            }

            if (!IsAuthorized(ctx.Request))
            {
                if (string.Equals(path, "/status-and-matches", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandlePublicStatusAsync(ctx).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(ctx.Response, 401, new { error = "unauthorized" }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/status-and-matches", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await HandleStatusAndMatchesAsync(ctx).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/execute-fill", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleExecuteFillAsync(ctx).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(ctx.Response, 404, new { error = "not_found" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeLocalhostServer.Handle", ex);
            try
            {
                if (ctx.Response.OutputStream.CanWrite)
                    await WriteJsonAsync(ctx.Response, 500, new { error = "internal_error" }).ConfigureAwait(false);
            }
            catch { /* client gone */ }
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["X-Fortiva-Bridge-Token"];
        return !string.IsNullOrEmpty(header)
            && string.Equals(header, _bridgeToken, StringComparison.Ordinal);
    }

    private static bool IsExtensionOrigin(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin))
            return false;

        var expected = BridgeLocalhostConstants.ExtensionOrigin.TrimEnd('/');
        return string.Equals(origin.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandlePublicStatusAsync(HttpListenerContext ctx)
    {
        // Never reveal unlock state to unauthenticated callers (local malware probing).
        var error = _isUnlocked() ? "auth_required" : "vault_locked";
        await WriteJsonAsync(ctx.Response, 200, new
        {
            status = new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = false,
                Error = error
            },
            authRequired = true,
            matches = Array.Empty<BridgeMatchSummary>()
        }).ConfigureAwait(false);
    }

    private async Task HandleAuthSessionAsync(HttpListenerContext ctx)
    {
        if (!IsExtensionOrigin(ctx.Request))
        {
            await WriteJsonAsync(ctx.Response, 403, new { error = "forbidden" }).ConfigureAwait(false);
            return;
        }

        // Tokens are issued only via validated named pipe (native host). HTTP never mints credentials.
        var error = _isUnlocked() ? "auth_required" : "vault_locked";
        await WriteJsonAsync(ctx.Response, 200, new
        {
            status = new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = false,
                Error = error
            },
            authRequired = true
        }).ConfigureAwait(false);
    }

    private async Task HandleStatusAndMatchesAsync(HttpListenerContext ctx)
    {
        if (!_isUnlocked())
        {
            await WriteStatusAsync(ctx.Response, new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = false,
                Error = "vault_locked"
            }).ConfigureAwait(false);
            return;
        }

        var domain = ctx.Request.QueryString["domain"] ?? "";
        var url = ctx.Request.QueryString["url"];
        var listed = _listMatches(new CredentialRequest { Domain = domain, Url = url });

        if (listed.Error is "locked")
        {
            await WriteStatusAsync(ctx.Response, new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = false,
                Error = "vault_locked"
            }).ConfigureAwait(false);
            return;
        }

        var response = new BridgeStatusAndMatchesResponse
        {
            Status = new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = true,
                Error = null
            },
            FillNonce = listed.FillNonce,
            Matches = MapMatches(listed.Matches, url)
        };

        await WriteJsonAsync(ctx.Response, 200, new
        {
            status = response.Status,
            matches = response.Matches,
            fillNonce = response.FillNonce
        }).ConfigureAwait(false);
    }

    private async Task HandleExecuteFillAsync(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        ExecuteFillPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ExecuteFillPayload>(body, BridgeJson.Options);
        }
        catch
        {
            await WriteJsonAsync(ctx.Response, 400, new { error = "bad_request" }).ConfigureAwait(false);
            return;
        }

        if (payload is null)
        {
            await WriteJsonAsync(ctx.Response, 400, new { error = "bad_request" }).ConfigureAwait(false);
            return;
        }

        var req = new CredentialRequest
        {
            Domain = payload.Domain ?? "",
            Url = payload.Url,
            EntryId = payload.EntryId,
            FillNonce = payload.FillNonce
        };

        var result = _resolveCredentials(req);
        await WriteJsonAsync(ctx.Response, 200, result).ConfigureAwait(false);
    }

    private async Task WriteStatusAsync(HttpListenerResponse response, BridgeStatusBlock status)
    {
        await WriteJsonAsync(response, 200, new
        {
            status,
            matches = Array.Empty<BridgeMatchSummary>()
        }).ConfigureAwait(false);
    }

    private static IReadOnlyList<BridgeMatchSummary> MapMatches(
        IReadOnlyList<CredentialMatchSummary>? matches,
        string? pageUrl)
    {
        if (matches is null || matches.Count == 0)
            return Array.Empty<BridgeMatchSummary>();

        return matches.Select(m => new BridgeMatchSummary
        {
            Id = m.Id.ToString(),
            Username = m.Username,
            Title = m.Title,
            Url = pageUrl ?? "",
            Releasable = m.Releasable,
            Score = m.Releasable ? 100 : 50
        }).ToList();
    }

    private static void AddCors(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = BridgeLocalhostConstants.ExtensionOrigin;
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Fortiva-Bridge-Token, Origin";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = BridgeJson.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private sealed class ExecuteFillPayload
    {
        public string? Domain { get; set; }
        public string? Url { get; set; }
        public Guid? EntryId { get; set; }
        public string? FillNonce { get; set; }
    }
}
