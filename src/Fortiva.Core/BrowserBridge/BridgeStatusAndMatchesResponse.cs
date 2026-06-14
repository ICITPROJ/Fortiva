namespace Fortiva.Core.BrowserBridge;

/// <summary>Unified native-messaging response for extension status + credential matches.</summary>
public sealed class BridgeStatusAndMatchesResponse
{
    public BridgeStatusBlock Status { get; set; } = new();

    public IReadOnlyList<BridgeMatchSummary> Matches { get; set; } = Array.Empty<BridgeMatchSummary>();

    /// <summary>Present when matches were listed successfully; required for execute_fill.</summary>
    public string? FillNonce { get; set; }
}

public sealed class BridgeStatusBlock
{
    public bool AppRunning { get; set; }

    public bool VaultUnlocked { get; set; }

    /// <summary>null, vault_locked, token_stale, host_unreachable, internal_error</summary>
    public string? Error { get; set; }
}

public sealed class BridgeMatchSummary
{
    public string Id { get; set; } = "";

    public string Username { get; set; } = "";

    public string Url { get; set; } = "";

    public int Score { get; set; }

    public string? Title { get; set; }

    public bool Releasable { get; set; } = true;
}
