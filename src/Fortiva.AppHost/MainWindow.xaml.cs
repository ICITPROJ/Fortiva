using Fortiva.AppHost.Pages;
using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.BrowserBridge;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Fortiva.AppHost;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private bool _suppressNav;
    private DateTimeOffset _lastPointerActivityReset = DateTimeOffset.MinValue;
    private static readonly TimeSpan PointerActivityThrottle = TimeSpan.FromSeconds(45);

    public MainWindow()
    {
        InitializeComponent();
        App.RegisterUiDispatcher(DispatcherQueue);

        // Custom title bar (before theme chrome so title-bar colors apply)
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInsets();
        Activated += (_, _) => UpdateTitleBarInsets();

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
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_clipboardTimer is null)
                        StatusText.Text = _vm.StatusMessage;
                    RefreshStatusChrome();
                });
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
            NavAudit.Visibility = Visibility.Collapsed;
            NavigationService.Current.Navigate<LicenseRequiredPage>();
        }
        else if (_vm.IsAdmin)
            NavigationService.Current.Navigate<Admin.AdminMainWindow>();
        else if (!_vm.VaultExists)
            NavigationService.Current.Navigate<OnboardingPage>();
        else
            NavigationService.Current.Navigate<UnlockPage>();

        StatusText.Text = _vm.StatusMessage;
        RefreshStatusChrome();

        ClipboardService.ClipboardCopied += OnClipboardCopied;
        _vm.BridgeUnlockRequested += OnBridgeUnlockRequested;
        if (!_vm.IsAdmin)
        {
            _vm.StartBridgeUnlockListener(AppContext.BaseDirectory);
            try
            {
                BrowserBridgeInstallService.EnsureInstalled(AppContext.BaseDirectory, _vm.IsEnterprise);
            }
            catch (Exception ex)
            {
                App.LogException("BrowserBridgeInstallService.EnsureInstalled", ex);
            }
        }

        if (_vm.PortableVaultUnavailable)
            Activated += MainWindow_ActivatedForPortablePrompt;

        DispatcherQueue.TryEnqueue(UpdateTitleBarInsets);

        if (Content is UIElement root)
        {
            root.PointerPressed += (_, _) => OnUserActivity();
            root.PointerMoved += (_, _) => OnPointerActivity();
            root.KeyDown += (_, _) => OnUserActivity();
        }
    }

    private void OnUserActivity()
    {
        if (_vm.IsUnlocked)
            _vm.ResetAutoLock();
    }

    private void OnPointerActivity()
    {
        if (!_vm.IsUnlocked)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastPointerActivityReset < PointerActivityThrottle)
            return;

        _lastPointerActivityReset = now;
        _vm.ResetAutoLock();
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

        FortivaDialogs.Configure(dialog, Content.XamlRoot);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _vm.SwitchToLocalVault();
            OnVaultLocationChanged();
        }
        else if (result == ContentDialogResult.Secondary)
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
            FortivaDialogs.Configure(retryDialog, Content.XamlRoot);
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
        if (_vm.IsUnlocked)
            _vm.ResetAutoLock();
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
                NavAudit.Visibility = Visibility.Collapsed;
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
                RefreshStatusChrome();
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
            RefreshStatusChrome();

            _suppressNav = true;
            try { NavView.SelectedItem = NavVault; }
            finally { _suppressNav = false; }

            if (!_vm.IsAdmin && !_vm.SkipNextBrowserExtensionPrompt)
                _ = BrowserExtensionSetupHelper.ShowFirstRunPromptAsync(Content.XamlRoot, _vm);
            _vm.SkipNextBrowserExtensionPrompt = false;
        });
    }

    private DispatcherTimer? _clipboardTimer;
    private int _clipboardSecondsLeft;
    private string _statusBeforeClipboard = "";

    private void OnClipboardCopied(int clearSeconds)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _clipboardTimer?.Stop();
            _clipboardSecondsLeft = clearSeconds;
            _statusBeforeClipboard = _vm.IsUnlocked ? _vm.StatusMessage : "Locked";
            StatusText.Text = $"Clipboard clears in {_clipboardSecondsLeft}s";
            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();
        });
    }

    private void ClipboardTimer_Tick(object? sender, object e)
    {
        _clipboardSecondsLeft--;
        if (_clipboardSecondsLeft > 0)
        {
            StatusText.Text = $"Clipboard clears in {_clipboardSecondsLeft}s";
            return;
        }

        _clipboardTimer?.Stop();
        _clipboardTimer = null;
        StatusText.Text = _vm.StatusMessage;
        RefreshStatusChrome();
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
            RefreshStatusChrome();
        });
    }

    private void OnBridgeUnlockRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                AppWindow.Show(true);
                Activate();

                if (_vm.IsUnlocked)
                    return;

                _suppressNav = true;
                try
                {
                    NavView.SelectedItem = null;
                    if (!_vm.VaultExists)
                        NavigationService.Current.Navigate<OnboardingPage>();
                    else
                        NavigationService.Current.Navigate<UnlockPage>(BridgeUnlockNavigationContext.Instance);
                }
                finally { _suppressNav = false; }
            }
            catch (Exception ex)
            {
                App.LogException("OnBridgeUnlockRequested", ex);
            }
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

    private void RefreshStatusChrome()
    {
        var theme = FortivaControlTheme.ResolveAppTheme();
        FortivaSurfaceEffects.ApplyIconButton(PanicBtn, PanicBtn);
        if (_vm.IsUnlocked)
        {
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = FortivaControlTheme.GetBrush("FortivaAccentBrush", theme, StatusIcon);
            PanicIcon.Foreground = FortivaControlTheme.GetBrush("FortivaMutedBrush", theme, PanicIcon);
        }
        else
        {
            StatusIcon.Glyph = "\uE72E";
            StatusIcon.Foreground = FortivaControlTheme.GetBrush("FortivaMutedBrush", theme, StatusIcon);
            PanicIcon.Foreground = FortivaControlTheme.GetBrush("SystemFillColorCriticalBrush", theme, PanicIcon);
        }
    }

    private void UpdateTitleBarInsets()
    {
        if (AppTitleBar.XamlRoot?.RasterizationScale is not double scale || scale <= 0)
            return;

        try
        {
            var titleBar = AppWindow.TitleBar;
            var left = titleBar.LeftInset / scale;
            var right = titleBar.RightInset / scale;
            // Insets can be NaN/negative briefly when the window deactivates (e.g. user switches
            // to Edge for extension setup). GridLength throws and was crashing the whole app.
            if (!IsValidTitleBarInset(left) || !IsValidTitleBarInset(right))
                return;

            LeftPaddingColumn.Width = new GridLength(left);
            RightPaddingColumn.Width = new GridLength(right);
        }
        catch (Exception ex)
        {
            App.LogException("UpdateTitleBarInsets", ex);
        }
    }

    private static bool IsValidTitleBarInset(double value)
        => double.IsFinite(value) && value >= 0 && value <= 10_000;
}
