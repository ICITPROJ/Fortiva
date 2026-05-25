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

        ReplaceThemeDictionary(element.Resources, dict);
    }

    public static void MergeOnto(ContentDialog dialog, ElementTheme? theme = null)
    {
        theme ??= ResolveTheme();
        dialog.RequestedTheme = theme.Value;

        var dict = GetDictionary(theme.Value);
        if (dict is null)
            return;

        ReplaceThemeDictionary(dialog.Resources, dict);
    }

    /// <summary>Keep a single Fortiva theme dictionary — avoid stale Light/Dark merges after toggling.</summary>
    private static void ReplaceThemeDictionary(ResourceDictionary resources, ResourceDictionary themeDict)
    {
        for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (IsAppThemeDictionary(resources.MergedDictionaries[i]))
                resources.MergedDictionaries.RemoveAt(i);
        }

        if (!resources.MergedDictionaries.Contains(themeDict))
            resources.MergedDictionaries.Add(themeDict);
    }

    private static bool IsAppThemeDictionary(ResourceDictionary dict)
    {
        if (Application.Current?.Resources.ThemeDictionaries is not ResourceDictionary themeDictionaries)
            return false;

        foreach (var value in themeDictionaries.Values)
        {
            if (ReferenceEquals(value, dict))
                return true;
        }

        return false;
    }
}
