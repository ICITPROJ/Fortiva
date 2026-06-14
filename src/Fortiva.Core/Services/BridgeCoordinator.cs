using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Services;

/// <summary>
/// Centralizes bridge lifecycle writes. Chromium spawns native hosts; WinUI owns pipes, hashes, and state.
/// </summary>
public sealed class BridgeCoordinator : IBridgeCoordinator
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Func<VaultSession?> _getSession;
    private readonly Func<bool> _vaultExists;
    private readonly Func<bool> _isEnterprise;
    private readonly Func<string> _getInstallRoot;
    private readonly BridgeEventBroadcaster _eventBroadcaster;
    private BridgeReadyState _currentState = BridgeReadyState.Uninitialized;
    private bool _disposed;

    public BridgeCoordinator(
        Func<VaultSession?> getSession,
        Func<bool> vaultExists,
        Func<bool> isEnterprise,
        Func<string> getInstallRoot)
    {
        _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
        _vaultExists = vaultExists ?? throw new ArgumentNullException(nameof(vaultExists));
        _isEnterprise = isEnterprise ?? throw new ArgumentNullException(nameof(isEnterprise));
        _getInstallRoot = getInstallRoot ?? throw new ArgumentNullException(nameof(getInstallRoot));
        _eventBroadcaster = new BridgeEventBroadcaster(_isEnterprise);
        _eventBroadcaster.ConfigureSnapshotSource(GetAuthoritativeSnapshot);
    }

    public BridgeReadyState CurrentState
    {
        get
        {
            lock (_stateLock)
                return _currentState;
        }
    }

    public event Action<BridgeReadyState>? ReadyStateChanged;

    public event Action? SessionRotated;

    public async Task ReconcileLifecycleAsync(string triggerReason, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await _lifecycleLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            FortivaDiagnosticLog.Write(
                "BridgeCoordinator",
                new InvalidOperationException(
                    $"Lifecycle reconciliation skipped — another operation is in progress. Trigger: {triggerReason}"));
            return;
        }

        try
        {
            var lightweight = IsHealthOnlyTrigger(triggerReason);
            var rotateSession = ShouldRotateSession(triggerReason);

            if (!lightweight)
            {
                SetState(BridgeReadyState.DeployingSidecars);
                VerifyAndRepairHashSidecars();
            }

            SetState(BridgeReadyState.StartingInfrastructure);
            if (rotateSession)
            {
                BridgePipeNaming.RotateSessionId(_isEnterprise());
                _eventBroadcaster.RestartForCurrentSession();
                SessionRotated?.Invoke();
                BridgeHostProcessCleanup.StopAllHosts();
            }

            var session = _getSession();
            var isUnlocked = session?.IsUnlocked ?? false;
            var hardRestart = triggerReason.Contains("Restart", StringComparison.OrdinalIgnoreCase);

            bool infrastructureHealthy;
            if (isUnlocked && session is not null)
            {
                infrastructureHealthy = await Task.Run(() =>
                {
                    if (hardRestart)
                    {
                        session.RestartBridgeInfrastructure();
                        return session.IsBridgeHealthy();
                    }

                    if (lightweight)
                        return session.IsBridgeHealthy() || session.EnsureBridgeInfrastructureHealthy();

                    return session.EnsureBridgeInfrastructureHealthy();
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                infrastructureHealthy = true;
            }

            if (!infrastructureHealthy)
            {
                SetState(BridgeReadyState.Faulted);
                return;
            }

            if (!_vaultExists())
            {
                SetState(BridgeReadyState.Uninitialized);
                return;
            }

            if (!isUnlocked)
            {
                SetState(BridgeReadyState.Locked);
                return;
            }

            if (session!.IsBridgeHealthy())
                SetState(BridgeReadyState.Unlocked);
            else
                SetState(BridgeReadyState.AwaitingHostConnection);
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeCoordinator.Reconcile", ex);
            SetState(BridgeReadyState.Faulted);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public BridgePresenceSnapshot GetAuthoritativeSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = _getSession();
        var vaultExists = _vaultExists();
        var isUnlocked = session?.IsUnlocked ?? false;
        var token = isUnlocked ? session?.GetActiveSessionToken() : null;
        var state = CurrentState;

        if (state is BridgeReadyState.Uninitialized or BridgeReadyState.Faulted)
        {
            if (!vaultExists)
                state = BridgeReadyState.Uninitialized;
            else if (!isUnlocked)
                state = BridgeReadyState.Locked;
            else if (session?.IsBridgeHealthy() == true)
                state = BridgeReadyState.Unlocked;
            else if (isUnlocked)
                state = BridgeReadyState.AwaitingHostConnection;
        }

        return new BridgePresenceSnapshot(state, vaultExists, isUnlocked, token, DateTime.UtcNow);
    }

    public void NotifyVaultLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetState(_vaultExists() ? BridgeReadyState.Locked : BridgeReadyState.Uninitialized);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _eventBroadcaster.Dispose();
        _lifecycleLock.Dispose();
    }

    private void SetState(BridgeReadyState state)
    {
        BridgeReadyState previous;
        lock (_stateLock)
        {
            if (_currentState == state)
                return;
            previous = _currentState;
            _currentState = state;
        }

        if (previous != state)
        {
            ReadyStateChanged?.Invoke(state);
            _ = PushStateToConnectedHostsAsync();
        }
    }

    private async Task PushStateToConnectedHostsAsync()
    {
        try
        {
            await _eventBroadcaster.BroadcastSnapshotAsync(GetAuthoritativeSnapshot()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FortivaDiagnosticLog.Write("BridgeCoordinator.PushState", ex);
        }
    }

    private static bool IsHealthOnlyTrigger(string triggerReason) =>
        string.Equals(triggerReason, "Watchdog", StringComparison.OrdinalIgnoreCase)
        || string.Equals(triggerReason, "WindowActivated", StringComparison.OrdinalIgnoreCase)
        || triggerReason.Contains("Health", StringComparison.OrdinalIgnoreCase);

    private bool ShouldRotateSession(string triggerReason)
    {
        if (!BridgePipeNaming.HasActiveSession(_isEnterprise()))
            return true;

        if (triggerReason.Contains("Restart", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void VerifyAndRepairHashSidecars()
    {
        var root = _getInstallRoot();
        if (string.IsNullOrWhiteSpace(root))
            return;

        try
        {
            BrowserBridgeInstallService.EnsureInstalled(root, _isEnterprise());
        }
        catch
        {
            /* best effort — Connect browser / deploy may repair later */
        }

        try
        {
            BrowserBridgeInstallService.RepairNativeHostIfStale(root, _isEnterprise());
        }
        catch
        {
            /* best effort */
        }

        var bridgeExe = Path.Combine(root, "BrowserBridge", BridgeClientValidator.BridgeHostExecutableName);
        if (File.Exists(bridgeExe))
        {
            try { BridgeInstallIntegrity.RecordBridgeHostHash(bridgeExe); }
            catch { /* best effort */ }
        }
    }
}
