using Fortiva.AppHost.Services;
using Fortiva.Core.Hello;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class HelloUnlockManagerTests
{
    [Fact]
    public void IsConfigured_DetectsHardwareBundleOnDisk()
    {
        var dir = CreateTempDir();
        try
        {
            var payload = new byte[WindowsHelloKeyProtector.MagicV4.Length + 32 + 16];
            WindowsHelloKeyProtector.MagicV4.CopyTo(payload, 0);
            File.WriteAllBytes(Path.Combine(dir, "hello.keyprotect"), payload);

            var manager = new HelloUnlockManager(dir);

            Assert.True(manager.IsConfigured);
            Assert.True(manager.IsHardwareBacked);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HelloBundleExists_ReturnsFalseWhenBundleMissing()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.False(HelloUnlockManager.HelloBundleExists(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fortiva-hello-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
