using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Fortiva.AppHost.Services;

/// <summary>Applies Fortiva theme brushes to code-built controls (dialogs, panels).</summary>
public static class FortivaControlTheme
{
    /// <summary>
    /// Prefer the host element's resolved theme so code-built UI matches surrounding XAML.
    /// Dialog subtrees fall back to user preference (ActualTheme is unreliable there).
    /// </summary>
    public static ElementTheme ResolveEffectiveTheme(XamlRoot? xamlRoot = null, FrameworkElement? context = null)
    {
        if (IsInContentDialogSubtree(context))
            return ResolveAppTheme();

        // Code-built roots often pin the wrong RequestedTheme — prefer ancestors.
        if (TryGetVisualTreeTheme(context, out var fromHost, includeStart: false))
            return fromHost;

        if (TryGetVisualTreeTheme(context, out var fromContext, includeStart: true))
            return fromContext;

        if (xamlRoot?.Content is FrameworkElement rootContent
            && TryGetVisualTreeTheme(rootContent, out var fromRoot, includeStart: true))
            return fromRoot;

        return ThemeService.CurrentElementTheme;
    }

    /// <summary>Theme from a known host page (most reliable for embedded panels).</summary>
    public static ElementTheme ResolveHostTheme(FrameworkElement host)
    {
        if (host.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            return host.ActualTheme;

        return ResolveEffectiveTheme(host.XamlRoot, host);
    }

    public static ElementTheme ResolveAppTheme()
    {
        var preference = ShellViewModel.Current.ThemePreference;
        if (preference == AppThemePreference.Light)
            return ElementTheme.Light;
        if (preference == AppThemePreference.Dark)
            return ElementTheme.Dark;

        return ThemeService.ResolveSystemElementTheme();
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
            return CloneBrush(themedBrush);

        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return CloneBrush(brush);

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static Brush CloneBrush(Brush brush) =>
        brush switch
        {
            SolidColorBrush scb => new SolidColorBrush(scb.Color),
            _ => brush
        };

    /// <summary>Apply resolved theme + merged dictionary to a code-built subtree root.</summary>
    public static void ApplyResolvedTheme(FrameworkElement element)
    {
        var theme = ResolveEffectiveTheme(element.XamlRoot, element);
        element.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(element, theme);
    }

    public static void ApplyTextBox(TextBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
        FortivaThemeResources.MergeOnto(box, resolved);
        box.RequestedTheme = resolved;
        box.Background = GetBrush("FortivaInputFillBrush", resolved, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", resolved, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", resolved, box);
        box.PlaceholderForeground = GetBrush("FortivaMutedBrush", resolved, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(12, 10, 12, 10);
        if (box.MinHeight < 44)
            box.MinHeight = 44;
        if (box.FontSize < 14)
            box.FontSize = 14;

        PinResource(box, "TextControlBackground", GetBrush("TextControlBackground", resolved, box));
        PinResource(box, "TextControlBackgroundFocused", GetBrush("TextControlBackgroundFocused", resolved, box));
        PinResource(box, "TextControlBackgroundPointerOver", GetBrush("TextControlBackgroundPointerOver", resolved, box));
        PinResource(box, "TextControlForeground", GetBrush("TextControlForeground", resolved, box));
        PinResource(box, "TextControlBorderBrush", GetBrush("TextControlBorderBrush", resolved, box));
        PinResource(box, "TextControlPlaceholderForeground", GetBrush("TextControlPlaceholderForeground", resolved, box));
    }

    public static void ApplyComboBox(ComboBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
        FortivaThemeResources.MergeOnto(box, resolved);
        box.RequestedTheme = resolved;
        box.Background = GetBrush("FortivaInputFillBrush", resolved, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", resolved, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", resolved, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(12, 8, 12, 8);
        box.MinHeight = 44;
        box.FontSize = 14;

        // WinUI ComboBox template reads these keys — Background alone is not enough.
        PinResource(box, "ComboBoxBackground", GetBrush("ComboBoxBackground", resolved, box));
        PinResource(box, "ComboBoxBackgroundFocused", GetBrush("ComboBoxBackgroundFocused", resolved, box));
        PinResource(box, "ComboBoxBackgroundPointerOver", GetBrush("ComboBoxBackgroundPointerOver", resolved, box));
        PinResource(box, "ComboBoxBackgroundPressed", GetBrush("ComboBoxBackgroundPressed", resolved, box));
        PinResource(box, "ComboBoxForeground", GetBrush("ComboBoxForeground", resolved, box));
        PinResource(box, "ComboBoxForegroundFocused", GetBrush("ComboBoxForegroundFocused", resolved, box));
        PinResource(box, "ComboBoxBorderBrush", GetBrush("ComboBoxBorderBrush", resolved, box));
        PinResource(box, "ComboBoxBorderBrushFocused", GetBrush("ComboBoxBorderBrushFocused", resolved, box));
        PinResource(box, "ComboBoxDropDownBackground", GetBrush("ComboBoxDropDownBackground", resolved, box));
        PinResource(box, "ComboBoxDropDownGlyphForeground", GetBrush("ComboBoxDropDownGlyphForeground", resolved, box));
    }

    public static void ApplyPasswordBox(PasswordBox box, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
        FortivaThemeResources.MergeOnto(box, theme);
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

    public static void ApplyReadOnlyPasswordTextBox(TextBox box, FrameworkElement? context = null)
    {
        ApplyTextBox(box, context);
        box.IsReadOnly = true;
        box.FontFamily = new FontFamily("Consolas");
    }

    public static void ApplySecondaryButton(Button button, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? button);
        FortivaThemeResources.MergeOnto(button, theme);
        button.RequestedTheme = theme;
        TryApplyStyle(button, "FortivaSecondaryButton");
    }

    public static void ApplyPreviewSurface(Border border, TextBlock content, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? border);
        FortivaThemeResources.MergeOnto(border, theme);
        border.RequestedTheme = theme;
        content.RequestedTheme = theme;
        border.Background = GetBrush("FortivaPreviewGradientBrush", theme, border);
        border.BorderBrush = GetBrush("FortivaGlassBorderBrush", theme, border);
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = new CornerRadius(12);
        content.Foreground = GetBrush("FortivaHeadingBrush", theme, border);
        FortivaSurfaceEffects.ApplyCardElevation(border, 4f);
    }

    public static void ApplyAccentButton(Button button, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? button);
        FortivaThemeResources.MergeOnto(button, theme);
        button.RequestedTheme = theme;
        TryApplyStyle(button, "FortivaAccentButton");
    }

    public static void ApplySectionLabel(TextBlock label, bool muted = false, bool pageHeader = false, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? label);
        label.RequestedTheme = theme;
        if (pageHeader)
            label.FontSize = 16;
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        label.Foreground = muted
            ? GetBrush("FortivaMutedBrush", theme, label)
            : GetBrush("FortivaHeadingBrush", theme, label);
    }

    public static void ApplyBodyText(TextBlock text, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? text);
        text.RequestedTheme = theme;
        text.Foreground = GetBrush("FortivaBodyBrush", theme, text);
    }

    public static void ApplyMutedText(TextBlock text, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? text);
        text.RequestedTheme = theme;
        text.Foreground = GetBrush("FortivaMutedBrush", theme, text);
    }

    public static void ApplyToggleSwitch(ToggleSwitch toggle, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context ?? toggle);
        FortivaThemeResources.MergeOnto(toggle, theme);
        toggle.RequestedTheme = theme;
        toggle.Foreground = GetBrush("FortivaBodyBrush", theme, toggle);
    }

    public static void ApplySlider(Slider slider, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? slider);
        FortivaThemeResources.MergeOnto(slider, resolved);
        slider.RequestedTheme = resolved;
        slider.Foreground = GetBrush("FortivaAccentBrush", resolved, slider);
        PinResource(slider, "SliderTrackFill", GetBrush("SliderTrackFill", resolved, slider));
        PinResource(slider, "SliderThumbFill", GetBrush("SliderThumbFill", resolved, slider));
    }

    public static void TryApplyStyle(FrameworkElement element, string styleKey)
    {
        if (Application.Current?.Resources.TryGetValue(styleKey, out var value) == true && value is Style style)
            element.Style = style;
    }

    private static bool IsInContentDialogSubtree(FrameworkElement? element)
    {
        for (var el = element; el is not null; el = VisualTreeHelper.GetParent(el) as FrameworkElement)
        {
            if (el is ContentDialog)
                return true;
        }

        return false;
    }

    private static bool TryGetVisualTreeTheme(FrameworkElement? start, out ElementTheme theme, bool includeStart = true)
    {
        var el = includeStart ? start : VisualTreeHelper.GetParent(start) as FrameworkElement;
        for (; el is not null; el = VisualTreeHelper.GetParent(el) as FrameworkElement)
        {
            if (el.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            {
                theme = el.ActualTheme;
                return true;
            }
        }

        theme = default;
        return false;
    }

    private static void PinResource(FrameworkElement element, string key, Brush brush) =>
        element.Resources[key] = brush;
}
