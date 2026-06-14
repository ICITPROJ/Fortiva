namespace Fortiva.Core.BrowserBridge;

/// <summary>Loopback HTTP bridge — avoids Chromium native-messaging spawn issues on Windows.</summary>
public static class BridgeLocalhostConstants
{
    public const int Port = 7847;

    public const string Prefix = "http://127.0.0.1:7847/";

    public static string ExtensionOrigin =>
        $"chrome-extension://{BrowserExtensionConstants.StableExtensionId}/";
}
