using System.Security.Cryptography;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Tests.Crypto;

public class CngAesGcmTests
{
    [Fact]
    public void Seal_Open_RoundTrip()
    {
        var key = CngAesGcm.GenerateKey();
        var plain = "fortiva-secret-payload"u8.ToArray();
        var sealedBlob = CngAesGcm.Seal(key, plain);
        var opened = CngAesGcm.Open(key, sealedBlob);
        Assert.Equal(plain, opened);
        SecureMemory.Zero(key);
    }

    [Fact]
    public void EncryptDecrypt_Invariant_RandomPayloads()
    {
        for (var i = 0; i < 100; i++)
        {
            var plaintext = RandomNumberGenerator.GetBytes(RandomNumberGenerator.GetInt32(1, 512));
            var key = CngAesGcm.GenerateKey();
            var nonce = CngAesGcm.GenerateNonce();
            var (ct, tag) = CngAesGcm.Encrypt(key, nonce, plaintext);
            var dec = CngAesGcm.Decrypt(key, nonce, ct, tag);
            Assert.Equal(plaintext, dec);
        }
    }

    [Fact]
    public void TamperedTag_Throws()
    {
        var key = CngAesGcm.GenerateKey();
        var sealedBlob = CngAesGcm.Seal(key, [1, 2, 3]);
        sealedBlob[^1] ^= 0xFF;
        Assert.ThrowsAny<Exception>(() => CngAesGcm.Open(key, sealedBlob));
    }
}
