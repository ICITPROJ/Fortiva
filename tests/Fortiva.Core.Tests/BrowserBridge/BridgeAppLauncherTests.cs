using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class BridgeAppLauncherTests
{
    [Fact]
    public void ResolveInstallRootFromBridgeDir_InfersParentOfBrowserBridge()
    {
        var bridgeDir = @"C:\Programs\Fortiva Personal\BrowserBridge";
        var root = BridgeAppLauncher.ResolveInstallRootFromBridgeDir(bridgeDir);
        Assert.NotNull(root);
        Assert.EndsWith("Fortiva Personal", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstalledPersonal_AllowsLaunchPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bridgeDir = Path.Combine(local, "Programs", "icmclab studio", "Fortiva Personal", "BrowserBridge");
        if (!Directory.Exists(bridgeDir))
            return;

        var root = BridgeAppLauncher.ResolveInstallRootFromBridgeDir(bridgeDir);
        if (root is null)
            return;

        var personal = Path.Combine(root, "Fortiva.Personal.exe");
        if (!File.Exists(personal))
            return;

        Assert.True(BridgeClientValidator.IsAllowedExecutablePath(personal, [root]));
    }
}
