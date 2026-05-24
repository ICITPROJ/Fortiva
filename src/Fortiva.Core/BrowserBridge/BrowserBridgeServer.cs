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
}

public sealed class CredentialResponse
{
    public bool Found { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? PasskeyCredentialId { get; set; }
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

    private static readonly JsonSerializerOptions BridgeJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<CredentialRequest, CredentialResponse> _credentialResolver;
    private readonly string _sessionToken;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public BrowserBridgeServer(Func<CredentialRequest, CredentialResponse> credentialResolver, string sessionToken)
    {
        _credentialResolver = credentialResolver;
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
        var line = await reader.ReadLineAsync(ct);
        if (line is null) return;

        var msg = JsonSerializer.Deserialize<BrowserBridgeMessage>(line, BridgeJson);
        if (msg is null || !IsAuthorized(msg))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new CredentialResponse()).AsMemory(), ct);
            return;
        }

        if (msg.Command == "get_credentials" && msg.Payload.HasValue)
        {
            var req = JsonSerializer.Deserialize<CredentialRequest>(msg.Payload.Value.GetRawText(), BridgeJson);
            if (req is not null)
            {
                var resp = _credentialResolver(req);
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp).AsMemory(), ct);
                return;
            }
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(new CredentialResponse()).AsMemory(), ct);
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
