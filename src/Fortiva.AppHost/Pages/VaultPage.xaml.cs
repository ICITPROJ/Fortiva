using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Otp;
using Fortiva.Core.Vault;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fortiva.AppHost.Pages;

public sealed partial class VaultPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly ClipboardService _clipboard;

    private Action? _stateChangedHandler;
    private Action? _vaultLocationHandler;
    private string _selectedCategoryKey = VaultCategoryFilter.AllKey;
    private Guid? _importBatchFilter;
    private bool _categoryListInitialized;
    private bool _listViewMode;
    private VaultEntryViewModel? _selectedEntry;
    private VaultEntryPaneHost? _paneHost;
    private CancellationTokenSource? _faviconCts;
    private bool _syncingSelection;
    private Guid? _pendingRestoreEntryId;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchDebounceTimer;
    private const double MasterDetailMinWidth = 1080;

    public VaultPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _vm.ThemeChanged += OnThemeChanged;
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        _stateChangedHandler = () => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyReadOnlyChrome();
            RefreshList();
        });
        _vm.StateChanged += _stateChangedHandler;
        _vaultLocationHandler = () => DispatcherQueue.TryEnqueue(() => RefreshList());
        _vm.VaultLocationChanged += _vaultLocationHandler;
        _vm.ConfirmDiscardBeforeLockAsync = async () =>
        {
            if (_paneHost?.ConfirmCloseAsync is { } confirm)
                return await confirm();
            return true;
        };
        ReadOnlyBar.IsOpen = _vm.IsReadOnly;
        if (_vm.IsReadOnly && !string.IsNullOrEmpty(_vm.Session?.RollbackWarning))
            ReadOnlyBar.Message = _vm.Session.RollbackWarning +
                " You can view entries but not edit. Use Enable editing below to confirm and unlock write access.";

        var pendingQuickAdd = false;
        Guid? openEntryId = null;
        if (e.Parameter is VaultPageNavigationContext ctx)
        {
            if (!string.IsNullOrWhiteSpace(ctx.SearchQuery))
                SearchBox.Text = ctx.SearchQuery;
            _importBatchFilter = ctx.ImportBatchId ?? _vm.VaultImportBatchFilter;
            pendingQuickAdd = ctx.QuickAdd;
            openEntryId = ctx.OpenEntryId;
        }
        else if (e.Parameter is Guid batchId)
        {
            _importBatchFilter = batchId;
        }
        else
        {
            _importBatchFilter = _vm.VaultImportBatchFilter;
            var pendingSearch = _vm.ConsumePendingVaultSearch();
            if (!string.IsNullOrWhiteSpace(pendingSearch))
                SearchBox.Text = pendingSearch;
        }

        if (_importBatchFilter.HasValue)
            _vm.VaultImportBatchFilter = _importBatchFilter;

        _listViewMode = _vm.PersonalSettings.VaultUseListView;
        GridViewBtn.IsChecked = !_listViewMode;
        ListViewBtn.IsChecked = _listViewMode;
        ApplyViewToggleChrome();

        ApplyReadOnlyChrome();

        RefreshCategories();
        RefreshList();

        var entryToOpen = openEntryId;
        if (entryToOpen is null)
            entryToOpen = _vm.ConsumePendingOpenVaultEntryId();
        else
            _vm.PendingOpenVaultEntryId = null;

        if (entryToOpen is { } entryId)
            DispatcherQueue.TryEnqueue(() => TryOpenEntry(entryId));
        else if (pendingQuickAdd)
            _ = QuickAddAsync();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
        if (_stateChangedHandler is not null)
        {
            _vm.StateChanged -= _stateChangedHandler;
            _stateChangedHandler = null;
        }
        if (_vaultLocationHandler is not null)
        {
            _vm.VaultLocationChanged -= _vaultLocationHandler;
            _vaultLocationHandler = null;
        }

        if (_vm.ConfirmDiscardBeforeLockAsync is not null)
            _vm.ConfirmDiscardBeforeLockAsync = null;

        _faviconCts?.Cancel();
        _faviconCts = null;
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = null;
        HideDetailPaneCore();
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var theme = FortivaControlTheme.ResolveHostTheme(this);
            ThemeService.ApplyToElement(this);
            FortivaControlTheme.ApplyAutoSuggestBox(SearchBox, this, theme);
            ApplyViewToggleChrome();
            if (DetailEditorFrame.Content is FrameworkElement detailContent)
                ThemeService.ApplyToElement(detailContent);
        });
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < MasterDetailMinWidth)
        {
            if (_selectedEntry is not null)
                _pendingRestoreEntryId = _selectedEntry.Id;
            _ = HideDetailPaneAsync(force: true);
            return;
        }

        if (e.NewSize.Width >= MasterDetailMinWidth && _pendingRestoreEntryId is { } restoreId)
        {
            _pendingRestoreEntryId = null;
            TryOpenEntry(restoreId);
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Windows.System.VirtualKey.N &&
            KeyboardHelpers.IsControlDown() &&
            !KeyboardHelpers.IsShiftDown())
        {
            e.Handled = true;
            _ = QuickAddAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.G && KeyboardHelpers.IsControlDown())
        {
            e.Handled = true;
            _ = GeneratePasswordAsync();
        }
    }

    private void RefreshList()
    {
        var q = SearchBox.Text?.Trim();
        IEnumerable<VaultEntryViewModel> source = string.IsNullOrEmpty(q) ? _vm.Entries : _vm.Search(q);
        if (_importBatchFilter is { } batchFilter)
            source = source.Where(e => e.ImportBatchId == batchFilter);
        source = VaultCategoryFilter.Apply(source, _selectedCategoryKey);

        var list = source
            .OrderByDescending(e => e.IsFavorite)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keepDetailId = DetailPane.Visibility == Visibility.Visible ? _selectedEntry?.Id : null;

        EntryGrid.ItemsSource = list;
        EntryList.ItemsSource = list;

        if (keepDetailId is { } detailId)
        {
            var refreshed = list.FirstOrDefault(e => e.Id == detailId);
            if (refreshed is not null)
            {
                SelectSingleEntry(refreshed);
                _selectedEntry = refreshed;
            }
            else
            {
                HideDetailPaneCore();
            }
        }

        var total = _vm.Entries.Count;
        var favorites = _vm.Entries.Count(e => e.IsFavorite);
        var totp = _vm.Entries.Count(e => e.HasTotp);

        ClearImportFilterBtn.Visibility = _importBatchFilter.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_importBatchFilter is { } filterId)
        {
            var batch = _vm.ImportHistory().FirstOrDefault(b => b.Id == filterId);
            VaultSubtitle.Text = batch is null
                ? "Showing entries from one import"
                : FormatImportFilterSubtitle(batch);
        }
        else
        {
            VaultSubtitle.Text = total == 0
                ? "Encrypted on this device · nothing leaves your PC"
                : $"{total} {(total == 1 ? "entry" : "entries")} · favorites first · tap to open";
        }

        StatFavorites.Text = $"{favorites} fav{(favorites == 1 ? "" : "s")}";
        StatTotp.Text = $"{totp} · 2FA";
        StatVaultTrust.Text = _vm.VaultTrustChipText;

        var showing = list.Count;
        var categoryLabel = GetSelectedCategoryLabel();
        CountText.Text = string.IsNullOrEmpty(q)
            ? (total == 0
                ? "No entries saved yet"
                : $"Showing {showing} entries in {categoryLabel}")
            : $"Showing {showing} of {total} entries in {categoryLabel} matching “{q}”";

        var vaultEmpty = total == 0;
        var categoryEmpty = !vaultEmpty && showing == 0;

        EmptyState.Visibility = vaultEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyCategoryState.Visibility = categoryEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (showing > 0)
        {
            EntryGrid.Visibility = _listViewMode ? Visibility.Collapsed : Visibility.Visible;
            EntryList.Visibility = _listViewMode ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            EntryGrid.Visibility = Visibility.Collapsed;
            EntryList.Visibility = Visibility.Collapsed;
        }

        PrefetchFavicons(list);
    }

    private void PrefetchFavicons(IReadOnlyList<VaultEntryViewModel> entries)
    {
        foreach (var entry in entries)
            entry.RefreshCachedFavicon();

        _faviconCts?.Cancel();
        _faviconCts = new CancellationTokenSource();
        var token = _faviconCts.Token;
        var urls = entries.Select(e => e.Url).ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                await EntryFaviconService.PrefetchAsync(urls, (host, path) =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (var vm in entries)
                        {
                            if (string.Equals(TryGetHost(vm.Url), host, StringComparison.OrdinalIgnoreCase))
                                vm.SetFaviconPath(path);
                        }
                    });
                }, token);
            }
            catch (OperationCanceledException)
            {
                /* navigated away */
            }
        }, token);
    }

    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try { return new Uri(url.Trim()).Host.ToLowerInvariant(); }
        catch { return null; }
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        _listViewMode = ReferenceEquals(sender, ListViewBtn);
        GridViewBtn.IsChecked = !_listViewMode;
        ListViewBtn.IsChecked = _listViewMode;
        _vm.PersonalSettings.VaultUseListView = _listViewMode;
        _vm.SavePersonalSettings();
        ApplyViewToggleChrome();
        ClearEntrySelection();
        BulkActionBar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Visible;
        HideDetailPane();
        RefreshList();
    }

    private void ApplyViewToggleChrome()
    {
        var theme = FortivaControlTheme.ResolveEffectiveTheme(GridViewBtn.XamlRoot, GridViewBtn);
        var accent = FortivaControlTheme.GetBrush("FortivaAccentGlowBrush", theme, GridViewBtn);
        var glass = FortivaControlTheme.GetBrush("FortivaGlassFillBrush", theme, GridViewBtn);
        var border = FortivaControlTheme.GetBrush("FortivaGlassBorderBrush", theme, GridViewBtn);
        var accentFg = FortivaControlTheme.GetBrush("FortivaAccentBrush", theme, GridViewBtn);

        GridViewBtn.Background = GridViewBtn.IsChecked == true ? accent : glass;
        GridViewBtn.BorderBrush = GridViewBtn.IsChecked == true ? accentFg : border;
        GridViewBtn.Foreground = GridViewBtn.IsChecked == true ? accentFg : FortivaControlTheme.GetBrush("FortivaMutedBrush", theme, GridViewBtn);

        ListViewBtn.Background = ListViewBtn.IsChecked == true ? accent : glass;
        ListViewBtn.BorderBrush = ListViewBtn.IsChecked == true ? accentFg : border;
        ListViewBtn.Foreground = ListViewBtn.IsChecked == true ? accentFg : FortivaControlTheme.GetBrush("FortivaMutedBrush", theme, ListViewBtn);
    }

    private void EntryContainer_ContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
            return;

        args.RegisterUpdateCallback(static (_, itemArgs) =>
        {
            if (itemArgs.ItemContainer.ContentTemplateRoot is Border card)
                FortivaSurfaceEffects.ApplyHoverLift(card);
        });
    }

    private void EntryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _ = OnEntrySelectionChangedAsync(EntryGrid.SelectedItems.Cast<VaultEntryViewModel>().ToList());

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _ = OnEntrySelectionChangedAsync(EntryList.SelectedItems.Cast<VaultEntryViewModel>().ToList());

    private async Task OnEntrySelectionChangedAsync(IReadOnlyList<VaultEntryViewModel> selected)
    {
        if (_syncingSelection)
            return;

        if (selected.Count > 1)
        {
            if (!await HideDetailPaneAsync())
            {
                RestoreSingleSelection(_selectedEntry);
                return;
            }

            BulkActionBar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Collapsed;
            BulkCountText.Text = $"{selected.Count} entries selected";
            return;
        }

        BulkActionBar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Visible;

        if (selected.Count == 1)
        {
            if (DetailPane.Visibility == Visibility.Visible
                && _selectedEntry?.Id != selected[0].Id
                && !await HideDetailPaneAsync())
            {
                RestoreSingleSelection(_selectedEntry);
                return;
            }

            if (UseMasterDetail())
                ShowDetail(selected[0]);
            else
                NavigateToEntry(selected[0]);
            return;
        }

        await HideDetailPaneAsync();
    }

    private void RestoreSingleSelection(VaultEntryViewModel? vm)
    {
        if (vm is null)
            return;

        SelectSingleEntry(vm);
    }

    private void SelectSingleEntry(VaultEntryViewModel vm)
    {
        _syncingSelection = true;
        try
        {
            if (_listViewMode)
            {
                EntryList.SelectedItems.Clear();
                EntryList.SelectedItems.Add(vm);
            }
            else
            {
                EntryGrid.SelectedItems.Clear();
                EntryGrid.SelectedItems.Add(vm);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void RefreshCategories()
    {
        var categories = VaultCategoryFilter.BuildCategories(_vm.Entries, _vm.PersonalSettings.VaultCategories);
        CategoryList.SelectionChanged -= CategoryList_SelectionChanged;
        CategoryList.ItemsSource = categories;

        var selectedIndex = categories.ToList().FindIndex(c => c.Key == _selectedCategoryKey);
        if (selectedIndex < 0)
        {
            _selectedCategoryKey = VaultCategoryFilter.AllKey;
            selectedIndex = 0;
        }

        if (categories.Count > 0)
            CategoryList.SelectedIndex = selectedIndex;

        CategoryList.SelectionChanged += CategoryList_SelectionChanged;
        _categoryListInitialized = true;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_categoryListInitialized)
            return;
        if (CategoryList.SelectedItem is not VaultCategoryItem item)
            return;
        if (item.Key == _selectedCategoryKey)
            return;

        _selectedCategoryKey = item.Key;
        RefreshList();
    }

    private IReadOnlyList<string> GetPreselectedTagsForNewEntry()
        => VaultCategoryFilter.IsUserTag(_selectedCategoryKey) ? [_selectedCategoryKey] : [];

    private async void NewCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReadOnly)
        {
            await ShowInfoAsync("Vault is read-only.");
            return;
        }

        var tag = await VaultCategoryDialog.ShowCreateAsync(Content.XamlRoot, _vm, themeHost: this);
        if (tag is null)
            return;

        _selectedCategoryKey = tag;
        RefreshCategories();
        RefreshList();
        _vm.StatusMessage = $"Category “{tag}” created.";
    }

    private string GetSelectedCategoryLabel()
    {
        if (CategoryList.SelectedItem is VaultCategoryItem item)
            return item.Label.ToLowerInvariant();
        return "all entries";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchDebounceTimer ??= DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        _searchDebounceTimer.Start();
    }

    private void SearchDebounceTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        RefreshList();
    }

    private bool UseMasterDetail() => ActualWidth >= MasterDetailMinWidth;

    private void ShowDetail(VaultEntryViewModel vm)
    {
        _selectedEntry = vm;
        DetailColumn.Width = new GridLength(420);
        DetailPane.Visibility = Visibility.Visible;

        _paneHost = new VaultEntryPaneHost
        {
            CloseRequested = () => DispatcherQueue.TryEnqueue(HideDetailPaneCore),
            Saved = () => DispatcherQueue.TryEnqueue(() =>
            {
                RefreshList();
                var refreshed = _vm.Entries.FirstOrDefault(e => e.Id == vm.Id);
                if (refreshed is not null && UseMasterDetail())
                    ShowDetail(refreshed);
            })
        };

        DetailEditorFrame.Content = null;
        DetailEditorFrame.Navigate(
            typeof(EntryPage),
            new EntryPaneNavigationContext(vm.Entry, _paneHost));
    }

    private async Task<bool> HideDetailPaneAsync(bool force = false)
    {
        if (!force && _paneHost?.ConfirmCloseAsync is { } confirm && !await confirm())
            return false;

        HideDetailPaneCore();
        return true;
    }

    private void HideDetailPane() => _ = HideDetailPaneAsync(force: true);

    private void HideDetailPaneCore()
    {
        _selectedEntry = null;
        _paneHost = null;
        DetailColumn.Width = new GridLength(0);
        DetailPane.Visibility = Visibility.Collapsed;
        ClearEntrySelection();
    }

    private async void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        if (await HideDetailPaneAsync())
            _pendingRestoreEntryId = null;
    }

    private void ClearEntrySelection()
    {
        _syncingSelection = true;
        try
        {
            EntryGrid.SelectedItems.Clear();
            EntryList.SelectedItems.Clear();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private IReadOnlyList<VaultEntryViewModel> GetBulkSelection()
    {
        var source = _listViewMode ? EntryList.SelectedItems : EntryGrid.SelectedItems;
        return source.Cast<VaultEntryViewModel>().ToList();
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReadOnly)
        {
            await ShowInfoAsync("Vault is read-only.");
            return;
        }

        var selected = GetBulkSelection();
        if (selected.Count == 0)
            return;

        var dlg = new ContentDialog
        {
            Title = "Delete entries?",
            Content = new TextBlock
            {
                Text = $"Permanently delete {selected.Count} entries?",
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot, themeHost: this);
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            var count = _vm.BulkDeleteEntries(selected.Select(s => s.Id));
            BulkActionBar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Visible;
            HideDetailPane();
            RefreshList();
            _vm.StatusMessage = $"Deleted {count} entries.";
        }
        catch (Exception ex)
        {
            await ShowInfoAsync(ex.Message);
        }
    }

    private async void BulkTag_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReadOnly)
        {
            await ShowInfoAsync("Vault is read-only.");
            return;
        }

        var selected = GetBulkSelection();
        if (selected.Count == 0)
            return;

        var tag = await VaultCategoryDialog.ShowCreateAsync(Content.XamlRoot, _vm, title: "Add tag to selection", themeHost: this);
        if (tag is null)
            return;

        try
        {
            var (updated, skippedAlready, skippedMax) = _vm.BulkAddTag(selected.Select(s => s.Id), tag);
            RefreshCategories();
            RefreshList();
            _vm.StatusMessage = updated > 0
                ? $"Tagged {updated} entries with “{tag}”."
                : skippedMax > 0
                    ? $"No entries updated — {skippedMax} already at the tag limit."
                    : "No entries were updated (tag may already apply).";
            if (updated > 0 && (skippedAlready > 0 || skippedMax > 0))
                _vm.StatusMessage += $" ({skippedAlready + skippedMax} skipped)";
        }
        catch (Exception ex)
        {
            await ShowInfoAsync(ex.Message);
        }
    }

    private void BulkClear_Click(object sender, RoutedEventArgs e)
    {
        ClearEntrySelection();
        BulkActionBar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Visible;
    }

    private void TryOpenEntry(Guid entryId)
    {
        var vm = _vm.Entries.FirstOrDefault(e => e.Id == entryId);
        if (vm is null && _vm.FindEntry(entryId) is { } entry)
            vm = new VaultEntryViewModel(entry);
        if (vm is null)
            return;

        SelectSingleEntry(vm);

        if (UseMasterDetail())
            ShowDetail(vm);
        else
            NavigateToEntry(vm);
    }

    private void ClearImportFilter_Click(object sender, RoutedEventArgs e)
    {
        _importBatchFilter = null;
        _vm.VaultImportBatchFilter = null;
        RefreshList();
    }

    private void ApplyReadOnlyChrome()
    {
        var readOnly = _vm.IsReadOnly;
        GeneratePasswordBtn.IsEnabled = !readOnly;
        AddEntryBtn.IsEnabled = !readOnly;
        NewCategoryBtn.IsEnabled = !readOnly;
    }

    private void NavigateToEntry(VaultEntryViewModel vm)
    {
        ClearEntrySelection();
        NavigationService.Current.ResetCurrent();
        NavigationService.Current.Navigate<EntryPage>(vm.Entry, animate: true);
    }

    private void EnableEditing_Click(object sender, RoutedEventArgs e)
    {
        _vm.PendingRollbackConfirm = true;
        _vm.Lock();
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultEntryViewModel vm } fe)
            CopyEntryField(vm.Entry.Password, isPassword: true, pulseTarget: fe);
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultEntryViewModel vm } fe)
        {
            if (string.IsNullOrWhiteSpace(vm.Username))
                return;
            CopyEntryField(vm.Username, isPassword: false, pulseTarget: fe);
        }
    }

    private void CopyOtp_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanUseTotp)
            return;
        if (sender is not FrameworkElement { Tag: VaultEntryViewModel vm })
            return;
        if (string.IsNullOrWhiteSpace(vm.Entry.TotpSecret))
            return;

        try
        {
            var code = TotpGenerator.Generate(vm.Entry.TotpSecret);
            CopyEntryField(code, isPassword: false, status: "Authenticator code copied.");
        }
        catch (Exception ex)
        {
            _ = ShowInfoAsync(ex.Message);
        }
    }

    private void CopyEntryField(string text, bool isPassword, string? status = null, FrameworkElement? pulseTarget = null)
    {
        try
        {
            if (isPassword)
                _clipboard.CopyPassword(text);
            else
                _clipboard.CopyText(text);
            _vm.ResetAutoLock();
            _vm.StatusMessage = status ?? "Password copied — clipboard will clear automatically.";
            if (pulseTarget is not null)
                FortivaSurfaceEffects.PulseSuccess(pulseTarget);
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowInfoAsync(ex.Message);
        }
    }

    private async void GeneratePassword_Click(object sender, RoutedEventArgs e)
        => await GeneratePasswordAsync();

    private async Task GeneratePasswordAsync()
    {
        if (!_vm.IsUnlocked) return;
        var generated = await PasswordGeneratorDialog.ShowAsync(
            Content.XamlRoot, _vm, preselectedTags: GetPreselectedTagsForNewEntry(), themeHost: this);
        if (generated is null) return;

        var create = new ContentDialog
        {
            Title = "Password generated",
            Content = "Create a new vault entry with this password, or copy it to the clipboard?",
            PrimaryButtonText = "Create entry",
            SecondaryButtonText = "Copy only",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        FortivaDialogs.Configure(create, Content.XamlRoot, themeHost: this);
        var choice = await create.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            NavigationService.Current.Navigate<EntryPage>(
                new EntryDraft { Password = generated.Password, Tags = generated.Tags }, animate: true);
            return;
        }

        if (choice == ContentDialogResult.Secondary)
            CopyEntryField(generated.Password, isPassword: true);
    }

    private async void AddEntry_Click(object sender, RoutedEventArgs e)
        => await QuickAddAsync();

    private async Task QuickAddAsync()
    {
        if (_vm.IsReadOnly) { await ShowInfoAsync("Vault is read-only."); return; }
        var outcome = await QuickAddEntryDialog.ShowAsync(Content.XamlRoot, _vm, GetPreselectedTagsForNewEntry(), themeHost: this);
        if (outcome.Result == QuickAddEntryDialog.QuickAddResult.Saved)
        {
            RefreshList();
            _vm.StatusMessage = "Entry saved.";
        }
        else if (outcome.Result == QuickAddEntryDialog.QuickAddResult.OpenFullForm && outcome.Draft is not null)
            NavigationService.Current.Navigate<EntryPage>(outcome.Draft, animate: true);
    }

    private void AddEntryFull_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReadOnly) { _ = ShowInfoAsync("Vault is read-only."); return; }
        NavigationService.Current.Navigate<EntryPage>(
            new EntryDraft { Tags = GetPreselectedTagsForNewEntry() }, animate: true);
    }

    private static string FormatImportFilterSubtitle(ImportBatch batch)
    {
        var line = $"From import: {batch.ProvenanceLabel} · {batch.ImportedAt.LocalDateTime:g}";
        if (!string.IsNullOrWhiteSpace(batch.SourceHint))
            line += $" · {batch.SourceHint.Trim()}";
        return line;
    }

    private async Task ShowInfoAsync(string message)
    {
        var dlg = new ContentDialog
        {
            Title          = "Fortiva",
            Content        = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = "OK",
            DefaultButton  = ContentDialogButton.Close,
            XamlRoot       = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot, themeHost: this);
        await dlg.ShowAsync();
    }
}
