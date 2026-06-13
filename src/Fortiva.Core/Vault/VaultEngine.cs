using System.Security.Cryptography;
using Fortiva.Core.Crypto;
using Fortiva.Core.ImportExport;
using Fortiva.Core.LocalState;
using Fortiva.Core.Policy;

namespace Fortiva.Core.Vault;

public sealed class VaultEngine
{
    private readonly VaultSnapshotManager _snapshots;
    private readonly DpapiLocalStateStore _localState;
    private readonly FortivaPolicy? _policy;

    public VaultEngine(
        string vaultDirectory,
        DpapiScope dpapiScope,
        FortivaPolicy? policy = null,
        string? localStateDirectory = null)
    {
        Directory.CreateDirectory(vaultDirectory);
        _snapshots = new VaultSnapshotManager(vaultDirectory);
        var stateDir = localStateDirectory ?? vaultDirectory;
        Directory.CreateDirectory(stateDir);
        _localState = new DpapiLocalStateStore(stateDir, dpapiScope);
        _policy = policy;
        TryRecoverInterruptedWrite();
    }

    public string VaultPath => _snapshots.VaultPath;
    public bool VaultExists => File.Exists(_snapshots.VaultPath);
    public VaultSnapshotManager Snapshots => _snapshots;

    /// <summary>
    /// Recovers from a crash/power-loss that interrupted the non-atomic replace fallback used on
    /// FAT32/exFAT (removable/portable drives). That fallback moves the live vault aside to
    /// <c>vault.fva.bak</c> before moving the new file into place, so a crash in between can leave
    /// no <c>vault.fva</c>. Restores the last good copy from the backup (or, failing that, the most
    /// recent snapshot) so the app does not appear to have "lost" the vault. No-op on a healthy
    /// vault or an empty directory. Returns true when a recovery was performed.
    /// </summary>
    public bool TryRecoverInterruptedWrite()
    {
        var vaultPath = _snapshots.VaultPath;
        var dir = Path.GetDirectoryName(vaultPath)!;
        var backup = vaultPath + VaultConstants.BackupSuffix;
        var temp = Path.Combine(dir, VaultConstants.VaultFileName + VaultConstants.TempSuffix);

        try
        {
            if (File.Exists(vaultPath))
            {
                // Healthy vault present — clean up a stale backup left by a completed fallback write.
                if (File.Exists(backup))
                    TryDelete(backup);
                return false;
            }

            // vault.fva is missing. Prefer the backup (the last successfully saved state that the
            // fallback preserved); otherwise fall back to the newest snapshot.
            if (File.Exists(backup) && new FileInfo(backup).Length > 0)
            {
                File.Move(backup, vaultPath);
                TryDelete(temp);
                return true;
            }

            var snapshot = _snapshots.FindLatestSnapshot();
            if (snapshot is not null && new FileInfo(snapshot).Length > 0)
            {
                File.Copy(snapshot, vaultPath, overwrite: false);
                TryDelete(temp);
                return true;
            }
        }
        catch
        {
            // Best-effort recovery; if it fails the caller still sees "no vault" and can recover
            // manually from the backup/snapshot files which are left in place.
        }

        return false;
    }

    // ── Creation ─────────────────────────────────────────────────────────────

    public void CreateVault(string masterPassword, SecurityLevel securityLevel, Argon2Parameters? kdfOverride = null)
    {
        if (VaultExists)
        {
            throw new InvalidOperationException(
                $"A vault already exists at {VaultPath}. " +
                "Remove the existing vault or unlock it instead of creating a new one.");
        }

        securityLevel = PolicyEnforcer.EnforceMinimumSecurityLevel(securityLevel, _policy);
        PolicyEnforcer.EnsureWritableSecurityLevel(securityLevel, _policy);
        var kdf = ResolveKdf(kdfOverride ?? Argon2Parameters.PersonalDefault, securityLevel);

        var (keys, wrappedVk, salt) = KeyHierarchy.CreateNew(masterPassword, kdf);

        var header = new VaultHeader
        {
            VaultId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
            SecurityLevel = securityLevel,
            KdfParameters = kdf,
            Salt = salt,
            WrappedVaultKey = wrappedVk,
            RevisionCounter = 1,
            SecurityLevelCounter = (ulong)securityLevel
        };

        var payload = new VaultPayload();
        SaveInternal(keys, header, payload, updateLocalState: true);
        keys.Dispose();
    }

    // ── Unlock ───────────────────────────────────────────────────────────────

    public VaultUnlockContext Unlock(string masterPassword, bool paranoiaMode = false, bool confirmRollback = false)
    {
        if (!VaultExists)
            throw new FileNotFoundException("Vault not found.", _snapshots.VaultPath);

        var fileBytes = File.ReadAllBytes(_snapshots.VaultPath);
        return UnlockFromBytes(fileBytes, masterPassword, paranoiaMode, confirmRollback);
    }

    public VaultUnlockContext UnlockFromSnapshot(
        int snapshotIndex,
        string masterPassword,
        bool paranoiaMode = true,
        bool confirmRollback = false)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(_snapshots.VaultPath)!,
            VaultConstants.SnapshotFileName(snapshotIndex));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Snapshot {snapshotIndex} not found.", path);
        return UnlockFromBytes(File.ReadAllBytes(path), masterPassword, paranoiaMode, confirmRollback);
    }

    private VaultUnlockContext UnlockFromBytes(byte[] fileBytes, string masterPassword, bool paranoiaMode, bool confirmRollback)
    {
        if (fileBytes.Length > VaultConstants.MaxVaultFileBytes)
            throw new InvalidDataException("Vault file exceeds maximum allowed size.");

        var (header, encEntries, encIntegrity) = VaultSerializer.ParseVaultFile(fileBytes);
        header.KdfParameters = ResolveKdf(header.KdfParameters, header.SecurityLevel);
        var keys = KeyHierarchy.Unlock(masterPassword, header.Salt, header.WrappedVaultKey, header.KdfParameters);
        return FinishUnlock(header, encEntries, encIntegrity, keys, paranoiaMode, confirmRollback);
    }

    public VaultUnlockContext UnlockWithMasterKey(byte[] masterKey, bool paranoiaMode = false, bool confirmRollback = false)
    {
        if (!VaultExists)
            throw new FileNotFoundException("Vault not found.", _snapshots.VaultPath);

        var fileBytes = File.ReadAllBytes(_snapshots.VaultPath);
        if (fileBytes.Length > VaultConstants.MaxVaultFileBytes)
            throw new InvalidDataException("Vault file exceeds maximum allowed size.");

        var (header, encEntries, encIntegrity) = VaultSerializer.ParseVaultFile(fileBytes);
        header.KdfParameters = ResolveKdf(header.KdfParameters, header.SecurityLevel);
        var keys = KeyHierarchy.UnlockWithMasterKey(masterKey, header.WrappedVaultKey);
        return FinishUnlock(header, encEntries, encIntegrity, keys, paranoiaMode, confirmRollback);
    }

    private VaultUnlockContext FinishUnlock(
        VaultHeader header,
        byte[] encEntries,
        byte[] encIntegrity,
        KeyHierarchy keys,
        bool paranoiaMode,
        bool confirmRollback)
    {
        try
        {
            VaultSerializer.VerifyHeaderMac(keys.VaultKey, header);
            var payload = VaultSerializer.DecryptPayload(keys, encEntries, encIntegrity);
            IntegrityValidator.ValidateConsistency(payload);

            var rollback = _localState.CheckRollback(header, paranoiaMode);
            var readOnly = false;
            string? warning = null;

            if (payload.Entries.Count > 0 && payload.IntegrityLog.Count == 0 && header.RevisionCounter > 1)
            {
                readOnly = true;
                var integrityNote = "Integrity log is missing for an established vault.";
                warning = string.IsNullOrEmpty(warning) ? integrityNote : $"{warning} {integrityNote}";
            }

            if (rollback.IsSuspicious)
            {
                warning = string.Join(" ", rollback.Warnings);
                if (rollback.ForceReadOnly || !confirmRollback)
                    readOnly = true;
            }

            if (!readOnly && _policy?.MandatoryParanoiaMode == true && header.SecurityLevel < SecurityLevel.Paranoia)
            {
                readOnly = true;
                var policyNote = "Vault security level is below organization policy (Paranoia Mode required). Editing is disabled.";
                warning = string.IsNullOrEmpty(warning) ? policyNote : $"{warning} {policyNote}";
            }

            if (!readOnly)
                _localState.UpdateFromHeader(header);

            return new VaultUnlockContext
            {
                Header = header,
                Payload = payload,
                Keys = keys,
                ReadOnly = readOnly,
                RollbackWarning = warning
            };
        }
        catch
        {
            keys.Dispose();
            throw;
        }
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    public void Save(VaultUnlockContext ctx)
    {
        if (ctx.ReadOnly)
            throw new InvalidOperationException("Vault is read-only.");
        PolicyEnforcer.EnsureWritableSecurityLevel(ctx.Header.SecurityLevel, _policy);
        IntegrityValidator.ValidateConsistency(ctx.Payload);
        using (VaultSaveMutex.Acquire(_snapshots.VaultPath))
        {
            EnsureNoConcurrentModification(ctx);
            ctx.Header.LastModifiedAt = DateTimeOffset.UtcNow;
            ctx.Header.RevisionCounter++;
            SaveInternal(ctx.Keys, ctx.Header, ctx.Payload, updateLocalState: true);
        }
    }

    /// <summary>
    /// Optimistic concurrency guard: after any successful save the in-memory revision counter equals
    /// the on-disk one. If another process saved in the meantime, the on-disk counter will be higher,
    /// so we refuse to overwrite and let the caller reload instead of silently clobbering.
    /// </summary>
    private void EnsureNoConcurrentModification(VaultUnlockContext ctx)
    {
        var diskRevision = TryReadOnDiskRevision();
        if (diskRevision is { } rev && rev != ctx.Header.RevisionCounter)
            throw new VaultConcurrencyException();
    }

    private ulong? TryReadOnDiskRevision()
    {
        var path = _snapshots.VaultPath;
        if (!File.Exists(path))
            return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > VaultConstants.MaxVaultFileBytes)
                return null;
            var (header, _, _) = VaultSerializer.ParseVaultFile(bytes);
            return header.RevisionCounter;
        }
        catch (Exception ex)
        {
            throw new VaultConcurrencyException(
                "Unable to verify the on-disk vault revision before saving. "
                + "Lock and unlock the vault, then try again.", ex);
        }
    }

    public void AddEntry(VaultUnlockContext ctx, VaultEntry entry)
    {
        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        entry.CreatedAt = DateTimeOffset.UtcNow;
        entry.ModifiedAt = entry.CreatedAt;
        ctx.Payload.Entries.Add(entry);
        ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("add", entry.Id, ctx.Header.RevisionCounter + 1, entry));
        try { Save(ctx); }
        catch
        {
            ctx.Payload.Entries.Remove(entry);
            ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            throw;
        }
    }

    public void UpdateEntry(VaultUnlockContext ctx, VaultEntry entry)
    {
        var idx = ctx.Payload.Entries.FindIndex(e => e.Id == entry.Id);
        if (idx < 0) throw new KeyNotFoundException($"Entry {entry.Id} not found.");
        var previous = ctx.Payload.Entries[idx];
        entry.ModifiedAt = DateTimeOffset.UtcNow;
        ctx.Payload.Entries[idx] = entry;
        ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("update", entry.Id, ctx.Header.RevisionCounter + 1, entry));
        try { Save(ctx); }
        catch
        {
            ctx.Payload.Entries[idx] = previous;
            ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            throw;
        }
    }

    public void DeleteEntry(VaultUnlockContext ctx, Guid entryId)
        => BulkDeleteEntries(ctx, [entryId]);

    /// <summary>Removes multiple entries then saves once. Returns how many were removed.</summary>
    public int BulkDeleteEntries(VaultUnlockContext ctx, IEnumerable<Guid> entryIds)
    {
        var removals = new List<(int Index, VaultEntry Entry)>();
        foreach (var id in entryIds.Distinct())
        {
            var idx = ctx.Payload.Entries.FindIndex(e => e.Id == id);
            if (idx >= 0)
                removals.Add((idx, ctx.Payload.Entries[idx]));
        }

        if (removals.Count == 0)
            return 0;

        var startLogCount = ctx.Payload.IntegrityLog.Count;
        var revision = ctx.Header.RevisionCounter + 1;
        foreach (var (idx, _) in removals.OrderByDescending(r => r.Index))
            ctx.Payload.Entries.RemoveAt(idx);
        foreach (var (_, entry) in removals)
            ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("delete", entry.Id, revision));

        try
        {
            Save(ctx);
            return removals.Count;
        }
        catch
        {
            foreach (var (idx, entry) in removals.OrderBy(r => r.Index))
                ctx.Payload.Entries.Insert(idx, entry);
            while (ctx.Payload.IntegrityLog.Count > startLogCount)
                ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            throw;
        }
    }

    /// <summary>Applies a tag to multiple entries then saves once.</summary>
    public (int Updated, int SkippedAlreadyTagged, int SkippedMaxTags) BulkAddTag(
        VaultUnlockContext ctx,
        IEnumerable<Guid> entryIds,
        string normalizedTag)
    {
        var updates = new List<(int Index, VaultEntry Previous, VaultEntry Updated)>();
        var skippedAlready = 0;
        var skippedMax = 0;

        foreach (var id in entryIds.Distinct())
        {
            var idx = ctx.Payload.Entries.FindIndex(e => e.Id == id);
            if (idx < 0)
                continue;

            var previous = ctx.Payload.Entries[idx];
            var tags = previous.Tags?.ToList() ?? [];
            if (tags.Any(t => string.Equals(t, normalizedTag, StringComparison.OrdinalIgnoreCase)))
            {
                skippedAlready++;
                continue;
            }

            if (tags.Count >= VaultConstants.MaxTagsPerEntry)
            {
                skippedMax++;
                continue;
            }

            var updated = previous.Clone();
            tags.Add(normalizedTag);
            updated.Tags = tags;
            updated.ModifiedAt = DateTimeOffset.UtcNow;
            updates.Add((idx, previous, updated));
        }

        if (updates.Count == 0)
            return (0, skippedAlready, skippedMax);

        var startLogCount = ctx.Payload.IntegrityLog.Count;
        var revision = ctx.Header.RevisionCounter + 1;
        foreach (var (idx, _, updated) in updates)
        {
            ctx.Payload.Entries[idx] = updated;
            ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("update", updated.Id, revision, updated));
        }

        try
        {
            Save(ctx);
            return (updates.Count, skippedAlready, skippedMax);
        }
        catch
        {
            foreach (var (idx, previous, _) in updates)
                ctx.Payload.Entries[idx] = previous;
            while (ctx.Payload.IntegrityLog.Count > startLogCount)
                ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            throw;
        }
    }

    public void BulkImport(VaultUnlockContext ctx, IEnumerable<VaultEntry> entries)
    {
        var list = entries.ToList();
        var startCount = ctx.Payload.Entries.Count;
        var startLogCount = ctx.Payload.IntegrityLog.Count;
        try
        {
            foreach (var entry in list)
            {
                entry.Id = Guid.NewGuid();
                entry.CreatedAt = DateTimeOffset.UtcNow;
                entry.ModifiedAt = entry.CreatedAt;
                ctx.Payload.Entries.Add(entry);
                ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("import", entry.Id, ctx.Header.RevisionCounter + 1, entry));
            }
            Save(ctx);
        }
        catch
        {
            while (ctx.Payload.Entries.Count > startCount)
                ctx.Payload.Entries.RemoveAt(ctx.Payload.Entries.Count - 1);
            while (ctx.Payload.IntegrityLog.Count > startLogCount)
                ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            throw;
        }
    }

    /// <summary>Applies a reviewed import plan (adds, selective updates, batch history). Never overwrites without explicit plan items.</summary>
    public void ApplyImport(VaultUnlockContext ctx, ImportApplyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var startEntryCount = ctx.Payload.Entries.Count;
        var startLogCount = ctx.Payload.IntegrityLog.Count;
        var startBatchCount = ctx.Payload.ImportBatches.Count;
        var updateSnapshots = new List<(int Index, VaultEntry Previous)>();

        try
        {
            foreach (var entry in plan.ToAdd)
            {
                if (entry.Id == Guid.Empty)
                    entry.Id = Guid.NewGuid();
                ctx.Payload.Entries.Add(entry);
                ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("import", entry.Id, ctx.Header.RevisionCounter + 1, entry));
            }

            foreach (var entry in plan.ToUpdate)
            {
                var idx = ctx.Payload.Entries.FindIndex(e => e.Id == entry.Id);
                if (idx < 0)
                    throw new KeyNotFoundException($"Import update target {entry.Id} not found.");
                updateSnapshots.Add((idx, ctx.Payload.Entries[idx].Clone()));
                ctx.Payload.Entries[idx] = entry;
                ctx.Payload.IntegrityLog.Add(IntegrityValidator.CreateLogEntry("update", entry.Id, ctx.Header.RevisionCounter + 1, entry));
            }

            ctx.Payload.ImportBatches.Add(plan.Batch);
            Save(ctx);
        }
        catch
        {
            while (ctx.Payload.Entries.Count > startEntryCount)
                ctx.Payload.Entries.RemoveAt(ctx.Payload.Entries.Count - 1);
            foreach (var (index, previous) in updateSnapshots)
            {
                if (index < ctx.Payload.Entries.Count)
                    ctx.Payload.Entries[index] = previous;
            }
            while (ctx.Payload.IntegrityLog.Count > startLogCount)
                ctx.Payload.IntegrityLog.RemoveAt(ctx.Payload.IntegrityLog.Count - 1);
            while (ctx.Payload.ImportBatches.Count > startBatchCount)
                ctx.Payload.ImportBatches.RemoveAt(ctx.Payload.ImportBatches.Count - 1);
            throw;
        }
    }

    public void ChangeMasterPassword(VaultUnlockContext ctx, string newPassword, Argon2Parameters? kdfOverride = null)
    {
        if (ctx.ReadOnly)
            throw new InvalidOperationException("Vault is read-only.");
        PolicyEnforcer.EnsureWritableSecurityLevel(ctx.Header.SecurityLevel, _policy);
        using (VaultSaveMutex.Acquire(_snapshots.VaultPath))
        {
            EnsureNoConcurrentModification(ctx);
            var kdf = ResolveKdf(kdfOverride ?? ctx.Header.KdfParameters, ctx.Header.SecurityLevel);
            var (newKeys, wrappedVk, salt) = KeyHierarchy.CreateNew(newPassword, kdf);
            ctx.Header.KdfParameters = kdf;
            ctx.Header.Salt = salt;
            ctx.Header.WrappedVaultKey = wrappedVk;
            ctx.Header.RevisionCounter++;
            SaveInternal(newKeys, ctx.Header, ctx.Payload, updateLocalState: true);
            ctx.Keys.Lock();
            ctx.Keys.Dispose();
            ctx.Keys = newKeys;
        }
    }

    /// <summary>Verifies a candidate master password against the currently unlocked vault header.</summary>
    public bool VerifyMasterPassword(VaultUnlockContext ctx, string candidatePassword)
    {
        try
        {
            using var trial = KeyHierarchy.Unlock(
                candidatePassword,
                ctx.Header.Salt,
                ctx.Header.WrappedVaultKey,
                ctx.Header.KdfParameters);
            return CryptographicOperations.FixedTimeEquals(trial.VaultKey, ctx.Keys.VaultKey);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void RestoreFromSnapshot(int snapshotIndex, string masterPassword)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(_snapshots.VaultPath)!,
            VaultConstants.SnapshotFileName(snapshotIndex));
        if (!File.Exists(path))
            throw new FileNotFoundException("Snapshot not found.", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > VaultConstants.MaxVaultFileBytes)
            throw new InvalidDataException("Snapshot exceeds maximum allowed size.");

        // Verify password against snapshot before overwriting live vault.
        VaultUnlockContext? verified = null;
        try
        {
            verified = UnlockFromBytes(bytes, masterPassword, false, true);
            using (VaultSaveMutex.Acquire(_snapshots.VaultPath))
            {
                WriteVaultAtomically(bytes, suppressSnapshot: true);
            }
        }
        finally
        {
            verified?.Keys.Dispose();
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void SaveInternal(KeyHierarchy keys, VaultHeader header, VaultPayload payload, bool updateLocalState)
    {
        header.HeaderMac = VaultSerializer.ComputeHeaderMac(keys.VaultKey, header);
        var (encEntries, encIntegrity) = VaultSerializer.EncryptPayload(keys, payload);
        var fileBytes = VaultSerializer.SerializeVaultFile(header, encEntries, encIntegrity);
        WriteVaultAtomically(fileBytes);
        if (updateLocalState)
            _localState.UpdateFromHeader(header);
    }

    private void WriteVaultAtomically(byte[] fileBytes, bool suppressSnapshot = false)
    {
        var dir = Path.GetDirectoryName(_snapshots.VaultPath)!;
        var temp = Path.Combine(dir, VaultConstants.VaultFileName + VaultConstants.TempSuffix);
        File.WriteAllBytes(temp, fileBytes);
        using (var fs = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            fs.Flush(flushToDisk: true);
        if (File.Exists(_snapshots.VaultPath))
            ReplaceFileWithFallback(temp, _snapshots.VaultPath);
        else
            File.Move(temp, _snapshots.VaultPath);
        TryRestrictPermissionsOnFixedDrive(_snapshots.VaultPath);
        if (!suppressSnapshot)
            _snapshots.RotateSnapshotAfterSave();
    }

    /// <summary>
    /// Tightens the vault file ACL to the current user on fixed (non-removable, non-network) drives.
    /// Removable/network media are intentionally skipped: a portable USB vault is meant to be opened
    /// on other machines/accounts, and a machine-specific ACL would lock the owner out. Best-effort —
    /// the vault contents are encrypted regardless.
    /// </summary>
    private static void TryRestrictPermissionsOnFixedDrive(string vaultPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;
            var root = Path.GetPathRoot(Path.GetFullPath(vaultPath));
            if (string.IsNullOrEmpty(root))
                return;
            if (new DriveInfo(root).DriveType != DriveType.Fixed)
                return;
            Hello.HelloFileSecurity.ApplyCurrentUserOnlyAcl(vaultPath);
        }
        catch
        {
            /* best effort */
        }
    }

    /// <summary>
    /// Atomically replaces <paramref name="destination"/> with <paramref name="temp"/>.
    /// Uses <see cref="File.Replace"/> on NTFS; falls back to a backup-and-move on file systems
    /// that don't support ReplaceFile (FAT32/exFAT on removable/portable drives), which would
    /// otherwise throw and break saving a portable vault on a USB stick.
    /// </summary>
    private static void ReplaceFileWithFallback(string temp, string destination)
    {
        try
        {
            File.Replace(temp, destination, destinationBackupFileName: null);
            return;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // ReplaceFile is unsupported on this volume — fall back below.
        }

        var backup = destination + VaultConstants.BackupSuffix;
        try
        {
            if (File.Exists(backup))
                File.Delete(backup);
            // Preserve the current vault as a backup until the new file is in place.
            File.Move(destination, backup);
            try
            {
                File.Move(temp, destination);
            }
            catch
            {
                // Restore the original vault if swapping in the new file failed.
                if (!File.Exists(destination) && File.Exists(backup))
                    File.Move(backup, destination);
                throw;
            }
            File.Delete(backup);
        }
        catch
        {
            if (File.Exists(temp))
                TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private Argon2Parameters ResolveKdf(Argon2Parameters requested, SecurityLevel level)
    {
        var effective = level == SecurityLevel.Paranoia ? Argon2Parameters.Paranoia : requested;
        if (_policy is null) return effective;
        return PolicyEnforcer.EnforceKdfMinimum(effective, _policy);
    }
}
