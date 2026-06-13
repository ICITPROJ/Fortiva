namespace Fortiva.Core.BrowserBridge;

/// <summary>Explicit bridge lifecycle state — single source of truth for readiness (Phase 1).</summary>
public enum BridgeReadyState
{
    Uninitialized,
    DeployingSidecars,
    StartingInfrastructure,
    AwaitingHostConnection,
    Locked,
    Unlocked,
    Faulted
}
