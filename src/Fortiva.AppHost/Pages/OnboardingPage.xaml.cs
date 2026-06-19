using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Platform;
using Fortiva.Core.Vault;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class OnboardingPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private HelloUnlockManager Hello => new(_vm.HelloDataDirectory, _vm.IsEnterprise);
    private int _step;
    private readonly StackPanel[] _steps;
    private readonly Border[] _dots;
    private bool _isFinishing;
    private bool _helloEnrollmentPending;

    public OnboardingPage()
    {
        InitializeComponent();
        _steps = [Step0, Step1, Step2, Step3, Step4];
        _dots = [Dot0, Dot1, Dot2, Dot3, Dot4];
        RefreshBrandLogo();
        _vm.BrandAppearanceChanged += OnBrandAppearanceChanged;
        RefreshPortableHint();
        _ = CheckHelloAvailabilityAsync();
        WireOnboardingEnterKey();
    }

    private void WireOnboardingEnterKey()
    {
        void OnEnter(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
                return;

            e.Handled = true;
            if (_step == 1)
                Step1Next_Click(sender, new RoutedEventArgs());
            else if (_step == 3 && FinishBtn.IsEnabled)
                FinishOnboarding_Click(sender, new RoutedEventArgs());
            else if (_step == 4)
                BrowserExtContinue_Click(sender, new RoutedEventArgs());
        }

        NewPasswordBox.KeyDown += OnEnter;
        ConfirmPasswordBox.KeyDown += OnEnter;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        RefreshPortableHint();

        _vm.RefreshVaultExists();
        if (_vm.VaultExists && !_vm.IsUnlocked)
            RedirectToUnlockIfVaultExists();

        ApplyEnterprisePolicyConstraints();
    }

    private void ApplyEnterprisePolicyConstraints()
    {
        var policy = _vm.Policy;
        if (!_vm.IsEnterprise || policy is null)
            return;

        if (policy.MandatoryWindowsHello)
            SkipHelloBtn.Visibility = Visibility.Collapsed;

        if (policy.MandatoryParanoiaMode)
        {
            ParanoiaToggle.IsOn = true;
            ParanoiaToggle.IsEnabled = false;
        }
    }

    private void RefreshPortableHint()
    {
        if (_vm.IsPortableMode)
        {
            PortableLocationHint.Text =
                $"Your vault will be stored on removable media at:\n{_vm.VaultDirectory}";
            PortableLocationHint.Visibility = Visibility.Visible;
        }
        else
        {
            PortableLocationHint.Visibility = Visibility.Collapsed;
        }
    }

    private async Task CheckHelloAvailabilityAsync()
    {
        var available = await HelloService.IsAvailableAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!available)
            {
                HelloInfoBar.Message = "Windows Hello is not available on this device. You can continue without it.";
                HelloInfoBar.Severity = InfoBarSeverity.Warning;
                HelloInfoBar.IsOpen = true;
                ShowHelloContinue(hideSkip: _vm.IsEnterprise && _vm.Policy?.MandatoryWindowsHello == true);
            }
        });
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.BrandAppearanceChanged -= OnBrandAppearanceChanged;
    }

    private void ShowHelloContinue(bool hideSkip = false)
    {
        EnableHelloBtn.Visibility = Visibility.Collapsed;
        HelloContinueBtn.Visibility = Visibility.Visible;
        SkipHelloBtn.Visibility = hideSkip ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HelloSkip_Click(object sender, RoutedEventArgs e)
        => ShowStep(Math.Min(_step + 1, _steps.Length - 1));

    private void HelloContinue_Click(object sender, RoutedEventArgs e)
        => ShowStep(Math.Min(_step + 1, _steps.Length - 1));

    private void OfflineAck_Changed(object sender, RoutedEventArgs e)
        => FinishBtn.IsEnabled = OfflineAckCheck.IsChecked == true && !_isFinishing;

    private void ShowFinishError(string message)
    {
        FinishErrorBar.Message = message;
        FinishErrorBar.IsOpen = true;
    }

    private void ShowStep(int step)
    {
        for (var i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < _dots.Length; i++)
        {
            _dots[i].Width = i == step ? 28 : 10;
            _dots[i].Opacity = i == step ? 1.0 : 0.45;
            _dots[i].Background = i == step
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
        }

        _step = step;
    }

    private void NextStep_Click(object sender, RoutedEventArgs e)
        => ShowStep(Math.Min(_step + 1, _steps.Length - 1));

    private void PrevStep_Click(object sender, RoutedEventArgs e)
        => ShowStep(Math.Max(_step - 1, 0));

    private void NewPassword_Changed(object sender, RoutedEventArgs e)
    {
        var result = _vm.AnalyzeStrength(NewPasswordBox.Password);
        StrengthBar.Value = (int)result.Strength;
        StrengthLabel.Text = $"{result.Label}  ({result.EntropyBits:F0} bits entropy)";

        var color = result.Strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak => new SolidColorBrush(Color.FromArgb(255, 220, 50, 50)),
            PasswordStrength.Fair => new SolidColorBrush(Color.FromArgb(255, 200, 130, 0)),
            PasswordStrength.Strong => new SolidColorBrush(Color.FromArgb(255, 0, 160, 80)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
        };
        StrengthBar.Foreground = color;
        StrengthSuggestion.Text = result.Suggestions.FirstOrDefault() ?? "";
    }

    private void Step1Next_Click(object sender, RoutedEventArgs e)
    {
        PasswordErrorBar.IsOpen = false;
        if (string.IsNullOrWhiteSpace(NewPasswordBox.Password))
        {
            PasswordErrorBar.Message = "Master password cannot be empty.";
            PasswordErrorBar.IsOpen = true;
            return;
        }
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            PasswordErrorBar.Message = "Passwords do not match.";
            PasswordErrorBar.IsOpen = true;
            return;
        }
        var strength = _vm.AnalyzeStrength(NewPasswordBox.Password);
        if (strength.Strength < PasswordStrength.Fair)
        {
            PasswordErrorBar.Message = "Your password is too weak. " + (strength.Suggestions.FirstOrDefault() ?? "");
            PasswordErrorBar.IsOpen = true;
            return;
        }
        ShowStep(2);
    }

    private void EnableHello_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NewPasswordBox.Password))
        {
            HelloInfoBar.Message = "Go back and set a master password first.";
            HelloInfoBar.Severity = InfoBarSeverity.Warning;
            HelloInfoBar.IsOpen = true;
            return;
        }

        _helloEnrollmentPending = true;
        HelloInfoBar.Message = "Windows Hello will be enabled when your vault is created. You will be prompted once for face, fingerprint, or PIN.";
        HelloInfoBar.Severity = InfoBarSeverity.Informational;
        HelloInfoBar.IsOpen = true;
        ShowHelloContinue(hideSkip: true);
    }

    private void RedirectToUnlockIfVaultExists()
    {
        _vm.RefreshVaultExists();
        if (!_vm.VaultExists) return;

        NavigationService.Current.ResetCurrent();
        NavigationService.Current.Navigate<UnlockPage>();
        NavigationService.Current.ClearHistory();
    }

    private bool TryBlockWhenLeftoverVaultExists()
    {
        _vm.RefreshVaultExists();
        if (_vm.VaultExists)
        {
            RedirectToUnlockIfVaultExists();
            return true;
        }

        if (_vm.IsEnterprise)
        {
            var enterpriseVault = Path.Combine(FortivaPaths.EnterpriseProgramData, VaultConstants.VaultFileName);
            if (File.Exists(enterpriseVault))
            {
                ShowFinishError(
                    "An enterprise vault already exists on this PC. Contact your IT administrator " +
                    "or unlock the existing vault instead of creating a new one.");
                return true;
            }
            return false;
        }

        if (_vm.IsPortableMode)
            return false;

        if (!FortivaPaths.PersonalVaultFileExists())
            return false;

        var paths = string.Join(", ", FortivaPaths.FindPersonalVaultFilePaths());
        ShowFinishError(
            "Fortiva password data from a previous install is still on this PC " +
            $"(for example: {paths}). " +
            "Close Fortiva, delete the Fortiva folder under AppData\\Roaming, then try again - " +
            "or restart the installer and choose to remove the old vault.");
        return true;
    }

    private async void FinishOnboarding_Click(object sender, RoutedEventArgs e)
    {
        FinishErrorBar.IsOpen = false;

        if (_isFinishing) return;

        if (TryBlockWhenLeftoverVaultExists())
            return;

        _vm.RefreshVaultExists();
        if (_vm.VaultExists)
        {
            RedirectToUnlockIfVaultExists();
            return;
        }

        if (OfflineAckCheck.IsChecked != true)
        {
            ShowFinishError("Please confirm you have recorded your master password offline.");
            return;
        }

        if (_vm.IsEnterprise && _vm.Policy?.MandatoryWindowsHello == true &&
            !_helloEnrollmentPending && !Hello.IsConfigured)
        {
            var helloAvailable = await HelloService.IsAvailableAsync().ConfigureAwait(true);
            ShowFinishError(helloAvailable
                ? "Your organization requires Windows Hello. Go back to step 2 and enable it before creating the vault."
                : "Your organization requires Windows Hello, but it is not available on this device. "
                  + "Set up face, fingerprint, or a PIN in Windows Settings, then return to step 2.");
            return;
        }

        var password = NewPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowFinishError("Master password was lost — go back to step 1 and re-enter it.");
            return;
        }

        _isFinishing = true;
        FinishBtn.IsEnabled = false;
        SetBusyOverlay(true, "Creating your encrypted vault…", "Deriving keys with Argon2id - this may take a few seconds.");

        try
        {
            var paranoia = ParanoiaToggle.IsOn;
            _vm.SetParanoiaMode(paranoia);

            var level = (_vm.Policy?.MandatoryParanoiaMode == true || ParanoiaToggle.IsOn)
                ? SecurityLevel.Paranoia
                : SecurityLevel.Standard;
            await _vm.CreateVaultAsync(password, level).ConfigureAwait(true);

            DispatcherQueue.TryEnqueue(() => BusyDetail.Text = "Unlocking vault…");
            _vm.DeferUnlockNavigation = true;
            _vm.SkipNextBrowserExtensionPrompt = true;
            var (ok, error) = await _vm.UnlockAsync(password, paranoiaMode: paranoia).ConfigureAwait(true);
            if (!ok)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SetBusyOverlay(false);
                    ShowFinishError(error ?? "Vault was created but unlock failed. Use the unlock screen with your master password.");
                    RedirectToUnlockIfVaultExists();
                });
                return;
            }

            if (_helloEnrollmentPending && _vm.IsUnlocked)
            {
                try
                {
                    await _vm.SyncHelloCredentialFromSessionAsync(_vm.IsEnterprise);
                }
                catch (Exception helloEx)
                {
                    App.LogException("OnboardingPage.SyncHello", helloEx);
                }

                if (_vm.IsEnterprise && _vm.Policy?.MandatoryWindowsHello == true && !Hello.IsConfigured)
                {
                    SetBusyOverlay(false);
                    ShowFinishError(
                        "Windows Hello setup did not complete. Go back to step 2, enable Hello, and try again.");
                    return;
                }
            }

            SetBusyOverlay(false);
            if (!_vm.IsAdmin)
            {
                RefreshBrowserExtensionStep();
                ShowStep(4);
                return;
            }

            _vm.RequestNavigationTab("Vault");
        }
        catch (Exception ex)
        {
            SetBusyOverlay(false);
            App.LogException("OnboardingPage.FinishOnboarding", ex);
            _vm.RefreshVaultExists();
            if (_vm.VaultExists)
            {
                ShowFinishError(
                    "Your vault was created. Use the unlock screen with your master password to continue.");
                RedirectToUnlockIfVaultExists();
                return;
            }

            ShowFinishError($"Failed to create vault: {App.DescribeException(ex)}");
        }
        finally
        {
            _isFinishing = false;
            FinishBtn.IsEnabled = OfflineAckCheck.IsChecked == true;
        }
    }

    private void SetBusyOverlay(bool visible, string? title = null, string? detail = null)
    {
        BusyOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = visible;
        if (title is not null) BusyTitle.Text = title;
        if (detail is not null) BusyDetail.Text = detail;
    }

    private void RefreshBrandLogo()
        => BrandAssets.ApplyLogo(BrandLogo, _vm.PreferParanoiaMode);

    private void OnBrandAppearanceChanged()
        => RefreshBrandLogo();

    private void RefreshBrowserExtensionStep()
    {
        BrowserExtensionSetupHelper.EnsureReady(_vm);
        var status = BrowserExtensionSetupHelper.GetStatus(_vm);
        BrowserExtStatusText.Text = BrowserExtensionSetupHelper.FormatLiveStatusMessage(status, _vm);
    }

    private async void BrowserExtConnect_Click(object sender, RoutedEventArgs e)
    {
        BrowserExtConnectBtn.IsEnabled = false;
        try
        {
            var result = await BrowserExtensionSetupHelper.ConnectBrowserAsync(_vm, XamlRoot);
            RefreshBrowserExtensionStep();
            if (!result.Success)
                BrowserExtStatusText.Text = result.Error ?? "Setup failed.";
        }
        finally
        {
            BrowserExtConnectBtn.IsEnabled = true;
        }
    }

    private void BrowserExtContinue_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeferUnlockNavigation = false;
        _vm.SkipNextBrowserExtensionPrompt = false;
        if (!_vm.IsEnterprise)
            _vm.SetBrowserExtensionSetupDismissed();

        NavigationService.Current.ResetCurrent();
        NavigationService.Current.ClearHistory();
        _vm.RequestNavigationTab("Vault");
    }
}
