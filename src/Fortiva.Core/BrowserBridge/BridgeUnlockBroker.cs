using System.IO.Pipes;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Listens while Fortiva is running (locked or unlocked). Browser bridge host sends UNLOCK
/// to foreground the app and wait for master password / Windows Hello, or STATUS for presence.
/// Pipe: \\.\pipe\Fortiva.Bridge.UnlockRequest
/// </summary>
public sealed class BridgeUnlockBroker : IDisposable
{
    public const string PipeName = "Fortiva.Bridge.UnlockRequest";
    private const int ListenerCount = 4;
    private const int MaxUnlockRequestsPerWindow = 8;
    private static readonly TimeSpan UnlockRateLimitWindow = TimeSpan.FromMinutes(5);
    private readonly Func<BridgePresenceSnapshot> _getPresence;
    private readonly Func<CancellationToken, Task<bool>> _requestUnlock;
    private readonly BridgeUnlockRateLimiter _rateLimiter = new();
    private CancellationTokenSource? _cts;
    private Task[] _listenTasks = [];

    public BridgeUnlockBroker(
        Func<BridgePresenceSnapshot> getPresence,
        Func<CancellationToken, Task<bool>> requestUnlock)
    {
        _getPresence = getPresence;
        _requestUnlock = requestUnlock;
    }

    /// <summary>Legacy ctor for tests — prefer <see cref="BridgeUnlockBroker(Func{BridgePresenceSnapshot}, Func{CancellationToken, Task{bool}})"/>.</summary>
    public BridgeUnlockBroker(
        Func<bool> isUnlocked,
        Func<bool> vaultExists,
        Func<CancellationToken, Task<bool>> requestUnlock,
        Func<bool>? isBridgeReady = null)
        : this(
            () => BridgePresenceSnapshot.FromLegacy(
                vaultExists(),
                isUnlocked(),
                isBridgeReady?.Invoke() ?? false),
            requestUnlock)
    {
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
        var authenticated = BridgePipeGuard.IsAllowedClient(server);
        var response = authenticated ? BuildStatusResponse() : BridgePresenceStatus.Unknown;
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            string? line = null;
            try
            {
                line = await BridgeJson.ReadBoundedLineAsync(reader, readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Connect-only probe — refresh presence at write time.
            }

            var command = line?.Trim();
            if (!authenticated)
            {
                response = BridgePresenceStatus.Unknown;
            }
            else if (command is not null && string.Equals(command, "UNLOCK", StringComparison.OrdinalIgnoreCase))
            {
                response = await ProcessUnlockRequestAsync(command, CancellationToken.None);
            }
            else
            {
                response = BuildStatusResponse();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch { /* BuildStatusResponse fallback */ }

        try
        {
            using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(response.AsMemory(), CancellationToken.None);
        }
        catch { /* client disconnected */ }
    }

    /// <summary>Core bridge protocol (testable without named pipes).</summary>
    internal Task<string> ProcessRequestAsync(string? requestLine, CancellationToken ct)
    {
        if (requestLine is null)
            return Task.FromResult("INVALID");

        var command = requestLine.Trim();
        if (string.Equals(command, "STATUS", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BuildStatusResponse());

        return ProcessUnlockRequestAsync(command, ct);
    }

    internal string BuildStatusResponse()
    {
        var snapshot = _getPresence();
        if (!snapshot.VaultExists)
            return BridgePresenceStatus.NoVault;
        if (!snapshot.Unlocked)
            return BridgePresenceStatus.Locked;

        return snapshot.BridgeReady
            ? BridgePresenceStatus.UnlockedBridgeReady
            : BridgePresenceStatus.UnlockedBridgeDown;
    }

    /// <summary>Unlock flow invoked from <see cref="ProcessRequestAsync"/>.</summary>
    internal async Task<string> ProcessUnlockRequestAsync(string requestLine, CancellationToken ct)
    {
        if (!string.Equals(requestLine, "UNLOCK", StringComparison.OrdinalIgnoreCase))
            return "INVALID";

        if (!_rateLimiter.TryAllow())
            return "RATE_LIMITED";

        var snapshot = _getPresence();
        if (!snapshot.VaultExists)
            return "NO_VAULT";

        if (snapshot.Unlocked)
            return "ALREADY_UNLOCKED";

        using var unlockCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        unlockCts.CancelAfter(TimeSpan.FromSeconds(90));
        var ok = await _requestUnlock(unlockCts.Token);
        return ok ? "OK" : "FAILED";
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

    internal sealed class BridgeUnlockRateLimiter
    {
        private readonly object _gate = new();
        private readonly Queue<DateTimeOffset> _requests = new();

        public bool TryAllow()
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                while (_requests.Count > 0 && now - _requests.Peek() > UnlockRateLimitWindow)
                    _requests.Dequeue();

                if (_requests.Count >= MaxUnlockRequestsPerWindow)
                    return false;

                _requests.Enqueue(now);
                return true;
            }
        }
    }
}
