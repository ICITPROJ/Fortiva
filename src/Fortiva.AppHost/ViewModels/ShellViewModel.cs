using System.Collections.ObjectModel;
using System.Text;
using Fortiva.AppHost.Services;
using Fortiva.Core.Audit;
using Fortiva.Core.Crypto;
using Fortiva.Core.Hello;
using Fortiva.Core.Licensing;
using Fortiva.Core.LocalState;
using Fortiva.Core.Password;
using Fortiva.Core.Security;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Services;
using Fortiva.Core.Vault;

namespace Fortiva.AppHost.ViewModels;

/// <summary>Top-level application state. One instance per process lifetime.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    // ── Singletons ────────────────────────────────────────────────────────────
    public static ShellViewModel Current { get; } = new();

    // ── Internal state ────────────────────────────────────────────────────────
    private VaultSession? _session;
    private string _statusMessage = "Welcome to Fortiva";
    private bool _isBusy;
    private bool _vaultExists;
    private bool _isLocking;
    private Action<Action>? _uiInvoker;
    private PersonalUserSettings _personalSettings = PersonalUserSettings.Load();
    private AppearanceSettings _appearance = AppearanceSettings.Load();

    public bool PreferParanoiaMode =>
        Policy?.MandatoryParanoiaMode == true || _personalSettings.ParanoiaMode;

    /// <summary>Fired when paranoia mode or brand appearance should refresh (logo/icon).</summary>
    public event Action? BrandAppearanceChanged;
    /// <summary>Fired when light/dark theme preference changes.</summary>
    public event Action? ThemeChanged;

    // ── Edition / policy / license ────────────────────────────────────────────
    public string Edition
    {
        get
        {
            try { return App.Edition; }
            catch { return "Personal"; }
        }
    }
    public bool IsEnterprise => Edition is "Enterprise" or "Admin";
    public bool IsAdmin => Edition == "Admin";
    public bool CanUseTotp => PolicyEnforcer.CanUseTotp(Edition == "Enterprise", Policy);
    public FortivaPolicy? Policy { get; private set; }
    public SignedLicense? License { get; private set; }
    public bool IsLicenseValid => LicenseVerifier.IsValidAndNotExpired(License);

    // ── Observable properties ────────────────────────────────────────────────
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool IsUnlocked => _session?.IsUnlocked ?? false;
    public bool IsReadOnly => _session?.IsReadOnly ?? false;
    public bool PendingRollbackConfirm { get; set; }
    public bool VaultExists { get => _vaultExists; private set => Set(ref _vaultExists, value); }

    public string VaultDirectory { get; private set; } = FortivaPaths.PersonalVaultDirectory;
    public bool IsPortableMode { get; private set; }

    /// <summary>Human-readable vault location for settings UI.</summary>
    public string VaultLocationLabel =>
        IsPortableMode ? $"Portable: {VaultDirectory}" : $"Local: {VaultDirectory}";

    public ObservableCollection<VaultEntryViewModel> Entries { get; } = [];

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action? LockOccurred;
    public event Action? UnlockOccurred;
    public event Action? StateChanged;
    public event Action? EnterpriseConfigChanged;
    public event Action? VaultLocationChanged;
    /// <summary>Request left-nav highlight without triggering navigation (e.g. toolbar shortcut).</summary>
    public event Action<string>? NavigationTabRequested;

    // ── Constructor ───────────────────────────────────────────────────────────
    private ShellViewModel()
    {
        if (IsEnterprise)
        {
            License = LicenseStore.Load();
            Policy = TryLoadPolicy();
            VaultDirectory = FortivaPaths.EnterpriseProgramData;
        }
        else
        {
            FortivaPaths.MigrateLegacyPersonalVaultIfNeeded();
            TryRestorePortableVault();
        }
        VaultExists = File.Exists(Path.Combine(VaultDirectory, VaultConstants.VaultFileName));
    }

    public void RequestNavigationTab(string tag) => NavigationTabRequested?.Invoke(tag);

    /// <summary>Re-read vault.fva from disk (e.g. after external install/uninstall).</summary>
    public void RefreshVaultExists()
    {
        if (!IsEnterprise)
            FortivaPaths.MigrateLegacyPersonalVaultIfNeeded();
        VaultExists = File.Exists(Path.Combine(VaultDirectory, VaultConstants.VaultFileName));
        OnPropertyChanged(nameof(VaultExists));
    }

    public void SetUiInvoker(Action<Action> invoker) => _uiInvoker = invoker;

    public PersonalUserSettings PersonalSettings => _personalSettings;

    public AppThemePreference ThemePreference => _appearance.Theme;

    public void SavePersonalSettings()
    {
        if (IsEnterprise) return;
        _personalSettings.Save();
    }

    public void SetAutoLockTimeout(int seconds)
    {
        _personalSettings.AutoLockSeconds = seconds;
        _session?.SetAutoLockTimeout(seconds);
        SavePersonalSettings();
    }

    public void SetClipboardClearSeconds(int seconds)
    {
        _personalSettings.ClipboardClearSeconds = seconds;
        SavePersonalSettings();
    }

    public void SetAutoUpdateEnabled(bool enabled)
    {
        if (IsEnterprise) return;
        _personalSettings.AutoUpdateEnabled = enabled;
        SavePersonalSettings();
    }

    public void SetParanoiaMode(bool enabled)
    {
        _personalSettings.ParanoiaMode = enabled;
        SavePersonalSettings();
        OnPropertyChanged(nameof(PreferParanoiaMode));
        BrandAppearanceChanged?.Invoke();
    }

    public void SetThemePreference(AppThemePreference theme)
    {
        if (_appearance.Theme == theme)
            return;
        _appearance.Theme = theme;
        _appearance.Save();
        ThemeChanged?.Invoke();
    }

    // ── Session lifecycle ─────────────────────────────────────────────────────

    public void CreateVault(string masterPassword, SecurityLevel level)
    {
        EnsureSession();
        _session!.CreateVault(masterPassword, level);
        VaultExists = true;
        StatusMessage = "Vault created.";
        OnPropertyChanged(nameof(VaultExists));
    }

    public async Task CreateVaultAsync(string masterPassword, SecurityLevel level)
    {
        EnsureSession();
        await Task.Run(() => _session!.CreateVault(masterPassword, level)).ConfigureAwait(false);
        RunOnUi(() =>
        {
            VaultExists = true;
            StatusMessage = "Vault created.";
        });
    }

    public async Task<(bool ok, string? error)> UnlockAsync(
        string masterPassword, bool paranoiaMode = false, bool confirmRollback = false)
    {
        EnterpriseGate.RequireValidLicense(IsEnterprise, IsAdmin, IsLicenseValid);
        RunOnUi(() => IsBusy = true);
        try
        {
            EnsureSession();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() => _session!.Unlock(masterPassword, paranoiaMode, confirmRollback))
                .ConfigureAwait(false);
            sw.Stop();

            string? rollbackWarning = null;
            RunOnUi(() =>
            {
                ApplyPersonalAutoLockTimeout();
                RefreshEntries();
                StatusMessage = $"Unlocked in {sw.ElapsedMilliseconds} ms";
                if (IsReadOnly) StatusMessage += " [READ-ONLY — rollback detected]";
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(IsReadOnly));
                rollbackWarning = _session!.RollbackWarning;
                UnlockOccurred?.Invoke();
            });
            return (true, rollbackWarning);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            RunOnUi(() => StatusMessage = "Unlock failed.");
            return (false, "Incorrect master password.");
        }
        catch (Exception ex)
        {
            RunOnUi(() => StatusMessage = "Unlock failed.");
            return (false, FormatError(ex));
        }
        finally { RunOnUi(() => IsBusy = false); }
    }

    public async Task<(bool ok, string? error)> UnlockWithMasterKeyAsync(
        byte[] masterKey, bool paranoiaMode = false, bool confirmRollback = false)
    {
        EnterpriseGate.RequireValidLicense(IsEnterprise, IsAdmin, IsLicenseValid);
        RunOnUi(() => IsBusy = true);
        try
        {
            EnsureSession();
            await Task.Run(() => _session!.UnlockWithMasterKey(masterKey, paranoiaMode, confirmRollback))
                .ConfigureAwait(false);

            string? rollbackWarning = null;
            RunOnUi(() =>
            {
                ApplyPersonalAutoLockTimeout();
                RefreshEntries();
                StatusMessage = "Unlocked with Windows Hello";
                if (IsReadOnly) StatusMessage += " [READ-ONLY — rollback detected]";
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(IsReadOnly));
                rollbackWarning = _session!.RollbackWarning;
                UnlockOccurred?.Invoke();
            });
            return (true, rollbackWarning);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            RunOnUi(() => StatusMessage = "Unlock failed.");
            return (false, "Windows Hello credential is invalid for this vault.");
        }
        catch (Exception ex)
        {
            RunOnUi(() => StatusMessage = "Unlock failed.");
            return (false, FormatError(ex));
        }
        finally { RunOnUi(() => IsBusy = false); }
    }

    public void Lock() => RunOnUi(LockCore);

    public void PanicLock() => RunOnUi(PanicLockCore);

    private void LockCore()
    {
        if (_isLocking) return;
        _isLocking = true;
        try
        {
            _session?.Lock();
            Entries.Clear();
            StatusMessage = "Locked";
            OnPropertyChanged(nameof(IsUnlocked));
            LockOccurred?.Invoke();
        }
        finally { _isLocking = false; }
    }

    private void PanicLockCore()
    {
        if (_isLocking) return;
        _isLocking = true;
        try
        {
            _session?.PanicLock();
            Entries.Clear();
            StatusMessage = "Locked";
            OnPropertyChanged(nameof(IsUnlocked));
            LockOccurred?.Invoke();
        }
        finally { _isLocking = false; }
    }

    private void RunOnUi(Action action)
    {
        if (_uiInvoker is null) { action(); return; }
        _uiInvoker(action);
    }

    private static string FormatError(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    private void ApplyPersonalAutoLockTimeout()
    {
        if (IsEnterprise) return;
        var seconds = Policy?.MaxAutoLockSeconds ?? _personalSettings.AutoLockSeconds;
        _session?.SetAutoLockTimeout(seconds);
    }

    // ── Entry operations ──────────────────────────────────────────────────────

    public void AddEntry(VaultEntry entry)
    {
        RequireSession().AddEntry(entry);
        RefreshEntries();
    }

    public void UpdateEntry(VaultEntry entry)
    {
        RequireSession().UpdateEntry(entry);
        RefreshEntries();
    }

    public void DeleteEntry(Guid id)
    {
        RequireSession().DeleteEntry(id);
        RefreshEntries();
    }

    public VaultUnlockContext? Context => _session?.Context;
    public VaultSession? Session => _session;

    public void BulkImport(IEnumerable<VaultEntry> entries)
    {
        EnterpriseGate.RequireValidLicense(IsEnterprise, IsAdmin, IsLicenseValid);
        if (!IsUnlocked)
            throw new InvalidOperationException("Unlock the vault before importing entries.");

        var session = RequireSession();
        session.SuppressAutoLock();
        try
        {
            session.BulkImport(entries);
            RefreshEntries();
        }
        finally
        {
            session.ResumeAutoLock();
        }
    }

    public void ChangeMasterPassword(string newPassword)
        => RequireSession().ChangeMasterPassword(newPassword);

    public async Task ChangeMasterPasswordAsync(string newPassword)
    {
        EnsureSession();
        RunOnUi(() => IsBusy = true);
        _session!.SuppressAutoLock();
        try
        {
            await Task.Run(() => _session.ChangeMasterPassword(newPassword)).ConfigureAwait(false);
            RunOnUi(RefreshEntries);
        }
        finally
        {
            _session?.ResumeAutoLock();
            RunOnUi(() => IsBusy = false);
        }
    }

    public IEnumerable<VaultEntryViewModel> Search(string query)
        => _session?.Search(query).Select(e => new VaultEntryViewModel(e)) ?? [];

    public PasswordHealthReport GetHealthReport()
        => PasswordHealthAnalyzer.Analyze(_session?.AllEntries() ?? []);

    public SecurityAuditReport GetSecurityAuditReport(bool helloConfigured)
    {
        var entries = _session?.AllEntries() ?? [];
        var policy = Policy;
        var autoLock = policy?.MaxAutoLockSeconds ?? _personalSettings.AutoLockSeconds;
        var clipboard = policy is not null
            ? PolicyEnforcer.GetClipboardClearSeconds(policy, _personalSettings.ClipboardClearSeconds)
            : _personalSettings.ClipboardClearSeconds;

        IReadOnlyList<Core.Audit.AuditEvent>? auditEvents = null;
        if (IsEnterprise)
        {
            try { auditEvents = Core.Audit.AuditLogger.Default.ReadRecent(1000); }
            catch { /* audit dir may be unavailable */ }
        }

        return SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = entries,
            AutoLockSeconds = autoLock,
            ClipboardClearSeconds = clipboard,
            WindowsHelloConfigured = helloConfigured,
            ParanoiaMode = PreferParanoiaMode,
            SnapshotCount = ListSnapshots().Count,
            AuditEvents = auditEvents,
            IncludeActivityAudit = IsEnterprise
        });
    }

    public string GeneratePassword(int length, PasswordGeneratorMode mode)
        => PasswordGenerator.Generate(length, mode);

    public string GeneratePassword(PasswordGeneratorOptions options)
        => PasswordGenerator.Generate(options);

    public PasswordStrengthResult AnalyzeStrength(string password)
        => PasswordStrengthAnalyzer.Analyze(password);

    public IReadOnlyList<string> ListSnapshots()
        => _session?.ListSnapshots() ?? [];

    public void ReloadPolicies()
    {
        if (!IsEnterprise) return;
        Policy = TryLoadPolicy();
        _session?.ApplyPolicy(Policy);
        OnPropertyChanged(nameof(Policy));
        StatusMessage = "Policies reloaded";
    }

    public void ReloadLicense()
    {
        if (!IsEnterprise) return;
        License = LicenseStore.Load();
        OnPropertyChanged(nameof(License));
        OnPropertyChanged(nameof(IsLicenseValid));
    }

    public void ReloadEnterpriseConfig()
    {
        if (!IsEnterprise) return;
        ReloadLicense();
        ReloadPolicies();
        EnterpriseConfigChanged?.Invoke();
        StatusMessage = IsLicenseValid ? "Enterprise configuration reloaded." : "License still invalid or missing.";
    }

    public void ResetAutoLock() => _session?.ResetAutoLock();

    /// <summary>Point Personal edition at a vault on removable media (locks first if open).</summary>
    public void SwitchToPortableVault(string vaultDirectory)
    {
        if (IsEnterprise || IsAdmin)
            throw new InvalidOperationException("Portable vault mode is only available in Fortiva Personal.");
        if (!PolicyEnforcer.CanUsePortableMode(Policy))
            throw new InvalidOperationException("Portable mode is forbidden by policy.");

        vaultDirectory = Path.GetFullPath(vaultDirectory);
        var vaultFile = Path.Combine(vaultDirectory, VaultConstants.VaultFileName);
        if (!File.Exists(vaultFile))
            throw new FileNotFoundException("No vault.fva found in the selected folder.", vaultFile);

        ApplyVaultDirectory(vaultDirectory, portable: true);
    }

    /// <summary>Set portable vault location for a new vault (onboarding will create vault.fva).</summary>
    public void PreparePortableVaultLocation(string vaultDirectory)
    {
        if (IsEnterprise || IsAdmin)
            throw new InvalidOperationException("Portable vault mode is only available in Fortiva Personal.");
        if (!PolicyEnforcer.CanUsePortableMode(Policy))
            throw new InvalidOperationException("Portable mode is forbidden by policy.");

        vaultDirectory = Path.GetFullPath(vaultDirectory);
        Directory.CreateDirectory(vaultDirectory);
        var vaultFile = Path.Combine(vaultDirectory, VaultConstants.VaultFileName);
        if (File.Exists(vaultFile))
            throw new InvalidOperationException("A vault already exists at this location.");

        ApplyVaultDirectory(vaultDirectory, portable: true);
    }

    /// <summary>Return Personal edition to the default local profile vault.</summary>
    public void SwitchToLocalVault()
    {
        if (IsEnterprise || IsAdmin)
            return;
        if (!IsPortableMode)
            return;
        ApplyVaultDirectory(FortivaPaths.PersonalVaultDirectory, portable: false);
    }

    private void TryRestorePortableVault()
    {
        var saved = _personalSettings.PortableVaultDirectory;
        if (string.IsNullOrWhiteSpace(saved))
            return;

        saved = Path.GetFullPath(saved);
        if (!Directory.Exists(saved))
        {
            StatusMessage = "Portable vault drive not connected — using local vault.";
            return;
        }

        ApplyVaultDirectory(saved, portable: true, notify: false);
    }

    private void ApplyVaultDirectory(string directory, bool portable, bool notify = true)
    {
        if (IsUnlocked)
            LockCore();

        _session = null;
        VaultDirectory = directory;
        IsPortableMode = portable;
        if (!IsEnterprise)
        {
            _personalSettings.PortableVaultDirectory = portable ? directory : null;
            SavePersonalSettings();
        }
        RefreshVaultExists();
        OnPropertyChanged(nameof(VaultDirectory));
        OnPropertyChanged(nameof(IsPortableMode));
        OnPropertyChanged(nameof(VaultLocationLabel));
        StatusMessage = portable
            ? $"Using portable vault at {directory}"
            : "Using local vault";
        if (notify)
            VaultLocationChanged?.Invoke();
    }

    public bool VerifyMasterPassword(string candidatePassword)
        => _session?.VerifyMasterPassword(candidatePassword) ?? false;

    /// <summary>Re-bind Windows Hello after master password verification.</summary>
    public void SyncHelloCredential(string masterPassword)
    {
        if (!VerifyMasterPassword(masterPassword))
            throw new InvalidOperationException("Master password verification failed.");
        SyncHelloCredentialFromSession();
    }

    public void SyncHelloCredentialFromSession()
    {
        var session = RequireSession();
        var mk = session.CopyMasterKeyForHelloSetup();
        var hello = new WindowsHelloKeyProtector(
            FortivaPaths.GetHelloDataDirectory(IsEnterprise),
            IsEnterprise);
        try
        {
            hello.StoreHelloBundle(mk, helloVerified: true);
        }
        finally
        {
            SecureMemory.Zero(mk);
        }
    }

    public void ClearHelloCredential()
    {
        new WindowsHelloKeyProtector(
            FortivaPaths.GetHelloDataDirectory(IsEnterprise),
            IsEnterprise).Clear();
    }

    public AuditLogger GetAuditLogger() =>
        IsEnterprise ? AuditLogger.ForEnterprise() : AuditLogger.ForPersonal();

    /// <summary>Test hook — reset session state between automated tests.</summary>
    internal void ResetForTesting()
    {
        _session?.Dispose();
        _session = null;
        Entries.Clear();
        VaultExists = false;
        StatusMessage = "Welcome to Fortiva";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureSession()
    {
        if (_session is not null) return;
        _session = new VaultSession(
            VaultDirectory,
            IsEnterprise ? DpapiScope.LocalMachine : DpapiScope.CurrentUser,
            Policy,
            enableAudit: true,
            auditDirectory: IsEnterprise ? null : FortivaPaths.PersonalAuditDirectory,
            requireEnterpriseLicense: IsEnterprise && !IsAdmin,
            enterpriseClient: Edition == "Enterprise");
        _session.AutoLockRequested += () =>
        {
            if (IsBusy) return;
            RunOnUi(LockCore);
        };
    }

    private VaultSession RequireSession()
    {
        if (_session is null || !IsUnlocked)
            throw new InvalidOperationException("Vault not unlocked.");
        return _session;
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        if (_session is null) return;
        foreach (var e in _session.AllEntries())
            Entries.Add(new VaultEntryViewModel(e));
        OnPropertyChanged(nameof(IsUnlocked));
        StateChanged?.Invoke();
    }

    private FortivaPolicy? TryLoadPolicy()
    {
        try { return PolicyStore.Load(enterpriseDefaultsWhenMissing: IsEnterprise); }
        catch { return FortivaPolicy.StrictEnterprise; }
    }
}

// ── Entry view model ─────────────────────────────────────────────────────────

public sealed class VaultEntryViewModel : ViewModelBase
{
    private bool _passwordVisible;
    private CancellationTokenSource? _revealCts;

    public VaultEntry Entry { get; }

    public VaultEntryViewModel(VaultEntry entry) => Entry = entry;

    public Guid Id => Entry.Id;
    public string Initial => string.IsNullOrEmpty(Entry.Title) ? "?" : Entry.Title[0].ToString().ToUpperInvariant();
    public string Title => Entry.Title;
    public string Username => Entry.Username;
    public string Url => Entry.Url;
    public string Notes => Entry.Notes;
    public bool IsSecureNote => Entry.IsSecureNote;
    public bool IsFavorite => Entry.IsFavorite;
    public string Tags => string.Join(", ", Entry.Tags);
    public bool HasTotp => !string.IsNullOrWhiteSpace(Entry.TotpSecret);
    public DateTimeOffset ModifiedAt => Entry.ModifiedAt;

    public string MaskedPassword => _passwordVisible ? Entry.Password : new string('•', Math.Min(Entry.Password.Length, 16));
    public bool PasswordVisible { get => _passwordVisible; private set { Set(ref _passwordVisible, value); OnPropertyChanged(nameof(MaskedPassword)); } }

    public string DomainDisplay => TryGetDomain(Entry.Url);

    public void RevealPasswordFor(int seconds = 5)
    {
        _revealCts?.Cancel();
        _revealCts = new CancellationTokenSource();
        PasswordVisible = true;
        var token = _revealCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), token).ContinueWith(t =>
        {
            if (!t.IsCanceled) PasswordVisible = false;
        }, TaskScheduler.Default);
    }

    public void HidePassword()
    {
        _revealCts?.Cancel();
        PasswordVisible = false;
    }

    private static string TryGetDomain(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }
}
