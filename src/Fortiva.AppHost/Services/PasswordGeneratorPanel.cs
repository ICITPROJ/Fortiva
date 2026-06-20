using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

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
    private readonly ClipboardService _clipboard;
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
    private readonly Border? _optionsShell;
    private readonly VaultTagPickerPanel _tagPicker;
    private readonly TextBlock _categoriesLabel;
    private readonly TextBlock? _introText;
    private readonly TextBlock _previewHint;
    private readonly Button _copyPreviewBtn;
    private readonly TextBlock _optionsHeader;
    private readonly Border _categoriesDivider;
    private readonly Button? _dialogRegenerateBtn;
    private readonly List<TextBlock> _sectionLabelBlocks = [];
    private readonly List<ToggleSwitch> _charToggles = [];

    private FrameworkElement? _themeHost;

    private readonly PasswordGeneratorHostMode _hostMode;

    public string CurrentPassword => _preview.Text;

    public IReadOnlyList<string> GetSelectedTags() => _tagPicker.GetSelectedTags();

    public void SetSelectedTags(IEnumerable<string>? tags) => _tagPicker.SetSelectedTags(tags);

    public StackPanel Root { get; }

    public PasswordGeneratorPanel(
        ShellViewModel vm,
        PasswordGeneratorOptions? initial = null,
        PasswordGeneratorHostMode hostMode = PasswordGeneratorHostMode.Page,
        ClipboardService? clipboard = null)
    {
        _hostMode = hostMode;
        _vm = vm;
        _clipboard = clipboard ?? new ClipboardService(
            vm.Policy,
            vm.PersonalSettings.ClipboardClearSeconds,
            vm.LogPolicyViolation);
        _tagPicker = new VaultTagPickerPanel(vm);
        _options = initial?.Clone() ?? PasswordGeneratorOptions.Default;

        _presetBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "Preset",
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

        var lowerToggle = CreateCharToggle("Lowercase (a-z)", _options.IncludeLowercase);
        var upperToggle = CreateCharToggle("Uppercase (A-Z)", _options.IncludeUppercase);
        var digitToggle = CreateCharToggle("Digits (0-9)", _options.IncludeDigits);
        var symbolToggle = CreateCharToggle("Symbols", _options.IncludeSymbols);

        _charSetsHeader = CreateSectionLabel("Character sets");
        _charOptionsPanel = new StackPanel { Spacing = 4 };
        _charOptionsPanel.Children.Add(lowerToggle);
        _charOptionsPanel.Children.Add(upperToggle);
        _charOptionsPanel.Children.Add(digitToggle);
        _charOptionsPanel.Children.Add(symbolToggle);

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

        _customCharsetLabel = CreateSectionLabel("Custom charset (only these characters)", small: true);
        _customCharsetLabel.Visibility = Visibility.Collapsed;
        _customCharsetBox = new TextBox
        {
            PlaceholderText = "e.g. abcdefghijklmnopqrstuvwxyz0123456789",
            FontFamily = new FontFamily("Consolas"),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _ambiguousToggle = CreateCharToggle("Exclude ambiguous (0/O, 1/l/I)", _options.ExcludeAmbiguous);
        _requireEachToggle = CreateCharToggle("Require at least one of each selected type", _options.RequireFromEachGroup);

        _preview = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var previewTitle = CreateSectionLabel("Generated password");
        _previewHint = new TextBlock
        {
            Text = "Select to copy",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var previewHeader = new Grid { ColumnSpacing = 8 };
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(previewTitle, 0);
        Grid.SetColumn(_previewHint, 1);

        _copyPreviewBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 14 }
        };
        ToolTipService.SetToolTip(_copyPreviewBtn, "Copy password");
        _copyPreviewBtn.Click += (_, _) => CopyPreviewToClipboard();
        Grid.SetColumn(_copyPreviewBtn, 2);

        previewHeader.Children.Add(previewTitle);
        previewHeader.Children.Add(_previewHint);
        previewHeader.Children.Add(_copyPreviewBtn);

        var previewInner = new StackPanel { Spacing = 0 };
        previewInner.Children.Add(previewHeader);
        previewInner.Children.Add(_preview);

        _previewBorder = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            MinHeight = 72,
            CornerRadius = new CornerRadius(12),
            Child = previewInner
        };
        FortivaControlTheme.ApplyPreviewSurface(_previewBorder, _preview);

        _strengthLabel = new TextBlock { FontSize = 12, Margin = new Thickness(2, 0, 0, 0) };
        _errorLabel = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 0) };

        _sectionLabelBlocks =
        [
            _lengthLabel, _wordCountLabel, _separatorLabel, _charSetsHeader,
            _symbolsLabel, _customCharsetLabel, previewTitle
        ];
        _categoriesLabel = CreateSectionLabel("Categories (optional)", pageHeader: true);
        _optionsHeader = CreateSectionLabel("Generation rules", pageHeader: true);
        _categoriesDivider = new Border { Height = 1, Opacity = 0.55, Margin = new Thickness(0, 4, 0, 8) };

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
                ApplyStrengthColor(analysis.Strength);
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

        var lengthHeader = new Grid { ColumnSpacing = 8, MinHeight = 24 };
        lengthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lengthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lengthHeader.Children.Add(_lengthLabel);
        Grid.SetColumn(_lengthValue, 1);
        lengthHeader.Children.Add(_lengthValue);

        var leftOptions = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
        leftOptions.Children.Add(_presetBox);
        leftOptions.Children.Add(lengthHeader);
        leftOptions.Children.Add(_lengthSlider);
        leftOptions.Children.Add(_wordCountLabel);
        leftOptions.Children.Add(_wordCountSlider);
        leftOptions.Children.Add(_separatorLabel);
        leftOptions.Children.Add(_separatorBox);
        leftOptions.Children.Add(_ambiguousToggle);
        leftOptions.Children.Add(_requireEachToggle);

        var rightOptions = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
        rightOptions.Children.Add(_charSetsHeader);
        rightOptions.Children.Add(_charOptionsPanel);
        rightOptions.Children.Add(_symbolsLabel);
        rightOptions.Children.Add(_symbolsBox);
        rightOptions.Children.Add(_customCharsetLabel);
        rightOptions.Children.Add(_customCharsetBox);

        FrameworkElement optionsContent;
        if (_hostMode == PasswordGeneratorHostMode.Page)
        {
            var optionsStack = new StackPanel { Spacing = 20 };
            optionsStack.Children.Add(leftOptions);
            optionsStack.Children.Add(rightOptions);
            optionsContent = optionsStack;
        }
        else
        {
            var optionsGrid = new Grid { ColumnSpacing = 24 };
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            Grid.SetColumn(leftOptions, 0);
            Grid.SetColumn(rightOptions, 1);
            optionsGrid.Children.Add(leftOptions);
            optionsGrid.Children.Add(rightOptions);
            optionsContent = optionsGrid;
        }

        FrameworkElement optionsContainer;
        if (_hostMode == PasswordGeneratorHostMode.Page)
        {
            optionsContainer = optionsContent;
        }
        else
        {
            _optionsShell = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20, 18, 20, 18),
                BorderThickness = new Thickness(1),
                Child = optionsContent
            };
            optionsContainer = _optionsShell;
        }

        Root = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 0, 0, 24)
        };

        if (hostMode == PasswordGeneratorHostMode.Dialog)
        {
            _introText = new TextBlock
            {
                Text = "Create strong passwords for new accounts or rotate existing ones.",
                TextWrapping = TextWrapping.WrapWholeWords,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Root.Children.Add(_introText);
        }

        Root.Children.Add(_previewBorder);
        Root.Children.Add(_strengthLabel);
        Root.Children.Add(_errorLabel);

        if (hostMode == PasswordGeneratorHostMode.Dialog)
        {
            _dialogRegenerateBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72C", FontSize = 14 },
                        new TextBlock { Text = "Regenerate" }
                    }
                }
            };
            _dialogRegenerateBtn.Click += (_, _) => Regenerate();
            Root.Children.Add(_dialogRegenerateBtn);
        }

        Root.Children.Add(_optionsHeader);
        Root.Children.Add(optionsContainer);

        Root.Children.Add(_categoriesDivider);
        Root.Children.Add(_categoriesLabel);
        Root.Children.Add(_tagPicker.Root);

        _vm.ThemeChanged += () => ApplyThemeResources(_themeHost);

        ApplyPresetUi();
        Regenerate();
    }

    private Action RegenerateInternal { get; }

    public void Regenerate() => RegenerateInternal();

    private void CopyPreviewToClipboard()
    {
        if (string.IsNullOrEmpty(_preview.Text))
            return;

        _clipboard.RefreshPolicy(_vm.Policy, _vm.PersonalSettings.ClipboardClearSeconds);
        try
        {
            _clipboard.CopyText(_preview.Text);
        }
        catch (InvalidOperationException)
        {
            _previewHint.Text = "Copy blocked";
            return;
        }

        _previewHint.Text = "Copied!";
        FortivaSurfaceEffects.PulseSuccess(_previewBorder);
        _ = ResetCopyHintAsync();
    }

    private async Task ResetCopyHintAsync()
    {
        try
        {
            await Task.Delay(1600);
            _previewHint.Text = "Select to copy";
        }
        catch { /* dialog closed */ }
    }

    public void ApplyThemeResources(FrameworkElement? host = null)
    {
        if (host is not null)
            _themeHost = host;

        var themeHost = _themeHost ?? Root;
        var theme = _themeHost is not null
            ? FortivaControlTheme.ResolveHostTheme(_themeHost)
            : FortivaControlTheme.ResolveEffectiveTheme(Root.XamlRoot, Root);

        Root.RequestedTheme = theme;
        FortivaThemeResources.MergeOnto(Root, theme);

        if (_optionsShell is not null)
        {
            _optionsShell.Background = FortivaControlTheme.GetBrush("FortivaGlassFillBrush", theme, themeHost);
            _optionsShell.BorderBrush = FortivaControlTheme.GetBrush("FortivaGlassBorderBrush", theme, themeHost);
            FortivaSurfaceEffects.ApplyCardElevation(_optionsShell, 4f);
        }

        FortivaControlTheme.ApplyPreviewSurface(_previewBorder, _preview, themeHost);
        FortivaSurfaceEffects.ApplyIconButton(_copyPreviewBtn, themeHost);
        FortivaControlTheme.ApplyComboBox(_presetBox, themeHost, theme);
        FortivaControlTheme.ApplyTextBox(_separatorBox, themeHost, theme);
        FortivaControlTheme.ApplyTextBox(_symbolsBox, themeHost, theme);
        FortivaControlTheme.ApplyTextBox(_customCharsetBox, themeHost, theme);
        FortivaControlTheme.TryApplyStyle(_lengthSlider, "FortivaSlider");
        FortivaControlTheme.TryApplyStyle(_wordCountSlider, "FortivaSlider");
        FortivaControlTheme.ApplySlider(_lengthSlider, themeHost, theme);
        FortivaControlTheme.ApplySlider(_wordCountSlider, themeHost, theme);

        foreach (var toggle in _charToggles.Concat([_ambiguousToggle, _requireEachToggle]))
        {
            FortivaControlTheme.ApplyToggleSwitch(toggle, themeHost);
            FortivaControlTheme.TryApplyStyle(toggle, "FortivaToggleSwitch");
            toggle.RequestedTheme = theme;
            toggle.Foreground = FortivaControlTheme.GetBrush("FortivaBodyBrush", theme, themeHost);
        }

        if (_introText is not null)
            FortivaControlTheme.ApplyBodyText(_introText, themeHost);

        if (_dialogRegenerateBtn is not null)
            FortivaControlTheme.ApplySecondaryButton(_dialogRegenerateBtn, themeHost);

        _tagPicker.ApplyTheme(themeHost);

        foreach (var label in _sectionLabelBlocks)
            FortivaControlTheme.ApplySectionLabel(label, context: themeHost);

        FortivaControlTheme.ApplySectionLabel(_categoriesLabel, pageHeader: true, context: themeHost);
        FortivaControlTheme.ApplySectionLabel(_optionsHeader, pageHeader: true, context: themeHost);

        _categoriesDivider.Background = FortivaControlTheme.GetBrush("FortivaGlassBorderBrush", theme, themeHost);

        _errorLabel.Foreground = FortivaControlTheme.GetBrush("SystemFillColorCriticalBrush", theme, themeHost);
        FortivaControlTheme.ApplyMutedText(_strengthLabel, themeHost);
        FortivaControlTheme.ApplyMutedText(_lengthValue, themeHost);
        FortivaControlTheme.ApplyMutedText(_previewHint, themeHost);

        if (!string.IsNullOrEmpty(_preview.Text))
        {
            var analysis = _vm.AnalyzeStrength(_preview.Text);
            ApplyStrengthColor(analysis.Strength);
        }
    }

    private void ApplyStrengthColor(PasswordStrength strength)
    {
        _strengthLabel.Foreground = strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak =>
                new SolidColorBrush(Color.FromArgb(255, 220, 50, 50)),
            PasswordStrength.Fair =>
                new SolidColorBrush(Color.FromArgb(255, 200, 130, 0)),
            PasswordStrength.Strong =>
                new SolidColorBrush(Color.FromArgb(255, 0, 160, 80)),
            _ => FortivaControlTheme.GetBrush("FortivaAccentBrush", FortivaControlTheme.ResolveHostTheme(_themeHost ?? Root), _themeHost ?? Root)
        };
    }

    private TextBlock CreateSectionLabel(string text, bool small = false, bool pageHeader = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = pageHeader ? 16 : (small ? 12 : 13)
        };
        _sectionLabelBlocks.Add(label);
        return label;
    }

    private ToggleSwitch CreateCharToggle(string header, bool isOn)
    {
        var toggle = new ToggleSwitch
        {
            Header = header,
            IsOn = isOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
            OffContent = "Off",
            OnContent = "On"
        };
        FortivaControlTheme.ApplyToggleSwitch(toggle);
        FortivaControlTheme.TryApplyStyle(toggle, "FortivaToggleSwitch");
        _charToggles.Add(toggle);
        return toggle;
    }
}
