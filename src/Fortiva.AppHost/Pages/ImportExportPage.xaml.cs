using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.ImportExport;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Fortiva.AppHost.Pages;

public sealed partial class ImportExportPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private Action? _stateChangedHandler;
    private ImportBatch? _selectedBatch;

    public ImportExportPage() => InitializeComponent();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _stateChangedHandler = () => DispatcherQueue.TryEnqueue(() =>
        {
            RefreshExportState();
            RefreshImportHistory();
        });
        _vm.StateChanged += _stateChangedHandler;
        RefreshExportState();
        RefreshImportHistory();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_stateChangedHandler is not null)
        {
            _vm.StateChanged -= _stateChangedHandler;
            _stateChangedHandler = null;
        }
    }

    private void RefreshExportState()
    {
        var canPlaintext = PolicyEnforcer.CanExportPlaintext(_vm.Policy);
        ExportCsvBtn.IsEnabled = canPlaintext && _vm.IsUnlocked && !_vm.IsReadOnly;
        ExportEncryptedBtn.IsEnabled = _vm.IsUnlocked && !_vm.IsReadOnly;
        ImportBtn.IsEnabled = _vm.IsUnlocked && !_vm.IsReadOnly;
    }

    private void RefreshImportHistory()
    {
        var batches = _vm.ImportHistory()
            .Select(b => new ImportHistoryRow(b))
            .ToList();

        ImportHistoryList.ItemsSource = batches;
        ImportHistoryList.Visibility = batches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportHistoryHint.Visibility = batches.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ViewImportEntriesBtn.Visibility = Visibility.Collapsed;
        _selectedBatch = null;
    }

    private bool GuardImport()
    {
        if (!_vm.IsUnlocked)
        {
            Show("Unlock the vault before importing.", InfoBarSeverity.Warning);
            return false;
        }
        if (_vm.IsReadOnly)
        {
            Show("Vault is read-only.", InfoBarSeverity.Warning);
            return false;
        }
        return true;
    }

    private bool GuardExport()
    {
        if (!_vm.IsUnlocked)
        {
            Show("Unlock the vault before exporting.", InfoBarSeverity.Warning);
            return false;
        }
        if (_vm.IsReadOnly)
        {
            Show("Vault is read-only.", InfoBarSeverity.Warning);
            return false;
        }
        return true;
    }

    private static ImportFormatKind SelectedFormat(int index) => index switch
    {
        1 => ImportFormatKind.KeePassCsv,
        2 => ImportFormatKind.BrowserCsv,
        3 => ImportFormatKind.AppleKeychainCsv,
        4 => ImportFormatKind.EncryptedBackup,
        _ => ImportFormatKind.GenericCsv
    };

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardImport()) return;

        var format = SelectedFormat(ImportFormatBox.SelectedIndex);
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        if (format == ImportFormatKind.EncryptedBackup)
        {
            picker.FileTypeFilter.Add(".fva");
            picker.FileTypeFilter.Add(".fvab");
            picker.FileTypeFilter.Add(".json");
        }
        else
        {
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".txt");
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        string? backupPassword = null;
        if (format == ImportFormatKind.EncryptedBackup)
        {
            var pwdBox = new PasswordBox { PlaceholderText = "Backup file password" };
            FortivaControlTheme.ApplyPasswordBox(pwdBox);
            var pwdDlg = new ContentDialog
            {
                Title = "Decrypt backup",
                Content = pwdBox,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            FortivaDialogs.Configure(pwdDlg, Content.XamlRoot);
            if (await pwdDlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrEmpty(pwdBox.Password))
            {
                Show("Backup password required.", InfoBarSeverity.Warning);
                return;
            }
            backupPassword = pwdBox.Password;
        }

        try
        {
            using var stream = await file.OpenStreamForReadAsync();
            var incoming = VaultImporter.ImportCredentials(stream, format, backupPassword);
            if (incoming.Count == 0)
            {
                Show("No entries found in the file.", InfoBarSeverity.Warning);
                return;
            }

            var existing = _vm.Context?.Payload.Entries ?? [];
            var preview = ImportMergeService.Analyze(existing, incoming);
            if (!await ConfirmImportPreviewAsync(preview, file.Name))
                return;

            if (preview.ConflictCount > 0 && !await ResolveConflictsAsync(preview))
                return;

            var metadata = await PromptImportMetadataAsync(format, file.Name);
            if (metadata is null)
                return;

            var sourceLabel = ImportSourceLabels.For(format);
            var plan = ImportMergeService.BuildApplyPlan(preview, sourceLabel, format.ToString(), file.Name, metadata);
            _vm.ApplyImport(plan);

            var summary =
                $"Import complete: {plan.Batch.AddedCount} added";
            if (plan.SkippedDuplicateCount > 0)
                summary += $", {plan.SkippedDuplicateCount} duplicates skipped (existing entries kept)";
            if (plan.ConflictKeptExistingCount + plan.ConflictUpdatedCount + plan.ConflictKeptBothCount > 0)
                summary += $", {plan.ConflictKeptExistingCount + plan.ConflictUpdatedCount + plan.ConflictKeptBothCount} conflicts resolved";
            summary += ". No existing entries were removed.";

            Show(summary, InfoBarSeverity.Success);
            RefreshImportHistory();
        }
        catch (Exception ex)
        {
            Show($"Import failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task<ImportBatchMetadata?> PromptImportMetadataAsync(ImportFormatKind format, string fileName)
    {
        var defaultName = ImportSourceLabels.SuggestDisplayName(format, fileName);

        var nameBox = new TextBox
        {
            Text = defaultName,
            PlaceholderText = "e.g. Old laptop passwords, Work Chrome export",
            MaxLength = ImportBatchMetadata.MaxDisplayNameLength
        };
        FortivaControlTheme.ApplyTextBox(nameBox);

        var sourceBox = new TextBox
        {
            PlaceholderText = "e.g. Home PC · Chrome · March 2024 backup",
            MaxLength = ImportBatchMetadata.MaxSourceHintLength
        };
        FortivaControlTheme.ApplyTextBox(sourceBox);

        var notesBox = new TextBox
        {
            PlaceholderText = "Why you imported this, where the file came from, anything useful later…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            MaxLength = ImportBatchMetadata.MaxNotesLength
        };
        FortivaControlTheme.ApplyTextBox(notesBox);

        var panel = new StackPanel { Spacing = 12, MinWidth = 360, MaxWidth = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = "Name this import so you can find it later in Import history and on vault entries.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Style = (Style)Application.Current.Resources["FortivaMutedText"],
            FontSize = 13
        });
        panel.Children.Add(new TextBlock { Text = "Import name", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Source (optional)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(sourceBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Notes (optional)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(notesBox);

        var scroll = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            Title = "Name this import",
            Content = scroll,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dialog, Content.XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        try
        {
            return ImportBatchMetadata.Create(
                nameBox.Text,
                sourceBox.Text,
                notesBox.Text,
                defaultName);
        }
        catch (ArgumentException ex)
        {
            Show(ex.Message, InfoBarSeverity.Warning);
            return null;
        }
    }

    private async Task<bool> ConfirmImportPreviewAsync(ImportPreview preview, string fileName)
    {
        var text =
            $"File: {fileName}\n\n" +
            $"• {preview.NewCount} new entries to add\n" +
            $"• {preview.DuplicateCount} duplicates (same site + username + password) — will be skipped\n" +
            $"• {preview.ConflictCount} conflicts (same site + username, different password)";

        if (preview.ConflictCount > 0)
            text += " — you'll choose what to keep next";
        text += "\n\nNothing is overwritten without your explicit choice.";

        var dialog = new ContentDialog
        {
            Title = "Review import",
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.WrapWholeWords },
            PrimaryButtonText = preview.ConflictCount > 0 ? "Resolve conflicts…" : "Import now",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dialog, Content.XamlRoot);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ResolveConflictsAsync(ImportPreview preview)
    {
        var conflicts = preview.Items.Where(i => i.Kind == ImportItemKind.Conflict).ToList();
        var panel = new StackPanel { Spacing = 12, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "Same login found with a different password. Choose what to keep for each:",
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var combos = new List<ComboBox>();
        foreach (var item in conflicts)
        {
            var host = ImportMergeService.ExtractHost(item.Incoming.Entry.Url);
            var label = string.IsNullOrWhiteSpace(host) ? item.Incoming.Entry.Title : host;
            var block = new StackPanel { Spacing = 4 };
            block.Children.Add(new TextBlock
            {
                Text = $"{label} · {item.Incoming.Entry.Username}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            block.Children.Add(new TextBlock
            {
                Text = $"Existing password vs import from {item.Incoming.Entry.Title}",
                Style = (Style)Application.Current.Resources["FortivaMutedText"],
                FontSize = 12
            });

            var combo = new ComboBox { Width = 360, SelectedIndex = 0 };
            combo.Items.Add(new ComboBoxItem { Content = "Keep existing password in Fortiva" });
            combo.Items.Add(new ComboBoxItem { Content = "Use imported password" });
            combo.Items.Add(new ComboBoxItem { Content = "Keep both (add imported as separate entry)" });
            combos.Add(combo);
            block.Children.Add(combo);
            panel.Children.Add(block);
        }

        var scroll = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            Title = $"Resolve {conflicts.Count} conflict(s)",
            Content = scroll,
            PrimaryButtonText = "Apply choices",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dialog, Content.XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return false;

        for (var i = 0; i < conflicts.Count; i++)
        {
            conflicts[i].Resolution = combos[i].SelectedIndex switch
            {
                1 => ImportConflictChoice.UseImported,
                2 => ImportConflictChoice.KeepBoth,
                _ => ImportConflictChoice.KeepExisting
            };
        }

        return true;
    }

    private void ImportHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImportHistoryList.SelectedItem is ImportHistoryRow row)
        {
            _selectedBatch = row.Batch;
            ViewImportEntriesBtn.Visibility = Visibility.Visible;
        }
        else
        {
            _selectedBatch = null;
            ViewImportEntriesBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void ViewImportEntries_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBatch is null) return;
        _vm.VaultImportBatchFilter = _selectedBatch.Id;
        _vm.RequestNavigationTab("Vault");
        NavigationService.Current.Navigate<VaultPage>(VaultPageNavigationContext.ForImportBatch(_selectedBatch.Id));
    }

    private async void ExportEncrypted_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardExport()) return;
        var pwdBox = new PasswordBox { PlaceholderText = "Export password (to protect the backup file)" };
        FortivaControlTheme.ApplyPasswordBox(pwdBox);
        var dlg = new ContentDialog
        {
            Title = "Encrypt backup",
            Content = pwdBox,
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot);
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
            Show($"Export failed: {App.DescribeException(ex)}", InfoBarSeverity.Error);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardExport()) return;

        if (!PolicyEnforcer.CanExportPlaintext(_vm.Policy))
        {
            _vm.LogPolicyViolation("Plaintext CSV export blocked by policy");
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
        FortivaDialogs.Configure(warn, Content.XamlRoot);
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
            var csv = VaultExporter.ExportPlaintextCsv(_vm.Context, _vm.Policy);
            await Windows.Storage.FileIO.WriteTextAsync(file, csv);
            Show($"Plaintext CSV saved to {file.Name}. Delete this file when done.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Show($"Export failed: {App.DescribeException(ex)}", InfoBarSeverity.Error);
        }
    }

    private void Show(string msg, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Message = msg;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static IntPtr GetHwnd() => App.MainWindowHandle;

    private sealed class ImportHistoryRow
    {
        public ImportHistoryRow(ImportBatch batch) => Batch = batch;
        public ImportBatch Batch { get; }
        public string Title => $"{Batch.ProvenanceLabel} · {Batch.ImportedAt.LocalDateTime:g}";
        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Batch.SourceHint))
                    parts.Add(Batch.SourceHint!.Trim());
                if (!string.IsNullOrWhiteSpace(Batch.Notes))
                {
                    var note = Batch.Notes!.Trim();
                    if (note.Length > 80)
                        note = note[..77] + "…";
                    parts.Add(note);
                }

                var counts =
                    $"+{Batch.AddedCount} added" +
                    (Batch.SkippedDuplicateCount > 0 ? $", {Batch.SkippedDuplicateCount} duplicates skipped" : "") +
                    (Batch.ConflictUpdatedCount + Batch.ConflictKeptExistingCount + Batch.ConflictKeptBothCount > 0
                        ? $", {Batch.ConflictUpdatedCount + Batch.ConflictKeptExistingCount + Batch.ConflictKeptBothCount} conflicts resolved"
                        : "");

                if (!string.IsNullOrWhiteSpace(Batch.FileName))
                    parts.Add(Batch.FileName!);

                if (!string.IsNullOrWhiteSpace(Batch.SourceLabel)
                    && !string.Equals(Batch.SourceLabel, Batch.ProvenanceLabel, StringComparison.OrdinalIgnoreCase))
                    parts.Add(Batch.SourceLabel);

                parts.Add(counts);
                return string.Join(" · ", parts);
            }
        }
    }
}
