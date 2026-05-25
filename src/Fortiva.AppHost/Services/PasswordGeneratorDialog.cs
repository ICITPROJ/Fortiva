using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Services;

/// <summary>Password generator dialog — same panel as the nav page, dialog action buttons.</summary>
public static class PasswordGeneratorDialog
{
    public const int ContentMinWidth = 640;

    public sealed class Result
    {
        public required string Password { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    public static async Task<Result?> ShowAsync(
        XamlRoot xamlRoot,
        ShellViewModel vm,
        PasswordGeneratorOptions? initial = null,
        IEnumerable<string>? preselectedTags = null)
    {
        var clipboard = new ClipboardService(vm.Policy, vm.PersonalSettings.ClipboardClearSeconds, vm.LogPolicyViolation);
        var panel = new PasswordGeneratorPanel(vm, initial, PasswordGeneratorHostMode.Dialog, clipboard);
        panel.SetSelectedTags(preselectedTags);
        var theme = FortivaControlTheme.ResolveAppTheme();

        var scroll = new ScrollViewer
        {
            MaxHeight = 620,
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
            result = await dlg.ShowAsync();
        }
        finally
        {
            vm.ThemeChanged -= OnThemeChanged;
        }

        if (result != ContentDialogResult.Primary || string.IsNullOrEmpty(panel.CurrentPassword))
            return null;

        return new Result
        {
            Password = panel.CurrentPassword,
            Tags = panel.GetSelectedTags()
        };
    }
}
