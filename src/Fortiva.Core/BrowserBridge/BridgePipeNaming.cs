namespace Fortiva.Core.BrowserBridge;

/// <summary>Session-scoped named pipe addresses (Phase 3 — eliminates global pipe wedge).</summary>
public static class BridgePipeNaming
{
    public const string CredentialPipePrefix = "Fortiva.BrowserBridge";
    public const string TokenPipePrefix = "Fortiva.Bridge.Token";
    public const string UnlockPipePrefix = "Fortiva.Bridge.UnlockRequest";
    public const string EventPipePrefix = "Fortiva.Bridge.Events";

    private static string? _inProcessSessionId;

    internal static string? InProcessSessionId => _inProcessSessionId;

    internal static void SetInProcessSessionId(string? sessionId)
        => _inProcessSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;

    public static string? TryCredentialPipeNameInProcess()
        => TrySuffix(CredentialPipePrefix, _inProcessSessionId);

    public static string? TryTokenPipeNameInProcess()
        => TrySuffix(TokenPipePrefix, _inProcessSessionId);

    public static string? TryUnlockPipeNameInProcess()
        => TrySuffix(UnlockPipePrefix, _inProcessSessionId);

    public static string? TryEventPipeNameInProcess()
        => TrySuffix(EventPipePrefix, _inProcessSessionId);

    /// <summary>Rotates session id, persists to registry, and returns the new id.</summary>
    public static string RotateSessionId(bool enterprise)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        BridgeSessionRegistry.WriteActiveSessionId(sessionId, enterprise);
        return sessionId;
    }

    public static string? ResolveActiveSessionId(bool enterprise)
        => BridgeSessionRegistry.ReadActiveSessionId(enterprise);

    public static bool HasActiveSession(bool enterprise)
        => !string.IsNullOrWhiteSpace(ResolveActiveSessionId(enterprise));

    public static string CredentialPipeName(bool enterprise)
        => Suffix(CredentialPipePrefix, RequireSessionId(enterprise));

    public static string TokenPipeName(bool enterprise)
        => Suffix(TokenPipePrefix, RequireSessionId(enterprise));

    public static string UnlockPipeName(bool enterprise)
        => Suffix(UnlockPipePrefix, RequireSessionId(enterprise));

    public static string EventPipeName(bool enterprise)
        => Suffix(EventPipePrefix, RequireSessionId(enterprise));

    /// <summary>Resolves pipe name for clients; returns null when no active session (host should not wedge).</summary>
    public static string? TryCredentialPipeName(bool enterprise)
        => TrySuffix(CredentialPipePrefix, ResolveActiveSessionId(enterprise));

    public static string? TryTokenPipeName(bool enterprise)
        => TrySuffix(TokenPipePrefix, ResolveActiveSessionId(enterprise));

    public static string? TryUnlockPipeName(bool enterprise)
        => TrySuffix(UnlockPipePrefix, ResolveActiveSessionId(enterprise));

    public static string? TryEventPipeName(bool enterprise)
        => TrySuffix(EventPipePrefix, ResolveActiveSessionId(enterprise));

    private static string RequireSessionId(bool enterprise)
    {
        var id = ResolveActiveSessionId(enterprise)
            ?? throw new InvalidOperationException("No active bridge session id. Reconcile bridge lifecycle first.");
        return id;
    }

    private static string Suffix(string prefix, string sessionId) => $"{prefix}_{sessionId}";

    private static string? TrySuffix(string prefix, string? sessionId)
        => string.IsNullOrWhiteSpace(sessionId) ? null : Suffix(prefix, sessionId);
}
