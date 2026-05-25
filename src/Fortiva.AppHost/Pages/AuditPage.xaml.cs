using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Audit;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class AuditPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;

    public AuditPage() => InitializeComponent();

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Refresh();
    }

    private void Refresh()
    {
        var events = _vm.GetAuditLogger().ReadRecent(500);
        AuditList.Items.Clear();

        foreach (var ev in events)
            AuditList.Items.Add(BuildRow(ev));

        CountLabel.Text = $"{events.Count} events";
    }

    private static Grid BuildRow(AuditEvent ev)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = ev.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = FortivaThemeResources.Body,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(time, 0);

        var typeText = ev.EventType.ToString();
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ev.EventType switch
            {
                AuditEventType.UnlockFailure or AuditEventType.PolicyViolation =>
                    FortivaThemeResources.StatusError,
                AuditEventType.UnlockSuccess =>
                    FortivaThemeResources.StatusSuccess,
                _ => FortivaThemeResources.GetBrush("AccentFillColorTertiaryBrush")
            },
            Child = new TextBlock
            {
                Text = typeText,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
        Grid.SetColumn(badge, 1);

        var msg = new TextBlock
        {
            Text = ev.Message,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(msg, 2);

        row.Children.Add(time);
        row.Children.Add(badge);
        row.Children.Add(msg);
        row.Margin = new Thickness(0, 4, 0, 4);
        return row;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.SuggestedFileName = $"fortiva-audit-{DateTime.Now:yyyyMMdd}";
        picker.FileTypeChoices.Add("JSONL", [".jsonl"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        _vm.GetAuditLogger().ExportTo(file.Path);
    }
}
