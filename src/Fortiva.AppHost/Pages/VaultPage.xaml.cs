using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Otp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Pages;

public sealed partial class VaultPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly ClipboardService _clipboard;

    private Action? _stateChangedHandler;

    public VaultPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        // Subscribe on each navigation; unsubscribe in OnNavigatedFrom to prevent leaks
        _stateChangedHandler = () => DispatcherQueue.TryEnqueue(() => RefreshList());
        _vm.StateChanged += _stateChangedHandler;
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
    }

    private void RefreshList()
    {
        var q = SearchBox.Text?.Trim();
        var source = string.IsNullOrEmpty(q) ? _vm.Entries : _vm.Search(q);
        var list = source.ToList();
        EntryList.ItemsSource = list;
        CountText.Text = $"{list.Count} {(list.Count == 1 ? "entry" : "entries")}";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        => RefreshList();

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is VaultEntryViewModel vm)
        {
            NavigationService.Current.ResetCurrent();
            NavigationService.Current.Navigate<EntryPage>(vm.Entry, animate: true);
            EntryList.SelectedItem = null;
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
        {
            try
            {
                _clipboard.CopyPassword(vm.Entry.Password);
                _vm.ResetAutoLock();
            }
            catch (InvalidOperationException ex)
            {
                _ = ShowInfoAsync(ex.Message);
            }
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
            _clipboard.CopyText(code);
            _vm.ResetAutoLock();
        }
        catch (Exception ex)
        {
            _ = ShowInfoAsync(ex.Message);
        }
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsUnlocked) return;
        _vm.RequestNavigationTab("Generator");
        NavigationService.Current.Navigate<PasswordGeneratorPage>();
    }

    private void AddEntry_Click(object sender, RoutedEventArgs e)
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
        await dlg.ShowAsync();
    }
}
