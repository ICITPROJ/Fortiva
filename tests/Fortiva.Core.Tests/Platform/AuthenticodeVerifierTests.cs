using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class AuthenticodeVerifierTests
{
    [Fact]
    public void AllowUnsignedBridgeForDevelopment_IsFalseInRelease()
    {
        if (!OperatingSystem.IsWindows())
            return;

#if DEBUG
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorBridge);
        }
#else
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        var priorActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());

            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());

            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
            Assert.True(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorBridge);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", priorActions);
        }
#endif
    }

    [Fact]
    public void IsSigned_MissingFile_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "fortiva-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        var priorActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Assert.False(AuthenticodeVerifier.IsSigned(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorBridge);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", priorActions);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
