namespace Fortiva.AppHost.Services;

/// <summary>Navigation hint — browser extension requested vault unlock.</summary>
public sealed class BridgeUnlockNavigationContext
{
    public static BridgeUnlockNavigationContext Instance { get; } = new();
    private BridgeUnlockNavigationContext() { }
}
