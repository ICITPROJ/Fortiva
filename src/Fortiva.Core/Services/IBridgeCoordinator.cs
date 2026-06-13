using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Services;

/// <summary>
/// Exclusive write-gate for browser bridge lifecycle (orphan cleanup, pipe infrastructure, state transitions).
/// Browser-spawned native hosts are delegated workers; only this coordinator mutates infrastructure.
/// </summary>
public interface IBridgeCoordinator : IDisposable
{
    BridgeReadyState CurrentState { get; }

    /// <summary>Fires when <see cref="CurrentState"/> changes.</summary>
    event Action<BridgeReadyState>? ReadyStateChanged;

    /// <summary>
    /// Single authoritative heal path — replaces scattered watchdog and UI reconciliation loops.
    /// </summary>
    Task ReconcileLifecycleAsync(string triggerReason, CancellationToken cancellationToken = default);

    /// <summary>Authoritative read for STATUS, UI, and tests (includes cached session token when unlocked).</summary>
    BridgePresenceSnapshot GetAuthoritativeSnapshot();

    /// <summary>Fast path when vault locks without a full infrastructure cycle.</summary>
    void NotifyVaultLocked();
}
