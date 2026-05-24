using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Crypto;
using Fortiva.Core.Hello;

namespace Fortiva.Core.Tests.Hello;

public sealed class WindowsHelloKeyProtectorTests : IDisposable
{
    private readonly string _dir;

    public WindowsHelloKeyProtectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FortivaHelloTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void StoreLoad_RoundTripsMasterKey()
    {
        var protector = new WindowsHelloKeyProtector(_dir);
        var masterKey = RandomNumberGenerator.GetBytes(32);

        protector.StoreHelloBundle(masterKey, helloVerified: true);
        var loaded = protector.TryLoadMasterKey(helloVerified: true);

        Assert.NotNull(loaded);
        Assert.True(CryptographicOperations.FixedTimeEquals(masterKey, loaded));
        SecureMemory.Zero(loaded);
    }

    [Fact]
    public void TryLoad_RejectsLegacyBlobWithoutMagicHeader()
    {
        var protector = new WindowsHelloKeyProtector(_dir);
        var path = Path.Combine(_dir, "hello.keyprotect");

        var legacyPlain = Encoding.UTF8.GetBytes("legacy-password");
        var legacyProtected = ProtectedData.Protect(
            legacyPlain,
            Encoding.UTF8.GetBytes("Fortiva.Hello.Verified.v1"),
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, legacyProtected);

        Assert.Null(protector.TryLoadMasterKey(helloVerified: true));
    }

    [Fact]
    public void Clear_RemovesCredentialFile()
    {
        var protector = new WindowsHelloKeyProtector(_dir);
        var mk = RandomNumberGenerator.GetBytes(32);
        protector.StoreHelloBundle(mk, helloVerified: true);
        Assert.True(protector.IsConfigured);
        Assert.True(File.Exists(Path.Combine(_dir, "hello.binding")));

        protector.Clear();
        Assert.False(protector.IsConfigured);
        Assert.False(File.Exists(Path.Combine(_dir, "hello.binding")));
        Assert.Null(protector.TryLoadMasterKey(helloVerified: true));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenNotConfigured()
    {
        var protector = new WindowsHelloKeyProtector(_dir);
        Assert.Null(protector.TryLoadMasterKey(helloVerified: true));
    }
}
