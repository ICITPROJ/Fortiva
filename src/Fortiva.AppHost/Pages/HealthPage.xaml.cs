using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Fortiva.Core.Platform;
using Fortiva.Core.Security;
using Fortiva.Core.Updates;
using Fortiva.Core.Vault;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Fortiva.AppHost.Pages;

public sealed partial class HealthPage : Page
{
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly HelloUnlockManager _hello;
    private int _buildGeneration;
    private Dictionary<Guid, VaultEntry> _entryLookup = [];
    private SecurityAuditReport? _lastAuditReport;

    public HealthPage()
    {
        InitializeComponent();
        _hello = new HelloUnlockManager(
            FortivaPaths.GetHelloDataDirectory(_vm.IsEnterprise),
            _vm.IsEnterprise);
        CatActivity.Visibility = _vm.IsEnterprise ? Visibility.Visible : Visibility.Collapsed;
        if (!_vm.IsEnterprise)
            CategoryGrid.ColumnDefinitions[3].Width = new GridLength(0);
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await BuildReportAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await BuildReportAsync();

    private void OpenGenerator_Click(object sender, RoutedEventArgs e)
        => NavigationService.Current.Navigate<PasswordGeneratorPage>();

    private void WeakCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => WeakExpander.IsExpanded = true;

    private void ReusedCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ReusedExpander.IsExpanded = true;

    private void OldCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => OldExpander.IsExpanded = true;

    private void MissingCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => MissingExpander.IsExpanded = true;

    private void TotalCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        WeakExpander.IsExpanded = false;
        ReusedExpander.IsExpanded = false;
        OldExpander.IsExpanded = false;
        MissingExpander.IsExpanded = false;
    }

    private async Task BuildReportAsync()
    {
        var generation = ++_buildGeneration;
        LoadingRing.IsActive = true;
        RunAuditBtn.IsEnabled = false;
        GeneratorBtn.IsEnabled = false;
        ExportAuditBtn.IsEnabled = false;
        try
        {
            var entries = _vm.Entries.ToList();
            _entryLookup = entries.ToDictionary(e => e.Id, e => e.Entry);
            var helloConfigured = _hello.IsConfigured;
            var report = await Task.Run(() => _vm.GetSecurityAuditReport(helloConfigured));

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
                    ScoreHeading.Text = "Could not complete security audit.";
                    ScoreDetail.Text = ex.Message;
                }
                applied.TrySetResult(true);
            });
            await applied.Task;
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScoreHeading.Text = "Could not complete security audit.";
                ScoreDetail.Text = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            });
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingRing.IsActive = false;
                RunAuditBtn.IsEnabled = true;
                GeneratorBtn.IsEnabled = true;
                ExportAuditBtn.IsEnabled = _lastAuditReport is not null;
            });
        }
    }

    private async void ExportAudit_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAuditReport is null)
        {
            await BuildReportAsync();
            if (_lastAuditReport is null) return;
        }

        var dlg = new ContentDialog
        {
            Title = "Export security audit",
            Content = "Choose a format. HTML can be opened in a browser and printed to PDF.",
            PrimaryButtonText = "JSON",
            SecondaryButtonText = "HTML",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        FortivaDialogs.Configure(dlg, XamlRoot);
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.None) return;

        var stamp = _lastAuditReport.RunAt.LocalDateTime.ToString("yyyyMMdd-HHmmss");
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        if (result == ContentDialogResult.Primary)
        {
            picker.SuggestedFileName = $"fortiva-security-audit-{stamp}";
            picker.FileTypeChoices.Add("JSON", [".json"]);
        }
        else
        {
            picker.SuggestedFileName = $"fortiva-security-audit-{stamp}";
            picker.FileTypeChoices.Add("HTML report", [".html"]);
        }

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var options = BuildExportOptions();
        var content = result == ContentDialogResult.Primary
            ? SecurityAuditExporter.ToJson(_lastAuditReport, options)
            : SecurityAuditExporter.ToHtml(_lastAuditReport, options);

        await FileIO.WriteTextAsync(file, content);
    }

    private SecurityAuditExportOptions BuildExportOptions()
    {
        return new SecurityAuditExportOptions
        {
            Edition = _vm.Edition,
            VaultLocation = _vm.VaultLocationLabel,
            AppVersion = AppVersion.Current
        };
    }

    private void HealthList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView { SelectedItem: HealthEntryDisplay item }) return;
        if (!_entryLookup.TryGetValue(item.Id, out var entry)) return;
        NavigationService.Current.ResetCurrent();
        NavigationService.Current.Navigate<EntryPage>(entry, animate: true);
        if (sender is ListView lv) lv.SelectedItem = null;
    }

    private void ApplyReport(SecurityAuditReport audit, IList<VaultEntryViewModel> entries)
    {
        _lastAuditReport = audit;
        ExportAuditBtn.IsEnabled = true;
        var report = audit.PasswordHealth;
        var loginTotal = report.TotalEntries + report.MissingCount;

        LastRunLabel.Text = $"Last audit: {audit.RunAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}";

        CatPasswordsCount.Text = audit.PasswordFindings.ToString();
        CatSettingsCount.Text = audit.SettingsFindings.ToString();
        CatVaultCount.Text = audit.VaultFindings.ToString();
        CatActivityCount.Text = audit.ActivityFindings.ToString();

        TotalCount.Text = loginTotal.ToString();
        WeakCount.Text = report.WeakCount.ToString();
        ReusedCount.Text = report.ReusedCount.ToString();
        OldCount.Text = report.OldCount.ToString();
        MissingCount.Text = report.MissingCount.ToString();

        PassStat.Text = audit.PassCount.ToString();
        WarnStat.Text = (audit.WarningCount + audit.InfoCount).ToString();
        CritStat.Text = audit.CriticalCount.ToString();
        SecureStat.Text = report.SecureCount.ToString();

        var score = audit.OverallScore;
        ScoreValue.Text = score.ToString();
        ApplyScoreVisuals(score, audit);

        PopulateFindings(audit.Findings);
        PopulateStrengthBars(report, loginTotal);
        PopulatePasswordLists(report, entries);

        WeakExpander.Header = BuildExpanderHeader("Weak passwords", report.WeakCount, "\uEA3A", GetBrush("SystemFillColorCriticalBrush"));
        ReusedExpander.Header = BuildExpanderHeader("Reused passwords", report.ReusedCount, "\uE8AB", GetBrush("SystemFillColorCautionBrush"));
        OldExpander.Header = BuildExpanderHeader("Old passwords (1+ year)", report.OldCount, "\uE787", GetBrush("SystemFillColorCautionBrush"));
        MissingExpander.Header = BuildExpanderHeader("Missing passwords", report.MissingCount, "\uE946", GetBrush("SystemFillColorNeutralBrush"));

        HighlightCard(WeakCard, report.WeakCount > 0);
        HighlightCard(ReusedCard, report.ReusedCount > 0);
        HighlightCard(OldCard, report.OldCount > 0);
        HighlightCard(MissingCard, report.MissingCount > 0);
        HighlightCard(CatPasswords, audit.PasswordFindings > 0);
        HighlightCard(CatSettings, audit.SettingsFindings > 0);
        HighlightCard(CatVault, audit.VaultFindings > 0);
        if (_vm.IsEnterprise)
            HighlightCard(CatActivity, audit.ActivityFindings > 0);
    }

    private void ApplyScoreVisuals(int score, SecurityAuditReport audit)
    {
        var hasCritical = audit.CriticalCount > 0;
        var (heading, detail, grade, ringColor, bannerColor) = score switch
        {
            >= 90 when !hasCritical => (
                "Excellent - your vault passes the full audit",
                $"{audit.PassCount} checks passed. Keep unique passwords and export encrypted backups regularly.",
                "A",
                Color.FromArgb(255, 16, 160, 90),
                Color.FromArgb(36, 16, 160, 90)),
            >= 75 => (
                "Good - minor issues to address",
                $"{audit.WarningCount + audit.InfoCount} recommendation(s) below will raise your score further.",
                "B",
                Color.FromArgb(255, 40, 150, 70),
                Color.FromArgb(36, 40, 150, 70)),
            >= 55 => (
                "Fair - security gaps need attention",
                "Review critical and warning findings. Start with reused and weak passwords.",
                "C",
                Color.FromArgb(255, 210, 130, 0),
                Color.FromArgb(36, 210, 130, 0)),
            >= 35 => (
                "Needs work - multiple risks detected",
                $"{audit.CriticalCount} critical and {audit.WarningCount} warning finding(s) in this audit.",
                "D",
                Color.FromArgb(255, 220, 110, 20),
                Color.FromArgb(36, 220, 110, 20)),
            _ => (
                "Critical - immediate action required",
                "This audit found serious issues across passwords and/or settings.",
                "F",
                Color.FromArgb(255, 220, 60, 60),
                Color.FromArgb(36, 220, 60, 60))
        };

        ScoreHeading.Text = heading;
        ScoreDetail.Text = detail;
        ScoreGrade.Text = grade;
        ScoreRing.BorderBrush = new SolidColorBrush(ringColor);
        ScoreValue.Foreground = new SolidColorBrush(ringColor);
        ScoreGrade.Foreground = new SolidColorBrush(ringColor);
        ScoreRing.Background = new SolidColorBrush(bannerColor);
    }

    private void PopulateFindings(IReadOnlyList<SecurityAuditFinding> findings)
    {
        FindingsPanel.Children.Clear();
        var ordered = findings
            .OrderBy(f => f.Severity == AuditSeverity.Pass ? 1 : 0)
            .ThenBy(f => f.Priority)
            .ThenByDescending(f => f.Severity)
            .ToList();

        foreach (var f in ordered)
        {
            var (accent, label) = f.Severity switch
            {
                AuditSeverity.Critical => (Color.FromArgb(255, 220, 60, 60), "CRITICAL"),
                AuditSeverity.Warning => (Color.FromArgb(255, 220, 130, 0), "WARNING"),
                AuditSeverity.Info => (Color.FromArgb(255, 80, 130, 200), "INFO"),
                _ => (Color.FromArgb(255, 16, 160, 90), "PASS")
            };

            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.FromArgb((byte)(f.Severity == AuditSeverity.Pass ? 16 : 24), accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badges = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            badges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B)),
                Child = new TextBlock { Text = label, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent) }
            });
            badges.Children.Add(new TextBlock { Text = f.Category, FontSize = 10, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center });
            grid.Children.Add(badges);

            var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = f.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.WrapWholeWords });
            text.Children.Add(new TextBlock { Text = f.Detail, Opacity = 0.75, FontSize = 13, TextWrapping = TextWrapping.WrapWholeWords });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (f.ActionHint is not null && f.Severity >= AuditSeverity.Info && f.Severity != AuditSeverity.Pass)
            {
                var btn = new Button { Content = ActionLabel(f.ActionHint), VerticalAlignment = VerticalAlignment.Center };
                var hint = f.ActionHint;
                btn.Click += (_, _) => NavigateAction(hint);
                Grid.SetColumn(btn, 2);
                grid.Children.Add(btn);
            }

            card.Child = grid;
            FindingsPanel.Children.Add(card);
        }
    }

    private static string ActionLabel(string hint) => hint switch
    {
        "generator" => "Open generator",
        "settings" => "Open settings",
        "export" => "Export backup",
        "import" => "Import",
        "audit" => "View audit log",
        _ => "Review"
    };

    private void NavigateAction(string hint)
    {
        switch (hint)
        {
            case "generator": NavigationService.Current.Navigate<PasswordGeneratorPage>(); break;
            case "settings": NavigationService.Current.Navigate<SettingsPage>(); break;
            case "export":
            case "import": NavigationService.Current.Navigate<ImportExportPage>(); break;
            case "audit": NavigationService.Current.Navigate<AuditPage>(); break;
            default: _vm.RequestNavigationTab("Vault"); NavigationService.Current.Navigate<VaultPage>(); break;
        }
    }

    private void PopulateStrengthBars(PasswordHealthReport report, int loginTotal)
    {
        StrengthBarsPanel.Children.Clear();
        if (loginTotal == 0)
        {
            StrengthBarsPanel.Children.Add(new TextBlock
            {
                Text = "Add login entries to see a strength breakdown.",
                Opacity = 0.65,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            return;
        }

        AddStrengthBar("Very strong", report.VeryStrongCount, loginTotal, Color.FromArgb(255, 16, 160, 90));
        AddStrengthBar("Strong", report.StrongCount, loginTotal, Color.FromArgb(255, 40, 150, 70));
        AddStrengthBar("Fair", report.FairCount, loginTotal, Color.FromArgb(255, 210, 170, 0));
        AddStrengthBar("Weak", report.WeakStrengthCount, loginTotal, Color.FromArgb(255, 220, 110, 20));
        AddStrengthBar("Very weak", report.VeryWeakCount, loginTotal, Color.FromArgb(255, 220, 60, 60));
    }

    private void AddStrengthBar(string label, int count, int total, Color fill)
    {
        var pct = total == 0 ? 0 : count * 100.0 / total;
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        row.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });

        var track = new Grid
        {
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
            CornerRadius = new CornerRadius(5)
        };
        track.SizeChanged += (_, _) =>
        {
            if (track.ActualWidth <= 0 || track.Children.Count == 0) return;
            if (track.Children[0] is Border bar)
                bar.Width = Math.Max(0, pct / 100.0 * track.ActualWidth);
        };
        track.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = 10,
            Background = new SolidColorBrush(fill),
            CornerRadius = new CornerRadius(5)
        });
        Grid.SetColumn(track, 1);
        row.Children.Add(track);

        var countBlock = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countBlock, 2);
        row.Children.Add(countBlock);
        StrengthBarsPanel.Children.Add(row);
    }

    private void PopulatePasswordLists(PasswordHealthReport report, IList<VaultEntryViewModel> entries)
    {
        PopulateList(WeakList, report.WeakEntryIds, entries, "Weak");
        PopulateList(ReusedList, report.ReusedEntryIds, entries, "Reused");
        PopulateList(OldList, report.OldEntryIds, entries, "1y+ old");
        PopulateList(MissingList, report.MissingEntryIds, entries, "No password");
    }

    private static void PopulateList(
        ListView list,
        IReadOnlyList<Guid> ids,
        IList<VaultEntryViewModel> source,
        string issueLabel)
    {
        var lookup = source.ToDictionary(e => e.Id);
        var (badgeBg, badgeFg) = issueLabel switch
        {
            "Weak" => (Color.FromArgb(48, 220, 60, 60), Color.FromArgb(255, 180, 40, 40)),
            "Reused" => (Color.FromArgb(48, 220, 130, 0), Color.FromArgb(255, 180, 100, 0)),
            "1y+ old" => (Color.FromArgb(48, 210, 130, 0), Color.FromArgb(255, 170, 100, 0)),
            _ => (Color.FromArgb(48, 120, 120, 120), Color.FromArgb(255, 90, 90, 90))
        };

        list.ItemsSource = ids
            .Where(id => lookup.ContainsKey(id))
            .Select(id =>
            {
                var e = lookup[id];
                return new HealthEntryDisplay(
                    e.Id, e.Title,
                    string.IsNullOrWhiteSpace(e.Username) ? e.DomainDisplay : e.Username,
                    e.Initial, issueLabel,
                    new SolidColorBrush(badgeBg),
                    new SolidColorBrush(badgeFg));
            })
            .ToList();
    }

    private static object BuildExpanderHeader(string title, int count, string glyph, Brush accent)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = accent });
        panel.Children.Add(new TextBlock
        {
            Text = $"{title} ({count})",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void HighlightCard(Border card, bool active)
    {
        card.Opacity = active ? 1.0 : 0.72;
        card.BorderThickness = active ? new Thickness(2) : new Thickness(0);
        card.BorderBrush = active ? GetBrush("AccentFillColorDefaultBrush") : null;
    }

    private static Brush GetBrush(string key)
        => (Application.Current.Resources[key] as Brush) ?? new SolidColorBrush(Colors.Gray);
}

public sealed class HealthEntryDisplay
{
    public HealthEntryDisplay(
        Guid id, string title, string subtitle, string initial, string issueLabel,
        Brush issueBadgeBrush, Brush issueTextBrush)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Initial = initial;
        IssueLabel = issueLabel;
        IssueBadgeBrush = issueBadgeBrush;
        IssueTextBrush = issueTextBrush;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Initial { get; }
    public string IssueLabel { get; }
    public Brush IssueBadgeBrush { get; }
    public Brush IssueTextBrush { get; }
}
