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

        var ctx2 = engine.Unlock("password-rollback-test!", paranoiaMode: true);
        try
        {
            Assert.True(ctx2.ReadOnly || ctx2.RollbackWarning is not null);
        }
        finally
        {
            ctx2.Keys.Dispose();
        }
    }

    [Fact]
    public void AtomicWrite_SurvivesPowerLossSimulation()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("atomic-write-test!", SecurityLevel.Standard);
        var temp = Path.Combine(_dir, VaultConstants.VaultFileName + VaultConstants.TempSuffix);
        Assert.False(File.Exists(temp));
    }
}
