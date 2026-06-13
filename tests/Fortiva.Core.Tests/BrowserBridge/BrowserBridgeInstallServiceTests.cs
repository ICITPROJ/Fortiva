using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BrowserBridgeInstallServiceTests
{
    [Fact]
    public void ReadVersionFromManifestFile_ReadsVersionField()
    {
        var source = BrowserBridgeInstallService.ResolveExtensionSource(AppContext.BaseDirectory);
        Assert.NotNull(source);
        var version = ExtensionIdHelper.ReadVersionFromManifestFile(Path.Combine(source, "manifest.json"));
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void IsStableExtensionId_AcceptsPinnedReleaseId()
    {
        Assert.True(BrowserExtensionConstants.IsStableExtensionId("llkpcnbhmhpenahlcdnbbfmkdfkgnpnj"));
        Assert.False(BrowserExtensionConstants.IsStableExtensionId("fake-extension-id"));
    }

    [Fact]
    public void BuildManifestJson_IncludesHostBridgeAndExtensionOrigin()
    {
        const string host = BrowserBridgeInstallService.PersonalHostName;
        const string bridge = @"C:\Fortiva\BrowserBridge\Fortiva.BrowserBridge.Host.exe";
        const string extId = "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj";

        var json = BrowserBridgeInstallService.BuildManifestJson(host, bridge, extId);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(host, doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(bridge, doc.RootElement.GetProperty("path").GetString());
        Assert.Equal("stdio", doc.RootElement.GetProperty("type").GetString());
        var origins = doc.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.Contains($"chrome-extension://{extId}/", origins);
    }

    [Fact]
    public void ResolveExtensionSource_FindsRepoExtensionFromTestOutput()
    {
        var source = BrowserBridgeInstallService.ResolveExtensionSource(AppContext.BaseDirectory);
        Assert.NotNull(source);
        Assert.True(File.Exists(Path.Combine(source, "manifest.json")));
    }

    [Fact]
    public void IsManifestBridgePathValid_RejectsStaleTempPath()
    {
        var temp = Path.Combine(Path.GetTempPath(), "fv-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var manifest = Path.Combine(temp, "host.json");
        var staleBridge = Path.Combine(Path.GetTempPath(), "deleted-bridge", "Fortiva.BrowserBridge.Host.exe");
        File.WriteAllText(manifest, BrowserBridgeInstallService.BuildManifestJson(
            BrowserBridgeInstallService.PersonalHostName,
            staleBridge,
            "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj"));

        var goodBridge = Path.Combine(temp, "Fortiva.BrowserBridge.Host.exe");
        File.WriteAllText(goodBridge, "stub");

        Assert.False(BrowserBridgeInstallService.IsManifestBridgePathValid(manifest, goodBridge));

        File.WriteAllText(manifest, BrowserBridgeInstallService.BuildManifestJson(
            BrowserBridgeInstallService.PersonalHostName,
            goodBridge,
            "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj"));
        Assert.True(BrowserBridgeInstallService.IsManifestBridgePathValid(manifest, goodBridge));

        try { Directory.Delete(temp, true); } catch { }
    }

    [Fact]
    public void RepairNativeHostIfStale_RewritesManifestWithCurrentBridgePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "fortiva-repair-" + Guid.NewGuid().ToString("N"));
        var appBase = Path.Combine(tempRoot, "app");
        var bridgeDir = Path.Combine(appBase, "BrowserBridge");
        Directory.CreateDirectory(bridgeDir);
        var bridgeExe = Path.Combine(bridgeDir, "Fortiva.BrowserBridge.Host.exe");
        File.WriteAllText(bridgeExe, "stub");

        var extDir = Path.Combine(appBase, "extension");
        Directory.CreateDirectory(extDir);
        var repoExt = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "extension"));
        if (!Directory.Exists(repoExt))
            return;

        foreach (var file in Directory.GetFiles(repoExt))
        {
            var name = Path.GetFileName(file);
            if (name is "content.js") continue;
            File.Copy(file, Path.Combine(extDir, name));
        }

        var userdata = Path.Combine(tempRoot, "userdata");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", userdata);

        try
        {
            var manifestPath = BrowserBridgeInstallService.GetNativeMessagingManifestPath(enterprise: false);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, BrowserBridgeInstallService.BuildManifestJson(
                BrowserBridgeInstallService.PersonalHostName,
                @"C:\Temp\stale\Fortiva.BrowserBridge.Host.exe",
                "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj"));

            var result = BrowserBridgeInstallService.RepairNativeHostIfStale(appBase, enterprise: false);
            Assert.True(result.Success, result.Error);
            Assert.True(BrowserBridgeInstallService.IsManifestBridgePathValid(manifestPath, bridgeExe));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void GetExtensionStagingPath_UsesEditionSpecificAppDataFolder()
    {
        var personal = BrowserBridgeInstallService.GetExtensionStagingPath(enterprise: false);
        var enterprise = BrowserBridgeInstallService.GetExtensionStagingPath(enterprise: true);

        Assert.Contains("FortivaPersonal", personal);
        Assert.Contains("FortivaEnterprise", enterprise);
        Assert.EndsWith("extension", personal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureInstalled_StagesExtensionWithoutContentJs()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var extensionSource = Path.Combine(repoRoot, "extension");
        if (!Directory.Exists(extensionSource))
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), "fortiva-install-test-" + Guid.NewGuid().ToString("N"));
        var appBase = Path.Combine(tempRoot, "app");
        var bridgeDir = Path.Combine(appBase, "BrowserBridge");
        Directory.CreateDirectory(bridgeDir);
        File.WriteAllText(Path.Combine(bridgeDir, "Fortiva.BrowserBridge.Host.exe"), "stub");

        var extDir = Path.Combine(appBase, "extension");
        Directory.CreateDirectory(extDir);
        foreach (var file in Directory.GetFiles(extensionSource))
        {
            var name = Path.GetFileName(file);
            if (name is "content.js") continue;
            File.Copy(file, Path.Combine(extDir, name));
        }

        var stagingRoot = Path.Combine(tempRoot, "userdata");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", stagingRoot);

        try
        {
            var result = BrowserBridgeInstallService.EnsureInstalled(appBase, enterprise: false);
            Assert.True(result.Success, result.Error);
            Assert.False(File.Exists(Path.Combine(result.ExtensionStagingPath!, "content.js")));
            Assert.True(File.Exists(Path.Combine(result.ExtensionStagingPath!, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(result.ExtensionStagingPath!, "fill-coordinator.js")));
            Assert.False(File.Exists(Path.Combine(result.ExtensionStagingPath!, "page-fill-main.js")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
