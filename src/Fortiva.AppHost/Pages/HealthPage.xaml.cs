using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Vault;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class HealthPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private int _buildGeneration;
    private Dictionary<Guid, VaultEntry> _entryLookup = [];

    public HealthPage() { InitializeComponent(); }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await BuildReportAsync();
    }

    private async void Refresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await BuildReportAsync();

    private async Task BuildReportAsync()
    {
        var generation = ++_buildGeneration;
        LoadingRing.IsActive = true;
        RefreshBtn.IsEnabled = false;
        try
        {
            var entries = _vm.Entries.ToList();
            _entryLookup = entries.ToDictionary(e => e.Id, e => e.Entry);
            var report = await Task.Run(_vm.GetHealthReport);

            var applied = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _buildGeneration) { applied.TrySetResult(true); return; }
                try
                {
                    ApplyReport(report, entries);
                }
                catch (Exception ex)
                {
                    ScoreHeading.Text = "Could not build health report.";
                    ScoreDetail.Text = ex.Message;
                }
                applied.TrySetResult(true);
            });
            await applied.Task;
        }
        catch (Exception ex)
        {
            ScoreHeading.Text = "Could not build health report.";
            ScoreDetail.Text = ex.Message;
        }
        finally
        {
            LoadingRing.IsActive = false;
            RefreshBtn.IsEnabled = true;
        }
    }

    private void HealthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView { SelectedItem: HealthListItem item }) return;
        if (!_entryLookup.TryGetValue(item.Id, out var entry)) return;
        NavigationService.Current.ResetCurrent();
        NavigationService.Current.Navigate<EntryPage>(entry, animate: true);
        if (sender is ListView lv) lv.SelectedItem = null;
    }

    private void ApplyReport(PasswordHealthReport report, IList<VaultEntryViewModel> entries)
    {
        TotalCount.Text = report.TotalEntries.ToString();
        WeakCount.Text = report.WeakCount.ToString();
        ReusedCount.Text = report.ReusedCount.ToString();
        OldCount.Text = report.OldCount.ToString();

        var deductions = report.WeakCount * 10 + report.ReusedCount * 7 + report.OldCount * 4;
        var score = Math.Max(0, Math.Min(100, 100 - deductions));
        ScoreBar.Value = score;

        var (labelText, bg, detail) = score switch
        {
            >= 90 => ("Excellent — your passwords look great!", Color.FromArgb(30, 0, 180, 80), "Keep it up! Continue using unique, strong passwords."),
            >= 70 => ("Good — a few improvements recommended.", Color.FromArgb(30, 200, 130, 0), $"{report.WeakCount} weak or {report.ReusedCount} reused passwords need attention."),
            >= 40 => ("Fair — significant issues found.", Color.FromArgb(30, 220, 100, 0), "Fix weak and reused passwords to improve security."),
            _ => ("Poor — immediate action needed.", Color.FromArgb(30, 220, 50, 50), "Multiple critical issues: weak, reused, and old passwords detected.")
        };
        ScoreHeading.Text = $"Security score: {score}/100 — {labelText}";
        ScoreDetail.Text = detail;
        ScoreBanner.Background = new SolidColorBrush(bg);

        PopulateList(WeakList, report.WeakEntryIds, entries);
        PopulateList(ReusedList, report.ReusedEntryIds, entries);
        PopulateList(OldList, report.OldEntryIds, entries);

        WeakExpander.Header = $"Weak passwords ({report.WeakCount})";
        ReusedExpander.Header = $"Reused passwords ({report.ReusedCount})";
        OldExpander.Header = $"Old passwords ({report.OldCount})";
    }

    private static void PopulateList(
        ListView list,
        IReadOnlyList<Guid> ids,
        IList<VaultEntryViewModel> source)
    {
        var lookup = source.ToDictionary(e => e.Id);
        var items = ids
            .Where(id => lookup.ContainsKey(id))
            .Select(id =>
            {
                var e = lookup[id];
                return new HealthListItem(e.Id, $"{e.Title}  —  {e.Username}");
            })
            .ToList<object>();
        list.ItemsSource = items;
    }

    private sealed record HealthListItem(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }
}
