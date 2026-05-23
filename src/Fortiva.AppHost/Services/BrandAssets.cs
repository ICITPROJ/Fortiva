using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fortiva.AppHost.Services;

/// <summary>Fortiva logo and window icon paths. Standard = Logo 1; Paranoia = Logo 3 (glowing).</summary>
public static class BrandAssets
{
    public const string StandardLogoFile = "fortiva-logo.png";
    public const string ParanoiaLogoFile = "fortiva-logo-paranoia.png";
    public const string StandardIconFile = "fortiva.ico";
    public const string ParanoiaIconFile = "fortiva-paranoia.ico";

    public static string LogoPath(bool paranoia)
        => Path.Combine(AppContext.BaseDirectory, "Assets", paranoia ? ParanoiaLogoFile : StandardLogoFile);

    public static string IconPath(bool paranoia)
        => Path.Combine(AppContext.BaseDirectory, "Assets", paranoia ? ParanoiaIconFile : StandardIconFile);

    public static void ApplyLogo(Image image, bool paranoia)
    {
        var path = LogoPath(paranoia);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "icmclab-logo.png");
        if (!File.Exists(path)) return;

        image.Source = new BitmapImage(new Uri(path));
    }

    public static void ApplyWindowIcon(Microsoft.UI.Windowing.AppWindow appWindow, bool paranoia)
    {
        var path = IconPath(paranoia);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "Assets", StandardIconFile);
        if (File.Exists(path))
            appWindow.SetIcon(path);
    }
}
