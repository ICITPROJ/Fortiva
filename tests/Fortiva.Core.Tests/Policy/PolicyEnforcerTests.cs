using Fortiva.Core.Crypto;
using Fortiva.Core.Policy;

namespace Fortiva.Core.Tests.Policy;

public class PolicyEnforcerTests
{
    [Fact]
    public void EnforceKdfMinimum_RaisesWeakParams()
    {
        var policy = FortivaPolicy.StrictEnterprise;
        var weak = Argon2Parameters.PersonalDefault;
        var enforced = PolicyEnforcer.EnforceKdfMinimum(weak, policy);
        Assert.True(enforced.MemoryKb >= policy.MinArgon2MemoryKb);
        Assert.True(enforced.Iterations >= policy.MinArgon2Iterations);
    }

    [Fact]
    public void PortableMode_ForbiddenByEnterprisePolicy()
    {
        Assert.False(PolicyEnforcer.CanUsePortableMode(FortivaPolicy.StrictEnterprise));
    }
}
