using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Pages;

public sealed partial class LicenseRequiredPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;

    public LicenseRequiredPage()
    {
        InitializeComponent();
        BrandAssets.ApplyLogo(BrandLogo, _vm.PreferParanoiaMode);
        RefreshDetail();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _vm.ThemeChanged += OnThemeChanged;
        BrandAssets.ApplyLogo(BrandLogo, _vm.PreferParanoiaMode);
        RefreshDetail();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
        => DispatcherQueue.TryEnqueue(() => ThemeService.ApplyToElement(this));

    private void RefreshDetail()
    {
        DetailText.Text = _vm.License is null
            ? "No license file found in %PROGRAMDATA%\\Fortiva\\license.dat"
            : "The installed license is invalid or expired.";
    }

    private void RecheckBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _vm.ReloadEnterpriseConfig();
        RefreshDetail();
        if (_vm.IsLicenseValid)
        {
            if (!_vm.VaultExists)
                NavigationService.Current.Navigate<OnboardingPage>();
            else
                NavigationService.Current.Navigate<UnlockPage>();
        }
    }
}
