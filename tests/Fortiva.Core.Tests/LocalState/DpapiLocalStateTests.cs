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
            var result = store.CheckRollback(header, paranoiaMode: false);
            Assert.True(result.IsSuspicious);
            Assert.True(result.ForceReadOnly);
            Assert.Contains(result.Warnings, w => w.Contains("local.state", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.RequiresConfirmation);
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
