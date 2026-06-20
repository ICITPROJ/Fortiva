using System.Diagnostics;
using Fortiva.AppHost.Services;
using Fortiva.Core.Updates;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public void ResolveInstalledExePath_PrefersRunningProcessPath()
    {
        var expected = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(expected));

        var resolved = UpdateService.ResolveInstalledExePath();
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task ConfirmInstallerStartedAsync_ReturnsFalse_WhenProcessIsNull()
    {
        Assert.False(await UpdateService.ConfirmInstallerStartedAsync(null));
    }

    [Fact]
    public void TryStopBridgeHost_DoesNotThrow_WhenBridgeIsNotRunning()
    {
        var ex = Record.Exception(UpdateService.TryStopBridgeHost);
        Assert.Null(ex);
    }

    [Fact]
    public async Task ConfirmInstallerStartedAsync_ReturnsFalse_WhenProcessExitsImmediatelyWithError()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 1",
            UseShellExecute = true,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        Assert.False(await UpdateService.ConfirmInstallerStartedAsync(process));
    }

    [Fact]
    public void BuildUpdateBatchScript_WaitsForFortivaExitThenRunsInstaller()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "fortiva-update-test.log");
        var script = UpdateService.BuildUpdateBatchScript(
            @"C:\Temp\FortivaPersonal-1.0.0-Setup.exe",
            "/VERYSILENT /FORCECLOSEAPPLICATIONS",
            @"C:\Users\me\AppData\Local\Programs\icmclab studio\Fortiva Personal\Fortiva.Personal.exe",
            @"C:\Temp\fortiva-update-abc.cmd",
            logPath,
            "1.0.45");

        Assert.Contains("waitfortiva", script);
        Assert.Contains("start \"\" /wait \"C:\\Temp\\FortivaPersonal-1.0.0-Setup.exe\"", script);
        Assert.Contains("/VERYSILENT /FORCECLOSEAPPLICATIONS", script);
        Assert.Contains("relaunching Fortiva", script);
        Assert.Contains("Fortiva.Personal.exe", script);
    }

    [Fact]
    public void LaunchInstallerWithRelaunch_ReturnsFalse_WhenInstallerMissing()
    {
        var result = UpdateService.LaunchInstallerWithRelaunch(
            Path.Combine(Path.GetTempPath(), "missing-fortiva-setup.exe"),
            UpdateUrlPolicy.DefaultInstallerArgs,
            UpdateService.ResolveInstalledExePath(),
            "9.9.9");
        Assert.False(result);
    }

    [Fact]
    public void WriteUpdateLog_AppendsWithoutThrowing()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"fortiva-update-log-{Guid.NewGuid():N}.log");
        try
        {
            UpdateService.WriteUpdateLog(logPath, "test entry");
            Assert.Contains("test entry", File.ReadAllText(logPath));
        }
        finally
        {
            if (File.Exists(logPath))
                File.Delete(logPath);
        }
    }

    [Fact]
    public void DefaultInstallerArgs_UsesForceCloseForSilentInAppUpdates()
    {
        Assert.Contains("FORCECLOSEAPPLICATIONS", UpdateUrlPolicy.DefaultInstallerArgs);
        Assert.Contains("VERYSILENT", UpdateUrlPolicy.DefaultInstallerArgs);
    }
}
