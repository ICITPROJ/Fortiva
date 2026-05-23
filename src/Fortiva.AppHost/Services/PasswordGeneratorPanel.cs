using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Password;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fortiva.AppHost.Services;

/// <summary>Reusable password generator UI — embedded in page or dialog.</summary>
public sealed class PasswordGeneratorPanel
{
    private readonly ShellViewModel _vm;
    private readonly PasswordGeneratorOptions _options;
    private readonly ComboBox _presetBox;
    private readonly TextBlock _lengthLabel;
    private readonly Slider _lengthSlider;
    private readonly TextBlock _wordCountLabel;
    private readonly Slider _wordCountSlider;
    private readonly TextBlock _separatorLabel;
    private readonly TextBox _separatorBox;
    private readonly StackPanel _charOptionsPanel;
    private readonly TextBlock _symbolsLabel;
    private readonly TextBox _symbolsBox;
    private readonly TextBlock _customCharsetLabel;
    private readonly TextBox _customCharsetBox;
    private readonly ToggleSwitch _ambiguousToggle;
    private readonly ToggleSwitch _requireEachToggle;
    private readonly TextBlock _preview;
    private readonly TextBlock _strengthLabel;
    private readonly TextBlock _errorLabel;

    public StackPanel Root { get; }

    public string CurrentPassword => _preview.Text;

    public PasswordGeneratorPanel(ShellViewModel vm, PasswordGeneratorOptions? initial = null)
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

        _lengthLabel = new TextBlock { Text = "Length", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _lengthSlider = new Slider { Minimum = 8, Maximum = 128, StepFrequency = 1, Value = _options.Length };

        _wordCountLabel = new TextBlock { Text = "Word count", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Visibility = Visibility.Collapsed };
        _wordCountSlider = new Slider { Minimum = 3, Maximum = 10, StepFrequency = 1, Value = _options.PassphraseWordCount, Visibility = Visibility.Collapsed };

        _separatorLabel = new TextBlock { Text = "Word separator", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Visibility = Visibility.Collapsed };
        _separatorBox = new TextBox { Width = 80, Text = _options.PassphraseSeparator, MaxLength = 3, Visibility = Visibility.Collapsed };

        var lowerToggle = new ToggleSwitch { Header = "Lowercase (a-z)", IsOn = _options.IncludeLowercase };
        var upperToggle = new ToggleSwitch { Header = "Uppercase (A-Z)", IsOn = _options.IncludeUppercase };
        var digitToggle = new ToggleSwitch { Header = "Digits (0-9)", IsOn = _options.IncludeDigits };
        var symbolToggle = new ToggleSwitch { Header = "Symbols", IsOn = _options.IncludeSymbols };

        _charOptionsPanel = new StackPanel { Spacing = 4 };
        _charOptionsPanel.Children.Add(lowerToggle);
        _charOptionsPanel.Children.Add(upperToggle);
        _charOptionsPanel.Children.Add(digitToggle);
        _charOptionsPanel.Children.Add(symbolToggle);

        _symbolsLabel = new TextBlock { Text = "Symbol characters (editable)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 };
        _symbolsBox = new TextBox
        {
            Text = _options.CustomSymbols,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            PlaceholderText = PasswordGeneratorOptions.DefaultSymbols
        };

        _customCharsetLabel = new TextBlock
        {
            Text = "Custom charset (only these characters)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        _customCharsetBox = new TextBox
        {
            PlaceholderText = "e.g. abcdefghijklmnopqrstuvwxyz0123456789",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Visibility = Visibility.Collapsed
        };

        _ambiguousToggle = new ToggleSwitch { Header = "Exclude ambiguous (0/O, 1/l/I)", IsOn = _options.ExcludeAmbiguous };
        _requireEachToggle = new ToggleSwitch { Header = "Require at least one of each selected type", IsOn = _options.RequireFromEachGroup };

        _preview = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var previewBorder = new Border
        {
            Padding = new Thickness(12),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = _preview
        };

        _strengthLabel = new TextBlock { FontSize = 11, Opacity = 0.7 };
        _errorLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed
        };

        void ApplyPresetUi()
        {
            var idx = _presetBox.SelectedIndex;
            var isPassphrase = idx == 2;
            var isPin = idx == 3;
            var isCustom = idx == 4;

            _lengthLabel.Visibility = isPassphrase ? Visibility.Collapsed : Visibility.Visible;
            _lengthSlider.Visibility = isPassphrase ? Visibility.Collapsed : Visibility.Visible;
            _wordCountLabel.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _wordCountSlider.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _separatorLabel.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;
            _separatorBox.Visibility = isPassphrase ? Visibility.Visible : Visibility.Collapsed;

            _charOptionsPanel.Visibility = isPassphrase || isPin || isCustom ? Visibility.Collapsed : Visibility.Visible;
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
        }

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
        _lengthSlider.ValueChanged += (_, _) => Regenerate();
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
        ApplyPresetUi();

        Root = new StackPanel { Spacing = 10, MinWidth = 320 };
        Root.Children.Add(new TextBlock
        {
            Text = "Create strong passwords for new accounts or rotate existing ones.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.7,
            FontSize = 13
        });
        Root.Children.Add(new TextBlock { Text = "Preset", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Root.Children.Add(_presetBox);
        Root.Children.Add(_lengthLabel);
        Root.Children.Add(_lengthSlider);
        Root.Children.Add(_wordCountLabel);
        Root.Children.Add(_wordCountSlider);
        Root.Children.Add(_separatorLabel);
        Root.Children.Add(_separatorBox);
        Root.Children.Add(_charOptionsPanel);
        Root.Children.Add(_symbolsLabel);
        Root.Children.Add(_symbolsBox);
        Root.Children.Add(_customCharsetLabel);
        Root.Children.Add(_customCharsetBox);
        Root.Children.Add(_ambiguousToggle);
        Root.Children.Add(_requireEachToggle);
        Root.Children.Add(new TextBlock
        {
            Text = "Preview (select text to copy)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        });
        Root.Children.Add(previewBorder);
        Root.Children.Add(_strengthLabel);
        Root.Children.Add(_errorLabel);

        Regenerate();
    }

    private Action RegenerateInternal { get; }

    public void Regenerate() => RegenerateInternal();
}
