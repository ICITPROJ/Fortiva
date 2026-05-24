using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Hello;
using Fortiva.Core.Password;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Fortiva.AppHost.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly WindowsHelloKeyProtector _hello;

    private int _autoLockSeconds = 300;
    private int _clipboardSeconds = 30;

    public SettingsPage()
    {
        InitializeComponent();
        _hello = new WindowsHelloKeyProtector(
            FortivaPaths.GetHelloDataDirectory(_vm.IsEnterprise),
            _vm.IsEnterprise);
        LoadLogo();

        // Slider ranges set here, never in XAML (XBF constraint ordering bug).
        // Always set Maximum BEFORE Minimum — default Maximum is 100, so setting
        // Minimum=30 first is safe due to coercion, but Maximum first is unambiguous.
        AutoLockSlider.Maximum       = 900;
        AutoLockSlider.Minimum       = 30;
        AutoLockSlider.StepFrequency = 30;

        ClipboardSlider.Maximum       = 120;
        ClipboardSlider.Minimum       = 5;
        ClipboardSlider.StepFrequency = 5;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadSettings();
    }

    private void LoadSettings()
    {
        var policy = _vm.Policy;

        _autoLockSeconds = policy?.MaxAutoLockSeconds ?? _vm.PersonalSettings.AutoLockSeconds;
        _clipboardSeconds = policy?.ClipboardClearSeconds ?? _vm.PersonalSettings.ClipboardClearSeconds;

        // Temporarily detach events to avoid spurious updates while setting Values
        AutoLockSlider.ValueChanged -= AutoLock_Changed;
        ClipboardSlider.ValueChanged -= Clipboard_Changed;

        AutoLockSlider.Value = Math.Clamp(_autoLockSeconds, 30, 900);
        ClipboardSlider.Value = Math.Clamp(_clipboardSeconds, 5, 120);

        AutoLockSlider.ValueChanged += AutoLock_Changed;
        ClipboardSlider.ValueChanged += Clipboard_Changed;

        AutoLockLabel.Text = FormatSeconds(_autoLockSeconds);
        ClipboardLabel.Text = $"Clipboard clears after {_clipboardSeconds} seconds";
        LoadThemeCombo();
        ParanoiaModeSwitch.IsOn = policy?.MandatoryParanoiaMode == true || _vm.PersonalSettings.ParanoiaMode;
        ParanoiaModeSwitch.IsEnabled = policy?.MandatoryParanoiaMode != true;

        if (policy is not null)
        {
            AutoLockSlider.IsEnabled = false;
            AutoLockLabel.Text += " (set by policy)";
            ClipboardSlider.IsEnabled = !policy.ClipboardDisabled;
        }

        HelloStatus.Text = _hello.IsConfigured
            ? "Windows Hello is configured."
            : "Windows Hello is not configured.";

        var canChangeSecrets = _vm.IsUnlocked && !_vm.IsReadOnly;
        CurrentPwd.IsEnabled = canChangeSecrets;
        NewPwd.IsEnabled = canChangeSecrets;
        NewPwdConfirm.IsEnabled = canChangeSecrets;
        ChangePasswordBtn.IsEnabled = canChangeSecrets;
        SetupHelloBtn.IsEnabled = canChangeSecrets;
        RemoveHelloBtn.IsEnabled = _hello.IsConfigured;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AboutAppName.Text   = $"Fortiva {App.Edition}";
        AboutVersion.Text   = $"Version {version?.ToString(3) ?? "1.0.0"}";
        AboutPublisher.Text = "Published by icmclab studio";
        RefreshAboutLogo();

        var showUpdates = !_vm.IsEnterprise && !_vm.IsAdmin;
        UpdatesSection.Visibility = showUpdates ? Visibility.Visible : Visibility.Collapsed;
        UpdatesDivider.Visibility = showUpdates ? Visibility.Visible : Visibility.Collapsed;
        if (showUpdates)
        {
            AutoUpdateSwitch.IsOn = _vm.PersonalSettings.AutoUpdateEnabled;
            UpdateFeedText.Text = $"Update feed: {ReleaseManifestUrls.PersonalLatest}";
            UpdateStatusText.Text = _vm.PersonalSettings.LastUpdateCheckUtc is null
                ? "Automatic update check on launch (once per day)."
                : $"Last checked { _vm.PersonalSettings.LastUpdateCheckUtc:yyyy-MM-dd HH:mm} UTC.";
        }

        var ctx = _vm.Context;
        VaultInfoText.Text = ctx is null
            ? "Vault not unlocked."
            : $"Vault ID: {ctx.Header.VaultId}\n" +
              $"Created:  {ctx.Header.CreatedAt:yyyy-MM-dd HH:mm} UTC\n" +
              $"Modified: {ctx.Header.LastModifiedAt:yyyy-MM-dd HH:mm} UTC\n" +
              $"Revision: {ctx.Header.RevisionCounter}\n" +
              $"Level:    {ctx.Header.SecurityLevel}\n" +
              $"Argon2 memory: {ctx.Header.KdfParameters.MemoryKb / 1024} MB\n" +
              $"Argon2 iters:  {ctx.Header.KdfParameters.Iterations}\n" +
              $"Entries:  {ctx.Payload.Entries.Count}";

        RefreshPortableUi();
    }

    private void RefreshPortableUi()
    {
        var showPortable = !_vm.IsEnterprise && !_vm.IsAdmin;
        PortableSection.Visibility = showPortable ? Visibility.Visible : Visibility.Collapsed;
        if (!showPortable)
            return;

        var allowed = PolicyEnforcer.CanUsePortableMode(_vm.Policy);
        PortableBtn.IsEnabled = allowed;
        LocalVaultBtn.IsEnabled = allowed;
        LocalVaultBtn.Visibility = _vm.IsPortableMode ? Visibility.Visible : Visibility.Collapsed;

        if (_vm.IsPortableMode)
        {
            PortableStatusText.Text = _vm.VaultLocationLabel;
        }
        else
        {
            var saved = _vm.PersonalSettings.PortableVaultDirectory;
            PortableStatusText.Text = string.IsNullOrWhiteSpace(saved)
                ? _vm.VaultLocationLabel
                : Directory.Exists(saved)
                    ? $"Local vault active. Saved USB location: {saved}"
                    : $"Local vault active. USB vault not connected ({saved}).";
        }

        if (!allowed)
            PortableStatusText.Text += "\nPortable mode is disabled by policy.";
    }

    private void RefreshAboutLogo()
        => BrandAssets.ApplyLogo(AboutLogo, _vm.PreferParanoiaMode);

    private void LoadLogo()
    {
        RefreshAboutLogo();
    }

    private void ParanoiaMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_vm.Policy?.MandatoryParanoiaMode == true) return;
        _vm.SetParanoiaMode(ParanoiaModeSwitch.IsOn);
        RefreshAboutLogo();
    }

    private void LoadThemeCombo()
    {
        ThemeCombo.SelectionChanged -= Theme_Changed;
        var tag = ThemeService.ToTag(_vm.ThemePreference);
        ThemeCombo.SelectedIndex = tag switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        ThemeCombo.SelectionChanged += Theme_Changed;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ComboBoxItem item)
            return;
        _vm.SetThemePreference(ThemeService.Parse(item.Tag?.ToString()));
    }

    private void AutoLock_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _autoLockSeconds = (int)e.NewValue;
        AutoLockLabel.Text = FormatSeconds(_autoLockSeconds);
        if (AutoLockSlider.IsEnabled)
            _vm.SetAutoLockTimeout(_autoLockSeconds);
    }

    private void Clipboard_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _clipboardSeconds = (int)e.NewValue;
        ClipboardLabel.Text = $"Clipboard clears after {_clipboardSeconds} seconds";
        if (ClipboardSlider.IsEnabled)
            _vm.SetClipboardClearSeconds(_clipboardSeconds);
    }

    private void NewPwd_Changed(object sender, RoutedEventArgs e)
    {
        var result = _vm.AnalyzeStrength(NewPwd.Password);
        NewPwdStrength.Value = (int)result.Strength;
        NewPwdStrengthLabel.Text = result.Label;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked) { ShowInfo("Unlock the vault before changing your master password."); return; }
        if (_vm.IsReadOnly) { ShowInfo("Vault is read-only. Confirm rollback on the unlock screen first.", InfoBarSeverity.Warning); return; }
        if (string.IsNullOrEmpty(CurrentPwd.Password)) { ShowInfo("Enter current password."); return; }
        if (string.IsNullOrEmpty(NewPwd.Password))     { ShowInfo("Enter a new password.");    return; }
        if (NewPwd.Password != NewPwdConfirm.Password) { ShowInfo("New passwords do not match."); return; }

        var strength = _vm.AnalyzeStrength(NewPwd.Password);
        if (strength.Strength < PasswordStrength.Fair)
        {
            ShowInfo("New password is too weak. " + (strength.Suggestions.FirstOrDefault() ?? ""), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            if (!_vm.VerifyMasterPassword(CurrentPwd.Password))
            {
                ShowInfo("Current master password is incorrect.", InfoBarSeverity.Error);
                return;
            }

            _vm.ChangeMasterPassword(NewPwd.Password);

            // Keep Windows Hello in sync with the new master password
            if (_hello.IsConfigured)
                _vm.SyncHelloCredential(NewPwd.Password);

            ShowInfo("Master password changed successfully.", InfoBarSeverity.Success);
            CurrentPwd.Password = NewPwd.Password = NewPwdConfirm.Password = "";
            NewPwdStrength.Value = 0;
            NewPwdStrengthLabel.Text = "";
        }
        catch (Exception ex)
        {
            ShowInfo($"Failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void SetupHello_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked) { ShowInfo("Unlock the vault before setting up Windows Hello."); return; }
        if (_vm.IsReadOnly) { ShowInfo("Vault is read-only. Confirm rollback on the unlock screen first.", InfoBarSeverity.Warning); return; }
        var pwdBox = new PasswordBox { PlaceholderText = "Enter your current master password" };
        var desc   = new TextBlock
        {
            Text = "Enter your master password to bind Windows Hello. " +
                   "After setup you can unlock with face, fingerprint, or PIN.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
            Opacity = 0.8,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12)
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(desc);
        panel.Children.Add(pwdBox);

        var dialog = new ContentDialog
        {
            Title               = "Set up Windows Hello",
            Content             = panel,
            PrimaryButtonText   = "Continue",
            SecondaryButtonText = "Cancel",
            XamlRoot            = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrEmpty(pwdBox.Password))
        {
            ShowInfo("Master password is required.", InfoBarSeverity.Error);
            return;
        }

        if (!_vm.VerifyMasterPassword(pwdBox.Password))
        {
            ShowInfo("Master password is incorrect.", InfoBarSeverity.Error);
            return;
        }

        var helloResult = await HelloService.VerifyAsync("Fortiva — set up Windows Hello");
        if (!helloResult.Verified)
        {
            ShowInfo(helloResult.ErrorMessage ?? "Windows Hello verification failed.", InfoBarSeverity.Error);
            return;
        }

        _vm.SyncHelloCredential(pwdBox.Password);
        HelloStatus.Text = "Windows Hello is configured.";
        RemoveHelloBtn.IsEnabled = true;
        ShowInfo("Windows Hello set up. You can unlock with face, fingerprint, or PIN.", InfoBarSeverity.Success);
    }

    private void RemoveHello_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearHelloCredential();
        HelloStatus.Text = "Windows Hello not configured.";
        RemoveHelloBtn.IsEnabled = false;
        ShowInfo("Windows Hello credential removed.");
    }

    private void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
        => _vm.SetAutoUpdateEnabled(AutoUpdateSwitch.IsOn);

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateBusyRing.IsActive = true;
        try
        {
            var result = await UpdateService.Current.CheckAsync();
            UpdateStatusText.Text = UpdateService.FormatUpdateStatus(result);

            if (result.Status is UpdateStatus.UpdateAvailable or UpdateStatus.PlatformUntested
                && result.IsOnlineManifest)
            {
                var dlg = new ContentDialog
                {
                    Title = "Install update?",
                    Content = new TextBlock
                    {
                        Text = $"{result.Message}\n\nFortiva will close and install the update. Your vault is not affected.",
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    PrimaryButtonText = "Install now",
                    CloseButtonText = "Later",
                    XamlRoot = XamlRoot
                };
                if (await dlg.ShowAsync() == ContentDialogResult.Primary && result.Manifest is not null)
                    await UpdateService.Current.ApplyAsync(result.Manifest, silent: false);
            }
            else
            {
                var severity = result.Status switch
                {
                    UpdateStatus.CheckFailed => InfoBarSeverity.Warning,
                    UpdateStatus.UpdateAvailable or UpdateStatus.PlatformUntested => InfoBarSeverity.Informational,
                    _ => InfoBarSeverity.Success
                };
                ShowInfo(UpdateService.FormatUpdateStatus(result), severity);
            }
        }
        finally
        {
            UpdateBusyRing.IsActive = false;
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private async void Portable_Click(object sender, RoutedEventArgs e)
    {
        if (!PolicyEnforcer.CanUsePortableMode(_vm.Policy))
        {
            ShowInfo("Portable mode is disabled by policy.", InfoBarSeverity.Warning);
            return;
        }

        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        if (!FortivaPaths.TryResolvePortableVaultDirectory(folder.Path, out var vaultDirectory))
        {
            if (!FortivaPaths.TryGetPortableVaultCreateDirectory(folder.Path, out vaultDirectory))
            {
                ShowInfo(
                    "No vault.fva found and this location cannot be used for a new vault.",
                    InfoBarSeverity.Warning);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Create vault on USB?",
                Content = $"No existing vault was found.\n\nCreate a new vault at:\n{vaultDirectory}",
                PrimaryButtonText = "Create here",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                _vm.PreparePortableVaultLocation(vaultDirectory);
                RefreshPortableUi();
                ShowInfo(
                    $"Portable location set at {vaultDirectory}. Complete setup on the next screen.",
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo(ex.Message, InfoBarSeverity.Error);
            }
            return;
        }

        try
        {
            _vm.SwitchToPortableVault(vaultDirectory);
            RefreshPortableUi();
            ShowInfo($"Portable vault loaded from {vaultDirectory}. Unlock to continue.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void LocalVault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.SwitchToLocalVault();
            RefreshPortableUi();
            ShowInfo("Switched back to your local vault. Unlock to continue.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowInfo(string msg, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        SettingsInfo.Message  = msg;
        SettingsInfo.Severity = severity;
        SettingsInfo.IsOpen   = true;
    }

    private static string FormatSeconds(int sec) =>
        sec < 60 ? $"{sec} seconds" : $"{sec / 60} minute{(sec / 60 == 1 ? "" : "s")}";
}
