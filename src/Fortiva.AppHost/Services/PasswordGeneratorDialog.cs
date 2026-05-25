using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Services;

/// <summary>Password generator dialog — same panel as the nav page, dialog action buttons.</summary>
public static class PasswordGeneratorDialog
{
    public const int ContentMinWidth = 600;

    public static async Task<string?> ShowAsync(
        XamlRoot xamlRoot,
        ShellViewModel vm,
        PasswordGeneratorOptions? initial = null)
    {
        var panel = new PasswordGeneratorPanel(vm, initial, PasswordGeneratorHostMode.Dialog);
        var theme = FortivaControlTheme.ResolveAppTheme();

        var scroll = new ScrollViewer
        {
            MaxHeight = 560,
            MinWidth = ContentMinWidth,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = panel.Root
        };
        FortivaThemeResources.MergeOnto(scroll, theme);

        var shell = FortivaDialogs.WrapDialogContent(scroll, theme);

        var dlg = new ContentDialog
        {
            Title = "Password generator",
            Content = shell,
            PrimaryButtonText = "Use password",
            SecondaryButtonText = "Regenerate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        FortivaDialogs.Configure(dlg, xamlRoot, onOpened: () => panel.ApplyThemeResources());
        panel.ApplyThemeResources();

        void OnThemeChanged() => panel.ApplyThemeResources();
        vm.ThemeChanged += OnThemeChanged;

        ContentDialogResult result;
        try
        {
            do
            {
                result = await dlg.ShowAsync();
                if (result == ContentDialogResult.Secondary) panel.Regenerate();
            } while (result == ContentDialogResult.Secondary);
        }
        finally
        {
            vm.ThemeChanged -= OnThemeChanged;
        }

        return result == ContentDialogResult.Primary && !string.IsNullOrEmpty(panel.CurrentPassword)
            ? panel.CurrentPassword
            : null;
    }
}
