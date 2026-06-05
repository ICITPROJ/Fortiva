using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Fortiva.Core.BrowserBridge;

public sealed class CredentialRequest
{
    public string Domain { get; set; } = "";
    public string? Url { get; set; }
    public Guid? EntryId { get; set; }
    public string? FillNonce { get; set; }
}

public sealed class CredentialMatchSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
}

public sealed class CredentialResponse
{
    public bool Found { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Title { get; set; }
    public string? PasskeyCredentialId { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<CredentialMatchSummary>? Matches { get; set; }
    public string? FillNonce { get; set; }
}

public sealed class BridgeStatusResponse
{
    public bool Ok { get; set; }
    public string Status { get; set; } = "setup_required";
    public string? Message { get; set; }
}

public sealed class BrowserBridgeMessage
{
    public string Command { get; set; } = "";
    public string? SessionToken { get; set; }
    public JsonElement? Payload { get; set; }
}

/// <summary>
/// Local-only named pipe server for browser extension. Requires per-unlock session token.
/// Pipe: \\.\pipe\Fortiva.BrowserBridge
/// </summary>
public sealed class BrowserBridgeServer : IDisposable
{
    public const string PipeName = "Fortiva.BrowserBridge";

    private readonly Func<CredentialRequest, CredentialResponse> _credentialResolver;
    private readonly Func<CredentialRequest, CredentialResponse> _matchLister;
    private readonly string _sessionToken;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public BrowserBridgeServer(
        Func<CredentialRequest, CredentialResponse> credentialResolver,
        Func<CredentialRequest, CredentialResponse> matchLister,
        string sessionToken)
    {
        _credentialResolver = credentialResolver;
        _matchLister = matchLister;
        _sessionToken = sessionToken;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var server = CreateSecuredServerStream();
            try
            {
                await server.WaitForConnectionAsync(ct);
                if (!BridgePipeGuard.IsAllowedClient(server))
                    continue;
                await HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* continue listening */ }
        }
    }

    private static NamedPipeServerStream CreateSecuredServerStream()
    {
        var pipeSecurity = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve current user SID for pipe ACL.");
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        var line = await BridgeJson.ReadBoundedLineAsync(reader, ct);
        if (string.IsNullOrEmpty(line)) return;

        var msg = BridgeJson.Deserialize<BrowserBridgeMessage>(line);
        if (msg is null || !IsAuthorized(msg))
        {
            await writer.WriteLineAsync(BridgeJson.Serialize(new CredentialResponse { Error = "locked" }).AsMemory(), ct);
            return;
        }

        if (msg.Payload.HasValue)
        {
            var req = BridgeJson.Deserialize<CredentialRequest>(msg.Payload.Value.GetRawText());
            if (req is not null)
            {
                if (msg.Command == "get_credentials")
                {
                    await writer.WriteLineAsync(BridgeJson.Serialize(_credentialResolver(req)).AsMemory(), ct);
                    return;
                }

                if (msg.Command == "list_credentials")
                {
                    var response = _matchLister(req);
                    await writer.WriteLineAsync(BridgeJson.Serialize(response).AsMemory(), ct);
                    return;
                }
            }
        }

        await writer.WriteLineAsync(BridgeJson.Serialize(new CredentialResponse()).AsMemory(), ct);
    }

    private bool IsAuthorized(BrowserBridgeMessage msg)
    {
        if (string.IsNullOrEmpty(msg.SessionToken)) return false;
        return BridgeSessionAuth.FixedTimeEqualsUtf8(msg.SessionToken, _sessionToken);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
