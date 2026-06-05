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

        var placeholder = new ReleaseManifest
        {
            Version = "1.0.0",
            InstallerUrl = "https://example.com/setup.exe",
            InstallerSha256 = new string('0', 64)
        };
        Assert.False(placeholder.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    public void TryParseVersion_rejects_invalid_values(string value)
    {
        Assert.False(AppVersion.TryParseVersion(value, out _));
    }

    [Fact]
    public void ReleaseManifest_rejects_invalid_version_string()
    {
        var manifest = new ReleaseManifest
        {
            Version = "not-a-version",
            InstallerUrl = "https://example.com/setup.exe",
            InstallerSha256 = new string('a', 64)
        };
        Assert.False(manifest.IsValid);
    }

    [Fact]
    public void ResolveInstallerArgs_uses_manifest_when_safe()
    {
        var manifest = new ReleaseManifest
        {
            InstallerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS"
        };
        Assert.Equal(manifest.InstallerArgs, UpdateUrlPolicy.ResolveInstallerArgs(manifest));
    }

    [Fact]
    public void ResolveInstallerArgs_falls_back_on_unsafe_tokens()
    {
        var manifest = new ReleaseManifest { InstallerArgs = "/VERYSILENT & calc.exe" };
        Assert.Equal(UpdateUrlPolicy.DefaultInstallerArgs, UpdateUrlPolicy.ResolveInstallerArgs(manifest));
    }

    [Theory]
    [InlineData("/LOADINF=\\\\attacker\\share\\evil.inf")]
    [InlineData("/DIR=C:\\attacker")]
    [InlineData("/LOG=C:\\x\\out.log")]
    [InlineData("/VERYSILENT /DIR=C:\\attacker")]
    public void ResolveInstallerArgs_rejects_value_bearing_switches(string args)
    {
        var manifest = new ReleaseManifest { InstallerArgs = args };
        Assert.Equal(UpdateUrlPolicy.DefaultInstallerArgs, UpdateUrlPolicy.ResolveInstallerArgs(manifest));
    }
}
