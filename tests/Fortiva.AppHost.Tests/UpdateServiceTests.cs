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
    public void SchedulePostUpdateRelaunchWatchdog_DoesNotThrow_WhenExeMissing()
    {
        using var process = Process.GetCurrentProcess();
        var ex = Record.Exception(() =>
            UpdateService.SchedulePostUpdateRelaunchWatchdog(process, Path.Combine(Path.GetTempPath(), "missing-fortiva.exe")));
        Assert.Null(ex);
    }
}
