using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Security;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace Fortiva.AppHost.Services;

/// <summary>Applies Fortiva theme brushes to code-built controls (dialogs, panels).</summary>
public static class FortivaControlTheme
{
    private sealed record InputBrushSet(
        Brush Background,
        Brush BackgroundHover,
        Brush Foreground,
        Brush Border,
        Brush BorderHover,
        Brush BorderFocused,
        Brush Placeholder,
        Brush DropDownBackground,
        Brush ItemBackground,
        Brush ItemBackgroundHover);

    private static readonly SolidColorBrush LightInputBackground = new(Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush LightInputBackgroundHover = new(Color.FromArgb(255, 245, 250, 254));
    private static readonly SolidColorBrush LightInputForeground = new(Color.FromArgb(255, 10, 12, 16));
    private static readonly SolidColorBrush LightInputBorder = new(Color.FromArgb(255, 142, 180, 204));
    private static readonly SolidColorBrush LightInputBorderHover = new(Color.FromArgb(255, 64, 188, 244));
    private static readonly SolidColorBrush LightInputBorderFocused = new(Color.FromArgb(255, 64, 188, 244));
    private static readonly SolidColorBrush LightInputPlaceholder = new(Color.FromArgb(255, 92, 106, 120));
    private static readonly SolidColorBrush LightItemBackgroundHover = new(Color.FromArgb(255, 234, 246, 252));

    private static readonly ConditionalWeakTable<TextBox, object> TextBoxThemeHooks = new();
    private static readonly ConditionalWeakTable<ComboBox, object> ComboBoxThemeHooks = new();
    /// <summary>
    /// Prefer the host element's resolved theme so code-built UI matches surrounding XAML.
    /// Dialog subtrees fall back to user preference (ActualTheme is unreliable there).
    /// </summary>
    public static ElementTheme ResolveEffectiveTheme(XamlRoot? xamlRoot = null, FrameworkElement? context = null)
    {
        var preference = ShellViewModel.Current.ThemePreference;
        if (preference == AppThemePreference.Light)
            return ElementTheme.Light;
        if (preference == AppThemePreference.Dark)
            return ElementTheme.Dark;

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
        var preference = ShellViewModel.Current.ThemePreference;
        if (preference == AppThemePreference.Light)
            return ElementTheme.Light;
        if (preference == AppThemePreference.Dark)
            return ElementTheme.Dark;

        if (host.RequestedTheme is ElementTheme.Light or ElementTheme.Dark)
            return host.RequestedTheme;

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

    /// <summary>High-contrast text on tinted audit finding cards (matches app theme, not host ActualTheme).</summary>
    public static (Brush Title, Brush Detail, Brush BadgeBg, Brush BadgeFg) GetAuditCardTextBrushes(
        string severityKey,
        FrameworkElement? context = null,
        ElementTheme? theme = null)
    {
        theme ??= ResolveAppTheme();
        var accent = GetAuditSeverityBrush(severityKey, context, theme);
        return (
            accent,
            GetBrush("FortivaBodyBrush", theme, context),
            accent,
            GetBrush("FortivaAccentButtonForegroundBrush", theme, context));
    }

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
            var resolved = theme ?? ResolveInputTheme(context, box);
            ApplyInputThemeCore(box, resolved, context, "FortivaTextBox");
            EnsureTextBoxThemeHook(box, context);
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    public static void ApplyComboBox(ComboBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveInputTheme(context, box);
            ApplyInputThemeCore(box, resolved, context, "FortivaComboBox");
            if (Application.Current?.Resources.TryGetValue("FortivaComboBoxItem", out var itemStyle) == true
                && itemStyle is Style comboItemStyle)
                box.ItemContainerStyle = comboItemStyle;

            EnsureComboBoxDropDownHook(box, context);
            EnsureComboBoxThemeHook(box, context);

            for (var i = 0; i < box.Items.Count; i++)
            {
                if (box.ContainerFromIndex(i) is ComboBoxItem item)
                    ApplyComboBoxItem(item, resolved, context);
            }
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    public static void ApplyExpander(Expander expander, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveInputTheme(context, expander);
            var host = context ?? expander;
            var brushes = GetInputBrushes(resolved, host);

            expander.RequestedTheme = resolved;
            FortivaThemeResources.MergeOnto(expander, resolved);
            expander.Style = null;
            TryApplyStyle(expander, "FortivaExpander");
            expander.Background = brushes.Background;
            expander.Foreground = brushes.Foreground;
            expander.BorderBrush = brushes.Border;
            expander.BorderThickness = new Thickness(1);
            expander.CornerRadius = new CornerRadius(8);

            PinExpanderResources(expander, brushes);
            ForceExpanderInnerChrome(expander, resolved, brushes);
        }

        Apply();
        ApplyWhenLoaded(expander, Apply);
    }

    private static void PinExpanderResources(FrameworkElement element, InputBrushSet brushes)
    {
        PinResource(element, "ExpanderBackground", brushes.Background);
        PinResource(element, "ExpanderHeaderBackground", brushes.Background);
        PinResource(element, "ExpanderHeaderForeground", brushes.Foreground);
        PinResource(element, "ExpanderHeaderForegroundPointerOver", brushes.Foreground);
        PinResource(element, "ExpanderHeaderForegroundPressed", brushes.Foreground);
        PinResource(element, "ExpanderHeaderBorderBrush", brushes.Border);
        PinResource(element, "ExpanderHeaderBorderPointerOverBrush", brushes.BorderHover);
        PinResource(element, "ExpanderHeaderBorderPressedBrush", brushes.BorderFocused);
        PinResource(element, "CardBackgroundFillColorDefaultBrush", brushes.Background);
        PinResource(element, "CardStrokeColorDefaultBrush", brushes.Border);
        PinResource(element, "TextFillColorPrimaryBrush", brushes.Foreground);
    }

    private static void ForceExpanderInnerChrome(Expander expander, ElementTheme theme, InputBrushSet brushes)
    {
        expander.RequestedTheme = theme;

        if (FindDescendant<ToggleButton>(expander) is ToggleButton { Name: "ExpanderHeader" } header)
        {
            header.RequestedTheme = theme;
            header.Background = brushes.Background;
            header.Foreground = brushes.Foreground;
            header.BorderBrush = brushes.Border;

            foreach (var child in GetVisualDescendants(header))
            {
                if (child is FrameworkElement fe)
                    fe.RequestedTheme = theme;

                switch (child)
                {
                    case TextBlock text:
                        text.Foreground = brushes.Foreground;
                        break;
                    case ContentPresenter presenter:
                        presenter.Foreground = brushes.Foreground;
                        break;
                }
            }
        }

        if (expander.Header is StackPanel headerPanel)
        {
            headerPanel.RequestedTheme = theme;
            foreach (var item in headerPanel.Children.OfType<TextBlock>())
                item.Foreground = brushes.Foreground;
        }
    }

    private static ElementTheme ResolveInputTheme(FrameworkElement? context, FrameworkElement element)
    {
        if (context is not null)
            return ResolveHostTheme(context);

        return ResolveEffectiveTheme(element.XamlRoot, element);
    }

    private static InputBrushSet GetInputBrushes(ElementTheme theme, FrameworkElement context)
    {
        if (theme == ElementTheme.Light)
        {
            return new InputBrushSet(
                LightInputBackground,
                LightInputBackgroundHover,
                LightInputForeground,
                LightInputBorder,
                LightInputBorderHover,
                LightInputBorderFocused,
                LightInputPlaceholder,
                LightInputBackground,
                LightInputBackground,
                LightItemBackgroundHover);
        }

        return new InputBrushSet(
            GetBrush("FortivaInputFillBrush", theme, context),
            GetBrush("TextControlBackgroundPointerOver", theme, context),
            GetBrush("FortivaHeadingBrush", theme, context),
            GetBrush("FortivaInputBorderBrush", theme, context),
            GetBrush("TextControlBorderBrushPointerOver", theme, context),
            GetBrush("TextControlBorderBrushFocused", theme, context),
            GetBrush("FortivaMutedBrush", theme, context),
            GetBrush("ComboBoxDropDownBackground", theme, context),
            GetBrush("ComboBoxItemBackground", theme, context),
            GetBrush("ComboBoxItemBackgroundPointerOver", theme, context));
    }

    private static void ApplyInputThemeCore(Control control, ElementTheme resolved, FrameworkElement? context, string styleKey)
    {
        var host = context ?? control;
        control.RequestedTheme = resolved;
        FortivaThemeResources.MergeOnto(control, resolved);

        // WinUI caches the first template against Application theme — reset so Light sticks.
        control.Style = null;
        TryApplyStyle(control, styleKey);

        var brushes = GetInputBrushes(resolved, host);
        control.Background = brushes.Background;
        control.Foreground = brushes.Foreground;
        control.BorderBrush = brushes.Border;
        control.BorderThickness = new Thickness(1);

        if (control is TextBox textBox)
        {
            textBox.PlaceholderForeground = brushes.Placeholder;
            textBox.CornerRadius = new CornerRadius(8);
            textBox.Padding = new Thickness(12, 10, 12, 10);
            if (textBox.MinHeight < 44)
                textBox.MinHeight = 44;
            if (textBox.FontSize < 14)
                textBox.FontSize = 14;
        }
        else if (control is ComboBox comboBox)
        {
            comboBox.CornerRadius = new CornerRadius(8);
            comboBox.Padding = new Thickness(12, 8, 12, 8);
            comboBox.MinHeight = 44;
            comboBox.FontSize = 14;
        }
        else if (control is AutoSuggestBox suggestBox)
        {
            suggestBox.CornerRadius = new CornerRadius(10);
            suggestBox.Padding = new Thickness(4, 0, 4, 0);
            suggestBox.MinHeight = 44;
            suggestBox.FontSize = 14;
        }

        PinAllInputResources(control, resolved, host, brushes);
        ForceInputInnerChrome(control, resolved, context, brushes);
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
        void Apply()
        {
            var resolved = theme ?? ResolveInputTheme(context, box);
            var host = context ?? box;
            var brushes = GetInputBrushes(resolved, host);

            FortivaThemeResources.MergeOnto(box, resolved);
            box.RequestedTheme = resolved;
            box.Style = null;
            TryApplyStyle(box, "FortivaPasswordBox");
            box.Background = brushes.Background;
            box.BorderBrush = brushes.Border;
            box.Foreground = brushes.Foreground;
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(8);
            box.Padding = new Thickness(14, 12, 14, 12);
            if (box.MinHeight < 44)
                box.MinHeight = 44;

            PinAllInputResources(box, resolved, host, brushes);
            ForceInputInnerChrome(box, resolved, context, brushes);
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    public static void ApplyAutoSuggestBox(AutoSuggestBox box, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveInputTheme(context, box);
            ApplyInputThemeCore(box, resolved, context, "FortivaSearchBox");
        }

        Apply();
        ApplyWhenLoaded(box, Apply);
    }

    public static void ApplyCategoryChip(ToggleButton toggle, bool selected, FrameworkElement context, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveInputTheme(context, toggle);
        var brushes = GetInputBrushes(resolved, context);
        var chipRadius = new CornerRadius(8);

        void Apply()
        {
            TryApplyStyle(toggle, "FortivaCategoryChip");
            toggle.RequestedTheme = resolved;
            FortivaThemeResources.MergeOnto(toggle, resolved);
            toggle.CornerRadius = chipRadius;
            toggle.Padding = new Thickness(10, 6, 10, 6);
            toggle.BorderThickness = new Thickness(1);
            toggle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

            var background = selected
                ? GetBrush("FortivaAccentGlowBrush", resolved, context)
                : brushes.Background;
            var border = selected
                ? GetBrush("FortivaAccentBrush", resolved, context)
                : brushes.Border;

            toggle.Background = background;
            toggle.BorderBrush = border;
            toggle.Foreground = brushes.Foreground;
            toggle.Resources["ControlCornerRadius"] = chipRadius;
            ForceCategoryChipInnerChrome(toggle, background, border, brushes.Foreground, resolved);
        }

        Apply();
        toggle.Loaded += (_, _) => Apply();
    }

    private static void ForceCategoryChipInnerChrome(
        ToggleButton toggle,
        Brush background,
        Brush border,
        Brush foreground,
        ElementTheme theme)
    {
        foreach (var child in GetVisualDescendants(toggle))
        {
            if (child is FrameworkElement fe)
                fe.RequestedTheme = theme;

            switch (child)
            {
                case Border chrome:
                    chrome.CornerRadius = new CornerRadius(8);
                    chrome.Background = background;
                    chrome.BorderBrush = chrome.BorderThickness != new Thickness(0) ? border : null;
                    break;
                case ContentPresenter presenter:
                    presenter.Foreground = foreground;
                    break;
            }
        }
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

    /// <summary>Compact button aligned with text fields — used beside inputs in dialogs.</summary>
    public static void ApplyInlineFieldButton(Button button, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveInputTheme(context, button);
        var host = context ?? button;
        var brushes = GetInputBrushes(resolved, host);

        button.RequestedTheme = resolved;
        FortivaThemeResources.MergeOnto(button, resolved);
        button.Style = null;
        button.CornerRadius = new CornerRadius(8);
        button.Padding = new Thickness(12, 8, 12, 8);
        button.MinHeight = 44;
        button.Background = brushes.Background;
        button.Foreground = brushes.Foreground;
        button.BorderBrush = brushes.Border;
        button.BorderThickness = new Thickness(1);
        button.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
    }

    /// <summary>Input-style bordered container for chip rows and similar controls.</summary>
    public static void ApplyInputContainer(Border border, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        var resolved = theme ?? ResolveInputTheme(context, border);
        var host = context ?? border;
        var brushes = GetInputBrushes(resolved, host);

        border.RequestedTheme = resolved;
        FortivaThemeResources.MergeOnto(border, resolved);
        border.Background = brushes.Background;
        border.BorderBrush = brushes.Border;
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = new CornerRadius(8);
        border.Padding = new Thickness(8, 8, 8, 8);
        border.MinHeight = 44;
    }

    public static void ApplyPreviewSurface(Border border, TextBlock content, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        void Apply()
        {
            var resolved = theme ?? ResolveInputTheme(context, border);
            var host = context ?? border;
            var brushes = GetInputBrushes(resolved, host);

            border.RequestedTheme = resolved;
            FortivaThemeResources.MergeOnto(border, resolved);
            content.RequestedTheme = resolved;

            if (resolved == ElementTheme.Light)
            {
                border.Background = brushes.Background;
                border.BorderBrush = brushes.Border;
            }
            else
            {
                border.Background = GetBrush("FortivaPreviewGradientBrush", resolved, border);
                border.BorderBrush = GetBrush("FortivaGlassBorderBrush", resolved, border);
            }

            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(12);
            content.Foreground = brushes.Foreground;

            ApplyPreviewSurfaceContents(border, resolved, host, brushes);
            FortivaSurfaceEffects.ApplyCardElevation(border, 4f);
        }

        Apply();
        ApplyWhenLoaded(border, Apply);
    }

    private static void ApplyPreviewSurfaceContents(
        Border border,
        ElementTheme theme,
        FrameworkElement host,
        InputBrushSet brushes)
    {
        foreach (var child in GetVisualDescendants(border))
        {
            if (child is FrameworkElement fe)
                fe.RequestedTheme = theme;

            switch (child)
            {
                case TextBlock text:
                    text.Foreground = text.FontSize <= 11
                        ? GetBrush("FortivaMutedBrush", theme, host)
                        : brushes.Foreground;
                    break;
                case FontIcon icon:
                    icon.Foreground = GetBrush("FortivaMutedBrush", theme, host);
                    break;
            }
        }
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

    public static Brush GetAuditSeverityBrush(string severityKey, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        theme ??= ResolveEffectiveTheme(context?.XamlRoot, context);
        var key = severityKey switch
        {
            "critical" => "FortivaSemanticErrorBrush",
            "warning" => "FortivaSemanticWarningBrush",
            "info" => "FortivaSemanticInfoBrush",
            _ => "FortivaSemanticSuccessBrush"
        };
        return GetBrush(key, theme, context);
    }

    public static Brush GetAuditSeverityBgBrush(string severityKey, FrameworkElement? context = null, ElementTheme? theme = null)
    {
        theme ??= ResolveEffectiveTheme(context?.XamlRoot, context);
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
        box.DropDownClosed += (_, _) => ApplyComboBox(box, context);
    }

    private static void EnsureTextBoxThemeHook(TextBox box, FrameworkElement? context)
    {
        if (TextBoxThemeHooks.TryGetValue(box, out _))
            return;

        TextBoxThemeHooks.Add(box, box);
        void Refresh(object? _, object __) => ApplyTextBox(box, context);
        box.ActualThemeChanged += Refresh;
        box.GotFocus += Refresh;
        box.LostFocus += Refresh;
    }

    private static void EnsureComboBoxThemeHook(ComboBox box, FrameworkElement? context)
    {
        if (ComboBoxThemeHooks.TryGetValue(box, out _))
            return;

        ComboBoxThemeHooks.Add(box, box);
        void Refresh(object? _, object __) => ApplyComboBox(box, context);
        box.ActualThemeChanged += Refresh;
        box.GotFocus += Refresh;
        box.LostFocus += Refresh;
    }

    private static void ApplyComboBoxDropDown(ComboBox box, ElementTheme theme, FrameworkElement? context)
    {
        var host = context ?? box;
        var brushes = GetInputBrushes(theme, host);
        box.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(box, theme);
        PinAllInputResources(box, theme, host, brushes);
        ForceInputInnerChrome(box, theme, context, brushes);

        ThemeComboBoxOpenPopups(box, theme, context, brushes);

        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.ContainerFromIndex(i) is ComboBoxItem item)
                ApplyComboBoxItem(item, theme, context, brushes);
        }

        ScheduleComboBoxDropDownRetheme(box, theme, context, brushes, retriesRemaining: 4);
    }

    private static void ScheduleComboBoxDropDownRetheme(
        ComboBox box,
        ElementTheme theme,
        FrameworkElement? context,
        InputBrushSet brushes,
        int retriesRemaining)
    {
        if (retriesRemaining <= 0 || !box.IsDropDownOpen)
            return;

        box.DispatcherQueue.TryEnqueue(() =>
        {
            if (!box.IsDropDownOpen)
                return;

            ThemeComboBoxOpenPopups(box, theme, context, brushes);
            ForceInputInnerChrome(box, theme, context, brushes);

            for (var i = 0; i < box.Items.Count; i++)
            {
                if (box.ContainerFromIndex(i) is ComboBoxItem item)
                    ApplyComboBoxItem(item, theme, context, brushes);
            }

            ScheduleComboBoxDropDownRetheme(box, theme, context, brushes, retriesRemaining - 1);
        });
    }

    private static void ThemeComboBoxOpenPopups(
        ComboBox box,
        ElementTheme theme,
        FrameworkElement? context,
        InputBrushSet brushes)
    {
        if (box.XamlRoot is null)
            return;

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot))
        {
            if (popup.Child is FrameworkElement popupRoot)
                ThemePopupRoot(popupRoot, theme, context ?? box, brushes);
        }

        if (FindDescendant<Popup>(box) is { Child: FrameworkElement inlinePopupRoot })
            ThemePopupRoot(inlinePopupRoot, theme, context ?? box, brushes);
    }

    private static void ThemePopupRoot(
        FrameworkElement popupRoot,
        ElementTheme theme,
        FrameworkElement context,
        InputBrushSet brushes)
    {
        popupRoot.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(popupRoot, theme);
        PinAllInputResources(popupRoot, theme, context, brushes);
        ForceInputInnerChrome(popupRoot, theme, context, brushes);
    }

    private static void ApplyComboBoxItem(
        ComboBoxItem item,
        ElementTheme theme,
        FrameworkElement? context,
        InputBrushSet? brushes = null)
    {
        var host = context ?? item;
        brushes ??= GetInputBrushes(theme, host);
        item.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(item, theme);
        item.Foreground = brushes.Foreground;
        item.Background = brushes.ItemBackground;
        PinComboBoxItemResources(item, theme, host, brushes);
    }

    private static void PinAllInputResources(
        FrameworkElement element,
        ElementTheme theme,
        FrameworkElement context,
        InputBrushSet brushes)
    {
        PinResource(element, "TextControlBackground", brushes.Background);
        PinResource(element, "TextControlBackgroundFocused", brushes.Background);
        PinResource(element, "TextControlBackgroundPointerOver", brushes.BackgroundHover);
        PinResource(element, "TextControlForeground", brushes.Foreground);
        PinResource(element, "TextControlBorderBrush", brushes.Border);
        PinResource(element, "TextControlBorderBrushPointerOver", brushes.BorderHover);
        PinResource(element, "TextControlBorderBrushFocused", brushes.BorderFocused);
        PinResource(element, "TextControlPlaceholderForeground", brushes.Placeholder);
        PinResource(element, "ControlFillColorDefaultBrush", brushes.Background);
        PinResource(element, "ControlFillColorInputActiveBrush", brushes.Background);
        PinResource(element, "ControlFillColorSecondaryBrush", brushes.BackgroundHover);
        PinResource(element, "TextFillColorPrimaryBrush", brushes.Foreground);

        PinResource(element, "ComboBoxBackground", brushes.Background);
        PinResource(element, "ComboBoxBackgroundUnfocused", brushes.Background);
        PinResource(element, "ComboBoxBackgroundFocused", brushes.Background);
        PinResource(element, "ComboBoxBackgroundPointerOver", brushes.BackgroundHover);
        PinResource(element, "ComboBoxBackgroundPressed", brushes.BackgroundHover);
        PinResource(element, "ComboBoxForeground", brushes.Foreground);
        PinResource(element, "ComboBoxForegroundFocused", brushes.Foreground);
        PinResource(element, "ComboBoxBorderBrush", brushes.Border);
        PinResource(element, "ComboBoxBorderBrushPointerOver", brushes.BorderHover);
        PinResource(element, "ComboBoxBorderBrushPressed", brushes.BorderFocused);
        PinResource(element, "ComboBoxBorderBrushFocused", brushes.BorderFocused);
        PinResource(element, "ComboBoxDropDownBackground", brushes.DropDownBackground);
        PinResource(element, "ComboBoxDropDownBorderBrush", brushes.Border);
        PinResource(element, "ComboBoxDropDownGlyphForeground", brushes.Foreground);
        PinResource(element, "ComboBoxHeaderForeground", brushes.Foreground);

        PinComboBoxItemResources(element, theme, context, brushes);
    }

    private static void PinComboBoxItemResources(
        FrameworkElement element,
        ElementTheme theme,
        FrameworkElement context,
        InputBrushSet brushes)
    {
        PinResource(element, "ComboBoxItemForeground", brushes.Foreground);
        PinResource(element, "ComboBoxItemForegroundPointerOver", brushes.Foreground);
        PinResource(element, "ComboBoxItemForegroundSelected", brushes.Foreground);
        PinResource(element, "ComboBoxItemForegroundSelectedPointerOver", brushes.Foreground);
        PinResource(element, "ComboBoxItemBackground", brushes.ItemBackground);
        PinResource(element, "ComboBoxItemBackgroundPointerOver", brushes.ItemBackgroundHover);
        PinResource(element, "ComboBoxItemBackgroundSelected", brushes.ItemBackgroundHover);
        PinResource(element, "ComboBoxItemBackgroundSelectedPointerOver", brushes.ItemBackgroundHover);
        PinResource(element, "SubtleFillColorTransparentBrush", brushes.ItemBackground);
        PinResource(element, "SubtleFillColorSecondaryBrush", brushes.ItemBackgroundHover);
        PinResource(element, "SubtleFillColorTertiaryBrush", brushes.ItemBackgroundHover);
    }

    private static void ForceInputInnerChrome(
        FrameworkElement control,
        ElementTheme theme,
        FrameworkElement? context,
        InputBrushSet? brushes = null)
    {
        var host = context ?? control;
        brushes ??= GetInputBrushes(theme, host);

        control.RequestedTheme = theme;

        switch (control)
        {
            case TextBox textBox:
                textBox.Background = brushes.Background;
                textBox.Foreground = brushes.Foreground;
                textBox.BorderBrush = brushes.Border;
                break;
            case ComboBox comboBox:
                comboBox.Background = brushes.Background;
                comboBox.Foreground = brushes.Foreground;
                comboBox.BorderBrush = brushes.Border;
                break;
            case AutoSuggestBox suggestBox:
                suggestBox.Background = brushes.Background;
                suggestBox.Foreground = brushes.Foreground;
                suggestBox.BorderBrush = brushes.Border;
                break;
            case PasswordBox passwordBox:
                passwordBox.Background = brushes.Background;
                passwordBox.Foreground = brushes.Foreground;
                passwordBox.BorderBrush = brushes.Border;
                break;
        }

        foreach (var child in GetVisualDescendants(control))
        {
            if (child is FrameworkElement fe)
                fe.RequestedTheme = theme;

            switch (child)
            {
                case Border chrome:
                    chrome.Background = brushes.Background;
                    if (IsPrimaryInputBorder(chrome, control))
                    {
                        chrome.BorderBrush = brushes.Border;
                        if (chrome.BorderThickness == default)
                            chrome.BorderThickness = new Thickness(1);
                    }
                    else
                    {
                        chrome.BorderBrush = null;
                        chrome.BorderThickness = new Thickness(0);
                    }
                    break;
                case ScrollViewer scroll:
                    scroll.Background = brushes.Background;
                    break;
                case ContentPresenter presenter:
                    presenter.Foreground = brushes.Foreground;
                    break;
                case TextBlock text:
                    text.Foreground = brushes.Foreground;
                    break;
                case ComboBoxItem item:
                    item.Background = brushes.ItemBackground;
                    item.Foreground = brushes.Foreground;
                    break;
                case ListViewBase list:
                    list.Background = brushes.DropDownBackground;
                    break;
                case Panel panel when panel.Background is SolidColorBrush { Color.A: > 0 }:
                    panel.Background = brushes.Background;
                    break;
            }
        }
    }

    private static bool IsPrimaryInputBorder(Border border, FrameworkElement control) =>
        border.Name.Contains("BorderElement", StringComparison.OrdinalIgnoreCase)
        || (control is ComboBox && string.Equals(border.Name, "Background", StringComparison.Ordinal))
        || border.Name.Contains("DropDownBorder", StringComparison.OrdinalIgnoreCase);

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
