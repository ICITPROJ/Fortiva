using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultEnginePolicyTests : IDisposable
{
    private readonly string _dir;

    public VaultEnginePolicyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-policy-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void CreateVault_UpgradesSecurityLevelWhenPolicyRequiresParanoia()
    {
        var policy = new FortivaPolicy { MandatoryParanoiaMode = true };
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser, policy);
        engine.CreateVault("policy-paranoia-test!", SecurityLevel.Standard);
        var ctx = engine.Unlock("policy-paranoia-test!");
        try
        {
            Assert.Equal(SecurityLevel.Paranoia, ctx.Header.SecurityLevel);
        }
        finally
        {
            ctx.Keys.Dispose();
        }
    }

    [Fact]
    public void Save_ThrowsWhenSecurityLevelBelowPolicyMinimum()
    {
        var policy = new FortivaPolicy { MandatoryParanoiaMode = true };
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser, policy);
        engine.CreateVault("policy-save-test!", SecurityLevel.Paranoia);
        var ctx = engine.Unlock("policy-save-test!");
        try
        {
            ctx.Header.SecurityLevel = SecurityLevel.Standard;
            Assert.Throws<InvalidOperationException>(() => engine.Save(ctx));
        }
        finally
        {
            ctx.Keys.Dispose();
        }
    }
}
