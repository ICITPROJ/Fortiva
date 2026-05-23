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
}
