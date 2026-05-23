using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Services;

/// <summary>Editable password generator dialog — shared by Entry page and Vault toolbar.</summary>
public static class PasswordGeneratorDialog
{
    public static async Task<string?> ShowAsync(
        XamlRoot xamlRoot,
        ShellViewModel vm,
        PasswordGeneratorOptions? initial = null)
    {
        var panel = new PasswordGeneratorPanel(vm, initial);

        var scroll = new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel.Root
        };

        var dlg = new ContentDialog
        {
            Title = "Password generator",
            Content = scroll,
            PrimaryButtonText = "Use password",
            SecondaryButtonText = "Regenerate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        ContentDialogResult result;
        do
        {
            result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Secondary) panel.Regenerate();
        } while (result == ContentDialogResult.Secondary);

        return result == ContentDialogResult.Primary && !string.IsNullOrEmpty(panel.CurrentPassword)
            ? panel.CurrentPassword
            : null;
    }
}
