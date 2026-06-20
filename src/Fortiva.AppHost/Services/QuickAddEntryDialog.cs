using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>Fast entry creation — title + optional fields, strong password auto-generated.</summary>
public static class QuickAddEntryDialog
{
    public enum QuickAddResult
    {
        Cancelled,
        Saved,
        OpenFullForm
    }

    public sealed class Outcome
    {
        public QuickAddResult Result { get; init; }
        public EntryDraft? Draft { get; init; }
    }

    public static async Task<Outcome> ShowAsync(
        XamlRoot xamlRoot,
        ShellViewModel vm,
        IEnumerable<string>? preselectedTags = null,
        FrameworkElement? themeHost = null)
    {
        var theme = FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost);

        var titleBox = new TextBox { PlaceholderText = "e.g. GitHub, Work email…" };
        var usernameBox = new TextBox { PlaceholderText = "username or email (optional)" };
        var urlBox = new TextBox { PlaceholderText = "https://example.com (optional)" };

        var tagPicker = new VaultTagPickerPanel(vm);
        tagPicker.SetSelectedTags(preselectedTags);

        var password = vm.GeneratePassword(PasswordGeneratorOptions.Default);
        var passwordBox = new TextBox
        {
            Text = password,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas")
        };

        var regenerateBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 14 },
                    new TextBlock { Text = "Regenerate" }
                }
            }
        };
        regenerateBtn.Click += (_, _) =>
        {
            password = vm.GeneratePassword(PasswordGeneratorOptions.Default);
            passwordBox.Text = password;
        };

        var intro = new TextBlock
        {
            Text = "Add a login quickly — pick a category or type a new one below.",
            TextWrapping = TextWrapping.WrapWholeWords,
            FontSize = 13
        };

        var titleLabel = CreateLabel("Title");
        var usernameLabel = CreateLabel("Username / email");
        var urlLabel = CreateLabel("Website (for autofill)");
        var categoryLabel = CreateLabel("Categories");
        var passwordLabel = CreateLabel("Password (auto-generated)");

        var form = new StackPanel { Spacing = 12, MinWidth = 420 };
        form.Children.Add(intro);
        form.Children.Add(titleLabel);
        form.Children.Add(titleBox);
        form.Children.Add(usernameLabel);
        form.Children.Add(usernameBox);
        form.Children.Add(urlLabel);
        form.Children.Add(urlBox);
        form.Children.Add(categoryLabel);
        form.Children.Add(tagPicker.Root);
        form.Children.Add(passwordLabel);

        var passwordRow = new Grid { ColumnSpacing = 8 };
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(passwordBox, 0);
        Grid.SetColumn(regenerateBtn, 1);
        passwordRow.Children.Add(passwordBox);
        passwordRow.Children.Add(regenerateBtn);
        form.Children.Add(passwordRow);

        var shell = FortivaDialogs.WrapDialogContent(form, xamlRoot, themeHost);

        var dlg = new ContentDialog
        {
            Title = "Quick add entry",
            Content = shell,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "More options…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void RefreshTheme()
        {
            var currentTheme = FortivaControlTheme.ResolveDialogTheme(xamlRoot, themeHost);
            FortivaThemeResources.MergeOnto(form, currentTheme);
            form.RequestedTheme = currentTheme;
            FortivaDialogs.ApplyThemeToTree(form, currentTheme);
            FortivaControlTheme.ApplyBodyText(intro, form);
            FortivaControlTheme.ApplyTextBox(titleBox, form);
            FortivaControlTheme.ApplyTextBox(usernameBox, form);
            FortivaControlTheme.ApplyTextBox(urlBox, form);
            FortivaControlTheme.ApplyReadOnlyPasswordTextBox(passwordBox, form);
            FortivaControlTheme.ApplySecondaryButton(regenerateBtn, form);
            FortivaControlTheme.ApplySectionLabel(titleLabel, context: form);
            FortivaControlTheme.ApplySectionLabel(usernameLabel, context: form);
            FortivaControlTheme.ApplySectionLabel(urlLabel, context: form);
            FortivaControlTheme.ApplySectionLabel(categoryLabel, context: form);
            FortivaControlTheme.ApplySectionLabel(passwordLabel, context: form);
            tagPicker.ApplyTheme(form);
        }

        FortivaDialogs.Configure(dlg, xamlRoot, onOpened: RefreshTheme, themeHost: themeHost);
        RefreshTheme();
        void OnThemeChanged() => RefreshTheme();
        vm.ThemeChanged += OnThemeChanged;

        try
        {
            while (true)
            {
                var result = await dlg.ShowAsync();
                if (result == ContentDialogResult.None)
                    return new Outcome { Result = QuickAddResult.Cancelled };

                var tags = tagPicker.GetSelectedTags();

                if (result == ContentDialogResult.Secondary)
                {
                    return new Outcome
                    {
                        Result = QuickAddResult.OpenFullForm,
                        Draft = new EntryDraft
                        {
                            Title = titleBox.Text.Trim(),
                            Username = usernameBox.Text.Trim(),
                            Url = urlBox.Text.Trim(),
                            Password = passwordBox.Text,
                            Tags = tags
                        }
                    };
                }

                if (string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    var warn = new ContentDialog
                    {
                        Title = "Title required",
                        Content = "Enter a title so you can find this entry later.",
                        CloseButtonText = "OK"
                    };
                    FortivaDialogs.Configure(warn, xamlRoot);
                    await warn.ShowAsync();
                    continue;
                }

                try
                {
                    var entry = new VaultEntry
                    {
                        Title = titleBox.Text.Trim(),
                        Username = usernameBox.Text.Trim(),
                        Password = passwordBox.Text,
                        Url = urlBox.Text.Trim(),
                        Tags = tags.ToList()
                    };
                    VaultEntryWebsite.NormalizeWebsite(entry);
                    vm.AddEntry(entry);
                    return new Outcome { Result = QuickAddResult.Saved };
                }
                catch (Exception ex)
                {
                    var err = new ContentDialog
                    {
                        Title = "Could not save",
                        Content = ex.Message,
                        CloseButtonText = "OK"
                    };
                    FortivaDialogs.Configure(err, xamlRoot);
                    await err.ShowAsync();
                }
            }
        }
        finally
        {
            vm.ThemeChanged -= OnThemeChanged;
        }
    }

    private static TextBlock CreateLabel(string text)
        => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };
}
