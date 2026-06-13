using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeClientValidatorTests
{
    private static string SeedTrustedInstallRoot(string root)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Fortiva.Personal.exe"), "");
        return root;
    }

    public BridgeClientValidatorTests()
    {
        // Unit tests use stub EXEs; keep Personal unsigned policy isolated from other tests.
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        AuthenticodePolicy.ConfigureForEdition("Personal");
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
    public void TryInferInstallRootFromBridgeHostPath_ResolvesStandardLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid());
        var host = Path.Combine(root, "BrowserBridge", "Fortiva.BrowserBridge.Host.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);

        try
        {
            Assert.Equal(Path.GetFullPath(root), BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(host));
            Assert.Null(BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(Path.Combine(root, "Fortiva.BrowserBridge.Host.exe")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_AcceptsHostWhenRootInferredFromPath()
    {
        var root = SeedTrustedInstallRoot(Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid()));
        var host = Path.Combine(root, "BrowserBridge", "Fortiva.BrowserBridge.Host.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);
        File.WriteAllText(host, "");

        try
        {
            File.WriteAllText(host, "");
            BridgeInstallIntegrity.RecordBridgeHostHash(host);
            var inferred = BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(host);
            Assert.NotNull(inferred);
            Assert.True(BridgeClientValidator.IsTrustedInstallRoot(inferred));
            Assert.True(BridgeClientValidator.IsAllowedBridgeHostPath(host, [inferred!]));
        }
        finally
        {
            if (File.Exists(host)) File.Delete(host);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_RejectsBridgeOutsideInstallRoot()
    {
        var root = SeedTrustedInstallRoot(Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid()));
        var bridgeDir = Path.Combine(root, "BrowserBridge");
        Directory.CreateDirectory(bridgeDir);

        var validBridge = Path.Combine(bridgeDir, "Fortiva.BrowserBridge.Host.exe");
        var invalidBridge = Path.Combine(Path.GetTempPath(), "Fortiva.BrowserBridge.Host.exe");

        try
        {
            File.WriteAllText(validBridge, "");
            BridgeInstallIntegrity.RecordBridgeHostHash(validBridge);
            File.WriteAllText(invalidBridge, "");

            Assert.True(BridgeClientValidator.IsAllowedBridgeHostPath(validBridge, [root]));
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(invalidBridge, [root]));
        }
        finally
        {
            try
            {
                if (File.Exists(invalidBridge)) File.Delete(invalidBridge);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_RejectsInstallRootWithoutEntryExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid());
        var bridgeDir = Path.Combine(root, "BrowserBridge");
        Directory.CreateDirectory(bridgeDir);
        var host = Path.Combine(bridgeDir, "Fortiva.BrowserBridge.Host.exe");
        File.WriteAllText(host, "");

        try
        {
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(host, [root]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_RejectsHostAtInstallRoot()
    {
        var root = SeedTrustedInstallRoot(Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid()));
        var hostAtRoot = Path.Combine(root, "Fortiva.BrowserBridge.Host.exe");
        File.WriteAllText(hostAtRoot, "stub");

        try
        {
            Assert.False(BridgeClientValidator.IsAllowedBridgeHostPath(hostAtRoot, [root]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsAllowedBridgeHostPath_AcceptsInstalledPersonalLayout()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "icmclab studio", "Fortiva Personal");
        var host = Path.Combine(root, "BrowserBridge", "Fortiva.BrowserBridge.Host.exe");
        if (!File.Exists(host))
            return;

        Assert.True(BridgeClientValidator.IsTrustedInstallRoot(root));
        BridgeInstallIntegrity.RecordBridgeHostHash(host);
        Assert.True(BridgeClientValidator.IsAllowedBridgeHostPath(host, [root]));
    }
}
