using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class FortivaPathsUserDataTests
{
    [Fact]
    public void IsProtectedUserDataPath_RecognizesAppDataFortiva()
    {
        var vault = Path.Combine(FortivaPaths.PersonalDataRoot, "vault.fva");
        Assert.True(FortivaPaths.IsProtectedUserDataPath(vault));
    }

    [Fact]
    public void IsProtectedUserDataPath_RecognizesLocalFortivaPersonal()
    {
        var appearance = Path.Combine(FortivaPaths.PersonalCrashLogDirectory, "appearance.json");
        Assert.True(FortivaPaths.IsProtectedUserDataPath(appearance));
    }

    [Fact]
    public void EnsureSafeInstallTarget_AllowsProgramsFolder()
    {
        var install = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "icmclab studio", "Fortiva Personal");

        var ex = Record.Exception(() => FortivaPaths.EnsureSafeInstallTarget(install));
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureSafeInstallTarget_RejectsUserDataRoot()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FortivaPaths.EnsureSafeInstallTarget(FortivaPaths.PersonalDataRoot));
    }
}
