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

    public bool IsConfigured => _protector.IsConfigured;

    public bool IsHardwareBacked => WindowsHelloKeyProtector.IsHardwareBackedBundle(_dataDirectory);

    public bool UsesSoftwareOnlyHello => IsConfigured && !IsHardwareBacked;

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
        var hardwareAvailable = await HelloCredentialStore.IsAvailableAsync();
        var preferHardware = useHardwareBacking ?? hardwareAvailable;
        if (preferHardware && hardwareAvailable)
        {
            // KeyCredential create/sign is the single Hello prompt — skip UserConsentVerifier here.
            await HelloCredentialStore.StoreAsync(_dataDirectory, masterKey);
            _protector.Clear();
            return;
        }

        var helloResult = await HelloService.VerifyAsync("Fortiva - set up Windows Hello");
        if (!helloResult.Verified)
            throw new InvalidOperationException(helloResult.ErrorMessage ?? "Windows Hello verification failed.");

        await HelloCredentialStore.DeleteCredentialAsync();
        _protector.StoreHelloBundle(masterKey, helloVerified: true);
    }

    public async Task<byte[]?> TryLoadMasterKeyAsync()
    {
        if (IsHardwareBacked)
        {
            var hardwareKey = await HelloCredentialStore.TryLoadAsync(_dataDirectory);
            if (hardwareKey is not null)
                return hardwareKey;
        }

        return _protector.TryLoadMasterKey(helloVerified: true);
    }

    public async Task ClearAsync()
    {
        _protector.Clear();
        await HelloCredentialStore.DeleteCredentialAsync();
    }
}
