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
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 2)
        };

        var form = new StackPanel { Spacing = 14, MinWidth = 420, MaxWidth = 480 };
        form.Children.Add(intro);
        form.Children.Add(CreateFieldGroup("Title", titleBox));
        form.Children.Add(CreateFieldGroup("Username / email", usernameBox));
        form.Children.Add(CreateFieldGroup("Website (for autofill)", urlBox));
        form.Children.Add(CreateFieldGroup("Categories", tagPicker.Root));

        var passwordRow = new Grid { ColumnSpacing = 8 };
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(passwordBox, 0);
        Grid.SetColumn(regenerateBtn, 1);
        passwordRow.Children.Add(passwordBox);
        passwordRow.Children.Add(regenerateBtn);

        var passwordGroup = new StackPanel { Spacing = 6 };
        passwordGroup.Children.Add(CreateLabel("Password (auto-generated)"));
        passwordGroup.Children.Add(passwordRow);
        form.Children.Add(passwordGroup);

        var dlg = new ContentDialog
        {
            Title = "Quick add entry",
            Content = form,
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
            foreach (var label in EnumerateFieldLabels(form))
                FortivaControlTheme.ApplySectionLabel(label, context: form);
            tagPicker.ApplyTheme(form);
        }

        static IEnumerable<TextBlock> EnumerateFieldLabels(StackPanel form)
        {
            foreach (var child in form.Children)
            {
                if (child is StackPanel group && group.Children.FirstOrDefault() is TextBlock label)
                    yield return label;
            }
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

    private static StackPanel CreateFieldGroup(string labelText, UIElement field)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(CreateLabel(labelText));
        group.Children.Add(field);
        return group;
    }

    private static TextBlock CreateLabel(string text)
        => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };
}
