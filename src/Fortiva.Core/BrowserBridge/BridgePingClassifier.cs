namespace Fortiva.Core.BrowserBridge;

/// <summary>Maps token + unlock-pipe presence to native-host ping status (testable, no I/O).</summary>
public static class BridgePingClassifier
{
    public static BridgeStatusResponse Classify(string? sessionToken, string? presence, bool? fortivaRunning = null)
    {
        var fortivaUp = fortivaRunning ?? BridgeProcessCheck.IsFortivaRunning();
        if (!string.IsNullOrEmpty(sessionToken))
        {
            return new BridgeStatusResponse
            {
                Ok = true,
                Status = "ready",
                Message = "Fortiva is unlocked and ready."
            };
        }

        if (presence is null)
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = fortivaUp ? "bridge_warming" : "setup_required",
                Message = fortivaUp
                    ? "Fortiva is starting — wait a moment, then click Fill again."
                    : "Click Fill — Fortiva will open and ask you to unlock."
            };
        }

        if (string.Equals(presence, BridgePresenceStatus.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "bridge_warming",
                Message = "Fortiva is starting — wait a moment, then click Fill again."
            };
        }

        if (string.Equals(presence, BridgePresenceStatus.NoVault, StringComparison.OrdinalIgnoreCase))
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "setup_required",
                Message = "Create or open your Fortiva vault on this PC first."
            };
        }

        if (BridgePresenceStatus.IsExplicitlyLocked(presence))
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "locked",
                Message = "Click Fill — Fortiva will open or ask for Windows Hello or your master password."
            };
        }

        if (BridgePresenceStatus.IsUnlocked(presence) && !BridgePresenceStatus.IsBridgeReady(presence))
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "bridge_warming",
                Message = "Fortiva is unlocked — the bridge is starting. Wait a moment, then try again."
            };
        }

        if (BridgePresenceStatus.IsBridgeReady(presence))
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "bridge_warming",
                Message = "Fortiva is unlocked — finishing bridge setup. Wait a moment, then try again."
            };
        }

        return new BridgeStatusResponse
        {
            Ok = false,
            Status = "setup_required",
            Message = "Run Connect browser in Fortiva Settings."
        };
    }
}
