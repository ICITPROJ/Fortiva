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
                HeadingText.Text = "Unlock for browser autofill";
                SubtitleText.Text = "Your browser extension needs credentials for this site. " +
                                    "Unlock with your master password or Windows Hello, then return to the browser.";
            }

            _helloCheckComplete = true;
            ApplyUnlockControls();
        });
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

        var helloResult = await HelloService.VerifyAsync("Unlock Fortiva vault");
        if (!helloResult.Verified)
        {
            SetBusy(false);
            ShowError(helloResult.ErrorMessage ?? "Windows Hello verification failed.");
            return;
        }

        var masterKey = await _hello.TryLoadMasterKeyAsync();
        if (masterKey is null)
        {
            await _hello.ClearAsync();
            HelloBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            SetBusy(false);
            if (HelloMandatory)
            {
                SubtitleText.Text = "Windows Hello credential was reset. Unlock with your master password, then re-configure Hello in Settings.";
                ApplyUnlockControls();
            }
            ShowError("Your Windows Hello credential is outdated. Unlock with your master password, then re-configure Hello in Settings.");
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
                await _hello.ClearAsync();
                HelloBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                if (HelloMandatory)
                {
                    SubtitleText.Text = "Windows Hello unlock failed. Unlock with your master password, then re-configure Hello in Settings.";
                    ApplyUnlockControls();
                }
                ShowError(error ?? "Windows Hello unlock failed. Re-configure Hello in Settings.");
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
            }
        }
        finally
        {
            Fortiva.Core.Crypto.SecureMemory.Zero(masterKey);
        }
    }

    private async Task TryUnlockAsync(string password, bool confirmRollback)
    {
        if (_vm.IsBusy || !_helloCheckComplete) return;

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
