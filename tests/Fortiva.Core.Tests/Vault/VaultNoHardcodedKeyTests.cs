using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

/// <summary>
/// Guards against the classic "fake-secure" failure: a hardcoded/static encryption key, fixed salt,
/// or fixed nonce that would let anyone who extracts the binary decrypt every user's vault.
/// </summary>
public class VaultNoHardcodedKeyTests : IDisposable
{
    private readonly string _dirA;
    private readonly string _dirB;
    private const string SamePassword = "identical-master-password-9!";

    public VaultNoHardcodedKeyTests()
    {
        _dirA = Path.Combine(Path.GetTempPath(), "fortiva-hk-a-" + Guid.NewGuid());
        _dirB = Path.Combine(Path.GetTempPath(), "fortiva-hk-b-" + Guid.NewGuid());
        Directory.CreateDirectory(_dirA);
        Directory.CreateDirectory(_dirB);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _dirA, _dirB })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    private static VaultUnlockContext SeedIdenticalVault(string dir)
    {
        var engine = new VaultEngine(dir, DpapiScope.CurrentUser);
        engine.CreateVault(SamePassword, SecurityLevel.Standard);
        var ctx = engine.Unlock(SamePassword);
        engine.AddEntry(ctx, new VaultEntry
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Bank",
            Username = "user@example.com",
            Password = "the-same-secret-value",
            CreatedAt = DateTimeOffset.UnixEpoch,
            ModifiedAt = DateTimeOffset.UnixEpoch
        });
        return ctx;
    }

    [Fact]
    public void TwoVaults_SamePasswordAndContent_ProduceDifferentCiphertext()
    {
        SeedIdenticalVault(_dirA).Keys.Dispose();
        SeedIdenticalVault(_dirB).Keys.Dispose();

        var fileA = File.ReadAllBytes(Path.Combine(_dirA, VaultConstants.VaultFileName));
        var fileB = File.ReadAllBytes(Path.Combine(_dirB, VaultConstants.VaultFileName));

        // Identical password + identical entry must NOT yield identical bytes on disk.
        // If a static key/salt/nonce were used, these would be equal.
        Assert.False(fileA.AsSpan().SequenceEqual(fileB),
            "Two vaults with the same password and content produced byte-identical files — indicates a static key/salt/nonce.");
    }

    [Fact]
    public void TwoVaults_SamePassword_HaveDifferentSaltAndWrappedKey()
    {
        SeedIdenticalVault(_dirA).Keys.Dispose();
        SeedIdenticalVault(_dirB).Keys.Dispose();

        var headerA = VaultSerializer.ParseVaultFile(File.ReadAllBytes(Path.Combine(_dirA, VaultConstants.VaultFileName))).header;
        var headerB = VaultSerializer.ParseVaultFile(File.ReadAllBytes(Path.Combine(_dirB, VaultConstants.VaultFileName))).header;

        Assert.False(headerA.Salt.AsSpan().SequenceEqual(headerB.Salt), "Per-vault salts must be random and unique.");
        Assert.False(headerA.WrappedVaultKey.AsSpan().SequenceEqual(headerB.WrappedVaultKey), "Per-vault wrapped keys must differ.");
        Assert.NotEqual(headerA.VaultId, headerB.VaultId);
    }

    [Fact]
    public void Vault_CannotBeDecryptedWithoutCorrectPassword()
    {
        SeedIdenticalVault(_dirA).Keys.Dispose();

        var reopen = new VaultEngine(_dirA, DpapiScope.CurrentUser);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => reopen.Unlock("not-the-password"));
    }

    [Fact]
    public void SameSession_EncryptingSamePlaintextTwice_IsNonDeterministic()
    {
        // Even with the SAME vault key, AES-GCM must use a fresh random nonce each time.
        var key = CngAesGcm.GenerateKey();
        var plaintext = "repeated-plaintext"u8.ToArray();

        var first = CngAesGcm.Seal(key, plaintext);
        var second = CngAesGcm.Seal(key, plaintext);

        Assert.False(first.AsSpan().SequenceEqual(second),
            "Sealing the same plaintext twice produced identical output — indicates a static nonce.");
        SecureMemory.Zero(key);
    }
}
