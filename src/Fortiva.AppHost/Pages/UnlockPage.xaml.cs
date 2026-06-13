using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Platform;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fortiva.AppHost.Pages;

public sealed partial class UnlockPage : Microsoft.UI.Xaml.Controls.Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly HelloUnlockManager _hello;
    private bool _rollbackConfirmRequired;
    private bool _helloCheckComplete;
    private bool _bridgeUnlockMode;
    private bool _autoHelloAttempted;

    public UnlockPage()
    {
        InitializeComponent();
        _hello = new HelloUnlockManager(
            FortivaPaths.GetHelloDataDirectory(_vm.IsEnterprise),
            _vm.IsEnterprise);
        LoadLogo();
        _vm.BrandAppearanceChanged += OnBrandAppearanceChanged;
    }

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
        EnablePasswordWhileHelloProbes();
        RefreshBrandLogo();
        _ = CheckHelloAsync();
    }

    private void RefreshBrandLogo()
        => BrandAssets.ApplyLogo(BrandLogo, _vm.PreferParanoiaMode);

    private void LoadLogo()
    {
        RefreshBrandLogo();
    }

    private async Task CheckHelloAsync()
    {
        var available = await HelloService.IsAvailableAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            var configured = _hello.IsConfigured;
            var showHello = available && configured;
            HelloBtn.Visibility = showHello
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

            if (HelloMandatory && configured)
            {
                PasswordField.IsEnabled = false;
                UnlockBtn.IsEnabled = false;
                SubtitleText.Text = "Your organization requires Windows Hello to unlock this vault.";
            }
            else if (HelloMandatory && !configured)
            {
                SubtitleText.Text = available
                    ? "Windows Hello is required by policy. Unlock with your master password once, then configure Hello in Settings."
                    : "Windows Hello is required by policy but unavailable on this device. Unlock with your master password and contact IT if Hello cannot be enabled.";
            }
            else if (available && !configured)
            {
                SubtitleText.Text = "Enter your master password to continue. " +
                                     "You can set up Windows Hello in Settings → Windows Hello.";
            }
            else
            {
                SubtitleText.Text = "Enter your master password to continue.";
            }

            if (_bridgeUnlockMode)
            {
                HeadingText.Text = "Unlock for browser Fill";
                SubtitleText.Text = "Your browser asked for credentials on this site. " +
                                    "Unlock with Windows Hello or your master password — Fill will finish automatically if you keep the extension popup open.";
            }

            _helloCheckComplete = true;
            ApplyUnlockControls();

            if (_bridgeUnlockMode && showHello && configured)
                _ = AutoHelloUnlockAsync();
        });
    }

    private async Task AutoHelloUnlockAsync()
    {
        if (_autoHelloAttempted)
            return;

        _autoHelloAttempted = true;

        try
        {
            // Cold-start from the browser extension often lands here before the WinUI HWND exists.
            // Firing Hello too early produces COM errors and misleading "re-setup Hello" guidance.
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                if (!_bridgeUnlockMode || _vm.IsUnlocked || _vm.IsBusy)
                    return;
                if (App.EnsureMainWindowHandle() != IntPtr.Zero)
                    break;
            }

            await Task.Delay(500);
            if (!_bridgeUnlockMode || _vm.IsUnlocked || _vm.IsBusy)
                return;

            HelloBtn_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
        }
        catch (Exception ex)
        {
            App.LogException("UnlockPage.AutoHelloUnlockAsync", ex);
        }
    }

    private void ApplyUnlockControls()
    {
        if (!_helloCheckComplete) return;

        var helloBlocksPassword = HelloMandatory && _hello.IsConfigured;
        PasswordField.IsEnabled = !helloBlocksPassword;
        UnlockBtn.IsEnabled = !helloBlocksPassword;
        HelloBtn.IsEnabled = true;
    }

    private void EnablePasswordWhileHelloProbes()
    {
        if (HelloMandatory && _hello.IsConfigured)
            return;
        PasswordField.IsEnabled = true;
        UnlockBtn.IsEnabled = true;
    }

    private async void UnlockBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await TryUnlockAsync(PasswordField.Password, _rollbackConfirmRequired);

    private void PasswordField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = TryUnlockAsync(PasswordField.Password, _rollbackConfirmRequired);
    }

    private async void HelloBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;

        if (!_hello.IsHardwareBacked)
        {
            var helloResult = await HelloService.VerifyAsync("Unlock Fortiva vault");
            if (!helloResult.Verified)
            {
                SetBusy(false);
                ShowError(helloResult.ErrorMessage ?? "Windows Hello verification failed.");
                return;
            }
        }

        var masterKey = await _hello.TryLoadMasterKeyAsync();
        if (masterKey is null)
        {
            SetBusy(false);
            if (HelloMandatory)
            {
                SubtitleText.Text = "Windows Hello could not load your credential. Unlock with your master password, then re-configure Hello in Settings.";
                ApplyUnlockControls();
            }
            ShowError(_hello.IsConfigured
                ? "Windows Hello did not unlock the vault this time. Try the Hello button again, "
                  + "or use your master password. Only re-enroll in Settings if Hello keeps failing after a successful password unlock."
                : "Windows Hello could not unlock the vault key. Try again, use your master password, or set up Hello in Settings.");
            return;
        }

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

        if (HelloMandatory && _hello.IsConfigured)
        {
            ShowError("Your organization requires Windows Hello to unlock this vault.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter your master password.");
            return;
        }

        SetBusy(true);
        ErrorBar.IsOpen = false;
        RollbackBar.IsOpen = false;

        var (ok, error) = await _vm.UnlockAsync(password, paranoiaMode: _vm.PreferParanoiaMode, confirmRollback: confirmRollback);

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

    private void SetBusy(bool busy)
    {
        BusyRing.Visibility = busy ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        BusyRing.IsActive = busy;
        var helloBlocksPassword = HelloMandatory && _hello.IsConfigured;
        UnlockBtn.IsEnabled = !busy && _helloCheckComplete && !helloBlocksPassword;
        HelloBtn.IsEnabled = !busy && _helloCheckComplete;
        PasswordField.IsEnabled = !busy && _helloCheckComplete && !helloBlocksPassword;
    }
}
