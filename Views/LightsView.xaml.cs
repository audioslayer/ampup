using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class LightsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Per-channel controls
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly EffectPickerControl[] _effectPickers = new EffectPickerControl[5];
    private readonly Border[] _color1Swatches = new Border[5];
    private readonly Border[] _color2Swatches = new Border[5];
    private readonly StackPanel[] _color2Panels = new StackPanel[5];
    private readonly Slider[] _speedSliders = new Slider[5];
    private readonly TextBlock[] _speedLabels = new TextBlock[5];
    private readonly StackPanel[] _speedPanels = new StackPanel[5];
    private readonly ComboBox[] _reactiveModeComboBoxes = new ComboBox[5];
    private readonly StackPanel[] _reactiveModePanels = new StackPanel[5];


    // Track current colors in memory
    private readonly Color[] _colors1 = new Color[5];
    private readonly Color[] _colors2 = new Color[5];

    // Global lighting controls
    private CheckBox? _globalEnableCheck;
    private EffectPickerControl? _globalEffectPicker;
    private Border? _globalColor1Swatch;
    private Border? _globalColor2Swatch;
    private StackPanel? _globalColor2Panel;
    private Slider? _globalSpeedSlider;
    private TextBlock? _globalSpeedLabel;
    private StackPanel? _globalSpeedPanel;
    private ComboBox? _globalReactiveModeCombo;
    private StackPanel? _globalReactiveModePanel;
    private Color _globalColor1 = Color.FromRgb(0x00, 0xE6, 0x76);
    private Color _globalColor2 = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private StackPanel? _globalSettingsPanel;

    private static readonly LightEffect[] EffectsNeedingColor2 =
        { LightEffect.ColorBlend, LightEffect.Blink, LightEffect.Pulse, LightEffect.MicStatus, LightEffect.DeviceMute, LightEffect.AudioReactive, LightEffect.GradientFill, LightEffect.Fire };
    private static readonly LightEffect[] EffectsNeedingSpeed =
        { LightEffect.Blink, LightEffect.Pulse, LightEffect.RainbowWave, LightEffect.RainbowCycle, LightEffect.AudioReactive, LightEffect.Breathing, LightEffect.Comet, LightEffect.Sparkle };

    public LightsView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        BuildGlobalCard();
        BuildChannelControls();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // Populate global lighting card
        var gl = config.GlobalLight;
        if (_globalEnableCheck != null)
            _globalEnableCheck.IsChecked = gl.Enabled;
        if (_globalEffectPicker != null)
            _globalEffectPicker.SelectedEffect = gl.Effect;
        _globalColor1 = Color.FromRgb((byte)gl.R, (byte)gl.G, (byte)gl.B);
        _globalColor2 = Color.FromRgb((byte)gl.R2, (byte)gl.G2, (byte)gl.B2);
        if (_globalColor1Swatch != null)
            _globalColor1Swatch.Background = new SolidColorBrush(_globalColor1);
        if (_globalColor2Swatch != null)
            _globalColor2Swatch.Background = new SolidColorBrush(_globalColor2);
        if (_globalSpeedSlider != null)
            _globalSpeedSlider.Value = Math.Clamp(gl.EffectSpeed, 1, 100);
        if (_globalSpeedLabel != null)
            _globalSpeedLabel.Text = gl.EffectSpeed.ToString();
        if (_globalReactiveModeCombo != null)
            _globalReactiveModeCombo.SelectedItem = gl.ReactiveMode;

        UpdateGlobalVisibility();

        for (int i = 0; i < 5; i++)
        {
            // Update header from knob label
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob != null)
            {
                var name = !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : FormatTargetName(knob.Target);
                _headers[i].Text = name;
            }

            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            _effectPickers[i].SelectedEffect = light.Effect;

            _colors1[i] = Color.FromRgb((byte)light.R, (byte)light.G, (byte)light.B);
            _colors2[i] = Color.FromRgb((byte)light.R2, (byte)light.G2, (byte)light.B2);
            _color1Swatches[i].Background = new SolidColorBrush(_colors1[i]);
            _color2Swatches[i].Background = new SolidColorBrush(_colors2[i]);

            _speedSliders[i].Value = Math.Clamp(light.EffectSpeed, 1, 100);
            _speedLabels[i].Text = light.EffectSpeed.ToString();

            if (_reactiveModeComboBoxes[i] != null)
                _reactiveModeComboBoxes[i].SelectedItem = light.ReactiveMode;



            UpdateVisibility(i, light.Effect);
        }

        BrightnessSlider.Value = Math.Clamp(config.LedBrightness, 0, 100);
        BrightnessValueLabel.Text = $"{(int)BrightnessSlider.Value}%";

        BrightnessSlider.ValueChanged -= BrightnessSlider_ValueChanged;
        BrightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;

        _loading = false;
    }

    private void BuildGlobalCard()
    {
        var panel = GlobalLightCardPanel;

        // Header row: checkbox + title
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

        var enableCheck = new CheckBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var headerLabel = new TextBlock
        {
            Text = "GLOBAL LIGHTING",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(enableCheck);
        headerRow.Children.Add(headerLabel);
        panel.Children.Add(headerRow);

        _globalEnableCheck = enableCheck;
        enableCheck.Checked += (_, _) =>
        {
            UpdateGlobalVisibility();
            if (!_loading) QueueSave();
        };
        enableCheck.Unchecked += (_, _) =>
        {
            UpdateGlobalVisibility();
            if (!_loading) QueueSave();
        };

        // Settings panel (collapsed when disabled)
        var settings = new StackPanel { Visibility = Visibility.Collapsed };
        _globalSettingsPanel = settings;

        // Effect picker
        settings.Children.Add(MakeLabel("EFFECT"));
        var effectPicker = new EffectPickerControl { Margin = new Thickness(0, 0, 0, 10) };
        effectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateGlobalEffectVisibility(effectPicker.SelectedEffect);
            QueueSave();
        };
        _globalEffectPicker = effectPicker;
        settings.Children.Add(effectPicker);

        // Color 1
        settings.Children.Add(MakeLabel("COLOR"));
        var swatch1 = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(_globalColor1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        swatch1.MouseLeftButtonDown += (_, _) => OnPickGlobalColor(isColor2: false);
        _globalColor1Swatch = swatch1;
        settings.Children.Add(swatch1);

        // Color 2 (conditional)
        var color2Panel = new StackPanel { Visibility = Visibility.Collapsed };
        color2Panel.Children.Add(MakeLabel("COLOR 2"));
        var swatch2 = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(_globalColor2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        swatch2.MouseLeftButtonDown += (_, _) => OnPickGlobalColor(isColor2: true);
        _globalColor2Swatch = swatch2;
        color2Panel.Children.Add(swatch2);
        _globalColor2Panel = color2Panel;
        settings.Children.Add(color2Panel);

        // Speed slider (conditional)
        var speedPanel = new StackPanel { Visibility = Visibility.Collapsed };
        speedPanel.Children.Add(MakeLabel("SPEED"));
        var speedGrid = new Grid();
        speedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        speedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var speedSlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Value = 50,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(speedSlider, 0);

        var speedLabel = new TextBlock
        {
            Text = "50",
            Style = FindStyle("SecondaryText"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(speedLabel, 1);

        speedSlider.ValueChanged += (_, e) =>
        {
            speedLabel.Text = ((int)e.NewValue).ToString();
            if (!_loading) QueueSave();
        };
        _globalSpeedSlider = speedSlider;
        _globalSpeedLabel = speedLabel;

        speedGrid.Children.Add(speedSlider);
        speedGrid.Children.Add(speedLabel);
        speedPanel.Children.Add(speedGrid);
        speedPanel.Margin = new Thickness(0, 2, 0, 10);
        _globalSpeedPanel = speedPanel;
        settings.Children.Add(speedPanel);

        // Reactive mode (conditional)
        var reactiveModePanel = new StackPanel { Visibility = Visibility.Collapsed };
        reactiveModePanel.Children.Add(MakeLabel("REACTIVE MODE"));
        var modeCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<ReactiveMode>(),
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12
        };
        modeCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        _globalReactiveModeCombo = modeCombo;
        reactiveModePanel.Children.Add(modeCombo);
        _globalReactiveModePanel = reactiveModePanel;
        settings.Children.Add(reactiveModePanel);

        panel.Children.Add(settings);
    }

    private void UpdateGlobalVisibility()
    {
        bool enabled = _globalEnableCheck?.IsChecked ?? false;

        if (_globalSettingsPanel != null)
            _globalSettingsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide per-knob panels
        PerKnobGrid.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        // Update sub-controls based on current effect
        if (enabled && _globalEffectPicker != null)
            UpdateGlobalEffectVisibility(_globalEffectPicker.SelectedEffect);
    }

    private void UpdateGlobalEffectVisibility(LightEffect effect)
    {
        bool needsColor2 = EffectsNeedingColor2.Contains(effect);
        bool needsSpeed = EffectsNeedingSpeed.Contains(effect);
        bool isReactive = effect == LightEffect.AudioReactive;

        if (_globalColor2Panel != null)
            _globalColor2Panel.Visibility = needsColor2 ? Visibility.Visible : Visibility.Collapsed;
        if (_globalSpeedPanel != null)
            _globalSpeedPanel.Visibility = needsSpeed ? Visibility.Visible : Visibility.Collapsed;
        if (_globalReactiveModePanel != null)
            _globalReactiveModePanel.Visibility = isReactive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPickGlobalColor(bool isColor2)
    {
        var current = isColor2 ? _globalColor2 : _globalColor1;
        var dialog = new ColorPickerDialog(current)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var chosen = dialog.SelectedColor;
            if (isColor2)
            {
                _globalColor2 = chosen;
                if (_globalColor2Swatch != null)
                    _globalColor2Swatch.Background = new SolidColorBrush(chosen);
            }
            else
            {
                _globalColor1 = chosen;
                if (_globalColor1Swatch != null)
                    _globalColor1Swatch.Background = new SolidColorBrush(chosen);
            }
            QueueSave();
        }
    }

    private void BuildChannelControls()
    {
        var panels = new[] { Led0Panel, Led1Panel, Led2Panel, Led3Panel, Led4Panel };

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var panel = panels[i];

            // Header (updated from knob label in LoadConfig)
            var header = new TextBlock
            {
                Text = $"LED {i + 1}",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _headers[i] = header;
            panel.Children.Add(header);

            // Effect picker
            panel.Children.Add(MakeLabel("EFFECT"));
            var effectPicker = new EffectPickerControl { Margin = new Thickness(0, 0, 0, 10) };
            effectPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                UpdateVisibility(idx, effectPicker.SelectedEffect);
                QueueSave();
            };
            _effectPickers[i] = effectPicker;
            panel.Children.Add(effectPicker);

            // Color 1 — clickable swatch
            panel.Children.Add(MakeLabel("COLOR"));
            var swatch1 = MakeColorSwatch(idx, isColor2: false);
            _color1Swatches[i] = swatch1;
            panel.Children.Add(swatch1);

            // Color 2 — clickable swatch (conditionally visible)
            var color2Container = new StackPanel();
            color2Container.Children.Add(MakeLabel("COLOR 2"));
            var swatch2 = MakeColorSwatch(idx, isColor2: true);
            _color2Swatches[i] = swatch2;
            color2Container.Children.Add(swatch2);
            _color2Panels[i] = color2Container;
            panel.Children.Add(color2Container);

            // Speed slider (conditionally visible)
            var speedContainer = new StackPanel();
            speedContainer.Children.Add(MakeLabel("SPEED"));
            var speedGrid = new Grid();
            speedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            speedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var speedSlider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = 50,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(speedSlider, 0);

            var speedLabel = new TextBlock
            {
                Text = "50",
                Style = FindStyle("SecondaryText"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(speedLabel, 1);

            speedSlider.ValueChanged += (_, e) =>
            {
                speedLabel.Text = ((int)e.NewValue).ToString();
                if (!_loading) QueueSave();
            };
            _speedSliders[i] = speedSlider;
            _speedLabels[i] = speedLabel;

            speedGrid.Children.Add(speedSlider);
            speedGrid.Children.Add(speedLabel);
            speedContainer.Children.Add(speedGrid);
            speedContainer.Margin = new Thickness(0, 2, 0, 0);
            _speedPanels[i] = speedContainer;
            panel.Children.Add(speedContainer);

            // Reactive mode picker (only visible for AudioReactive)
            var reactiveContainer = new StackPanel();
            reactiveContainer.Children.Add(MakeLabel("REACTIVE MODE"));
            var modeCombo = new ComboBox
            {
                ItemsSource = Enum.GetValues<ReactiveMode>(),
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12
            };
            modeCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _reactiveModeComboBoxes[idx] = modeCombo;
            reactiveContainer.Children.Add(modeCombo);
            reactiveContainer.Visibility = Visibility.Collapsed;
            _reactiveModePanels[idx] = reactiveContainer;
            panel.Children.Add(reactiveContainer);

            // LinkToVolume removed — not needed
        }
    }

    private Border MakeColorSwatch(int idx, bool isColor2)
    {
        var swatch = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        swatch.MouseLeftButtonDown += (_, _) => OnPickColor(idx, isColor2);
        return swatch;
    }

    private void OnPickColor(int idx, bool isColor2)
    {
        var current = isColor2 ? _colors2[idx] : _colors1[idx];
        var dialog = new ColorPickerDialog(current)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var chosen = dialog.SelectedColor;
            if (isColor2)
            {
                _colors2[idx] = chosen;
                _color2Swatches[idx].Background = new SolidColorBrush(chosen);
            }
            else
            {
                _colors1[idx] = chosen;
                _color1Swatches[idx].Background = new SolidColorBrush(chosen);
            }
            QueueSave();
        }
    }

    private void UpdateVisibility(int idx, LightEffect effect)
    {
        bool needsColor2 = EffectsNeedingColor2.Contains(effect);
        bool needsSpeed = EffectsNeedingSpeed.Contains(effect);
        bool isReactive = effect == LightEffect.AudioReactive;

        _color2Panels[idx].Visibility = needsColor2 ? Visibility.Visible : Visibility.Collapsed;
        _speedPanels[idx].Visibility = needsSpeed ? Visibility.Visible : Visibility.Collapsed;
        _reactiveModePanels[idx].Visibility = isReactive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessValueLabel.Text = $"{(int)e.NewValue}%";
        if (!_loading) QueueSave();
    }

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        // Save global lighting config
        var gl = _config.GlobalLight;
        gl.Enabled = _globalEnableCheck?.IsChecked ?? false;
        if (_globalEffectPicker != null)
            gl.Effect = _globalEffectPicker.SelectedEffect;
        gl.R = _globalColor1.R;
        gl.G = _globalColor1.G;
        gl.B = _globalColor1.B;
        gl.R2 = _globalColor2.R;
        gl.G2 = _globalColor2.G;
        gl.B2 = _globalColor2.B;
        if (_globalSpeedSlider != null)
            gl.EffectSpeed = (int)_globalSpeedSlider.Value;
        if (_globalReactiveModeCombo?.SelectedItem is ReactiveMode glMode)
            gl.ReactiveMode = glMode;

        for (int i = 0; i < 5; i++)
        {
            var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            light.Effect = _effectPickers[i].SelectedEffect;

            light.R = _colors1[i].R;
            light.G = _colors1[i].G;
            light.B = _colors1[i].B;

            light.R2 = _colors2[i].R;
            light.G2 = _colors2[i].G;
            light.B2 = _colors2[i].B;

            light.EffectSpeed = (int)_speedSliders[i].Value;

            if (_reactiveModeComboBoxes[i]?.SelectedItem is ReactiveMode mode)
                light.ReactiveMode = mode;


        }

        _config.LedBrightness = (int)BrightnessSlider.Value;

        _onSave(_config);
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3)
        };
    }

    private Brush FindBrush(string key)
    {
        return (Brush)(FindResource(key) ?? Brushes.White);
    }

    private Style? FindStyle(string key)
    {
        return FindResource(key) as Style;
    }

    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none")
            return "None";
        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}

/// <summary>
/// Visual HSV color picker dialog with spectrum gradient, hue bar, hex input, and RGB sliders.
/// </summary>
public class ColorPickerDialog : Window
{
    public Color SelectedColor { get; private set; }

    private readonly Border _spectrumArea;
    private readonly WriteableBitmap _spectrumBitmap;
    private readonly Ellipse _spectrumCursor;
    private readonly Canvas _spectrumCanvas;
    private readonly Border _hueBar;
    private readonly WriteableBitmap _hueBitmap;
    private readonly Border _hueCursor;
    private readonly Canvas _hueCanvas;
    private readonly Border _preview;
    private readonly TextBox _hexInput;
    private readonly Slider _rSlider, _gSlider, _bSlider;
    private readonly TextBlock _rLabel, _gLabel, _bLabel;

    private float _hue; // 0-360
    private float _sat; // 0-1
    private float _val; // 0-1
    private bool _updating;

    private const int SpecW = 256;
    private const int SpecH = 256;
    private const int HueW = 256;
    private const int HueH = 20;

    public ColorPickerDialog(Color initial)
    {
        SelectedColor = initial;
        Title = "Pick Color";
        Width = 320;
        Height = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));

        // Convert initial color to HSV
        RgbToHsv(initial.R, initial.G, initial.B, out _hue, out _sat, out _val);

        var mainPanel = new StackPanel { Margin = new Thickness(16) };

        // --- Spectrum area (saturation X, value Y) ---
        _spectrumBitmap = new WriteableBitmap(SpecW, SpecH, 96, 96, PixelFormats.Bgra32, null);
        _spectrumCanvas = new Canvas { Width = SpecW, Height = SpecH, ClipToBounds = true };

        var spectrumImage = new Image
        {
            Source = _spectrumBitmap,
            Width = SpecW,
            Height = SpecH,
            Stretch = Stretch.None
        };
        _spectrumCanvas.Children.Add(spectrumImage);

        _spectrumCursor = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        _spectrumCanvas.Children.Add(_spectrumCursor);

        _spectrumArea = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Child = _spectrumCanvas,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _spectrumCanvas.MouseLeftButtonDown += Spectrum_MouseDown;
        _spectrumCanvas.MouseMove += Spectrum_MouseMove;
        _spectrumCanvas.MouseLeftButtonUp += Spectrum_MouseUp;
        mainPanel.Children.Add(_spectrumArea);

        // --- Hue bar ---
        _hueBitmap = new WriteableBitmap(HueW, HueH, 96, 96, PixelFormats.Bgra32, null);
        _hueCanvas = new Canvas { Width = HueW, Height = HueH, ClipToBounds = true };

        var hueImage = new Image
        {
            Source = _hueBitmap,
            Width = HueW,
            Height = HueH,
            Stretch = Stretch.None
        };
        _hueCanvas.Children.Add(hueImage);

        _hueCursor = new Border
        {
            Width = 6,
            Height = HueH + 4,
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        Canvas.SetTop(_hueCursor, -2);
        _hueCanvas.Children.Add(_hueCursor);

        var hueBar = new Border
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Child = _hueCanvas,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _hueBar = hueBar;
        _hueCanvas.MouseLeftButtonDown += Hue_MouseDown;
        _hueCanvas.MouseMove += Hue_MouseMove;
        _hueCanvas.MouseLeftButtonUp += Hue_MouseUp;
        mainPanel.Children.Add(hueBar);

        // --- Preview + hex row ---
        var previewRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        _preview = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(initial),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(_preview, 0);
        previewRow.Children.Add(_preview);

        _hexInput = new TextBox
        {
            Text = ColorToHex(initial),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 4, 6, 4)
        };
        _hexInput.LostFocus += HexInput_LostFocus;
        _hexInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) HexInput_LostFocus(null, null!); };
        Grid.SetColumn(_hexInput, 1);
        previewRow.Children.Add(_hexInput);
        mainPanel.Children.Add(previewRow);

        // --- Compact RGB sliders ---
        (_rSlider, _rLabel) = MakeChannelRow(mainPanel, "R", initial.R, Color.FromRgb(255, 80, 80));
        (_gSlider, _gLabel) = MakeChannelRow(mainPanel, "G", initial.G, Color.FromRgb(80, 255, 80));
        (_bSlider, _bLabel) = MakeChannelRow(mainPanel, "B", initial.B, Color.FromRgb(80, 130, 255));

        _rSlider.ValueChanged += (_, _) => { if (!_updating) OnRgbChanged(); };
        _gSlider.ValueChanged += (_, _) => { if (!_updating) OnRgbChanged(); };
        _bSlider.ValueChanged += (_, _) => { if (!_updating) OnRgbChanged(); };

        // --- OK / Cancel buttons ---
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xD8)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        okBtn.Click += (_, _) =>
        {
            SelectedColor = HsvToColor(_hue, _sat, _val);
            DialogResult = true;
            Close();
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        mainPanel.Children.Add(btnPanel);

        Content = mainPanel;

        // Render initial state
        Loaded += (_, _) =>
        {
            RenderHueBar();
            RenderSpectrum();
            UpdateCursors();
        };
    }

    // ── Spectrum interaction ──

    private bool _spectrumDragging;

    private void Spectrum_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _spectrumDragging = true;
        _spectrumCanvas.CaptureMouse();
        PickFromSpectrum(e.GetPosition(_spectrumCanvas));
    }

    private void Spectrum_MouseMove(object sender, MouseEventArgs e)
    {
        if (_spectrumDragging)
            PickFromSpectrum(e.GetPosition(_spectrumCanvas));
    }

    private void Spectrum_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _spectrumDragging = false;
        _spectrumCanvas.ReleaseMouseCapture();
    }

    private void PickFromSpectrum(Point p)
    {
        _sat = (float)Math.Clamp(p.X / SpecW, 0, 1);
        _val = 1f - (float)Math.Clamp(p.Y / SpecH, 0, 1);
        UpdateFromHsv();
    }

    // ── Hue bar interaction ──

    private bool _hueDragging;

    private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = true;
        _hueCanvas.CaptureMouse();
        PickFromHue(e.GetPosition(_hueCanvas));
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (_hueDragging)
            PickFromHue(e.GetPosition(_hueCanvas));
    }

    private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = false;
        _hueCanvas.ReleaseMouseCapture();
    }

    private void PickFromHue(Point p)
    {
        _hue = (float)Math.Clamp(p.X / HueW * 360.0, 0, 360);
        RenderSpectrum();
        UpdateFromHsv();
    }

    // ── Hex input ──

    private void HexInput_LostFocus(object? sender, RoutedEventArgs e)
    {
        var hex = _hexInput.Text.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint val))
        {
            byte r = (byte)((val >> 16) & 0xFF);
            byte g = (byte)((val >> 8) & 0xFF);
            byte b = (byte)(val & 0xFF);
            RgbToHsv(r, g, b, out _hue, out _sat, out _val);
            RenderSpectrum();
            UpdateFromHsv();
        }
    }

    // ── RGB slider changes ──

    private void OnRgbChanged()
    {
        byte r = (byte)_rSlider.Value;
        byte g = (byte)_gSlider.Value;
        byte b = (byte)_bSlider.Value;
        RgbToHsv(r, g, b, out _hue, out _sat, out _val);
        _updating = true;
        RenderSpectrum();
        UpdateCursors();
        UpdatePreviewAndHex();
        _updating = false;
    }

    // ── Update all UI from current HSV ──

    private void UpdateFromHsv()
    {
        _updating = true;
        var c = HsvToColor(_hue, _sat, _val);
        _rSlider.Value = c.R;
        _gSlider.Value = c.G;
        _bSlider.Value = c.B;
        _rLabel.Text = c.R.ToString();
        _gLabel.Text = c.G.ToString();
        _bLabel.Text = c.B.ToString();
        UpdateCursors();
        UpdatePreviewAndHex();
        _updating = false;
    }

    private void UpdateCursors()
    {
        // Spectrum cursor
        double sx = _sat * SpecW;
        double sy = (1.0 - _val) * SpecH;
        Canvas.SetLeft(_spectrumCursor, sx - 7);
        Canvas.SetTop(_spectrumCursor, sy - 7);

        // Hue cursor
        double hx = _hue / 360.0 * HueW;
        Canvas.SetLeft(_hueCursor, hx - 3);
    }

    private void UpdatePreviewAndHex()
    {
        var c = HsvToColor(_hue, _sat, _val);
        _preview.Background = new SolidColorBrush(c);
        _hexInput.Text = ColorToHex(c);
    }

    // ── Rendering ──

    private void RenderSpectrum()
    {
        var pixels = new byte[SpecW * SpecH * 4];
        for (int y = 0; y < SpecH; y++)
        {
            float v = 1f - (float)y / SpecH;
            for (int x = 0; x < SpecW; x++)
            {
                float s = (float)x / SpecW;
                var (r, g, b) = HsvToRgb(_hue, s, v);
                int offset = (y * SpecW + x) * 4;
                pixels[offset + 0] = b; // B
                pixels[offset + 1] = g; // G
                pixels[offset + 2] = r; // R
                pixels[offset + 3] = 255;
            }
        }
        _spectrumBitmap.WritePixels(new Int32Rect(0, 0, SpecW, SpecH), pixels, SpecW * 4, 0);
    }

    private void RenderHueBar()
    {
        var pixels = new byte[HueW * HueH * 4];
        for (int x = 0; x < HueW; x++)
        {
            float h = (float)x / HueW * 360f;
            var (r, g, b) = HsvToRgb(h, 1f, 1f);
            for (int y = 0; y < HueH; y++)
            {
                int offset = (y * HueW + x) * 4;
                pixels[offset + 0] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }
        _hueBitmap.WritePixels(new Int32Rect(0, 0, HueW, HueH), pixels, HueW * 4, 0);
    }

    // ── Helpers ──

    private (Slider slider, TextBlock label) MakeChannelRow(StackPanel parent, string name, byte value, Color tint)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        var lbl = new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush(tint),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = value,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        var valLabel = new TextBlock
        {
            Text = value.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valLabel, 2);
        row.Children.Add(valLabel);

        slider.ValueChanged += (_, e) => valLabel.Text = ((int)e.NewValue).ToString();

        parent.Children.Add(row);
        return (slider, valLabel);
    }

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        v = max;
        s = max > 0 ? delta / max : 0;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == rf)
        {
            h = 60f * (((gf - bf) / delta) % 6f);
        }
        else if (max == gf)
        {
            h = 60f * (((bf - rf) / delta) + 2f);
        }
        else
        {
            h = 60f * (((rf - gf) / delta) + 4f);
        }

        if (h < 0) h += 360f;
    }

    private static (byte r, byte g, byte b) HsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180f) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return (
            (byte)((r1 + m) * 255f + 0.5f),
            (byte)((g1 + m) * 255f + 0.5f),
            (byte)((b1 + m) * 255f + 0.5f)
        );
    }

    private static Color HsvToColor(float h, float s, float v)
    {
        var (r, g, b) = HsvToRgb(h, s, v);
        return Color.FromRgb(r, g, b);
    }
}
