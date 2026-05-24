using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Admin;
using Fortiva.Core.Audit;
using Fortiva.Core.Licensing;
using Fortiva.Core.Policy;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Fortiva.AppHost.Admin;

public sealed partial class AdminMainWindow : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly AuditLogger _audit = AuditLogger.Default;
    private FortivaPolicy _policy;
    private List<SharedVaultDefinition> _sharedVaultEntries = [];

    public AdminMainWindow()
    {
        InitializeComponent();
        _policy = _vm.Policy ?? FortivaPolicy.StrictEnterprise;

        // Slider ranges must be set before any Value assignments —
        // same WinUI 3 XBF constraint ordering issue as SettingsPage.
        ArgonMemSlider.Maximum = 512;  ArgonMemSlider.Minimum = 64;  ArgonMemSlider.StepFrequency = 64;
        ArgonIterSlider.Maximum = 10;  ArgonIterSlider.Minimum = 1;  ArgonIterSlider.StepFrequency = 1;
        AutoLockSlider.Maximum = 600;  AutoLockSlider.Minimum = 30;  AutoLockSlider.StepFrequency = 30;
        ClipboardSlider.Maximum = 120; ClipboardSlider.Minimum = 5;  ClipboardSlider.StepFrequency = 5;

        LoadLicenseStatus();
        LoadPolicyToUI();
        LoadSharedVaults();
        MainTabView.SelectionChanged += MainTabView_SelectionChanged;
#if !DEBUG
        GenerateTrialBtn.Visibility = Visibility.Collapsed;
#endif
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAuditLog();
    }

    // ── License tab ──────────────────────────────────────────────────────────

    private void LoadLicenseStatus()
    {
        var lic = _vm.License;
        if (lic is null)
        {
            LicenseStatus.Text = "No license installed";
            LicenseStatusBanner.Background = new SolidColorBrush(Color.FromArgb(30, 200, 80, 0));
            return;
        }
        var valid = LicenseVerifier.IsValidAndNotExpired(lic);
        LicenseStatus.Text = valid ? $"Active - {lic.Document.CompanyName}" : "Invalid or expired";
        LicenseStatusBanner.Background = valid
            ? new SolidColorBrush(Color.FromArgb(30, 0, 180, 80))
            : new SolidColorBrush(Color.FromArgb(30, 200, 50, 50));
        LicenseDetail.Text =
            $"Edition:  {lic.Document.Edition}\n" +
            $"Company:  {lic.Document.CompanyName}\n" +
            $"Expires:  {lic.Document.ExpiresAt:yyyy-MM-dd}\n" +
            $"Seats:    {LicenseSeatRegistry.CountActiveSeats()} / {lic.Document.MaxSeats} in use\n" +
            $"Features: {string.Join(", ", lic.Document.FeatureFlags)}";
    }

    private async void ImportLicense_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".dat");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            var lic = LicenseStore.TryImportFromFile(file.Path);
            if (lic is null) throw new Exception("Could not parse or verify license file.");
            if (!LicenseVerifier.Verify(lic))
                throw new Exception("License signature is invalid.");
            if (lic.Document.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new Exception("License has expired.");
            LicenseStore.Save(lic);
            _vm.ReloadEnterpriseConfig();
            LoadLicenseStatus();
            ShowLicenseInfo("License imported and saved.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowLicenseInfo($"Import failed: {App.DescribeException(ex)}", InfoBarSeverity.Error);
        }
    }

    private void VerifyLicense_Click(object sender, RoutedEventArgs e)
    {
        var lic = _vm.License;
        var valid = LicenseVerifier.IsValidAndNotExpired(lic);
        ShowLicenseInfo(valid ? "License is valid." : "License is invalid or expired.", valid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void GenerateTrial_Click(object sender, RoutedEventArgs e)
    {
        var lic = LicenseVerifier.CreateDevLicense("Trial", DateTimeOffset.UtcNow.AddDays(30));
        LicenseStore.Save(lic);
        _vm.ReloadEnterpriseConfig();
        LoadLicenseStatus();
        ShowLicenseInfo("Dev trial license created (signature invalid - for testing only).", InfoBarSeverity.Warning);
    }

    private void ShowLicenseInfo(string msg, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        LicenseInfoBar.Message = msg;
        LicenseInfoBar.Severity = severity;
        LicenseInfoBar.IsOpen = true;
    }

    // ── Policy tab ───────────────────────────────────────────────────────────

    private void LoadPolicyToUI()
    {
        ArgonMemSlider.ValueChanged -= ArgonMem_Changed;
        ArgonIterSlider.ValueChanged -= ArgonIter_Changed;
        AutoLockSlider.ValueChanged -= AutoLock_Changed;
        ClipboardSlider.ValueChanged -= Clipboard_Changed;

        // Clamp all values to slider ranges before assignment
        ArgonMemSlider.Value  = Math.Clamp(_policy.MinArgon2MemoryKb / 1024.0, 64, 512);
        ArgonMemLabel.Text    = $"{(int)ArgonMemSlider.Value} MB";
        ArgonIterSlider.Value = Math.Clamp(_policy.MinArgon2Iterations, 1, 10);
        ArgonIterLabel.Text   = $"{(int)ArgonIterSlider.Value} iterations";
        AutoLockSlider.Value  = Math.Clamp(_policy.MaxAutoLockSeconds, 30, 600);
        AutoLockLabel.Text    = $"{(int)AutoLockSlider.Value}s";
        ClipboardSlider.Value = Math.Clamp(_policy.ClipboardClearSeconds, 5, 120);
        ClipboardLabel.Text   = $"{(int)ClipboardSlider.Value}s";

        ArgonMemSlider.ValueChanged += ArgonMem_Changed;
        ArgonIterSlider.ValueChanged += ArgonIter_Changed;
        AutoLockSlider.ValueChanged += AutoLock_Changed;
        ClipboardSlider.ValueChanged += Clipboard_Changed;

        ClipboardDisabledSwitch.IsOn = _policy.ClipboardDisabled;
        ParanoiaSwitch.IsOn = _policy.MandatoryParanoiaMode;
        HelloSwitch.IsOn = _policy.MandatoryWindowsHello;
        PortableSwitch.IsOn = _policy.PortableModeAllowed;
        TotpSwitch.IsOn = _policy.TotpEnabled;
        ExportModeBox.SelectedIndex = (int)_policy.ExportMode;
    }

    private void ArgonMem_Changed(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    { _policy.MinArgon2MemoryKb = (int)e.NewValue * 1024; ArgonMemLabel.Text = $"{(int)e.NewValue} MB"; }

    private void ArgonIter_Changed(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    { _policy.MinArgon2Iterations = (int)e.NewValue; ArgonIterLabel.Text = $"{(int)e.NewValue} iterations"; }

    private void AutoLock_Changed(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    { _policy.MaxAutoLockSeconds = (int)e.NewValue; AutoLockLabel.Text = $"{(int)e.NewValue}s"; }

    private void Clipboard_Changed(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    { _policy.ClipboardClearSeconds = (int)e.NewValue; ClipboardLabel.Text = $"{(int)e.NewValue}s"; }

    private void ValidatePolicy_Click(object sender, RoutedEventArgs e)
    {
        ApplyUIToPolicy();
        var errors = PolicyValidator.Validate(_policy);
        if (errors.Count == 0)
        {
            PolicyValidationBar.Message = "Policy is valid.";
            PolicyValidationBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            PolicyValidationBar.Message = string.Join("\n", errors);
            PolicyValidationBar.Severity = InfoBarSeverity.Error;
        }
        PolicyValidationBar.IsOpen = true;
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        ApplyUIToPolicy();
        var errors = PolicyValidator.Validate(_policy);
        if (errors.Count > 0)
        {
            PolicyValidationBar.Message = "Policy has errors:\n" + string.Join("\n", errors);
            PolicyValidationBar.Severity = InfoBarSeverity.Error;
            PolicyValidationBar.IsOpen = true;
            return;
        }
        PolicyStore.Save(_policy);
        _vm.ReloadPolicies();
        PolicyValidationBar.Message = "Policy saved and deployed to %PROGRAMDATA%\\Fortiva.";
        PolicyValidationBar.Severity = InfoBarSeverity.Success;
        PolicyValidationBar.IsOpen = true;
    }

    private void ResetPolicy_Click(object sender, RoutedEventArgs e)
    {
        _policy = FortivaPolicy.StrictEnterprise;
        LoadPolicyToUI();
    }

    private void ApplyUIToPolicy()
    {
        _policy.ClipboardDisabled = ClipboardDisabledSwitch.IsOn;
        _policy.MandatoryParanoiaMode = ParanoiaSwitch.IsOn;
        _policy.MandatoryWindowsHello = HelloSwitch.IsOn;
        _policy.PortableModeAllowed = PortableSwitch.IsOn;
        _policy.TotpEnabled = TotpSwitch.IsOn;
        _policy.ExportMode = (ExportPolicyMode)ExportModeBox.SelectedIndex;
    }

    // ── Shared vaults ────────────────────────────────────────────────────────

    private void LoadSharedVaults()
    {
        try
        {
            var vaults = SharedVaultStore.Load();
            _sharedVaultEntries = vaults?.Vaults ?? [];
            SharedVaultList.ItemsSource = _sharedVaultEntries
                .Select(v => $"{v.Name}  →  {v.StoragePath}")
                .ToList();
        }
        catch { }
    }

    private async void AddSharedVault_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Vault display name" };
        var pathBox = new TextBox { PlaceholderText = @"\\server\share\team.fva" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Display name" });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Vault path (local or UNC)" });
        panel.Children.Add(pathBox);
        var dlg = new ContentDialog
        {
            Title = "Add shared vault",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var cfg = SharedVaultStore.Load() ?? new SharedVaultConfiguration();
        cfg.Vaults.Add(new SharedVaultDefinition { Id = Guid.NewGuid(), Name = nameBox.Text, StoragePath = pathBox.Text });
        SharedVaultStore.Save(cfg);
        LoadSharedVaults();
    }

    private void RemoveSharedVault_Click(object sender, RoutedEventArgs e)
    {
        if (SharedVaultList.SelectedIndex < 0 || SharedVaultList.SelectedIndex >= _sharedVaultEntries.Count)
            return;

        _sharedVaultEntries.RemoveAt(SharedVaultList.SelectedIndex);
        SharedVaultStore.Save(new SharedVaultConfiguration { Vaults = _sharedVaultEntries });
        LoadSharedVaults();
    }

    // ── Audit tab ────────────────────────────────────────────────────────────

    private void LoadAuditLog()
    {
        var events = _audit.ReadRecent(200);
        AuditList.Items.Clear();

        foreach (var ev in events.OrderByDescending(e => e.Timestamp))
        {
            AuditList.Items.Add(new TextBlock
            {
                Text = $"{ev.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}  {ev.EventType,-20}  {ev.Message}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2)
            });
        }

        AuditCount.Text = $"{events.Count} events";
    }

    private void MainTabView_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is TabView { SelectedItem: TabViewItem item } &&
            item.Header?.ToString() == "Audit")
            LoadAuditLog();
    }

    private async void ExportAudit_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.SuggestedFileName = $"fortiva-audit-{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("JSONL", [".jsonl"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        _audit.ExportTo(file.Path);
    }
}
