using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeClientValidatorTests
{
    public BridgeClientValidatorTests()
    {
        // Unit tests use stub EXEs; Authenticode is enforced in Release builds only.
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
    }

    [Theory]
    [InlineData("Fortiva.BrowserBridge.Host.exe", true)]
    [InlineData("Fortiva.Personal.exe", true)]
    [InlineData("Fortiva.Enterprise.exe", true)]
    [InlineData("malware.exe", false)]
    public void IsAllowedExecutableName_ValidatesKnownClients(string name, bool expected) =>
        Assert.Equal(expected, BridgeClientValidator.IsAllowedExecutableName(name));

    [Fact]
    public void IsAllowedBridgeHostPath_RejectsPersonalExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid());
        var personal = Path.Combine(root, "Fortiva.Personal.exe");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(personal, "");
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(personal, [root]));
        }
        finally
        {
            if (File.Exists(personal)) File.Delete(personal);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    [Fact]
    public void IsAllowedExecutablePath_RejectsWhenInstallRootsEmpty()
    {
        var bridge = Path.Combine(Path.GetTempPath(), "Fortiva.BrowserBridge.Host.exe");
        try
        {
            File.WriteAllText(bridge, "");
            Assert.False(BridgeClientValidator.IsAllowedExecutablePath(bridge, []));
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(bridge, []));
        }
        finally
        {
            if (File.Exists(bridge)) File.Delete(bridge);
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_RejectsBridgeOutsideInstallRoot()
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

            Assert.True(BridgeClientValidator.IsAllowedBridgeHostPath(validBridge, [root]));
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(invalidBridge, [root]));
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
