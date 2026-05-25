using Fortiva.AppHost.Services;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public void LaunchInstallerWithRestart_WritesScriptThatWaitsThenStartsApp()
    {
        var installer = Path.Combine(Path.GetTempPath(), "FortivaPersonal-9.9.9-Setup.exe");
        var appExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "icmclab studio", "Fortiva Personal", "Fortiva.Personal.exe");
        const string args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

        var before = Directory.GetFiles(Path.GetTempPath(), "fortiva-update-*.cmd").Length;
        UpdateService.LaunchInstallerWithRestart(installer, appExe, args);
        var scripts = Directory.GetFiles(Path.GetTempPath(), "fortiva-update-*.cmd");
        Assert.Equal(before + 1, scripts.Length);

        try
        {
            var content = File.ReadAllText(scripts[^1]);
            Assert.Contains($"start /wait \"\" \"{installer}\" {args}", content);
            Assert.Contains($"if exist \"{appExe}\" start \"\" \"{appExe}\"", content);
            Assert.Contains("del \"%~f0\"", content);
        }
        finally
        {
            foreach (var script in scripts.Where(f => f.Contains("fortiva-update-", StringComparison.Ordinal)))
            {
                try { File.Delete(script); } catch { }
            }
        }
    }

    [Fact]
    public void ResolveInstalledExePath_PrefersRunningProcessPath()
    {
        var expected = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(expected));

        var resolved = UpdateService.ResolveInstalledExePath();
        Assert.Equal(expected, resolved);
    }
}
