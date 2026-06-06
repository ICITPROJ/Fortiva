using Microsoft.Win32;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Detects and formats Chromium enterprise extension force-install policy (Enterprise edition).</summary>
public static class BrowserExtensionPolicyService
{
    private const string ChromeForceInstallSubKey = @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist";
    private const string EdgeForceInstallSubKey = @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist";

    public static string ExpectedForceInstallValue(string? updateManifestUrl = null)
        => BrowserExtensionConstants.FormatForceInstallListValue(
            updateManifestUrl ?? BrowserExtensionConstants.EnterpriseUpdateManifestUrl);

    public static bool IsForceInstallConfigured(string? updateManifestUrl = null)
    {
        var expected = ExpectedForceInstallValue(updateManifestUrl);
        return RegistryContainsForceInstallValue(Registry.LocalMachine, ChromeForceInstallSubKey, expected)
            || RegistryContainsForceInstallValue(Registry.LocalMachine, EdgeForceInstallSubKey, expected);
    }

    public static bool IsNativeHostRegisteredMachineWide(string hostName, string expectedManifestPath)
    {
        var expected = Path.GetFullPath(expectedManifestPath);
        foreach (var subKey in MachineNativeHostRegistrySubKeys(hostName))
        {
            var current = ReadRegistryDefault(Registry.LocalMachine, subKey);
            if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return File.Exists(expected);
    }

    internal static IEnumerable<string> MachineNativeHostRegistrySubKeys(string hostName)
    {
        yield return $@"SOFTWARE\Google\Chrome\NativeMessagingHosts\{hostName}";
        yield return $@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts\{hostName}";
    }

    private static bool RegistryContainsForceInstallValue(RegistryKey root, string subKeyPath, string expected)
    {
        using var key = root.OpenSubKey(subKeyPath);
        if (key is null)
            return false;

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string value
                && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ReadRegistryDefault(RegistryKey root, string subKey)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(string.Empty) as string;
    }
}
