using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class AuthenticodeVerifierTests
{
    [Fact]
    public void AllowUnsignedBridgeForDevelopment_IsFalseInRelease()
    {
        if (OperatingSystem.IsWindows())
        {
#if DEBUG
            var prior = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
            try
            {
                Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
                Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
            }
            finally
            {
                Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", prior);
            }
#else
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
#endif
        }
    }

    [Fact]
    public void IsSigned_MissingFile_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "fortiva-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        try
        {
#if DEBUG
            var prior = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
            try
            {
                Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
                Assert.False(AuthenticodeVerifier.IsSigned(path));
            }
            finally
            {
                Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", prior);
            }
#else
            Assert.False(AuthenticodeVerifier.IsSigned(path));
#endif
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
