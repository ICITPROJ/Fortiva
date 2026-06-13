namespace Fortiva.Core.BrowserBridge;

/// <summary>Stable browser extension identity and enterprise update endpoints.</summary>
public static class BrowserExtensionConstants
{
    /// <summary>Derived from <c>extension/manifest.json</c> <c>key</c> — do not change without a migration plan.</summary>
    public const string StableExtensionId = "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj";

    /// <summary>
    /// Chrome/Edge <c>ExtensionInstallForcelist</c> update manifest (stable URL; CRX version inside XML changes per release).
    /// </summary>
    public const string EnterpriseUpdateManifestUrl =
        "https://github.com/ICITPROJ/Fortiva/releases/latest/download/fortiva-extension-updates.xml";

    public const string EnterpriseCrxFileName = "FortivaAutofill.crx";

    public static string FormatForceInstallListValue(string updateManifestUrl)
        => $"{StableExtensionId};{updateManifestUrl}";

    public static bool IsStableExtensionId(string? extensionId)
        => !string.IsNullOrWhiteSpace(extensionId)
            && string.Equals(extensionId.Trim(), StableExtensionId, StringComparison.OrdinalIgnoreCase);
}
