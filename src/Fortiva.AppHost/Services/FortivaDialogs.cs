using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Fortiva.AppHost.Services;

/// <summary>Ensures ContentDialog and code-built content match the app theme (light/dark).</summary>
public static class FortivaDialogs
{
    public static void Configure(
        ContentDialog dialog,
        XamlRoot xamlRoot,
        Action? onOpened = null,
        FrameworkElement? themeHost = null)
    {
        dialog.XamlRoot = xamlRoot;

        void ApplyDialogTheme()
        {
            var theme = FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost);
            dialog.RequestedTheme = theme;
            FortivaThemeResources.MergeOnto(dialog, theme);

            if (dialog.Content is FrameworkElement content)
            {
                PrepareDialogContent(content, theme);
                ApplyThemeToTree(content, theme);
            }
        }

        if (dialog.Content is FrameworkElement initialContent)
            PrepareDialogContent(initialContent, FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost));

        dialog.Opened += (_, _) =>
        {
            ApplyDialogTheme();
            EnableEnterKeyDefaultButton(dialog);
            onOpened?.Invoke();
        };
    }

    /// <summary>
    /// Makes Enter activate the dialog default button (Primary / Close), including from text fields.
    /// Multiline fields keep Enter for new lines; use Ctrl+Enter to submit there.
    /// </summary>
    public static void EnableEnterKeyDefaultButton(ContentDialog dialog)
    {
        var defaultButton = ResolveDefaultButton(dialog);
        if (defaultButton == ContentDialogButton.None)
            return;

        dialog.PreviewKeyDown += (_, e) => OnDialogPreviewKeyDown(dialog, defaultButton, e);

        if (dialog.Content is DependencyObject content)
            WireInputEnterKey(content, dialog, defaultButton);
    }

    private static ContentDialogButton ResolveDefaultButton(ContentDialog dialog)
    {
        if (dialog.DefaultButton != ContentDialogButton.None)
            return dialog.DefaultButton;

        if (!string.IsNullOrEmpty(dialog.PrimaryButtonText))
            return ContentDialogButton.Primary;

        if (!string.IsNullOrEmpty(dialog.CloseButtonText))
            return ContentDialogButton.Close;

        return ContentDialogButton.None;
    }

    private static void OnDialogPreviewKeyDown(
        ContentDialog dialog,
        ContentDialogButton defaultButton,
        KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        var focused = FocusManager.GetFocusedElement(dialog.XamlRoot);
        if (focused is TextBox or PasswordBox or ComboBox or AutoSuggestBox)
            return;

        if (TryActivateDefaultButton(dialog, defaultButton))
            e.Handled = true;
    }

    private static void WireInputEnterKey(
        DependencyObject root,
        ContentDialog dialog,
        ContentDialogButton defaultButton)
    {
        switch (root)
        {
            case TextBox textBox:
                textBox.KeyDown += (_, e) => OnInputKeyDown(dialog, defaultButton, textBox, e);
                break;
            case PasswordBox passwordBox:
                passwordBox.KeyDown += (_, e) => OnInputKeyDown(dialog, defaultButton, passwordBox, e);
                break;
        }

        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    WireInputEnterKey(child, dialog, defaultButton);
                break;
            case Border border when border.Child is DependencyObject borderChild:
                WireInputEnterKey(borderChild, dialog, defaultButton);
                break;
            case ContentControl contentControl when contentControl.Content is DependencyObject contentChild:
                WireInputEnterKey(contentChild, dialog, defaultButton);
                break;
            case ScrollViewer scrollViewer when scrollViewer.Content is DependencyObject scrollChild:
                WireInputEnterKey(scrollChild, dialog, defaultButton);
                break;
            case ItemsControl itemsControl when itemsControl.ItemsPanelRoot is DependencyObject panelRoot:
                WireInputEnterKey(panelRoot, dialog, defaultButton);
                break;
        }
    }

    private static void OnInputKeyDown(
        ContentDialog dialog,
        ContentDialogButton defaultButton,
        Control input,
        KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        if (input is TextBox { AcceptsReturn: true } && !KeyboardHelpers.IsControlDown())
            return;

        if (TryActivateDefaultButton(dialog, defaultButton))
            e.Handled = true;
    }

    private static bool TryActivateDefaultButton(ContentDialog dialog, ContentDialogButton defaultButton)
    {
        var buttonName = defaultButton switch
        {
            ContentDialogButton.Primary => "PrimaryButton",
            ContentDialogButton.Secondary => "SecondaryButton",
            ContentDialogButton.Close => "CloseButton",
            _ => null
        };

        if (buttonName is null)
            return false;

        if (!IsDefaultButtonEnabled(dialog, defaultButton))
            return false;

        dialog.UpdateLayout();

        if (FindTemplateButton(dialog, buttonName) is not Button button || !button.IsEnabled)
            return false;

        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is ButtonAutomationPeer peer)
        {
            peer.Invoke();
            return true;
        }

        return false;
    }

    private static bool IsDefaultButtonEnabled(ContentDialog dialog, ContentDialogButton defaultButton)
        => defaultButton switch
        {
            ContentDialogButton.Primary => dialog.IsPrimaryButtonEnabled
                && !string.IsNullOrEmpty(dialog.PrimaryButtonText),
            ContentDialogButton.Secondary => dialog.IsSecondaryButtonEnabled
                && !string.IsNullOrEmpty(dialog.SecondaryButtonText),
            ContentDialogButton.Close => !string.IsNullOrEmpty(dialog.CloseButtonText),
            _ => false
        };

    private static Button? FindTemplateButton(ContentDialog dialog, string name)
    {
        if (dialog.FindName(name) is Button named)
            return named;

        return FindDescendant<Button>(dialog, b => string.Equals(b.Name, name, StringComparison.Ordinal));
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
                return match;

            var found = FindDescendant(child, predicate);
            if (found is not null)
                return found;
        }

        return null;
    }

    public static Border WrapDialogContent(UIElement inner, XamlRoot? xamlRoot = null, FrameworkElement? themeHost = null)
    {
        var theme = xamlRoot is not null
            ? FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost)
            : FortivaControlTheme.ResolveEffectiveTheme(themeHost?.XamlRoot, themeHost);
        // ContentDialog already provides the outer chrome — avoid a nested bordered panel.
        var shell = new Border
        {
            Padding = new Thickness(0, 2, 0, 4),
            BorderThickness = new Thickness(0),
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
        var resolved = theme ?? FortivaControlTheme.ResolveEffectiveTheme(element.XamlRoot, element);
        FortivaThemeResources.MergeOnto(element, resolved);
        element.RequestedTheme = resolved;

        switch (element)
        {
            case ComboBox comboBox:
                FortivaControlTheme.ApplyComboBox(comboBox, element, resolved);
                break;
            case TextBox textBox:
                FortivaControlTheme.ApplyTextBox(textBox, element, resolved);
                break;
            case PasswordBox passwordBox:
                FortivaControlTheme.ApplyPasswordBox(passwordBox, element, resolved);
                break;
            case AutoSuggestBox autoSuggestBox:
                FortivaControlTheme.ApplyAutoSuggestBox(autoSuggestBox, element, resolved);
                break;
            case ToggleSwitch toggleSwitch:
                FortivaControlTheme.ApplyToggleSwitch(toggleSwitch, element, resolved);
                break;
            case Slider slider:
                FortivaControlTheme.ApplySlider(slider, element, resolved);
                break;
            case Button button:
                FortivaControlTheme.ApplySecondaryButton(button, element, resolved);
                break;
            case FontIcon fontIcon:
                FortivaControlTheme.ApplyFontIcon(fontIcon, element, resolved);
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
                        ApplyThemeToTree(fe, resolved);
                break;
            case ContentControl { Content: FrameworkElement contentFe }:
                ApplyThemeToTree(contentFe, resolved);
                break;
            case Border { Child: FrameworkElement borderFe }:
                ApplyThemeToTree(borderFe, resolved);
                break;
        }
    }
}
