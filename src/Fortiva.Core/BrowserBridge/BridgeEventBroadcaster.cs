using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Session-scoped push pipe: WinUI broadcasts <see cref="BridgePushMessage"/> lines to connected native hosts.
/// </summary>
public sealed class BridgeEventBroadcaster : IDisposable
{
    private const int ListenerCount = 2;
    private const int MaxPushClients = 16;

    private readonly Func<bool> _isEnterprise;
    private readonly ConcurrentDictionary<int, StreamWriter> _writers = new();
    private CancellationTokenSource? _cts;
    private Task[] _listenTasks = [];
    private int _nextClientId;
    private Func<BridgePresenceSnapshot>? _getSnapshot;
    private bool _disposed;

    public BridgeEventBroadcaster(Func<bool> isEnterprise)
    {
        _isEnterprise = isEnterprise ?? throw new ArgumentNullException(nameof(isEnterprise));
    }

    public void ConfigureSnapshotSource(Func<BridgePresenceSnapshot> getSnapshot)
        => _getSnapshot = getSnapshot;

    public void RestartForCurrentSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sessionId = BridgePipeNaming.ResolveActiveSessionId(_isEnterprise());
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Stop();
            return;
        }

        Stop();
        Start(sessionId);
    }

    private void Start(string sessionId)
    {
        _cts = new CancellationTokenSource();
        var pipeName = $"{BridgePipeNaming.EventPipePrefix}_{sessionId}";
        _listenTasks = Enumerable.Range(0, ListenerCount)
            .Select(_ => Task.Run(() => ListenLoopAsync(pipeName, _cts.Token), _cts.Token))
            .ToArray();
    }

    private async Task ListenLoopAsync(string pipeName, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = BridgePipeListener.CreateSecuredServerStream(pipeName, MaxPushClients);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                if (BridgePipeListener.IsPipeClientValidationEnabled
                    && !BridgePipeGuard.IsAllowedClient(server))
                {
                    try { server.Disconnect(); } catch { /* release instance */ }
                    continue;
                }

                var connected = server;
                server = null;
                _ = Task.Run(async () =>
                {
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    var writer = new StreamWriter(connected, Encoding.UTF8, bufferSize: 4096, leaveOpen: false)
                    {
                        AutoFlush = true
                    };
                    _writers[clientId] = writer;

                    try
                    {
                        await PushCurrentSnapshotAsync().ConfigureAwait(false);
                        await HoldClientUntilDisconnectAsync(connected, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writers.TryRemove(clientId, out _);
                        try { writer.Dispose(); } catch { /* best effort */ }
                        try { connected.Dispose(); } catch { /* best effort */ }
                    }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                server?.Dispose();
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }

    private static async Task HoldClientUntilDisconnectAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var buffer = new byte[1];
        while (!ct.IsCancellationRequested && server.IsConnected)
        {
            try
            {
                var read = await server.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    public async Task BroadcastSnapshotAsync(BridgePresenceSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var json = BridgeJson.Serialize(BridgeSnapshotPush.FromSnapshot(snapshot));
        await BroadcastJsonLineAsync(json).ConfigureAwait(false);
    }

    private async Task PushCurrentSnapshotAsync()
    {
        if (_getSnapshot is null)
            return;

        try
        {
            await BroadcastSnapshotAsync(_getSnapshot()).ConfigureAwait(false);
        }
        catch
        {
            /* best effort */
        }
    }

    public async Task BroadcastJsonLineAsync(string jsonLine)
    {
        foreach (var pair in _writers)
        {
            try
            {
                await pair.Value.WriteLineAsync(jsonLine.AsMemory()).ConfigureAwait(false);
            }
            catch
            {
                _writers.TryRemove(pair.Key, out _);
            }
        }
    }

    public void Stop()
    {
        var cts = _cts;
        var tasks = _listenTasks;
        _cts = null;
        _listenTasks = [];

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { /* best effort */ }
            try { cts.Dispose(); } catch { /* best effort */ }
        }

        foreach (var pair in _writers)
        {
            try { pair.Value.Dispose(); } catch { /* best effort */ }
        }

        _writers.Clear();

        if (tasks.Length > 0)
        {
            try { BridgePipeListener.WaitAllAsync(tasks, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
            catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}
