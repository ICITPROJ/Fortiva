using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultParserFuzzTests
{
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 })]
    [InlineData(new byte[] { 0x46, 0x4F, 0x52 })]
    public void ParseVaultFile_RejectsGarbage(byte[] data)
    {
        Assert.ThrowsAny<Exception>(() => VaultSerializer.ParseVaultFile(data));
    }

    [Fact]
    public void ParseVaultFile_RejectsBadMagic()
    {
        var bad = new byte[64];
        Assert.Throws<InvalidDataException>(() => VaultSerializer.ParseVaultFile(bad));
    }

    [Fact]
    public void ParseVaultFile_TruncatedRealVault_ThrowsInvalidData()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-trunc-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var engine = new VaultEngine(dir, DpapiScope.CurrentUser);
            engine.CreateVault("truncation-test-password-1!", SecurityLevel.Standard);
            var full = File.ReadAllBytes(engine.VaultPath);

            // Truncate at many offsets past the magic — every cut must surface as InvalidDataException,
            // never a raw ArgumentException / EndOfStreamException leaking from the parser.
            for (var cut = 8; cut < full.Length; cut += Math.Max(1, full.Length / 32))
            {
                var truncated = full[..cut];
                Assert.Throws<InvalidDataException>(() => VaultSerializer.ParseVaultFile(truncated));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
