using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fortiva.AppHost.Services;

/// <summary>Fortiva Logo Icon 3 — transparent PNG (UI) and matching .ico (taskbar/window).</summary>
public static class BrandAssets
{
    /// <summary>UI logo PNG (transparent). Generated from Assets/source/fortiva-icon-source.png.</summary>
    public const string StandardLogoFile = "fortiva-logo.png";
    public const string ParanoiaLogoFile = "fortiva-logo-paranoia.png";
    public const string StandardIconFile = "fortiva.ico";
    public const string ParanoiaIconFile = "fortiva-paranoia.ico";
    public const string WebsiteUrl = "https://fortiva.studio.icmclab.cloud/";
    public const string PublisherLogoFile = "icmclab-logo.png";

    public static string LogoPath(bool paranoia)
        => Path.Combine(AppContext.BaseDirectory, "Assets", paranoia ? ParanoiaLogoFile : StandardLogoFile);

    public static string IconPath(bool paranoia)
        => Path.Combine(AppContext.BaseDirectory, "Assets", paranoia ? ParanoiaIconFile : StandardIconFile);

    public static void ApplyLogo(Image image, bool paranoia)
    {
        var path = LogoPath(paranoia);
        if (!File.Exists(path))
            path = PublisherLogoPath();
        if (!File.Exists(path)) return;

        image.Source = new BitmapImage(new Uri(path)) { DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical };
    }

    public static string PublisherLogoPath()
        => Path.Combine(AppContext.BaseDirectory, "Assets", PublisherLogoFile);

    /// <summary>icmclab studio publisher mark — Settings → About only (not the Fortiva app icon).</summary>
    public static void ApplyPublisherLogo(Image image)
    {
        var path = PublisherLogoPath();
        if (!File.Exists(path)) return;

        image.Source = new BitmapImage(new Uri(path)) { DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical };
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
