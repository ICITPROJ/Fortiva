using Fortiva.AppHost.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;

namespace Fortiva.AppHost.Services;

/// <summary>Pick existing categories (tags) or type a new one — used in vault, quick add, and entry editor.</summary>
public sealed class VaultTagPickerPanel
{
    private static readonly ItemsPanelTemplate ChipWrapPanelTemplate = CreateChipWrapPanelTemplate();

    private readonly ShellViewModel _vm;
    private FrameworkElement? _themeHost;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly ItemsControl _chipItems = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly Border _outerShell = new();
    private readonly StackPanel _inner = new() { Spacing = 12 };
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
        _chipItems.ItemsPanel = ChipWrapPanelTemplate;

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
        _addBtn.MinWidth = 88;
        _addBtn.MinHeight = 44;
        _addBtn.Click += (_, _) => AddFromInput();

        _newTagBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                AddFromInput();
            }
        };

        var addRow = new Grid { ColumnSpacing = 10 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 88 });
        Grid.SetColumn(_newTagBox, 0);
        Grid.SetColumn(_addBtn, 1);
        addRow.Children.Add(_newTagBox);
        addRow.Children.Add(_addBtn);

        Root = _outerShell;
        _outerShell.HorizontalAlignment = HorizontalAlignment.Stretch;
        _inner.Children.Add(_chipItems);
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
        var theme = FortivaControlTheme.ResolveAppTheme();
        FortivaThemeResources.MergeOnto(Root, theme);
        Root.RequestedTheme = theme;
        FortivaControlTheme.ApplyInputContainer(_outerShell, host, theme);
        _outerShell.Padding = new Thickness(12);
        _inner.Spacing = 12;

        FortivaControlTheme.ApplyTextBox(_newTagBox, host, theme);
        FortivaControlTheme.ApplyInlineFieldButton(_addBtn, host, theme);
        FortivaControlTheme.ApplyMutedText(_emptyHint, host);
        if (_addBtn.Content is StackPanel addContent)
        {
            foreach (var child in addContent.Children.OfType<TextBlock>())
                FortivaControlTheme.ApplyBodyText(child, host);
        }

        RefreshChipThemes();
    }

    public void SetEnabled(bool enabled)
    {
        _newTagBox.IsEnabled = enabled;
        _addBtn.IsEnabled = enabled;
        _chipItems.IsEnabled = enabled;
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
        _chipItems.Items.Clear();
        var tags = _vm.GetKnownVaultTags();
        if (tags.Count == 0)
        {
            _chipItems.Items.Add(_emptyHint);
            return;
        }

        var host = _themeHost ?? Root;
        var theme = FortivaControlTheme.ResolveAppTheme();
        foreach (var tag in tags)
        {
            var isSelected = _selected.Contains(tag);
            var label = new TextBlock
            {
                Text = tag,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360
            };
            ToolTipService.SetToolTip(label, tag);
            var toggle = new ToggleButton
            {
                Content = label,
                IsChecked = isSelected,
                Tag = tag,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 8, 8)
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
            _chipItems.Items.Add(toggle);
        }
    }

    private void RefreshChipThemes()
    {
        var host = _themeHost ?? Root;
        var theme = FortivaControlTheme.ResolveAppTheme();
        foreach (var item in _chipItems.Items)
        {
            if (item is ToggleButton toggle)
                FortivaSurfaceEffects.ApplyChipToggle(toggle, toggle.IsChecked == true, host, theme);
            else if (item is TextBlock hint)
                FortivaControlTheme.ApplyMutedText(hint, host);
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

    private static ItemsPanelTemplate CreateChipWrapPanelTemplate()
    {
        const string xaml =
            """
            <ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <ItemsWrapGrid Orientation='Horizontal' MaximumRowsOrColumns='0'/>
            </ItemsPanelTemplate>
            """;
        return (ItemsPanelTemplate)XamlReader.Load(xaml);
    }
}
