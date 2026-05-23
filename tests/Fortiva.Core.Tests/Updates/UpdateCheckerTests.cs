using System.Net.Http;
using System.Net.Sockets;
using Fortiva.Core.Updates;

namespace Fortiva.Core.Tests.Updates;

public sealed class UpdateMessagesTests
{
    [Fact]
    public void ForCheckFailure_maps_host_not_found()
    {
        var ex = new HttpRequestException("No such host is known. (github.com:443)");
        var msg = UpdateMessages.ForCheckFailure(ex);
        Assert.Contains("update server", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.com:443", msg);
    }

    [Fact]
    public void ForCheckFailure_maps_socket_host_not_found()
    {
        var msg = UpdateMessages.ForCheckFailure(new SocketException((int)SocketError.HostNotFound));
        Assert.Contains("update server", msg, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class UpdateCheckerTests
{
    [Fact]
    public void Evaluate_offline_same_version_is_up_to_date()
    {
        var manifest = SampleManifest("1.0.0");
        var result = ReleaseManifestEvaluator.Evaluate(manifest, "1.0.0", fromNetwork: false);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.IsOnlineManifest);
        Assert.Contains("latest version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_offline_newer_version_reports_without_install_prompt()
    {
        var manifest = SampleManifest("2.0.0");
        var result = ReleaseManifestEvaluator.Evaluate(manifest, "1.0.0", fromNetwork: false);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.False(result.IsOnlineManifest);
        Assert.Contains("Connect to the internet", result.Message);
    }

    private static ReleaseManifest SampleManifest(string version) => new()
    {
        Version = version,
        MaxWindowsBuildTested = 99999,
        InstallerUrl = "https://github.com/ICITPROJ/Fortiva/releases/download/v1.0.0/FortivaPersonal-1.0.0-Setup.exe",
        InstallerSha256 = new string('a', 64)
    };
}
