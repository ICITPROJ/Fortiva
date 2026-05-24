using Fortiva.AppHost.ViewModels;
using Fortiva.Core.ImportExport;
using Fortiva.Core.Policy;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Fortiva.AppHost.Pages;

public sealed partial class ImportExportPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;

    public ImportExportPage() => InitializeComponent();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var canPlaintext = PolicyEnforcer.CanExportPlaintext(_vm.Policy);
        ExportCsvBtn.IsEnabled = canPlaintext;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked)
        {
            Show("Unlock the vault before importing.", InfoBarSeverity.Warning);
            return;
        }
        if (_vm.IsReadOnly) { Show("Vault is read-only.", InfoBarSeverity.Warning); return; }

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".txt");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            using var stream = await file.OpenStreamForReadAsync();
            var entries = ImportFormatBox.SelectedIndex switch
            {
                1 => KeePassImporter.ImportFromKeePassCsv(stream),
                _ => CsvImporter.ImportFromCsv(stream)
            };
            if (entries.Count == 0)
            {
                Show("No entries found in the file.", InfoBarSeverity.Warning);
                return;
            }
            _vm.BulkImport(entries);
            Show($"{entries.Count} entries imported successfully.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show($"Import failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ExportEncrypted_Click(object sender, RoutedEventArgs e)
    {
        var pwdBox = new PasswordBox { PlaceholderText = "Export password (to protect the backup file)" };
        var dlg = new ContentDialog
        {
            Title = "Encrypt backup",
            Content = pwdBox,
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrEmpty(pwdBox.Password)) { Show("Export password required.", InfoBarSeverity.Warning); return; }

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        picker.SuggestedFileName = $"fortiva-backup-{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("Fortiva Vault Backup", [".fva"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            if (_vm.Context is null) { Show("Vault not unlocked.", InfoBarSeverity.Error); return; }
            var bytes = VaultExporter.ExportEncrypted(_vm.Context, pwdBox.Password);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            Show("Encrypted backup saved.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!PolicyEnforcer.CanExportPlaintext(_vm.Policy))
        {
            Show("Plaintext export is disabled by policy.", InfoBarSeverity.Error);
            return;
        }

        var warn = new ContentDialog
        {
            Title = "Security warning",
            Content = new TextBlock
            {
                Text = "You are about to export all passwords as a plaintext CSV file.\n\n" +
                       "Anyone with access to this file can read all your passwords.\n\n" +
                       "Only proceed if you need to migrate to another password manager.",
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = "Export plaintext CSV",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await warn.ShowAsync() != ContentDialogResult.Primary) return;

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        picker.SuggestedFileName = $"fortiva-export-{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("CSV File", [".csv"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            if (_vm.Context is null) { Show("Vault not unlocked.", InfoBarSeverity.Error); return; }
            var csv = VaultExporter.ExportPlaintextCsv(_vm.Context);
            await Windows.Storage.FileIO.WriteTextAsync(file, csv);
            Show($"Plaintext CSV saved to {file.Name}. Delete this file when done.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Show($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void Show(string msg, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Message = msg;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static IntPtr GetHwnd() => App.MainWindowHandle;
}
