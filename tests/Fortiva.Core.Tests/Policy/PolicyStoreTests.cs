using Fortiva.Core.Policy;

namespace Fortiva.Core.Tests.Policy;

public class PolicyStoreTests
{
    [Fact]
    public void Load_MissingFile_PersonalDefault_WhenNotEnterprise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortiva-policy-{Guid.NewGuid():N}.json");
        var policy = PolicyStore.Load(path, enterpriseDefaultsWhenMissing: false);
        Assert.Equal(FortivaPolicy.PersonalDefault.MaxAutoLockSeconds, policy.MaxAutoLockSeconds);
        Assert.False(policy.MandatoryWindowsHello);
    }

    [Fact]
    public void Load_MissingFile_StrictEnterprise_WhenEnterprise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortiva-policy-{Guid.NewGuid():N}.json");
        var policy = PolicyStore.Load(path, enterpriseDefaultsWhenMissing: true);
        Assert.Equal(FortivaPolicy.StrictEnterprise.MaxAutoLockSeconds, policy.MaxAutoLockSeconds);
        Assert.True(policy.MandatoryWindowsHello);
    }
}
