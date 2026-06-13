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

/// <summary>Atomic vault + bridge state for STATUS responses (read under VaultSession gate).</summary>
public readonly record struct BridgePresenceSnapshot(bool VaultExists, bool Unlocked, bool BridgeReady);
