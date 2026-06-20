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
    private IReadOnlyList<VaultDuplicateGroup> _vaultDuplicateGroups = [];
    private Guid? _selectedVaultDuplicateEntryId;

    public ImportExportPage() => InitializeComponent();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _stateChangedHandler = () => DispatcherQueue.TryEnqueue(() =>
        {
            RefreshExportState();
            RefreshImportHistory();
            RefreshVaultDuplicateScan();
        });
        _vm.StateChanged += _stateChangedHandler;
        RefreshExportState();
        RefreshImportHistory();
        RefreshVaultDuplicateScan();

        if (e.Parameter is ImportExportNavigationContext { FocusDuplicates: true })
        {
            RefreshVaultDuplicateScan();
            if (_vaultDuplicateGroups.Count == 0 && _vm.IsUnlocked)
                _vaultDuplicateGroups = _vm.GetVaultDuplicateGroups();
            RefreshVaultDuplicateScan();
            PageScroll.UpdateLayout();
            PageScroll.ChangeView(null, PageScroll.ScrollableHeight, null);
        }
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
        ReviewImportDuplicatesBtn.Visibility = Visibility.Collapsed;
        _selectedBatch = null;
    }

    private void RefreshVaultDuplicateScan()
    {
        if (!_vm.IsUnlocked)
        {
            VaultDuplicateSummary.Visibility = Visibility.Collapsed;
            VaultDuplicateList.Visibility = Visibility.Collapsed;
            OpenVaultDuplicateEntryBtn.Visibility = Visibility.Collapsed;
            _vaultDuplicateGroups = [];
            return;
        }

        _vaultDuplicateGroups = _vm.GetVaultDuplicateGroups();
        if (_vaultDuplicateGroups.Count == 0)
        {
            VaultDuplicateSummary.Text = "No duplicate login groups found in the vault.";
            VaultDuplicateSummary.Visibility = Visibility.Visible;
            VaultDuplicateList.Visibility = Visibility.Collapsed;
            OpenVaultDuplicateEntryBtn.Visibility = Visibility.Collapsed;
            return;
        }

        var exact = _vaultDuplicateGroups.Count(g => g.Kind == VaultDuplicateKind.Exact);
        var similar = _vaultDuplicateGroups.Count(g => g.Kind != VaultDuplicateKind.Exact);
        VaultDuplicateSummary.Text =
            $"Found {_vaultDuplicateGroups.Count} group(s): {exact} exact duplicate(s), {similar} similar (URL variations or different passwords).";
        VaultDuplicateSummary.Visibility = Visibility.Visible;
        VaultDuplicateList.ItemsSource = _vaultDuplicateGroups.Select(g => new VaultDuplicateRow(g)).ToList();
        VaultDuplicateList.Visibility = Visibility.Visible;
        OpenVaultDuplicateEntryBtn.Visibility = Visibility.Collapsed;
        _selectedVaultDuplicateEntryId = null;
        _selectedVaultDuplicateGroup = null;
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
            if (plan.SkippedDuplicateCount > 0)
                summary += " Review skipped duplicates in Import history below.";

            Show(summary, InfoBarSeverity.Success);
            RefreshImportHistory();
            RefreshVaultDuplicateScan();
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
            ReviewImportDuplicatesBtn.Visibility =
                row.Batch.SkippedDuplicateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            _selectedBatch = null;
            ViewImportEntriesBtn.Visibility = Visibility.Collapsed;
            ReviewImportDuplicatesBtn.Visibility = Visibility.Collapsed;
        }
    }

    private async void ReviewImportDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBatch is null || _selectedBatch.SkippedDuplicateCount <= 0)
            return;

        var rows = BuildImportDuplicateRows(_selectedBatch);
        var list = new ListView
        {
            ItemsSource = rows,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 320
        };

        var dialog = new ContentDialog
        {
            Title = $"Skipped duplicates — {_selectedBatch.ProvenanceLabel}",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "These rows matched existing vault entries and were not imported. Your original entries were kept.",
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    list
                }
            },
            PrimaryButtonText = "Open selected entry",
            SecondaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };
        FortivaDialogs.Configure(dialog, XamlRoot, themeHost: this);

        list.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is ImportDuplicateRow row
                && row.ExistingEntryId.HasValue;
        };
        dialog.IsPrimaryButtonEnabled = false;

        if (rows.FirstOrDefault(r => r.ExistingEntryId.HasValue) is { } firstOpenable)
        {
            list.SelectedItem = firstOpenable;
            dialog.IsPrimaryButtonEnabled = true;
        }

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;
            if (list.SelectedItem is not ImportDuplicateRow row || !row.ExistingEntryId.HasValue)
                return;
            if (!TryOpenEntry(row.ExistingEntryId.Value))
                Show("The original entry could not be found. It may have been renamed or removed.", InfoBarSeverity.Warning);
            return;
        }
    }

    private void ScanVaultDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked)
        {
            Show("Unlock the vault before scanning for duplicates.", InfoBarSeverity.Warning);
            return;
        }

        RefreshVaultDuplicateScan();
        if (_vaultDuplicateGroups.Count == 0)
            Show("No duplicate login groups found.", InfoBarSeverity.Success);
        else
            Show($"Found {_vaultDuplicateGroups.Count} duplicate group(s). Select one below to open an entry.", InfoBarSeverity.Informational);
    }

    private void VaultDuplicateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VaultDuplicateList.SelectedItem is not VaultDuplicateRow row)
        {
            _selectedVaultDuplicateEntryId = null;
            OpenVaultDuplicateEntryBtn.Visibility = Visibility.Collapsed;
            return;
        }

        _selectedVaultDuplicateGroup = row.Group;
        _selectedVaultDuplicateEntryId = row.FirstEntryId;
        OpenVaultDuplicateEntryBtn.Visibility = Visibility.Visible;
    }

    private sealed record DuplicateEntryOption(Guid Id, string Title, string Username)
    {
        public string Subtitle => string.IsNullOrWhiteSpace(Username) ? Id.ToString()[..8] : Username;
        public override string ToString() => $"{Title} · {Subtitle}";
    }

    private VaultDuplicateGroup? _selectedVaultDuplicateGroup;

    private async void OpenVaultDuplicateEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVaultDuplicateGroup is null)
            return;

        var group = _selectedVaultDuplicateGroup;
        if (group.EntryIds.Count == 1)
        {
            if (!TryOpenEntry(group.EntryIds[0]))
                Show("Entry could not be found.", InfoBarSeverity.Warning);
            return;
        }

        var options = group.EntryIds
            .Select(id =>
            {
                var vm = _vm.Entries.FirstOrDefault(e => e.Id == id);
                return vm is null ? null : new DuplicateEntryOption(id, vm.Title, vm.Username);
            })
            .Where(o => o is not null)
            .Cast<DuplicateEntryOption>()
            .ToList();

        if (options.Count == 0)
        {
            Show("Entries could not be found.", InfoBarSeverity.Warning);
            return;
        }

        var list = new ListView
        {
            ItemsSource = options,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 280
        };
        list.SelectedIndex = 0;

        var dlg = new ContentDialog
        {
            Title = "Open duplicate entry",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "This group contains multiple vault entries. Choose which one to open.",
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    list
                }
            },
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            IsPrimaryButtonEnabled = options.Count > 0
        };
        FortivaDialogs.Configure(dlg, XamlRoot, themeHost: this);
        list.SelectionChanged += (_, _) =>
        {
            dlg.IsPrimaryButtonEnabled = list.SelectedItem is DuplicateEntryOption;
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary || list.SelectedItem is not DuplicateEntryOption picked)
            return;

        if (!TryOpenEntry(picked.Id))
            Show("Entry could not be found.", InfoBarSeverity.Warning);
    }

    private bool TryOpenEntry(Guid entryId)
    {
        if (_vm.Entries.All(e => e.Id != entryId))
            return false;

        NavigationService.Current.ResetCurrent();
        if (!NavigationService.Current.Navigate<VaultPage>(
                VaultPageNavigationContext.ForEntry(entryId), animate: true))
            return false;

        _vm.RequestNavigationTab("Vault");
        return true;
    }

    private static List<ImportDuplicateRow> BuildImportDuplicateRows(ImportBatch batch)
    {
        if (batch.SkippedDuplicates.Count > 0)
        {
            return batch.SkippedDuplicates.Select(dup => new ImportDuplicateRow(
                dup.ExistingEntryId,
                dup.Title,
                string.IsNullOrWhiteSpace(dup.Username) ? dup.Url : dup.Username,
                "Kept existing vault entry")).ToList();
        }

        return
        [
            new ImportDuplicateRow(
                null,
                $"{batch.SkippedDuplicateCount} duplicate(s) skipped",
                "Details were not recorded for this older import",
                batch.ProvenanceLabel)
        ];
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

    private sealed class ImportDuplicateRow
    {
        public ImportDuplicateRow(Guid? existingEntryId, string title, string subtitle, string badge)
        {
            ExistingEntryId = existingEntryId;
            Title = title;
            Subtitle = subtitle;
            Badge = badge;
        }

        public Guid? ExistingEntryId { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string Badge { get; }
    }

    private sealed class VaultDuplicateRow
    {
        public VaultDuplicateRow(VaultDuplicateGroup group)
        {
            Group = group;
            FirstEntryId = group.EntryIds[0];
        }

        public VaultDuplicateGroup Group { get; }
        public Guid FirstEntryId { get; }
        public string Title => Group.Title;
        public string Subtitle => Group.Kind switch
        {
            VaultDuplicateKind.Exact =>
                $"{Group.Username} · {Group.EntryIds.Count} exact duplicates",
            VaultDuplicateKind.SimilarSite =>
                $"{Group.Username} · {Group.EntryIds.Count} entries (same site, different URL variations)",
            _ =>
                $"{Group.Username} · {Group.EntryIds.Count} entries (same site + username, different passwords)"
        };
    }
}
