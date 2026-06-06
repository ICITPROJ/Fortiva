using System.Text.Json;
using Microsoft.Win32;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Copies the browser extension to a stable user folder and registers the native messaging host
/// for Chrome/Edge — no manual registry scripts required.
/// </summary>
public static class BrowserBridgeInstallService
{
    public const string PersonalHostName = "com.fortiva.browserbridge.personal";
    public const string EnterpriseHostName = "com.fortiva.browserbridge.enterprise";
    private const string BridgeExeName = "Fortiva.BrowserBridge.Host.exe";

    public static string HostNameForEdition(bool enterprise)
        => enterprise ? EnterpriseHostName : PersonalHostName;

    public static string GetAppDataRoot(bool enterprise)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            enterprise ? "FortivaEnterprise" : "FortivaPersonal");

    public static string GetExtensionStagingPath(bool enterprise)
        => Path.Combine(GetAppDataRoot(enterprise), "extension");

    public static string GetNativeMessagingManifestPath(bool enterprise)
        => Path.Combine(GetAppDataRoot(enterprise), "NativeMessaging", HostNameForEdition(enterprise) + ".json");

    public static BrowserBridgeInstallStatus GetStatus(string appBaseDirectory, bool enterprise)
    {
        var hostName = HostNameForEdition(enterprise);
        var staging = GetExtensionStagingPath(enterprise);
        var manifestPath = GetNativeMessagingManifestPath(enterprise);
        var bridgePath = ResolveBridgeExecutable(appBaseDirectory);
        var extensionSource = ResolveExtensionSource(appBaseDirectory);

        var extensionReady = Directory.Exists(staging)
            && File.Exists(Path.Combine(staging, "manifest.json"));
        var bridgeReady = !string.IsNullOrEmpty(bridgePath) && File.Exists(bridgePath);
        var registered = IsNativeHostRegistered(hostName, manifestPath, enterprise);
        var forceInstall = enterprise && BrowserExtensionPolicyService.IsForceInstallConfigured();

        return new BrowserBridgeInstallStatus(
            extensionReady,
            bridgeReady,
            registered,
            staging,
            bridgePath,
            extensionSource,
            manifestPath,
            hostName,
            extensionReady ? TryReadExtensionId(staging) : null,
            forceInstall);
    }

    public static BrowserBridgeInstallResult EnsureInstalled(string appBaseDirectory, bool enterprise)
    {
        var hostName = HostNameForEdition(enterprise);
        var bridgePath = ResolveBridgeExecutable(appBaseDirectory);
        if (string.IsNullOrEmpty(bridgePath) || !File.Exists(bridgePath))
        {
            return BrowserBridgeInstallResult.Fail(
                "Fortiva browser bridge is missing. Reinstall Fortiva or run build-release.ps1.");
        }

        var extensionSource = ResolveExtensionSource(appBaseDirectory);
        if (string.IsNullOrEmpty(extensionSource))
        {
            return BrowserBridgeInstallResult.Fail(
                "Extension files were not found with this install. Reinstall Fortiva.");
        }

        var stagingPath = GetExtensionStagingPath(enterprise);
        CopyExtensionFiles(extensionSource, stagingPath);

        string extensionId;
        try
        {
            extensionId = ExtensionIdHelper.ReadFromManifestFile(Path.Combine(stagingPath, "manifest.json"));
        }
        catch (Exception ex)
        {
            return BrowserBridgeInstallResult.Fail($"Invalid extension manifest: {ex.Message}");
        }

        var manifestPath = GetNativeMessagingManifestPath(enterprise);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var manifestJson = BuildManifestJson(hostName, bridgePath, extensionId);
        File.WriteAllText(manifestPath, manifestJson);

        RegisterNativeHost(hostName, manifestPath);

        if (enterprise)
            TryRegisterMachineNativeHost(appBaseDirectory, hostName, extensionId);

        return BrowserBridgeInstallResult.Ok(stagingPath, bridgePath, extensionId, hostName, manifestPath);
    }

    internal static string BuildManifestJson(string hostName, string bridgeExecutablePath, string extensionId)
    {
        var payload = new
        {
            name = hostName,
            description = "Fortiva local credential bridge",
            path = Path.GetFullPath(bridgeExecutablePath),
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{extensionId}/" }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string? ResolveExtensionSource(string appBaseDirectory)
    {
        foreach (var relative in new[] { "extension", Path.Combine("dist", "extension") })
        {
            var found = FindDirectoryUpward(appBaseDirectory, relative, "manifest.json");
            if (found is not null)
                return found;
        }

        return null;
    }

    internal static string? ResolveBridgeExecutable(string appBaseDirectory)
    {
        var direct = Path.Combine(appBaseDirectory, "BrowserBridge", BridgeExeName);
        if (File.Exists(direct))
            return direct;

        var fromDist = FindFileUpward(
            appBaseDirectory,
            Path.Combine("dist", "BrowserBridge", BridgeExeName));
        return fromDist;
    }

    private static string? FindDirectoryUpward(string startDirectory, string relativeDir, string requiredFile)
    {
        var dir = startDirectory;
        for (var depth = 0; depth < 10; depth++)
        {
            var candidate = Path.Combine(dir, relativeDir);
            if (File.Exists(Path.Combine(candidate, requiredFile)))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }

        return null;
    }

    private static string? FindFileUpward(string startDirectory, string relativeFile)
    {
        var dir = startDirectory;
        for (var depth = 0; depth < 10; depth++)
        {
            var candidate = Path.Combine(dir, relativeFile);
            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }

        return null;
    }

    private static void CopyExtensionFiles(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("com.fortiva.browserbridge", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(name, "content.js", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(file, Path.Combine(destinationDirectory, name), overwrite: true);
        }
    }

    private static string? TryReadExtensionId(string extensionDirectory)
    {
        try
        {
            return ExtensionIdHelper.ReadFromManifestFile(Path.Combine(extensionDirectory, "manifest.json"));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNativeHostRegistered(string hostName, string expectedManifestPath, bool enterprise)
    {
        var expected = Path.GetFullPath(expectedManifestPath);
        if (UserNativeHostMatches(hostName, expected))
            return File.Exists(expected);

        if (!enterprise)
            return false;

        var machineManifest = ResolveMachineNativeMessagingManifestPath(hostName);
        return machineManifest is not null
            && BrowserExtensionPolicyService.IsNativeHostRegisteredMachineWide(hostName, machineManifest);
    }

    private static bool UserNativeHostMatches(string hostName, string expectedManifestPath)
    {
        foreach (var subKey in UserNativeHostRegistrySubKeys(hostName))
        {
            var current = ReadRegistryDefault(Registry.CurrentUser, subKey);
            if (!string.Equals(current, expectedManifestPath, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    internal static string? ResolveMachineNativeMessagingManifestPath(string hostName)
    {
        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrEmpty(programFiles))
                continue;

            var candidate = Path.Combine(
                programFiles,
                "icmclab studio",
                "Fortiva Enterprise",
                "NativeMessaging",
                hostName + ".json");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void TryRegisterMachineNativeHost(string appBaseDirectory, string hostName, string extensionId)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var machineManifest = ResolveMachineNativeMessagingManifestPath(hostName);
        if (machineManifest is not null
            && BrowserExtensionPolicyService.IsNativeHostRegisteredMachineWide(hostName, machineManifest))
            return;

        var bridgePath = ResolveBridgeExecutable(appBaseDirectory);
        if (string.IsNullOrEmpty(bridgePath) || !File.Exists(bridgePath))
            return;

        var installRoot = Path.GetDirectoryName(Path.GetDirectoryName(bridgePath));
        if (string.IsNullOrEmpty(installRoot))
            return;

        var manifestPath = Path.Combine(installRoot, "NativeMessaging", hostName + ".json");
        if (!Directory.Exists(Path.GetDirectoryName(manifestPath)!))
            return;

        try
        {
            var manifestJson = BuildManifestJson(hostName, bridgePath, extensionId);
            File.WriteAllText(manifestPath, manifestJson);
            foreach (var subKey in BrowserExtensionPolicyService.MachineNativeHostRegistrySubKeys(hostName))
                WriteRegistryDefault(Registry.LocalMachine, subKey, Path.GetFullPath(manifestPath));
        }
        catch (UnauthorizedAccessException)
        {
            // Installer registers HKLM when elevated; non-admin launch cannot repair machine keys.
        }
        catch (System.Security.SecurityException)
        {
            // Same as above on locked-down systems.
        }
    }

    private static IEnumerable<string> UserNativeHostRegistrySubKeys(string hostName)
    {
        yield return $@"Software\Google\Chrome\NativeMessagingHosts\{hostName}";
        yield return $@"Software\Microsoft\Edge\NativeMessagingHosts\{hostName}";
    }

    private static void RegisterNativeHost(string hostName, string manifestPath)
    {
        var fullManifest = Path.GetFullPath(manifestPath);
        foreach (var subKey in UserNativeHostRegistrySubKeys(hostName))
            WriteRegistryDefault(Registry.CurrentUser, subKey, fullManifest);
    }

    private static void WriteRegistryDefault(RegistryKey root, string subKey, string value)
    {
        using var key = root.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Could not write registry key: {subKey}");
        key.SetValue(string.Empty, value);
    }

    private static string? ReadRegistryDefault(RegistryKey root, string subKey)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(string.Empty) as string;
    }
}

public sealed record BrowserBridgeInstallStatus(
    bool ExtensionFilesReady,
    bool BridgeExecutableFound,
    bool NativeHostRegistered,
    string ExtensionStagingPath,
    string? BridgeExecutablePath,
    string? ExtensionSourcePath,
    string NativeMessagingManifestPath,
    string HostName,
    string? ExtensionId,
    bool ExtensionForceInstallConfigured = false)
{
    public bool IsReadyForBrowser => ExtensionFilesReady && BridgeExecutableFound && NativeHostRegistered;
}

public sealed record BrowserBridgeInstallResult(
    bool Success,
    string? ExtensionStagingPath,
    string? BridgeExecutablePath,
    string? ExtensionId,
    string? HostName,
    string? NativeMessagingManifestPath,
    string? Error)
{
    public static BrowserBridgeInstallResult Ok(
        string stagingPath,
        string bridgePath,
        string extensionId,
        string hostName,
        string manifestPath)
        => new(true, stagingPath, bridgePath, extensionId, hostName, manifestPath, null);

    public static BrowserBridgeInstallResult Fail(string error)
        => new(false, null, null, null, null, null, error);
}
