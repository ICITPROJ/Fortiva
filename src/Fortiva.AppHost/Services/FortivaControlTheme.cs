using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

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

    /// <summary>Theme for modal UI — match the window/page that opened the dialog.</summary>
    public static ElementTheme ResolveDialogTheme(XamlRoot xamlRoot, FrameworkElement? themeHost = null)
    {
        if (themeHost is not null)
            return ResolveHostTheme(themeHost);

        if (xamlRoot.Content is FrameworkElement rootContent)
            return ResolveEffectiveTheme(xamlRoot, rootContent);

        return ThemeService.CurrentElementTheme;
    }

    /// <summary>Theme from a known host page (most reliable for embedded panels).</summary>
    public static ElementTheme ResolveHostTheme(FrameworkElement host)
    {
        if (host.RequestedTheme is ElementTheme.Light or ElementTheme.Dark)
            return host.RequestedTheme;

        var preference = ShellViewModel.Current.ThemePreference;
        if (preference != AppThemePreference.System)
            return ThemeService.ToElementTheme(preference);

        if (host.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            return host.ActualTheme;

        return ThemeService.CurrentElementTheme;
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

    public static (Brush Ring, Brush Banner) GetHealthScoreBrushes(int score, bool hasCritical, FrameworkElement? context = null)
    {
        var key = score switch
        {
            >= 90 when !hasCritical => "success",
            >= 75 => "success",
            >= 55 => "warning",
            >= 35 => "warning",
            _ => "critical"
        };
        return (GetAuditSeverityBrush(key, context), GetAuditSeverityBgBrush(key, context));
    }

    public static string GetAuditSeverityLabel(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => "CRITICAL",
        AuditSeverity.Warning => "WARNING",
        AuditSeverity.Info => "INFO",
        _ => "PASS"
    };

    public static string GetAuditSeverityKey(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => "critical",
        AuditSeverity.Warning => "warning",
        AuditSeverity.Info => "info",
        _ => "success"
    };

    public static (Brush BadgeBg, Brush BadgeFg) GetPasswordIssueBadgeBrushes(string issueLabel, FrameworkElement? context = null)
    {
        var key = issueLabel switch
        {
            "Weak" => "critical",
            "Reused" or "1y+ old" => "warning",
            _ => "info"
        };
        if (issueLabel is not ("Weak" or "Reused" or "1y+ old"))
            return (GetBrush("FortivaTrackSubtleBrush", context: context), GetBrush("FortivaMutedBrush", context: context));

        return (GetAuditSeverityBgBrush(key, context), GetAuditSeverityBrush(key, context));
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
        void Apply()
        {
            var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
            FortivaThemeResources.MergeOnto(box, resolved);
            box.RequestedTheme = resolved;
            TryApplyStyle(box, "FortivaTextBox");
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

            PinSharedInputResources(box, resolved, context ?? box);
            ForceInputInnerChrome(box, resolved, context);
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    public static void ApplyComboBox(ComboBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
            FortivaThemeResources.MergeOnto(box, resolved);
            box.RequestedTheme = resolved;
            TryApplyStyle(box, "FortivaComboBox");
            if (Application.Current?.Resources.TryGetValue("FortivaComboBoxItem", out var itemStyle) == true
                && itemStyle is Style comboItemStyle)
                box.ItemContainerStyle = comboItemStyle;

            box.Background = GetBrush("FortivaInputFillBrush", resolved, box);
            box.BorderBrush = GetBrush("FortivaInputBorderBrush", resolved, box);
            box.Foreground = GetBrush("FortivaHeadingBrush", resolved, box);
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(8);
            box.Padding = new Thickness(12, 8, 12, 8);
            box.MinHeight = 44;
            box.FontSize = 14;

            PinComboBoxResources(box, resolved, context ?? box);
            EnsureComboBoxDropDownHook(box, context);
            ForceInputInnerChrome(box, resolved, context);

            for (var i = 0; i < box.Items.Count; i++)
            {
                if (box.ContainerFromIndex(i) is ComboBoxItem item)
                    ApplyComboBoxItem(item, resolved, context);
            }
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    /// <summary>Walk a code-built subtree and pin RequestedTheme + merged dictionary on every element.</summary>
    public static void ApplyThemeRecursively(FrameworkElement root, ElementTheme theme)
    {
        root.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(root, theme);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is FrameworkElement child)
                ApplyThemeRecursively(child, theme);
        }
    }

    public static void ApplyPasswordBox(PasswordBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
        FortivaThemeResources.MergeOnto(box, resolved);
        box.RequestedTheme = resolved;
        box.Background = GetBrush("FortivaInputFillBrush", resolved, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", resolved, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", resolved, box);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(8);
        box.Padding = new Thickness(14, 12, 14, 12);
        if (box.MinHeight < 44)
            box.MinHeight = 44;

        PinResource(box, "TextControlBackground", GetBrush("TextControlBackground", resolved, box));
        PinResource(box, "TextControlBackgroundFocused", GetBrush("TextControlBackgroundFocused", resolved, box));
        PinResource(box, "TextControlBackgroundPointerOver", GetBrush("TextControlBackgroundPointerOver", resolved, box));
        PinResource(box, "TextControlForeground", GetBrush("TextControlForeground", resolved, box));
        PinResource(box, "TextControlBorderBrush", GetBrush("TextControlBorderBrush", resolved, box));
    }

    public static void ApplyAutoSuggestBox(AutoSuggestBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? box);
        FortivaThemeResources.MergeOnto(box, resolved);
        box.RequestedTheme = resolved;
        box.Background = GetBrush("FortivaInputFillBrush", resolved, box);
        box.BorderBrush = GetBrush("FortivaInputBorderBrush", resolved, box);
        box.Foreground = GetBrush("FortivaHeadingBrush", resolved, box);
        PinResource(box, "TextControlBackground", GetBrush("TextControlBackground", resolved, box));
        PinResource(box, "TextControlBackgroundFocused", GetBrush("TextControlBackgroundFocused", resolved, box));
        PinResource(box, "TextControlForeground", GetBrush("TextControlForeground", resolved, box));
        PinResource(box, "TextControlBorderBrush", GetBrush("TextControlBorderBrush", resolved, box));
        PinResource(box, "TextControlPlaceholderForeground", GetBrush("TextControlPlaceholderForeground", resolved, box));
    }

    public static void ApplyReadOnlyPasswordTextBox(TextBox box, FrameworkElement? context = null)
    {
        ApplyTextBox(box, context);
        box.IsReadOnly = true;
        box.FontFamily = new FontFamily("Consolas");
    }

    public static void ApplySecondaryButton(Button button, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? button);
        FortivaThemeResources.MergeOnto(button, resolved);
        button.RequestedTheme = resolved;
        TryApplyStyle(button, "FortivaSecondaryButton");
        PinButtonResources(button, resolved);
    }

    public static void ApplyPreviewSurface(Border border, TextBlock content, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? border);
        FortivaThemeResources.MergeOnto(border, resolved);
        border.RequestedTheme = resolved;
        content.RequestedTheme = resolved;
        border.Background = GetBrush("FortivaPreviewGradientBrush", resolved, border);
        border.BorderBrush = GetBrush("FortivaGlassBorderBrush", resolved, border);
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = new CornerRadius(12);
        content.Foreground = GetBrush("FortivaHeadingBrush", resolved, border);
        FortivaSurfaceEffects.ApplyCardElevation(border, 4f);
    }

    public static void ApplyAccentButton(Button button, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? button);
        FortivaThemeResources.MergeOnto(button, resolved);
        button.RequestedTheme = resolved;
        TryApplyStyle(button, "FortivaAccentButton");
        PinButtonResources(button, resolved);
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

    public static void ApplyToggleSwitch(ToggleSwitch toggle, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? toggle);
        FortivaThemeResources.MergeOnto(toggle, resolved);
        toggle.RequestedTheme = resolved;
        toggle.Foreground = GetBrush("FortivaBodyBrush", resolved, toggle);
        PinResource(toggle, "ToggleSwitchFillOn", GetBrush("ToggleSwitchFillOn", resolved, toggle));
        PinResource(toggle, "ToggleSwitchFillOnPointerOver", GetBrush("ToggleSwitchFillOnPointerOver", resolved, toggle));
        PinResource(toggle, "ToggleSwitchFillOff", GetBrush("ToggleSwitchFillOff", resolved, toggle));
        PinResource(toggle, "ToggleSwitchFillOffPointerOver", GetBrush("ToggleSwitchFillOffPointerOver", resolved, toggle));
        PinResource(toggle, "ToggleSwitchKnobFillOn", GetBrush("ToggleSwitchKnobFillOn", resolved, toggle));
        PinResource(toggle, "ToggleSwitchKnobFillOff", GetBrush("ToggleSwitchKnobFillOff", resolved, toggle));
        PinResource(toggle, "ToggleSwitchStrokeOn", GetBrush("ToggleSwitchStrokeOn", resolved, toggle));
        PinResource(toggle, "ToggleSwitchStrokeOff", GetBrush("ToggleSwitchStrokeOff", resolved, toggle));
    }

    public static void ApplySlider(Slider slider, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? slider);
            FortivaThemeResources.MergeOnto(slider, resolved);
            slider.RequestedTheme = resolved;
            TryApplyStyle(slider, "FortivaSlider");
            slider.Foreground = GetBrush("FortivaAccentBrush", resolved, slider);
            PinResource(slider, "SliderTrackFill", GetBrush("SliderTrackFill", resolved, slider));
            PinResource(slider, "SliderTrackFillPointerOver", GetBrush("SliderTrackFillPointerOver", resolved, slider));
            PinResource(slider, "SliderTrackFillPressed", GetBrush("SliderTrackFillPressed", resolved, slider));
            PinResource(slider, "SliderTrackFillDisabled", GetBrush("SliderTrackFillDisabled", resolved, slider));
            PinResource(slider, "SliderTrackValueFill", GetBrush("SliderTrackValueFill", resolved, slider));
            PinResource(slider, "SliderTrackValueFillPointerOver", GetBrush("SliderTrackValueFillPointerOver", resolved, slider));
            PinResource(slider, "SliderTrackValueFillPressed", GetBrush("SliderTrackValueFillPressed", resolved, slider));
            PinResource(slider, "SliderTrackValueFillDisabled", GetBrush("SliderTrackValueFillDisabled", resolved, slider));
            PinResource(slider, "SliderThumbFill", GetBrush("SliderThumbFill", resolved, slider));
            PinResource(slider, "ControlStrongFillColorDefaultBrush", GetBrush("ControlStrongFillColorDefaultBrush", resolved, slider));
        }

        Apply();
        ApplyWhenLoaded(slider, Apply);
    }

    public static void ApplyFontIcon(FontIcon icon, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveEffectiveTheme(context?.XamlRoot, context ?? icon);
        icon.RequestedTheme = resolved;
        icon.Foreground = GetBrush("FortivaBodyBrush", resolved, icon);
    }

    public static Brush GetPasswordStrengthBrush(PasswordStrength strength, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context);
        var key = strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak => "FortivaSemanticErrorBrush",
            PasswordStrength.Fair => "FortivaSemanticWarningBrush",
            PasswordStrength.Strong or PasswordStrength.VeryStrong => "FortivaSemanticSuccessBrush",
            _ => "FortivaAccentBrush"
        };
        return GetBrush(key, theme, context);
    }

    public static Brush GetAuditSeverityBrush(string severityKey, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context);
        var key = severityKey switch
        {
            "critical" => "FortivaSemanticErrorBrush",
            "warning" => "FortivaSemanticWarningBrush",
            "info" => "FortivaSemanticInfoBrush",
            _ => "FortivaSemanticSuccessBrush"
        };
        return GetBrush(key, theme, context);
    }

    public static Brush GetAuditSeverityBgBrush(string severityKey, FrameworkElement? context = null)
    {
        var theme = ResolveEffectiveTheme(context?.XamlRoot, context);
        var key = severityKey switch
        {
            "critical" => "FortivaSemanticErrorBgBrush",
            "warning" => "FortivaSemanticWarningBgBrush",
            "info" => "FortivaSemanticInfoBgBrush",
            _ => "FortivaSemanticSuccessBgBrush"
        };
        return GetBrush(key, theme, context);
    }

    public static void TryApplyStyle(FrameworkElement element, string styleKey)
    {
        if (Application.Current?.Resources.TryGetValue(styleKey, out var value) == true && value is Style style)
            element.Style = style;
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

    private static readonly ConditionalWeakTable<ComboBox, object> ComboBoxDropDownHooks = new();

    private static void EnsureComboBoxDropDownHook(ComboBox box, FrameworkElement? context)
    {
        if (ComboBoxDropDownHooks.TryGetValue(box, out _))
            return;

        ComboBoxDropDownHooks.Add(box, box);
        box.DropDownOpened += (_, _) =>
        {
            var theme = context is not null
                ? ResolveHostTheme(context)
                : ResolveEffectiveTheme(box.XamlRoot, box);
            ApplyComboBoxDropDown(box, theme, context);
        };
        box.DropDownClosed += (_, _) =>
        {
            var theme = context is not null
                ? ResolveHostTheme(context)
                : ResolveEffectiveTheme(box.XamlRoot, box);
            ForceInputInnerChrome(box, theme, context);
            PinComboBoxResources(box, theme, context ?? box);
        };
    }

    private static void ApplyComboBoxDropDown(ComboBox box, ElementTheme theme, FrameworkElement? context)
    {
        box.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(box, theme);
        PinComboBoxResources(box, theme, context ?? box);
        ForceInputInnerChrome(box, theme, context);

        ThemeComboBoxOpenPopups(box, theme, context);

        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.ContainerFromIndex(i) is ComboBoxItem item)
                ApplyComboBoxItem(item, theme, context);
        }

        ScheduleComboBoxDropDownRetheme(box, theme, context, retriesRemaining: 4);
    }

    private static void ScheduleComboBoxDropDownRetheme(ComboBox box, ElementTheme theme, FrameworkElement? context, int retriesRemaining)
    {
        if (retriesRemaining <= 0 || !box.IsDropDownOpen)
            return;

        box.DispatcherQueue.TryEnqueue(() =>
        {
            if (!box.IsDropDownOpen)
                return;

            ThemeComboBoxOpenPopups(box, theme, context);
            ForceInputInnerChrome(box, theme, context);

            for (var i = 0; i < box.Items.Count; i++)
            {
                if (box.ContainerFromIndex(i) is ComboBoxItem item)
                    ApplyComboBoxItem(item, theme, context);
            }

            ScheduleComboBoxDropDownRetheme(box, theme, context, retriesRemaining - 1);
        });
    }

    private static void ThemeComboBoxOpenPopups(ComboBox box, ElementTheme theme, FrameworkElement? context)
    {
        if (box.XamlRoot is null)
            return;

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot))
        {
            if (popup.Child is not FrameworkElement popupRoot)
                continue;

            if (!PopupBelongsToComboBox(popupRoot, box))
                continue;

            ThemePopupRoot(popupRoot, theme, context ?? box);
        }

        if (FindDescendant<Popup>(box) is { Child: FrameworkElement inlinePopupRoot })
            ThemePopupRoot(inlinePopupRoot, theme, context ?? box);
    }

    private static bool PopupBelongsToComboBox(FrameworkElement popupRoot, ComboBox box) =>
        box.IsDropDownOpen && FindDescendant<ComboBoxItem>(popupRoot) is not null;

    private static void ThemePopupRoot(FrameworkElement popupRoot, ElementTheme theme, FrameworkElement context)
    {
        popupRoot.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(popupRoot, theme);
        PinComboBoxResources(popupRoot, theme, context);
        PinComboBoxItemResources(popupRoot, theme, context);
        ForceInputInnerChrome(popupRoot, theme, context);
    }

    private static void ApplyComboBoxItem(ComboBoxItem item, ElementTheme theme, FrameworkElement? context)
    {
        item.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(item, theme);
        item.Foreground = GetBrush("ComboBoxItemForeground", theme, context ?? item);
        item.Background = GetBrush("ComboBoxItemBackground", theme, context ?? item);
        PinComboBoxItemResources(item, theme, context ?? item);
    }

    private static void PinSharedInputResources(FrameworkElement element, ElementTheme theme, FrameworkElement context)
    {
        PinResource(element, "TextControlBackground", GetBrush("TextControlBackground", theme, context));
        PinResource(element, "TextControlBackgroundFocused", GetBrush("TextControlBackgroundFocused", theme, context));
        PinResource(element, "TextControlBackgroundPointerOver", GetBrush("TextControlBackgroundPointerOver", theme, context));
        PinResource(element, "TextControlForeground", GetBrush("TextControlForeground", theme, context));
        PinResource(element, "TextControlBorderBrush", GetBrush("TextControlBorderBrush", theme, context));
        PinResource(element, "TextControlPlaceholderForeground", GetBrush("TextControlPlaceholderForeground", theme, context));
        PinResource(element, "ControlFillColorDefaultBrush", GetBrush("ControlFillColorDefaultBrush", theme, context));
        PinResource(element, "ControlFillColorInputActiveBrush", GetBrush("ControlFillColorInputActiveBrush", theme, context));
        PinResource(element, "TextFillColorPrimaryBrush", GetBrush("TextFillColorPrimaryBrush", theme, context));
    }

    private static void PinComboBoxResources(FrameworkElement element, ElementTheme theme, FrameworkElement context)
    {
        PinSharedInputResources(element, theme, context);
        PinResource(element, "ComboBoxBackground", GetBrush("ComboBoxBackground", theme, context));
        PinResource(element, "ComboBoxBackgroundUnfocused", GetBrush("ComboBoxBackgroundUnfocused", theme, context));
        PinResource(element, "ComboBoxBackgroundFocused", GetBrush("ComboBoxBackgroundFocused", theme, context));
        PinResource(element, "ComboBoxBackgroundPointerOver", GetBrush("ComboBoxBackgroundPointerOver", theme, context));
        PinResource(element, "ComboBoxBackgroundPressed", GetBrush("ComboBoxBackgroundPressed", theme, context));
        PinResource(element, "ComboBoxForeground", GetBrush("ComboBoxForeground", theme, context));
        PinResource(element, "ComboBoxForegroundFocused", GetBrush("ComboBoxForegroundFocused", theme, context));
        PinResource(element, "ComboBoxBorderBrush", GetBrush("ComboBoxBorderBrush", theme, context));
        PinResource(element, "ComboBoxBorderBrushFocused", GetBrush("ComboBoxBorderBrushFocused", theme, context));
        PinResource(element, "ComboBoxDropDownBackground", GetBrush("ComboBoxDropDownBackground", theme, context));
        PinResource(element, "ComboBoxDropDownBorderBrush", GetBrush("ComboBoxDropDownBorderBrush", theme, context));
        PinResource(element, "ComboBoxDropDownGlyphForeground", GetBrush("ComboBoxDropDownGlyphForeground", theme, context));
        PinResource(element, "ComboBoxHeaderForeground", GetBrush("ComboBoxHeaderForeground", theme, context));
        PinResource(element, "ControlFillColorSecondaryBrush", GetBrush("ControlFillColorSecondaryBrush", theme, context));
        PinComboBoxItemResources(element, theme, context);
    }

    private static void PinComboBoxItemResources(FrameworkElement element, ElementTheme theme, FrameworkElement context)
    {
        PinResource(element, "ComboBoxItemForeground", GetBrush("ComboBoxItemForeground", theme, context));
        PinResource(element, "ComboBoxItemForegroundPointerOver", GetBrush("ComboBoxItemForegroundPointerOver", theme, context));
        PinResource(element, "ComboBoxItemForegroundSelected", GetBrush("ComboBoxItemForegroundSelected", theme, context));
        PinResource(element, "ComboBoxItemForegroundSelectedPointerOver", GetBrush("ComboBoxItemForegroundSelectedPointerOver", theme, context));
        PinResource(element, "ComboBoxItemBackground", GetBrush("ComboBoxItemBackground", theme, context));
        PinResource(element, "ComboBoxItemBackgroundPointerOver", GetBrush("ComboBoxItemBackgroundPointerOver", theme, context));
        PinResource(element, "ComboBoxItemBackgroundSelected", GetBrush("ComboBoxItemBackgroundSelected", theme, context));
        PinResource(element, "ComboBoxItemBackgroundSelectedPointerOver", GetBrush("ComboBoxItemBackgroundSelectedPointerOver", theme, context));
        PinResource(element, "SubtleFillColorTransparentBrush", GetBrush("SubtleFillColorTransparentBrush", theme, context));
        PinResource(element, "SubtleFillColorSecondaryBrush", GetBrush("SubtleFillColorSecondaryBrush", theme, context));
        PinResource(element, "SubtleFillColorTertiaryBrush", GetBrush("SubtleFillColorTertiaryBrush", theme, context));
    }

    private static void ForceInputInnerChrome(FrameworkElement control, ElementTheme theme, FrameworkElement? context)
    {
        var host = context ?? control;
        var bg = GetBrush("FortivaInputFillBrush", theme, host);
        var fg = GetBrush("FortivaHeadingBrush", theme, host);
        var border = GetBrush("FortivaInputBorderBrush", theme, host);
        var itemBg = GetBrush("ComboBoxItemBackground", theme, host);
        var itemFg = GetBrush("ComboBoxItemForeground", theme, host);
        var dropDownBg = GetBrush("ComboBoxDropDownBackground", theme, host);

        control.RequestedTheme = theme;

        switch (control)
        {
            case TextBox textBox:
                textBox.Background = bg;
                textBox.Foreground = fg;
                textBox.BorderBrush = border;
                break;
            case ComboBox comboBox:
                comboBox.Background = bg;
                comboBox.Foreground = fg;
                comboBox.BorderBrush = border;
                break;
        }

        foreach (var child in GetVisualDescendants(control))
        {
            if (child is FrameworkElement fe)
                fe.RequestedTheme = theme;

            switch (child)
            {
                case Border chrome:
                    chrome.Background = bg;
                    if (chrome.BorderThickness != default)
                        chrome.BorderBrush = border;
                    break;
                case ScrollViewer scroll:
                    scroll.Background = bg;
                    break;
                case ContentPresenter presenter:
                    presenter.Foreground = fg;
                    break;
                case ComboBoxItem item:
                    item.Background = itemBg;
                    item.Foreground = itemFg;
                    break;
                case ListViewBase list:
                    list.Background = dropDownBg;
                    break;
            }
        }
    }

    private static IEnumerable<DependencyObject> GetVisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in GetVisualDescendants(child))
                yield return nested;
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

    private static void PinResource(FrameworkElement element, string key, Brush brush) =>
        element.Resources[key] = brush;

    private static void ApplyWhenLoaded(FrameworkElement element, Action apply)
    {
        void Run()
        {
            apply();
            element.DispatcherQueue.TryEnqueue(() => apply());
        }

        Run();
        if (!element.IsLoaded)
            element.Loaded += (_, _) => Run();
    }

    private static void PinButtonResources(Button button, ElementTheme theme)
    {
        PinResource(button, "ButtonBackground", GetBrush("ButtonBackground", theme, button));
        PinResource(button, "ButtonBackgroundPointerOver", GetBrush("ButtonBackgroundPointerOver", theme, button));
        PinResource(button, "ButtonBackgroundPressed", GetBrush("ButtonBackgroundPressed", theme, button));
        PinResource(button, "ButtonForeground", GetBrush("ButtonForeground", theme, button));
        PinResource(button, "ButtonBorderBrush", GetBrush("ButtonBorderBrush", theme, button));
        PinResource(button, "ButtonBorderBrushPointerOver", GetBrush("ButtonBorderBrushPointerOver", theme, button));
    }
}
