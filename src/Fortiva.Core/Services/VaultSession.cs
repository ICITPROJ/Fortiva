using Fortiva.Core.Audit;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Crypto;
using Fortiva.Core.Licensing;
using Fortiva.Core.LocalState;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;
using System.Threading;

namespace Fortiva.Core.Services;

public sealed class VaultSession : IDisposable
{
    private readonly VaultEngine _engine;
    private FortivaPolicy? _policy;
    private readonly AuditLogger? _audit;
    private readonly bool _requireEnterpriseLicense;
    private readonly bool _enterpriseClient;
    private BrowserBridgeServer? _bridge;
    private BridgeTokenBroker? _tokenBroker;
    private AutoLockTimer? _autoLock;
    private VaultUnlockContext? _context;
    private string? _bridgeSessionToken;
    private readonly object _payloadLock = new();
    private int _autoLockSuppressCount;
    private int _autoLockTimeoutSeconds = 300;
    private readonly string _rollbackStateDirectory;
    private BridgeFillNonce? _fillNonce;

    public VaultSession(
        string vaultDirectory,
        DpapiScope scope,
        FortivaPolicy? policy = null,
        bool enableAudit = false,
        string? auditDirectory = null,
        bool requireEnterpriseLicense = false,
        bool enterpriseClient = false,
        string? rollbackStateDirectory = null)
    {
        _rollbackStateDirectory = rollbackStateDirectory ?? vaultDirectory;
        _engine = new VaultEngine(vaultDirectory, DpapiScope.CurrentUser, policy, _rollbackStateDirectory);
        _policy = policy;
        _requireEnterpriseLicense = requireEnterpriseLicense;
        _enterpriseClient = enterpriseClient;
        if (enableAudit)
            _audit = auditDirectory is null
                ? AuditLogger.ForEnterprise()
                : new AuditLogger(auditDirectory);
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
        RuntimeIntegrity.EnsureSafeForSensitiveOperation();
        EnsureEnterpriseLicense();
        EnsureEnterpriseSeat();
        _audit?.Log(AuditEventType.UnlockAttempt, "Unlock attempted");
        try
        {
            DisposeSession();
            _context = _engine.Unlock(masterPassword, paranoiaMode, confirmRollback);
            RegisterEnterpriseSeat();
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
        RuntimeIntegrity.EnsureSafeForSensitiveOperation();
        EnsureEnterpriseLicense();
        EnsureEnterpriseSeat();
        _audit?.Log(AuditEventType.UnlockAttempt, "Hello unlock attempted");
        byte[]? mkCopy = null;
        try
        {
            mkCopy = masterKey.ToArray();
            DisposeSession();
            _context = _engine.UnlockWithMasterKey(mkCopy, paranoiaMode, confirmRollback);
            RegisterEnterpriseSeat();
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
        ScrubPayloadSecrets();
        ForceGarbageCollectionBestEffort();
        DisposeSession();
        _audit?.Log(AuditEventType.Lock, "Panic lock");
        StateChanged?.Invoke();
    }

    public VaultUnlockContext UnlockFromSnapshot(
        int snapshotIndex,
        string masterPassword,
        bool paranoiaMode = true,
        bool confirmRollback = false)
    {
        EnsureEnterpriseLicense();
        DisposeSession();
        _context = _engine.UnlockFromSnapshot(snapshotIndex, masterPassword, paranoiaMode, confirmRollback);
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
            if (_context is null)
                return new CredentialResponse { Error = "locked" };

            if (_fillNonce is null || !_fillNonce.TryConsume(req.FillNonce))
                return new CredentialResponse { Error = "invalid_nonce" };

            if (!TryNormalizeRequest(req, out var requestHost, out var hostError))
                return hostError;

            var matches = ListMatchesForHost(requestHost);
            if (matches.Count == 0)
                return new CredentialResponse { Error = "no_match" };

            VaultEntry? match = null;
            if (req.EntryId is { } entryId)
            {
                match = _context.Payload.Entries.FirstOrDefault(e => e.Id == entryId);
            }
            else if (matches.Count == 1)
            {
                match = matches[0].Entry;
            }
            else
            {
                return new CredentialResponse
                {
                    Found = false,
                    Error = "multiple_matches",
                    Matches = matches.Select(m => new CredentialMatchSummary
                    {
                        Id = m.Entry.Id,
                        Title = m.Entry.Title,
                        Username = m.Entry.Username
                    }).ToList()
                };
            }

            if (match is null || !EntryHostMatches(match.Url, requestHost))
                return new CredentialResponse { Error = "no_match" };

            _audit?.Log(AuditEventType.BrowserBridgeAccess,
                $"Browser bridge credential served for host {requestHost} (entry {match.Id})");

            return new CredentialResponse
            {
                Found = true,
                Title = match.Title,
                Username = match.Username,
                Password = match.Password,
                PasskeyCredentialId = match.PasskeyCredentialId
            };
        }
    }

    public CredentialResponse ListMatchesForDomain(CredentialRequest req)
    {
        lock (_payloadLock)
        {
            if (_context is null)
                return new CredentialResponse { Error = "locked" };

            if (!TryNormalizeRequest(req, out var requestHost, out var hostError))
                return hostError;

            var matches = ListMatchesForHost(requestHost)
                .Select(m => new CredentialMatchSummary
                {
                    Id = m.Entry.Id,
                    Title = m.Entry.Title,
                    Username = m.Entry.Username
                })
                .ToList();

            _audit?.Log(AuditEventType.BrowserBridgeAccess,
                $"Browser bridge listed {matches.Count} credential(s) for host {requestHost}");

            return new CredentialResponse
            {
                Found = matches.Count > 0,
                Matches = matches,
                FillNonce = _fillNonce?.Issue(),
                Error = matches.Count == 0 ? "no_match" : null
            };
        }
    }

    [Obsolete("Use ListMatchesForDomain returning CredentialResponse")]
    public IReadOnlyList<CredentialMatchSummary> ListMatchSummariesForDomain(CredentialRequest req)
        => ListMatchesForDomain(req).Matches ?? [];

    private List<(VaultEntry Entry, int Score)> ListMatchesForHost(string requestHost)
    {
        return _context!.Payload.Entries
            .Where(e => !e.IsSecureNote && !string.IsNullOrEmpty(e.Url))
            .Where(e => EntryHostMatches(e.Url, requestHost))
            .Select(e => (Entry: e, Score: MatchScore(e, requestHost)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int MatchScore(VaultEntry entry, string requestHost)
    {
        var host = ExtractHost(entry.Url);
        return string.Equals(host, requestHost, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }

    private static bool TryNormalizeRequest(
        CredentialRequest req,
        out string requestHost,
        out CredentialResponse errorResponse)
    {
        requestHost = "";
        errorResponse = new CredentialResponse { Error = "no_match" };

        var fromUrl = ExtractHost(req.Url);
        var fromDomain = string.IsNullOrWhiteSpace(req.Domain)
            ? ""
            : req.Domain.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(fromUrl) && !string.IsNullOrEmpty(fromDomain) &&
            !string.Equals(fromUrl, fromDomain, StringComparison.OrdinalIgnoreCase))
        {
            errorResponse = new CredentialResponse { Error = "host_mismatch" };
            return false;
        }

        requestHost = !string.IsNullOrEmpty(fromUrl) ? fromUrl : fromDomain;
        if (string.IsNullOrEmpty(requestHost))
            return false;

        if (DomainSafety.ContainsSuspiciousCharacters(requestHost))
        {
            errorResponse = new CredentialResponse { Error = "invalid_host" };
            return false;
        }

        requestHost = DomainSafety.NormalizeHost(requestHost);
        return true;
    }

    private static string ResolveRequestHost(CredentialRequest req)
    {
        if (TryNormalizeRequest(req, out var host, out _))
            return host;
        return "";
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

    public void SuppressAutoLock() => Interlocked.Increment(ref _autoLockSuppressCount);

    public void ResumeAutoLock()
    {
        Interlocked.Decrement(ref _autoLockSuppressCount);
        if (Volatile.Read(ref _autoLockSuppressCount) < 0)
            Interlocked.Exchange(ref _autoLockSuppressCount, 0);
    }

    public bool IsAutoLockSuppressed => Volatile.Read(ref _autoLockSuppressCount) > 0;

    public void SetAutoLockTimeout(int seconds)
    {
        _autoLockTimeoutSeconds = seconds > 0 ? seconds : 300;
        if (_autoLock is not null)
            _autoLock.TimeoutSeconds = _autoLockTimeoutSeconds;
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

    private void EnsureEnterpriseSeat()
    {
        if (!_requireEnterpriseLicense) return;
        var license = LicenseStore.Load();
        if (license is null)
            throw new InvalidOperationException("A valid enterprise license is required.");
        LicenseSeatRegistry.EnsureSeatAvailable(license);
    }

    private void RegisterEnterpriseSeat()
    {
        if (!_requireEnterpriseLicense) return;
        var license = LicenseStore.Load();
        if (license is null) return;
        LicenseSeatRegistry.RegisterCurrentSeat(license);
    }

    private void EnsureWritable()
    {
        if (_context is null) throw new InvalidOperationException("Vault not unlocked.");
        if (_context.ReadOnly) throw new InvalidOperationException("Vault is read-only.");
    }

    private void StartInfrastructure()
    {
        _bridge?.Dispose();
        _tokenBroker?.Dispose();
        BridgeSessionAuth.ConfigureTokenDirectory(FortivaPaths.GetBridgeSessionDirectory(_enterpriseClient));
        BridgeSessionAuth.ClearSessionToken();
        _bridgeSessionToken = BridgeSessionAuth.CreateSessionToken();
        _tokenBroker = new BridgeTokenBroker(_bridgeSessionToken);
        _tokenBroker.Start();
        _fillNonce = new BridgeFillNonce();
        BridgeClientValidator.ConfigureAllowedInstallRoots(AppContext.BaseDirectory);
        _bridge = new BrowserBridgeServer(ResolveForDomain, ListMatchesForDomain, _bridgeSessionToken);
        _bridge.Start();

        _autoLock?.Dispose();
        var timeout = PolicyEnforcer.EnforceAutoLock(_autoLockTimeoutSeconds, _policy ?? new FortivaPolicy());
        _autoLock = new AutoLockTimer(timeout);
        _autoLock.LockRequested += () =>
        {
            if (IsAutoLockSuppressed) return;
            AutoLockRequested?.Invoke();
        };
    }

    private void DisposeSession()
    {
        _bridge?.Dispose();
        _bridge = null;
        _tokenBroker?.Dispose();
        _tokenBroker = null;
        BridgeSessionAuth.ClearSessionToken();
        _bridgeSessionToken = null;
        _fillNonce?.Reset();
        _fillNonce = null;
        _autoLock?.Dispose();
        _autoLock = null;
        _context?.Keys.Lock();
        _context?.Keys.Dispose();
        _context = null;
    }

    private void ScrubPayloadSecrets()
    {
        lock (_payloadLock)
        {
            if (_context?.Payload.Entries is null)
                return;

            foreach (var entry in _context.Payload.Entries)
            {
                entry.Password = "";
                entry.TotpSecret = null;
            }
        }
    }

    private static void ForceGarbageCollectionBestEffort()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        catch
        {
            /* best effort */
        }
    }

    public void Dispose() => DisposeSession();
}
