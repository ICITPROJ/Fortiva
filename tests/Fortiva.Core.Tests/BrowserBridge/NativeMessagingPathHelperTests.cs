using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class NativeMessagingPathHelperTests
{
    [Fact]
    public void ForNativeHostManifest_PathWithSpaces_UsesShortPath()
    {
        var longPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "icmclab studio",
            "Fortiva Personal",
            "BrowserBridge",
            BridgeClientValidator.BridgeHostExecutableName);

        if (!File.Exists(longPath))
        {
            // Dev machines without Personal install skip.
            return;
        }

        var manifestPath = NativeMessagingPathHelper.ForNativeHostManifest(longPath);
        Assert.False(manifestPath.Contains(' ', StringComparison.Ordinal));
        Assert.True(NativeMessagingPathHelper.PathsReferToSameFile(longPath, manifestPath));
    }
}
