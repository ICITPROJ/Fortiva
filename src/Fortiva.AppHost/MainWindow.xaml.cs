using Fortiva.AppHost.Pages;
using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Fortiva.AppHost;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private bool _suppressNav;

    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar (before theme chrome so title-bar colors apply)
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ThemeService.Apply(this, _vm.ThemePreference);
        ThemeService.ApplySystemBackdrop(this);

        TitleText.Text = $"Fortiva {App.Edition}";

        RefreshBrandAppearance();

        NavigationService.Current.Initialize(ContentFrame);

        _vm.SetUiInvoker(action => DispatcherQueue.TryEnqueue(() => action()));
        _vm.RefreshVaultExists();

        _vm.LockOccurred += OnLocked;
        _vm.UnlockOccurred += OnUnlocked;
        _vm.BrandAppearanceChanged += RefreshBrandAppearance;
        _vm.ThemeChanged += OnThemeChanged;
        _vm.EnterpriseConfigChanged += OnEnterpriseConfigChanged;
        _vm.VaultLocationChanged += OnVaultLocationChanged;
        _vm.NavigationTabRequested += SelectNavigationTab;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.StatusMessage) or nameof(ShellViewModel.IsUnlocked))
                DispatcherQueue.TryEnqueue(() => StatusText.Text = _vm.StatusMessage);
        };

        NavAudit.Visibility = _vm.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        NavAdmin.Visibility = Visibility.Collapsed;

        if (_vm.IsAdmin)
        {
            NavVault.Visibility = NavGenerator.Visibility = NavHealth.Visibility = NavImport.Visibility = NavSettings.Visibility = Visibility.Collapsed;
            NavLock.Visibility = Visibility.Collapsed;
        }

        if (_vm.IsEnterprise && !_vm.IsAdmin && !_vm.IsLicenseValid)
        {
            NavVault.Visibility = NavGenerator.Visibility = NavHealth.Visibility = NavImport.Visibility = Visibility.Collapsed;
            NavLock.Visibility = Visibility.Collapsed;
            NavigationService.Current.Navigate<LicenseRequiredPage>();
        }
        else if (_vm.IsAdmin)
            NavigationService.Current.Navigate<Admin.AdminMainWindow>();
        else if (!_vm.VaultExists)
            NavigationService.Current.Navigate<OnboardingPage>();
        else
            NavigationService.Current.Navigate<UnlockPage>();

        StatusText.Text = _vm.StatusMessage;

        if (_vm.PortableVaultUnavailable)
            Activated += MainWindow_ActivatedForPortablePrompt;
    }

    private bool _portablePromptShown;

    private void MainWindow_ActivatedForPortablePrompt(object sender, WindowActivatedEventArgs args)
    {
        if (_portablePromptShown || !_vm.PortableVaultUnavailable)
            return;
        _portablePromptShown = true;
        Activated -= MainWindow_ActivatedForPortablePrompt;
        _ = ShowPortableVaultUnavailableDialogAsync();
    }

    private async Task ShowPortableVaultUnavailableDialogAsync()
    {
        var path = _vm.UnavailablePortablePath ?? "your USB drive";
        var dialog = new ContentDialog
        {
            Title = "Portable vault unavailable",
            Content = $"Fortiva could not find your portable vault at:\n{path}\n\n" +
                      "The drive may be unplugged. Fortiva is using your local vault until you reconnect the drive or change location in Settings.",
            PrimaryButtonText = "Use local vault",
            SecondaryButtonText = "Retry",
            CloseButtonText = "Open Settings",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (_vm.RetryPortableVaultConnection())
            {
                OnVaultLocationChanged();
                return;
            }

            var retryDialog = new ContentDialog
            {
                Title = "Drive still unavailable",
                Content = "Connect your USB drive and try again, or continue with your local vault.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await retryDialog.ShowAsync();
        }
        else if (result == ContentDialogResult.None)
        {
            _suppressNav = true;
            try { NavView.SelectedItem = NavSettings; }
            finally { _suppressNav = false; }
            NavigationService.Current.Navigate<SettingsPage>();
        }

        _vm.DismissPortableVaultUnavailable();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNav) return;
        if (args.SelectedItem is not NavigationViewItem item) return;

        try
        {
            var tag = item.Tag?.ToString();

            if (tag == "Lock")
            {
                _suppressNav = true;
                try
                {
                    NavView.SelectedItem = null;
                    if (_vm.IsUnlocked) _vm.Lock();
                }
                finally { _suppressNav = false; }
                return;
            }

            if (tag == "Admin")
            {
                NavigationService.Current.Navigate<Admin.AdminMainWindow>();
                return;
            }

            if (!_vm.IsUnlocked)
            {
                _suppressNav = true;
                try
                {
                    if (!_vm.VaultExists)
                        NavigationService.Current.Navigate<OnboardingPage>();
                    else
                        NavigationService.Current.Navigate<UnlockPage>();
                    NavView.SelectedItem = null;
                }
                finally { _suppressNav = false; }
                return;
            }

            if (_vm.IsEnterprise && !_vm.IsLicenseValid && tag is not "Settings")
            {
                NavigationService.Current.Navigate<LicenseRequiredPage>();
                return;
            }

            switch (tag)
            {
                case "Vault":        NavigationService.Current.Navigate<VaultPage>(); break;
                case "Generator":    NavigationService.Current.Navigate<PasswordGeneratorPage>(); break;
                case "Health":       NavigationService.Current.Navigate<HealthPage>(); break;
                case "ImportExport": NavigationService.Current.Navigate<ImportExportPage>(); break;
                case "Settings":     NavigationService.Current.Navigate<SettingsPage>(); break;
                case "Audit":        NavigationService.Current.Navigate<AuditPage>(); break;
            }
        }
        catch (Exception ex)
        {
            App.LogException($"NavView_SelectionChanged({item.Tag})", ex);
            StatusText.Text = "Navigation error - see crash log.";
        }
    }

    private void OnVaultLocationChanged()
    {
        if (_vm.IsAdmin) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            _suppressNav = true;
            try
            {
                NavView.SelectedItem = null;
                NavigationService.Current.ResetCurrent();
                NavigationService.Current.ClearHistory();
                if (!_vm.VaultExists)
                    NavigationService.Current.Navigate<OnboardingPage>();
                else
                    NavigationService.Current.Navigate<UnlockPage>();
            }
            finally { _suppressNav = false; }
        });
    }

    private void SelectNavigationTab(string tag)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            NavigationViewItem? item = tag switch
            {
                "Vault" => NavVault,
                "Generator" => NavGenerator,
                "Health" => NavHealth,
                "ImportExport" => NavImport,
                "Settings" => NavSettings,
                "Audit" => NavAudit,
                _ => null
            };
            if (item is null) return;

            _suppressNav = true;
            try { NavView.SelectedItem = item; }
            finally { _suppressNav = false; }
        });
    }

    private void OnEnterpriseConfigChanged()
    {
        if (_vm.IsAdmin) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            var licensed = _vm.IsLicenseValid;
            NavVault.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
            NavGenerator.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
            NavHealth.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
            NavImport.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
            NavAudit.Visibility = _vm.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            NavLock.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;

            if (licensed && ContentFrame.Content is LicenseRequiredPage)
            {
                if (!_vm.VaultExists)
                    NavigationService.Current.Navigate<OnboardingPage>();
                else
                    NavigationService.Current.Navigate<UnlockPage>();
            }
            else if (!licensed)
            {
                NavVault.Visibility = NavGenerator.Visibility = NavHealth.Visibility = NavImport.Visibility = Visibility.Collapsed;
                NavLock.Visibility = Visibility.Collapsed;
                NavigationService.Current.Navigate<LicenseRequiredPage>();
            }
        });
    }

    private void OnLocked()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _suppressNav = true;
            try
            {
                NavView.SelectedItem = null;
                NavigationService.Current.ResetCurrent();
                NavigationService.Current.Navigate<UnlockPage>();
                NavigationService.Current.ClearHistory();
                StatusText.Text = "Locked";
            }
            finally { _suppressNav = false; }
        });
    }

    private void OnUnlocked()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Navigate explicitly — do NOT rely on SelectionChanged while _suppressNav is true
            NavigationService.Current.ResetCurrent();
            NavigationService.Current.ClearHistory();
            NavigationService.Current.Navigate<VaultPage>();
            StatusText.Text = _vm.StatusMessage;

            _suppressNav = true;
            try { NavView.SelectedItem = NavVault; }
            finally { _suppressNav = false; }
        });
    }

    private void PanicBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.PanicLock();
        AppWindow.Hide();
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ThemeService.Apply(this, _vm.ThemePreference);
            ThemeService.ApplySystemBackdrop(this);
            RefreshBrandAppearance();
        });
    }

    private void RefreshBrandAppearance()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var paranoia = _vm.PreferParanoiaMode;
            BrandAssets.ApplyLogo(TitleBarLogo, paranoia);
            BrandAssets.ApplyWindowIcon(AppWindow, paranoia);
        });
    }
}
