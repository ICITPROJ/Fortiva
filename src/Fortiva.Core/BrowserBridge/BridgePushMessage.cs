namespace Fortiva.Core.BrowserBridge;

/// <summary>Push payload streamed to extension via native host stdout (Phase 4).</summary>
public sealed class BridgePushMessage
{
    public string Type { get; init; } = "STATE_CHANGED";
    public string State { get; init; } = nameof(BridgeReadyState.Uninitialized);
    public bool VaultExists { get; init; }
    public bool IsVaultUnlocked { get; init; }
    public string? CachedSessionToken { get; init; }
    public DateTime Timestamp { get; init; }
    /// <summary>Popup-compatible ping fields (derived from <see cref="State"/>).</summary>
    public bool Ok { get; init; }
    public string Status { get; init; } = "setup_required";
    public string? Message { get; init; }
}

/// <summary>Maps coordinator snapshots to extension push + ping status.</summary>
public static class BridgeSnapshotPush
{
    public static BridgePushMessage FromSnapshot(BridgePresenceSnapshot snapshot)
    {
        var status = MapPingStatus(snapshot);
        return new BridgePushMessage
        {
            Type = "STATE_CHANGED",
            State = snapshot.State.ToString(),
            VaultExists = snapshot.VaultExists,
            IsVaultUnlocked = snapshot.IsVaultUnlocked,
            CachedSessionToken = snapshot.CachedSessionToken,
            Timestamp = snapshot.Timestamp,
            Ok = status == "ready",
            Status = status,
            Message = MapMessage(snapshot, status)
        };
    }

    public static string MapPingStatus(BridgePresenceSnapshot snapshot)
    {
        if (!snapshot.VaultExists)
            return "setup_required";

        return snapshot.State switch
        {
            BridgeReadyState.Unlocked when snapshot.BridgeReady => "ready",
            BridgeReadyState.Locked => "locked",
            BridgeReadyState.DeployingSidecars or BridgeReadyState.StartingInfrastructure
                or BridgeReadyState.AwaitingHostConnection => "bridge_warming",
            BridgeReadyState.Unlocked => "bridge_warming",
            BridgeReadyState.Faulted => "setup_required",
            _ => snapshot.IsVaultUnlocked ? "bridge_warming" : "locked"
        };
    }

    private static string MapMessage(BridgePresenceSnapshot snapshot, string status) =>
        status switch
        {
            "ready" => "Fortiva is unlocked and ready.",
            "locked" => "Click Fill — Fortiva will open or ask for Windows Hello or your master password.",
            "bridge_warming" => "Fortiva is starting the bridge. Wait a moment, then click Fill again.",
            _ => "Run Connect browser in Fortiva Settings."
        };
}
