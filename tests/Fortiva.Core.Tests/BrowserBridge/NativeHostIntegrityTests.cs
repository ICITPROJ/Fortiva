using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class NativeHostIntegrityTests
{
    public NativeHostIntegrityTests()
    {
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        AuthenticodePolicy.ConfigureForEdition("Personal");
    }

    [Fact]
    public void VerifyCurrentProcess_ReturnsFalseForTestRunnerLayout()
    {
        var prior = AuthenticodePolicy.RequireSignedExecutables;
        try
        {
            AuthenticodePolicy.RequireSignedExecutables = false;
            Assert.False(NativeHostIntegrity.VerifyCurrentProcess());
        }
        finally
        {
            AuthenticodePolicy.RequireSignedExecutables = prior;
        }
    }

    [Fact]
    public void VerifyCurrentProcess_ReturnsFalseWhenExplicitRootDoesNotContainProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), "FortivaInstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Fortiva.Personal.exe"), "");
        try
        {
            Assert.False(NativeHostIntegrity.VerifyCurrentProcess(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
