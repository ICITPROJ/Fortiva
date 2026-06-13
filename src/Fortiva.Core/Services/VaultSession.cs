using Fortiva.Core.Audit;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.ImportExport;
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
    private readonly object _sessionGate = new();
    private int _autoLockSuppressCount;
    private int _autoLockTimeoutSeconds = 300;
    private readonly string _rollbackStateDirectory;
    private BridgeFillNonce? _fillNonce;
    private Task? _bridgeShutdownTask;

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
        _engine = new VaultEngine(vaultDirectory, scope, policy, _rollbackStateDirectory);
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

    /// <summary>
    /// True when unlocked and bridge pipes are listening. Uses the in-process session token
    /// (not a pipe GET — the token broker only accepts BrowserBridge.Host clients).
    /// </summary>
    public bool IsBridgeHealthy() =>
        _context is not null
        && !string.IsNullOrEmpty(_bridgeSessionToken)
        && _bridge is not null
        && _tokenBroker is not null
        && BridgeHealthCheck.AreListenersActive();
    public VaultUnlockContext? Context => _context;
    public string? RollbackWarning => _context?.RollbackWarning;
    public bool VaultExists => _engine.VaultExists;

    /// <summary>Thread-safe snapshot for browser bridge STATUS (same gate as unlock/lock).</summary>
    public BridgePresenceSnapshot GetBridgePresenceSnapshot()
    {
        lock (_sessionGate)
        {
            if (_context is null)
                return new BridgePresenceSnapshot(_engine.VaultExists, Unlocked: false, BridgeReady: false);

            var bridgeReady = !string.IsNullOrEmpty(_bridgeSessionToken)
                && _bridge is not null
                && _tokenBroker is not null
                && BridgeHealthCheck.AreListenersActive(300);

            return new BridgePresenceSnapshot(_engine.VaultExists, Unlocked: true, BridgeReady: bridgeReady);
        }
    }

    public void CreateVault(string masterPassword, SecurityLevel level)
        => _engine.CreateVault(masterPassword, level);

    public void Unlock(string masterPassword, bool paranoiaMode = false, bool confirmRollback = false)
    {
        lock (_sessionGate)
        {
            RuntimeIntegrity.EnsureSafeForSensitiveOperation();
            EnsureEnterpriseLicense();
            EnsureEnterpriseSeat();
            _audit?.Log(AuditEventType.UnlockAttempt, "Unlock attempted");
            try
            {
                DisposeSessionCore();
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
    }

    public void UnlockWithMasterKey(byte[] masterKey, bool paranoiaMode = false, bool confirmRollback = false)
    {
        lock (_sessionGate)
        {
            RuntimeIntegrity.EnsureSafeForSensitiveOperation();
            EnsureEnterpriseLicense();
            EnsureEnterpriseSeat();
            _audit?.Log(AuditEventType.UnlockAttempt, "Hello unlock attempted");
            byte[]? mkCopy = null;
            try
            {
                mkCopy = masterKey.ToArray();
                DisposeSessionCore();
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
    }

    public void Lock()
    {
        lock (_sessionGate)
        {
            ScrubPayloadSecrets();
            DisposeSessionCore();
            _audit?.Log(AuditEventType.Lock, "Vault locked");
            StateChanged?.Invoke();
        }
    }

    public void PanicLock()
    {
        lock (_sessionGate)
        {
            ScrubPayloadSecrets();
            ForceGarbageCollectionBestEffort();
            DisposeSessionCore();
            _audit?.Log(AuditEventType.Lock, "Panic lock");
            StateChanged?.Invoke();
        }
    }

    public VaultUnlockContext UnlockFromSnapshot(
        int snapshotIndex,
        string masterPassword,
        bool paranoiaMode = true,
        bool confirmRollback = false)
    {
        lock (_sessionGate)
        {
            RuntimeIntegrity.EnsureSafeForSensitiveOperation();
            EnsureEnterpriseLicense();
            EnsureEnterpriseSeat();
            DisposeSessionCore();
            _context = _engine.UnlockFromSnapshot(snapshotIndex, masterPassword, paranoiaMode, confirmRollback);
            RegisterEnterpriseSeat();
            _audit?.Log(AuditEventType.SnapshotRestore, $"Restored from snapshot {snapshotIndex}");
            StartInfrastructure();
            StateChanged?.Invoke();
            return _context;
        }
    }

    public byte[] CopyMasterKeyForHelloSetup()
    {
        if (_context is null) throw new InvalidOperationException("Vault not unlocked.");
        return _context.Keys.MasterKey.ToArray();
    }

    public void ChangeMasterPassword(string newPassword, Argon2Parameters? kdf = null)
    {
        EnsureWritable();
        lock (_payloadLock)
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
        EnsureEntryCompliesWithPolicy(entry);
        lock (_payloadLock)
            _engine.AddEntry(_context!, entry);
        StateChanged?.Invoke();
    }

    public void UpdateEntry(VaultEntry entry)
    {
        EnsureWritable();
        EnsureEntryCompliesWithPolicy(entry);
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

    public int BulkDeleteEntries(IEnumerable<Guid> entryIds)
    {
        EnsureWritable();
        int count;
        lock (_payloadLock)
            count = _engine.BulkDeleteEntries(_context!, entryIds);
        if (count > 0)
            StateChanged?.Invoke();
        return count;
    }

    public (int Updated, int SkippedAlreadyTagged, int SkippedMaxTags) BulkAddTag(
        IEnumerable<Guid> entryIds,
        string normalizedTag)
    {
        EnsureWritable();
        (int updated, int skippedAlready, int skippedMax) result;
        lock (_payloadLock)
            result = _engine.BulkAddTag(_context!, entryIds, normalizedTag);
        if (result.updated > 0)
            StateChanged?.Invoke();
        return result;
    }

    public void BulkImport(IEnumerable<VaultEntry> entries)
    {
        EnsureWritable();
        foreach (var entry in entries)
            EnsureEntryCompliesWithPolicy(entry);
        lock (_payloadLock)
            _engine.BulkImport(_context!, entries);
        _audit?.Log(AuditEventType.ConfigurationChange, "Bulk import completed");
        StateChanged?.Invoke();
    }

    public void ApplyImport(ImportExport.ImportApplyPlan plan)
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.ApplyImport(_context!, plan);
        _audit?.Log(AuditEventType.ConfigurationChange,
            $"Import “{plan.Batch.ProvenanceLabel}”: +{plan.Batch.AddedCount} added, "
            + $"{plan.Batch.SkippedDuplicateCount} duplicates skipped");
        StateChanged?.Invoke();
    }

    public IReadOnlyList<ImportBatch> ImportHistory()
    {
        lock (_payloadLock)
        {
            if (_context is null) return [];
            return _context.Payload.ImportBatches
                .OrderByDescending(b => b.ImportedAt)
                .ToList();
        }
    }

    public IReadOnlyList<VaultEntry> EntriesForImportBatch(Guid batchId)
    {
        lock (_payloadLock)
        {
            if (_context is null) return [];
            return _context.Payload.Entries
                .Where(e => e.ImportBatchId == batchId)
                .OrderByDescending(e => e.ImportedAt ?? e.CreatedAt)
                .ToList();
        }
    }

    /// <summary>Recreates browser bridge pipes (e.g. after a stuck token broker). Vault must be unlocked.</summary>
    public void RestartBridgeInfrastructure()
    {
        lock (_sessionGate)
        {
            if (_context is null)
                throw new InvalidOperationException("Unlock the vault before reconnecting the browser.");

            StartInfrastructure();
            StateChanged?.Invoke();
        }
    }

    /// <summary>Restarts bridge pipes when the token broker or credential server is not listening.</summary>
    public void EnsureBridgeInfrastructureHealthy()
    {
        lock (_sessionGate)
        {
            if (_context is null)
                return;

            if (_bridgeShutdownTask is { IsCompleted: false })
            {
                WaitForBridgeShutdown();
                if (_bridgeShutdownTask is { IsCompleted: false })
                {
                    _audit?.Log(
                        AuditEventType.BrowserBridgeAccess,
                        "Bridge shutdown did not finish; forcing pipe restart",
                        success: false);
                    _bridgeShutdownTask = null;
                }
            }

            if (BridgeHealthCheck.AreListenersActive())
                return;

            StartInfrastructure();
            StateChanged?.Invoke();
        }
    }

    public void Save()
    {
        EnsureWritable();
        lock (_payloadLock)
            _engine.Save(_context!);
    }

    /// <summary>
    /// Two-way merge of this (active) vault with another unlocked vault. Both vaults are written
    /// with the converged entry set; the active session's in-memory entries are updated in place.
    /// </summary>
    public VaultSyncResult SyncWith(VaultEngine otherEngine, VaultUnlockContext otherContext)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(otherEngine);
        ArgumentNullException.ThrowIfNull(otherContext);

        VaultSyncResult result;
        lock (_payloadLock)
            result = VaultSynchronizer.SyncTwoWay(_engine, _context!, otherEngine, otherContext);

        _audit?.Log(AuditEventType.ConfigurationChange,
            $"Vault sync completed (+{result.Local.Added} ~{result.Local.Updated} -{result.Local.Removed}, total {result.MergedTotal})");
        StateChanged?.Invoke();
        return result;
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
        List<VaultEntry> snapshot;
        lock (_payloadLock)
        {
            if (_context is null)
                return [];
            snapshot = _context.Payload.Entries.ToList();
        }

        return snapshot.Where(e =>
            string.IsNullOrEmpty(query) ||
            e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<string> ListSnapshots()
        => _engine.Snapshots.ListSnapshots();

    public CredentialResponse ResolveForDomain(CredentialRequest req)
    {
        lock (_payloadLock)
        {
            if (_context is null)
                return new CredentialResponse { Error = "locked" };

            // Normalize the host first so the fill nonce can be validated against the host it was
            // issued for. This prevents a malicious bridge client from listing on one domain and
            // replaying the nonce to fetch credentials for a different domain.
            if (!TryNormalizeRequest(req, out var requestHost, out var hostError))
                return hostError;

            if (_fillNonce is null || !_fillNonce.TryConsume(req.FillNonce, requestHost))
                return new CredentialResponse { Error = "invalid_nonce" };

            var matches = ListMatchesForHost(requestHost);
            if (matches.Count == 0)
                return new CredentialResponse { Error = "no_match" };

            VaultEntry? match = null;
            if (req.EntryId is { } entryId)
            {
                match = matches.Select(m => m.Entry).FirstOrDefault(e => e.Id == entryId);
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
                    Matches = matches.Select(m => ToMatchSummary(m.Entry, requestHost)).ToList()
                };
            }

            if (match is null)
                return new CredentialResponse { Error = "no_match" };

            if (match.IsSecureNote)
                return new CredentialResponse { Error = "no_match" };

            var matchUrl = GetEffectiveEntryUrl(match) ?? match.Url;
            if (string.IsNullOrEmpty(matchUrl) || !EntryHostMatchesForCredentialRelease(matchUrl, requestHost))
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
                .Select(m => ToMatchSummary(m.Entry, requestHost))
                .ToList();

            _audit?.Log(AuditEventType.BrowserBridgeAccess,
                $"Browser bridge listed {matches.Count} credential(s) for host {requestHost}");

            return new CredentialResponse
            {
                Found = matches.Count > 0,
                Matches = matches,
                FillNonce = _fillNonce?.Issue(requestHost),
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
            .Where(e => !e.IsSecureNote)
            .Select(e => (Entry: e, Url: GetEffectiveEntryUrl(e)))
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .Where(x => EntryHostMatches(x.Url!, requestHost))
            .Select(x => x.Entry)
            .Select(e => (Entry: e, Score: MatchScore(e, requestHost)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int MatchScore(VaultEntry entry, string requestHost)
    {
        var host = ExtractHost(GetEffectiveEntryUrl(entry) ?? entry.Url);
        if (string.IsNullOrEmpty(host))
            return 0;

        host = DomainSafety.NormalizeHost(host);
        if (string.Equals(host, requestHost, StringComparison.OrdinalIgnoreCase))
            return 3;

        return DomainSafety.ShareRegistrableDomain(host, requestHost) ? 1 : 0;
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

        var normalizedUrl = string.IsNullOrEmpty(fromUrl) ? "" : DomainSafety.NormalizeHost(fromUrl);
        var normalizedDomain = string.IsNullOrEmpty(fromDomain) ? "" : DomainSafety.NormalizeHost(fromDomain);

        if (!string.IsNullOrEmpty(normalizedUrl) && !string.IsNullOrEmpty(normalizedDomain) &&
            !string.Equals(normalizedUrl, normalizedDomain, StringComparison.OrdinalIgnoreCase))
        {
            errorResponse = new CredentialResponse { Error = "host_mismatch" };
            return false;
        }

        requestHost = !string.IsNullOrEmpty(normalizedUrl) ? normalizedUrl : normalizedDomain;
        if (string.IsNullOrEmpty(requestHost))
            return false;

        if (DomainSafety.ContainsSuspiciousCharacters(requestHost))
        {
            errorResponse = new CredentialResponse { Error = "invalid_host" };
            return false;
        }

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
        if (entryHost is null)
            return false;

        return DomainSafety.HostsMatchForAutofill(entryHost, requestHost);
    }

    private static bool EntryHostMatchesForCredentialRelease(string entryUrl, string requestHost)
    {
        var entryHost = ExtractHost(entryUrl);
        if (entryHost is null)
            return false;

        return DomainSafety.HostsMatchForCredentialRelease(entryHost, requestHost);
    }

    private static CredentialMatchSummary ToMatchSummary(VaultEntry entry, string requestHost)
    {
        var entryUrl = GetEffectiveEntryUrl(entry) ?? entry.Url;
        var releasable = !string.IsNullOrEmpty(entryUrl)
            && EntryHostMatchesForCredentialRelease(entryUrl, requestHost);

        return new CredentialMatchSummary
        {
            Id = entry.Id,
            Title = entry.Title,
            Username = entry.Username,
            Releasable = releasable
        };
    }

    private static string? GetEffectiveEntryUrl(VaultEntry entry)
        => VaultEntryWebsite.GetEffectiveUrl(entry);

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
        {
            _autoLock.TimeoutSeconds = _autoLockTimeoutSeconds;
            _autoLock.ResetActivity();
        }
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
        PolicyEnforcer.EnsureWritableSecurityLevel(_context.Header.SecurityLevel, _policy);
    }

    private void EnsureEntryCompliesWithPolicy(VaultEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.TotpSecret)
            && !PolicyEnforcer.CanUseTotp(_enterpriseClient, _policy))
        {
            throw new InvalidOperationException("Authenticator codes are not permitted by policy.");
        }
    }

    private void StartInfrastructure()
    {
        BridgeHostProcessCleanup.StopOrphanedHosts();
        WaitForBridgeShutdown();
        StopBridgeInfrastructure(waitForListeners: true);

        BridgeSessionAuth.ConfigureTokenDirectory(FortivaPaths.GetBridgeSessionDirectory(_enterpriseClient));
        BridgeSessionAuth.ClearSessionToken();
        _bridgeSessionToken = BridgeSessionAuth.CreateSessionToken();
        BridgeClientValidator.ConfigureAllowedInstallRoots(AppContext.BaseDirectory);
        _fillNonce = new BridgeFillNonce();

        try
        {
            BrowserBridgeInstallService.RepairNativeHostIfStale(AppContext.BaseDirectory, _enterpriseClient);
        }
        catch
        {
            /* best effort — re-pin HKCU native messaging after unlock */
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            _tokenBroker = new BridgeTokenBroker(_bridgeSessionToken);
            _tokenBroker.Start();
            _bridge = new BrowserBridgeServer(ResolveForDomain, ListMatchesForDomain, _bridgeSessionToken);
            _bridge.Start();

            if (BridgeHealthCheck.AreListenersActive(BridgeHealthCheck.StartupHealthTimeoutMs))
                break;

            StopBridgeInfrastructure(waitForListeners: true);
            if (attempt < 2)
                Thread.Sleep(100 * (attempt + 1));
        }

        if (!BridgeHealthCheck.AreListenersActive())
        {
            _audit?.Log(
                AuditEventType.BrowserBridgeAccess,
                "Bridge pipes failed to start after unlock",
                success: false);
        }

        _autoLock?.Dispose();
        var timeout = PolicyEnforcer.EnforceAutoLock(_autoLockTimeoutSeconds, _policy ?? new FortivaPolicy());
        _autoLock = new AutoLockTimer(timeout);
        _autoLock.LockRequested += () =>
        {
            if (IsAutoLockSuppressed) return;
            AutoLockRequested?.Invoke();
        };
    }

    private void StopBridgeInfrastructure(bool waitForListeners)
    {
        var bridge = _bridge;
        var broker = _tokenBroker;
        _bridge = null;
        _tokenBroker = null;

        if (bridge is not null || broker is not null)
        {
            var prior = _bridgeShutdownTask;
            _bridgeShutdownTask = Task.Run(async () =>
            {
                if (prior is not null)
                {
                    try { await prior.ConfigureAwait(false); } catch { /* best effort */ }
                }

                bridge?.DisposeBlocking();
                broker?.DisposeBlocking();
            });
        }

        if (waitForListeners)
            WaitForBridgeShutdown();
    }

    private void WaitForBridgeShutdown()
    {
        var shutdown = _bridgeShutdownTask;
        if (shutdown is null)
            return;

        try { shutdown.Wait(TimeSpan.FromSeconds(8)); }
        catch { /* best effort */ }

        if (shutdown.IsCompleted)
            _bridgeShutdownTask = null;
    }

    private void DisposeSessionCore()
    {
        StopBridgeInfrastructure(waitForListeners: false);
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
                entry.Username = "";
                entry.Password = "";
                entry.Notes = "";
                entry.TotpSecret = null;
                entry.PasskeyCredentialId = null;
                entry.Tags ??= [];
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

    public void Dispose()
    {
        lock (_sessionGate)
        {
            ScrubPayloadSecrets();
            StopBridgeInfrastructure(waitForListeners: true);
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
    }
}
