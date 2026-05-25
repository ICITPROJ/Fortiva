using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Listens while Fortiva is running (locked or unlocked). Browser bridge host sends UNLOCK
/// to foreground the app and wait for master password / Windows Hello.
/// Pipe: \\.\pipe\Fortiva.Bridge.UnlockRequest
/// </summary>
public sealed class BridgeUnlockBroker : IDisposable
{
    public const string PipeName = "Fortiva.Bridge.UnlockRequest";
    private const int MaxUnlockRequestsPerWindow = 8;
    private static readonly TimeSpan UnlockRateLimitWindow = TimeSpan.FromMinutes(5);

    private readonly Func<bool> _isUnlocked;
    private readonly Func<bool> _vaultExists;
    private readonly Func<CancellationToken, Task<bool>> _requestUnlock;
    private readonly BridgeUnlockRateLimiter _rateLimiter = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public BridgeUnlockBroker(
        Func<bool> isUnlocked,
        Func<bool> vaultExists,
        Func<CancellationToken, Task<bool>> requestUnlock)
    {
        _isUnlocked = isUnlocked;
        _vaultExists = vaultExists;
        _requestUnlock = requestUnlock;
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

    public async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        var response = await ProcessUnlockRequestAsync(line, ct);
        await writer.WriteLineAsync(response.AsMemory(), ct);
    }

    /// <summary>Core unlock protocol (testable without named pipes).</summary>
    internal async Task<string> ProcessUnlockRequestAsync(string? requestLine, CancellationToken ct)
    {
        if (requestLine is null || !string.Equals(requestLine.Trim(), "UNLOCK", StringComparison.OrdinalIgnoreCase))
            return "INVALID";

        if (!_rateLimiter.TryAllow())
            return "RATE_LIMITED";

        if (!_vaultExists())
            return "NO_VAULT";

        if (_isUnlocked())
            return "ALREADY_UNLOCKED";

        using var unlockCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        unlockCts.CancelAfter(TimeSpan.FromMinutes(2));
        var ok = await _requestUnlock(unlockCts.Token);
        return ok ? "OK" : "FAILED";
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

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
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
