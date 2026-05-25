using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Fortiva.AppHost.Services;

public static class ThemeService
{
    private static Window? _window;
    private static AppThemePreference _preference;
    private static FrameworkElement? _root;
    private static bool _systemHooked;

    /// <summary>Must run before any Window/XAML is created (App.OnLaunched).</summary>
    public static void ApplyApplicationThemeEarly(AppThemePreference preference)
    {
        if (Application.Current is null)
            return;

        try
        {
            Application.Current.RequestedTheme = preference switch
            {
                AppThemePreference.Light => ApplicationTheme.Light,
                AppThemePreference.Dark => ApplicationTheme.Dark,
                _ => GetSystemApplicationTheme()
            };
        }
        catch
        {
            // WinUI forbids changing Application.RequestedTheme after resources init — element theme still applies.
        }
    }

    public static void Apply(Window window, AppThemePreference preference)
    {
        _window = window;
        _preference = preference;

        if (window.Content is not FrameworkElement root)
            return;

        _root = root;
        ApplyElementTheme(root, preference);
        ApplyTitleBar(window, IsLightTheme(root, preference));
        EnsureSystemThemeHook(root);
    }

    /// <summary>Windows 11 Mica / acrylic system backdrop for a glass shell.</summary>
    public static void ApplySystemBackdrop(Window window)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            return;
        }

        if (DesktopAcrylicController.IsSupported())
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
    }

    public static ElementTheme ToElementTheme(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ResolveSystemElementTheme()
    };

    public static ElementTheme ResolveSystemElementTheme()
    {
        var bg = new UISettings().GetColorValue(UIColorType.Background);
        return (bg.R + bg.G + bg.B) > 383 ? ElementTheme.Light : ElementTheme.Dark;
    }

    /// <summary>Apply resolved Fortiva theme to a page or subtree (matches code-built UI).</summary>
    public static void ApplyToElement(FrameworkElement element, AppThemePreference? preference = null)
    {
        var pref = preference ?? _preference;
        var theme = ToElementTheme(pref);
        element.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(element, theme);
    }

    public static AppThemePreference Parse(string? value) => value switch
    {
        "Light" => AppThemePreference.Light,
        "Dark" => AppThemePreference.Dark,
        _ => AppThemePreference.System
    };

    public static string ToTag(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => "Light",
        AppThemePreference.Dark => "Dark",
        _ => "System"
    };

    private static ApplicationTheme GetSystemApplicationTheme()
    {
        var bg = new UISettings().GetColorValue(UIColorType.Background);
        return (bg.R + bg.G + bg.B) > 383 ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }

    private static void ApplyElementTheme(FrameworkElement root, AppThemePreference preference)
    {
        var theme = ToElementTheme(preference);
        root.RequestedTheme = theme;

        if (FindDescendant<NavigationView>(root) is { } navView)
            navView.RequestedTheme = theme;

        if (FindDescendant<Frame>(root) is { } frame)
        {
            frame.RequestedTheme = theme;
            if (frame.Content is FrameworkElement page)
                ApplyToElement(page, preference);
        }
    }

    private static void EnsureSystemThemeHook(FrameworkElement root)
    {
        if (_systemHooked)
            return;

        _systemHooked = true;
        root.ActualThemeChanged += (_, _) =>
        {
            if (_preference != AppThemePreference.System || _window is null || _root is null)
                return;

            ApplyElementTheme(_root, _preference);
            ApplyTitleBar(_window, _root.ActualTheme == ElementTheme.Light);
        };
    }

    private static bool IsLightTheme(FrameworkElement root, AppThemePreference preference) =>
        preference switch
        {
            AppThemePreference.Light => true,
            AppThemePreference.Dark => false,
            _ => root.ActualTheme == ElementTheme.Light
        };

    private static void ApplyTitleBar(Window window, bool light)
    {
        var titleBar = window.AppWindow.TitleBar;
        if (!titleBar.ExtendsContentIntoTitleBar)
            return;

        if (light)
        {
            titleBar.ButtonForegroundColor = Colors.Black;
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonPressedForegroundColor = Colors.Black;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 120, 120, 120);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
        }
        else
        {
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 70, 70, 70);
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
