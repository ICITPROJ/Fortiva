using System.Text;
using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Native-messaging host logic: one unlock + token + credential round-trip per browser request.
/// </summary>
public static class BridgeNativeForwarder
{
    private static int OverallTimeoutSeconds => IsFastTest ? 5 : 120;
    private const int CredentialReadSeconds = 30;

    public static bool IsEnterpriseEdition { get; private set; }

    public static void ConfigureEdition(bool enterprise) => IsEnterpriseEdition = enterprise;

    public static bool HasActiveBridgeSession() => BridgePipeNaming.HasActiveSession(IsEnterpriseEdition);

    public static async Task<string> HandleAsync(JsonElement request, CancellationToken ct = default)
    {
        var command = request.TryGetProperty("command", out var cmd) ? cmd.GetString() : "";
        if (string.Equals(command, "get_status_and_matches", StringComparison.OrdinalIgnoreCase))
            return await GetStatusAndMatchesAsync(request, ct).ConfigureAwait(false);

        if (string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase))
            return await ExecuteFillAsync(request, ct).ConfigureAwait(false);

        if (string.Equals(command, "ping", StringComparison.OrdinalIgnoreCase))
            return BridgeJson.Serialize(await BridgePingEvaluator.EvaluateAsync(ct).ConfigureAwait(false));

        if (string.Equals(command, "prepare_fill", StringComparison.OrdinalIgnoreCase))
            return await PrepareFillAsync(request, ct).ConfigureAwait(false);

        if (string.Equals(command, "get_session_token", StringComparison.OrdinalIgnoreCase))
            return await GetSessionTokenForExtensionAsync(ct).ConfigureAwait(false);

        return await ForwardCredentialAsync(request, ct, allowBrowserUnlock: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Single native command for extension: app status + credential matches. Never unlocks from browser.
    /// </summary>
    public static async Task<string> GetStatusAndMatchesAsync(JsonElement request, CancellationToken ct = default)
    {
        var domain = "";
        var url = "";
        if (request.TryGetProperty("payload", out var payload))
        {
            if (payload.TryGetProperty("domain", out var d)) domain = d.GetString() ?? "";
            if (payload.TryGetProperty("url", out var u)) url = u.GetString() ?? "";
        }

        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(IsFastTest ? 5 : 10));

            if (!BridgePipeNaming.HasActiveSession(IsEnterpriseEdition))
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                    VaultUnlocked = false,
                    Error = "host_unreachable"
                });
            }

            var appRunning = BridgeProcessCheck.IsFortivaRunning();
            if (!appRunning)
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = false,
                    VaultUnlocked = false,
                    Error = "host_unreachable"
                });
            }

            var presence = await BridgePresenceClient.RequestStatusAsync(
                timeoutMs: IsFastTest ? 400 : 800,
                maxAttempts: 2,
                cancellationToken: overall.Token).ConfigureAwait(false);

            if (BridgePresenceStatus.IsExplicitlyLocked(presence))
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = false,
                    Error = "vault_locked"
                });
            }

            if (string.Equals(presence, BridgePresenceStatus.NoVault, StringComparison.OrdinalIgnoreCase))
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = false,
                    Error = "host_unreachable"
                });
            }

            var token = await RequestSessionTokenAsync(
                attempts: 2,
                timeoutMs: IsFastTest ? 400 : 800,
                overall.Token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(token))
            {
                token = await RequestSessionTokenAsync(
                    attempts: 2,
                    timeoutMs: IsFastTest ? 400 : 800,
                    overall.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(token))
                {
                    var error = BridgePresenceStatus.IsUnlocked(presence) ? "token_stale" : "vault_locked";
                    return SerializeStatusAndMatches(new BridgeStatusBlock
                    {
                        AppRunning = true,
                        VaultUnlocked = BridgePresenceStatus.IsUnlocked(presence),
                        Error = error
                    });
                }
            }

            if (!await WaitForCredentialPipeAsync(overall.Token, maxAttempts: IsFastTest ? 3 : 6, delayMs: 100)
                    .ConfigureAwait(false))
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = true,
                    Error = "internal_error"
                });
            }

            var listJson = await InvokeCredentialPipeAsync(
                BuildEnvelope("list_credentials", domain, url),
                token!,
                overall.Token,
                readSeconds: 4).ConfigureAwait(false);

            if (IsStaleTokenError(listJson))
            {
                var fresh = await RequestSessionTokenAsync(
                    attempts: 2,
                    timeoutMs: IsFastTest ? 400 : 800,
                    overall.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(fresh))
                {
                    return SerializeStatusAndMatches(new BridgeStatusBlock
                    {
                        AppRunning = true,
                        VaultUnlocked = true,
                        Error = "token_stale"
                    });
                }

                listJson = await InvokeCredentialPipeAsync(
                    BuildEnvelope("list_credentials", domain, url),
                    fresh,
                    overall.Token,
                    readSeconds: 4).ConfigureAwait(false);
                if (IsStaleTokenError(listJson))
                {
                    return SerializeStatusAndMatches(new BridgeStatusBlock
                    {
                        AppRunning = true,
                        VaultUnlocked = true,
                        Error = "token_stale"
                    });
                }
            }

            var credError = CredentialJsonError(listJson);
            if (credError is "locked")
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = false,
                    Error = "vault_locked"
                });
            }

            if (credError is not null and not "no_match")
            {
                return SerializeStatusAndMatches(new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = true,
                    Error = "internal_error"
                });
            }

            return ParseListCredentialsToStatusAndMatches(listJson, url);
        }
        catch (OperationCanceledException)
        {
            return SerializeStatusAndMatches(new BridgeStatusBlock
            {
                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                VaultUnlocked = false,
                Error = "internal_error"
            });
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeNativeForwarder.GetStatusAndMatches", ex);
            return SerializeStatusAndMatches(new BridgeStatusBlock
            {
                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                VaultUnlocked = false,
                Error = "internal_error"
            });
        }
    }

    /// <summary>
    /// Returns the per-unlock session token for loopback HTTP bridge auth.
    /// Only reachable via validated native-messaging host (pipe client validation).
    /// </summary>
    public static async Task<string> GetSessionTokenForExtensionAsync(CancellationToken ct = default)
    {
        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(IsFastTest ? 5 : 8));

            if (!BridgePipeNaming.HasActiveSession(IsEnterpriseEdition))
            {
                return SerializeSessionToken(null, new BridgeStatusBlock
                {
                    AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                    VaultUnlocked = false,
                    Error = "vault_locked"
                });
            }

            var token = await RequestSessionTokenAsync(
                attempts: IsFastTest ? 2 : 3,
                timeoutMs: IsFastTest ? 400 : 1200,
                overall.Token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(token))
            {
                return SerializeSessionToken(null, new BridgeStatusBlock
                {
                    AppRunning = true,
                    VaultUnlocked = false,
                    Error = "token_stale"
                });
            }

            return SerializeSessionToken(token, new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = true,
                Error = null
            });
        }
        catch (OperationCanceledException)
        {
            return SerializeSessionToken(null, new BridgeStatusBlock
            {
                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                VaultUnlocked = false,
                Error = "internal_error"
            });
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeNativeForwarder.GetSessionToken", ex);
            return SerializeSessionToken(null, new BridgeStatusBlock
            {
                AppRunning = false,
                VaultUnlocked = false,
                Error = "host_unreachable"
            });
        }
    }

    private static string SerializeSessionToken(string? bridgeToken, BridgeStatusBlock status) =>
        BridgeJson.SerializeSessionToken(new BridgeSessionTokenResponse
        {
            BridgeToken = bridgeToken,
            Status = status
        });

    private static string SerializeStatusAndMatches(BridgeStatusBlock status, IReadOnlyList<BridgeMatchSummary>? matches = null) =>
        BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
        {
            Status = status,
            Matches = matches ?? Array.Empty<BridgeMatchSummary>()
        });

    private static bool IsStaleTokenError(string credentialJson)
    {
        var err = CredentialJsonError(credentialJson);
        return err is "decrypt_failed";
    }

    private static string ParseListCredentialsToStatusAndMatches(string listJson, string? pageUrl)
    {
        using var doc = JsonDocument.Parse(listJson);
        var root = doc.RootElement;

        var response = new BridgeStatusAndMatchesResponse
        {
            Status = new BridgeStatusBlock
            {
                AppRunning = true,
                VaultUnlocked = true,
                Error = null
            }
        };

        if (root.TryGetProperty("fillNonce", out var nonceEl) && nonceEl.ValueKind == JsonValueKind.String)
            response.FillNonce = nonceEl.GetString();

        if (root.TryGetProperty("matches", out var matchesEl) && matchesEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<BridgeMatchSummary>();
            foreach (var item in matchesEl.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var username = item.TryGetProperty("username", out var userEl) ? userEl.GetString() ?? "" : "";
                var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                var releasable = !item.TryGetProperty("releasable", out var relEl) || relEl.GetBoolean();
                list.Add(new BridgeMatchSummary
                {
                    Id = id,
                    Username = username,
                    Url = pageUrl ?? "",
                    Title = title,
                    Releasable = releasable,
                    Score = releasable ? 100 : 50
                });
            }

            response.Matches = list;
        }

        return BridgeJson.Serialize(response);
    }

    /// <summary>Status + list_credentials in a single native-host invocation (fewer spawns, one unlock flow).</summary>
    public static async Task<string> PrepareFillAsync(JsonElement request, CancellationToken ct = default)
    {
        var domain = "";
        var url = "";
        if (request.TryGetProperty("payload", out var payload))
        {
            if (payload.TryGetProperty("domain", out var d)) domain = d.GetString() ?? "";
            if (payload.TryGetProperty("url", out var u)) url = u.GetString();
        }

        var ping = await BridgePingEvaluator.EvaluateAsync(ct).ConfigureAwait(false);
        // Preview only unless vault is ready — execute_fill handles unlock + credentials on Fill click.
        if (!string.Equals(ping.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return BridgeJson.Serialize(ping);

        var listed = await ForwardCredentialAsync(
            BuildEnvelope("list_credentials", domain, url),
            ct,
            tokenSource: request,
            allowBrowserUnlock: false).ConfigureAwait(false);
        return MergeStatusAndCredential(ping, listed);
    }

    /// <summary>
    /// Unlock (if needed), list matches, and fetch credentials in one native-host process —
    /// avoids a second browser spawn mid-unlock.
    /// </summary>
    public static async Task<string> ExecuteFillAsync(JsonElement request, CancellationToken ct = default)
    {
        var domain = "";
        var url = "";
        string? entryIdText = null;
        string? fillNonce = null;
        if (request.TryGetProperty("payload", out var payload))
        {
            if (payload.TryGetProperty("domain", out var d)) domain = d.GetString() ?? "";
            if (payload.TryGetProperty("url", out var u)) url = u.GetString();
            if (payload.TryGetProperty("entryId", out var e)) entryIdText = e.GetString();
            if (payload.TryGetProperty("fillNonce", out var n)) fillNonce = n.GetString();
        }

        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(OverallTimeoutSeconds));

            var token = await EnsureSessionTokenAsync(request, overall.Token, allowBrowserUnlock: false)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
                return await BuildTokenFailureResponseAsync().ConfigureAwait(false);

            if (!await WaitForCredentialPipeAsync(overall.Token).ConfigureAwait(false))
                return BridgeJson.Serialize(new CredentialResponse { Error = "setup_required" });

            string listJson;
            if (string.IsNullOrEmpty(fillNonce))
            {
                listJson = await InvokeCredentialPipeAsync(
                    BuildEnvelope("list_credentials", domain, url),
                    token,
                    overall.Token).ConfigureAwait(false);
            }
            else
            {
                listJson = BridgeJson.Serialize(new CredentialResponse { FillNonce = fillNonce });
            }

            using var listDoc = JsonDocument.Parse(listJson);
            var listRoot = listDoc.RootElement;

            if (listRoot.TryGetProperty("error", out var listErr) && listErr.ValueKind == JsonValueKind.String)
            {
                var code = listErr.GetString();
                if (code is "locked" or "cancelled" or "setup_required" or "host_mismatch" or "invalid_host")
                    return listJson;
            }

            if (string.IsNullOrEmpty(fillNonce) &&
                listRoot.TryGetProperty("fillNonce", out var nonceEl) &&
                nonceEl.ValueKind == JsonValueKind.String)
            {
                fillNonce = nonceEl.GetString();
            }

            Guid? entryId = null;
            if (!string.IsNullOrEmpty(entryIdText) && Guid.TryParse(entryIdText, out var parsed))
                entryId = parsed;

            if (!entryId.HasValue &&
                listRoot.TryGetProperty("matches", out var matchesEl) &&
                matchesEl.ValueKind == JsonValueKind.Array)
            {
                var count = matchesEl.GetArrayLength();
                if (count == 1 &&
                    matchesEl[0].TryGetProperty("id", out var idEl) &&
                    Guid.TryParse(idEl.GetString(), out var single))
                {
                    entryId = single;
                }
                else if (count > 1)
                {
                    return listJson;
                }
            }

            if (!string.IsNullOrEmpty(fillNonce) && !entryId.HasValue)
                return BridgeJson.Serialize(new CredentialResponse { Error = "invalid_nonce" });

            if (string.IsNullOrEmpty(fillNonce) || !entryId.HasValue)
                return BridgeJson.Serialize(new CredentialResponse { Error = "no_match" });

            var getJson = await InvokeCredentialPipeAsync(
                BuildGetEnvelope(domain, url, entryId.Value, fillNonce),
                token,
                overall.Token).ConfigureAwait(false);

            return MergeFillCredential(listJson, getJson);
        }
        catch (OperationCanceledException)
        {
            return BridgeJson.Serialize(new CredentialResponse { Error = "cancelled" });
        }
        catch (IOException)
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva bridge connection dropped. Wait a moment and click Fill again."
            });
        }
        catch (Exception ex) when (IsFastTest)
        {
            return BridgeJson.Serialize(new CredentialResponse { Error = "setup_required", Message = ex.Message });
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeNativeForwarder.ExecuteFill", ex);
            return BridgeJson.Serialize(new CredentialResponse { Error = "internal_error" });
        }
    }

    public static async Task<string> ForwardCredentialAsync(
        JsonElement request,
        CancellationToken ct = default,
        JsonElement? tokenSource = null,
        bool allowBrowserUnlock = true)
    {
        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromSeconds(OverallTimeoutSeconds));

            var token = await EnsureSessionTokenAsync(
                tokenSource ?? request,
                overall.Token,
                allowBrowserUnlock).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
                return await BuildTokenFailureResponseAsync().ConfigureAwait(false);

            if (!await WaitForCredentialPipeAsync(overall.Token).ConfigureAwait(false))
            {
                return BridgeJson.Serialize(new CredentialResponse { Error = "setup_required" });
            }

            var result = await InvokeCredentialPipeAsync(request, token, overall.Token).ConfigureAwait(false);
            if (ShouldRetryCredentialPipeWithFreshToken(result, tokenSource ?? request))
            {
                var fresh = await RequestSessionTokenAsync(
                    attempts: IsFastTest ? 1 : 3,
                    timeoutMs: IsFastTest ? 400 : 1200,
                    overall.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(fresh) && !string.Equals(fresh, token, StringComparison.Ordinal))
                    result = await InvokeCredentialPipeAsync(request, fresh, overall.Token).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return BridgeJson.Serialize(new CredentialResponse { Error = "cancelled" });
        }
        catch (IOException)
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva bridge connection dropped. Wait a moment and click Fill again."
            });
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeNativeForwarder.ForwardCredential", ex);
            return BridgeJson.Serialize(new CredentialResponse { Error = "internal_error" });
        }
    }

    private static async Task<string?> EnsureSessionTokenAsync(
        JsonElement request,
        CancellationToken ct,
        bool allowBrowserUnlock = true)
    {
        var pushed = TryGetPushCachedToken(request);
        if (!string.IsNullOrWhiteSpace(pushed))
            return pushed;

        var fast = IsFastTest;
        var token = await RequestSessionTokenAsync(
            attempts: fast ? 1 : 2,
            timeoutMs: fast ? 400 : 800,
            ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
            return token;

        var presence = await BridgePresenceClient.RequestStatusAsync(
            timeoutMs: fast ? 400 : 1200,
            maxAttempts: fast ? 1 : 2,
            cancellationToken: ct).ConfigureAwait(false);
        if (BridgePresenceStatus.IsExplicitlyLocked(presence))
            return null;

        if (string.Equals(presence, BridgePresenceStatus.NoVault, StringComparison.OrdinalIgnoreCase))
            return null;

        token = await RequestSessionTokenAsync(
            attempts: fast ? 1 : 3,
            timeoutMs: fast ? 400 : 1500,
            ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
            return token;

        if (BridgePresenceStatus.IsUnlocked(presence))
        {
            token = await RequestSessionTokenAsync(
                attempts: fast ? 2 : 4,
                timeoutMs: fast ? 400 : 1500,
                ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                return token;
        }
        else if (!allowBrowserUnlock)
        {
            return null;
        }
        else if (!await BridgeUnlockClient.RequestUnlockAsync(
            totalTimeoutMs: fast ? 2000 : 45_000,
            cancellationToken: ct).ConfigureAwait(false))
        {
            return null;
        }
        else
        {
            await WaitForCredentialPipeAsync(
                ct,
                maxAttempts: fast ? 3 : 12,
                delayMs: fast ? 100 : 300).ConfigureAwait(false);
            token = await RequestSessionTokenAsync(
                attempts: fast ? 2 : 4,
                timeoutMs: fast ? 400 : 2000,
                ct).ConfigureAwait(false);
        }

        return token;
    }

    /// <summary>
    /// Phase 5+: token pushed on STATE_CHANGED avoids Fortiva.Bridge.Token_{guid} round-trips.
    /// </summary>
    internal static string? TryGetPushCachedToken(JsonElement request)
    {
        if (request.TryGetProperty("cachedSessionToken", out var cached) &&
            cached.ValueKind == JsonValueKind.String)
        {
            var value = cached.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (request.TryGetProperty("sessionToken", out var session) &&
            session.ValueKind == JsonValueKind.String)
        {
            var value = session.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (!request.TryGetProperty("payload", out var payload))
            return null;

        if (payload.TryGetProperty("cachedSessionToken", out var payloadCached) &&
            payloadCached.ValueKind == JsonValueKind.String)
        {
            var value = payloadCached.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (payload.TryGetProperty("sessionToken", out var payloadSession) &&
            payloadSession.ValueKind == JsonValueKind.String)
        {
            var value = payloadSession.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool IsFastTest =>
        string.Equals(Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST"), "1", StringComparison.Ordinal);

    private static async Task<string> BuildTokenFailureResponseAsync()
    {
        var presence = await BridgePresenceClient.RequestStatusAsync(timeoutMs: 2000).ConfigureAwait(false);
        if (BridgePresenceStatus.IsExplicitlyLocked(presence))
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "locked",
                Message = "Fortiva is open but still locked. Approve Windows Hello or enter your master password in the Fortiva window."
            });
        }

        if (presence is null && !BridgeProcessCheck.IsFortivaRunning())
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva could not be started from the browser. Open Fortiva from the Start menu, unlock, then try Fill again."
            });
        }

        if (BridgeUnlockClient.LastFailureWasRateLimited)
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "rate_limited",
                Message = "Too many unlock attempts from the browser. Wait five minutes, then click Fill again."
            });
        }

        return BridgeJson.Serialize(new CredentialResponse
        {
            Error = "cancelled",
            Message = "Unlock was cancelled or timed out. Click Fill again when you are ready."
        });
    }

    private static async Task<bool> WaitForCredentialPipeAsync(
        CancellationToken ct,
        int maxAttempts = 0,
        int delayMs = 0)
    {
        if (maxAttempts <= 0)
            maxAttempts = IsFastTest ? 4 : 24;
        if (delayMs <= 0)
            delayMs = IsFastTest ? 100 : 500;

        for (var i = 0; i < maxAttempts && !ct.IsCancellationRequested; i++)
        {
            if (BridgeHealthCheck.IsCredentialPipeListening(timeoutMs: 400))
                return true;
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }

        return BridgeHealthCheck.IsCredentialPipeListening(timeoutMs: 1500);
    }

    private static async Task<string> InvokeCredentialPipeAsync(
        JsonElement request,
        string token,
        CancellationToken ct,
        int readSeconds = 0)
    {
        if (readSeconds <= 0)
            readSeconds = CredentialReadSeconds;
        var pipeName = BridgePipeNaming.TryCredentialPipeNameInProcess()
            ?? BridgePipeNaming.TryCredentialPipeName(IsEnterpriseEdition);
        if (pipeName is null)
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva bridge is not active. Unlock Fortiva and try again."
            });
        }

        using var client = new System.IO.Pipes.NamedPipeClientStream(
            ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, ct).ConfigureAwait(false);
        using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8);

        var envelope = new Dictionary<string, object?>
        {
            ["command"] = request.TryGetProperty("command", out var cmd) ? cmd.GetString() : "",
            ["SessionToken"] = token
        };
        if (request.TryGetProperty("payload", out var payload))
            envelope["payload"] = payload;

        var requestLine = JsonSerializer.Serialize(envelope, BridgeJson.Options);
        await writer.WriteLineAsync(requestLine.AsMemory(), ct).ConfigureAwait(false);

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(TimeSpan.FromSeconds(readSeconds));
        var line = await BridgeJson.ReadBoundedLineAsync(reader, readCts.Token).ConfigureAwait(false);
        if (line is null)
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva bridge connection dropped. Wait a moment and click Fill again."
            });
        }

        return BridgeCredentialProtector.UnprotectJsonLine(line, token);
    }

    private static async Task<string?> RequestSessionTokenAsync(int attempts, int timeoutMs, CancellationToken ct)
    {
        for (var attempt = 0; attempt < attempts && !ct.IsCancellationRequested; attempt++)
        {
            var token = await BridgeSessionAuth.RequestTokenFromBrokerAsync(
                timeoutMs: timeoutMs,
                cancellationToken: ct,
                enterprise: IsEnterpriseEdition).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                return token;

            if (attempt < attempts - 1)
                await Task.Delay(120 * (attempt + 1), ct).ConfigureAwait(false);
        }

        return null;
    }

    private static bool ShouldRetryCredentialPipeWithFreshToken(string credentialJson, JsonElement request)
    {
        if (!string.IsNullOrEmpty(TryGetPushCachedToken(request)))
            return CredentialJsonError(credentialJson) is "decrypt_failed";
        return false;
    }

    private static string? CredentialJsonError(string credentialJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(credentialJson);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return err.GetString();
        }
        catch
        {
            /* best effort */
        }

        return null;
    }

    private static JsonElement BuildEnvelope(string command, string domain, string? url)
    {
        var json = JsonSerializer.Serialize(new
        {
            command,
            payload = new { domain, url }
        }, BridgeJson.Options);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildGetEnvelope(string domain, string? url, Guid entryId, string fillNonce)
    {
        var json = JsonSerializer.Serialize(new
        {
            command = "get_credentials",
            payload = new { domain, url, entryId, fillNonce }
        }, BridgeJson.Options);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string MergeFillCredential(string listJson, string getJson)
    {
        try
        {
            using var getDoc = JsonDocument.Parse(getJson);
            var getRoot = getDoc.RootElement;
            var merged = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["status"] = "ready"
            };

            foreach (var prop in getRoot.EnumerateObject())
            {
                if (prop.NameEquals("matches") && getRoot.TryGetProperty("found", out var found) && found.GetBoolean())
                    continue;
                merged[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText(), BridgeJson.Options);
            }

            if (!merged.ContainsKey("matches"))
            {
                using var listDoc = JsonDocument.Parse(listJson);
                if (listDoc.RootElement.TryGetProperty("matches", out var matches))
                    merged["matches"] = JsonSerializer.Deserialize<object>(matches.GetRawText(), BridgeJson.Options);
            }

            return JsonSerializer.Serialize(merged, BridgeJson.Options);
        }
        catch
        {
            return getJson;
        }
    }

    private static string MergeStatusAndCredential(BridgeStatusResponse ping, string credentialJson)
    {
        try
        {
            using var credDoc = JsonDocument.Parse(credentialJson);
            var root = credDoc.RootElement;
            var merged = new Dictionary<string, object?>
            {
                ["ok"] = ping.Ok,
                ["status"] = ping.Status,
                ["message"] = ping.Message
            };

            string? credError = null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                credError = err.GetString();

            if (!string.IsNullOrEmpty(credError))
            {
                merged["error"] = credError;
                if (credError is not "no_match")
                {
                    merged["ok"] = false;
                    merged["status"] = credError switch
                    {
                        "locked" => "locked",
                        "cancelled" => "cancelled",
                        "bridge_warming" => "bridge_warming",
                        _ => "setup_required"
                    };
                    if (root.TryGetProperty("message", out var credMsg) && credMsg.ValueKind == JsonValueKind.String)
                        merged["message"] = credMsg.GetString();
                }
            }

            if (root.TryGetProperty("matches", out var matches))
                merged["matches"] = JsonSerializer.Deserialize<object>(matches.GetRawText(), BridgeJson.Options);
            if (root.TryGetProperty("fillNonce", out var nonce))
                merged["fillNonce"] = nonce.GetString();
            if (root.TryGetProperty("found", out var found))
                merged["found"] = found.GetBoolean();

            return JsonSerializer.Serialize(merged, BridgeJson.Options);
        }
        catch
        {
            return BridgeJson.Serialize(ping);
        }
    }
}
