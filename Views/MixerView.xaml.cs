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
        { "master", "mic", "system", "any", "active_window", "output_device", "input_device", "monitor",
          "apps",
          "ha_light", "ha_media", "ha_fan", "ha_cover",
          "fc_fan",
          "discord", "spotify", "chrome" };

    // HA target prefix → domain for entity filtering
    private static readonly Dictionary<string, string> HATargetDomains = new()
    {
        { "ha_light", "light" },
        { "ha_media", "media_player" },
        { "ha_fan", "fan" },
        { "ha_cover", "cover" }
    };

    // Display names for HA targets in the combo
    private static readonly Dictionary<string, string> HATargetDisplayNames = new()
    {
        { "ha_light", "HA: Light" },
        { "ha_media", "HA: Media" },
        { "ha_fan", "HA: Fan" },
        { "ha_cover", "HA: Cover" },
        { "fc_fan", "FC: Fan Speed" },
        { "apps", "App Group" }
    };

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly Image[] _icons = new Image[5];
    private readonly ComboBox[] _targetCombos = new ComboBox[5];
    private readonly ComboBox[] _curveCombos = new ComboBox[5];
    private readonly Slider[] _minSliders = new Slider[5];
    private readonly Slider[] _maxSliders = new Slider[5];
    private readonly TextBlock[] _minLabels = new TextBlock[5];
    private readonly TextBlock[] _maxLabels = new TextBlock[5];
    private readonly ComboBox[] _deviceCombos = new ComboBox[5];
    private readonly StackPanel[] _devicePanels = new StackPanel[5];
    private readonly ComboBox[] _haEntityCombos = new ComboBox[5];
    private readonly StackPanel[] _haEntityPanels = new StackPanel[5];
    private readonly TextBlock[] _muteLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];

    // Audio devices cache
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    // FC controller picker
    private readonly ComboBox[] _fcControllerCombos = new ComboBox[5];
    private readonly StackPanel[] _fcControllerPanels = new StackPanel[5];

    // App group picker (for "apps" target)
    private readonly StackPanel[] _appsPanels = new StackPanel[5];
    private readonly StackPanel[] _appsListPanels = new StackPanel[5]; // holds the app tag chips
    private readonly ComboBox[] _appsAddCombos = new ComboBox[5];

    // HA entities cache
    private List<HAEntity> _haEntities = new();
    private HAIntegration? _ha;

    // FC controllers cache
    private List<FanControlSensor> _fcControllers = new();
    private FanController? _fc;

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

        // Create/update HA client if enabled
        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
        {
            if (_ha == null)
                _ha = new HAIntegration(config.HomeAssistant);
            else
                _ha.UpdateConfig(config.HomeAssistant);
        }

        // Create/update FC client if enabled
        if (config.FanControl.Enabled)
        {
            if (_fc == null)
                _fc = new FanController(config.FanControl);
            else
                _fc.UpdateConfig(config.FanControl);
        }

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            // Label — use custom label, or derive from target name
            _channelLabels[i].Text = GetDisplayLabel(knob);

            // Target combo — for HA targets, select just the prefix
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

            // Visibility for device/HA pickers
            UpdatePickerVisibility(i, knob.Target);

            // Apply light color to knob arc and volume label
            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light != null)
            {
                var color = Color.FromRgb(
                    (byte)Math.Clamp(light.R, 0, 255),
                    (byte)Math.Clamp(light.G, 0, 255),
                    (byte)Math.Clamp(light.B, 0, 255));
                _knobs[i].ArcColor = color;
                _volLabels[i].Foreground = new SolidColorBrush(color);
                _vuMeters[i].BarColor = color;
            }
        }

        _loading = false;

        // Fetch HA entities if enabled
        if (_ha != null)
            _ = FetchHAEntitiesAsync();

        // Fetch FC controllers if enabled
        if (_fc != null)
            _ = FetchFCControllersAsync();

        // Start live polling
        _liveTimer.Start();
    }

    private async Task FetchHAEntitiesAsync()
    {
        if (_ha == null) return;

        try
        {
            var connected = await _ha.TestConnectionAsync();
            if (!connected) return;

            _haEntities = await _ha.GetEntitiesAsync();

            Dispatcher.Invoke(() =>
            {
                // Re-populate all HA entity combos
                for (int i = 0; i < 5; i++)
                {
                    var target = GetSelectedTarget(_targetCombos[i]);
                    if (HATargetDomains.ContainsKey(target))
                        PopulateHAEntityCombo(i, target);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"MixerView HA fetch: {ex.Message}");
        }
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
                var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
                bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget.StartsWith("fc_") || baseTarget == "monitor";

                float vol;
                float peak;
                if (isNonAudio)
                {
                    // Use hardware knob position for non-audio targets
                    vol = App.KnobPositions[i];
                    peak = 0f;
                }
                else
                {
                    vol = _mixer.GetVolume(knob);
                    peak = _mixer.GetPeakLevel(knob);
                }

                _knobs[i].Value = vol;
                int pct = (int)(vol * 100);
                _knobs[i].PercentText = $"{pct}%";
                _volLabels[i].Text = $"{pct}%";

                _vuMeters[i].Level = peak;
                _vuMeters[i].Tick();
            }
            catch (Exception ex)
            {
                Logger.Log($"LiveTimer ch{i}: {ex.Message}");
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

            // --- Channel label (editable) ---
            var label = new TextBox
            {
                Text = $"Knob {i + 1}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 0, 8),
                MaxLength = 20,
                Cursor = System.Windows.Input.Cursors.IBeam
            };
            // Show subtle underline on hover/focus
            label.GotFocus += (_, _) => label.BorderBrush = FindBrush("AccentBrush");
            label.LostFocus += (_, _) =>
            {
                label.BorderBrush = Brushes.Transparent;
                if (!_loading) QueueSave();
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
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            // Populate with display-friendly names
            foreach (var tv in TargetValues)
            {
                var displayText = HATargetDisplayNames.TryGetValue(tv, out var dn) ? dn : tv;
                targetCombo.Items.Add(new ComboBoxItem { Content = displayText, Tag = tv });
            }
            targetCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var selected = GetSelectedTarget(targetCombo);
                UpdatePickerVisibility(idx, selected);
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

            // HA entity picker (hidden unless target is ha_light / ha_media / etc.)
            var haContainer = new StackPanel { Visibility = Visibility.Collapsed };
            haContainer.Children.Add(MakeLabel("HA ENTITY"));
            var haEntityCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            haEntityCombo.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _haEntityCombos[i] = haEntityCombo;
            _haEntityPanels[i] = haContainer;
            haContainer.Children.Add(haEntityCombo);
            controlsPanel.Children.Add(haContainer);

            // FC controller picker (hidden unless target is fc_fan)
            var fcContainer = new StackPanel { Visibility = Visibility.Collapsed };
            fcContainer.Children.Add(MakeLabel("FC CONTROLLER"));
            var fcCtrlCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            fcCtrlCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
            fcCtrlCombo.SelectedIndex = 0;
            fcCtrlCombo.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _fcControllerCombos[i] = fcCtrlCombo;
            _fcControllerPanels[i] = fcContainer;
            fcContainer.Children.Add(fcCtrlCombo);
            controlsPanel.Children.Add(fcContainer);

            // App group picker (hidden unless target is "apps")
            var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
            appsContainer.Children.Add(MakeLabel("APPS"));

            // List of bound app chips
            var appsListPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            _appsListPanels[i] = appsListPanel;
            appsContainer.Children.Add(appsListPanel);

            // "Add app" row: combo + button
            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var appsAddCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEditable = true
            };
            Grid.SetColumn(appsAddCombo, 0);
            appsAddCombo.DropDownOpened += (_, _) => PopulateRunningApps(idx);
            _appsAddCombos[i] = appsAddCombo;
            addRow.Children.Add(appsAddCombo);

            var addBtn = new Button
            {
                Content = "+",
                FontWeight = FontWeights.Bold,
                Width = 28,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                Background = FindBrush("AccentBrush"),
                Foreground = Brushes.Black,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(addBtn, 1);
            addBtn.Click += (_, _) =>
            {
                var appName = appsAddCombo.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(appName)) return;
                AddAppToGroup(idx, appName);
                appsAddCombo.Text = "";
            };
            addRow.Children.Add(addBtn);
            appsContainer.Children.Add(addRow);

            _appsPanels[i] = appsContainer;
            controlsPanel.Children.Add(appsContainer);

            panel.Children.Add(controlsBorder);
        }
    }

    // --- Visibility ---

    private void UpdatePickerVisibility(int idx, string target)
    {
        // Extract HA prefix from compound targets like "ha_light:light.entity_id"
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        bool showDevice = baseTarget == "output_device" || baseTarget == "input_device";
        _devicePanels[idx].Visibility = showDevice ? Visibility.Visible : Visibility.Collapsed;

        bool showHA = HATargetDomains.ContainsKey(baseTarget);
        _haEntityPanels[idx].Visibility = showHA ? Visibility.Visible : Visibility.Collapsed;

        if (showHA)
            PopulateHAEntityCombo(idx, baseTarget);

        bool showFC = baseTarget == "fc_fan";
        _fcControllerPanels[idx].Visibility = showFC ? Visibility.Visible : Visibility.Collapsed;

        if (showFC)
            PopulateFCControllerCombo(idx);

        bool showApps = baseTarget == "apps";
        _appsPanels[idx].Visibility = showApps ? Visibility.Visible : Visibility.Collapsed;

        if (showApps)
        {
            PopulateRunningApps(idx);
            RebuildAppChips(idx);
        }
    }

    private void PopulateHAEntityCombo(int idx, string haTarget)
    {
        var combo = _haEntityCombos[idx];
        combo.Items.Clear();

        if (!HATargetDomains.TryGetValue(haTarget, out var domain))
            return;

        var filtered = _haEntities.Where(e => e.Domain == domain).OrderBy(e => e.FriendlyName).ToList();
        foreach (var entity in filtered)
            combo.Items.Add(new ComboBoxItem { Content = entity.FriendlyName, Tag = entity.EntityId });

        // Restore selection from config
        if (_config != null)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
            if (knob != null && knob.Target.StartsWith(haTarget + ":"))
            {
                var entityId = knob.Target.Substring(haTarget.Length + 1);
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is ComboBoxItem item && item.Tag as string == entityId)
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
    }

    private void PopulateFCControllerCombo(int idx)
    {
        var combo = _fcControllerCombos[idx];
        combo.Items.Clear();

        combo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });

        foreach (var ctrl in _fcControllers.OrderBy(c => c.Name))
        {
            var pct = (int)Math.Round(ctrl.Value);
            combo.Items.Add(new ComboBoxItem { Content = $"{ctrl.Name} ({pct}%)", Tag = ctrl.Id });
        }

        // Restore selection from config
        if (_config != null)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
            if (knob != null && knob.Target.StartsWith("fc_fan:"))
            {
                var controlId = FanController.ParseTarget(knob.Target);
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is ComboBoxItem item && item.Tag as string == controlId)
                    {
                        combo.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        if (combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
    }

    private async Task FetchFCControllersAsync()
    {
        if (_fc == null) return;

        try
        {
            var connected = await _fc.TestConnectionAsync();
            if (!connected) return;

            _fcControllers = await _fc.GetControllersAsync();

            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    var target = GetSelectedTarget(_targetCombos[i]);
                    if (target == "fc_fan")
                        PopulateFCControllerCombo(i);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"MixerView FC fetch: {ex.Message}");
        }
    }

    // --- Target / device combo helpers ---

    private void SelectTarget(ComboBox combo, string target)
    {
        // For HA compound targets like "ha_light:light.entity_id", select just "ha_light"
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == baseTarget)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // Custom process name — add it as a new item
        combo.Items.Add(new ComboBoxItem { Content = target, Tag = target });
        combo.SelectedIndex = combo.Items.Count - 1;
    }

    private string GetSelectedTarget(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "none";
        return "none";
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

    private string GetSelectedEntityId(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "";
        return "";
    }

    // --- App group helpers ---

    private void AddAppToGroup(int idx, string appName)
    {
        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        if (!knob.Apps.Contains(appName, StringComparer.OrdinalIgnoreCase))
        {
            knob.Apps.Add(appName);
            RebuildAppChips(idx);
            QueueSave();
        }
    }

    private void RemoveAppFromGroup(int idx, string appName)
    {
        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        knob.Apps.RemoveAll(a => a.Equals(appName, StringComparison.OrdinalIgnoreCase));
        RebuildAppChips(idx);
        QueueSave();
    }

    private void RebuildAppChips(int idx)
    {
        var panel = _appsListPanels[idx];
        panel.Children.Clear();

        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        foreach (var app in knob.Apps)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var chipGrid = new Grid();
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var appLabel = new TextBlock
            {
                Text = app,
                FontSize = 12,
                Foreground = FindBrush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(appLabel, 0);
            chipGrid.Children.Add(appLabel);

            var removeBtn = new TextBlock
            {
                Text = "\u2715",
                FontSize = 10,
                Foreground = FindBrush("DangerRedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 2, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var appCapture = app;
            removeBtn.MouseLeftButtonDown += (_, _) => RemoveAppFromGroup(idx, appCapture);
            Grid.SetColumn(removeBtn, 1);
            chipGrid.Children.Add(removeBtn);

            chip.Child = chipGrid;
            panel.Children.Add(chip);
        }
    }

    private void PopulateRunningApps(int idx)
    {
        var combo = _appsAddCombos[idx];
        combo.Items.Clear();

        if (_mixer == null) return;
        var runningApps = _mixer.GetRunningAudioApps();

        // Exclude apps already in the group
        var knob = _config?.Knobs.FirstOrDefault(k => k.Idx == idx);
        var existing = knob?.Apps ?? new List<string>();

        foreach (var app in runningApps)
        {
            if (!existing.Contains(app, StringComparer.OrdinalIgnoreCase))
                combo.Items.Add(app);
        }
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

            // Save label — store the user's text, or empty if it matches the auto-derived name
            var labelText = _channelLabels[i].Text.Trim();
            var selectedTarget = GetSelectedTarget(_targetCombos[i]);
            var autoName = FormatTargetName(selectedTarget);
            knob.Label = (labelText == autoName || labelText == $"Knob {i + 1}") ? "" : labelText;

            // For HA targets, combine with the selected entity
            if (HATargetDomains.ContainsKey(selectedTarget))
            {
                var entityId = GetSelectedEntityId(_haEntityCombos[i]);
                knob.Target = !string.IsNullOrEmpty(entityId) ? $"{selectedTarget}:{entityId}" : selectedTarget;
            }
            else if (selectedTarget == "fc_fan")
            {
                var controlId = GetSelectedEntityId(_fcControllerCombos[i]);
                knob.Target = !string.IsNullOrEmpty(controlId) ? $"fc_fan:{controlId}" : "fc_fan";
            }
            else if (selectedTarget == "apps")
            {
                knob.Target = "apps";
                // Apps list is managed directly via AddAppToGroup/RemoveAppFromGroup
            }
            else
            {
                knob.Target = selectedTarget;
            }

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

    /// <summary>
    /// Get the display label for a knob. Uses custom label if set, otherwise
    /// derives a friendly name from the target (e.g., "master" → "Master").
    /// </summary>
    private static string GetDisplayLabel(KnobConfig knob)
    {
        if (!string.IsNullOrWhiteSpace(knob.Label))
            return knob.Label;
        return FormatTargetName(knob.Target);
    }

    /// <summary>
    /// Format a target string into a display-friendly name.
    /// "active_window" → "Active Window", "discord" → "Discord", etc.
    /// </summary>
    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none")
            return "None";

        // HA compound targets — show display name
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        if (HATargetDisplayNames.TryGetValue(baseTarget, out var displayName))
            return displayName;

        // Replace underscores with spaces and title-case each word
        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
