using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Services;

public static class VaultCategoryDialog
{
    public static async Task<string?> ShowCreateAsync(
        XamlRoot xamlRoot,
        ShellViewModel vm,
        string title = "New category",
        FrameworkElement? themeHost = null)
    {
        var box = new TextBox
        {
            PlaceholderText = "e.g. Work, Finance, Shopping…",
            MaxLength = VaultTagHelper.MaxTagLength
        };

        var hint = new TextBlock
        {
            Text = "Categories are tags on your entries. Assign them when saving a password.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.75,
            FontSize = 12
        };

        var form = new StackPanel { Spacing = 10, MinWidth = 320 };
        form.Children.Add(hint);
        form.Children.Add(box);

        var dlg = new ContentDialog
        {
            Title = title,
            Content = form,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        void ApplyTheme()
        {
            var theme = FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost);
            FortivaThemeResources.MergeOnto(form, theme);
            FortivaControlTheme.ApplyTextBox(box, form, theme);
            FortivaControlTheme.ApplyBodyText(hint, form);
        }

        FortivaDialogs.Configure(dlg, xamlRoot, onOpened: ApplyTheme, themeHost: themeHost);
        ApplyTheme();

        while (true)
        {
            if (await dlg.ShowAsync() != ContentDialogResult.Primary)
                return null;

            var tag = VaultTagHelper.NormalizeTag(box.Text);
            if (tag is null)
            {
                var warn = new ContentDialog
                {
                    Title = "Name required",
                    Content = "Enter a category name.",
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot
                };
                FortivaDialogs.Configure(warn, xamlRoot, themeHost: themeHost);
                await warn.ShowAsync();
                continue;
            }

            vm.EnsureVaultCategory(tag);
            return tag;
        }
    }
}
