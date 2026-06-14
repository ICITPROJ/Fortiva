using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Platform;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;
using Fortiva.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;

namespace Fortiva.AppHost.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private HelloUnlockManager Hello => new(_vm.HelloDataDirectory, _vm.IsEnterprise);

    private int _autoLockSeconds = 300;
    private int _clipboardSeconds = 30;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autoLockSaveTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clipboardSaveTimer;
    private Action? _stateChangedHandler;

    public SettingsPage()
    {
        InitializeComponent();
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
        ThemeService.ApplyToElement(this);
        _stateChangedHandler ??= () => DispatcherQueue.TryEnqueue(RefreshBrowserExtensionUi);
        _vm.StateChanged += _stateChangedHandler;
        _bridgeHealthCheckedThisVisit = false;
        LoadSettings();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_stateChangedHandler is not null)
            _vm.StateChanged -= _stateChangedHandler;
    }

    private void LoadSettings()
    {
        var policy = _vm.Policy;

        _autoLockSeconds = Math.Clamp(
            _vm.IsEnterprise && policy is not null
                ? policy.MaxAutoLockSeconds
                : _vm.PersonalSettings.AutoLockSeconds,
            PersonalUserSettings.MinAutoLockSeconds,
            PersonalUserSettings.MaxAutoLockSeconds);
        _clipboardSeconds = Math.Clamp(
            _vm.IsEnterprise && policy is not null
                ? policy.ClipboardClearSeconds
                : _vm.PersonalSettings.ClipboardClearSeconds,
            PersonalUserSettings.MinClipboardClearSeconds,
            PersonalUserSettings.MaxClipboardClearSeconds);

        // Temporarily detach events to avoid spurious updates while setting Values
        AutoLockSlider.ValueChanged -= AutoLock_Changed;
        ClipboardSlider.ValueChanged -= Clipboard_Changed;

        AutoLockSlider.Value = _autoLockSeconds;
        ClipboardSlider.Value = _clipboardSeconds;

        AutoLockSlider.ValueChanged += AutoLock_Changed;
        ClipboardSlider.ValueChanged += Clipboard_Changed;

        AutoLockLabel.Text = FormatSeconds(_autoLockSeconds);
        ClipboardLabel.Text = $"Clipboard clears after {_clipboardSeconds} seconds";
        SettingsSubtitle.Text = _vm.IsEnterprise
            ? "Security, browser extension, and enterprise policy."
            : "Appearance, security, browser Fill, and updates.";
        LoadThemeCombo();
        ParanoiaModeSwitch.Toggled -= ParanoiaMode_Toggled;
        ParanoiaModeSwitch.IsOn = policy?.MandatoryParanoiaMode == true || _vm.PersonalSettings.ParanoiaMode;
        ParanoiaModeSwitch.IsEnabled = policy?.MandatoryParanoiaMode != true;
        ParanoiaModeSwitch.Toggled += ParanoiaMode_Toggled;

        if (policy is not null)
        {
            AutoLockSlider.IsEnabled = false;
            AutoLockLabel.Text += " (set by policy)";
            ClipboardSlider.IsEnabled = !policy.ClipboardDisabled;
        }

        HelloStatus.Text = Hello.IsConfigured
            ? Hello.IsHardwareBacked
                ? "Windows Hello is configured (hardware-backed TPM)."
                : "Windows Hello is configured (software protection — upgrade to TPM when available)."
            : "Windows Hello is not configured.";
        _ = RefreshHelloUpgradeBannerAsync();

        var canChangeSecrets = _vm.IsUnlocked && !_vm.IsReadOnly;
        CurrentPwd.IsEnabled = canChangeSecrets;
        NewPwd.IsEnabled = canChangeSecrets;
        NewPwdConfirm.IsEnabled = canChangeSecrets;
        ChangePasswordBtn.IsEnabled = canChangeSecrets;
        SetupHelloBtn.IsEnabled = canChangeSecrets;
        RemoveHelloBtn.IsEnabled = Hello.IsConfigured;

        AboutAppName.Text   = $"Fortiva {App.Edition}";
        AboutVersion.Text   = $"Version {AppVersion.Current}";
        AboutPublisher.Text = "Published by icmclab studio";
        RefreshAboutLogo();

        var showUpdates = !_vm.IsEnterprise && !_vm.IsAdmin;
        UpdatesSection.Visibility = showUpdates ? Visibility.Visible : Visibility.Collapsed;
        UpdatesDivider.Visibility = showUpdates ? Visibility.Visible : Visibility.Collapsed;
        if (showUpdates)
        {
            AutoUpdateSwitch.Toggled -= AutoUpdate_Toggled;
            AutoUpdateSwitch.IsOn = _vm.PersonalSettings.AutoUpdateEnabled;
            AutoUpdateSwitch.Toggled += AutoUpdate_Toggled;
            UpdateFeedText.Text = $"Installed: v{AppVersion.Current} · Feed: {ReleaseManifestUrls.PersonalLatest}";
            UpdateStatusText.Text = _vm.PersonalSettings.LastUpdateCheckUtc is null
                ? "Automatic update check on launch (once per day)."
                : $"Last checked { _vm.PersonalSettings.LastUpdateCheckUtc:yyyy-MM-dd HH:mm} UTC.";
            RefreshUpdateApplyFailureBanner();
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
        RefreshSharedVaultUi();
        try
        {
            RefreshBrowserExtensionUi();
        }
        catch (Exception ex)
        {
            App.LogException("SettingsPage.LoadSettings.RefreshBrowserExtensionUi", ex);
            BrowserExtensionHealthHeadline.Text = "Browser extension status unavailable";
            BrowserExtensionStatusText.Text = "Open Settings again after unlock, or restart Fortiva.";
        }
    }

    private void RefreshBrowserExtensionUi()
    {
        var show = !_vm.IsAdmin;
        BrowserExtensionSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BrowserExtensionDivider.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        var status = BrowserExtensionSetupHelper.GetStatus(_vm);

        var readiness = BrowserExtensionSetupHelper.GetFillReadiness(_vm);
        var (headline, detail, glyph) = BrowserExtensionSetupHelper.DescribeFillReadiness(readiness);
        BrowserExtensionHealthHeadline.Text = headline;
        BrowserExtensionStatusText.Text = detail;
        BridgeHealthIcon.Glyph = glyph;
        BridgeHealthIcon.Foreground = readiness switch
        {
            BrowserExtensionSetupHelper.BridgeFillReadiness.Ready => FortivaThemeResources.StatusSuccess,
            BrowserExtensionSetupHelper.BridgeFillReadiness.VaultLocked or
            BrowserExtensionSetupHelper.BridgeFillReadiness.BridgeStarting => FortivaThemeResources.StatusWarning,
            BrowserExtensionSetupHelper.BridgeFillReadiness.FilesMissing => FortivaThemeResources.StatusError,
            _ => FortivaControlTheme.GetBrush("FortivaAccentBrush", null, BridgeHealthIcon),
        };

        ConnectBrowserBtn.IsEnabled = readiness != BrowserExtensionSetupHelper.BridgeFillReadiness.PolicyManaged;
        RestartBridgeBtn.IsEnabled = _vm.IsUnlocked;
        OpenExtensionsBtn.IsEnabled = status.IsReadyForBrowser;

        var browser = BrowserExtensionSetupHelper.DetectPreferredBrowser();
        var browserName = browser == BrowserExtensionSetupHelper.SupportedBrowser.Chrome ? "Chrome" : "Edge";
        OpenExtensionsBtn.Content = $"Open {browserName} extensions";

        var pathLines = status.ExtensionFilesReady
            ? $"Extension folder: {status.ExtensionStagingPath}"
            : status.ExtensionSourcePath is not null
                ? $"Will copy extension from: {status.ExtensionSourcePath}"
                : "Extension folder not prepared yet.";

        if (BrowserExtensionSetupHelper.ExtensionVersionNeedsReload(_vm, out var extVer, out var appVer))
        {
            pathLines += $"\nReload Fortiva Autofill in {browserName} — extension v{extVer}, app v{appVer}.";
        }

        BrowserExtensionPathText.Text = pathLines;

        if (_vm.IsUnlocked
            && readiness != BrowserExtensionSetupHelper.BridgeFillReadiness.Ready
            && !_bridgeHealthCheckedThisVisit)
        {
            _bridgeHealthCheckedThisVisit = true;
            ScheduleBridgeHealthRefresh();
        }
    }

    private bool _bridgeHealthRefreshPending;
    private bool _bridgeHealthCheckedThisVisit;

    private void ScheduleBridgeHealthRefresh()
    {
        if (_bridgeHealthRefreshPending)
            return;
        _bridgeHealthRefreshPending = true;

        _ = Task.Run(async () =>
        {
            try { await _vm.ReconcileBridgeLifecycleAsync("SettingsHealthRefresh").ConfigureAwait(false); }
            catch (Exception ex) { App.LogException("SettingsPage.ReconcileBridgeLifecycle", ex); }
        }).ContinueWith(_ =>
        {
            _bridgeHealthRefreshPending = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                try { RefreshBrowserExtensionUi(); }
                catch (Exception ex) { App.LogException("SettingsPage.RefreshBrowserExtensionUi.afterBridge", ex); }
            });
        }, TaskScheduler.Default);
    }

    private async void RestartBridge_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked)
        {
            ShowInfo("Unlock the vault before restarting the browser bridge.", InfoBarSeverity.Warning);
            return;
        }

        RestartBridgeBtn.IsEnabled = false;
        try
        {
            await _vm.RestartBridgeInfrastructureAsync();
            RefreshBrowserExtensionUi();
            var browser = BrowserExtensionSetupHelper.DetectPreferredBrowser();
            var browserName = browser == BrowserExtensionSetupHelper.SupportedBrowser.Chrome ? "Chrome" : "Edge";
            ShowInfo($"Browser bridge restarted. Reload the Fortiva extension in {browserName} if Fill still fails.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Error);
        }
        finally
        {
            RestartBridgeBtn.IsEnabled = _vm.IsUnlocked;
        }
    }

    private async void OpenExtensions_Click(object sender, RoutedEventArgs e)
    {
        OpenExtensionsBtn.IsEnabled = false;
        try
        {
            var browser = BrowserExtensionSetupHelper.DetectPreferredBrowser();
            await BrowserExtensionSetupHelper.OpenBrowserExtensionsAsync(browser);
            var browserName = browser == BrowserExtensionSetupHelper.SupportedBrowser.Chrome ? "Chrome" : "Edge";
            ShowInfo($"In {browserName}, find Fortiva Autofill and click Reload after Fortiva updates.",
                InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Warning);
        }
        finally
        {
            OpenExtensionsBtn.IsEnabled = true;
        }
    }

    private async void ConnectBrowser_Click(object sender, RoutedEventArgs e)
    {
        ConnectBrowserBtn.IsEnabled = false;
        try
        {
            var result = await BrowserExtensionSetupHelper.ConnectBrowserAsync(_vm, XamlRoot);
            RefreshBrowserExtensionUi();
            if (!result.Success)
            {
                ShowInfo(result.Error ?? "Browser setup failed.", InfoBarSeverity.Error);
                return;
            }

            var browser = result.Browser == BrowserExtensionSetupHelper.SupportedBrowser.Chrome ? "Chrome" : "Edge";
            var reloadHint = BrowserExtensionSetupHelper.ExtensionVersionNeedsReload(_vm, out var extVer, out var appVer)
                ? $" Reload Fortiva Autofill in {browser} (extension v{extVer}, app v{appVer})."
                : "";
            var msg = result.Mode switch
            {
                BrowserExtensionSetupHelper.ExtensionConnectMode.AutoLoaded =>
                    $"Fortiva opened {browser} with the extension. On a login page, click the Fortiva icon → Fill.{reloadHint}",
                BrowserExtensionSetupHelper.ExtensionConnectMode.PolicyManaged =>
                    "IT policy will install the browser extension. Restart Chrome or Edge if the Fortiva icon is not visible yet.",
                _ =>
                    $"Fortiva opened {browser} and the extension folder. Turn on Developer mode, click Load unpacked, and select the folder that opened."
            };
            ShowInfo(msg, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Error);
            RefreshBrowserExtensionUi();
        }
        finally
        {
            ConnectBrowserBtn.IsEnabled = true;
        }
    }

    private void RefreshSharedVaultUi()
    {
        var showShared = _vm.IsEnterprise && !_vm.IsAdmin;
        SharedVaultSection.Visibility = showShared ? Visibility.Visible : Visibility.Collapsed;
        SharedVaultSectionDivider.Visibility = showShared ? Visibility.Visible : Visibility.Collapsed;
        if (!showShared)
            return;

        _vm.ReloadSharedVaults();
        SharedVaultBox.SelectionChanged -= SharedVault_Changed;

        var items = new List<ComboBoxItem>
        {
            new() { Content = "Default organization vault", Tag = FortivaPaths.EnterpriseProgramData }
        };
        foreach (var vault in _vm.SharedVaults)
        {
            items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(vault.Name) ? vault.StoragePath : vault.Name,
                Tag = vault.StoragePath
            });
        }

        SharedVaultBox.ItemsSource = items;
        var selectedPath = _vm.VaultDirectory;
        var match = items.FirstOrDefault(i =>
            string.Equals(i.Tag?.ToString(), selectedPath, StringComparison.OrdinalIgnoreCase));
        SharedVaultBox.SelectedItem = match ?? items[0];
        SharedVaultBox.SelectionChanged += SharedVault_Changed;
        SharedVaultStatusText.Text = _vm.VaultLocationLabel;
    }

    private void SharedVault_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SharedVaultBox.SelectedItem is not ComboBoxItem item)
            return;
        var path = item.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (string.Equals(path, _vm.VaultDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (_vm.IsUnlocked)
            {
                ShowInfo("Lock the vault before switching shared vault locations.", InfoBarSeverity.Warning);
                RefreshSharedVaultUi();
                return;
            }

            _vm.SwitchEnterpriseVault(path);
            SharedVaultStatusText.Text = _vm.VaultLocationLabel;
            ShowInfo($"Active vault: {_vm.VaultLocationLabel}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Error);
            RefreshSharedVaultUi();
        }
    }

    private void RefreshPortableUi()
    {
        var showPortable = !_vm.IsEnterprise && !_vm.IsAdmin;
        PortableSection.Visibility = showPortable ? Visibility.Visible : Visibility.Collapsed;
        PortableSectionDivider.Visibility = showPortable ? Visibility.Visible : Visibility.Collapsed;
        if (!showPortable)
            return;

        var allowed = PolicyEnforcer.CanUsePortableMode(_vm.Policy);
        PortableBtn.IsEnabled = allowed;
        LocalVaultBtn.IsEnabled = allowed;
        LocalVaultBtn.Visibility = _vm.IsPortableMode ? Visibility.Visible : Visibility.Collapsed;

        var canSync = allowed && _vm.CanSyncWithCounterpart;
        SyncBtn.IsEnabled = canSync;
        SyncBtn.Visibility = allowed && !string.IsNullOrWhiteSpace(_vm.CounterpartVaultDirectory)
            ? Visibility.Visible
            : Visibility.Collapsed;

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

        var localDir = _vm.VaultDirectory;
        var otherDir = _vm.CounterpartVaultDirectory;
        if (!string.IsNullOrWhiteSpace(otherDir)
            && (VaultSyncMarker.Exists(localDir) || VaultSyncMarker.Exists(otherDir)))
        {
            var marker = VaultSyncMarker.Read(localDir) ?? VaultSyncMarker.Read(otherDir);
            PortableStatusText.Text += "\n\nWarning: " + (marker?.Message
                ?? "A previous sync did not finish cleanly. Verify both vault copies, then use Sync.");
        }
    }

    private void RefreshAboutLogo()
        => BrandAssets.ApplyLogo(AboutLogo, _vm.PreferParanoiaMode);

    private async void WebsiteLink_Click(object sender, RoutedEventArgs e)
    {
        if (!SafeUriLauncher.TryNormalizeHttpUri(BrandAssets.WebsiteUrl, out var uri))
            return;

        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            SettingsInfo.Message = "Could not open the Fortiva website.";
            SettingsInfo.Severity = InfoBarSeverity.Error;
            SettingsInfo.IsOpen = true;
        }
    }

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
            ScheduleDebouncedSave(ref _autoLockSaveTimer, () => _vm.SetAutoLockTimeout(_autoLockSeconds));
    }

    private void Clipboard_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _clipboardSeconds = (int)e.NewValue;
        ClipboardLabel.Text = $"Clipboard clears after {_clipboardSeconds} seconds";
        if (ClipboardSlider.IsEnabled)
            ScheduleDebouncedSave(ref _clipboardSaveTimer, () => _vm.SetClipboardClearSeconds(_clipboardSeconds));
    }

    private void ScheduleDebouncedSave(ref Microsoft.UI.Dispatching.DispatcherQueueTimer? timer, Action save)
    {
        timer ??= CreateDebounceTimer(save);
        timer.Stop();
        timer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateDebounceTimer(Action save)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            save();
        };
        return timer;
    }

    private void NewPwd_Changed(object sender, RoutedEventArgs e)
    {
        var result = _vm.AnalyzeStrength(NewPwd.Password);
        NewPwdStrength.Value = (int)result.Strength;
        NewPwdStrengthLabel.Text = result.Label;
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
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

        ChangePasswordBtn.IsEnabled = false;
        try
        {
            if (!_vm.VerifyMasterPassword(CurrentPwd.Password))
            {
                ShowInfo("Current master password is incorrect.", InfoBarSeverity.Error);
                return;
            }

            await _vm.ChangeMasterPasswordAsync(NewPwd.Password);

            if (Hello.IsConfigured)
            {
                try
                {
                    await _vm.SyncHelloCredentialFromSessionAsync();
                }
                catch (Exception helloEx)
                {
                    ShowInfo(
                        "Master password changed, but Windows Hello was not re-bound: "
                        + App.DescribeException(helloEx)
                        + " Re-enable Hello in Settings when ready.",
                        InfoBarSeverity.Warning);
                }
            }

            ShowInfo("Master password changed successfully.", InfoBarSeverity.Success);
            CurrentPwd.Password = NewPwd.Password = NewPwdConfirm.Password = "";
            NewPwdStrength.Value = 0;
            NewPwdStrengthLabel.Text = "";
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            ShowInfo($"Failed: {detail}", InfoBarSeverity.Error);
        }
        finally
        {
            ChangePasswordBtn.IsEnabled = true;
        }
    }

    private async void SetupHello_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked) { ShowInfo("Unlock the vault before setting up Windows Hello."); return; }
        if (_vm.IsReadOnly) { ShowInfo("Vault is read-only. Confirm rollback on the unlock screen first.", InfoBarSeverity.Warning); return; }
        SetupHelloBtn.IsEnabled = false;
        var pwdBox = new PasswordBox { PlaceholderText = "Enter your current master password" };
        var desc   = new TextBlock
        {
            Text = "Enter your master password once, then approve Windows Hello when prompted. " +
                   "After setup you can unlock with face, fingerprint, or PIN.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12)
        };
        FortivaControlTheme.ApplyBodyText(desc);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(desc);
        panel.Children.Add(pwdBox);
        FortivaControlTheme.ApplyPasswordBox(pwdBox);

        var dialog = new ContentDialog
        {
            Title               = "Set up Windows Hello",
            Content             = panel,
            PrimaryButtonText   = "Continue",
            SecondaryButtonText = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = XamlRoot
        };

        FortivaDialogs.Configure(dialog, XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            SetupHelloBtn.IsEnabled = true;
            return;
        }
        if (string.IsNullOrEmpty(pwdBox.Password))
        {
            ShowInfo("Master password is required.", InfoBarSeverity.Error);
            SetupHelloBtn.IsEnabled = true;
            return;
        }

        if (!_vm.VerifyMasterPassword(pwdBox.Password))
        {
            ShowInfo("Master password is incorrect.", InfoBarSeverity.Error);
            SetupHelloBtn.IsEnabled = true;
            return;
        }

        try
        {
            await _vm.SyncHelloCredentialAsync(pwdBox.Password);
            HelloStatus.Text = Hello.IsHardwareBacked
                ? "Windows Hello is configured (hardware-backed)."
                : "Windows Hello is configured.";
            HelloUpgradeInfo.IsOpen = false;
            RemoveHelloBtn.IsEnabled = true;
            ShowInfo("Windows Hello set up. You can unlock with face, fingerprint, or PIN.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Error);
        }
        finally
        {
            SetupHelloBtn.IsEnabled = true;
        }
    }

    private async void RemoveHello_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsEnterprise && _vm.Policy?.MandatoryWindowsHello == true)
        {
            ShowInfo("Your organization requires Windows Hello. Removing Hello is not allowed.", InfoBarSeverity.Warning);
            return;
        }

        await _vm.ClearHelloCredentialAsync();
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
                        Text = $"{result.Message}\n\nFortiva will close, install the update silently, and reopen automatically. "
                            + "Your vault, Windows Hello, and settings stay in place. "
                            + $"An encrypted backup copy is saved locally before install (last {PreUpdateVaultBackup.MaxRetainedBackups} kept in %LocalAppData%\\FortivaPersonal\\pre-update-backups).",
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    PrimaryButtonText = "Install now",
                    CloseButtonText = "Later",
                    XamlRoot = XamlRoot
                };
                FortivaDialogs.Configure(dlg, XamlRoot);
                if (await dlg.ShowAsync() == ContentDialogResult.Primary && result.Manifest is not null)
                {
                    try
                    {
                        await UpdateService.Current.ApplyAsync(result.Manifest, silent: false);
                    }
                    catch (Exception ex)
                    {
                        ShowInfo(UpdateMessages.ForApplyFailure(ex), InfoBarSeverity.Error);
                    }
                }
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
            FortivaDialogs.Configure(dialog, Content.XamlRoot);
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

        var confirm = new ContentDialog
        {
            Title = "Switch to portable vault?",
            Content = "Your local vault at %APPDATA%\\Fortiva is not deleted — it stays on this PC.\n\n" +
                      "Use \"Use local vault\" in Settings anytime to switch back.\n\n" +
                      $"Portable vault: {vaultDirectory}",
            PrimaryButtonText = "Switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(confirm, Content.XamlRoot);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

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

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked) { ShowInfo("Unlock the vault before syncing."); return; }
        if (_vm.IsReadOnly) { ShowInfo("Vault is read-only. Confirm rollback on the unlock screen first.", InfoBarSeverity.Warning); return; }

        var other = _vm.CounterpartVaultDirectory;
        if (string.IsNullOrWhiteSpace(other))
        {
            ShowInfo("Set up a USB/portable vault location first, then sync.", InfoBarSeverity.Warning);
            return;
        }

        var localDir = _vm.VaultDirectory;
        if (VaultSyncMarker.Exists(localDir) || VaultSyncMarker.Exists(other))
        {
            var marker = VaultSyncMarker.Read(localDir) ?? VaultSyncMarker.Read(other);
            var warn = new ContentDialog
            {
                Title = "Sync divergence warning",
                Content = (marker?.Message ?? "A previous sync did not complete cleanly.")
                    + "\n\nOnly continue if you have verified both vault copies. Sync will clear this warning.",
                PrimaryButtonText = "I've verified — continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            FortivaDialogs.Configure(warn, XamlRoot);
            if (await warn.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        var counterpartLabel = _vm.IsPortableMode ? "your local vault" : "the USB vault";
        var pwdBox = new PasswordBox { PlaceholderText = $"Master password for {counterpartLabel}" };
        var desc = new TextBlock
        {
            Text = $"Enter the master password for {counterpartLabel} ({other}). " +
                   "Entries will be merged both ways; the newest edit of each entry wins.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12)
        };
        FortivaControlTheme.ApplyBodyText(desc);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(desc);
        panel.Children.Add(pwdBox);
        FortivaControlTheme.ApplyPasswordBox(pwdBox);

        var dialog = new ContentDialog
        {
            Title = "Sync vaults",
            Content = panel,
            PrimaryButtonText = "Sync",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        FortivaDialogs.Configure(dialog, XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrEmpty(pwdBox.Password))
        {
            ShowInfo("Master password is required to sync.", InfoBarSeverity.Error);
            return;
        }

        SyncBtn.IsEnabled = false;
        try
        {
            if (VaultSyncMarker.Exists(localDir) || VaultSyncMarker.Exists(other))
                VaultSyncMarker.ClearBoth(localDir, other);

            var result = await _vm.SyncWithPortableAsync(pwdBox.Password);
            ShowInfo(
                $"Sync complete. This vault: +{result.Local.Added} added, {result.Local.Updated} updated, " +
                $"{result.Local.Removed} removed. {result.MergedTotal} entries total.",
                InfoBarSeverity.Success);
        }
        catch (UnauthorizedAccessException)
        {
            ShowInfo("Master password for the counterpart vault is incorrect.", InfoBarSeverity.Error);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            ShowInfo("Master password for the counterpart vault is incorrect.", InfoBarSeverity.Error);
        }
        catch (VaultSyncDivergedException ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        catch (VaultSyncPartialException ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo(App.DescribeException(ex), InfoBarSeverity.Error);
        }
        finally
        {
            RefreshPortableUi();
        }
    }

    private Task RefreshHelloUpgradeBannerAsync()
    {
        HelloUpgradeInfo.IsOpen = !_vm.PersonalSettings.HelloHardwareUpgradeDismissed
            && Hello.UsesSoftwareOnlyHello;
        return Task.CompletedTask;
    }

    private void HelloUpgradeInfo_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        => _vm.SetHelloHardwareUpgradeDismissed(true);

    private void RefreshUpdateApplyFailureBanner()
    {
        var error = _vm.PersonalSettings.LastUpdateApplyError;
        if (string.IsNullOrWhiteSpace(error))
        {
            UpdateApplyFailureInfo.IsOpen = false;
            return;
        }

        var when = _vm.PersonalSettings.LastUpdateApplyFailedUtc?.ToString("yyyy-MM-dd HH:mm") ?? "recently";
        UpdateApplyFailureInfo.Message = $"{error} (last attempt {when} UTC). Use Check for updates to retry.";
        UpdateApplyFailureInfo.IsOpen = true;
    }

    private void UpdateApplyFailureInfo_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        => _vm.ClearUpdateApplyFailure();

    private void ShowInfo(string msg, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        SettingsInfo.Message  = msg;
        SettingsInfo.Severity = severity;
        SettingsInfo.IsOpen   = true;
    }

    private static string FormatSeconds(int sec) =>
        sec < 60 ? $"{sec} seconds" : $"{sec / 60} minute{(sec / 60 == 1 ? "" : "s")}";
}
