using Fortiva.Core.Audit;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Crypto;
using Fortiva.Core.Licensing;
using Fortiva.Core.LocalState;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Services;

public sealed class VaultSession : IDisposable
{
    private readonly VaultEngine _engine;
    private FortivaPolicy? _policy;
    private readonly AuditLogger? _audit;
    private readonly bool _requireEnterpriseLicense;
    private readonly bool _enterpriseClient;
    private BrowserBridgeServer? _bridge;
    private AutoLockTimer? _autoLock;
    private VaultUnlockContext? _context;
    private string? _bridgeSessionToken;
    private readonly object _payloadLock = new();

    public VaultSession(
        string vaultDirectory,
        DpapiScope scope,
        FortivaPolicy? policy = null,
        bool enableAudit = false,
        bool requireEnterpriseLicense = false,
        bool enterpriseClient = false)
    {
        _engine = new VaultEngine(vaultDirectory, scope, policy);
        _policy = policy;
        _requireEnterpriseLicense = requireEnterpriseLicense;
        _enterpriseClient = enterpriseClient;
        if (enableAudit)
            _audit = new AuditLogger();
    }

    public bool IsUnlocked => _context is not null;
    public bool IsReadOnly => _context?.ReadOnly ?? false;
    public VaultUnlockContext? Context => _context;
    public string? RollbackWarning => _context?.RollbackWarning;
    public bool VaultExists => _engine.VaultExists;

    public void CreateVault(string masterPassword, SecurityLevel level)
        => _engine.CreateVault(masterPassword, level);

    public void Unlock(string masterPassword, bool paranoiaMode = false, bool confirmRollback = false)
    {
        EnsureEnterpriseLicense();
        _audit?.Log(AuditEventType.UnlockAttempt, "Unlock attempted");
        try
        {
            DisposeSession();
            _context = _engine.Unlock(masterPassword, paranoiaMode, confirmRollback);
            _audit?.Log(AuditEventType.UnlockSuccess, "Unlock succeeded");
            StartInfrastructure();
            StateChanged?.Invoke();
        }
        catch (Exception)
        {
            _audit?.Log(AuditEventType.UnlockFailure, "Unlock failed", success: false);
            throw;
        }
    }

    public void UnlockWithMasterKey(byte[] masterKey, bool paranoiaMode = false, bool confirmRollback = false)
    {
        EnsureEnterpriseLicense();
        _audit?.Log(AuditEventType.UnlockAttempt, "Hello unlock attempted");
        byte[]? mkCopy = null;
        try
        {
            mkCopy = masterKey.ToArray();
            DisposeSession();
            _context = _engine.UnlockWithMasterKey(mkCopy, paranoiaMode, confirmRollback);
            _audit?.Log(AuditEventType.UnlockSuccess, "Hello unlock succeeded");
            StartInfrastructure();
            StateChanged?.Invoke();
        }
        catch (Exception)
        {
            _audit?.Log(AuditEventType.UnlockFailure, "Hello unlock failed", success: false);
            throw;
        }
        finally
        {
            if (mkCopy is not null) SecureMemory.Zero(mkCopy);
        }
    }

    public void Lock()
    {
        DisposeSession();
        _audit?.Log(AuditEventType.Lock, "Vault locked");
        StateChanged?.Invoke();
    }

    public void PanicLock()
    {
        DisposeSession();
        _audit?.Log(AuditEventType.Lock, "Panic lock");
        StateChanged?.Invoke();
    }

    public VaultUnlockContext UnlockFromSnapshot(int snapshotIndex, string masterPassword)
    {
        EnsureEnterpriseLicense();
        DisposeSession();
        _context = _engine.UnlockFromSnapshot(snapshotIndex, masterPassword);
        _audit?.Log(AuditEventType.SnapshotRestore, $"Restored from snapshot {snapshotIndex}");
        StartInfrastructure();
        StateChanged?.Invoke();
        return _context;
    }

    public byte[] CopyMasterKeyForHelloSetup()
    {
        if (_context is null) throw new InvalidOperationException("Vault not unlocked.");
        return _context.Keys.MasterKey.ToArray();
    }

    public void ChangeMasterPassword(string newPassword, Argon2Parameters? kdf = null)
    {
        EnsureWritable();
        _engine.ChangeMasterPassword(_context!, newPassword, kdf);
        _audit?.Log(AuditEventType.ConfigurationChange, "Master password changed");
    }

    public bool VerifyMasterPassword(string candidatePassword)
    {
        if (_context is null) return false;
        return _engine.VerifyMasterPassword(_context, candidatePassword);
    }

    public void AddEntry(VaultEntry entry)
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.AddEntry(_context!, entry);
        StateChanged?.Invoke();
    }

    public void UpdateEntry(VaultEntry entry)
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.UpdateEntry(_context!, entry);
        StateChanged?.Invoke();
    }

    public void DeleteEntry(Guid entryId)
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.DeleteEntry(_context!, entryId);
        StateChanged?.Invoke();
    }

    public void BulkImport(IEnumerable<VaultEntry> entries)
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.BulkImport(_context!, entries);
        _audit?.Log(AuditEventType.ConfigurationChange, "Bulk import completed");
        StateChanged?.Invoke();
    }

    public void Save()
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.Save(_context!);
    }

    public IReadOnlyList<VaultEntry> AllEntries()
    {
        lock (_payloadLock)
        {
            if (_context is null) return [];
            return _context.Payload.Entries.AsReadOnly();
        }
    }

    public IEnumerable<VaultEntry> Search(string query)
    {
        lock (_payloadLock)
        {
            if (_context is null) yield break;
            foreach (var e in _context.Payload.Entries)
            {
                if (string.IsNullOrEmpty(query) ||
                    e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    yield return e;
            }
        }
    }

    public IReadOnlyList<string> ListSnapshots()
        => _engine.Snapshots.ListSnapshots();

    public CredentialResponse ResolveForDomain(CredentialRequest req)
    {
        lock (_payloadLock)
        {
            if (_context is null) return new CredentialResponse();

            var requestHost = ResolveRequestHost(req);
            if (string.IsNullOrEmpty(requestHost)) return new CredentialResponse();

            var match = _context.Payload.Entries
                .Where(e => !e.IsSecureNote && !string.IsNullOrEmpty(e.Url))
                .FirstOrDefault(e => EntryHostMatches(e.Url, requestHost));
            if (match is null) return new CredentialResponse();

            _audit?.Log(AuditEventType.SharedVaultAccess,
                $"Browser bridge credential served for host {requestHost} (entry {match.Id})");

            return new CredentialResponse
            {
                Found = true,
                Username = match.Username,
                Password = match.Password,
                PasskeyCredentialId = match.PasskeyCredentialId
            };
        }
    }

    private static string ResolveRequestHost(CredentialRequest req)
    {
        var fromUrl = ExtractHost(req.Url);
        if (!string.IsNullOrEmpty(fromUrl)) return fromUrl;
        return req.Domain.Trim().ToLowerInvariant();
    }

    private static bool EntryHostMatches(string entryUrl, string requestHost)
    {
        var entryHost = ExtractHost(entryUrl);
        return entryHost is not null &&
               string.Equals(entryHost, requestHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            if (!url.Contains("://", StringComparison.Ordinal))
                url = "https://" + url;
            return new Uri(url, UriKind.Absolute).Host.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public void ResetAutoLock() => _autoLock?.ResetActivity();

    public void SetAutoLockTimeout(int seconds)
    {
        if (_autoLock is not null) _autoLock.TimeoutSeconds = seconds;
    }

    public void ApplyPolicy(FortivaPolicy? policy)
    {
        _policy = policy;
        if (_autoLock is not null && policy is not null)
        {
            var timeout = PolicyEnforcer.EnforceAutoLock(_autoLock.TimeoutSeconds, policy);
            _autoLock.TimeoutSeconds = timeout;
        }
    }

    public event Action? AutoLockRequested;
    public event Action? StateChanged;

    private void EnsureEnterpriseLicense()
    {
        if (!_requireEnterpriseLicense) return;
        if (!LicenseVerifier.IsValidAndNotExpired(LicenseStore.Load()))
            throw new InvalidOperationException("A valid enterprise license is required.");
    }

    private void EnsureWritable()
    {
        if (_context is null) throw new InvalidOperationException("Vault not unlocked.");
        if (_context.ReadOnly) throw new InvalidOperationException("Vault is read-only.");
    }

    private void StartInfrastructure()
    {
        _bridge?.Dispose();
        BridgeSessionAuth.ConfigureTokenDirectory(FortivaPaths.GetBridgeSessionDirectory(_enterpriseClient));
        BridgeSessionAuth.ClearSessionToken();
        _bridgeSessionToken = BridgeSessionAuth.CreateSessionToken();
        _bridge = new BrowserBridgeServer(ResolveForDomain, _bridgeSessionToken);
        _bridge.Start();

        _autoLock?.Dispose();
        var timeout = PolicyEnforcer.EnforceAutoLock(300, _policy ?? new FortivaPolicy());
        _autoLock = new AutoLockTimer(timeout);
        _autoLock.LockRequested += () => AutoLockRequested?.Invoke();
    }

    private void DisposeSession()
    {
        _bridge?.Dispose();
        _bridge = null;
        BridgeSessionAuth.ClearSessionToken();
        _bridgeSessionToken = null;
        _autoLock?.Dispose();
        _autoLock = null;
        _context?.Keys.Lock();
        _context?.Keys.Dispose();
        _context = null;
    }

    public void Dispose() => DisposeSession();
}
