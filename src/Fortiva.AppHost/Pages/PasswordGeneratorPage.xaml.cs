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
        _vm.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged() => _panel?.ApplyThemeResources();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);

        if (_panel is null)
        {
            _panel = new PasswordGeneratorPanel(_vm);
            GeneratorHost.Children.Add(_panel.Root);
        }
        else
        {
            _panel.Regenerate();
        }

        StatusBar.IsOpen = false;
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        _panel?.Regenerate();
        StatusBar.IsOpen = false;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_panel is null || string.IsNullOrEmpty(_panel.CurrentPassword))
            return;

        try
        {
            _clipboard.CopyPassword(_panel.CurrentPassword);
            _vm.ResetAutoLock();
            StatusBar.Message = "Password copied. Clipboard will clear automatically.";
            StatusBar.IsOpen = true;
        }
        catch (InvalidOperationException ex)
        {
            StatusBar.Message = ex.Message;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.IsOpen = true;
        }
    }
}
