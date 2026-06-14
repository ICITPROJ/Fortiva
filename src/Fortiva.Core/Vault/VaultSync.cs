namespace Fortiva.Core.Vault;

/// <summary>Per-vault summary of what a sync changed on one side.</summary>
public sealed record VaultSyncSideResult(int Added, int Updated, int Removed, int Total);

/// <summary>Result of a two-way sync across both vaults.</summary>
public sealed record VaultSyncResult(VaultSyncSideResult Local, VaultSyncSideResult Remote, int MergedTotal);

/// <summary>
/// Pure, deterministic two-way merge of two vault payloads.
/// Strategy: union by entry Id, last-write-wins by <see cref="VaultEntry.ModifiedAt"/>,
/// with delete propagation via the integrity log's "delete" tombstones. An edit that is newer
/// than a delete resurrects the entry (LWW). The merged payload is guaranteed to pass
/// <see cref="IntegrityValidator.ValidateConsistency"/>.
/// </summary>
public static class VaultMergeEngine
{
    public const string SyncAction = "sync";

    public static VaultPayload Merge(VaultPayload local, VaultPayload remote, ulong revision)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        // 1. Latest delete tombstone time per id across both sides.
        var deleteAt = new Dictionary<Guid, DateTimeOffset>();
        foreach (var log in EnumerateLogs(local, remote))
        {
            if (log.Action == "delete" && log.EntryId is { } id &&
                (!deleteAt.TryGetValue(id, out var existing) || log.Timestamp > existing))
            {
                deleteAt[id] = log.Timestamp;
            }
        }

        // 2. Best (newest) live version per id — remote wins ties so USB sync is not silently overridden.
        var live = new Dictionary<Guid, VaultEntry>();
        foreach (var entry in local.Entries)
            live[entry.Id] = entry;
        foreach (var entry in remote.Entries)
        {
            if (!live.TryGetValue(entry.Id, out var existing) || entry.ModifiedAt >= existing.ModifiedAt)
                live[entry.Id] = entry;
        }

        // 3. Apply tombstones — a delete at or after the newest edit removes the entry.
        var merged = new List<VaultEntry>();
        foreach (var (id, entry) in live)
        {
            if (deleteAt.TryGetValue(id, out var deletedTime) && deletedTime >= entry.ModifiedAt)
                continue;
            merged.Add(entry.Clone());
        }

        // Deterministic ordering so both vaults converge byte-for-byte on the entry set.
        merged.Sort(static (a, b) =>
        {
            var byCreated = a.CreatedAt.CompareTo(b.CreatedAt);
            return byCreated != 0 ? byCreated : a.Id.CompareTo(b.Id);
        });

        // 4. Rebuild a clean, consistent integrity log:
        //    preserved delete tombstones (for ids that did not survive) + one hash log per live entry.
        var mergedIds = merged.Select(e => e.Id).ToHashSet();
        var rebuiltLog = new List<IntegrityLogEntry>();
        foreach (var (id, timestamp) in deleteAt)
        {
            if (mergedIds.Contains(id))
                continue; // resurrected by a newer edit — drop the stale tombstone
            rebuiltLog.Add(new IntegrityLogEntry
            {
                Timestamp = timestamp,
                Action = "delete",
                EntryId = id,
                RevisionAfter = revision,
                EntryHash = []
            });
        }

        foreach (var entry in merged)
        {
            rebuiltLog.Add(new IntegrityLogEntry
            {
                Timestamp = entry.ModifiedAt,
                Action = SyncAction,
                EntryId = entry.Id,
                RevisionAfter = revision,
                EntryHash = IntegrityValidator.HashEntry(entry)
            });
        }

        var mergedBatches = local.ImportBatches
            .Concat(remote.ImportBatches)
            .GroupBy(b => b.Id)
            .Select(g => g.OrderByDescending(b => b.ImportedAt).First())
            .OrderByDescending(b => b.ImportedAt)
            .ToList();

        return new VaultPayload { Entries = merged, IntegrityLog = rebuiltLog, ImportBatches = mergedBatches };
    }

    private static IEnumerable<IntegrityLogEntry> EnumerateLogs(VaultPayload local, VaultPayload remote)
        => local.IntegrityLog.Concat(remote.IntegrityLog);
}

/// <summary>
/// Orchestrates a two-way sync between two unlocked vaults: merges their payloads and writes the
/// converged result back to both (each re-encrypted under its own key). Saving rotates each
/// vault's snapshots, so the pre-sync state remains recoverable.
/// </summary>
public static class VaultSynchronizer
{
    public static VaultSyncResult SyncTwoWay(
        VaultEngine localEngine,
        VaultUnlockContext local,
        VaultEngine remoteEngine,
        VaultUnlockContext remote)
    {
        ArgumentNullException.ThrowIfNull(localEngine);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remoteEngine);
        ArgumentNullException.ThrowIfNull(remote);

        if (local.ReadOnly || remote.ReadOnly)
            throw new InvalidOperationException("Both vaults must be writable to sync.");

        EnsureCompatibleVaults(local, remote);

        var localDir = Path.GetDirectoryName(localEngine.VaultPath)!;
        var remoteDir = Path.GetDirectoryName(remoteEngine.VaultPath)!;
        if (VaultSyncMarker.Exists(localDir) || VaultSyncMarker.Exists(remoteDir))
        {
            var marker = VaultSyncMarker.Read(localDir) ?? VaultSyncMarker.Read(remoteDir);
            var detail = marker?.Message
                ?? "A previous sync did not complete cleanly.";
            throw new VaultSyncDivergedException(
                detail + " Open Settings → Portable mode and run Sync again after verifying both "
                + "vault copies, or restore from a snapshot.");
        }

        var localBefore = SnapshotModified(local.Payload);
        var remoteBefore = SnapshotModified(remote.Payload);

        var revision = Math.Max(local.Header.RevisionCounter, remote.Header.RevisionCounter) + 1;
        var merged = VaultMergeEngine.Merge(local.Payload, remote.Payload, revision);

        // Validate before persisting so a bad merge never reaches disk.
        IntegrityValidator.ValidateConsistency(merged);

        var localResult = ComputeSide(localBefore, merged);
        var remoteResult = ComputeSide(remoteBefore, merged);

        var localBackup = ClonePayload(local.Payload);
        var remoteBackup = ClonePayload(remote.Payload);

        // Persist the remote vault first. The remote is typically the removable/portable copy
        // (USB), which is the most likely to fail (drive unplugged, disk full, read-only). If it
        // fails we have not touched the local vault at all, so the two cannot diverge — the sync is
        // simply aborted. Only after the remote is durably saved do we apply and save the local.
        VaultSyncMarker.WriteInProgressBoth(localDir, remoteDir);
        try
        {
            ApplyMerged(remote, merged);
            remoteEngine.Save(remote);

            ApplyMerged(local, merged);
            try
            {
                localEngine.Save(local);
            }
            catch (Exception ex)
            {
                try
                {
                    ApplyMerged(remote, remoteBackup);
                    remoteEngine.Save(remote);
                    ApplyMerged(local, localBackup);
                    throw new VaultSyncPartialException(
                        "Saving this vault failed after the other vault was updated. Both vaults were rolled "
                        + "back to their pre-sync state. Check free space and permissions, then run sync again.", ex);
                }
                catch (VaultSyncPartialException)
                {
                    throw;
                }
                catch (Exception rollbackEx)
                {
                    ApplyMerged(local, localBackup);
                    var message =
                        "Portable sync failed and could not roll back the USB/local copy. "
                        + "Do not edit either vault until you verify both copies and run Sync again, "
                        + "or restore from a snapshot.";
                    VaultSyncMarker.WriteDivergence(localDir, message, localDir, remoteDir);
                    VaultSyncMarker.WriteDivergence(remoteDir, message, localDir, remoteDir);
                    throw new VaultSyncDivergedException(message, rollbackEx);
                }
            }

            VaultSyncMarker.ClearBoth(localDir, remoteDir);
            return new VaultSyncResult(localResult, remoteResult, merged.Entries.Count);
        }
        catch (VaultSyncPartialException)
        {
            VaultSyncMarker.ClearBoth(localDir, remoteDir);
            throw;
        }
        catch (VaultSyncDivergedException)
        {
            throw;
        }
        catch
        {
            if (!VaultSyncMarker.HasDivergence(localDir) && !VaultSyncMarker.HasDivergence(remoteDir))
            {
                var message =
                    "Portable sync was interrupted. Verify both vault copies before editing or syncing again.";
                VaultSyncMarker.WriteDivergence(localDir, message, localDir, remoteDir);
                VaultSyncMarker.WriteDivergence(remoteDir, message, localDir, remoteDir);
            }

            throw;
        }
    }

    private static VaultPayload ClonePayload(VaultPayload source)
    {
        var clone = new VaultPayload();
        foreach (var entry in source.Entries)
            clone.Entries.Add(entry.Clone());

        foreach (var log in source.IntegrityLog)
            clone.IntegrityLog.Add(new IntegrityLogEntry
            {
                Timestamp = log.Timestamp,
                Action = log.Action,
                EntryId = log.EntryId,
                RevisionAfter = log.RevisionAfter,
                EntryHash = (byte[])log.EntryHash.Clone()
            });

        foreach (var batch in source.ImportBatches)
            clone.ImportBatches.Add(batch);

        return clone;
    }

    private static void ApplyMerged(VaultUnlockContext ctx, VaultPayload merged)
    {
        ctx.Payload.Entries.Clear();
        foreach (var entry in merged.Entries)
            ctx.Payload.Entries.Add(entry.Clone());

        ctx.Payload.IntegrityLog.Clear();
        foreach (var log in merged.IntegrityLog)
            ctx.Payload.IntegrityLog.Add(new IntegrityLogEntry
            {
                Timestamp = log.Timestamp,
                Action = log.Action,
                EntryId = log.EntryId,
                RevisionAfter = log.RevisionAfter,
                EntryHash = (byte[])log.EntryHash.Clone()
            });

        ctx.Payload.ImportBatches.Clear();
        foreach (var batch in merged.ImportBatches)
            ctx.Payload.ImportBatches.Add(batch);
    }

    private static Dictionary<Guid, DateTimeOffset> SnapshotModified(VaultPayload payload)
    {
        var map = new Dictionary<Guid, DateTimeOffset>();
        foreach (var entry in payload.Entries)
            map[entry.Id] = entry.ModifiedAt;
        return map;
    }

    private static VaultSyncSideResult ComputeSide(
        Dictionary<Guid, DateTimeOffset> before,
        VaultPayload merged)
    {
        var added = 0;
        var updated = 0;
        var mergedIds = new HashSet<Guid>();
        foreach (var entry in merged.Entries)
        {
            mergedIds.Add(entry.Id);
            if (!before.TryGetValue(entry.Id, out var previousModified))
                added++;
            else if (entry.ModifiedAt > previousModified)
                updated++;
        }

        var removed = before.Keys.Count(id => !mergedIds.Contains(id));
        return new VaultSyncSideResult(added, updated, removed, merged.Entries.Count);
    }

    private static void EnsureCompatibleVaults(VaultUnlockContext local, VaultUnlockContext remote)
    {
        if (local.Header.VaultId == remote.Header.VaultId)
            return;

        var remoteHasData = remote.Payload.Entries.Count > 0
            || remote.Payload.ImportBatches.Count > 0
            || remote.Header.RevisionCounter > 1;

        if (remoteHasData)
        {
            throw new VaultSyncDivergedException(
                "These vaults belong to different Fortiva databases and cannot be merged. "
                + "Point portable sync at the correct USB copy, or create a fresh empty folder for this vault.");
        }

        remote.Header.VaultId = local.Header.VaultId;
    }
}
