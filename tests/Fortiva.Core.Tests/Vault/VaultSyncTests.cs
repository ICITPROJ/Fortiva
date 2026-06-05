using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultSyncTests : IDisposable
{
    private readonly string _localDir;
    private readonly string _remoteDir;
    private const string Pwd = "sync-test-master-password-1!";

    public VaultSyncTests()
    {
        _localDir = Path.Combine(Path.GetTempPath(), "fortiva-sync-local-" + Guid.NewGuid());
        _remoteDir = Path.Combine(Path.GetTempPath(), "fortiva-sync-remote-" + Guid.NewGuid());
        Directory.CreateDirectory(_localDir);
        Directory.CreateDirectory(_remoteDir);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _localDir, _remoteDir })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    // ── Pure merge engine ────────────────────────────────────────────────────

    private static VaultEntry Entry(Guid id, string title, string pwd, DateTimeOffset modified)
        => new()
        {
            Id = id,
            Title = title,
            Password = pwd,
            CreatedAt = modified,
            ModifiedAt = modified
        };

    [Fact]
    public void Merge_UnionsDistinctEntries()
    {
        var a = new VaultPayload { Entries = { Entry(Guid.NewGuid(), "A", "pa", DateTimeOffset.UtcNow) } };
        var b = new VaultPayload { Entries = { Entry(Guid.NewGuid(), "B", "pb", DateTimeOffset.UtcNow) } };

        var merged = VaultMergeEngine.Merge(a, b, 5);

        Assert.Equal(2, merged.Entries.Count);
        IntegrityValidator.ValidateConsistency(merged);
    }

    [Fact]
    public void Merge_SameId_NewerModifiedWins()
    {
        var id = Guid.NewGuid();
        var older = Entry(id, "Old", "old-pass", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Entry(id, "New", "new-pass", DateTimeOffset.UtcNow);

        var a = new VaultPayload { Entries = { older } };
        var b = new VaultPayload { Entries = { newer } };

        var merged = VaultMergeEngine.Merge(a, b, 1);

        Assert.Single(merged.Entries);
        Assert.Equal("new-pass", merged.Entries[0].Password);
        IntegrityValidator.ValidateConsistency(merged);
    }

    [Fact]
    public void Merge_DeleteTombstone_RemovesEntryFromOtherSide()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Side A still has the entry; side B deleted it later.
        var a = new VaultPayload { Entries = { Entry(id, "Doomed", "p", createdAt) } };
        var b = new VaultPayload
        {
            IntegrityLog =
            {
                new IntegrityLogEntry
                {
                    Action = "delete",
                    EntryId = id,
                    Timestamp = DateTimeOffset.UtcNow,
                    EntryHash = []
                }
            }
        };

        var merged = VaultMergeEngine.Merge(a, b, 1);

        Assert.Empty(merged.Entries);
        IntegrityValidator.ValidateConsistency(merged);
    }

    [Fact]
    public void Merge_EditNewerThanDelete_ResurrectsEntry()
    {
        var id = Guid.NewGuid();

        // Deleted on B at T, then edited on A at T+5min → edit wins.
        var deleteTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var editTime = DateTimeOffset.UtcNow;

        var a = new VaultPayload { Entries = { Entry(id, "Revived", "p", editTime) } };
        var b = new VaultPayload
        {
            IntegrityLog =
            {
                new IntegrityLogEntry { Action = "delete", EntryId = id, Timestamp = deleteTime, EntryHash = [] }
            }
        };

        var merged = VaultMergeEngine.Merge(a, b, 1);

        Assert.Single(merged.Entries);
        Assert.Equal("Revived", merged.Entries[0].Title);
        IntegrityValidator.ValidateConsistency(merged);
    }

    [Fact]
    public void Merge_IsDeterministic_RegardlessOfArgumentOrder()
    {
        var e1 = Entry(Guid.NewGuid(), "One", "1", DateTimeOffset.UtcNow.AddMinutes(-2));
        var e2 = Entry(Guid.NewGuid(), "Two", "2", DateTimeOffset.UtcNow.AddMinutes(-1));
        var a = new VaultPayload { Entries = { e1 } };
        var b = new VaultPayload { Entries = { e2 } };

        var ab = VaultMergeEngine.Merge(a, b, 1);
        var ba = VaultMergeEngine.Merge(b, a, 1);

        Assert.Equal(
            ab.Entries.Select(e => e.Id),
            ba.Entries.Select(e => e.Id));
    }

    // ── End-to-end synchronizer over two real vaults ──────────────────────────

    private static (VaultEngine engine, VaultUnlockContext ctx) OpenOrCreate(string dir)
    {
        var engine = new VaultEngine(dir, DpapiScope.CurrentUser);
        if (!engine.VaultExists)
            engine.CreateVault(Pwd, SecurityLevel.Standard);
        return (engine, engine.Unlock(Pwd));
    }

    [Fact]
    public void SyncTwoWay_ConvergesBothVaults()
    {
        var (localEngine, local) = OpenOrCreate(_localDir);
        var (remoteEngine, remote) = OpenOrCreate(_remoteDir);
        try
        {
            localEngine.AddEntry(local, new VaultEntry { Title = "Desktop only", Password = "d1" });
            remoteEngine.AddEntry(remote, new VaultEntry { Title = "USB only", Password = "u1" });

            var result = VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            Assert.Equal(2, result.MergedTotal);
            Assert.Equal(2, local.Payload.Entries.Count);
            Assert.Equal(2, remote.Payload.Entries.Count);
            // local gained the USB entry; remote gained the desktop entry
            Assert.Equal(1, result.Local.Added);
            Assert.Equal(1, result.Remote.Added);
        }
        finally
        {
            local.Keys.Dispose();
            remote.Keys.Dispose();
        }

        // Reopen both from disk and confirm persistence + integrity.
        var reopenLocal = new VaultEngine(_localDir, DpapiScope.CurrentUser).Unlock(Pwd);
        var reopenRemote = new VaultEngine(_remoteDir, DpapiScope.CurrentUser).Unlock(Pwd);
        try
        {
            Assert.Equal(2, reopenLocal.Payload.Entries.Count);
            Assert.Equal(2, reopenRemote.Payload.Entries.Count);
            Assert.Equal(
                reopenLocal.Payload.Entries.Select(e => e.Title).OrderBy(t => t),
                reopenRemote.Payload.Entries.Select(e => e.Title).OrderBy(t => t));
        }
        finally
        {
            reopenLocal.Keys.Dispose();
            reopenRemote.Keys.Dispose();
        }
    }

    [Fact]
    public void SyncTwoWay_PropagatesDeleteToOtherVault()
    {
        var (localEngine, local) = OpenOrCreate(_localDir);
        var (remoteEngine, remote) = OpenOrCreate(_remoteDir);
        try
        {
            var shared = new VaultEntry { Title = "Shared", Password = "s1" };
            localEngine.AddEntry(local, shared);

            // First sync copies it to the remote.
            VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);
            Assert.Single(remote.Payload.Entries);
            var sharedId = remote.Payload.Entries[0].Id;

            // Delete on the remote, then sync again → must disappear from local too.
            remoteEngine.DeleteEntry(remote, sharedId);
            var result = VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            Assert.Equal(0, result.MergedTotal);
            Assert.Empty(local.Payload.Entries);
            Assert.Empty(remote.Payload.Entries);
        }
        finally
        {
            local.Keys.Dispose();
            remote.Keys.Dispose();
        }
    }

    [Fact]
    public void SyncTwoWay_ConflictingEdit_NewestWins()
    {
        var (localEngine, local) = OpenOrCreate(_localDir);
        var (remoteEngine, remote) = OpenOrCreate(_remoteDir);
        try
        {
            var entry = new VaultEntry { Title = "Login", Password = "original" };
            localEngine.AddEntry(local, entry);
            VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            var id = local.Payload.Entries[0].Id;

            // Edit on remote first (older), then on local (newer).
            var remoteCopy = remote.Payload.Entries.First(e => e.Id == id).Clone();
            remoteCopy.Password = "remote-edit";
            remoteCopy.ModifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            remoteEngine.UpdateEntry(remote, remoteCopy);

            var localCopy = local.Payload.Entries.First(e => e.Id == id).Clone();
            localCopy.Password = "local-edit-newer";
            localCopy.ModifiedAt = DateTimeOffset.UtcNow.AddMinutes(1);
            localEngine.UpdateEntry(local, localCopy);

            VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            Assert.Equal("local-edit-newer", local.Payload.Entries.First(e => e.Id == id).Password);
            Assert.Equal("local-edit-newer", remote.Payload.Entries.First(e => e.Id == id).Password);
        }
        finally
        {
            local.Keys.Dispose();
            remote.Keys.Dispose();
        }
    }

    [Fact]
    public void SyncTwoWay_Idempotent_SecondSyncNoChanges()
    {
        var (localEngine, local) = OpenOrCreate(_localDir);
        var (remoteEngine, remote) = OpenOrCreate(_remoteDir);
        try
        {
            localEngine.AddEntry(local, new VaultEntry { Title = "X", Password = "x" });
            remoteEngine.AddEntry(remote, new VaultEntry { Title = "Y", Password = "y" });
            VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            var second = VaultSynchronizer.SyncTwoWay(localEngine, local, remoteEngine, remote);

            Assert.Equal(0, second.Local.Added);
            Assert.Equal(0, second.Local.Updated);
            Assert.Equal(0, second.Local.Removed);
            Assert.Equal(0, second.Remote.Added);
            Assert.Equal(0, second.Remote.Updated);
            Assert.Equal(0, second.Remote.Removed);
        }
        finally
        {
            local.Keys.Dispose();
            remote.Keys.Dispose();
        }
    }
}
