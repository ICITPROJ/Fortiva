using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Otp;
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

    public VaultPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        _stateChangedHandler = () => DispatcherQueue.TryEnqueue(() => RefreshList());
        _vm.StateChanged += _stateChangedHandler;
        _vaultLocationHandler = () => DispatcherQueue.TryEnqueue(() => RefreshList());
        _vm.VaultLocationChanged += _vaultLocationHandler;
        ReadOnlyBar.IsOpen = _vm.IsReadOnly;
        if (_vm.IsReadOnly && !string.IsNullOrEmpty(_vm.Session?.RollbackWarning))
            ReadOnlyBar.Message = _vm.Session.RollbackWarning +
                " You can view entries but not edit. Use Enable editing below to confirm and unlock write access.";
        RefreshList();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
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
        var all = string.IsNullOrEmpty(q) ? _vm.Entries : _vm.Search(q);
        var list = all
            .OrderByDescending(e => e.IsFavorite)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EntryGrid.ItemsSource = list;

        var total = _vm.Entries.Count;
        var favorites = _vm.Entries.Count(e => e.IsFavorite);
        var totp = _vm.Entries.Count(e => e.HasTotp);

        VaultSubtitle.Text = total == 0
            ? "Encrypted on this device · nothing leaves your PC"
            : $"{total} {(total == 1 ? "entry" : "entries")} · favorites first · tap a card to open";

        StatFavorites.Text = $"{favorites} fav{(favorites == 1 ? "" : "s")}";
        StatTotp.Text = $"{totp} · 2FA";
        StatVaultTrust.Text = _vm.VaultTrustChipText;

        var showing = list.Count;
        CountText.Text = string.IsNullOrEmpty(q)
            ? (total == 0 ? "No entries saved yet" : $"Showing all {showing} entries")
            : $"Showing {showing} of {total} entries matching “{q}”";

        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EntryGrid.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        => RefreshList();

    private void EntryGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VaultEntryViewModel vm)
        {
            NavigationService.Current.ResetCurrent();
            NavigationService.Current.Navigate<EntryPage>(vm.Entry, animate: true);
        }
    }

    private void EnableEditing_Click(object sender, RoutedEventArgs e)
    {
        _vm.PendingRollbackConfirm = true;
        _vm.Lock();
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultEntryViewModel vm })
            CopyEntryField(vm.Entry.Password, isPassword: true);
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultEntryViewModel vm })
        {
            if (string.IsNullOrWhiteSpace(vm.Username))
                return;
            CopyEntryField(vm.Username, isPassword: false);
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

    private void CopyEntryField(string text, bool isPassword, string? status = null)
    {
        try
        {
            if (isPassword)
                _clipboard.CopyPassword(text);
            else
                _clipboard.CopyText(text);
            _vm.ResetAutoLock();
            _vm.StatusMessage = status ?? "Password copied — clipboard will clear automatically.";
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
        var password = await PasswordGeneratorDialog.ShowAsync(Content.XamlRoot, _vm);
        if (password is null) return;

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
        FortivaDialogs.Configure(create, Content.XamlRoot);
        var choice = await create.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            NavigationService.Current.Navigate<EntryPage>(
                new EntryDraft { Password = password }, animate: true);
            return;
        }

        if (choice == ContentDialogResult.Secondary)
            CopyEntryField(password, isPassword: true);
    }

    private async void AddEntry_Click(object sender, RoutedEventArgs e)
        => await QuickAddAsync();

    private async Task QuickAddAsync()
    {
        if (_vm.IsReadOnly) { await ShowInfoAsync("Vault is read-only."); return; }
        var outcome = await QuickAddEntryDialog.ShowAsync(Content.XamlRoot, _vm);
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
        NavigationService.Current.Navigate<EntryPage>(null, animate: true);
    }

    private async Task ShowInfoAsync(string message)
    {
        var dlg = new ContentDialog
        {
            Title          = "Fortiva",
            Content        = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = "OK",
            XamlRoot       = Content.XamlRoot
        };
        FortivaDialogs.Configure(dlg, Content.XamlRoot);
        await dlg.ShowAsync();
    }
}
