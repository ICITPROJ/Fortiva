using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Pages;

public sealed partial class PasswordGeneratorPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly ClipboardService _clipboard;
    private PasswordGeneratorPanel? _panel;

    public PasswordGeneratorPage()
    {
        InitializeComponent();
        _clipboard = new ClipboardService(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds, _vm.LogPolicyViolation);
    }

    private void OnThemeChanged() => _panel?.ApplyThemeResources();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.ThemeChanged += OnThemeChanged;
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);

        if (_panel is null)
        {
            _panel = new PasswordGeneratorPanel(_vm, hostMode: PasswordGeneratorHostMode.Page);
            GeneratorHost.Children.Add(_panel.Root);
        }
        else
        {
            _panel.Regenerate();
        }

        StatusBar.IsOpen = false;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        _panel?.Regenerate();
        StatusBar.IsOpen = false;
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_panel is null || string.IsNullOrEmpty(_panel.CurrentPassword))
            return;

        _clipboard.CopyPassword(_panel.CurrentPassword);
        StatusBar.Message = "Password copied to clipboard.";
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;
    }

    /// <summary>Same as dialog primary — create entry with generated password.</summary>
    private void UsePassword_Click(object sender, RoutedEventArgs e)
    {
        if (_panel is null || string.IsNullOrEmpty(_panel.CurrentPassword))
            return;

        if (_vm.IsReadOnly)
        {
            StatusBar.Message = "Vault is read-only.";
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.IsOpen = true;
            return;
        }

        NavigationService.Current.Navigate<EntryPage>(
            new EntryDraft { Password = _panel.CurrentPassword }, animate: true);
    }
}
