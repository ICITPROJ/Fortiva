using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeClientValidatorTests
{
    [Theory]
    [InlineData("Fortiva.BrowserBridge.Host.exe", true)]
    [InlineData("Fortiva.Personal.exe", true)]
    [InlineData("Fortiva.Enterprise.exe", true)]
    [InlineData("malware.exe", false)]
    public void IsAllowedExecutableName_ValidatesKnownClients(string name, bool expected) =>
        Assert.Equal(expected, BridgeClientValidator.IsAllowedExecutableName(name));

    [Fact]
    public void IsAllowedExecutablePath_RejectsWhenInstallRootsEmpty()
    {
        var bridge = Path.Combine(Path.GetTempPath(), "Fortiva.BrowserBridge.Host.exe");
        try
        {
            File.WriteAllText(bridge, "");
            Assert.False(BridgeClientValidator.IsAllowedExecutablePath(bridge, []));
        }
        finally
        {
            if (File.Exists(bridge)) File.Delete(bridge);
        }
    }

    [Fact]
    public void IsAllowedExecutablePath_RejectsBridgeOutsideInstallRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid());
        var bridgeDir = Path.Combine(root, "BrowserBridge");
        Directory.CreateDirectory(bridgeDir);

        var validBridge = Path.Combine(bridgeDir, "Fortiva.BrowserBridge.Host.exe");
        var invalidBridge = Path.Combine(Path.GetTempPath(), "Fortiva.BrowserBridge.Host.exe");

        try
        {
            File.WriteAllText(validBridge, "");
            File.WriteAllText(invalidBridge, "");

            Assert.True(BridgeClientValidator.IsAllowedExecutablePath(validBridge, [root]));
            Assert.False(BridgeClientValidator.IsAllowedExecutablePath(invalidBridge, [root]));
        }
        finally
        {
            if (File.Exists(validBridge)) File.Delete(validBridge);
            if (File.Exists(invalidBridge)) File.Delete(invalidBridge);
            if (Directory.Exists(bridgeDir)) Directory.Delete(bridgeDir);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }
}
