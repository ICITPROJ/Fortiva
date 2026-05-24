using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Policy;

public class PolicySecurityLevelTests
{
    [Fact]
    public void EnforceMinimumSecurityLevel_UpgradesToParanoiaWhenRequired()
    {
        var policy = new FortivaPolicy { MandatoryParanoiaMode = true };
        var level = PolicyEnforcer.EnforceMinimumSecurityLevel(SecurityLevel.Standard, policy);
        Assert.Equal(SecurityLevel.Paranoia, level);
    }

    [Fact]
    public void EnsureWritableSecurityLevel_ThrowsWhenBelowPolicyMinimum()
    {
        var policy = new FortivaPolicy { MandatoryParanoiaMode = true };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyEnforcer.EnsureWritableSecurityLevel(SecurityLevel.Standard, policy));
        Assert.Contains("Paranoia", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
