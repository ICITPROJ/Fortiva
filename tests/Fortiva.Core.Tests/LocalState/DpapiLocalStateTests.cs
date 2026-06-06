using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.LocalState;

public sealed class DpapiLocalStateTests
{
    [Fact]
    public void CheckRollback_MissingLocalState_FlagsEstablishedVault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FortivaLocalState-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var store = new DpapiLocalStateStore(dir, DpapiScope.CurrentUser);

        var header = new VaultHeader
        {
            VaultId = Guid.NewGuid(),
            RevisionCounter = 2,
            SecurityLevel = SecurityLevel.Standard,
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Outside Paranoia Mode the missing-state case must be confirmable (not forced read-only)
            // so legitimate reinstall / new-PC / restore scenarios can recover.
            var result = store.CheckRollback(header, paranoiaMode: false);
            Assert.True(result.IsSuspicious);
            Assert.False(result.ForceReadOnly);
            Assert.Contains(result.Warnings, w => w.Contains("local.state", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.RequiresConfirmation);

            // Paranoia Mode keeps the strict behavior (forced read-only until state is restored).
            var paranoid = store.CheckRollback(header, paranoiaMode: true);
            Assert.True(paranoid.IsSuspicious);
            Assert.True(paranoid.ForceReadOnly);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CheckRollback_MissingLocalState_OkForNewVault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FortivaLocalState-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var store = new DpapiLocalStateStore(dir, DpapiScope.CurrentUser);

        var header = new VaultHeader
        {
            VaultId = Guid.NewGuid(),
            RevisionCounter = 1,
            SecurityLevel = SecurityLevel.Standard,
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var result = store.CheckRollback(header, paranoiaMode: false);
            Assert.False(result.IsSuspicious);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
