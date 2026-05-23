using System.Collections.ObjectModel;
using Fortiva.Core.Crypto;
using Fortiva.Core.Hello;
using Fortiva.Core.Licensing;
using Fortiva.Core.LocalState;
using Fortiva.Core.Password;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Services;
using Fortiva.Core.Vault;

namespace Fortiva.AppHost.ViewModels;

public sealed class AppViewModel
{
    private VaultSession? _session;
    private readonly bool _enterprise;
    private FortivaPolicy? _policy;
    private bool _paranoiaMode;
    private bool _portableMode;
    private string? _portableRoot;

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public string StatusMessage { get; private set; } = "Locked";
    public bool IsUnlocked => _session?.IsUnlocked ?? false;
    public bool IsReadOnly => _session?.IsReadOnly ?? false;
    public event Action? StateChanged;

    public AppViewModel(bool enterprise)
    {
        _enterprise = enterprise;
        if (enterprise)
        {
            if (!LicenseVerifier.IsValidAndNotExpired(LicenseStore.Load()))
                StatusMessage = "Invalid or expired license.";
            _policy = PolicyStore.Load();
        }
    }

    public string VaultDirectory =>
        FortivaPaths.GetVaultDirectory(_portableMode, _portableRoot);

    public void SetPortableMode(bool enabled, string? root = null)
    {
        if (_policy is not null && !PolicyEnforcer.CanUsePortableMode(_policy))
            throw new InvalidOperationException("Portable mode is forbidden by policy.");
        _portableMode = enabled;
        _portableRoot = root;
    }

    public void SetParanoiaMode(bool enabled) => _paranoiaMode = enabled;

    public void CreateVault(string masterPassword, SecurityLevel level)
    {
        EnsureSession();
        _session!.CreateVault(masterPassword, level);
        StatusMessage = "Vault created. Please unlock.";
        Notify();
    }

    public void Unlock(string masterPassword, bool confirmRollback = false)
    {
        EnsureSession();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _session!.Unlock(masterPassword, _paranoiaMode, confirmRollback);
        sw.Stop();
        RefreshEntries();
        StatusMessage = _session.IsReadOnly
            ? $"Unlocked (read-only) in {sw.ElapsedMilliseconds} ms — rollback warning."
            : $"Unlocked in {sw.ElapsedMilliseconds} ms";
        Notify();
    }

    public void Lock()
    {
        _session?.Lock();
        Entries.Clear();
        StatusMessage = "Locked";
        Notify();
    }

    public void PanicLock()
    {
        _session?.PanicLock();
        Entries.Clear();
        StatusMessage = "Panic locked";
        Notify();
    }

    public void AddEntry(string title, string username, string password, string url, string notes = "")
    {
        if (_session is null) return;
        var entry = new VaultEntry { Title = title, Username = username, Password = password, Url = url, Notes = notes };
        _session.AddEntry(entry);
        RefreshEntries();
        Notify();
    }

    public void DeleteEntry(Guid id)
    {
        if (_session?.Context is null) return;
        var entry = _session.Context.Payload.Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return;
        _session.Context.Payload.Entries.Remove(entry);
        _session.Save();
        RefreshEntries();
        Notify();
    }

    public PasswordHealthReport GetHealthReport()
    {
        if (_session?.Context is null) return new PasswordHealthReport();
        return PasswordHealthAnalyzer.Analyze(_session.Context.Payload.Entries);
    }

    public string GeneratePassword(int length, PasswordGeneratorMode mode)
        => PasswordGenerator.Generate(length, mode);

    public bool ConfigureHello(byte[] masterKey)
    {
        var hello = new WindowsHelloKeyProtector(VaultDirectory, _enterprise);
        hello.StoreHelloBundle(masterKey, helloVerified: true);
        return true;
    }

    public void ReloadPolicies()
    {
        if (!_enterprise) return;
        _policy = PolicyStore.Load();
        StatusMessage = "Policies reloaded";
        Notify();
    }

    private void EnsureSession()
    {
        _session ??= new VaultSession(
            VaultDirectory,
            _enterprise ? DpapiScope.LocalMachine : DpapiScope.CurrentUser,
            _policy,
            enableAudit: _enterprise);
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        if (_session is null) return;
        foreach (var e in _session.Search(""))
            Entries.Add(e);
    }

    private void Notify() => StateChanged?.Invoke();
}
