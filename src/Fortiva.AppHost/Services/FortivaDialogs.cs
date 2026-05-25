using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>Ensures ContentDialog and code-built content match the app theme (light/dark).</summary>
public static class FortivaDialogs
{
    public static void Configure(ContentDialog dialog, XamlRoot xamlRoot, Action? onOpened = null)
    {
        dialog.XamlRoot = xamlRoot;

        void ApplyDialogTheme()
        {
            var theme = FortivaControlTheme.ResolveAppTheme();
            dialog.RequestedTheme = theme;
            FortivaThemeResources.MergeOnto(dialog, theme);

            if (dialog.Content is FrameworkElement content)
            {
                PrepareDialogContent(content, theme);
                ApplyThemeToTree(content, theme);
            }
        }

        if (dialog.Content is FrameworkElement initialContent)
            PrepareDialogContent(initialContent, FortivaControlTheme.ResolveAppTheme());

        dialog.Opened += (_, _) =>
        {
            ApplyDialogTheme();
            onOpened?.Invoke();
        };
    }

    public static Border WrapDialogContent(UIElement inner, ElementTheme? theme = null)
    {
        theme ??= FortivaControlTheme.ResolveAppTheme();
        var shell = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Background = FortivaControlTheme.GetBrush("FortivaSurfaceBrush", theme),
            BorderBrush = FortivaControlTheme.GetBrush("FortivaGlassBorderBrush", theme),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = inner
        };
        FortivaThemeResources.MergeOnto(shell, theme);
        return shell;
    }

    private static void PrepareDialogContent(FrameworkElement content, ElementTheme theme)
    {
        FortivaThemeResources.MergeOnto(content, theme);
        content.RequestedTheme = theme;
    }

    public static void ApplyThemeToTree(FrameworkElement element, ElementTheme? theme = null)
    {
        theme ??= FortivaControlTheme.ResolveAppTheme();
        FortivaThemeResources.MergeOnto(element, theme);

        switch (element)
        {
            case ComboBox comboBox:
                FortivaControlTheme.ApplyComboBox(comboBox, element);
                break;
            case TextBox textBox:
                FortivaControlTheme.ApplyTextBox(textBox, element);
                break;
            case PasswordBox passwordBox:
                FortivaControlTheme.ApplyPasswordBox(passwordBox, element);
                break;
            case ToggleSwitch toggleSwitch:
                FortivaControlTheme.ApplyToggleSwitch(toggleSwitch, element);
                break;
            case Slider slider:
                FortivaControlTheme.ApplySlider(slider, element);
                break;
            case TextBlock textBlock when textBlock.IsTextSelectionEnabled
                                      && textBlock.FontFamily?.Source == "Consolas":
                break;
            case TextBlock textBlock when textBlock.FontWeight == Microsoft.UI.Text.FontWeights.SemiBold:
                FortivaControlTheme.ApplySectionLabel(textBlock, context: element);
                break;
            case TextBlock textBlock when textBlock.FontSize <= 12:
                FortivaControlTheme.ApplyMutedText(textBlock, element);
                break;
            case TextBlock textBlock:
                FortivaControlTheme.ApplyBodyText(textBlock, element);
                break;
            case Border border when border.Child is TextBlock previewText
                                 && previewText.FontFamily?.Source == "Consolas":
                FortivaControlTheme.ApplyPreviewSurface(border, previewText, element);
                break;
        }

        switch (element)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    if (child is FrameworkElement fe)
                        ApplyThemeToTree(fe, theme);
                break;
            case ContentControl { Content: FrameworkElement contentFe }:
                ApplyThemeToTree(contentFe, theme);
                break;
            case Border { Child: FrameworkElement borderFe }:
                ApplyThemeToTree(borderFe, theme);
                break;
        }
    }
}
