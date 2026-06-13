using System.IO.Pipes;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Serves the per-unlock bridge session token over a secured named pipe.
/// Token is held in process memory only — not written to disk.
/// </summary>
public sealed class BridgeTokenBroker : IDisposable
{
    public const string PipeName = "Fortiva.Bridge.Token";
    private const int ListenerCount = 4;

    private readonly string _sessionToken;
    private readonly bool _enterprise;
    private CancellationTokenSource? _cts;
    private Task[] _listenTasks = [];

    public BridgeTokenBroker(string sessionToken, bool enterprise = false)
    {
        _sessionToken = sessionToken;
        _enterprise = enterprise;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTasks = BridgePipeListener.Start(
            BridgePipeNaming.TokenPipeName(_enterprise),
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
        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromMilliseconds(2500));
            var line = await BridgeJson.ReadBoundedLineAsync(reader, readCts.Token);
            if (string.Equals(line?.Trim(), "GET", StringComparison.OrdinalIgnoreCase))
                await writer.WriteLineAsync(_sessionToken.AsMemory(), ct);
            else
                await writer.WriteLineAsync("".AsMemory(), ct);
        }
        catch
        {
            try { await writer.WriteLineAsync("".AsMemory(), CancellationToken.None); } catch { /* client gone */ }
        }
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
