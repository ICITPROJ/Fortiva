using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BrowserExtensionPolicyServiceTests
{
    [Fact]
    public void ExpectedForceInstallValue_UsesStableExtensionIdAndUrl()
    {
        const string url = "https://example.test/updates.xml";
        var value = BrowserExtensionPolicyService.ExpectedForceInstallValue(url);

        Assert.Equal(
            $"{BrowserExtensionConstants.StableExtensionId};{url}",
            value);
    }

    [Fact]
    public void FormatForceInstallListValue_MatchesPolicyServiceDefault()
    {
        Assert.Equal(
            BrowserExtensionPolicyService.ExpectedForceInstallValue(),
            BrowserExtensionConstants.FormatForceInstallListValue(
                BrowserExtensionConstants.EnterpriseUpdateManifestUrl));
    }

    [Fact]
    public void MachineNativeHostRegistrySubKeys_IncludesChromeAndEdge()
    {
        var keys = BrowserExtensionPolicyService
            .MachineNativeHostRegistrySubKeys(BrowserBridgeInstallService.EnterpriseHostName)
            .ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("SOFTWARE\\Google\\Chrome\\NativeMessagingHosts\\com.fortiva.browserbridge.enterprise", keys);
        Assert.Contains("SOFTWARE\\Microsoft\\Edge\\NativeMessagingHosts\\com.fortiva.browserbridge.enterprise", keys);
    }
}
