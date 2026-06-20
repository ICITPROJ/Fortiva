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
        var script = UpdateService.BuildUpdateBatchScript(
            @"C:\Temp\FortivaPersonal-1.0.0-Setup.exe",
            "/VERYSILENT /FORCECLOSEAPPLICATIONS",
            @"C:\Users\me\AppData\Local\Programs\icmclab studio\Fortiva Personal\Fortiva.Personal.exe",
            @"C:\Temp\fortiva-update-abc.cmd");

        Assert.Contains("waitfortiva", script);
        Assert.Contains("start \"\" /wait \"C:\\Temp\\FortivaPersonal-1.0.0-Setup.exe\"", script);
        Assert.Contains("/VERYSILENT /FORCECLOSEAPPLICATIONS", script);
        Assert.Contains("Fortiva.Personal.exe", script);
    }

    [Fact]
    public void LaunchInstallerWithRelaunch_ReturnsNull_WhenInstallerMissing()
    {
        var result = UpdateService.LaunchInstallerWithRelaunch(
            Path.Combine(Path.GetTempPath(), "missing-fortiva-setup.exe"),
            UpdateUrlPolicy.DefaultInstallerArgs,
            UpdateService.ResolveInstalledExePath());
        Assert.Null(result);
    }

    [Fact]
    public void DefaultInstallerArgs_UsesForceCloseForSilentInAppUpdates()
    {
        Assert.Contains("FORCECLOSEAPPLICATIONS", UpdateUrlPolicy.DefaultInstallerArgs);
        Assert.Contains("VERYSILENT", UpdateUrlPolicy.DefaultInstallerArgs);
    }
}
