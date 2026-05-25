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

    public async Task StoreFromMasterKeyAsync(byte[] masterKey)
    {
        if (await HelloCredentialStore.IsAvailableAsync())
        {
            await HelloCredentialStore.StoreAsync(_dataDirectory, masterKey);
            return;
        }

        _protector.StoreHelloBundle(masterKey, helloVerified: true);
    }

    public async Task<byte[]?> TryLoadMasterKeyAsync()
    {
        if (IsHardwareBacked)
            return await HelloCredentialStore.TryLoadAsync(_dataDirectory);

        return _protector.TryLoadMasterKey(helloVerified: true);
    }

    public async Task ClearAsync()
    {
        _protector.Clear();
        await HelloCredentialStore.DeleteCredentialAsync();
    }
}
