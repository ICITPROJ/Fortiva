using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeInstallIntegrityTests : IDisposable
{
    private readonly string _root;

    public BridgeInstallIntegrityTests()
    {
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        AuthenticodePolicy.ConfigureForEdition("Personal");
        _root = Path.Combine(Path.GetTempPath(), "fortiva-bridge-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "BrowserBridge"));
        File.WriteAllText(Path.Combine(_root, "Fortiva.Personal.exe"), "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string BridgeExe => Path.Combine(_root, "BrowserBridge", BridgeClientValidator.BridgeHostExecutableName);

    [Fact]
    public void RecordAndVerify_AcceptsUnchangedExecutable()
    {
        File.WriteAllText(BridgeExe, "bridge-v1");
        BridgeInstallIntegrity.RecordBridgeHostHash(BridgeExe);

        Assert.True(BridgeInstallIntegrity.VerifyBridgeHostHash(BridgeExe, [_root]));
    }

    [Fact]
    public void Verify_RejectsTamperedExecutable()
    {
        File.WriteAllText(BridgeExe, "bridge-v1");
        BridgeInstallIntegrity.RecordBridgeHostHash(BridgeExe);
        File.WriteAllText(BridgeExe, "bridge-tampered");

        Assert.False(BridgeInstallIntegrity.VerifyBridgeHostHash(BridgeExe, [_root]));
    }

    [Fact]
    public void Verify_RejectsMissingSidecar()
    {
        File.WriteAllText(BridgeExe, "bridge-v1");
        Assert.False(BridgeInstallIntegrity.VerifyBridgeHostHash(BridgeExe, [_root]));
    }

    [Fact]
    public void Verify_RejectsWhenSidecarDeletedAfterPin()
    {
        File.WriteAllText(BridgeExe, "bridge-v1");
        BridgeInstallIntegrity.RecordBridgeHostHash(BridgeExe);
        File.Delete(BridgeInstallIntegrity.GetSidecarPath(_root));

        Assert.False(BridgeInstallIntegrity.VerifyBridgeHostHash(BridgeExe, [_root]));
    }

    [Fact]
    public void RecordBridgeHostHash_CreatesSidecarOnInstall()
    {
        File.WriteAllText(BridgeExe, "bridge-v1");
        var sidecar = BridgeInstallIntegrity.GetSidecarPath(_root);
        Assert.False(File.Exists(sidecar));

        BridgeInstallIntegrity.RecordBridgeHostHash(BridgeExe);

        Assert.True(File.Exists(sidecar));
        Assert.True(BridgeInstallIntegrity.VerifyBridgeHostHash(BridgeExe, [_root]));
    }
}
