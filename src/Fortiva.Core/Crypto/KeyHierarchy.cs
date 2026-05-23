using System.Security.Cryptography;

namespace Fortiva.Core.Crypto;

/// <summary>
/// Master password → Argon2id → MK; MK encrypts VK; VK encrypts vault payload.
/// </summary>
public sealed class KeyHierarchy : IDisposable
{
    private byte[]? _masterKey;
    private byte[]? _vaultKey;

    public byte[] MasterKey => _masterKey ?? throw new InvalidOperationException("Not unlocked.");
    public byte[] VaultKey => _vaultKey ?? throw new InvalidOperationException("Vault key not loaded.");

    public static (KeyHierarchy hierarchy, byte[] wrappedVaultKey, byte[] salt) CreateNew(
        string masterPassword,
        Argon2Parameters kdfParams)
    {
        var (mk, salt) = Argon2Kdf.DeriveMasterKey(masterPassword, kdfParams);
        var vk = CngAesGcm.GenerateKey();
        var wrapped = CngAesGcm.Seal(mk, vk, associatedData: KeyWrapAssociatedData);
        var hierarchy = new KeyHierarchy();
        hierarchy._masterKey = mk;
        hierarchy._vaultKey = vk;
        return (hierarchy, wrapped, salt);
    }

    public static KeyHierarchy Unlock(string masterPassword, byte[] salt, byte[] wrappedVaultKey, Argon2Parameters kdfParams)
    {
        Argon2Parameters.Validate(kdfParams);
        var (mk, _) = Argon2Kdf.DeriveMasterKey(masterPassword, kdfParams, salt);
        return UnlockWithMasterKey(mk, wrappedVaultKey);
    }

    public static KeyHierarchy UnlockWithMasterKey(ReadOnlySpan<byte> masterKey, byte[] wrappedVaultKey)
    {
        var mk = masterKey.ToArray();
        try
        {
            var vk = CngAesGcm.Open(mk, wrappedVaultKey, KeyWrapAssociatedData);
            return new KeyHierarchy { _masterKey = mk, _vaultKey = vk };
        }
        catch (CryptographicException)
        {
            SecureMemory.Zero(mk);
            throw new CryptographicException("Invalid master password or corrupted vault key wrap.");
        }
    }

    public byte[] WrapVaultKey() => CngAesGcm.Seal(MasterKey, VaultKey, KeyWrapAssociatedData);

    public byte[] EncryptPayload(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
        => CngAesGcm.Seal(VaultKey, plaintext, associatedData);

    public byte[] DecryptPayload(ReadOnlySpan<byte> sealedBlob, ReadOnlySpan<byte> associatedData)
        => CngAesGcm.Open(VaultKey, sealedBlob, associatedData);

    private static ReadOnlySpan<byte> KeyWrapAssociatedData => "Fortiva.VK.Wrap.v1"u8;

    public void Lock()
    {
        if (_masterKey is not null) SecureMemory.Zero(_masterKey);
        if (_vaultKey is not null) SecureMemory.Zero(_vaultKey);
        _masterKey = null;
        _vaultKey = null;
    }

    public void Dispose() => Lock();
}
