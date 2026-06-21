using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Otp;
using Fortiva.Core.Password;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class EntryPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly ClipboardService _clipboard;
    private readonly VaultTagPickerPanel _tagPicker;
    private VaultEntry? _existing;
    private VaultEntryPaneHost? _paneHost;
    private bool _isNew;
    private CancellationTokenSource? _revealCts;
    private DispatcherTimer? _otpTimer;
    private string? _normalizedTotpSecret;
    private string? _baselineSignature;

    public EntryPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
        _tagPicker = new VaultTagPickerPanel(_vm);
        TagsPickerHost.Child = _tagPicker.Root;
        foreach (var field in new Control[] { TitleBox, UsernameBox, UrlBox, PasswordBox })
            field.KeyDown += InputField_KeyDown;
        TitleBox.TextChanged += (_, _) => OnFormChanged();
        UsernameBox.TextChanged += (_, _) => OnFormChanged();
        UrlBox.TextChanged += (_, _) => OnFormChanged();
        NotesBox.TextChanged += (_, _) => OnFormChanged();
        PasswordBox.PasswordChanged += (_, _) => OnFormChanged();
        TotpSecretBox.PasswordChanged += (_, _) => OnFormChanged();
        FavoriteBtn.Checked += (_, _) => OnFormChanged();
        FavoriteBtn.Unchecked += (_, _) => OnFormChanged();
        _tagPicker.TagsChanged += OnFormChanged;
    }

    private void OnFormChanged() { }

    private string BuildSignature()
    {
        var tags = string.Join(",", _tagPicker.GetSelectedTags().OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        return string.Join('\u001f',
        [
            TitleBox.Text,
            UsernameBox.Text,
            PasswordBox.Password,
            UrlBox.Text,
            NotesBox.Text,
            (FavoriteBtn.IsChecked ?? false).ToString(),
            SecureNoteToggle.IsOn.ToString(),
            TotpSecretBox.Password,
            tags
        ]);
    }

    private bool IsDirty => _baselineSignature is not null && BuildSignature() != _baselineSignature;

    private void CaptureBaseline() => _baselineSignature = BuildSignature();

    private async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        if (!IsDirty || _vm.IsReadOnly)
            return true;

        var dlg = new ContentDialog
        {
            Title = "Unsaved changes",
            Content = new TextBlock
            {
                Text = "Save your edits before leaving this entry?",
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot, themeHost: this);
        var result = await dlg.ShowAsync();
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary))
            return false;
        if (result == ContentDialogResult.Primary)
        {
            if (SaveBtn.IsEnabled)
                Save_Click(SaveBtn, new RoutedEventArgs());
            return !IsDirty;
        }

        return true;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double pageWidth)
    {
        var narrow = pageWidth < 720;

        FormCol1.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(TitlePanel, 0);
        Grid.SetColumnSpan(TitlePanel, 2);

        Grid.SetColumn(UsernamePanel, 0);
        Grid.SetRow(UsernamePanel, 1);

        Grid.SetColumn(UrlPanel, narrow ? 0 : 1);
        Grid.SetRow(UrlPanel, narrow ? 2 : 1);

        Grid.SetColumn(PasswordPanel, 0);
        Grid.SetRow(PasswordPanel, narrow ? 3 : 2);
        Grid.SetColumnSpan(PasswordPanel, 2);

        Grid.SetColumn(TagsPanel, 0);
        Grid.SetRow(TagsPanel, narrow ? 4 : 3);

        Grid.SetColumn(SecureNotePanel, narrow ? 0 : 1);
        Grid.SetRow(SecureNotePanel, narrow ? 5 : 3);

        Grid.SetColumn(NotesPanel, 0);
        Grid.SetRow(NotesPanel, narrow ? 6 : 4);
        Grid.SetColumnSpan(NotesPanel, 2);

        Grid.SetColumn(ProvenancePanel, 0);
        Grid.SetRow(ProvenancePanel, narrow ? 7 : 5);
        Grid.SetColumnSpan(ProvenancePanel, 2);

        Grid.SetColumn(OtpSection, 0);
        Grid.SetRow(OtpSection, narrow ? 8 : 6);
        Grid.SetColumnSpan(OtpSection, 2);

        OtpCol1.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(TotpSecretBox, 0);
        Grid.SetRow(TotpSecretBox, 0);
        Grid.SetColumn(OtpPreviewBorder, narrow ? 0 : 1);
        Grid.SetRow(OtpPreviewBorder, narrow ? 1 : 0);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == VirtualKey.S && KeyboardHelpers.IsControlDown())
        {
            e.Handled = true;
            Save_Click(SaveBtn, new RoutedEventArgs());
        }
    }

    private void InputField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || e.Handled)
            return;

        if (ReferenceEquals(sender, NotesBox))
            return;

        e.Handled = true;
        if (SaveBtn.IsEnabled)
            Save_Click(SaveBtn, new RoutedEventArgs());
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _vm.ThemeChanged += OnThemeChanged;
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        _revealCts?.Cancel();
        PasswordBox.PasswordRevealMode = PasswordRevealMode.Peek;

        _paneHost = null;

        VaultEntry? entry = null;
        if (e.Parameter is EntryPaneNavigationContext paneCtx)
        {
            entry = paneCtx.Entry;
            _paneHost = paneCtx.Host;
        }
        else if (e.Parameter is VaultEntry vaultEntry)
        {
            entry = vaultEntry;
        }

        if (entry is null && _vm.ConsumePendingOpenVaultEntryId() is { } pendingId)
            entry = _vm.FindEntry(pendingId);

        if (entry is not null)
        {
            _existing = entry;
            _isNew = false;
            PageTitle.Text = entry.Title;
            DeleteBtn.Visibility = Visibility.Visible;
            TitleBox.Text = entry.Title;
            UsernameBox.Text = entry.Username;
            PasswordBox.Password = entry.Password;
            UrlBox.Text = entry.Url;
            _tagPicker.ReloadKnownTags();
            _tagPicker.SetSelectedTags(entry.Tags);
            NotesBox.Text = entry.Notes;
            FavoriteBtn.IsChecked = entry.IsFavorite;
            SecureNoteToggle.IsOn = entry.IsSecureNote;
            TotpSecretBox.Password = entry.TotpSecret ?? "";
            _normalizedTotpSecret = string.IsNullOrWhiteSpace(entry.TotpSecret)
                ? null
                : TotpSecretNormalizer.Normalize(entry.TotpSecret);
            UpdateStrength(entry.Password);
            BindProvenance(entry);
        }
        else if (e.Parameter is EntryDraft draft)
        {
            ResetForNewEntry();
            if (!string.IsNullOrWhiteSpace(draft.Title)) TitleBox.Text = draft.Title;
            if (!string.IsNullOrWhiteSpace(draft.Username)) UsernameBox.Text = draft.Username;
            if (!string.IsNullOrWhiteSpace(draft.Url)) UrlBox.Text = draft.Url;
            if (!string.IsNullOrWhiteSpace(draft.Password))
            {
                PasswordBox.Password = draft.Password;
                UpdateStrength(draft.Password);
            }
            if (draft.Tags is { Count: > 0 })
                _tagPicker.SetSelectedTags(draft.Tags);
            TitleBox.Focus(FocusState.Programmatic);
        }
        else
        {
            ResetForNewEntry();
            TitleBox.Focus(FocusState.Programmatic);
        }

        BackBtn.Visibility = _paneHost is null ? Visibility.Visible : Visibility.Collapsed;
        ClosePaneBtn.Visibility = _paneHost is null ? Visibility.Collapsed : Visibility.Visible;
        ClosePaneSeparator.Visibility = ClosePaneBtn.Visibility;
        PageRoot.Margin = _paneHost is null
            ? new Thickness(24, 20, 24, 16)
            : new Thickness(16, 16, 14, 12);
        if (_paneHost is not null)
            _paneHost.ConfirmCloseAsync = ConfirmDiscardIfDirtyAsync;

        ApplySecureNoteLayout();
        ApplyReadOnlyState();
        _tagPicker.ApplyTheme(this);
        ConfigureOtpSection();
        ApplyResponsiveLayout(ActualWidth);
        CaptureBaseline();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
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
        _tagPicker.ReloadKnownTags();
        _tagPicker.SetSelectedTags([]);
        NotesBox.Text = "";
        FavoriteBtn.IsChecked = false;
        SecureNoteToggle.IsOn = false;
        TotpSecretBox.Password = "";
        _normalizedTotpSecret = null;
        var generated = _vm.GeneratePassword(PasswordGeneratorOptions.Default);
        PasswordBox.Password = generated;
        UpdateStrength(generated);
        ProvenancePanel.Visibility = Visibility.Collapsed;
    }

    private void BindProvenance(VaultEntry entry)
    {
        if (!entry.HasImportProvenance)
        {
            ProvenancePanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProvenancePanel.Visibility = Visibility.Visible;
        ProvenanceSourceText.Text = string.IsNullOrWhiteSpace(entry.ImportSource)
            ? "Imported entry"
            : $"Imported from {entry.ImportSource}";

        var parts = new List<string>();
        if (entry.SourceCreatedAt.HasValue)
            parts.Add($"Originally created {entry.SourceCreatedAt.Value.LocalDateTime:g}");
        if (entry.ImportedAt.HasValue)
            parts.Add($"Imported into Fortiva {entry.ImportedAt.Value.LocalDateTime:g}");
        else
            parts.Add($"Created in Fortiva {entry.CreatedAt.LocalDateTime:g}");
        if (entry.SourceLastUsedAt.HasValue)
            parts.Add($"Last used (source) {entry.SourceLastUsedAt.Value.LocalDateTime:g}");
        parts.Add($"Last modified {entry.ModifiedAt.LocalDateTime:g}");

        ProvenanceDatesText.Text = string.Join(" · ", parts);
    }

    private void ApplyReadOnlyState()
    {
        var ro = _vm.IsReadOnly;
        TitleBox.IsEnabled = !ro;
        UsernameBox.IsEnabled = !ro;
        PasswordBox.IsEnabled = !ro;
        UrlBox.IsEnabled = !ro;
        _tagPicker.SetEnabled(!ro);
        NotesBox.IsEnabled = !ro;
        FavoriteBtn.IsEnabled = !ro;
        SecureNoteToggle.IsEnabled = !ro;
        TotpSecretBox.IsEnabled = !ro;
        DeleteBtn.IsEnabled = !ro && !_isNew;
        SaveBtn.IsEnabled = !ro;
    }

    private void SecureNoteToggle_Toggled(object sender, RoutedEventArgs e)
        => ApplySecureNoteLayout();

    private void ApplySecureNoteLayout()
    {
        var isNote = SecureNoteToggle.IsOn;
        PasswordPanel.Visibility = isNote ? Visibility.Collapsed : Visibility.Visible;
        if (isNote)
        {
            PasswordBox.Password = "";
            StrengthBar.Value = 0;
            StrengthLabel.Text = "";
        }
        else if (!string.IsNullOrEmpty(PasswordBox.Password))
        {
            UpdateStrength(PasswordBox.Password);
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => UpdateStrength(PasswordBox.Password);

    private void UpdateStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) { StrengthBar.Value = 0; StrengthLabel.Text = ""; return; }
        var result = _vm.AnalyzeStrength(password);
        StrengthBar.Value = (int)result.Strength;
        StrengthLabel.Text = result.Label;
        StrengthBar.Foreground = FortivaControlTheme.GetPasswordStrengthBrush(result.Strength, this);
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ThemeService.ApplyToElement(this);
            _tagPicker.ApplyTheme(this);
            if (!string.IsNullOrEmpty(PasswordBox.Password))
                UpdateStrength(PasswordBox.Password);
        });
    }

    private async void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        var generated = await PasswordGeneratorDialog.ShowAsync(Content.XamlRoot, _vm, themeHost: this);
        if (generated is null) return;
        PasswordBox.Password = generated.Password;
        UpdateStrength(generated.Password);
        if (generated.Tags.Count > 0)
            _tagPicker.SetSelectedTags(generated.Tags);
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
        if (string.IsNullOrEmpty(UsernameBox.Text))
            return;
        try
        {
            _clipboard.CopyText(UsernameBox.Text);
            _vm.ResetAutoLock();
            _vm.StatusMessage = "Username copied — clipboard will clear automatically.";
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowErrorAsync(ex.Message);
        }
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordBox.Password)) return;
        try
        {
            _clipboard.CopyPassword(PasswordBox.Password);
            _vm.ResetAutoLock();
            _vm.StatusMessage = "Password copied — clipboard will clear automatically.";
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowErrorAsync(ex.Message);
        }
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
        VaultEntryWebsite.NormalizeWebsite(entry);
        entry.IsFavorite = FavoriteBtn.IsChecked ?? false;
        entry.IsSecureNote = SecureNoteToggle.IsOn;
        entry.Tags = _tagPicker.GetSelectedTags().Take(VaultTagHelper.MaxTagsPerEntry).ToList();

        try
        {
            if (_vm.CanUseTotp)
            {
                entry.TotpSecret = string.IsNullOrWhiteSpace(TotpSecretBox.Password)
                    ? null
                    : TotpSecretNormalizer.Normalize(TotpSecretBox.Password);
            }

            if (_isNew)
                _vm.AddEntry(entry);
            else
                _vm.UpdateEntry(entry);
            CaptureBaseline();
            FinishEditor(saved: true);
        }
        catch (VaultConcurrencyException)
        {
            _ = ShowErrorAsync(
                "Another Fortiva window saved changes to this vault first. Close other windows, reload the vault, and try again.");
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
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot, themeHost: this);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                _vm.DeleteEntry(_existing.Id);
                FinishEditor(saved: false);
            }
            catch (Exception ex)
            {
                _ = ShowErrorAsync(ex.Message);
            }
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_paneHost is not null)
        {
            await _paneHost.TryCloseAsync();
            return;
        }

        FinishEditor(saved: false);
    }

    private void FinishEditor(bool saved)
    {
        if (_paneHost is not null)
        {
            if (saved)
                _paneHost.NotifySaved();
            else
                _paneHost.Close();
            return;
        }

        NavigationService.Current.GoBack();
    }

    private async Task ShowErrorAsync(string msg)
    {
        var dlg = new ContentDialog
        {
            Title = "Error",
            Content = new TextBlock { Text = msg },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot, themeHost: this);
        await dlg.ShowAsync();
    }
}
