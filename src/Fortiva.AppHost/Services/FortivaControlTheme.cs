using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>Applies Fortiva theme brushes to code-built controls (dialogs, panels).</summary>
public static class FortivaControlTheme
{
    public static ElementTheme ResolveEffectiveTheme(XamlRoot? xamlRoot = null, FrameworkElement? element = null)
    {
        var preference = ShellViewModel.Current.ThemePreference;
        if (preference == AppThemePreference.Light)
            return ElementTheme.Light;
        if (preference == AppThemePreference.Dark)
            return ElementTheme.Dark;

        if (element?.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            return element.ActualTheme;

        if (xamlRoot?.Content is FrameworkElement root && root.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            return root.ActualTheme;

        return Application.Current?.RequestedTheme == ApplicationTheme.Light
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public static Brush GetBrush(string key, ElementTheme? theme = null, FrameworkElement? context = null)
    {
        theme ??= ResolveEffectiveTheme(context?.XamlRoot, context);
        var dictKey = theme == ElementTheme.Light ? "Light" : "Dark";

        if (Application.Current?.Resources is ResourceDictionary appResources
            && appResources.ThemeDictionaries.TryGetValue(dictKey, out var themeDictObj)
            && themeDictObj is ResourceDictionary themeDict
            && themeDict.TryGetValue(key, out var themedValue)
            && themedValue is Brush themedBrush)
            return themedBrush;

        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return brush;

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public static void ApplyTextBox(TextBox box, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(box.XamlRoot, box);
        FortivaThemeResources.MergeOnto(box, theme);
        box.RequestedTheme = theme;
        box.Background = GetBrush("FortivaInputFillBrush", theme, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", theme, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", theme, box);
        box.PlaceholderForeground = GetBrush("FortivaMutedBrush", theme, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(12, 10, 12, 10);
        if (box.MinHeight < 44)
            box.MinHeight = 44;
        if (box.FontSize < 14)
            box.FontSize = 14;
    }

    public static void ApplyPasswordBox(PasswordBox box, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(box.XamlRoot, box);
        box.RequestedTheme = theme;
        box.Background = GetBrush("FortivaInputFillBrush", theme, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", theme, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", theme, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(14, 12, 14, 12);
        if (box.MinHeight < 44)
            box.MinHeight = 44;
    }

    public static void ApplyComboBox(ComboBox box, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(box.XamlRoot, box);
        FortivaThemeResources.MergeOnto(box, theme);
        box.RequestedTheme = theme;
        box.Background = GetBrush("FortivaInputFillBrush", theme, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", theme, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", theme, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(12, 8, 12, 8);
        box.MinHeight = 44;
        box.FontSize = 14;
    }

    public static void ApplyPreviewSurface(Border border, TextBlock content, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(border.XamlRoot, border);
        FortivaThemeResources.MergeOnto(border, theme);
        border.RequestedTheme = theme;
        content.RequestedTheme = theme;
        border.Background = theme == ElementTheme.Light
            ? GetBrush("FortivaPreviewGradientBrush", theme, border)
            : GetBrush("FortivaAccentGlowBrush", theme, border);
        border.BorderBrush = GetBrush("FortivaGlassBorderBrush", theme, border);
        border.BorderThickness = new Thickness(1);
        content.Foreground = GetBrush("FortivaHeadingBrush", theme, border);
    }

    public static void ApplySectionLabel(TextBlock label, bool muted = false, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(label.XamlRoot, label);
        label.RequestedTheme = theme;
        label.Foreground = muted
            ? GetBrush("FortivaMutedBrush", theme, label)
            : GetBrush("FortivaHeadingBrush", theme, label);
    }

    public static void ApplyBodyText(TextBlock text, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(text.XamlRoot, text);
        text.RequestedTheme = theme;
        text.Foreground = GetBrush("FortivaBodyBrush", theme, text);
    }

    public static void ApplyMutedText(TextBlock text, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(text.XamlRoot, text);
        text.RequestedTheme = theme;
        text.Foreground = GetBrush("FortivaMutedBrush", theme, text);
    }

    public static void ApplyToggleSwitch(ToggleSwitch toggle, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(toggle.XamlRoot, toggle);
        FortivaThemeResources.MergeOnto(toggle, theme);
        toggle.RequestedTheme = theme;
        toggle.Foreground = GetBrush("FortivaBodyBrush", theme, toggle);
    }

    public static void ApplySlider(Slider slider, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(slider.XamlRoot, slider);
        FortivaThemeResources.MergeOnto(slider, theme);
        slider.RequestedTheme = theme;
        slider.Foreground = GetBrush("FortivaAccentBrush", theme, slider);
    }

    public static void TryApplyStyle(FrameworkElement element, string styleKey)
    {
        if (Application.Current?.Resources.TryGetValue(styleKey, out var value) == true && value is Style style)
            element.Style = style;
    }
}
