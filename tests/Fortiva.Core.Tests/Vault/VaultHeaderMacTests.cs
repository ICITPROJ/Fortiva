using System.Security.Cryptography;
using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultHeaderMacTests : IDisposable
{
    private readonly string _dir;

    public VaultHeaderMacTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-mac-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Unlock_RejectsTamperedHeaderField()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("mac-test-password!", SecurityLevel.Standard);

        var bytes = File.ReadAllBytes(engine.VaultPath);
        // Flip a byte in the KDF parameters region (after magic + version bytes).
        bytes[20] ^= 0xFF;
        File.WriteAllBytes(engine.VaultPath, bytes);

        Assert.ThrowsAny<Exception>(() =>
            engine.Unlock("mac-test-password!", false, false));
    }
}
