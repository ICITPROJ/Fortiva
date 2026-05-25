using System.Text.Json;
using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BrowserBridgeInstallServiceTests
{
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
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
