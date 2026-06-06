using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultEngineTests : IDisposable
{
    private readonly string _dir;

    public VaultEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Create_Unlock_AddEntry_Snapshot()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("test-master-password-123!", SecurityLevel.Standard);
        Assert.True(File.Exists(engine.VaultPath));

        var ctx = engine.Unlock("test-master-password-123!");
        try
        {
            engine.AddEntry(ctx, new VaultEntry
            {
                Title = "Example",
                Username = "user",
                Password = "p@ssw0rd!",
                Url = "https://example.com"
            });
            Assert.Single(ctx.Payload.Entries);
        }
        finally
        {
            ctx.Keys.Dispose();
        }

        for (var i = 1; i <= VaultConstants.SnapshotCount + 1; i++)
        {
            var ctx2 = engine.Unlock("test-master-password-123!");
            ctx2.Keys.Dispose();
        }
        Assert.True(File.Exists(Path.Combine(_dir, VaultConstants.SnapshotFileName(1))));
    }

    [Fact]
    public void WrongPassword_Throws()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("correct-password-xyz!", SecurityLevel.Standard);
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            engine.Unlock("wrong-password"));
    }

    [Fact]
    public void RollbackDetection_WarnsOnDowngrade()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("password-rollback-test!", SecurityLevel.Paranoia);
        var ctx = engine.Unlock("password-rollback-test!");
        ctx.Header.SecurityLevel = SecurityLevel.Standard;
        ctx.Header.SecurityLevelCounter = 0;
        engine.Save(ctx);
        ctx.Keys.Dispose();

        var ctx2 = engine.Unlock("password-rollback-test!", paranoiaMode: false, confirmRollback: false);
        try
        {
            Assert.True(ctx2.ReadOnly);
            Assert.NotNull(ctx2.RollbackWarning);
        }
        finally
        {
            ctx2.Keys.Dispose();
        }
    }

    [Fact]
    public void ConcurrentSave_FromSecondInstance_IsRejected()
    {
        var engineA = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engineA.CreateVault("concurrency-test-password-1!", SecurityLevel.Standard);

        // Two independent engine instances open the same vault (e.g. two app windows/processes).
        var ctxA = engineA.Unlock("concurrency-test-password-1!");
        var engineB = new VaultEngine(_dir, DpapiScope.CurrentUser);
        var ctxB = engineB.Unlock("concurrency-test-password-1!");
        try
        {
            // A saves first and wins.
            engineA.AddEntry(ctxA, new VaultEntry { Title = "A's entry", Username = "a", Password = "pa" });

            // B still holds the stale revision; its save must be rejected rather than clobber A.
            Assert.Throws<VaultConcurrencyException>(() =>
                engineB.AddEntry(ctxB, new VaultEntry { Title = "B's entry", Username = "b", Password = "pb" }));

            // A's change survived; B's in-memory add was rolled back.
            var verify = engineA.Unlock("concurrency-test-password-1!");
            try
            {
                Assert.Single(verify.Payload.Entries);
                Assert.Equal("A's entry", verify.Payload.Entries[0].Title);
            }
            finally
            {
                verify.Keys.Dispose();
            }
        }
        finally
        {
            ctxA.Keys.Dispose();
            ctxB.Keys.Dispose();
        }
    }

    [Fact]
    public void InterruptedWrite_RecoversVaultFromBackup()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("interrupted-write-password-1!", SecurityLevel.Standard);
        var ctx = engine.Unlock("interrupted-write-password-1!");
        engine.AddEntry(ctx, new VaultEntry { Title = "Recoverable", Username = "u", Password = "p" });
        ctx.Keys.Dispose();

        // Simulate a crash partway through the FAT32 replace fallback: vault.fva moved to .bak,
        // new file not yet moved into place.
        var vaultPath = Path.Combine(_dir, VaultConstants.VaultFileName);
        var backupPath = vaultPath + VaultConstants.BackupSuffix;
        File.Move(vaultPath, backupPath);
        Assert.False(File.Exists(vaultPath));

        // Opening the engine again should auto-recover from the backup.
        var recovered = new VaultEngine(_dir, DpapiScope.CurrentUser);
        Assert.True(recovered.VaultExists);
        Assert.False(File.Exists(backupPath));

        var ctx2 = recovered.Unlock("interrupted-write-password-1!");
        try
        {
            Assert.Single(ctx2.Payload.Entries);
            Assert.Equal("Recoverable", ctx2.Payload.Entries[0].Title);
        }
        finally
        {
            ctx2.Keys.Dispose();
        }
    }

    [Fact]
    public void InterruptedWrite_RecoversVaultFromSnapshotWhenNoBackup()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("snapshot-recovery-password-1!", SecurityLevel.Standard);
        var ctx = engine.Unlock("snapshot-recovery-password-1!");
        engine.AddEntry(ctx, new VaultEntry { Title = "FromSnapshot", Username = "u", Password = "p" });
        ctx.Keys.Dispose();
        // Ensure a snapshot exists.
        engine.Unlock("snapshot-recovery-password-1!").Keys.Dispose();

        var vaultPath = Path.Combine(_dir, VaultConstants.VaultFileName);
        Assert.True(File.Exists(Path.Combine(_dir, VaultConstants.SnapshotFileName(1))));
        File.Delete(vaultPath);

        var recovered = new VaultEngine(_dir, DpapiScope.CurrentUser);
        Assert.True(recovered.VaultExists);
        var ctx2 = recovered.Unlock("snapshot-recovery-password-1!");
        try
        {
            Assert.Contains(ctx2.Payload.Entries, e => e.Title == "FromSnapshot");
        }
        finally
        {
            ctx2.Keys.Dispose();
        }
    }

    [Fact]
    public void MissingLocalState_AfterReinstall_RecoversAfterConfirm()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("reinstall-recovery-password-1!", SecurityLevel.Standard);
        var ctx = engine.Unlock("reinstall-recovery-password-1!");
        engine.AddEntry(ctx, new VaultEntry { Title = "Bank", Username = "me", Password = "secret!" });
        ctx.Keys.Dispose();

        // Simulate reinstall / new PC / restoring only vault.fva: local.state is gone.
        File.Delete(Path.Combine(_dir, "local.state"));

        // First unlock without confirmation: warned + read-only (so the user notices).
        var warned = engine.Unlock("reinstall-recovery-password-1!", paranoiaMode: false, confirmRollback: false);
        Assert.True(warned.ReadOnly);
        Assert.NotNull(warned.RollbackWarning);
        warned.Keys.Dispose();

        // Confirming recovers full read-write access and re-establishes local.state.
        var confirmed = engine.Unlock("reinstall-recovery-password-1!", paranoiaMode: false, confirmRollback: true);
        try
        {
            Assert.False(confirmed.ReadOnly);
            engine.AddEntry(confirmed, new VaultEntry { Title = "Email", Username = "me", Password = "second!" });
            Assert.Equal(2, confirmed.Payload.Entries.Count);
        }
        finally
        {
            confirmed.Keys.Dispose();
        }

        // local.state was rebuilt, so a subsequent unlock is clean (no warning, writable).
        Assert.True(File.Exists(Path.Combine(_dir, "local.state")));
        var clean = engine.Unlock("reinstall-recovery-password-1!");
        try
        {
            Assert.False(clean.ReadOnly);
            Assert.Null(clean.RollbackWarning);
        }
        finally
        {
            clean.Keys.Dispose();
        }
    }

    [Fact]
    public void RollbackDetection_ConfirmStillReadOnlyInParanoiaOnSecurityDowngrade()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("password-rollback-test!", SecurityLevel.Paranoia);
        var ctx = engine.Unlock("password-rollback-test!");
        ctx.Header.SecurityLevel = SecurityLevel.Standard;
        ctx.Header.SecurityLevelCounter = 0;
        engine.Save(ctx);
        ctx.Keys.Dispose();

        var ctx2 = engine.Unlock("password-rollback-test!", paranoiaMode: true, confirmRollback: true);
        try
        {
            Assert.True(ctx2.ReadOnly);
            Assert.NotNull(ctx2.RollbackWarning);
        }
        finally
        {
            ctx2.Keys.Dispose();
        }
    }

    [Fact]
    public void RepeatedSaves_KeepVaultConsistent_NoLeftoverTempOrBackup()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("repeated-save-password-1!", SecurityLevel.Standard);

        for (var i = 0; i < 25; i++)
        {
            var ctx = engine.Unlock("repeated-save-password-1!");
            try
            {
                engine.AddEntry(ctx, new VaultEntry { Title = "Entry " + i, Password = "p" + i });
            }
            finally
            {
                ctx.Keys.Dispose();
            }
        }

        var verify = engine.Unlock("repeated-save-password-1!");
        try
        {
            Assert.Equal(25, verify.Payload.Entries.Count);
        }
        finally
        {
            verify.Keys.Dispose();
        }

        Assert.False(File.Exists(Path.Combine(_dir, VaultConstants.VaultFileName + VaultConstants.TempSuffix)));
        Assert.False(File.Exists(Path.Combine(_dir, VaultConstants.VaultFileName + ".bak")));
    }

    [Fact]
    public void CreateVault_ThrowsWhenVaultAlreadyExists()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("first-password-123!", SecurityLevel.Standard);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.CreateVault("second-password-456!", SecurityLevel.Standard));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
