using System.IO.Pipes;

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

    public bool UsernameProtected { get; set; }

    public string? UsernameSealed { get; set; }

    /// <summary>False when listed via registrable-domain match but password release requires exact host.</summary>
    public bool Releasable { get; set; } = true;

}



public sealed class CredentialResponse

{

    public bool Found { get; set; }

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>True when <see cref="Password"/> was sealed for pipe transport — see <see cref="PasswordSealed"/>.</summary>
    public bool PasswordProtected { get; set; }

    /// <summary>AES-GCM sealed password blob (base64). Cleared after native host decrypts.</summary>
    public string? PasswordSealed { get; set; }

    /// <summary>True when <see cref="Username"/> was sealed for pipe transport — see <see cref="UsernameSealed"/>.</summary>
    public bool UsernameProtected { get; set; }

    /// <summary>AES-GCM sealed username blob (base64). Cleared after native host decrypts.</summary>
    public string? UsernameSealed { get; set; }

    public string? Title { get; set; }

    public string? PasskeyCredentialId { get; set; }

    public string? Error { get; set; }

    public string? Message { get; set; }

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

    private const int ListenerCount = 4;



    private readonly Func<CredentialRequest, CredentialResponse> _credentialResolver;

    private readonly Func<CredentialRequest, CredentialResponse> _matchLister;

    private readonly string _sessionToken;

    private CancellationTokenSource? _cts;

    private Task[] _listenTasks = [];



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

        _listenTasks = BridgePipeListener.Start(
            PipeName,
            ListenerCount,
            maxInstances: 8,
            HandleClientAsync,
            _cts.Token,
            validateClients: true);
    }



    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)

    {

        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);

        using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        readCts.CancelAfter(TimeSpan.FromSeconds(10));

        var line = await BridgeJson.ReadBoundedLineAsync(reader, readCts.Token);

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

                    var resolved = _credentialResolver(req);
                    var protectedResponse = BridgeCredentialProtector.ProtectForPipe(resolved, _sessionToken);
                    await writer.WriteLineAsync(BridgeJson.Serialize(protectedResponse).AsMemory(), ct);

                    return;

                }



                if (msg.Command == "list_credentials")

                {

                    var response = _matchLister(req);
                    var protectedList = BridgeCredentialProtector.ProtectListForPipe(response, _sessionToken);

                    await writer.WriteLineAsync(BridgeJson.Serialize(protectedList).AsMemory(), ct);

                    return;

                }

            }

        }



        await writer.WriteLineAsync(
            BridgeJson.Serialize(new CredentialResponse { Error = "unknown_command" }).AsMemory(), ct);

    }



    private bool IsAuthorized(BrowserBridgeMessage msg)

    {

        if (string.IsNullOrEmpty(msg.SessionToken)) return false;

        return BridgeSessionAuth.FixedTimeEqualsUtf8(msg.SessionToken, _sessionToken);

    }



    public void Dispose() => Dispose(waitForListeners: false);

    internal void DisposeBlocking() => Dispose(waitForListeners: true);

    private void Dispose(bool waitForListeners)
    {
        if (_cts is null)
            return;

        var cts = _cts;
        var tasks = _listenTasks;
        _cts = null;
        _listenTasks = [];
        BridgePipeListener.ShutdownListeners(cts, tasks, waitForListeners);
    }

}


