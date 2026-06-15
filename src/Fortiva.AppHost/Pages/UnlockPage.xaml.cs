using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fortiva.AppHost.Pages;

public sealed partial class UnlockPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private HelloUnlockManager? _hello;
    private bool _rollbackConfirmRequired;
    private bool _helloCheckComplete;
    private bool _bridgeUnlockMode;
    private bool _autoHelloAttempted;
    private bool _passwordSectionVisible = true;

    public UnlockPage()
    {
        InitializeComponent();
        LoadLogo();
        _vm.BrandAppearanceChanged += OnBrandAppearanceChanged;
    }

    private HelloUnlockManager Hello =>
        _hello ??= new HelloUnlockManager(_vm.HelloDataDirectory, _vm.IsEnterprise);

    private void OnBrandAppearanceChanged()
        => RefreshBrandLogo();

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.BrandAppearanceChanged -= OnBrandAppearanceChanged;
        if (_bridgeUnlockMode && !_vm.IsUnlocked)
            _vm.CancelBridgeUnlockIfPending();
    }

    private bool HelloMandatory =>
        _vm.IsEnterprise && _vm.Policy?.MandatoryWindowsHello == true;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _hello = new HelloUnlockManager(_vm.HelloDataDirectory, _vm.IsEnterprise);
        _bridgeUnlockMode = e.Parameter is BridgeUnlockNavigationContext;

        if (_vm.IsUnlocked)
        {
            NavigationService.Current.Navigate<VaultPage>();
            return;
        }

        _rollbackConfirmRequired = _vm.PendingRollbackConfirm;
        if (_rollbackConfirmRequired)
        {
            RollbackBar.Message = "Confirm rollback to enable editing. Enter your master password and tap Unlock.";
            RollbackBar.IsOpen = true;
        }
        else
        {
            RollbackBar.IsOpen = false;
        }

        ErrorBar.IsOpen = false;
        PasswordField.Password = "";
        _helloCheckComplete = false;
        _autoHelloAttempted = false;
        HelloBtn.IsEnabled = false;
        BusyRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        ApplyHelloFirstLayout(Hello.IsConfigured && !_rollbackConfirmRequired);
        RefreshBrandLogo();
        App.EnsureMainWindowIcon(_vm.PreferParanoiaMode);
        _vm.BeginVaultPrefetch();
        _ = CheckHelloAsync();
    }

    private void RefreshBrandLogo()
        => BrandAssets.ApplyLogo(BrandLogo, _vm.PreferParanoiaMode);

    private void LoadLogo()
        => RefreshBrandLogo();

    private void ApplyHelloFirstLayout(bool helloPrimary)
    {
        _passwordSectionVisible = !helloPrimary || _rollbackConfirmRequired;
        PasswordSection.Visibility = _passwordSectionVisible
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        UnlockBtn.Visibility = _passwordSectionVisible
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        UsePasswordInsteadBtn.Visibility = helloPrimary && !_rollbackConfirmRequired
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void UsePasswordInstead_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _passwordSectionVisible = true;
        PasswordSection.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        UnlockBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        UsePasswordInsteadBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PasswordField.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private async Task CheckHelloAsync()
    {
        var configured = Hello.IsConfigured;
        var consentAvailable = await HelloService.IsAvailableAsync();
        var unlockReady = configured && await Hello.IsUnlockReadyAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            var showHello = unlockReady;
            HelloBtn.Visibility = showHello
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

            if (showHello && !_rollbackConfirmRequired)
                ApplyHelloFirstLayout(true);

            if (HelloMandatory && configured && !_rollbackConfirmRequired)
            {
                SubtitleText.Text = "Use your Windows PIN, face, or fingerprint to unlock.";
                ApplyHelloFirstLayout(true);
            }
            else if (HelloMandatory && configured && _rollbackConfirmRequired)
            {
                SubtitleText.Text =
                    "Rollback confirmation requires your master password. Enter it below and tap Unlock.";
                ApplyHelloFirstLayout(false);
            }
            else if (HelloMandatory && !configured)
            {
                SubtitleText.Text = consentAvailable
                    ? "Windows Hello is required by policy. Unlock with your master password once, then configure Hello in Settings."
                    : "Windows Hello is required by policy but unavailable on this device. Unlock with your master password and contact IT if Hello cannot be enabled.";
            }
            else if (showHello && !_rollbackConfirmRequired)
            {
                SubtitleText.Text = _bridgeUnlockMode
                    ? "Approve your Windows PIN, face, or fingerprint — Fill will finish automatically if the popup stays open."
                    : "Approve your Windows PIN, face, or fingerprint to unlock quickly.";
            }
            else if (consentAvailable && !configured)
            {
                SubtitleText.Text = "Enter your master password to continue. " +
                                     "You can set up Windows Hello in Settings → Windows Hello.";
            }
            else if (configured && !showHello)
            {
                SubtitleText.Text =
                    "Windows Hello is set up but unavailable right now. Use your master password, or set it up again in Settings.";
                ApplyHelloFirstLayout(false);
            }
            else
            {
                SubtitleText.Text = "Enter your master password to continue.";
            }

            if (_bridgeUnlockMode && showHello)
                HeadingText.Text = "Unlock for browser Fill";

            _helloCheckComplete = true;
            ApplyUnlockControls();

            if (showHello && !_rollbackConfirmRequired && !_autoHelloAttempted)
                _ = AutoHelloUnlockAsync();
        });
    }

    private async Task AutoHelloUnlockAsync()
    {
        if (_autoHelloAttempted || _rollbackConfirmRequired)
            return;

        _autoHelloAttempted = true;

        try
        {
            if (_bridgeUnlockMode)
            {
                for (var i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    if (_vm.IsUnlocked || _vm.IsBusy)
                        return;
                    if (App.EnsureMainWindowHandle() != IntPtr.Zero)
                        break;
                }

                await Task.Delay(400);
            }
            else
            {
                for (var i = 0; i < 6; i++)
                {
                    if (App.EnsureMainWindowHandle() != IntPtr.Zero)
                        break;
                    await Task.Delay(20);
                }

                await Task.Delay(40);
            }

            if (_vm.IsUnlocked || _vm.IsBusy)
                return;

            App.EnsureMainWindowHandle();
            await HelloBtn_ClickCoreAsync();
        }
        catch (Exception ex)
        {
            App.LogException("UnlockPage.AutoHelloUnlockAsync", ex);
        }
    }

    private void ApplyUnlockControls()
    {
        if (!_helloCheckComplete) return;

        var helloBlocksPassword = HelloMandatory && Hello.IsConfigured && !_rollbackConfirmRequired;
        PasswordField.IsEnabled = !helloBlocksPassword;
        UnlockBtn.IsEnabled = !helloBlocksPassword;
        HelloBtn.IsEnabled = true;
    }

    private async void UnlockBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await TryUnlockAsync(PasswordField.Password, _rollbackConfirmRequired);

    private void PasswordField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = TryUnlockAsync(PasswordField.Password, _rollbackConfirmRequired);
    }

    private async void HelloBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await HelloBtn_ClickCoreAsync();

    private async Task HelloBtn_ClickCoreAsync()
    {
        if (_vm.IsBusy)
            return;

        SetBusy(true, "Verifying Windows Hello…");
        ErrorBar.IsOpen = false;

        var helloResult = await Hello.TryUnlockMasterKeyAsync();
        if (!helloResult.Ok || helloResult.MasterKey is null)
        {
            SetBusy(false);
            if (helloResult.Cancelled)
            {
                if (!_passwordSectionVisible && !HelloMandatory)
                    UsePasswordInstead_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
                return;
            }

            if (HelloMandatory)
            {
                SubtitleText.Text = "Windows Hello could not load your credential. Unlock with your master password, then re-configure Hello in Settings.";
                ApplyHelloFirstLayout(false);
                ApplyUnlockControls();
            }

            ShowError(helloResult.Error
                ?? "Windows Hello did not unlock the vault. Try again or use your master password.");
            return;
        }

        var masterKey = helloResult.MasterKey;
        try
        {
            var (ok, error) = await _vm.UnlockWithMasterKeyAsync(
                masterKey,
                paranoiaMode: _vm.PreferParanoiaMode,
                confirmRollback: _rollbackConfirmRequired);
            SetBusy(false);

            if (!ok)
            {
                if (HelloMandatory)
                {
                    SubtitleText.Text = "Windows Hello unlock failed. Unlock with your master password, then re-configure Hello in Settings.";
                    ApplyHelloFirstLayout(false);
                    ApplyUnlockControls();
                }
                ShowError(error ?? "Windows Hello unlock failed. Try your master password, or tap Hello again.");
            }
            else if (error is not null)
            {
                RollbackBar.Message = error;
                RollbackBar.IsOpen = true;
                if (_vm.IsReadOnly)
                {
                    RollbackBar.Message = error + "\n\nVault is still read-only. Tap Unlock again to confirm rollback.";
                    _rollbackConfirmRequired = true;
                    ApplyHelloFirstLayout(false);
                }
            }
            else
            {
                _vm.PendingRollbackConfirm = false;
                if (_bridgeUnlockMode)
                {
                    SubtitleText.Text = "Unlocked — switch back to your browser tab. "
                                        + "Fill will finish automatically if the Fortiva popup is still open.";
                }
            }
        }
        finally
        {
            Fortiva.Core.Crypto.SecureMemory.Zero(masterKey);
        }
    }

    private async Task TryUnlockAsync(string password, bool confirmRollback)
    {
        if (_vm.IsBusy)
        {
            ShowError("Fortiva is busy — wait a moment and try again.");
            return;
        }

        if (!_helloCheckComplete)
        {
            ShowError("Please wait a moment while Fortiva finishes starting up.");
            return;
        }

        if (HelloMandatory && Hello.IsConfigured && !confirmRollback)
        {
            ShowError("Your organization requires Windows Hello to unlock this vault.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter your master password.");
            return;
        }

        SetBusy(true, "Deriving encryption key…");
        ErrorBar.IsOpen = false;
        RollbackBar.IsOpen = false;

        var (ok, error) = await _vm.UnlockAsync(
            password,
            paranoiaMode: _vm.PreferParanoiaMode,
            confirmRollback: confirmRollback);

        SetBusy(false);

        if (!ok)
        {
            ShowError(error ?? "Unlock failed. Check your password.");
            PasswordField.Password = "";
        }
        else if (error is not null)
        {
            RollbackBar.Message = error;
            RollbackBar.IsOpen = true;
            if (_vm.IsReadOnly)
            {
                RollbackBar.Message = error + "\n\nVault is still read-only. Tap Unlock again to confirm rollback.";
                _rollbackConfirmRequired = true;
                ApplyHelloFirstLayout(false);
            }
        }
        else
        {
            _vm.PendingRollbackConfirm = false;
            if (_bridgeUnlockMode)
            {
                SubtitleText.Text = "Unlocked — switch back to your browser tab. "
                                    + "Fill will finish automatically if the Fortiva popup is still open.";
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        BusyRing.Visibility = busy ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        BusyRing.IsActive = busy;
        if (busy && status is not null)
            SubtitleText.Text = status;
        var helloBlocksPassword = HelloMandatory && Hello.IsConfigured && !_rollbackConfirmRequired;
        UnlockBtn.IsEnabled = !busy && _helloCheckComplete && !helloBlocksPassword;
        HelloBtn.IsEnabled = !busy && _helloCheckComplete;
        PasswordField.IsEnabled = !busy && _helloCheckComplete && !helloBlocksPassword;
    }
}
