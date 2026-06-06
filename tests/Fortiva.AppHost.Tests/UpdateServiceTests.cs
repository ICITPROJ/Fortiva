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
    public void DefaultInstallerArgs_UsesForceCloseForSilentInAppUpdates()
    {
        Assert.Contains("FORCECLOSEAPPLICATIONS", UpdateUrlPolicy.DefaultInstallerArgs);
        Assert.Contains("VERYSILENT", UpdateUrlPolicy.DefaultInstallerArgs);
    }
}
