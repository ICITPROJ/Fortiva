using Fortiva.Core.Policy;
using Xunit;

namespace Fortiva.Core.Tests.Policy;

public sealed class TotpPolicyTests
{
    [Fact]
    public void CanUseTotp_DisabledForPersonal()
        => Assert.False(PolicyEnforcer.CanUseTotp(isEnterpriseClient: false, policy: null));

    [Fact]
    public void CanUseTotp_EnabledForEnterprise()
        => Assert.True(PolicyEnforcer.CanUseTotp(isEnterpriseClient: true, FortivaPolicy.StrictEnterprise));

    [Fact]
    public void CanUseTotp_DisabledWhenPolicySaysSo()
    {
        var policy = new FortivaPolicy { TotpEnabled = false };
        Assert.False(PolicyEnforcer.CanUseTotp(isEnterpriseClient: true, policy));
    }
}
