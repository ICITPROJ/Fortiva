using System.Security.Cryptography;
using Fortiva.Core.Crypto;
using Fortiva.Core.Hello;
using Windows.Security.Cryptography;
using Windows.Security.Credentials;

namespace Fortiva.AppHost.Services;

/// <summary>
/// TPM / KeyCredential-backed Hello unlock (format v4).
/// Master key unwrap requires KeyCredential.RequestSignAsync — not recoverable via DPAPI alone.
/// </summary>
public static class HelloCredentialStore
{
    private const string CredentialName = "Fortiva.VaultUnlock";
    private static readonly byte[] MkWrapAssociatedData = "Fortiva.Hello.MK.v4"u8.ToArray();

    public static async Task<bool> IsAvailableAsync()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var open = await KeyCredentialManager.OpenAsync(CredentialName);
            if (open.Status == KeyCredentialStatus.Success)
                return true;

            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch
        {
            return false;
        }
    }

    public static async Task StoreAsync(string dataDirectory, byte[] masterKey)
    {
        var credential = await OpenOrCreateCredentialAsync();
        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = await SignChallengeAsync(credential, challenge);
        var wrapKey = DeriveWrapKey(challenge, signature);

        byte[]? wrappedMk = null;
        try
        {
            wrappedMk = CngAesGcm.Seal(wrapKey, masterKey, MkWrapAssociatedData);
            var payload = new byte[WindowsHelloKeyProtector.MagicV4.Length + challenge.Length + wrappedMk.Length];
            WindowsHelloKeyProtector.MagicV4.CopyTo(payload, 0);
            challenge.CopyTo(payload.AsSpan(WindowsHelloKeyProtector.MagicV4.Length));
            wrappedMk.CopyTo(payload, WindowsHelloKeyProtector.MagicV4.Length + challenge.Length);

            var path = Path.Combine(dataDirectory, "hello.keyprotect");
            HelloFileSecurity.WriteRestrictedFile(path, payload);
            HelloFileSecurity.WriteRestrictedFile(Path.Combine(dataDirectory, "hello.binding"), challenge);
        }
        finally
        {
            SecureMemory.Zero(wrapKey);
            SecureMemory.Zero(challenge);
            if (wrappedMk is not null) SecureMemory.Zero(wrappedMk);
        }
    }

    public static async Task<byte[]?> TryLoadAsync(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "hello.keyprotect");
        if (!File.Exists(path))
            return null;

        var fileBytes = File.ReadAllBytes(path);
        if (fileBytes.Length <= WindowsHelloKeyProtector.MagicV4.Length + 32 ||
            !fileBytes.AsSpan(0, WindowsHelloKeyProtector.MagicV4.Length).SequenceEqual(WindowsHelloKeyProtector.MagicV4))
            return null;

        var challenge = fileBytes.AsSpan(WindowsHelloKeyProtector.MagicV4.Length, 32).ToArray();
        var wrappedMk = fileBytes[(WindowsHelloKeyProtector.MagicV4.Length + 32)..];

        try
        {
            var open = await KeyCredentialManager.OpenAsync(CredentialName);
            if (open.Status != KeyCredentialStatus.Success || open.Credential is null)
                return null;

            var signature = await SignChallengeAsync(open.Credential, challenge);
            var wrapKey = DeriveWrapKey(challenge, signature);
            try
            {
                return CngAesGcm.Open(wrapKey, wrappedMk, MkWrapAssociatedData);
            }
            finally
            {
                SecureMemory.Zero(wrapKey);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            SecureMemory.Zero(challenge);
        }
    }

    public static async Task DeleteCredentialAsync()
    {
        try
        {
            await KeyCredentialManager.DeleteAsync(CredentialName);
        }
        catch
        {
            /* credential may not exist */
        }
    }

    private static async Task<KeyCredential> OpenOrCreateCredentialAsync()
    {
        var open = await KeyCredentialManager.OpenAsync(CredentialName);
        if (open.Status == KeyCredentialStatus.Success && open.Credential is not null)
            return open.Credential;

        var create = await KeyCredentialManager.RequestCreateAsync(
            CredentialName,
            KeyCredentialCreationOption.ReplaceExisting);
        if (create.Status != KeyCredentialStatus.Success || create.Credential is null)
            throw new InvalidOperationException("Windows Hello key credential could not be created.");

        return create.Credential;
    }

    /// <summary>MK wrap key requires a live Hello signature — not derivable from on-disk challenge alone.</summary>
    private static byte[] DeriveWrapKey(byte[] challenge, byte[] signature)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, 32, challenge, MkWrapAssociatedData);

    private static async Task<byte[]> SignChallengeAsync(KeyCredential credential, byte[] challenge)
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(challenge);
        var result = await credential.RequestSignAsync(buffer);
        if (result.Status != KeyCredentialStatus.Success || result.Result is null)
            throw new InvalidOperationException("Windows Hello signature request failed.");

        CryptographicBuffer.CopyToByteArray(result.Result, out var bytes);
        return bytes;
    }
}
