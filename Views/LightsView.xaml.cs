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

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();
    private readonly List<TextBlock> _subLabels = new();

    // Effect icons
    private static readonly Dictionary<LightEffect, string> EffectIcons = new()
    {
        { LightEffect.SingleColor, "\U0001F7E2" },
        { LightEffect.ColorBlend, "\U0001F308" },
        { LightEffect.PositionFill, "\U0001F4CA" },
        { LightEffect.Blink, "\u2728" },
        { LightEffect.Pulse, "\U0001F4AB" },
        { LightEffect.RainbowWave, "\U0001F308" },
        { LightEffect.RainbowCycle, "\U0001F3A8" },
        { LightEffect.MicStatus, "\U0001F3A4" },
        { LightEffect.DeviceMute, "\U0001F507" },
        { LightEffect.AudioReactive, "\U0001F3B5" },
        { LightEffect.Breathing, "\U0001F4A8" },
        { LightEffect.GradientFill, "\U0001F30C" },
        { LightEffect.Comet, "\u2604" },
        { LightEffect.Sparkle, "\u2728" },
        { LightEffect.Fire, "\U0001F525" },
    };

    // Per-channel controls
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly TextBlock[] _headerIcons = new TextBlock[5];
    private readonly TextBlock[] _headerEffects = new TextBlock[5];
    private readonly EffectPickerControl[] _effectPickers = new EffectPickerControl[5];
    private readonly Border[] _color1Swatches = new Border[5];
    private readonly Border[] _color2Swatches = new Border[5];
    private readonly StackPanel[] _color2Panels = new StackPanel[5];
    private readonly StyledSlider[] _speedSliders = new StyledSlider[5];
    private readonly StackPanel[] _speedPanels = new StackPanel[5];
    private readonly ComboBox[] _reactiveModeComboBoxes = new ComboBox[5];
    private readonly StackPanel[] _reactiveModePanels = new StackPanel[5];
    private readonly TextBox[] _programNameBoxes = new TextBox[5];
    private readonly StackPanel[] _programNamePanels = new StackPanel[5];


    // Track current colors in memory
    private readonly Color[] _colors1 = new Color[5];
    private readonly Color[] _colors2 = new Color[5];

    // Global lighting controls
    private CheckBox? _globalEnableCheck;
    private EffectPickerControl? _globalEffectPicker;
    private Border? _globalColor1Swatch;
    private Border? _globalColor2Swatch;
    private StackPanel? _globalColor2Panel;
    private StyledSlider? _globalSpeedSlider;
    private StackPanel? _globalSpeedPanel;
    private ComboBox? _globalReactiveModeCombo;
    private StackPanel? _globalReactiveModePanel;
    private Color _globalColor1 = ThemeManager.Accent;
    private Color _globalColor2 = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private StackPanel? _globalSettingsPanel;
    private StackPanel? _globalPalettePanel;
    private StyledSlider? _brightnessSlider;

    private static readonly (string Name, Color Primary, Color Secondary)[] ColorPalettes = new[]
    {
        ("Sunset",    Color.FromRgb(0xFF, 0x6B, 0x35), Color.FromRgb(0xFF, 0xD7, 0x00)),
        ("Ocean",     Color.FromRgb(0x00, 0x77, 0xB6), Color.FromRgb(0x00, 0xE5, 0xFF)),
        ("Neon",      Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0x00, 0xFF, 0xFF)),
        ("Forest",    Color.FromRgb(0x00, 0xC8, 0x53), Color.FromRgb(0xAE, 0xD5, 0x81)),
        ("Lava",      Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x8A, 0x00)),
        ("Arctic",    Color.FromRgb(0xE0, 0xF7, 0xFA), Color.FromRgb(0x00, 0x97, 0xA7)),
        ("Galaxy",    Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0xFF, 0x80, 0xAB)),
        ("Toxic",     Color.FromRgb(0x76, 0xFF, 0x03), Color.FromRgb(0x00, 0xE6, 0x76)),
        ("Inferno",   Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0xD6, 0x00)),
        ("Vaporwave", Color.FromRgb(0xFF, 0x71, 0xCE), Color.FromRgb(0x01, 0xCD, 0xFE)),
        ("Ember",     Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0x8B, 0x00, 0x00)),
        ("Aurora",    Color.FromRgb(0x00, 0xFF, 0x87), Color.FromRgb(0x7B, 0x2F, 0xFF)),
    };

    // Clipboard for light copy/paste
    private static LightConfig? _lightClipboard;

    // Per-channel border references (for context menu attachment)
    private readonly Border[] _ledBorders = new Border[5];

    // DeviceSelect per-knob controls (3 rows each)
    private readonly StackPanel[] _deviceSelectPanels = new StackPanel[5];
    private readonly ListPicker[][] _dsDevicePickers = new ListPicker[5][];
    private readonly Border[][] _dsColorBtns = new Border[5][];
    private readonly Color[][] _dsColors = new Color[5][];

    // Audio devices cache (populated from AudioMixer)
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    private static readonly LightEffect[] EffectsNeedingColor2 =
        { LightEffect.ColorBlend, LightEffect.Blink, LightEffect.Pulse, LightEffect.MicStatus, LightEffect.DeviceMute, LightEffect.AudioReactive, LightEffect.GradientFill, LightEffect.Fire, LightEffect.PingPong, LightEffect.Candle, LightEffect.Scanner, LightEffect.ColorWave, LightEffect.Segments, LightEffect.PositionBlend, LightEffect.ProgramMute };
    private static readonly LightEffect[] EffectsNeedingSpeed =
        { LightEffect.Blink, LightEffect.Pulse, LightEffect.RainbowWave, LightEffect.RainbowCycle, LightEffect.AudioReactive, LightEffect.Breathing, LightEffect.Comet, LightEffect.Sparkle, LightEffect.PingPong, LightEffect.Stack, LightEffect.Wave, LightEffect.Candle, LightEffect.Scanner, LightEffect.MeteorRain, LightEffect.ColorWave, LightEffect.Segments, LightEffect.Wheel, LightEffect.RainbowWheel };
    private static readonly LightEffect[] EffectsNeedingProgramName =
        { LightEffect.ProgramMute };
    private static readonly LightEffect[] EffectsNeedingDeviceSelect =
        { LightEffect.DeviceSelect };

    public LightsView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildGlobalCard();
        BuildChannelControls();
        SetupStripContextMenus();

        // Brightness slider — created here, added to global settings panel in BuildGlobalCard
        _brightnessSlider = new StyledSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Suffix = "%",
            AccentColor = ThemeManager.Accent,
            ToolTip = "Global LED brightness (0 = off, 100 = full brightness)",
        };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            if (!_loading) QueueSave();
        };
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave, AudioMixer? mixer = null)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        if (mixer != null)
            _audioDevices = mixer.GetAudioDevices();

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
            UpdateHeaderEffect(i);

            _colors1[i] = Color.FromRgb((byte)light.R, (byte)light.G, (byte)light.B);
            _colors2[i] = Color.FromRgb((byte)light.R2, (byte)light.G2, (byte)light.B2);
            _color1Swatches[i].Background = new SolidColorBrush(_colors1[i]);
            _color2Swatches[i].Background = new SolidColorBrush(_colors2[i]);

            _speedSliders[i].Value = Math.Clamp(light.EffectSpeed, 1, 100);

            if (_reactiveModeComboBoxes[i] != null)
                _reactiveModeComboBoxes[i].SelectedItem = light.ReactiveMode;

            if (_programNameBoxes[i] != null)
                _programNameBoxes[i].Text = light.ProgramName ?? "";

            // Populate DeviceSelect device pickers
            PopulateDeviceSelectPickers(i);
            LoadDeviceSelectColors(i, light);

            UpdateVisibility(i, light.Effect);
        }

        if (_brightnessSlider != null)
            _brightnessSlider.Value = Math.Clamp(config.LedBrightness, 0, 100);

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
            ToolTip = "Apply one effect to all 5 knobs simultaneously",
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
        settings.Children.Add(MakeSectionHeader("EFFECT"));
        var effectPicker = new EffectPickerControl(showGlobal: true)
        {
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Choose the LED lighting effect",
        };
        effectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateGlobalEffectVisibility(effectPicker.SelectedEffect);
            QueueSave();
        };
        _globalEffectPicker = effectPicker;
        settings.Children.Add(effectPicker);

        // Color 1 + Color 2 in a horizontal row
        settings.Children.Add(MakeSectionHeader("COLOR"));
        var swatch1 = MakeGlobalColorSwatch(_globalColor1, isColor2: false);
        _globalColor1Swatch = swatch1;
        var swatch2 = MakeGlobalColorSwatch(_globalColor2, isColor2: true);
        _globalColor2Swatch = swatch2;

        var color2Panel = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        color2Panel.Children.Add(MakeSubLabel("SECONDARY"));
        color2Panel.Children.Add(swatch2);
        _globalColor2Panel = color2Panel;

        var globalColorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        globalColorRow.Children.Add(MakeSubLabel("PRIMARY"));
        globalColorRow.Children.Add(swatch1);
        globalColorRow.Children.Add(color2Panel);
        settings.Children.Add(globalColorRow);

        // Palette presets (shown only when color2 is visible)
        var paletteSection = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        _globalPalettePanel = paletteSection;

        paletteSection.Children.Add(new TextBlock
        {
            Text = "PALETTE",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        var paletteWrap = new WrapPanel();
        foreach (var (name, primary, secondary) in ColorPalettes)
        {
            var p1 = primary;
            var p2 = secondary;

            // Tile: 28x28 with diagonal split showing both colors
            var tileCanvas = new Canvas { Width = 28, Height = 28, ClipToBounds = true };
            var leftRect = new System.Windows.Shapes.Rectangle { Width = 28, Height = 28, Fill = new SolidColorBrush(p1) };
            Canvas.SetLeft(leftRect, 0);
            tileCanvas.Children.Add(leftRect);
            var poly = new System.Windows.Shapes.Polygon
            {
                Fill = new SolidColorBrush(p2),
                Points = new PointCollection(new[] { new Point(28, 0), new Point(28, 28), new Point(0, 28) }),
            };
            tileCanvas.Children.Add(poly);

            var tileBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                Child = tileCanvas,
                ToolTip = name,
            };
            tileBorder.MouseEnter += (_, _) => tileBorder.BorderBrush = new SolidColorBrush(Colors.White);
            tileBorder.MouseLeave += (_, _) => tileBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
            tileBorder.MouseLeftButtonDown += (_, _) =>
            {
                _globalColor1 = p1;
                _globalColor2 = p2;
                if (_globalColor1Swatch != null)
                    _globalColor1Swatch.Background = new SolidColorBrush(p1);
                if (_globalColor2Swatch != null)
                    _globalColor2Swatch.Background = new SolidColorBrush(p2);
                if (!_loading) QueueSave();
            };
            paletteWrap.Children.Add(tileBorder);
        }
        paletteSection.Children.Add(paletteWrap);
        settings.Children.Add(paletteSection);

        // Speed slider (conditional)
        var speedPanel = new StackPanel { Visibility = Visibility.Collapsed };
        speedPanel.Children.Add(MakeSectionHeader("SPEED"));

        var speedSlider = new StyledSlider
        {
            Minimum = 1,
            Maximum = 100,
            Value = 50,
            Suffix = "",
            AccentColor = ThemeManager.Accent,
            ToolTip = "Animation speed — higher = faster",
        };
        speedSlider.ValueChanged += (_, _) =>
        {
            if (!_loading) QueueSave();
        };
        _globalSpeedSlider = speedSlider;

        speedPanel.Children.Add(speedSlider);
        speedPanel.Margin = new Thickness(0, 2, 0, 10);
        _globalSpeedPanel = speedPanel;
        settings.Children.Add(speedPanel);

        // Brightness (always visible when global is enabled)
        var brightnessPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
        brightnessPanel.Children.Add(MakeSectionHeader("BRIGHTNESS"));
        brightnessPanel.Children.Add(_brightnessSlider);
        settings.Children.Add(brightnessPanel);

        // Reactive mode (conditional)
        var reactiveModePanel = new StackPanel { Visibility = Visibility.Collapsed };
        reactiveModePanel.Children.Add(MakeLabel("REACTIVE MODE"));
        var modeCombo = new ComboBox
        {
            Style = FindStyle("HoverComboBox"),
            ItemsSource = Enum.GetValues<ReactiveMode>(),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            ToolTip = "BeatPulse: bass drives all LEDs. SpectrumBands: per-knob frequency. ColorShift: hue by energy",
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
        if (_globalPalettePanel != null)
            _globalPalettePanel.Visibility = needsColor2 ? Visibility.Visible : Visibility.Collapsed;
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

            // ── Header: label + icon + effect name ──
            var headerStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var header = new TextBlock
            {
                Text = $"LED {i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            _headers[i] = header;
            headerStack.Children.Add(header);

            var headerIcon = new TextBlock
            {
                Text = "\U0001F7E2",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("AccentBrush"),
            };
            _headerIcons[i] = headerIcon;
            headerStack.Children.Add(headerIcon);

            var headerEffect = new TextBlock
            {
                Text = "Single Color",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("AccentBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            _headerEffects[i] = headerEffect;
            headerStack.Children.Add(headerEffect);

            panel.Children.Add(headerStack);

            // ── Separator ──
            panel.Children.Add(MakeSeparator(12));

            // ── EFFECT section ──
            panel.Children.Add(MakeSectionHeader("EFFECT"));
            var effectPicker = new EffectPickerControl
            {
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "Choose the LED lighting effect for this knob",
            };
            effectPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                UpdateVisibility(idx, effectPicker.SelectedEffect);
                UpdateHeaderEffect(idx);
                QueueSave();
            };
            _effectPickers[i] = effectPicker;
            panel.Children.Add(effectPicker);

            // ── Separator ──
            panel.Children.Add(MakeSeparator(10));

            // ── COLOR section ──
            panel.Children.Add(MakeSectionHeader("COLOR"));

            // Horizontal row: PRIMARY [circle] SECONDARY [circle]
            var swatch1 = MakeColorSwatch(idx, isColor2: false);
            _color1Swatches[i] = swatch1;
            var swatch2 = MakeColorSwatch(idx, isColor2: true);
            _color2Swatches[i] = swatch2;

            var colorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            colorRow.Children.Add(MakeSubLabel("PRIMARY"));
            colorRow.Children.Add(swatch1);

            var color2Container = new StackPanel { Orientation = Orientation.Horizontal };
            color2Container.Children.Add(MakeSubLabel("SECONDARY"));
            color2Container.Children.Add(swatch2);

            colorRow.Children.Add(color2Container);
            _color2Panels[i] = color2Container;
            panel.Children.Add(colorRow);

            // ── SPEED section (conditionally visible — separator included) ──
            var speedContainer = new StackPanel();
            speedContainer.Children.Add(MakeSeparator(10));
            speedContainer.Children.Add(MakeSectionHeader("SPEED"));

            var speedSlider = new StyledSlider
            {
                Minimum = 1,
                Maximum = 100,
                Value = 50,
                Suffix = "",
                AccentColor = ThemeManager.Accent,
                ToolTip = "Animation speed — higher = faster",
            };
            speedSlider.ValueChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _speedSliders[i] = speedSlider;

            speedContainer.Children.Add(speedSlider);
            speedContainer.Margin = new Thickness(0, 0, 0, 0);
            _speedPanels[i] = speedContainer;
            panel.Children.Add(speedContainer);

            // Reactive mode picker (only visible for AudioReactive)
            var reactiveContainer = new StackPanel();
            reactiveContainer.Children.Add(MakeLabel("REACTIVE MODE"));
            var modeCombo = new ComboBox
            {
                Style = FindStyle("HoverComboBox"),
                ItemsSource = Enum.GetValues<ReactiveMode>(),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12,
                ToolTip = "BeatPulse: bass drives all LEDs. SpectrumBands: per-knob frequency. ColorShift: hue by energy",
            };
            modeCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _reactiveModeComboBoxes[idx] = modeCombo;
            reactiveContainer.Children.Add(modeCombo);
            reactiveContainer.Visibility = Visibility.Collapsed;
            _reactiveModePanels[idx] = reactiveContainer;
            panel.Children.Add(reactiveContainer);

            // Program name box (only visible for ProgramMute)
            var programNameContainer = new StackPanel();
            programNameContainer.Children.Add(MakeLabel("PROGRAM NAME"));
            var programNameBox = new TextBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Process name to monitor for mute state (e.g. spotify)",
            };
            programNameBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _programNameBoxes[idx] = programNameBox;
            programNameContainer.Children.Add(programNameBox);
            programNameContainer.Visibility = Visibility.Collapsed;
            _programNamePanels[idx] = programNameContainer;
            panel.Children.Add(programNameContainer);

            // DeviceSelect rows (hidden unless DeviceSelect effect)
            var deviceSelectContainer = new StackPanel { Visibility = Visibility.Collapsed };
            deviceSelectContainer.Children.Add(MakeLabel("DEVICE COLORS"));
            deviceSelectContainer.ToolTip = "Set LED color for each audio output device";

            _dsDevicePickers[idx] = new ListPicker[3];
            _dsColorBtns[idx] = new Border[3];
            _dsColors[idx] = new Color[3];
            for (int row = 0; row < 3; row++)
            {
                int rowCapture = row;

                // Initialize default colors per row (blue, green, orange)
                _dsColors[idx][row] = row switch
                {
                    0 => Color.FromRgb(0x00, 0x96, 0xFF),
                    1 => Color.FromRgb(0x00, 0xE6, 0x76),
                    _ => Color.FromRgb(0xFF, 0x87, 0x22),
                };

                var rowPanel = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var devicePicker = new ListPicker
                {
                    Margin = new Thickness(0, 0, 4, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                devicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
                _dsDevicePickers[idx][row] = devicePicker;
                Grid.SetColumn(devicePicker, 0);
                rowPanel.Children.Add(devicePicker);

                var colorBtn = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(_dsColors[idx][row]),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                colorBtn.MouseLeftButtonDown += (_, _) => OnPickDeviceSelectColor(idx, rowCapture);
                _dsColorBtns[idx][row] = colorBtn;
                Grid.SetColumn(colorBtn, 1);
                rowPanel.Children.Add(colorBtn);

                deviceSelectContainer.Children.Add(rowPanel);
            }

            _deviceSelectPanels[idx] = deviceSelectContainer;
            panel.Children.Add(deviceSelectContainer);
        }
    }

    private void UpdateHeaderEffect(int idx)
    {
        var effect = _effectPickers[idx].SelectedEffect;
        var icon = EffectIcons.GetValueOrDefault(effect, "\U0001F7E2");
        var name = effect.ToString();
        // Add spaces before capitals for display
        var display = System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");

        _headerIcons[idx].Text = icon;
        _headerEffects[idx].Text = display;

        // Color icon + label to match the effect's tile color
        var effectColor = EffectPickerControl.EffectColors.GetValueOrDefault(effect, ThemeManager.Accent);
        _headerIcons[idx].Foreground = new SolidColorBrush(effectColor);
        _headerEffects[idx].Foreground = new SolidColorBrush(effectColor);
    }

    private Border MakeColorSwatch(int idx, bool isColor2)
    {
        var normalBorder = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        var swatch = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = isColor2 ? "Secondary color (accent/contrast for animated effects)" : "Primary LED color",
        };

        // Hover glow effect
        swatch.MouseEnter += (_, _) =>
        {
            swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            swatch.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ((SolidColorBrush)swatch.Background).Color,
                BlurRadius = 10,
                Opacity = 0.4,
                ShadowDepth = 0,
            };
        };
        swatch.MouseLeave += (_, _) =>
        {
            swatch.BorderBrush = normalBorder;
            swatch.Effect = null;
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

    private void PopulateDeviceSelectPickers(int idx)
    {
        if (_dsDevicePickers[idx] == null) return;
        for (int row = 0; row < 3; row++)
        {
            var picker = _dsDevicePickers[idx][row];
            picker.ClearItems();
            picker.AddItem("(none)", "");
            foreach (var (id, name, isOutput) in _audioDevices)
            {
                if (isOutput)
                    picker.AddItem(name, id);
            }
        }
    }

    private void LoadDeviceSelectColors(int idx, LightConfig light)
    {
        if (_dsDevicePickers[idx] == null || light.DeviceColors == null) return;
        for (int row = 0; row < 3 && row < light.DeviceColors.Count; row++)
        {
            var entry = light.DeviceColors[row];
            // Select device in picker
            for (int p = 0; p < _dsDevicePickers[idx][row].ItemCount; p++)
            {
                if (_dsDevicePickers[idx][row].GetTagAt(p) as string == entry.DeviceId)
                {
                    _dsDevicePickers[idx][row].SelectedIndex = p;
                    break;
                }
            }
            // Set color
            _dsColors[idx][row] = Color.FromRgb((byte)Math.Clamp(entry.R, 0, 255),
                                                 (byte)Math.Clamp(entry.G, 0, 255),
                                                 (byte)Math.Clamp(entry.B, 0, 255));
            if (_dsColorBtns[idx][row] != null)
                _dsColorBtns[idx][row].Background = new SolidColorBrush(_dsColors[idx][row]);
        }
    }

    private void OnPickDeviceSelectColor(int idx, int row)
    {
        var current = _dsColors[idx][row];
        var dialog = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            _dsColors[idx][row] = dialog.SelectedColor;
            if (_dsColorBtns[idx][row] != null)
                _dsColorBtns[idx][row].Background = new SolidColorBrush(dialog.SelectedColor);
            QueueSave();
        }
    }

    private void UpdateVisibility(int idx, LightEffect effect)
    {
        bool needsColor2 = EffectsNeedingColor2.Contains(effect);
        bool needsSpeed = EffectsNeedingSpeed.Contains(effect);
        bool isReactive = effect == LightEffect.AudioReactive;
        bool needsProgramName = EffectsNeedingProgramName.Contains(effect);
        bool needsDeviceSelect = effect == LightEffect.DeviceSelect;

        _color2Panels[idx].Visibility = needsColor2 ? Visibility.Visible : Visibility.Collapsed;
        _speedPanels[idx].Visibility = needsSpeed ? Visibility.Visible : Visibility.Collapsed;
        _reactiveModePanels[idx].Visibility = isReactive ? Visibility.Visible : Visibility.Collapsed;
        _programNamePanels[idx].Visibility = needsProgramName ? Visibility.Visible : Visibility.Collapsed;

        if (_deviceSelectPanels[idx] != null)
        {
            _deviceSelectPanels[idx].Visibility = needsDeviceSelect ? Visibility.Visible : Visibility.Collapsed;
            if (needsDeviceSelect)
                PopulateDeviceSelectPickers(idx);
        }
    }

    private void SetupStripContextMenus()
    {
        // Get the border parents of each LED panel
        var panels = new StackPanel[] { Led0Panel, Led1Panel, Led2Panel, Led3Panel, Led4Panel };
        for (int i = 0; i < 5; i++)
            _ledBorders[i] = panels[i].Parent as Border ?? new Border();

        var menuBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        var menuFg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var menuBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var border = _ledBorders[i];

            var copyItem = new MenuItem
            {
                Header = "Copy Channel",
                Foreground = menuFg,
                Background = menuBg,
            };
            var pasteItem = new MenuItem
            {
                Header = "Paste Channel",
                Foreground = menuFg,
                Background = menuBg,
            };
            var resetItem = new MenuItem
            {
                Header = "Reset to Default",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                Background = menuBg,
            };

            copyItem.Click += (_, _) =>
            {
                if (_config == null) return;
                var light = _config.Lights.FirstOrDefault(l => l.Idx == idx);
                if (light == null) return;
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(light);
                _lightClipboard = Newtonsoft.Json.JsonConvert.DeserializeObject<LightConfig>(json);
            };

            pasteItem.Click += (_, _) =>
            {
                if (_lightClipboard == null || _config == null || _onSave == null) return;
                var light = _config.Lights.FirstOrDefault(l => l.Idx == idx);
                if (light == null) return;

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_lightClipboard);
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<LightConfig>(json)!;
                copy.Idx = idx;

                // Apply all fields from copy
                light.Effect = copy.Effect;
                light.R = copy.R; light.G = copy.G; light.B = copy.B;
                light.R2 = copy.R2; light.G2 = copy.G2; light.B2 = copy.B2;
                light.EffectSpeed = copy.EffectSpeed;
                light.ReactiveMode = copy.ReactiveMode;
                light.ProgramName = copy.ProgramName;
                light.DeviceColors = copy.DeviceColors != null
                    ? new List<DeviceColorEntry>(copy.DeviceColors.Select(dc => new DeviceColorEntry { DeviceId = dc.DeviceId, R = dc.R, G = dc.G, B = dc.B }))
                    : new List<DeviceColorEntry>();

                _loading = true;
                _effectPickers[idx].SelectedEffect = light.Effect;
                UpdateHeaderEffect(idx);
                _colors1[idx] = Color.FromRgb((byte)light.R, (byte)light.G, (byte)light.B);
                _colors2[idx] = Color.FromRgb((byte)light.R2, (byte)light.G2, (byte)light.B2);
                _color1Swatches[idx].Background = new SolidColorBrush(_colors1[idx]);
                _color2Swatches[idx].Background = new SolidColorBrush(_colors2[idx]);
                _speedSliders[idx].Value = Math.Clamp(light.EffectSpeed, 1, 100);
                if (_reactiveModeComboBoxes[idx] != null)
                    _reactiveModeComboBoxes[idx].SelectedItem = light.ReactiveMode;
                if (_programNameBoxes[idx] != null)
                    _programNameBoxes[idx].Text = light.ProgramName ?? "";
                PopulateDeviceSelectPickers(idx);
                LoadDeviceSelectColors(idx, light);
                UpdateVisibility(idx, light.Effect);
                _loading = false;

                QueueSave();
            };

            resetItem.Click += (_, _) =>
            {
                if (_config == null || _onSave == null) return;
                var light = _config.Lights.FirstOrDefault(l => l.Idx == idx);
                if (light == null) return;

                light.Effect = LightEffect.SingleColor;
                light.R = 0; light.G = 230; light.B = 118;
                light.R2 = 0; light.G2 = 0; light.B2 = 0;
                light.EffectSpeed = 50;
                light.ProgramName = "";
                light.DeviceColors = new List<DeviceColorEntry>();

                _loading = true;
                _effectPickers[idx].SelectedEffect = LightEffect.SingleColor;
                UpdateHeaderEffect(idx);
                _colors1[idx] = Color.FromRgb(0, 230, 118);
                _colors2[idx] = Color.FromRgb(0, 0, 0);
                _color1Swatches[idx].Background = new SolidColorBrush(_colors1[idx]);
                _color2Swatches[idx].Background = new SolidColorBrush(_colors2[idx]);
                _speedSliders[idx].Value = 50;
                if (_programNameBoxes[idx] != null)
                    _programNameBoxes[idx].Text = "";
                UpdateVisibility(idx, LightEffect.SingleColor);
                _loading = false;

                QueueSave();
            };

            var separator = new Separator
            {
                Background = menuBorder,
                Foreground = menuBorder,
                Margin = new Thickness(4, 2, 4, 2),
            };

            var contextMenu = new ContextMenu
            {
                Background = menuBg,
                BorderBrush = menuBorder,
                BorderThickness = new Thickness(1),
            };

            contextMenu.ContextMenuOpening += (_, _) =>
            {
                pasteItem.IsEnabled = _lightClipboard != null;
                pasteItem.Opacity = _lightClipboard != null ? 1.0 : 0.4;
            };

            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(pasteItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(resetItem);

            border.ContextMenu = contextMenu;
        }
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

            if (_programNameBoxes[i] != null)
                light.ProgramName = _programNameBoxes[i].Text.Trim();

            // Save DeviceSelect mappings
            if (_dsDevicePickers[i] != null)
            {
                light.DeviceColors = new List<DeviceColorEntry>();
                for (int row = 0; row < 3; row++)
                {
                    var deviceId = _dsDevicePickers[i][row].SelectedTag as string ?? "";
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        light.DeviceColors.Add(new DeviceColorEntry
                        {
                            DeviceId = deviceId,
                            R = _dsColors[i][row].R,
                            G = _dsColors[i][row].G,
                            B = _dsColors[i][row].B,
                        });
                    }
                }
            }
        }

        if (_brightnessSlider != null)
            _config.LedBrightness = (int)_brightnessSlider.Value;

        _onSave(_config);
    }

    private Grid MakeSectionHeader(string title)
    {
        var accent = ThemeManager.Accent;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        _sectionHeaders.Add((bar, label));
        return grid;
    }

    private Border MakeSeparator(int spacing = 10)
    {
        return new Border
        {
            Height = 1,
            Background = FindBrush("CardBorderBrush"),
            Margin = new Thickness(0, spacing, 0, spacing),
        };
    }

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            bar.Background = new SolidColorBrush(accent);
            label.Foreground = new SolidColorBrush(accent);
        }
        foreach (var lbl in _subLabels)
            lbl.Foreground = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B));

        for (int i = 0; i < 5; i++)
        {
            _effectPickers[i].AccentColor = accent;
            _speedSliders[i].AccentColor = accent;
        }
        if (_globalEffectPicker != null)
            _globalEffectPicker.AccentColor = accent;
        if (_globalSpeedSlider != null)
            _globalSpeedSlider.AccentColor = accent;
        if (_brightnessSlider != null)
            _brightnessSlider.AccentColor = accent;
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 4, 0, 3)
        };
    }

    private TextBlock MakeSubLabel(string text)
    {
        var accent = ThemeManager.Accent;
        var lbl = new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B)),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _subLabels.Add(lbl);
        return lbl;
    }

    private Border MakeGlobalColorSwatch(Color initial, bool isColor2)
    {
        var normalBorder = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        var swatch = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(initial),
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = isColor2 ? "Secondary color (accent/contrast for animated effects)" : "Primary LED color",
        };
        swatch.MouseEnter += (_, _) =>
        {
            swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            swatch.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ((SolidColorBrush)swatch.Background).Color,
                BlurRadius = 10,
                Opacity = 0.4,
                ShadowDepth = 0,
            };
        };
        swatch.MouseLeave += (_, _) =>
        {
            swatch.BorderBrush = normalBorder;
            swatch.Effect = null;
        };
        swatch.MouseLeftButtonDown += (_, _) => OnPickGlobalColor(isColor2);
        return swatch;
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
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        // Convert initial color to HSV
        RgbToHsv(initial.R, initial.G, initial.B, out _hue, out _sat, out _val);

        // Outer border for the dialog (rounded, dark, with accent glow)
        var outerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                Opacity = 0.6,
                ShadowDepth = 0,
            },
        };
        outerBorder.MouseLeftButtonDown += (_, _) => DragMove();

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

        // Quick preset colors
        mainPanel.Children.Add(new TextBlock
        {
            Text = "QUICK PICK",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 0, 6),
        });
        var presetPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var presets = new[] {
            "#FF0000", "#FF4500", "#FF8C00", "#FFD700", "#FFFF00",
            "#7FFF00", "#00FF00", "#00E676", "#00CED1", "#00BFFF",
            "#0080FF", "#4040FF", "#8000FF", "#FF00FF", "#FF1493",
            "#FFFFFF", "#C0C0C0", "#808080",
        };
        foreach (var hex in presets)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
                BorderThickness = new Thickness(1),
            };
            dot.MouseEnter += (_, _) => dot.BorderBrush = new SolidColorBrush(Colors.White);
            dot.MouseLeave += (_, _) => dot.BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
            var capturedColor = c;
            dot.MouseLeftButtonDown += (_, _) =>
            {
                RgbToHsv(capturedColor.R, capturedColor.G, capturedColor.B, out _hue, out _sat, out _val);
                RenderSpectrum();
                UpdateFromHsv();
            };
            presetPanel.Children.Add(dot);
        }
        mainPanel.Children.Add(presetPanel);

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

        outerBorder.Child = mainPanel;
        Content = outerBorder;

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
