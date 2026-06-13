namespace Fortiva.Core.BrowserBridge;

/// <summary>STATUS line protocol on Fortiva.Bridge.UnlockRequest (plain text, one line).</summary>
public static class BridgePresenceStatus
{
    public const string NoVault = "NO_VAULT";
    public const string Locked = "LOCKED";
    public const string UnlockedBridgeReady = "UNLOCKED|BRIDGE_READY";
    public const string UnlockedBridgeDown = "UNLOCKED|BRIDGE_DOWN";
    public const string Unknown = "UNKNOWN";

    public static bool IsExplicitlyLocked(string? status)
        => string.Equals(status, Locked, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnlocked(string? status)
        => status?.StartsWith("UNLOCKED", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsBridgeReady(string? status)
        => string.Equals(status, UnlockedBridgeReady, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Atomic bridge readiness bundle for STATUS, coordinator observability, and Phase 5 token push.
/// </summary>
public readonly record struct BridgePresenceSnapshot(
    BridgeReadyState State,
    bool VaultExists,
    bool IsVaultUnlocked,
    string? CachedSessionToken,
    DateTime Timestamp)
{
    public bool Unlocked => IsVaultUnlocked;

    public bool BridgeReady =>
        State == BridgeReadyState.Unlocked
        && IsVaultUnlocked
        && !string.IsNullOrEmpty(CachedSessionToken);

    public static BridgePresenceSnapshot NoSession(bool vaultExists) =>
        vaultExists
            ? new(BridgeReadyState.Locked, true, false, null, DateTime.UtcNow)
            : new(BridgeReadyState.Uninitialized, false, false, null, DateTime.UtcNow);

    public static BridgePresenceSnapshot FromLegacy(bool vaultExists, bool unlocked, bool bridgeReady)
    {
        if (!vaultExists)
            return NoSession(false);
        if (!unlocked)
            return NoSession(true);
        var state = bridgeReady ? BridgeReadyState.Unlocked : BridgeReadyState.AwaitingHostConnection;
        return new(state, true, true, bridgeReady ? "legacy" : null, DateTime.UtcNow);
    }
}
