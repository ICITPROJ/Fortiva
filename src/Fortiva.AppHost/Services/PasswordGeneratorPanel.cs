using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>Where the shared generator panel is hosted — controls intro copy only.</summary>
public enum PasswordGeneratorHostMode
{
    Page,
    Dialog
}

/// <summary>Reusable password generator UI — identical layout in nav page and vault/entry dialog.</summary>
public sealed class PasswordGeneratorPanel
{
    private readonly ShellViewModel _vm;
    private readonly PasswordGeneratorOptions _options;
    private readonly ComboBox _presetBox;
    private readonly TextBlock _lengthLabel;
    private readonly TextBlock _lengthValue;
    private readonly Slider _lengthSlider;
    private readonly TextBlock _wordCountLabel;
    private readonly Slider _wordCountSlider;
    private readonly TextBlock _separatorLabel;
    private readonly TextBox _separatorBox;
    private readonly StackPanel _charOptionsPanel;
    private readonly TextBlock _charSetsHeader;
    private readonly TextBlock _symbolsLabel;
    private readonly TextBox _symbolsBox;
    private readonly TextBlock _customCharsetLabel;
    private readonly TextBox _customCharsetBox;
    private readonly ToggleSwitch _ambiguousToggle;
    private readonly ToggleSwitch _requireEachToggle;
    private readonly TextBlock _preview;
    private readonly TextBlock _strengthLabel;
    private readonly TextBlock _errorLabel;
    private readonly Border _previewBorder;
    private readonly TextBlock? _introText;
    private readonly TextBlock _previewLabel;
    private readonly IReadOnlyList<TextBlock> _sectionLabels;
    private readonly List<ToggleSwitch> _charToggles = [];
    private readonly List<TextBlock> _charToggleLabels = [];

    public StackPanel Root { get; }

    public string CurrentPassword => _preview.Text;

    public PasswordGeneratorPanel(
        ShellViewModel vm,
        PasswordGeneratorOptions? initial = null,
        PasswordGeneratorHostMode hostMode = PasswordGeneratorHostMode.Page)
    {
        _vm = vm;
        _options = initial?.Clone() ?? PasswordGeneratorOptions.Default;

        _presetBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                "Alphanumeric + symbols",
                "Alphanumeric only",
                "Passphrase",
                "PIN / numeric",
                "Custom charset"
            },
            SelectedIndex = 0
        };
        FortivaControlTheme.TryApplyStyle(_presetBox, "FortivaComboBox");
        FortivaControlTheme.ApplyComboBox(_presetBox);

        _lengthLabel = CreateSectionLabel("Length");
        _lengthValue = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _lengthSlider = new Slider { Minimum = 8, Maximum = 128, StepFrequency = 1, Value = _options.Length };

        _wordCountLabel = CreateSectionLabel("Word count");
        _wordCountLabel.Visibility = Visibility.Collapsed;
        _wordCountSlider = new Slider { Minimum = 3, Maximum = 10, StepFrequency = 1, Value = _options.PassphraseWordCount, Visibility = Visibility.Collapsed };

        _separatorLabel = CreateSectionLabel("Word separator");
        _separatorLabel.Visibility = Visibility.Collapsed;
        _separatorBox = new TextBox { Width = 80, Text = _options.PassphraseSeparator, MaxLength = 3, Visibility = Visibility.Collapsed };
        FortivaControlTheme.ApplyTextBox(_separatorBox);

        var lowerToggle = CreateCharToggle(_options.IncludeLowercase);
        var upperToggle = CreateCharToggle(_options.IncludeUppercase);
        var digitToggle = CreateCharToggle(_options.IncludeDigits);
        var symbolToggle = CreateCharToggle(_options.IncludeSymbols);

        _charSetsHeader = CreateSectionLabel("Character sets");
        _charOptionsPanel = new StackPanel { Spacing = 6 };
        _charOptionsPanel.Children.Add(CreateCharToggleRow("Lowercase (a-z)", lowerToggle));
        _charOptionsPanel.Children.Add(CreateCharToggleRow("Uppercase (A-Z)", upperToggle));
        _charOptionsPanel.Children.Add(CreateCharToggleRow("Digits (0-9)", digitToggle));
        _charOptionsPanel.Children.Add(CreateCharToggleRow("Symbols", symbolToggle));

        _presetBox.SelectedIndex = _options.Mode switch
        {
            PasswordGeneratorMode.Alphanumeric => 1,
            PasswordGeneratorMode.Passphrase => 2,
            PasswordGeneratorMode.Pin => 3,
            PasswordGeneratorMode.Custom => 4,
            _ => 0
        };

        _symbolsLabel = CreateSectionLabel("Symbol characters (editable)", small: true);
        _symbolsBox = new TextBox
        {
            Text = _options.CustomSymbols,
            FontFamily = new FontFamily("Consolas"),
            PlaceholderText = PasswordGeneratorOptions.DefaultSymbols,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        FortivaControlTheme.ApplyTextBox(_symbolsBox);

        _customCharsetLabel = CreateSectionLabel("Custom charset (only these characters)", small: true);
        _customCharsetLabel.Visibility = Visibility.Collapsed;
        _customCharsetBox = new TextBox
        {
            PlaceholderText = "e.g. abcdefghijklmnopqrstuvwxyz0123456789",
            FontFamily = new FontFamily("Consolas"),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        FortivaControlTheme.ApplyTextBox(_customCharsetBox);

        _ambiguousToggle = CreateOptionToggle("Exclude ambiguous (0/O, 1/l/I)", _options.ExcludeAmbiguous);
        _requireEachToggle = CreateOptionToggle("Require at least one of each selected type", _options.RequireFromEachGroup);

        _previewLabel = CreateSectionLabel("Preview (select text to copy)");
        _preview = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        _previewBorder = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            MinHeight = 56,
            CornerRadius = new CornerRadius(10),
            Child = _preview
        };
        FortivaControlTheme.ApplyPreviewSurface(_previewBorder, _preview);
        _preview.FontSize = 17;

        _strengthLabel = new TextBlock { FontSize = 12 };
        _errorLabel = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed };

        _sectionLabels =
        [
            _lengthLabel, _wordCountLabel, _separatorLabel, _charSetsHeader,
            _symbolsLabel, _customCharsetLabel, _previewLabel
        ];

        void ApplyPresetUi()
        {
            var idx = _presetBox.SelectedIndex;
            var isPassphrase = idx == 2;
            var isPin = idx == 3;
            var isCustom = idx == 4;

            _lengthLabel.Visibility = isPassphrase ? Visibility.Collapsed : Visibility.Visible;
            _lengthValue.Visibility = isPassphrase ? Visibility.Collapsed : Visibility.Visible;
            _lengthSlider.Visibility = isPassphrase ? Visibility.Collapsed : Visibility.Visible;
            _wordCountLabel.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _wordCountSlider.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _separatorLabel.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _separatorBox.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;

            _charOptionsPanel.Visibility = isPassphrase || isPin || isCustom ? Visibility.Collapsed : Visibility.Visible;
            _charSetsHeader.Visibility = _charOptionsPanel.Visibility;
            _symbolsLabel.Visibility = isPassphrase || isPin || isCustom ? Visibility.Collapsed : Visibility.Visible;
            _symbolsBox.Visibility = isPassphrase || isPin || isCustom ? Visibility.Collapsed : Visibility.Visible;
            _customCharsetLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            _customCharsetBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            _ambiguousToggle.Visibility = isPassphrase || isCustom ? Visibility.Collapsed : Visibility.Visible;
            _requireEachToggle.Visibility = isPassphrase || isPin || isCustom ? Visibility.Collapsed : Visibility.Visible;

            if (isPin)
            {
                _lengthSlider.Minimum = 4;
                _lengthSlider.Maximum = 16;
                if (_lengthSlider.Value < 4) _lengthSlider.Value = 6;
            }
            else if (!isPassphrase)
            {
                _lengthSlider.Minimum = 8;
                _lengthSlider.Maximum = 128;
            }

            UpdateLengthValue();
        }

        void UpdateLengthValue()
            => _lengthValue.Text = $"{(int)_lengthSlider.Value} characters";

        PasswordGeneratorOptions BuildOptions()
        {
            var o = _options.Clone();
            o.Length = (int)_lengthSlider.Value;
            o.PassphraseWordCount = (int)_wordCountSlider.Value;
            o.PassphraseSeparator = _separatorBox.Text;
            o.IncludeLowercase = lowerToggle.IsOn;
            o.IncludeUppercase = upperToggle.IsOn;
            o.IncludeDigits = digitToggle.IsOn;
            o.IncludeSymbols = symbolToggle.IsOn;
            o.CustomSymbols = _symbolsBox.Text;
            o.CustomCharset = string.IsNullOrWhiteSpace(_customCharsetBox.Text) ? null : _customCharsetBox.Text.Trim();
            o.ExcludeAmbiguous = _ambiguousToggle.IsOn;
            o.RequireFromEachGroup = _requireEachToggle.IsOn;

            o.Mode = _presetBox.SelectedIndex switch
            {
                1 => PasswordGeneratorMode.Alphanumeric,
                2 => PasswordGeneratorMode.Passphrase,
                3 => PasswordGeneratorMode.Pin,
                4 => PasswordGeneratorMode.Custom,
                _ => PasswordGeneratorMode.AlphanumericSymbols
            };

            if (o.Mode == PasswordGeneratorMode.Alphanumeric)
                o.IncludeSymbols = false;

            return o;
        }

        void Regenerate()
        {
            _errorLabel.Visibility = Visibility.Collapsed;
            try
            {
                var built = BuildOptions();
                var pw = _vm.GeneratePassword(built);
                _preview.Text = pw;
                var analysis = _vm.AnalyzeStrength(pw);
                _strengthLabel.Text = $"{analysis.Label} · {analysis.EntropyBits:F0} bits entropy";
            }
            catch (Exception ex)
            {
                _preview.Text = "";
                _errorLabel.Text = ex.Message;
                _errorLabel.Visibility = Visibility.Visible;
                _strengthLabel.Text = "";
            }
        }

        _presetBox.SelectionChanged += (_, _) => { ApplyPresetUi(); Regenerate(); };
        _lengthSlider.ValueChanged += (_, _) => { UpdateLengthValue(); Regenerate(); };
        _wordCountSlider.ValueChanged += (_, _) => Regenerate();
        _separatorBox.TextChanged += (_, _) => Regenerate();
        lowerToggle.Toggled += (_, _) => Regenerate();
        upperToggle.Toggled += (_, _) => Regenerate();
        digitToggle.Toggled += (_, _) => Regenerate();
        symbolToggle.Toggled += (_, _) => Regenerate();
        _symbolsBox.TextChanged += (_, _) => Regenerate();
        _customCharsetBox.TextChanged += (_, _) => Regenerate();
        _ambiguousToggle.Toggled += (_, _) => Regenerate();
        _requireEachToggle.Toggled += (_, _) => Regenerate();

        RegenerateInternal = Regenerate;

        var lengthHeader = new Grid { ColumnSpacing = 8 };
        lengthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lengthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lengthHeader.Children.Add(_lengthLabel);
        Grid.SetColumn(_lengthValue, 1);
        lengthHeader.Children.Add(_lengthValue);

        var leftOptions = new StackPanel { Spacing = 10 };
        leftOptions.Children.Add(CreateSectionLabel("Preset"));
        leftOptions.Children.Add(_presetBox);
        leftOptions.Children.Add(lengthHeader);
        leftOptions.Children.Add(_lengthSlider);
        leftOptions.Children.Add(_wordCountLabel);
        leftOptions.Children.Add(_wordCountSlider);
        leftOptions.Children.Add(_separatorLabel);
        leftOptions.Children.Add(_separatorBox);
        leftOptions.Children.Add(_ambiguousToggle);
        leftOptions.Children.Add(_requireEachToggle);

        var rightOptions = new StackPanel { Spacing = 10 };
        rightOptions.Children.Add(_charSetsHeader);
        rightOptions.Children.Add(_charOptionsPanel);
        rightOptions.Children.Add(_symbolsLabel);
        rightOptions.Children.Add(_symbolsBox);
        rightOptions.Children.Add(_customCharsetLabel);
        rightOptions.Children.Add(_customCharsetBox);

        var optionsGrid = new Grid { ColumnSpacing = 28 };
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftOptions, 0);
        Grid.SetColumn(rightOptions, 1);
        optionsGrid.Children.Add(leftOptions);
        optionsGrid.Children.Add(rightOptions);

        Root = new StackPanel
        {
            Spacing = 14,
            MinWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (hostMode == PasswordGeneratorHostMode.Dialog)
        {
            _introText = new TextBlock
            {
                Text = "Create strong passwords for new accounts or rotate existing ones.",
                TextWrapping = TextWrapping.WrapWholeWords,
                FontSize = 13
            };
            Root.Children.Add(_introText);
        }

        Root.Children.Add(_previewLabel);
        Root.Children.Add(_previewBorder);
        Root.Children.Add(_strengthLabel);
        Root.Children.Add(_errorLabel);
        Root.Children.Add(optionsGrid);

        Root.ActualThemeChanged += (_, _) => ApplyThemeResources();
        _vm.ThemeChanged += ApplyThemeResources;

        ApplyPresetUi();
        ApplyThemeResources();
        Regenerate();
    }

    private Action RegenerateInternal { get; }

    public void Regenerate() => RegenerateInternal();

    public void ApplyThemeResources()
    {
        var theme = FortivaControlTheme.ResolveEffectiveTheme(Root.XamlRoot, Root);
        Root.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(Root, theme);

        FortivaControlTheme.ApplyPreviewSurface(_previewBorder, _preview, Root);
        FortivaControlTheme.ApplyComboBox(_presetBox, Root);
        FortivaControlTheme.TryApplyStyle(_presetBox, "FortivaComboBox");
        FortivaControlTheme.ApplyTextBox(_separatorBox, Root);
        FortivaControlTheme.ApplyTextBox(_symbolsBox, Root);
        FortivaControlTheme.ApplyTextBox(_customCharsetBox, Root);
        FortivaControlTheme.ApplySlider(_lengthSlider, Root);
        FortivaControlTheme.ApplySlider(_wordCountSlider, Root);
        FortivaControlTheme.TryApplyStyle(_lengthSlider, "FortivaSlider");
        FortivaControlTheme.TryApplyStyle(_wordCountSlider, "FortivaSlider");

        foreach (var toggle in _charToggles.Concat([_ambiguousToggle, _requireEachToggle]))
        {
            FortivaControlTheme.ApplyToggleSwitch(toggle, Root);
            FortivaControlTheme.TryApplyStyle(toggle, "FortivaToggleSwitch");
        }

        if (_introText is not null)
            FortivaControlTheme.ApplyBodyText(_introText, Root);

        foreach (var label in _sectionLabels)
            FortivaControlTheme.ApplySectionLabel(label, context: Root);

        foreach (var label in _charToggleLabels)
            FortivaControlTheme.ApplyBodyText(label, Root);

        _errorLabel.Foreground = FortivaControlTheme.GetBrush("SystemFillColorCriticalBrush", theme, Root);
        FortivaControlTheme.ApplyMutedText(_strengthLabel, Root);
        FortivaControlTheme.ApplyMutedText(_lengthValue, Root);
    }

    private static TextBlock CreateSectionLabel(string text, bool small = false)
        => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = small ? 12 : 13
        };

    private ToggleSwitch CreateCharToggle(bool isOn)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            MinWidth = 44,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _charToggles.Add(toggle);
        return toggle;
    }

    private static ToggleSwitch CreateOptionToggle(string label, bool isOn)
        => new()
        {
            Header = label,
            IsOn = isOn,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private Grid CreateCharToggleRow(string label, ToggleSwitch toggle)
    {
        var grid = new Grid { MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 12, 0)
        };
        _charToggleLabels.Add(text);
        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(text);
        grid.Children.Add(toggle);
        return grid;
    }
}
