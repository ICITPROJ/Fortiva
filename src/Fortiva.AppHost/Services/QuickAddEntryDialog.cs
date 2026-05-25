using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public static async Task<Outcome> ShowAsync(XamlRoot xamlRoot, ShellViewModel vm)
    {
        var titleBox = new TextBox { PlaceholderText = "e.g. GitHub, Work email…" };
        var usernameBox = new TextBox { PlaceholderText = "username or email (optional)" };
        var urlBox = new TextBox { PlaceholderText = "https://example.com (optional)" };
        FortivaControlTheme.ApplyTextBox(titleBox);
        FortivaControlTheme.ApplyTextBox(usernameBox);
        FortivaControlTheme.ApplyTextBox(urlBox);

        var password = vm.GeneratePassword(PasswordGeneratorOptions.Default);
        var passwordBox = new PasswordBox
        {
            Password = password,
            IsEnabled = false,
            PasswordRevealMode = PasswordRevealMode.Visible
        };
        FortivaControlTheme.ApplyPasswordBox(passwordBox);

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
            passwordBox.Password = password;
        };

        var intro = new TextBlock
        {
            Text = "Add a login quickly — expand with More options if you need tags, notes, or TOTP.",
            TextWrapping = TextWrapping.WrapWholeWords,
            FontSize = 12
        };
        FortivaControlTheme.ApplyMutedText(intro);

        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(intro);
        form.Children.Add(CreateLabel("Title"));
        form.Children.Add(titleBox);
        form.Children.Add(CreateLabel("Username / email"));
        form.Children.Add(usernameBox);
        form.Children.Add(CreateLabel("URL"));
        form.Children.Add(urlBox);
        form.Children.Add(CreateLabel("Password (auto-generated)"));

        var passwordRow = new Grid { ColumnSpacing = 8 };
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(passwordBox, 0);
        Grid.SetColumn(regenerateBtn, 1);
        passwordRow.Children.Add(passwordBox);
        passwordRow.Children.Add(regenerateBtn);
        form.Children.Add(passwordRow);

        var theme = FortivaControlTheme.ResolveEffectiveTheme(xamlRoot);
        var shell = FortivaDialogs.WrapDialogContent(form, theme);

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
            FortivaControlTheme.ApplyTextBox(titleBox);
            FortivaControlTheme.ApplyTextBox(usernameBox);
            FortivaControlTheme.ApplyTextBox(urlBox);
            FortivaControlTheme.ApplyPasswordBox(passwordBox);
            FortivaControlTheme.ApplyMutedText(intro);
        }

        FortivaDialogs.Configure(dlg, xamlRoot, onOpened: RefreshTheme);
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
                            Password = passwordBox.Password
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
                    vm.AddEntry(new VaultEntry
                    {
                        Title = titleBox.Text.Trim(),
                        Username = usernameBox.Text.Trim(),
                        Password = passwordBox.Password,
                        Url = urlBox.Text.Trim()
                    });
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
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        FortivaControlTheme.ApplySectionLabel(label);
        return label;
    }
}
