using System.Net;
using System.Security.Cryptography;
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
    private readonly string _bridgeToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public BridgeLocalhostServer(
        Func<bool> isUnlocked,
        Func<CredentialRequest, CredentialResponse> listMatches,
        Func<CredentialRequest, CredentialResponse> resolveCredentials)
    {
        _isUnlocked = isUnlocked ?? throw new ArgumentNullException(nameof(isUnlocked));
        _listMatches = listMatches ?? throw new ArgumentNullException(nameof(listMatches));
        _resolveCredentials = resolveCredentials ?? throw new ArgumentNullException(nameof(resolveCredentials));
    }

    public void Start()
    {
        if (_listener is not null)
            return;

        var listener = new HttpListener();
        listener.Prefixes.Add(BridgeLocalhostConstants.Prefix);
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

            if (!IsAuthorized(ctx.Request))
            {
                await WriteJsonAsync(ctx.Response, 401, new { error = "unauthorized" }).ConfigureAwait(false);
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";

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
        if (string.IsNullOrEmpty(header))
            return string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.Url?.AbsolutePath?.TrimEnd('/'), "/status-and-matches", StringComparison.OrdinalIgnoreCase);

        return string.Equals(header, _bridgeToken, StringComparison.Ordinal);
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
            bridgeToken = _bridgeToken,
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
            bridgeToken = _bridgeToken,
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
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Fortiva-Bridge-Token";
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
