using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WolfMixer.Views;

public partial class LightsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Per-channel controls
    private readonly ComboBox[] _effectCombos = new ComboBox[5];
    private readonly Rectangle[] _color1Rects = new Rectangle[5];
    private readonly Rectangle[] _color2Rects = new Rectangle[5];
    private readonly Button[] _color1Buttons = new Button[5];
    private readonly Button[] _color2Buttons = new Button[5];
    private readonly StackPanel[] _color2Panels = new StackPanel[5];
    private readonly Slider[] _speedSliders = new Slider[5];
    private readonly TextBlock[] _speedLabels = new TextBlock[5];
    private readonly StackPanel[] _speedPanels = new StackPanel[5];

    // Track current colors in memory
    private readonly Color[] _colors1 = new Color[5];
    private readonly Color[] _colors2 = new Color[5];

    private static readonly LightEffect[] EffectsNeedingColor2 =
        { LightEffect.ColorBlend, LightEffect.Blink, LightEffect.Pulse, LightEffect.MicStatus, LightEffect.DeviceMute };
    private static readonly LightEffect[] EffectsNeedingSpeed =
        { LightEffect.Blink, LightEffect.Pulse, LightEffect.RainbowWave, LightEffect.RainbowCycle };

    public LightsView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        BuildChannelControls();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        for (int i = 0; i < 5; i++)
        {
            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            _effectCombos[i].SelectedItem = light.Effect;

            _colors1[i] = Color.FromRgb((byte)light.R, (byte)light.G, (byte)light.B);
            _colors2[i] = Color.FromRgb((byte)light.R2, (byte)light.G2, (byte)light.B2);
            _color1Rects[i].Fill = new SolidColorBrush(_colors1[i]);
            _color2Rects[i].Fill = new SolidColorBrush(_colors2[i]);

            _speedSliders[i].Value = Math.Clamp(light.EffectSpeed, 1, 100);
            _speedLabels[i].Text = light.EffectSpeed.ToString();

            UpdateVisibility(i, light.Effect);
        }

        BrightnessSlider.Value = Math.Clamp(config.LedBrightness, 0, 100);
        BrightnessValueLabel.Text = $"{(int)BrightnessSlider.Value}%";

        BrightnessSlider.ValueChanged -= BrightnessSlider_ValueChanged;
        BrightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;

        _loading = false;
    }

    private void BuildChannelControls()
    {
        var panels = new[] { Led0Panel, Led1Panel, Led2Panel, Led3Panel, Led4Panel };

        for (int i = 0; i < 5; i++)
        {
            int idx = i; // capture
            var panel = panels[i];

            // Header
            var header = new TextBlock
            {
                Text = $"LED {i + 1}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(header);

            // Effect label
            panel.Children.Add(MakeLabel("EFFECT"));

            // Effect combo
            var combo = new ComboBox
            {
                ItemsSource = Enum.GetValues<LightEffect>(),
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                if (combo.SelectedItem is LightEffect eff)
                    UpdateVisibility(idx, eff);
                QueueSave();
            };
            _effectCombos[i] = combo;
            panel.Children.Add(combo);

            // Color 1
            panel.Children.Add(MakeLabel("COLOR 1"));
            var (color1Panel, color1Rect, color1Btn) = MakeColorRow(idx, isColor2: false);
            _color1Rects[i] = color1Rect;
            _color1Buttons[i] = color1Btn;
            panel.Children.Add(color1Panel);

            // Color 2
            var color2Header = MakeLabel("COLOR 2");
            var (color2Row, color2Rect, color2Btn) = MakeColorRow(idx, isColor2: true);
            _color2Rects[i] = color2Rect;
            _color2Buttons[i] = color2Btn;

            var color2Container = new StackPanel();
            color2Container.Children.Add(color2Header);
            color2Container.Children.Add(color2Row);
            _color2Panels[i] = color2Container;
            panel.Children.Add(color2Container);

            // Speed
            var speedHeader = MakeLabel("SPEED");
            var speedSlider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = 50,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var speedLabel = new TextBlock
            {
                Text = "50",
                Style = FindStyle("SecondaryText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            speedSlider.ValueChanged += (_, e) =>
            {
                speedLabel.Text = ((int)e.NewValue).ToString();
                if (!_loading) QueueSave();
            };
            _speedSliders[i] = speedSlider;
            _speedLabels[i] = speedLabel;

            var speedContainer = new StackPanel();
            speedContainer.Children.Add(speedHeader);
            speedContainer.Children.Add(speedSlider);
            speedContainer.Children.Add(speedLabel);
            _speedPanels[i] = speedContainer;
            panel.Children.Add(speedContainer);
        }
    }

    private (StackPanel panel, Rectangle rect, Button btn) MakeColorRow(int idx, bool isColor2)
    {
        var rect = new Rectangle
        {
            Width = double.NaN,
            Height = 28,
            RadiusX = 4,
            RadiusY = 4,
            Fill = new SolidColorBrush(Colors.Black),
            Stroke = FindBrush("InputBorderBrush"),
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var btn = new Button
        {
            Content = "Pick",
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 12),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => OnPickColor(idx, isColor2);

        var panel = new StackPanel();
        panel.Children.Add(rect);
        panel.Children.Add(btn);
        return (panel, rect, btn);
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
                _color2Rects[idx].Fill = new SolidColorBrush(chosen);
            }
            else
            {
                _colors1[idx] = chosen;
                _color1Rects[idx].Fill = new SolidColorBrush(chosen);
            }
            QueueSave();
        }
    }

    private void UpdateVisibility(int idx, LightEffect effect)
    {
        bool needsColor2 = EffectsNeedingColor2.Contains(effect);
        bool needsSpeed = EffectsNeedingSpeed.Contains(effect);

        _color2Panels[idx].Visibility = needsColor2 ? Visibility.Visible : Visibility.Collapsed;
        _speedPanels[idx].Visibility = needsSpeed ? Visibility.Visible : Visibility.Collapsed;
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

        for (int i = 0; i < 5; i++)
        {
            var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            if (_effectCombos[i].SelectedItem is LightEffect eff)
                light.Effect = eff;

            light.R = _colors1[i].R;
            light.G = _colors1[i].G;
            light.B = _colors1[i].B;

            light.R2 = _colors2[i].R;
            light.G2 = _colors2[i].G;
            light.B2 = _colors2[i].B;

            light.EffectSpeed = (int)_speedSliders[i].Value;
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
            Margin = new Thickness(0, 0, 0, 4)
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
}

/// <summary>
/// Simple RGB color picker dialog with dark theme.
/// </summary>
public class ColorPickerDialog : Window
{
    public Color SelectedColor { get; private set; }

    private readonly Slider _rSlider;
    private readonly Slider _gSlider;
    private readonly Slider _bSlider;
    private readonly Rectangle _preview;
    private readonly TextBlock _rLabel;
    private readonly TextBlock _gLabel;
    private readonly TextBlock _bLabel;

    public ColorPickerDialog(Color initial)
    {
        SelectedColor = initial;
        Title = "Pick Color";
        Width = 320;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));

        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        // Preview
        _preview = new Rectangle
        {
            Height = 40,
            RadiusX = 6,
            RadiusY = 6,
            Fill = new SolidColorBrush(initial),
            Stroke = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 0, 16)
        };
        mainPanel.Children.Add(_preview);

        // R
        (_rSlider, _rLabel) = MakeChannelRow(mainPanel, "R", initial.R);
        // G
        (_gSlider, _gLabel) = MakeChannelRow(mainPanel, "G", initial.G);
        // B
        (_bSlider, _bLabel) = MakeChannelRow(mainPanel, "B", initial.B);

        _rSlider.ValueChanged += (_, _) => UpdatePreview();
        _gSlider.ValueChanged += (_, _) => UpdatePreview();
        _bSlider.ValueChanged += (_, _) => UpdatePreview();

        // Buttons
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xD8)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        okBtn.Click += (_, _) =>
        {
            SelectedColor = Color.FromRgb((byte)_rSlider.Value, (byte)_gSlider.Value, (byte)_bSlider.Value);
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
            Cursor = System.Windows.Input.Cursors.Hand
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
    }

    private (Slider slider, TextBlock label) MakeChannelRow(StackPanel parent, string name, byte value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var lbl = new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 13,
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
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        var valLabel = new TextBlock
        {
            Text = value.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valLabel, 2);
        row.Children.Add(valLabel);

        slider.ValueChanged += (_, e) => valLabel.Text = ((int)e.NewValue).ToString();

        parent.Children.Add(row);
        return (slider, valLabel);
    }

    private void UpdatePreview()
    {
        _preview.Fill = new SolidColorBrush(
            Color.FromRgb((byte)_rSlider.Value, (byte)_gSlider.Value, (byte)_bSlider.Value));
    }
}
