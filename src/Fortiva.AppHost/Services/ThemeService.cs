using Microsoft.UI.Xaml;

namespace Fortiva.AppHost.Services;

public static class ThemeService
{
    public static void Apply(Window window, AppThemePreference preference)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = ToElementTheme(preference);
    }

    public static ElementTheme ToElementTheme(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

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
}
