using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Audit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Pages;

public sealed partial class AuditPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;

    public AuditPage() => InitializeComponent();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _vm.ThemeChanged += OnThemeChanged;
        Refresh();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ThemeService.ApplyToElement(this);
            Refresh();
        });
    }

    private void Refresh()
    {
        var events = _vm.GetAuditLogger().ReadRecent(500);
        AuditList.Items.Clear();

        foreach (var ev in events)
            AuditList.Items.Add(BuildRow(ev));

        CountLabel.Text = $"{events.Count} events";
    }

    private Border BuildRow(AuditEvent ev)
    {
        var theme = FortivaControlTheme.ResolveAppTheme();
        var (timestampBrush, messageBrush, rowBg, rowBorder) =
            FortivaControlTheme.GetAuditLogRowBrushes(this, theme);
        var (badgeBg, badgeFg) = FortivaControlTheme.GetAuditEventBadgeBrushes(ev.EventType, this, theme);

        var row = new Grid { RequestedTheme = theme };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = ev.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = timestampBrush,
            VerticalAlignment = VerticalAlignment.Center,
            RequestedTheme = theme
        };
        Grid.SetColumn(time, 0);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = badgeBg,
            RequestedTheme = theme,
            Child = new TextBlock
            {
                Text = ev.EventType.ToString(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = badgeFg,
                RequestedTheme = theme
            }
        };
        Grid.SetColumn(badge, 1);

        var msg = new TextBlock
        {
            Text = ev.Message,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = messageBrush,
            RequestedTheme = theme
        };
        Grid.SetColumn(msg, 2);

        row.Children.Add(time);
        row.Children.Add(badge);
        row.Children.Add(msg);

        var card = new Border
        {
            Background = rowBg,
            BorderBrush = rowBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            RequestedTheme = theme,
            Child = row
        };
        FortivaThemeResources.MergeOnto(card, theme);
        return card;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.SuggestedFileName = $"fortiva-audit-{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("JSONL", [".jsonl"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        _vm.GetAuditLogger().ExportTo(file.Path);
    }
}
