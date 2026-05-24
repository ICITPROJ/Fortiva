using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Hello;
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
    private readonly WindowsHelloKeyProtector _helloProtector;
    private int _step;
    private readonly StackPanel[] _steps;
    private readonly Border[] _dots;
    private bool _isFinishing;
    private bool _helloEnrollmentPending;

    public OnboardingPage()
    {
        InitializeComponent();
        _helloProtector = new WindowsHelloKeyProtector(
            FortivaPaths.GetHelloDataDirectory(_vm.IsEnterprise),
            _vm.IsEnterprise);
        _steps = [Step0, Step1, Step2, Step3];
        _dots = [Dot0, Dot1, Dot2, Dot3];
        RefreshPortableHint();
        _ = CheckHelloAvailabilityAsync();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshPortableHint();

        _vm.RefreshVaultExists();
        if (_vm.VaultExists && !_vm.IsUnlocked)
        {
            NavigationService.Current.ResetCurrent();
            NavigationService.Current.Navigate<UnlockPage>();
            NavigationService.Current.ClearHistory();
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
                ShowHelloContinue();
            }
        });
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

    private async void EnableHello_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NewPasswordBox.Password))
        {
            HelloInfoBar.Message = "Go back and set a master password first.";
            HelloInfoBar.Severity = InfoBarSeverity.Warning;
            HelloInfoBar.IsOpen = true;
            return;
        }

        var result = await HelloService.VerifyAsync("Fortiva — enable Windows Hello unlock");
        if (result.Verified)
        {
            _helloEnrollmentPending = true;
            HelloInfoBar.Message = "Windows Hello verified. It will be enabled after your vault is created.";
            HelloInfoBar.Severity = InfoBarSeverity.Success;
            HelloInfoBar.IsOpen = true;
            ShowHelloContinue(hideSkip: true);
        }
        else
        {
            HelloInfoBar.Message = result.ErrorMessage ?? "Verification failed.";
            HelloInfoBar.Severity = InfoBarSeverity.Error;
            HelloInfoBar.IsOpen = true;
        }
    }

    private async void FinishOnboarding_Click(object sender, RoutedEventArgs e)
    {
        FinishErrorBar.IsOpen = false;

        if (_isFinishing) return;

        _vm.RefreshVaultExists();
        if (_vm.VaultExists)
        {
            NavigationService.Current.ResetCurrent();
            NavigationService.Current.Navigate<UnlockPage>();
            NavigationService.Current.ClearHistory();
            return;
        }

        if (OfflineAckCheck.IsChecked != true)
        {
            ShowFinishError("Please confirm you have recorded your master password offline.");
            return;
        }

        var password = NewPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowFinishError("Master password was lost — go back to step 2 and re-enter it.");
            return;
        }

        _isFinishing = true;
        FinishBtn.IsEnabled = false;
        SetBusyOverlay(true, "Creating your encrypted vault…", "Deriving keys with Argon2id — this may take a few seconds.");

        try
        {
            var paranoia = ParanoiaToggle.IsOn;
            _vm.SetParanoiaMode(paranoia);

            var level = paranoia ? SecurityLevel.Paranoia : SecurityLevel.Standard;
            await _vm.CreateVaultAsync(password, level).ConfigureAwait(true);

            DispatcherQueue.TryEnqueue(() => BusyDetail.Text = "Unlocking vault…");
            var (ok, error) = await _vm.UnlockAsync(password, paranoiaMode: paranoia).ConfigureAwait(true);
            if (!ok)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SetBusyOverlay(false);
                    ShowFinishError(error ?? "Vault was created but unlock failed. Restart Fortiva and enter your master password.");
                });
                return;
            }

            if (_helloEnrollmentPending)
                _vm.SyncHelloCredentialFromSession();

            SetBusyOverlay(false);
            _vm.RequestNavigationTab("Vault");
        }
        catch (Exception ex)
        {
            SetBusyOverlay(false);
            App.LogException("OnboardingPage.FinishOnboarding", ex);
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            ShowFinishError($"Failed to create vault: {detail}");
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
}
