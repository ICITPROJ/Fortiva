using System.Security.Cryptography;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Tests.Crypto;

public class KeyHierarchyTests
{
    [Fact]
    public void CreateNew_WrapUnwrap_RoundTrip()
    {
        var (hierarchy, wrapped, salt) = KeyHierarchy.CreateNew("test-pw-123!", Argon2Parameters.PersonalDefault);
        var vk1 = hierarchy.VaultKey.ToArray();
        hierarchy.Dispose();

        var h2 = KeyHierarchy.Unlock("test-pw-123!", salt, wrapped, Argon2Parameters.PersonalDefault);
        Assert.Equal(vk1, h2.VaultKey);
        h2.Dispose();
    }

    [Fact]
    public void WrongPassword_ThrowsCryptographicException()
    {
        var (hierarchy, wrapped, salt) = KeyHierarchy.CreateNew("correct", Argon2Parameters.PersonalDefault);
        hierarchy.Dispose();
        Assert.Throws<CryptographicException>(
            () => KeyHierarchy.Unlock("wrong", salt, wrapped, Argon2Parameters.PersonalDefault));
    }

    [Fact]
    public void Lock_ClearsKeys()
    {
        var (hierarchy, _, _) = KeyHierarchy.CreateNew("pw", Argon2Parameters.PersonalDefault);
        hierarchy.Lock();
        Assert.Throws<InvalidOperationException>(() => _ = hierarchy.MasterKey);
        Assert.Throws<InvalidOperationException>(() => _ = hierarchy.VaultKey);
    }

    [Fact]
    public void EncryptDecrypt_PayloadRoundTrip()
    {
        var (hierarchy, _, _) = KeyHierarchy.CreateNew("pw", Argon2Parameters.PersonalDefault);
        var payload = "top secret payload"u8.ToArray();
        var ad = "test-ad"u8;
        var encrypted = hierarchy.EncryptPayload(payload, ad);
        var decrypted = hierarchy.DecryptPayload(encrypted, ad);
        Assert.Equal(payload, decrypted);
        hierarchy.Dispose();
    }

    [Fact]
    public void WrongAssociatedData_ThrowsOnDecrypt()
    {
        var (hierarchy, _, _) = KeyHierarchy.CreateNew("pw", Argon2Parameters.PersonalDefault);
        var payload = "data"u8.ToArray();
        var encrypted = hierarchy.EncryptPayload(payload, "correct-ad"u8);
        Assert.ThrowsAny<CryptographicException>(
            () => hierarchy.DecryptPayload(encrypted, "wrong-ad"u8));
        hierarchy.Dispose();
    }
}
