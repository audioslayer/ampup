using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WolfMixer.Controls;

namespace WolfMixer.Views;

public partial class MixerView : UserControl
{
    private AppConfig? _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _liveTimer;

    // Target options for the combo box
    private static readonly string[] TargetValues =
        { "master", "mic", "system", "any", "active_window", "output_device", "input_device", "monitor", "discord", "spotify", "chrome" };

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBlock[] _channelLabels = new TextBlock[5];
    private readonly Image[] _icons = new Image[5];
    private readonly ComboBox[] _targetCombos = new ComboBox[5];
    private readonly ComboBox[] _curveCombos = new ComboBox[5];
    private readonly Slider[] _minSliders = new Slider[5];
    private readonly Slider[] _maxSliders = new Slider[5];
    private readonly TextBlock[] _minLabels = new TextBlock[5];
    private readonly TextBlock[] _maxLabels = new TextBlock[5];
    private readonly ComboBox[] _deviceCombos = new ComboBox[5];
    private readonly StackPanel[] _devicePanels = new StackPanel[5];
    private readonly TextBlock[] _muteLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];

    // Audio devices cache
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    public MixerView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _liveTimer.Tick += LiveTimer_Tick;

        Unloaded += (_, _) => _liveTimer.Stop();

        BuildChannelControls();
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onConfigChanged)
    {
        _loading = true;
        _config = config;
        _mixer = mixer;
        _onSave = onConfigChanged;

        _audioDevices = mixer.GetAudioDevices();

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            // Label
            _channelLabels[i].Text = string.IsNullOrEmpty(knob.Label) ? $"Knob {i + 1}" : knob.Label;

            // Target combo
            SelectTarget(_targetCombos[i], knob.Target);

            // Curve combo
            _curveCombos[i].SelectedItem = knob.Curve;

            // Volume range sliders
            _minSliders[i].Value = Math.Clamp(knob.MinVolume, 0, 100);
            _maxSliders[i].Value = Math.Clamp(knob.MaxVolume, 0, 100);
            _minLabels[i].Text = $"{knob.MinVolume}%";
            _maxLabels[i].Text = $"{knob.MaxVolume}%";

            // Device combo
            PopulateDeviceCombo(_deviceCombos[i]);
            SelectDeviceCombo(_deviceCombos[i], knob.DeviceId);

            // Device visibility
            UpdateDeviceVisibility(i, knob.Target);
        }

        _loading = false;

        // Start live polling
        _liveTimer.Start();
    }

    private void LiveTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        if (_mixer == null || _config == null) return;

        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            try
            {
                // Volume
                float vol = _mixer.GetVolume(knob);
                _knobs[i].Value = vol;
                int pct = (int)(vol * 100);
                _knobs[i].PercentText = $"{pct}%";
                _volLabels[i].Text = $"{pct}%";

                // Peak level
                float peak = _mixer.GetPeakLevel(knob);
                _vuMeters[i].Level = peak;
                _vuMeters[i].Tick();
            }
            catch
            {
                // Silently ignore — device may have been removed
            }
        }
    }

    private void BuildChannelControls()
    {
        var panels = new[] { Ch0Panel, Ch1Panel, Ch2Panel, Ch3Panel, Ch4Panel };

        for (int i = 0; i < 5; i++)
        {
            int idx = i; // capture
            var panel = panels[i];

            // --- App icon placeholder ---
            var iconContainer = new Border
            {
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            // TODO: Resolve icon by target name (e.g., find app icon for "discord", "spotify", etc.)
            var icon = new Image
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform
            };
            iconContainer.Child = icon;
            _icons[i] = icon;
            panel.Children.Add(iconContainer);

            // --- Channel label ---
            var label = new TextBlock
            {
                Text = $"Knob {i + 1}",
                Style = FindStyle("BodyText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _channelLabels[i] = label;
            panel.Children.Add(label);

            // --- Mute indicator (hidden by default) ---
            var muteLabel = new TextBlock
            {
                Text = "MUTE",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("DangerRedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };
            _muteLabels[i] = muteLabel;
            panel.Children.Add(muteLabel);

            // --- Animated knob ---
            var knob = new AnimatedKnobControl
            {
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _knobs[i] = knob;
            panel.Children.Add(knob);

            // --- VU meter ---
            var vuMeter = new VuMeterControl
            {
                Width = 14,
                Height = 80,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _vuMeters[i] = vuMeter;
            panel.Children.Add(vuMeter);

            // --- Volume percentage ---
            var volLabel = new TextBlock
            {
                Text = "0%",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                MinWidth = 36,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _volLabels[i] = volLabel;
            panel.Children.Add(volLabel);

            // --- Controls section (recessed sub-panel) ---
            var controlsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 10, 8, 6),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var controlsPanel = new StackPanel();
            controlsBorder.Child = controlsPanel;

            // Target label + combo
            controlsPanel.Children.Add(MakeLabel("TARGET"));
            var targetCombo = new ComboBox
            {
                ItemsSource = TargetValues,
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            targetCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var selected = targetCombo.SelectedItem as string ?? "none";
                UpdateDeviceVisibility(idx, selected);
                QueueSave();
            };
            _targetCombos[i] = targetCombo;
            controlsPanel.Children.Add(targetCombo);

            // Response curve label + combo
            controlsPanel.Children.Add(MakeLabel("CURVE"));
            var curveCombo = new ComboBox
            {
                ItemsSource = Enum.GetValues<ResponseCurve>(),
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            curveCombo.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _curveCombos[i] = curveCombo;
            controlsPanel.Children.Add(curveCombo);

            // Min volume
            controlsPanel.Children.Add(MakeLabel("MIN VOLUME"));
            var minSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var minLabel = new TextBlock
            {
                Text = "0%",
                Style = FindStyle("SecondaryText"),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            minSlider.ValueChanged += (_, e) =>
            {
                minLabel.Text = $"{(int)e.NewValue}%";
                if (!_loading) QueueSave();
            };
            _minSliders[i] = minSlider;
            _minLabels[i] = minLabel;
            controlsPanel.Children.Add(minSlider);
            controlsPanel.Children.Add(minLabel);

            // Max volume
            controlsPanel.Children.Add(MakeLabel("MAX VOLUME"));
            var maxSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var maxLabel = new TextBlock
            {
                Text = "100%",
                Style = FindStyle("SecondaryText"),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            maxSlider.ValueChanged += (_, e) =>
            {
                maxLabel.Text = $"{(int)e.NewValue}%";
                if (!_loading) QueueSave();
            };
            _maxSliders[i] = maxSlider;
            _maxLabels[i] = maxLabel;
            controlsPanel.Children.Add(maxSlider);
            controlsPanel.Children.Add(maxLabel);

            // Device picker (hidden unless target is output_device / input_device)
            var deviceContainer = new StackPanel { Visibility = Visibility.Collapsed };
            deviceContainer.Children.Add(MakeLabel("DEVICE"));
            var deviceCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            deviceCombo.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _deviceCombos[i] = deviceCombo;
            _devicePanels[i] = deviceContainer;
            deviceContainer.Children.Add(deviceCombo);
            controlsPanel.Children.Add(deviceContainer);

            panel.Children.Add(controlsBorder);
        }
    }

    // --- Visibility ---

    private void UpdateDeviceVisibility(int idx, string target)
    {
        bool show = target == "output_device" || target == "input_device";
        _devicePanels[idx].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Target / device combo helpers ---

    private void SelectTarget(ComboBox combo, string target)
    {
        for (int i = 0; i < TargetValues.Length; i++)
        {
            if (TargetValues[i] == target)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // Custom process name — extend the ItemsSource with it
        var extended = TargetValues.Append(target).ToArray();
        combo.ItemsSource = extended;
        combo.SelectedItem = target;
    }

    private void PopulateDeviceCombo(ComboBox combo)
    {
        combo.Items.Clear();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            var tag = isOutput ? "OUT" : "IN";
            combo.Items.Add(new ComboBoxItem { Content = $"[{tag}] {name}", Tag = id });
        }
    }

    private void SelectDeviceCombo(ComboBox combo, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            combo.SelectedIndex = -1;
            return;
        }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == deviceId)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    private string GetSelectedDeviceId(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "";
        return "";
    }

    // --- Save ---

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
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            knob.Target = _targetCombos[i].SelectedItem as string ?? "none";

            if (_curveCombos[i].SelectedItem is ResponseCurve curve)
                knob.Curve = curve;

            knob.MinVolume = (int)_minSliders[i].Value;
            knob.MaxVolume = (int)_maxSliders[i].Value;
            knob.DeviceId = GetSelectedDeviceId(_deviceCombos[i]);
        }

        _onSave(_config);
    }

    // --- Helpers ---

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
}
