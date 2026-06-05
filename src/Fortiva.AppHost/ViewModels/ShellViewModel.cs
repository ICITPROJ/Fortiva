using System.Collections.ObjectModel;
using System.Text;
using Fortiva.AppHost.Services;
using Fortiva.Core.Admin;
using Fortiva.Core.Audit;
using Fortiva.Core.BrowserBridge;
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
using Windows.ApplicationModel.DataTransfer;

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
    private bool _deferAutoLock;
    private bool _vaultExists;
    private bool _isLocking;
    private Action<Action>? _uiInvoker;
    private PersonalUserSettings _personalSettings = PersonalUserSettings.Load();
    private EnterpriseUserSettings _enterpriseSettings = EnterpriseUserSettings.Load();
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
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            if (!value && _deferAutoLock)
            {
                _deferAutoLock = false;
                LockCore();
            }
        }
    }
    public bool IsUnlocked => _session?.IsUnlocked ?? false;
    public bool IsReadOnly => _session?.IsReadOnly ?? false;
    public bool PendingRollbackConfirm { get; set; }
    public bool VaultExists { get => _vaultExists; private set => Set(ref _vaultExists, value); }

    public string VaultDirectory { get; private set; } = FortivaPaths.PersonalVaultDirectory;
    public bool IsPortableMode { get; private set; }
    public bool PortableVaultUnavailable { get; private set; }
    public string? UnavailablePortablePath { get; private set; }
    public bool IsSharedVaultMode { get; private set; }
    public IReadOnlyList<SharedVaultDefinition> SharedVaults { get; private set; } = [];

    /// <summary>Human-readable vault location for settings UI.</summary>
    public string VaultLocationLabel =>
        IsPortableMode ? $"Portable: {VaultDirectory}"
        : IsSharedVaultMode ? $"Shared: {VaultDirectory}"
        : $"Local: {VaultDirectory}";

    /// <summary>Short trust chip on the vault toolbar.</summary>
    public string VaultTrustChipText =>
        IsPortableMode ? "Portable vault"
        : IsSharedVaultMode ? "Shared vault"
        : "Local only";

    public ObservableCollection<VaultEntryViewModel> Entries { get; } = [];

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action? LockOccurred;
    public event Action? UnlockOccurred;
    public event Action? StateChanged;
    public event Action? EnterpriseConfigChanged;
    public event Action? VaultLocationChanged;
    /// <summary>Request left-nav highlight without triggering navigation (e.g. toolbar shortcut).</summary>
    public event Action<string>? NavigationTabRequested;
    /// <summary>Browser extension needs vault unlocked — bring app forward and show unlock UI.</summary>
    public event Action? BridgeUnlockRequested;

    private BridgeUnlockBroker? _bridgeUnlockBroker;
    private readonly object _bridgeUnlockGate = new();
    private TaskCompletionSource<bool>? _bridgeUnlockWait;

    // ── Constructor ───────────────────────────────────────────────────────────
    private ShellViewModel()
    {
        if (IsEnterprise)
        {
            License = LicenseStore.Load();
            Policy = TryLoadPolicy();
            SharedVaults = SharedVaultStore.Load().Vaults;
            VaultDirectory = ResolveEnterpriseVaultDirectory();
        }
        else
        {
            FortivaPaths.MigrateLegacyPersonalVaultIfNeeded();
            TryRestorePortableVault();
        }
        VaultExists = File.Exists(Path.Combine(VaultDirectory, VaultConstants.VaultFileName));
    }

    public void RequestNavigationTab(string tag) => NavigationTabRequested?.Invoke(tag);

    /// <summary>Abort a pending browser-extension unlock wait (navigation away, user cancel).</summary>
    public void CancelBridgeUnlockIfPending() => CompleteBridgeUnlockIfPending(false);

    /// <summary>Starts unlock listener for browser extension (runs while app is open, even when locked).</summary>
    public void StartBridgeUnlockListener(string installRoot)
    {
        if (IsAdmin)
            return;

        _bridgeUnlockBroker?.Dispose();
        BridgeClientValidator.ConfigureAllowedInstallRoots(installRoot);
        _bridgeUnlockBroker = new BridgeUnlockBroker(
            () => IsUnlocked,
            () => VaultExists,
            RequestUnlockFromBridgeAsync);
        _bridgeUnlockBroker.Start();
    }

    private async Task<bool> RequestUnlockFromBridgeAsync(CancellationToken ct)
    {
        if (IsUnlocked)
            return true;

        if (!VaultExists)
            return false;

        TaskCompletionSource<bool> tcs;
        var notifyUi = false;
        lock (_bridgeUnlockGate)
        {
            if (_bridgeUnlockWait is not null)
            {
                tcs = _bridgeUnlockWait;
            }
            else
            {
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _bridgeUnlockWait = tcs;
                notifyUi = true;
            }
        }

        if (notifyUi)
            RunOnUi(() => BridgeUnlockRequested?.Invoke());

        await using var reg = ct.Register(() => CompleteBridgeUnlockIfPending(false));
        return await tcs.Task.ConfigureAwait(false);
    }

    private void CompleteBridgeUnlockIfPending(bool success)
    {
        TaskCompletionSource<bool>? wait;
        lock (_bridgeUnlockGate)
        {
            wait = _bridgeUnlockWait;
            if (wait is null)
                return;
            _bridgeUnlockWait = null;
        }

        wait.TrySetResult(success);
    }

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

    public void RecordUpdateApplyFailure(string message)
    {
        if (IsEnterprise || IsAdmin) return;
        _personalSettings.LastUpdateApplyFailedUtc = DateTimeOffset.UtcNow;
        _personalSettings.LastUpdateApplyError = message;
        SavePersonalSettings();
    }

    public void ClearUpdateApplyFailure()
    {
        if (IsEnterprise || IsAdmin) return;
        if (_personalSettings.LastUpdateApplyError is null && _personalSettings.LastUpdateApplyFailedUtc is null)
            return;
        _personalSettings.LastUpdateApplyFailedUtc = null;
        _personalSettings.LastUpdateApplyError = null;
        SavePersonalSettings();
    }

    public void SetHelloHardwareUpgradeDismissed(bool dismissed)
    {
        if (IsEnterprise) return;
        _personalSettings.HelloHardwareUpgradeDismissed = dismissed;
        SavePersonalSettings();
    }

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

    public void SetBrowserExtensionSetupDismissed(bool dismissed = true)
    {
        if (IsEnterprise) return;
        _personalSettings.BrowserExtensionSetupDismissed = dismissed;
        SavePersonalSettings();
    }

    /// <summary>Onboarding step 4 handles browser setup — suppress duplicate prompt on unlock.</summary>
    public bool SkipNextBrowserExtensionPrompt { get; set; }

    public void SetParanoiaMode(bool enabled)
    {
        _personalSettings.ParanoiaMode = enabled;
        SavePersonalSettings();
        OnPropertyChanged(nameof(PreferParanoiaMode));
        BrandAppearanceChanged?.Invoke();
    }

    /// <summary>All tags from entries plus user-saved empty categories.</summary>
    public IReadOnlyList<string> GetKnownVaultTags()
    {
        var entryTags = Entries.SelectMany(e => e.Entry.Tags);
        return VaultTagHelper.CollectKnownTags(entryTags, _personalSettings.VaultCategories);
    }

    /// <summary>Persist a sidebar category so it appears even with zero entries.</summary>
    public void EnsureVaultCategory(string tag)
    {
        var normalized = VaultTagHelper.NormalizeTag(tag);
        if (normalized is null)
            return;

        if (_personalSettings.VaultCategories.Any(c =>
                string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        _personalSettings.VaultCategories.Add(normalized);
        SavePersonalSettings();
        StateChanged?.Invoke();
    }

    private void SyncVaultCategoriesFromTags(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
            EnsureVaultCategory(tag);
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
        EnterpriseGate.RequireValidLicense(IsEnterprise, IsAdmin, IsLicenseValid);
        EnsureSession();
        _session!.CreateVault(masterPassword, level);
        VaultExists = true;
        StatusMessage = "Vault created.";
        OnPropertyChanged(nameof(VaultExists));
    }

    public async Task CreateVaultAsync(string masterPassword, SecurityLevel level)
    {
        EnterpriseGate.RequireValidLicense(IsEnterprise, IsAdmin, IsLicenseValid);
        EnsureSession();
        await Task.Run(() => _session!.CreateVault(masterPassword, level)).ConfigureAwait(false);
        try
        {
            RunOnUi(() =>
            {
                VaultExists = true;
                StatusMessage = "Vault created.";
                OnPropertyChanged(nameof(VaultExists));
            });
        }
        catch (Exception ex)
        {
            // Vault file may already be on disk even if UI bookkeeping failed.
            App.LogException("CreateVaultAsync.RunOnUi", ex);
            RunOnUi(RefreshVaultExists);
        }
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
                if (IsReadOnly) StatusMessage += " [READ-ONLY - rollback detected]";
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(IsReadOnly));
                rollbackWarning = _session!.RollbackWarning;
                var stayOnUnlock = IsReadOnly && !string.IsNullOrEmpty(rollbackWarning) && !confirmRollback;
                PendingRollbackConfirm = stayOnUnlock;
                if (!stayOnUnlock)
                    UnlockOccurred?.Invoke();
                CompleteBridgeUnlockIfPending(!stayOnUnlock);
            });
            return (true, rollbackWarning);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            RunOnUi(() =>
            {
                StatusMessage = "Unlock failed.";
                CompleteBridgeUnlockIfPending(false);
            });
            return (false, "Incorrect master password.");
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                StatusMessage = "Unlock failed.";
                CompleteBridgeUnlockIfPending(false);
            });
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
                if (IsReadOnly) StatusMessage += " [READ-ONLY - rollback detected]";
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(IsReadOnly));
                rollbackWarning = _session!.RollbackWarning;
                var stayOnUnlock = IsReadOnly && !string.IsNullOrEmpty(rollbackWarning) && !confirmRollback;
                PendingRollbackConfirm = stayOnUnlock;
                if (!stayOnUnlock)
                    UnlockOccurred?.Invoke();
                CompleteBridgeUnlockIfPending(!stayOnUnlock);
            });
            return (true, rollbackWarning);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            RunOnUi(() =>
            {
                StatusMessage = "Unlock failed.";
                CompleteBridgeUnlockIfPending(false);
            });
            return (false, "Windows Hello credential is invalid for this vault.");
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                StatusMessage = "Unlock failed.";
                CompleteBridgeUnlockIfPending(false);
            });
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
            ClearClipboardOnLock();
            _session?.Lock();
            Entries.Clear();
            StatusMessage = "Locked";
            OnPropertyChanged(nameof(IsUnlocked));
            StateChanged?.Invoke();
            LockOccurred?.Invoke();
            CompleteBridgeUnlockIfPending(false);
        }
        finally { _isLocking = false; }
    }

    private void PanicLockCore()
    {
        if (_isLocking) return;
        _isLocking = true;
        try
        {
            ClearClipboardOnLock();
            _session?.PanicLock();
            Entries.Clear();
            StatusMessage = "Locked";
            OnPropertyChanged(nameof(IsUnlocked));
            StateChanged?.Invoke();
            LockOccurred?.Invoke();
            CompleteBridgeUnlockIfPending(false);
        }
        finally { _isLocking = false; }
    }

    internal void InvokeOnUi(Action action) => RunOnUi(action);

    private static void ClearClipboardOnLock()
    {
        try
        {
            Clipboard.Clear();
        }
        catch
        {
            /* best effort */
        }
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
        var seconds = IsEnterprise
            ? (Policy?.MaxAutoLockSeconds ?? PersonalUserSettings.DefaultAutoLockSeconds)
            : _personalSettings.AutoLockSeconds;
        if (!IsEnterprise)
            seconds = Math.Clamp(seconds, PersonalUserSettings.MinAutoLockSeconds, PersonalUserSettings.MaxAutoLockSeconds);
        if (Policy is not null)
            seconds = PolicyEnforcer.EnforceAutoLock(seconds, Policy);
        _session?.SetAutoLockTimeout(seconds);
    }

    // ── Entry operations ──────────────────────────────────────────────────────

    public void AddEntry(VaultEntry entry)
    {
        SyncVaultCategoriesFromTags(entry.Tags);
        RequireSession().AddEntry(entry);
        RefreshEntries();
    }

    public void UpdateEntry(VaultEntry entry)
    {
        SyncVaultCategoriesFromTags(entry.Tags);
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

    /// <summary>Directory of the "other" vault that the active vault would sync against.</summary>
    public string? CounterpartVaultDirectory =>
        IsPortableMode
            ? FortivaPaths.PersonalVaultDirectory
            : _personalSettings.PortableVaultDirectory;

    public bool CanSyncWithCounterpart =>
        !IsEnterprise && !IsAdmin && IsUnlocked && !IsReadOnly &&
        PolicyEnforcer.CanUsePortableMode(Policy) &&
        !string.IsNullOrWhiteSpace(CounterpartVaultDirectory);

    /// <summary>
    /// Two-way sync between the active vault and its counterpart (local ⇄ USB). If the counterpart
    /// vault does not yet exist it is created with the supplied password (effectively cloning the
    /// active vault onto it). The counterpart's own master password is required to unlock it.
    /// </summary>
    public async Task<VaultSyncResult> SyncWithPortableAsync(string counterpartPassword)
    {
        if (IsEnterprise || IsAdmin)
            throw new InvalidOperationException("Vault sync is only available in Fortiva Personal.");
        if (!PolicyEnforcer.CanUsePortableMode(Policy))
            throw new InvalidOperationException("Portable mode is forbidden by policy.");

        var session = RequireSession();
        if (session.IsReadOnly)
            throw new InvalidOperationException("The active vault is read-only and cannot be synced.");

        var otherDir = CounterpartVaultDirectory;
        if (string.IsNullOrWhiteSpace(otherDir))
            throw new InvalidOperationException("No USB/portable vault location is configured to sync with.");

        otherDir = Path.GetFullPath(otherDir);
        if (string.Equals(otherDir, Path.GetFullPath(VaultDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The counterpart vault is the same as the active vault.");

        if (IsPortableMode)
        {
            // Active vault is the USB; its directory must still be reachable.
            if (!Directory.Exists(VaultDirectory))
                throw new DirectoryNotFoundException("The portable vault drive is no longer connected.");
        }
        else if (!Directory.Exists(otherDir))
        {
            throw new DirectoryNotFoundException(
                "The USB/portable vault drive is not connected. Reconnect it and try again.");
        }

        var level = session.Context?.Header.SecurityLevel ?? SecurityLevel.Standard;

        return await Task.Run(() =>
        {
            session.SuppressAutoLock();
            VaultUnlockContext? otherContext = null;
            try
            {
                var otherEngine = new VaultEngine(otherDir, DpapiScope.CurrentUser, Policy);
                if (!otherEngine.VaultExists)
                    otherEngine.CreateVault(counterpartPassword, level);

                otherContext = otherEngine.Unlock(counterpartPassword);
                var result = session.SyncWith(otherEngine, otherContext);
                RunOnUi(RefreshEntries);
                return result;
            }
            finally
            {
                otherContext?.Keys.Dispose();
                session.ResumeAutoLock();
            }
        }).ConfigureAwait(false);
    }

    private void TryRestorePortableVault()
    {
        var saved = _personalSettings.PortableVaultDirectory;
        if (string.IsNullOrWhiteSpace(saved))
            return;

        saved = Path.GetFullPath(saved);
        if (!Directory.Exists(saved))
        {
            PortableVaultUnavailable = true;
            UnavailablePortablePath = saved;
            StatusMessage = "Portable vault drive not connected - using local vault.";
            return;
        }

        ApplyVaultDirectory(saved, portable: true, notify: false);
    }

    public void DismissPortableVaultUnavailable()
    {
        PortableVaultUnavailable = false;
        OnPropertyChanged(nameof(PortableVaultUnavailable));
    }

    public bool RetryPortableVaultConnection()
    {
        if (string.IsNullOrWhiteSpace(UnavailablePortablePath))
            return false;
        var saved = Path.GetFullPath(UnavailablePortablePath);
        if (!Directory.Exists(saved))
            return false;

        PortableVaultUnavailable = false;
        UnavailablePortablePath = null;
        ApplyVaultDirectory(saved, portable: true);
        OnPropertyChanged(nameof(PortableVaultUnavailable));
        return true;
    }

    private string ResolveEnterpriseVaultDirectory()
    {
        var selected = _enterpriseSettings.SelectedVaultDirectory;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            selected = Path.GetFullPath(selected);
            if (Directory.Exists(selected))
            {
                IsSharedVaultMode = !string.Equals(
                    selected,
                    Path.GetFullPath(FortivaPaths.EnterpriseProgramData),
                    StringComparison.OrdinalIgnoreCase);
                return selected;
            }
        }

        IsSharedVaultMode = false;
        return FortivaPaths.EnterpriseProgramData;
    }

    public void SwitchEnterpriseVault(string? vaultDirectory)
    {
        if (!IsEnterprise || IsAdmin)
            throw new InvalidOperationException("Shared vault selection is only available in Fortiva Enterprise.");

        vaultDirectory = string.IsNullOrWhiteSpace(vaultDirectory)
            ? FortivaPaths.EnterpriseProgramData
            : Path.GetFullPath(vaultDirectory);

        if (!Directory.Exists(vaultDirectory))
            throw new DirectoryNotFoundException($"Vault directory not found: {vaultDirectory}");

        _enterpriseSettings.SelectedVaultDirectory =
            string.Equals(vaultDirectory, FortivaPaths.EnterpriseProgramData, StringComparison.OrdinalIgnoreCase)
                ? null
                : vaultDirectory;
        _enterpriseSettings.Save();

        ApplyVaultDirectory(vaultDirectory, portable: false);
        IsSharedVaultMode = _enterpriseSettings.SelectedVaultDirectory is not null;
        OnPropertyChanged(nameof(IsSharedVaultMode));
        OnPropertyChanged(nameof(VaultTrustChipText));
    }

    public void ReloadSharedVaults()
    {
        if (!IsEnterprise) return;
        SharedVaults = SharedVaultStore.Load().Vaults;
        OnPropertyChanged(nameof(SharedVaults));
    }

    private void ApplyVaultDirectory(string directory, bool portable, bool notify = true)
    {
        if (IsUnlocked)
            LockCore();

        _session = null;
        VaultDirectory = directory;
        IsPortableMode = portable;
        IsSharedVaultMode = IsEnterprise && !portable &&
            !string.Equals(directory, FortivaPaths.EnterpriseProgramData, StringComparison.OrdinalIgnoreCase);
        if (!IsEnterprise)
        {
            _personalSettings.PortableVaultDirectory = portable ? directory : null;
            SavePersonalSettings();
        }
        RefreshVaultExists();
        OnPropertyChanged(nameof(VaultDirectory));
        OnPropertyChanged(nameof(IsPortableMode));
        OnPropertyChanged(nameof(IsSharedVaultMode));
        OnPropertyChanged(nameof(VaultLocationLabel));
        OnPropertyChanged(nameof(VaultTrustChipText));
        StatusMessage = portable
            ? $"Using portable vault at {directory}"
            : IsSharedVaultMode
                ? $"Using shared vault at {directory}"
                : IsEnterprise
                    ? "Using organization vault"
                    : "Using local vault";
        if (notify)
            VaultLocationChanged?.Invoke();
    }

    public bool VerifyMasterPassword(string candidatePassword)
        => _session?.VerifyMasterPassword(candidatePassword) ?? false;

    /// <summary>Re-bind Windows Hello after master password verification.</summary>
    public async Task SyncHelloCredentialAsync(string masterPassword)
    {
        if (!VerifyMasterPassword(masterPassword))
            throw new InvalidOperationException("Master password verification failed.");
        await SyncHelloCredentialFromSessionAsync().ConfigureAwait(false);
    }

    public async Task SyncHelloCredentialFromSessionAsync()
    {
        RuntimeIntegrity.EnsureSafeForSensitiveOperation();
        var session = RequireSession();
        var mk = session.CopyMasterKeyForHelloSetup();
        var manager = new HelloUnlockManager(
            FortivaPaths.GetHelloDataDirectory(IsEnterprise),
            IsEnterprise);
        try
        {
            await manager.StoreFromMasterKeyAsync(mk).ConfigureAwait(false);
        }
        finally
        {
            SecureMemory.Zero(mk);
        }
    }

    public async Task ClearHelloCredentialAsync()
    {
        await new HelloUnlockManager(
            FortivaPaths.GetHelloDataDirectory(IsEnterprise),
            IsEnterprise).ClearAsync().ConfigureAwait(false);
    }

    [Obsolete("Use SyncHelloCredentialAsync")]
    public void SyncHelloCredential(string masterPassword)
        => SyncHelloCredentialAsync(masterPassword).GetAwaiter().GetResult();

    [Obsolete("Use SyncHelloCredentialFromSessionAsync")]
    public void SyncHelloCredentialFromSession()
        => SyncHelloCredentialFromSessionAsync().GetAwaiter().GetResult();

    [Obsolete("Use ClearHelloCredentialAsync")]
    public void ClearHelloCredential()
        => ClearHelloCredentialAsync().GetAwaiter().GetResult();

    public AuditLogger GetAuditLogger() =>
        IsEnterprise ? AuditLogger.ForEnterprise() : AuditLogger.ForPersonal();

    public void LogPolicyViolation(string message)
    {
        try
        {
            GetAuditLogger().Log(AuditEventType.PolicyViolation, message, success: false);
        }
        catch
        {
            /* audit dir may be unavailable */
        }
    }

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

        if (IsEnterprise)
        {
            var rollbackDir = FortivaPaths.GetRollbackStateDirectory(VaultDirectory, enterprise: true);
            DpapiLocalStateStore.MigrateEnterpriseRollbackState(VaultDirectory, rollbackDir);
        }

        _session = new VaultSession(
            VaultDirectory,
            DpapiScope.CurrentUser,
            Policy,
            enableAudit: true,
            auditDirectory: IsEnterprise ? null : FortivaPaths.PersonalAuditDirectory,
            requireEnterpriseLicense: IsEnterprise && !IsAdmin,
            enterpriseClient: Edition == "Enterprise",
            rollbackStateDirectory: FortivaPaths.GetRollbackStateDirectory(VaultDirectory, IsEnterprise));
        ApplyPersonalAutoLockTimeout();
        _session.AutoLockRequested += () =>
        {
            RunOnUi(() =>
            {
                if (IsBusy)
                {
                    _deferAutoLock = true;
                    return;
                }
                LockCore();
            });
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

    public string Subtitle
    {
        get
        {
            if (IsSecureNote) return "Secure note";
            if (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(DomainDisplay))
                return $"{Username} · {DomainDisplay}";
            if (!string.IsNullOrWhiteSpace(Username)) return Username;
            if (!string.IsNullOrWhiteSpace(DomainDisplay)) return DomainDisplay;
            return "No username or URL";
        }
    }

    public bool HasUsernameLine => !IsSecureNote && !string.IsNullOrWhiteSpace(Username);
    public string UsernameLine => Username;
    public bool HasDetailLine => !IsSecureNote && !string.IsNullOrWhiteSpace(DomainDisplay);
    public string DetailLine => DomainDisplay;
    public bool IsSecureNoteEntry => IsSecureNote;
    public bool HasMissingDetails => !IsSecureNote && !HasUsernameLine && !HasDetailLine;

    public bool HasTagChip => Entry.Tags.Count > 0;
    public string TagChip => HasTagChip ? Entry.Tags[0] : "";
    public int ExtraTagCount => Math.Max(0, Entry.Tags.Count - 1);
    public bool HasExtraTags => ExtraTagCount > 0;
    public string ExtraTagsLabel => HasExtraTags ? $"+{ExtraTagCount}" : "";

    public void RevealPasswordFor(int seconds = 5)
    {
        _revealCts?.Cancel();
        _revealCts = new CancellationTokenSource();
        PasswordVisible = true;
        var token = _revealCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                ShellViewModel.Current.InvokeOnUi(() => PasswordVisible = false);
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
