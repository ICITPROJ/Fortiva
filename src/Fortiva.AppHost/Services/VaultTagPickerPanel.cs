using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Fortiva.AppHost.Services;

/// <summary>Pick existing categories (tags) or type a new one — used in vault, quick add, and entry editor.</summary>
public sealed class VaultTagPickerPanel
{
    private readonly ShellViewModel _vm;
    private FrameworkElement? _themeHost;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _chipPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _chipScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Disabled,
        Padding = new Thickness(0),
        MinHeight = 36
    };
    private readonly Border _outerShell = new();
    private readonly StackPanel _inner = new() { Spacing = 10 };
    private readonly TextBox _newTagBox = new() { PlaceholderText = "New category name…" };
    private readonly Button _addBtn = new();
    private readonly TextBlock _emptyHint = new()
    {
        Text = "No categories yet — type a name below.",
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    public VaultTagPickerPanel(ShellViewModel vm)
    {
        _vm = vm;

        _addBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new FontIcon { Glyph = "\uE710", FontSize = 12 },
                new TextBlock { Text = "Add" }
            }
        };
        _addBtn.Click += (_, _) => AddFromInput();

        _newTagBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                AddFromInput();
            }
        };

        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_newTagBox, 0);
        Grid.SetColumn(_addBtn, 1);
        addRow.Children.Add(_newTagBox);
        addRow.Children.Add(_addBtn);

        Root = _outerShell;
        _chipScroll.Content = _chipPanel;
        _inner.Children.Add(_chipScroll);
        _inner.Children.Add(addRow);
        _outerShell.Child = _inner;
    }

    public FrameworkElement Root { get; }

    public event Action? TagsChanged;

    private void NotifyTagsChanged() => TagsChanged?.Invoke();

    public void ApplyTheme(FrameworkElement? context = null)
    {
        if (context is not null)
            _themeHost = context;

        var host = _themeHost ?? Root;
        var theme = FortivaControlTheme.ResolveHostTheme(host);
        FortivaThemeResources.MergeOnto(Root, theme);
        Root.RequestedTheme = theme;
        FortivaControlTheme.ApplyThemeRecursively(Root, theme);
        _outerShell.RequestedTheme = theme;
        _outerShell.HorizontalAlignment = HorizontalAlignment.Stretch;
        _outerShell.Background = FortivaControlTheme.GetBrush("FortivaInputFillBrush", theme, host);
        _outerShell.BorderBrush = FortivaControlTheme.GetBrush("FortivaInputBorderBrush", theme, host);
        _outerShell.BorderThickness = new Thickness(1);
        _outerShell.CornerRadius = new CornerRadius(8);
        _outerShell.Padding = new Thickness(10);
        _inner.Spacing = 10;

        FortivaControlTheme.ApplyTextBox(_newTagBox, host, theme);
        FortivaControlTheme.ApplyInlineFieldButton(_addBtn, host, theme);
        FortivaControlTheme.ApplyMutedText(_emptyHint, host);
        if (_addBtn.Content is StackPanel addContent)
        {
            foreach (var child in addContent.Children.OfType<TextBlock>())
                FortivaControlTheme.ApplyBodyText(child, host);
        }
        RebuildChips();
    }

    public void SetEnabled(bool enabled)
    {
        _newTagBox.IsEnabled = enabled;
        _addBtn.IsEnabled = enabled;
        _chipScroll.IsEnabled = enabled;
    }

    public void ReloadKnownTags() => RebuildChips();

    public void SetSelectedTags(IEnumerable<string>? tags)
    {
        _selected.Clear();
        foreach (var tag in tags ?? [])
        {
            var normalized = VaultTagHelper.NormalizeTag(tag);
            if (normalized is not null)
                _selected.Add(normalized);
        }
        RebuildChips();
    }

    public IReadOnlyList<string> GetSelectedTags()
        => _selected.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

    private void AddFromInput()
    {
        var tag = VaultTagHelper.NormalizeTag(_newTagBox.Text);
        if (tag is null)
            return;

        _vm.EnsureVaultCategory(tag);
        _selected.Add(tag);
        _newTagBox.Text = "";
        RebuildChips();
        NotifyTagsChanged();
    }

    private void RebuildChips()
    {
        _chipPanel.Children.Clear();
        var tags = _vm.GetKnownVaultTags();
        var host = _themeHost ?? Root;
        var theme = FortivaControlTheme.ResolveHostTheme(host);
        if (tags.Count == 0)
        {
            _chipPanel.Children.Add(_emptyHint);
            return;
        }

        foreach (var tag in tags)
        {
            var isSelected = _selected.Contains(tag);
            var toggle = new ToggleButton
            {
                Content = tag,
                IsChecked = isSelected,
                Tag = tag,
                FontSize = 12,
                MaxWidth = 260
            };
            FortivaSurfaceEffects.ApplyChipToggle(toggle, isSelected, host, theme);
            toggle.Checked += ChipToggleChanged;
            toggle.Unchecked += ChipToggleChanged;
            toggle.PointerEntered += (_, _) =>
            {
                if (toggle.IsChecked != true)
                    toggle.Background = FortivaControlTheme.GetBrush("FortivaSurfaceSubtleBrush", theme, host);
            };
            toggle.PointerExited += (_, _) =>
                FortivaSurfaceEffects.ApplyChipToggle(toggle, toggle.IsChecked == true, host, theme);
            _chipPanel.Children.Add(toggle);
        }
    }

    private void ChipToggleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.Tag is not string tag)
            return;

        var selected = toggle.IsChecked == true;
        var host = _themeHost ?? Root;
        FortivaSurfaceEffects.ApplyChipToggle(toggle, selected, host);

        if (selected)
            _selected.Add(tag);
        else
            _selected.Remove(tag);
        NotifyTagsChanged();
    }
}
