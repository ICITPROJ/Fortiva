using Microsoft.Win32;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Discovery service for the active bridge pipe session. WinUI writes; native host reads.
/// HKCU\Software\ICITPROJ\Fortiva\{Personal|Enterprise}\ActiveBridgeSessionId
/// </summary>
public static class BridgeSessionRegistry
{
    private const string RegistryRoot = @"Software\ICITPROJ\Fortiva";

    public static void WriteActiveSessionId(string sessionId, bool enterprise)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session id is required.", nameof(sessionId));

        using var key = Registry.CurrentUser.CreateSubKey(GetEditionKeyPath(enterprise));
        key.SetValue("ActiveBridgeSessionId", sessionId, RegistryValueKind.String);
        BridgePipeNaming.SetInProcessSessionId(sessionId);
    }

    public static string? ReadActiveSessionId(bool enterprise)
    {
        var inProcess = BridgePipeNaming.InProcessSessionId;
        if (!string.IsNullOrWhiteSpace(inProcess))
            return inProcess;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GetEditionKeyPath(enterprise));
            return key?.GetValue("ActiveBridgeSessionId") as string;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearActiveSessionId(bool enterprise)
    {
        BridgePipeNaming.SetInProcessSessionId(null);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GetEditionKeyPath(enterprise), writable: true);
            key?.DeleteValue("ActiveBridgeSessionId", throwOnMissingValue: false);
        }
        catch
        {
            /* best effort */
        }
    }

    internal static string GetEditionKeyPath(bool enterprise)
        => enterprise ? $"{RegistryRoot}\\Enterprise" : $"{RegistryRoot}\\Personal";
}
