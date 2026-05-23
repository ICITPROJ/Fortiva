using Fortiva.Core.Updates;

namespace Fortiva.Core.Tests.Updates;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    public void IsRemoteNewer_compares_semver(string remote, string local, bool expected)
        => Assert.Equal(expected, AppVersion.IsRemoteNewer(remote, local));

    [Fact]
    public void ReleaseManifest_validates_required_fields()
    {
        var ok = new ReleaseManifest
        {
            Version = "1.0.1",
            InstallerUrl = "https://example.com/setup.exe",
            InstallerSha256 = new string('a', 64)
        };
        Assert.True(ok.IsValid);

        var bad = new ReleaseManifest { Version = "1.0.1" };
        Assert.False(bad.IsValid);
    }
}
