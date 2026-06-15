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
    private HelloUnlockManager Hello => new(_vm.HelloDataDirectory, _vm.IsEnterprise);
    private int _buildGeneration;
    private Dictionary<Guid, VaultEntry> _entryLookup = [];
    private SecurityAuditReport? _lastAuditReport;
    private IReadOnlyList<SecurityAuditFinding> _allFindings = [];
    private string? _categoryFilter;
    private AuditSeverity? _severityFilter;

    public HealthPage()
    {
        InitializeComponent();
        CatActivity.Visibility = _vm.IsEnterprise ? Visibility.Visible : Visibility.Collapsed;
        if (!_vm.IsEnterprise)
            CategoryGrid.ColumnDefinitions[3].Width = new GridLength(0);
        SetCategoryCardCursors();
    }

    private void SetCategoryCardCursors()
    {
        var hand = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var arrow = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        foreach (var card in new[] { CatPasswords, CatSettings, CatVault, CatActivity })
        {
            card.PointerEntered += (_, e) =>
            {
                if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch)
                    ProtectedCursor = hand;
            };
            card.PointerExited += (_, _) => ProtectedCursor = arrow;
        }
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ThemeService.ApplyToElement(this);
        _vm.ThemeChanged += OnThemeChanged;

        string? focusIssue = e.Parameter is HealthPageNavigationContext ctx ? ctx.FocusIssue : null;
        await BuildReportAsync();
        if (focusIssue is not null)
            FocusPasswordIssue(focusIssue);
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _vm.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            ThemeService.ApplyToElement(this);
            if (_lastAuditReport is not null)
                await BuildReportAsync();
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await BuildReportAsync();

    private void OpenGenerator_Click(object sender, RoutedEventArgs e)
        => NavigationService.Current.Navigate<PasswordGeneratorPage>();

    private void WeakCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FocusPasswordIssue("weak");

    private void ReusedCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FocusPasswordIssue("reused");

    private void OldCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FocusPasswordIssue("old");

    private void MissingCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FocusPasswordIssue("missing");

    private void TotalCard_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        WeakExpander.IsExpanded = false;
        ReusedExpander.IsExpanded = false;
        OldExpander.IsExpanded = false;
        MissingExpander.IsExpanded = false;
    }

    private void CatPasswords_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetCategoryFilter("Passwords");

    private void CatSettings_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetCategoryFilter("Settings");

    private void CatVault_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetCategoryFilter("Vault");
        if (ImportDuplicatesExpander.Visibility == Visibility.Visible)
            ImportDuplicatesExpander.IsExpanded = true;
    }

    private void CatActivity_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetCategoryFilter("Activity");

    private void PassStat_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetSeverityFilter(AuditSeverity.Pass);

    private void WarnStat_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetSeverityFilter(AuditSeverity.Warning);

    private void CritStat_Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetSeverityFilter(AuditSeverity.Critical);

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
        => ClearFilters();

    private void SetCategoryFilter(string category)
    {
        _categoryFilter = category;
        _severityFilter = null;
        ApplyFindingFilter();
        UpdateFilterHighlights();
    }

    private void SetSeverityFilter(AuditSeverity severity)
    {
        _severityFilter = severity;
        _categoryFilter = null;
        ApplyFindingFilter();
        UpdateFilterHighlights();
    }

    private void ClearFilters()
    {
        _categoryFilter = null;
        _severityFilter = null;
        ApplyFindingFilter();
        UpdateFilterHighlights();
    }

    private void ApplyFindingFilter()
    {
        IEnumerable<SecurityAuditFinding> filtered = _allFindings;
        if (_categoryFilter is not null)
            filtered = filtered.Where(f => f.Category == _categoryFilter);
        if (_severityFilter is AuditSeverity.Warning)
            filtered = filtered.Where(f => f.Severity is AuditSeverity.Warning or AuditSeverity.Info);
        else if (_severityFilter is not null)
            filtered = filtered.Where(f => f.Severity == _severityFilter);

        RenderFindings(filtered.ToList());
        UpdateFilterBanner();
    }

    private void UpdateFilterBanner()
    {
        if (_categoryFilter is null && _severityFilter is null)
        {
            FilterBanner.Visibility = Visibility.Collapsed;
            return;
        }

        FilterBanner.Visibility = Visibility.Visible;
        FilterBannerText.Text = _categoryFilter is not null
            ? $"Showing {_categoryFilter.ToLowerInvariant()} findings"
            : _severityFilter switch
            {
                AuditSeverity.Pass => "Showing passed checks",
                AuditSeverity.Warning => "Showing warnings and recommendations",
                AuditSeverity.Critical => "Showing critical findings",
                _ => "Showing filtered findings"
            };
    }

    private void UpdateFilterHighlights()
    {
        var audit = _lastAuditReport;
        HighlightSelectedCard(CatPasswords, _categoryFilter == "Passwords", audit?.PasswordFindings > 0);
        HighlightSelectedCard(CatSettings, _categoryFilter == "Settings", audit?.SettingsFindings > 0);
        HighlightSelectedCard(CatVault, _categoryFilter == "Vault", audit?.VaultFindings > 0);
        if (_vm.IsEnterprise)
            HighlightSelectedCard(CatActivity, _categoryFilter == "Activity", audit?.ActivityFindings > 0);
    }

    private void ImportDuplicatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView { SelectedItem: ImportDuplicateDisplay item }) return;
        if (!item.ExistingEntryId.HasValue || !_entryLookup.TryGetValue(item.ExistingEntryId.Value, out var entry)) return;
        NavigationService.Current.ResetCurrent();
        NavigationService.Current.Navigate<EntryPage>(entry, animate: true);
        if (sender is ListView lv) lv.SelectedItem = null;
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
            if (!_vm.IsUnlocked)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != _buildGeneration) return;
                    ScoreHeading.Text = "Unlock your vault first";
                    ScoreDetail.Text = "Enter your master password on the unlock screen, then return here and run the audit again.";
                    LastRunLabel.Text = "Last audit: (vault locked)";
                    FindingsPanel.Children.Clear();
                });
                return;
            }

            var entries = _vm.Entries.ToList();
            _entryLookup = entries.ToDictionary(e => e.Id, e => e.Entry);
            var helloConfigured = Hello.IsConfigured;
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
        PopulateImportDuplicates(_vm.ImportHistory());
        PopulateStrengthBars(report, loginTotal);
        PopulatePasswordLists(report, entries);

        WeakExpander.Header = BuildExpanderHeader("Weak passwords", report.WeakCount, "\uEA3A", FortivaThemeResources.StatusError);
        ReusedExpander.Header = BuildExpanderHeader("Reused passwords", report.ReusedCount, "\uE8AB", FortivaThemeResources.StatusWarning);
        OldExpander.Header = BuildExpanderHeader("Old passwords (1+ year)", report.OldCount, "\uE787", FortivaThemeResources.StatusWarning);
        MissingExpander.Header = BuildExpanderHeader("Missing passwords", report.MissingCount, "\uE946", FortivaThemeResources.Body);

        HighlightCard(WeakCard, report.WeakCount > 0);
        HighlightCard(ReusedCard, report.ReusedCount > 0);
        HighlightCard(OldCard, report.OldCount > 0);
        HighlightCard(MissingCard, report.MissingCount > 0);
        HighlightCard(CatPasswords, audit.PasswordFindings > 0);
        HighlightCard(CatSettings, audit.SettingsFindings > 0);
        HighlightCard(CatVault, audit.VaultFindings > 0);
        if (_vm.IsEnterprise)
            HighlightCard(CatActivity, audit.ActivityFindings > 0);
        UpdateFilterHighlights();
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
        _allFindings = findings;
        ApplyFindingFilter();
    }

    private void RenderFindings(IReadOnlyList<SecurityAuditFinding> findings)
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
            badges.Children.Add(new TextBlock
            {
                Text = f.Category,
                FontSize = 10,
                Foreground = FortivaControlTheme.GetBrush("FortivaMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            grid.Children.Add(badges);

            var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = f.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = FortivaControlTheme.GetBrush("FortivaHeadingBrush")
            });
            text.Children.Add(new TextBlock
            {
                Text = f.Detail,
                FontSize = 13,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = FortivaControlTheme.GetBrush("FortivaBodyBrush")
            });
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
        "import-duplicates" => "View duplicates",
        "audit" => "View audit log",
        "health-weak" or "health-reused" or "health-old" or "health-missing" => "View entries",
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
            case "import-duplicates":
                NavigationService.Current.Navigate<ImportExportPage>(ImportExportNavigationContext.ShowDuplicates);
                break;
            case "audit": NavigationService.Current.Navigate<AuditPage>(); break;
            case "health-weak": NavigateToPasswordIssue("weak"); break;
            case "health-reused": NavigateToPasswordIssue("reused"); break;
            case "health-old": NavigateToPasswordIssue("old"); break;
            case "health-missing": NavigateToPasswordIssue("missing"); break;
            default: _vm.RequestNavigationTab("Vault"); NavigationService.Current.Navigate<VaultPage>(); break;
        }
    }

    private void NavigateToPasswordIssue(string issue)
    {
        _vm.RequestNavigationTab("Health");
        if (NavigationService.Current.CurrentPageType == typeof(HealthPage))
            FocusPasswordIssue(issue);
        else
            NavigationService.Current.Navigate<HealthPage>(HealthPageNavigationContext.ForIssue(issue));
    }

    private void FocusPasswordIssue(string issue)
    {
        WeakExpander.IsExpanded = issue == "weak";
        ReusedExpander.IsExpanded = issue == "reused";
        OldExpander.IsExpanded = issue == "old";
        MissingExpander.IsExpanded = issue == "missing";

        var target = issue switch
        {
            "weak" => WeakExpander,
            "reused" => ReusedExpander,
            "old" => OldExpander,
            "missing" => MissingExpander,
            _ => null
        };

        if (target is null)
            return;

        DispatcherQueue.TryEnqueue(() => target.StartBringIntoView());
    }

    private void PopulateStrengthBars(PasswordHealthReport report, int loginTotal)
    {
        StrengthBarsPanel.Children.Clear();
        if (loginTotal == 0)
        {
            StrengthBarsPanel.Children.Add(new TextBlock
            {
                Text = "Add login entries to see a strength breakdown.",
                Foreground = FortivaControlTheme.GetBrush("FortivaMutedBrush"),
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

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FortivaControlTheme.GetBrush("FortivaBodyBrush")
        });

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
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FortivaControlTheme.GetBrush("FortivaHeadingBrush")
        });
        return panel;
    }

    private void PopulateImportDuplicates(IReadOnlyList<ImportBatch> batches)
    {
        var rows = new List<ImportDuplicateDisplay>();
        foreach (var batch in batches.Where(b => b.SkippedDuplicateCount > 0).OrderByDescending(b => b.ImportedAt))
        {
            if (batch.SkippedDuplicates.Count > 0)
            {
                rows.AddRange(batch.SkippedDuplicates.Select(dup => new ImportDuplicateDisplay(
                    dup.ExistingEntryId,
                    dup.Title,
                    string.IsNullOrWhiteSpace(dup.Username) ? dup.Url : dup.Username,
                    FormatImportBatchLabel(batch))));
            }
            else
            {
                rows.Add(new ImportDuplicateDisplay(
                    null,
                    $"{batch.SkippedDuplicateCount} duplicate{(batch.SkippedDuplicateCount == 1 ? "" : "s")} skipped",
                    "Existing vault entries were kept",
                    FormatImportBatchLabel(batch)));
            }
        }

        if (rows.Count == 0)
        {
            ImportDuplicatesExpander.Visibility = Visibility.Collapsed;
            ImportDuplicatesList.ItemsSource = null;
            return;
        }

        ImportDuplicatesExpander.Visibility = Visibility.Visible;
        ImportDuplicatesExpander.Header = BuildExpanderHeader(
            "Import duplicates (existing entries kept)",
            rows.Count,
            "\uE8C8",
            FortivaThemeResources.StatusWarning);
        ImportDuplicatesList.ItemsSource = rows;
    }

    private static string FormatImportBatchLabel(ImportBatch batch)
    {
        var name = !string.IsNullOrWhiteSpace(batch.DisplayName)
            ? batch.DisplayName
            : batch.SourceLabel;
        return $"{name} · {batch.ImportedAt.LocalDateTime:yyyy-MM-dd}";
    }

    private void HighlightSelectedCard(Border card, bool selected, bool hasIssues)
    {
        if (selected)
        {
            card.BorderThickness = new Thickness(2);
            card.BorderBrush = FortivaThemeResources.GetBrush("AccentFillColorDefaultBrush");
            card.Opacity = 1.0;
            return;
        }

        HighlightCard(card, hasIssues);
    }

    private void HighlightCard(Border card, bool active)
    {
        card.Opacity = active ? 1.0 : 0.72;
        card.BorderThickness = active ? new Thickness(2) : new Thickness(0);
        card.BorderBrush = active ? FortivaThemeResources.GetBrush("AccentFillColorDefaultBrush") : null;
    }

    private static Brush GetBrush(string key)
        => FortivaThemeResources.GetBrush(key);
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

public sealed class ImportDuplicateDisplay
{
    public ImportDuplicateDisplay(Guid? existingEntryId, string title, string subtitle, string batchLabel)
    {
        ExistingEntryId = existingEntryId;
        Title = title;
        Subtitle = subtitle;
        BatchLabel = batchLabel;
    }

    public Guid? ExistingEntryId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string BatchLabel { get; }
}
