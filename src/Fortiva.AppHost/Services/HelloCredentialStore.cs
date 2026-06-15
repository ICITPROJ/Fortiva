using System.Runtime.InteropServices;
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
            var open = await KeyCredentialManager.OpenAsync(CredentialName).AsTask().ConfigureAwait(false);
            if (open.Status == KeyCredentialStatus.Success)
                return true;

            return await KeyCredentialManager.IsSupportedAsync().AsTask().ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> HasOrphanedCredentialAsync(string dataDirectory)
    {
        if (HelloUnlockManager.HelloBundleExists(dataDirectory))
            return false;

        try
        {
            var open = await KeyCredentialManager.OpenAsync(CredentialName).AsTask().ConfigureAwait(false);
            return open.Status == KeyCredentialStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public static async Task StoreAsync(string dataDirectory, byte[] masterKey, bool forceRecreateCredential = false)
    {
        Directory.CreateDirectory(dataDirectory);

        var credential = await OpenOrCreateCredentialAsync(forceRecreateCredential).ConfigureAwait(true);
        HelloSetupLog.Step("KeyCredential ready; requesting Hello signature for vault binding.");

        var challenge = RandomNumberGenerator.GetBytes(32);

        App.BringMainWindowToFront();
        byte[] signature;
        try
        {
            signature = await SignChallengeAsync(credential, challenge).ConfigureAwait(true);
        }
        catch (Exception ex) when (HelloHardwareErrors.IsHardwareUnavailable(ex))
        {
            throw new HelloHardwareUnavailableException(HelloHardwareErrors.Describe(ex), ex);
        }

        HelloSetupLog.Step("Hello signature received; writing binding files.");
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
            VerifyBundleWritten(dataDirectory);
        }
        finally
        {
            SecureMemory.Zero(wrapKey);
            SecureMemory.Zero(challenge);
            if (wrappedMk is not null) SecureMemory.Zero(wrappedMk);
        }
    }

    private static void VerifyBundleWritten(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "hello.keyprotect");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Windows Hello binding was not saved to {path}. Check folder permissions and try again.");
        }

        if (new FileInfo(path).Length < WindowsHelloKeyProtector.MagicV4.Length + 33)
        {
            throw new InvalidOperationException(
                $"Windows Hello binding at {path} is incomplete. Try setup again.");
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
            var open = await KeyCredentialManager.OpenAsync(CredentialName).AsTask().ConfigureAwait(false);
            if (open.Status != KeyCredentialStatus.Success || open.Credential is null)
                return null;

            var signature = await SignChallengeAsync(open.Credential, challenge).ConfigureAwait(false);
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
            await KeyCredentialManager.DeleteAsync(CredentialName).AsTask().ConfigureAwait(false);
        }
        catch
        {
            /* credential may not exist */
        }
    }

    private static async Task<KeyCredential> OpenOrCreateCredentialAsync(bool forceRecreate)
    {
        App.EnsureMainWindowIcon();
        App.EnsureMainWindowHandle();

        if (!forceRecreate)
        {
            var open = await KeyCredentialManager.OpenAsync(CredentialName).AsTask().ConfigureAwait(true);
            if (open.Status == KeyCredentialStatus.Success && open.Credential is not null)
                return open.Credential;
        }

        try { await KeyCredentialManager.DeleteAsync(CredentialName).AsTask().ConfigureAwait(true); }
        catch { /* best effort */ }

        var create = await KeyCredentialManager.RequestCreateAsync(
            CredentialName,
            KeyCredentialCreationOption.ReplaceExisting).AsTask().ConfigureAwait(true);

        if (create.Status != KeyCredentialStatus.Success || create.Credential is null)
        {
            HelloSetupLog.Step($"KeyCredential RequestCreate failed: {create.Status}");
            throw new InvalidOperationException(
                $"Windows Hello key credential could not be created ({create.Status}). " +
                "Open Windows Settings → Accounts → Sign-in options and confirm PIN or biometrics are set up.");
        }

        HelloSetupLog.Step("KeyCredential created; next step is Hello signature.");
        return create.Credential;
    }

    /// <summary>MK wrap key requires a live Hello signature — not derivable from on-disk challenge alone.</summary>
    private static byte[] DeriveWrapKey(byte[] challenge, byte[] signature)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, 32, challenge, MkWrapAssociatedData);

    private static async Task<byte[]> SignChallengeAsync(KeyCredential credential, byte[] challenge)
    {
        App.EnsureMainWindowIcon();
        App.EnsureMainWindowHandle();
        App.BringMainWindowToFront();

        var buffer = CryptographicBuffer.CreateFromByteArray(challenge);
        KeyCredentialOperationResult result;
        try
        {
            result = await credential.RequestSignAsync(buffer).AsTask().ConfigureAwait(true);
        }
        catch (COMException ex) when (HelloHardwareErrors.IsHardwareUnavailable(ex))
        {
            throw new HelloHardwareUnavailableException(HelloHardwareErrors.Describe(ex), ex);
        }

        if (result.Status != KeyCredentialStatus.Success || result.Result is null)
        {
            throw new InvalidOperationException(
                $"Windows Hello signature request failed ({result.Status}). " +
                "Try again, or remove Hello in Settings and set it up again.");
        }

        CryptographicBuffer.CopyToByteArray(result.Result, out var bytes);
        return bytes;
    }
}
