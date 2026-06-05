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
