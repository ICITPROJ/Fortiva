using Fortiva.Core.Crypto;
using Fortiva.Core.Hello;

namespace Fortiva.AppHost.Services;

/// <summary>Unified Hello unlock storage — prefers TPM-backed v4, falls back to gated DPAPI v3.</summary>
public sealed class HelloUnlockManager
{
    private readonly WindowsHelloKeyProtector _protector;
    private readonly string _dataDirectory;

    public HelloUnlockManager(string dataDirectory, bool enterpriseClient = false)
    {
        _dataDirectory = dataDirectory;
        _protector = new WindowsHelloKeyProtector(dataDirectory, enterpriseClient);
    }

    public bool IsConfigured => HelloBundleExists(_dataDirectory);

    public static bool HelloBundleExists(string dataDirectory) =>
        File.Exists(Path.Combine(dataDirectory, "hello.keyprotect"));

    public bool IsHardwareBacked => WindowsHelloKeyProtector.IsHardwareBackedBundle(_dataDirectory);

    public bool UsesSoftwareOnlyHello => IsConfigured && !IsHardwareBacked;

    public async Task<bool> IsUnlockReadyAsync()
    {
        if (!IsConfigured)
            return false;

        return IsHardwareBacked
            ? await HelloCredentialStore.IsAvailableAsync().ConfigureAwait(true)
            : await HelloService.IsAvailableAsync().ConfigureAwait(true);
    }

    public async Task<bool> ShouldPromptHardwareUpgradeAsync()
    {
        if (!UsesSoftwareOnlyHello)
            return false;

        return await HelloCredentialStore.IsAvailableAsync();
    }

    /// <param name="useHardwareBacking">
    /// When null, uses TPM-backed v4 when KeyCredential is available; otherwise v3 DPAPI.
    /// </param>
    public async Task StoreFromMasterKeyAsync(byte[] masterKey, bool? useHardwareBacking = null)
    {
        var hardwareAvailable = await HelloCredentialStore.IsAvailableAsync().ConfigureAwait(true);
        var useHardware = useHardwareBacking == true && hardwareAvailable;

        var forceRecreateCredential = false;
        HelloSetupLog.Step(
            $"StoreFromMasterKey hardwareAvailable={hardwareAvailable} useHardware={useHardware} reuseCredential=true");

        if (useHardware)
        {
            // Reuse existing KeyCredential when present — one RequestSignAsync prompt to bind the vault.
            if (_protector.IsConfigured && !IsHardwareBacked)
                _protector.Clear();

            await HelloCredentialStore.StoreAsync(_dataDirectory, masterKey, forceRecreateCredential)
                .ConfigureAwait(true);
            EnsureBundlePersisted();
            return;
        }

        var helloResult = await HelloService.VerifyAsync("Fortiva - set up Windows Hello").ConfigureAwait(true);
        if (!helloResult.Verified)
            throw new InvalidOperationException(helloResult.ErrorMessage ?? "Windows Hello verification failed.");

        HelloSetupLog.Step("UserConsentVerifier approved; saving software Hello binding.");

        await HelloCredentialStore.DeleteCredentialAsync().ConfigureAwait(true);
        _protector.StoreHelloBundle(masterKey, helloVerified: true);
        EnsureBundlePersisted();
    }

    private void EnsureBundlePersisted()
    {
        if (HelloBundleExists(_dataDirectory))
            return;

        throw new InvalidOperationException(
            "Windows Hello was approved but Fortiva could not save the vault binding. " +
            $"Check that Fortiva can write files in: {_dataDirectory}");
    }

    public async Task<byte[]?> TryLoadMasterKeyAsync()
    {
        var result = await TryUnlockMasterKeyAsync().ConfigureAwait(false);
        return result.MasterKey;
    }

    /// <summary>Single Hello prompt (PIN / face / fingerprint), then unwrap the vault master key.</summary>
    public async Task<HelloMasterKeyResult> TryUnlockMasterKeyAsync()
    {
        if (!IsConfigured)
            return HelloMasterKeyResult.NotConfigured;

        if (IsHardwareBacked)
        {
            try
            {
                var hardwareKey = await HelloCredentialStore.TryLoadAsync(_dataDirectory).ConfigureAwait(false);
                if (hardwareKey is not null)
                    return HelloMasterKeyResult.Success(hardwareKey);
            }
            catch (Exception ex)
            {
                App.LogException("HelloUnlockManager.TryUnlockMasterKeyAsync.Hardware", ex);
            }

            return HelloMasterKeyResult.Failed(
                "Windows Hello did not unlock the vault. Try again or use your master password.");
        }

        var helloResult = await HelloService.VerifyAsync("Unlock Fortiva vault").ConfigureAwait(false);
        if (!helloResult.Verified)
        {
            var cancelled = helloResult.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true;
            return HelloMasterKeyResult.Denied(helloResult.ErrorMessage, cancelled);
        }

        var masterKey = _protector.TryLoadMasterKey(helloVerified: true);
        return masterKey is not null
            ? HelloMasterKeyResult.Success(masterKey)
            : HelloMasterKeyResult.Failed(
                "Windows Hello credential is invalid for this vault. Unlock with your master password, then re-enable Hello in Settings.");
    }

    public void ClearBindingFiles() => _protector.Clear();

    public async Task ClearAsync()
    {
        ClearBindingFiles();
        await HelloCredentialStore.DeleteCredentialAsync();
    }
}

public sealed record HelloMasterKeyResult(bool Ok, byte[]? MasterKey, string? Error, bool Cancelled)
{
    public static HelloMasterKeyResult Success(byte[] masterKey) => new(true, masterKey, null, false);
    public static HelloMasterKeyResult NotConfigured => new(false, null, "Windows Hello is not set up.", false);
    public static HelloMasterKeyResult Denied(string? error, bool cancelled) => new(false, null, error, cancelled);
    public static HelloMasterKeyResult Failed(string error) => new(false, null, error, false);
}
