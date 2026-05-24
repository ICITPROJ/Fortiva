using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class FortivaPathsTests
{
    [Fact]
    public void PersonalUninstallDirectories_CoversAllKnownPaths()
    {
        var dirs = FortivaPaths.PersonalUninstallDirectories;
        Assert.Contains(FortivaPaths.PersonalDataRoot, dirs);
        Assert.Contains(FortivaPaths.PersonalLegacyDataRoot, dirs);
        Assert.Contains(FortivaPaths.PersonalCrashLogDirectory, dirs);
        Assert.Contains(FortivaPaths.PersonalLegacyLocalRoot, dirs);
        Assert.Equal(4, dirs.Count);
    }

    [Fact]
    public void EnterprisePaths_MatchInstallerContract()
    {
        Assert.EndsWith("Fortiva", FortivaPaths.EnterpriseProgramData);
        Assert.EndsWith(Path.Combine("Fortiva", "audit"), FortivaPaths.EnterpriseAuditDirectory);
        Assert.EndsWith(Path.Combine("FortivaEnterprise", "Hello"), FortivaPaths.EnterpriseHelloDirectory);
        Assert.Single(FortivaPaths.EnterpriseUninstallLocalDirectories);
        Assert.Equal(FortivaPaths.EnterpriseCrashLogDirectory, FortivaPaths.EnterpriseUninstallLocalDirectories[0]);
    }

    [Fact]
    public void AdminConfigFileNames_MatchExpectedArtifacts()
    {
        var names = FortivaPaths.AdminConfigFileNames;
        Assert.Contains("policies.json", names);
        Assert.Contains("license.dat", names);
        Assert.Contains("shared-vaults.json", names);
    }
}
