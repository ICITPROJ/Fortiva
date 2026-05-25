using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Theme dictionary access and merge helpers for XAML pages, dialogs, and code-built UI.
/// </summary>
public static class FortivaThemeResources
{
    public static Brush Body => FortivaControlTheme.GetBrush("FortivaBodyBrush");
    public static Brush StatusSuccess => FortivaControlTheme.GetBrush("FortivaStatusSuccessBrush");
    public static Brush StatusWarning => FortivaControlTheme.GetBrush("FortivaStatusWarningBrush");
    public static Brush StatusError => FortivaControlTheme.GetBrush("FortivaStatusErrorBrush");

    public static Brush GetBrush(string key) => FortivaControlTheme.GetBrush(key);

    public static ElementTheme ResolveTheme() => FortivaControlTheme.ResolveEffectiveTheme();

    public static ResourceDictionary? GetDictionary(ElementTheme theme)
    {
        var key = theme == ElementTheme.Light ? "Light" : "Dark";
        if (Application.Current?.Resources.ThemeDictionaries.TryGetValue(key, out var dict) != true)
            return null;
        return dict as ResourceDictionary;
    }

    public static void MergeOnto(FrameworkElement element, ElementTheme? theme = null)
    {
        theme ??= ResolveTheme();
        element.RequestedTheme = theme.Value;

        var dict = GetDictionary(theme.Value);
        if (dict is null)
            return;

        if (!element.Resources.MergedDictionaries.Contains(dict))
            element.Resources.MergedDictionaries.Add(dict);
    }

    public static void MergeOnto(ContentDialog dialog, ElementTheme? theme = null)
    {
        theme ??= ResolveTheme();
        dialog.RequestedTheme = theme.Value;

        var dict = GetDictionary(theme.Value);
        if (dict is null)
            return;

        if (!dialog.Resources.MergedDictionaries.Contains(dict))
            dialog.Resources.MergedDictionaries.Add(dict);
    }
}
