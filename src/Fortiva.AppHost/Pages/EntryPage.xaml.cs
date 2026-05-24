using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Otp;
using Fortiva.Core.Password;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class EntryPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly ClipboardService _clipboard;
    private VaultEntry? _existing;
    private bool _isNew;
    private CancellationTokenSource? _revealCts;
    private DispatcherTimer? _otpTimer;
    private string? _normalizedTotpSecret;

    public EntryPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        _revealCts?.Cancel();
        PasswordBox.PasswordRevealMode = PasswordRevealMode.Peek;

        if (e.Parameter is VaultEntry entry)
        {
            _existing = entry;
            _isNew = false;
            PageTitle.Text = entry.Title;
            DeleteBtn.Visibility = Visibility.Visible;
            TitleBox.Text = entry.Title;
            UsernameBox.Text = entry.Username;
            PasswordBox.Password = entry.Password;
            UrlBox.Text = entry.Url;
            TagsBox.Text = string.Join(", ", entry.Tags);
            NotesBox.Text = entry.Notes;
            FavoriteBtn.IsChecked = entry.IsFavorite;
            SecureNoteToggle.IsOn = entry.IsSecureNote;
            TotpSecretBox.Password = entry.TotpSecret ?? "";
            _normalizedTotpSecret = string.IsNullOrWhiteSpace(entry.TotpSecret)
                ? null
                : TotpSecretNormalizer.Normalize(entry.TotpSecret);
            UpdateStrength(entry.Password);
        }
        else
        {
            ResetForNewEntry();
        }

        ApplyReadOnlyState();
        ConfigureOtpSection();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopOtpTimer();
    }

    private void ConfigureOtpSection()
    {
        OtpSection.Visibility = _vm.CanUseTotp ? Visibility.Visible : Visibility.Collapsed;
        if (!_vm.CanUseTotp)
        {
            StopOtpTimer();
            return;
        }

        StartOtpTimer();
        RefreshOtpDisplay();
    }

    private void StartOtpTimer()
    {
        StopOtpTimer();
        _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _otpTimer.Tick += (_, _) => RefreshOtpDisplay();
        _otpTimer.Start();
    }

    private void StopOtpTimer()
    {
        if (_otpTimer is null) return;
        _otpTimer.Stop();
        _otpTimer = null;
    }

    private void RefreshOtpDisplay()
    {
        if (!_vm.CanUseTotp)
            return;

        if (string.IsNullOrWhiteSpace(_normalizedTotpSecret))
        {
            OtpCodeText.Text = "------";
            OtpTimerBar.Value = TotpGenerator.DefaultPeriodSeconds;
            OtpTimerLabel.Text = "Add a secret to generate codes.";
            return;
        }

        try
        {
            OtpCodeText.Text = TotpGenerator.Generate(_normalizedTotpSecret);
            var remaining = TotpGenerator.GetRemainingSeconds();
            OtpTimerBar.Value = remaining;
            OtpTimerLabel.Text = $"Refreshes in {remaining} s";
        }
        catch (Exception ex)
        {
            OtpCodeText.Text = "------";
            OtpTimerLabel.Text = ex.Message;
        }
    }

    private void TotpSecret_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            _normalizedTotpSecret = TotpSecretNormalizer.Normalize(TotpSecretBox.Password);
        }
        catch
        {
            _normalizedTotpSecret = null;
        }
        RefreshOtpDisplay();
    }

    private void CopyOtp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_normalizedTotpSecret))
            return;
        try
        {
            var code = TotpGenerator.Generate(_normalizedTotpSecret);
            _clipboard.CopyText(code);
            _vm.ResetAutoLock();
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowErrorAsync(ex.Message);
        }
    }

    private void ResetForNewEntry()
    {
        _existing = null;
        _isNew = true;
        PageTitle.Text = "New entry";
        DeleteBtn.Visibility = Visibility.Collapsed;
        TitleBox.Text = "";
        UsernameBox.Text = "";
        PasswordBox.Password = "";
        UrlBox.Text = "";
        TagsBox.Text = "";
        NotesBox.Text = "";
        FavoriteBtn.IsChecked = false;
        SecureNoteToggle.IsOn = false;
        TotpSecretBox.Password = "";
        _normalizedTotpSecret = null;
        UpdateStrength("");
    }

    private void ApplyReadOnlyState()
    {
        var ro = _vm.IsReadOnly;
        TitleBox.IsEnabled = !ro;
        UsernameBox.IsEnabled = !ro;
        PasswordBox.IsEnabled = !ro;
        UrlBox.IsEnabled = !ro;
        TagsBox.IsEnabled = !ro;
        NotesBox.IsEnabled = !ro;
        FavoriteBtn.IsEnabled = !ro;
        SecureNoteToggle.IsEnabled = !ro;
        TotpSecretBox.IsEnabled = !ro;
        DeleteBtn.IsEnabled = !ro && !_isNew;
        SaveBtn.IsEnabled = !ro;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => UpdateStrength(PasswordBox.Password);

    private void UpdateStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) { StrengthBar.Value = 0; StrengthLabel.Text = ""; return; }
        var result = _vm.AnalyzeStrength(password);
        StrengthBar.Value = (int)result.Strength;
        StrengthLabel.Text = result.Label;
        StrengthBar.Foreground = result.Strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak =>
                new SolidColorBrush(Color.FromArgb(255, 220, 50, 50)),
            PasswordStrength.Fair =>
                new SolidColorBrush(Color.FromArgb(255, 200, 130, 0)),
            PasswordStrength.Strong =>
                new SolidColorBrush(Color.FromArgb(255, 0, 160, 80)),
            _ =>
                new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
        };
    }

    private async void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        var password = await PasswordGeneratorDialog.ShowAsync(Content.XamlRoot, _vm);
        if (password is null) return;
        PasswordBox.Password = password;
        UpdateStrength(password);
    }

    private void RevealPassword_Click(object sender, RoutedEventArgs e)
    {
        PasswordBox.PasswordRevealMode = PasswordRevealMode.Visible;
        _revealCts?.Cancel();
        _revealCts = new CancellationTokenSource();
        var token = _revealCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (PasswordBox.PasswordRevealMode == PasswordRevealMode.Visible)
                        PasswordBox.PasswordRevealMode = PasswordRevealMode.Peek;
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(UsernameBox.Text)) _clipboard.CopyText(UsernameBox.Text);
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordBox.Password)) _clipboard.CopyPassword(PasswordBox.Password);
    }

    private async void VisitUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!SafeUriLauncher.TryNormalizeHttpUri(url, out var uri))
        {
            await ShowErrorAsync("Only http and https URLs are allowed.");
            return;
        }
        try
        {
            var launched = await Launcher.LaunchUriAsync(uri);
            if (!launched) await ShowErrorAsync("Could not open URL. Check that it is valid.");
        }
        catch
        {
            await ShowErrorAsync("Could not open URL. Check that it is valid.");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReadOnly) { _ = ShowErrorAsync("Vault is read-only."); return; }
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            _ = ShowErrorAsync("Title is required.");
            return;
        }

        var entry = _existing?.Clone() ?? new VaultEntry();
        entry.Title = TitleBox.Text.Trim();
        entry.Username = UsernameBox.Text.Trim();
        entry.Password = PasswordBox.Password;
        entry.Url = UrlBox.Text.Trim();
        entry.Notes = NotesBox.Text;
        entry.IsFavorite = FavoriteBtn.IsChecked ?? false;
        entry.IsSecureNote = SecureNoteToggle.IsOn;
        entry.Tags = TagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (_vm.CanUseTotp)
        {
            entry.TotpSecret = string.IsNullOrWhiteSpace(TotpSecretBox.Password)
                ? null
                : TotpSecretNormalizer.Normalize(TotpSecretBox.Password);
        }

        try
        {
            if (_isNew)
                _vm.AddEntry(entry);
            else
                _vm.UpdateEntry(entry);
            NavigationService.Current.GoBack();
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync(ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;
        var dlg = new ContentDialog
        {
            Title = "Delete entry?",
            Content = new TextBlock { Text = $"'{_existing.Title}' will be permanently deleted.", TextWrapping = TextWrapping.WrapWholeWords },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            _vm.DeleteEntry(_existing.Id);
            NavigationService.Current.GoBack();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => NavigationService.Current.GoBack();

    private async Task ShowErrorAsync(string msg)
    {
        var dlg = new ContentDialog
        {
            Title = "Error",
            Content = new TextBlock { Text = msg },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dlg.ShowAsync();
    }
}
