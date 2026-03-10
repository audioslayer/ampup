using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    // HA target prefix → domain for entity filtering
    private static readonly Dictionary<string, string> HATargetDomains = new()
    {
        { "ha_light", "light" },
        { "ha_media", "media_player" },
        { "ha_fan", "fan" },
        { "ha_cover", "cover" }
    };

    // Display names for targets
    private static readonly Dictionary<string, string> HATargetDisplayNames = new()
    {
        { "ha_light", "HA: Light" },
        { "ha_media", "HA: Media" },
        { "ha_fan", "HA: Fan" },
        { "ha_cover", "HA: Cover" },
        { "apps", "App Group" }
    };

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly Image[] _icons = new Image[5];
    private readonly GridPicker[] _targetPickers = new GridPicker[5];
    private readonly SegmentedControl[] _curvePickers = new SegmentedControl[5];
    private readonly RangeSlider[] _rangeSliders = new RangeSlider[5];
    private readonly ListPicker[] _devicePickers = new ListPicker[5];
    private readonly StackPanel[] _devicePanels = new StackPanel[5];
    private readonly ListPicker[] _haEntityPickers = new ListPicker[5];
    private readonly StackPanel[] _haEntityPanels = new StackPanel[5];
    private readonly TextBlock[] _muteLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];

    // Collapsible settings
    private readonly Border[] _settingsBorders = new Border[5];
    private readonly bool[] _settingsExpanded = new bool[5];

    // Audio devices cache
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    // App group picker (for "apps" target)
    private readonly StackPanel[] _appsPanels = new StackPanel[5];
    private readonly StackPanel[] _appsListPanels = new StackPanel[5];
    private readonly ListPicker[] _appsAddPickers = new ListPicker[5];

    // HA entities cache
    private List<HAEntity> _haEntities = new();
    private HAIntegration? _ha;

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

        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
        {
            if (_ha == null)
                _ha = new HAIntegration(config.HomeAssistant);
            else
                _ha.UpdateConfig(config.HomeAssistant);
        }

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            _channelLabels[i].Text = GetDisplayLabel(knob);
            SelectTarget(_targetPickers[i], knob.Target);
            SelectCurve(_curvePickers[i], knob.Curve);

            _rangeSliders[i].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
            _rangeSliders[i].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);

            PopulateDevicePicker(_devicePickers[i]);
            SelectPickerByTag(_devicePickers[i], knob.DeviceId);

            UpdatePickerVisibility(i, knob.Target);

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

        if (_ha != null)
            _ = FetchHAEntitiesAsync();

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
                for (int i = 0; i < 5; i++)
                {
                    var target = GetSelectedTarget(_targetPickers[i]);
                    if (HATargetDomains.ContainsKey(target))
                        PopulateHAEntityPicker(i, target);
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
                bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget == "monitor";

                float vol;
                float peak;
                if (isNonAudio)
                {
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
            int idx = i;
            var panel = panels[i];

            // ═══════════════════════════════════════════════════════════
            // TOP SECTION: Icon + Label + Mute
            // ═══════════════════════════════════════════════════════════

            // Icon container — hidden by default, shown when Source is set
            var iconContainer = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 6),
                Visibility = Visibility.Collapsed
            };
            var icon = new Image
            {
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform
            };
            iconContainer.Child = icon;
            _icons[i] = icon;
            panel.Children.Add(iconContainer);

            var label = new TextBox
            {
                Text = $"Knob {i + 1}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 0, 2),
                MaxLength = 20,
                Cursor = System.Windows.Input.Cursors.IBeam
            };
            label.GotFocus += (_, _) =>
            {
                label.BorderThickness = new Thickness(0, 0, 0, 1);
                label.BorderBrush = FindBrush("AccentBrush");
            };
            label.LostFocus += (_, _) =>
            {
                label.BorderThickness = new Thickness(0);
                if (!_loading) QueueSave();
            };
            _channelLabels[i] = label;
            panel.Children.Add(label);

            var muteLabel = new TextBlock
            {
                Text = "MUTE",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("DangerRedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };
            _muteLabels[i] = muteLabel;
            panel.Children.Add(muteLabel);

            // ═══════════════════════════════════════════════════════════
            // MIDDLE SECTION: Knob centered, VU meter on far right
            // ═══════════════════════════════════════════════════════════

            var knobVuGrid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4)
            };
            knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var knob = new AnimatedKnobControl
            {
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(knob, 0);
            _knobs[i] = knob;
            knobVuGrid.Children.Add(knob);

            var vuMeter = new VuMeterControl
            {
                Width = 6,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            };
            Grid.SetColumn(vuMeter, 1);
            _vuMeters[i] = vuMeter;
            knobVuGrid.Children.Add(vuMeter);

            panel.Children.Add(knobVuGrid);

            var volLabel = new TextBlock
            {
                Text = "0%",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _volLabels[i] = volLabel;
            panel.Children.Add(volLabel);

            // Target display (shows current target as small text)
            var targetDisplay = new TextBlock
            {
                FontSize = 10,
                Foreground = FindBrush("TextSecBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(targetDisplay);

            // ═══════════════════════════════════════════════════════════
            // BOTTOM SECTION: Settings (always visible)
            // ═══════════════════════════════════════════════════════════

            var divider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(divider);

            var settingsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var settingsBorder = new Border
            {
                Child = settingsPanel,
                Padding = new Thickness(0)
            };
            _settingsBorders[i] = settingsBorder;
            _settingsExpanded[i] = true;

            // ── Settings content ──

            // TARGET — GridPicker with categories
            settingsPanel.Children.Add(MakeLabel("TARGET"));
            var targetPicker = new GridPicker { Margin = new Thickness(0, 0, 0, 6) };

            targetPicker.AddCategory("Audio");
            targetPicker.AddItem("Master", "master");
            targetPicker.AddItem("Mic", "mic");
            targetPicker.AddItem("System", "system");
            targetPicker.AddItem("Any", "any");
            targetPicker.AddItem("Active Window", "active_window");

            targetPicker.AddCategory("Devices");
            targetPicker.AddItem("Output Device", "output_device");
            targetPicker.AddItem("Input Device", "input_device");
            targetPicker.AddItem("Monitor", "monitor");

            targetPicker.AddCategory("Integrations");
            targetPicker.AddItem("HA: Light", "ha_light");
            targetPicker.AddItem("HA: Media", "ha_media");
            targetPicker.AddItem("HA: Fan", "ha_fan");
            targetPicker.AddItem("HA: Cover", "ha_cover");

            targetPicker.AddCategory("Apps");
            targetPicker.AddItem("Discord", "discord");
            targetPicker.AddItem("Spotify", "spotify");
            targetPicker.AddItem("Chrome", "chrome");
            targetPicker.AddItem("App Group", "apps");

            targetPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var selected = GetSelectedTarget(_targetPickers[idx]);
                UpdatePickerVisibility(idx, selected);
                UpdateTargetDisplay(idx);
                QueueSave();
            };
            _targetPickers[i] = targetPicker;
            settingsPanel.Children.Add(targetPicker);

            // Store reference to update target display
            targetPicker.Tag = targetDisplay;

            // CURVE — SegmentedControl (pill bar)
            settingsPanel.Children.Add(MakeLabel("CURVE"));
            var curvePicker = new SegmentedControl { Margin = new Thickness(0, 0, 0, 6) };
            curvePicker.AddSegment("Linear", ResponseCurve.Linear);
            curvePicker.AddSegment("Log", ResponseCurve.Logarithmic);
            curvePicker.AddSegment("Exp", ResponseCurve.Exponential);
            curvePicker.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _curvePickers[i] = curvePicker;
            settingsPanel.Children.Add(curvePicker);

            // VOLUME RANGE
            settingsPanel.Children.Add(MakeLabel("VOLUME RANGE"));
            var rangeSlider = new RangeSlider
            {
                Minimum = 0,
                Maximum = 100,
                LowerValue = 0,
                UpperValue = 100,
                Height = 38,
                Margin = new Thickness(0, 0, 0, 6)
            };
            rangeSlider.LowerValueChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            rangeSlider.UpperValueChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _rangeSliders[i] = rangeSlider;
            settingsPanel.Children.Add(rangeSlider);

            // Device picker — ListPicker (hidden unless output_device / input_device)
            var deviceContainer = new StackPanel { Visibility = Visibility.Collapsed };
            deviceContainer.Children.Add(MakeLabel("DEVICE"));
            var devicePicker = new ListPicker { Margin = new Thickness(0, 0, 0, 4) };
            devicePicker.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _devicePickers[i] = devicePicker;
            _devicePanels[i] = deviceContainer;
            deviceContainer.Children.Add(devicePicker);
            settingsPanel.Children.Add(deviceContainer);

            // HA entity picker — ListPicker (hidden unless ha_*)
            var haContainer = new StackPanel { Visibility = Visibility.Collapsed };
            haContainer.Children.Add(MakeLabel("HA ENTITY"));
            var haEntityPicker = new ListPicker { Margin = new Thickness(0, 0, 0, 4) };
            haEntityPicker.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _haEntityPickers[i] = haEntityPicker;
            _haEntityPanels[i] = haContainer;
            haContainer.Children.Add(haEntityPicker);
            settingsPanel.Children.Add(haContainer);

            // App group picker (hidden unless "apps")
            var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
            appsContainer.Children.Add(MakeLabel("APP GROUP"));

            var appsListPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            _appsListPanels[i] = appsListPanel;
            appsContainer.Children.Add(appsListPanel);

            // "Add app" — ListPicker showing running audio apps
            var appsAddPicker = new ListPicker { Margin = new Thickness(0, 0, 0, 4) };
            appsAddPicker.DropdownOpening += (_, _) => PopulateRunningApps(idx);
            appsAddPicker.SelectionChanged += (_, _) =>
            {
                // Only add if the selected item has a tag (skip placeholder items)
                var tag = appsAddPicker.SelectedTag as string;
                if (!string.IsNullOrEmpty(tag))
                {
                    AddAppToGroup(idx, tag);
                    // Reset picker after adding
                    Dispatcher.BeginInvoke(() =>
                    {
                        PopulateRunningApps(idx);
                        appsAddPicker.SelectedIndex = -1;
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            };
            _appsAddPickers[i] = appsAddPicker;
            appsContainer.Children.Add(appsAddPicker);

            _appsPanels[i] = appsContainer;
            settingsPanel.Children.Add(appsContainer);

            panel.Children.Add(settingsBorder);
        }
    }

    private void UpdateTargetDisplay(int idx)
    {
        if (_targetPickers[idx].Tag is TextBlock display)
        {
            var target = GetSelectedTarget(_targetPickers[idx]);
            display.Text = HATargetDisplayNames.TryGetValue(target, out var dn) ? dn : FormatTargetName(target);
        }
    }

    // --- Visibility ---

    private void UpdatePickerVisibility(int idx, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        bool showDevice = baseTarget == "output_device" || baseTarget == "input_device";
        _devicePanels[idx].Visibility = showDevice ? Visibility.Visible : Visibility.Collapsed;

        bool showHA = HATargetDomains.ContainsKey(baseTarget);
        _haEntityPanels[idx].Visibility = showHA ? Visibility.Visible : Visibility.Collapsed;

        if (showHA)
            PopulateHAEntityPicker(idx, baseTarget);

        bool showApps = baseTarget == "apps";
        _appsPanels[idx].Visibility = showApps ? Visibility.Visible : Visibility.Collapsed;

        if (showApps)
        {
            PopulateRunningApps(idx);
            RebuildAppChips(idx);
        }

        UpdateTargetDisplay(idx);
    }

    private void PopulateHAEntityPicker(int idx, string haTarget)
    {
        var picker = _haEntityPickers[idx];
        picker.ClearItems();

        if (!HATargetDomains.TryGetValue(haTarget, out var domain))
            return;

        var filtered = _haEntities.Where(e => e.Domain == domain).OrderBy(e => e.FriendlyName).ToList();
        foreach (var entity in filtered)
            picker.AddItem(entity.FriendlyName, entity.EntityId);

        if (_config != null)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
            if (knob != null && knob.Target.StartsWith(haTarget + ":"))
            {
                var entityId = knob.Target.Substring(haTarget.Length + 1);
                SelectPickerByTag(picker, entityId);
            }
        }
    }

    // --- Picker helpers ---

    private void SelectTarget(GridPicker picker, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == baseTarget)
            {
                picker.SelectedIndex = i;
                if (picker.Tag is TextBlock display)
                    display.Text = HATargetDisplayNames.TryGetValue(baseTarget, out var dn) ? dn : FormatTargetName(baseTarget);
                return;
            }
        }
        // Custom process name — add it dynamically
        picker.AddItem(target, target);
        picker.SelectedIndex = picker.ItemCount - 1;
        if (picker.Tag is TextBlock d)
            d.Text = FormatTargetName(target);
    }

    private void SelectCurve(SegmentedControl picker, ResponseCurve curve)
    {
        for (int i = 0; i < picker.SegmentCount; i++)
        {
            if (picker.GetTagAt(i) is ResponseCurve rc && rc == curve)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
    }

    private string GetSelectedTarget(GridPicker picker)
    {
        return picker.SelectedTag as string ?? "none";
    }

    private static bool SelectPickerByTag(ListPicker picker, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            picker.SelectedIndex = -1;
            return false;
        }
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == tag)
            {
                picker.SelectedIndex = i;
                return true;
            }
        }
        picker.SelectedIndex = -1;
        return false;
    }

    private void PopulateDevicePicker(ListPicker picker)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            var tag = isOutput ? "OUT" : "IN";
            picker.AddItem($"[{tag}] {name}", id);
        }
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
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0xE6, 0x76)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xE6, 0x76)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 3),
                SnapsToDevicePixels = true
            };

            var chipGrid = new Grid();
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Accent dot
            var dot = new Border
            {
                Width = 4, Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = FindBrush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(dot, 0);
            chipGrid.Children.Add(dot);

            var appLabel = new TextBlock
            {
                Text = app,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(appLabel, 1);
            chipGrid.Children.Add(appLabel);

            var removeBtn = new TextBlock
            {
                Text = "\u2715",
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var appCapture = app;
            removeBtn.MouseLeftButtonDown += (_, _) => RemoveAppFromGroup(idx, appCapture);
            removeBtn.MouseEnter += (_, _) => removeBtn.Foreground = FindBrush("DangerRedBrush");
            removeBtn.MouseLeave += (_, _) => removeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            Grid.SetColumn(removeBtn, 2);
            chipGrid.Children.Add(removeBtn);

            chip.Child = chipGrid;

            // Chip hover
            chip.MouseEnter += (_, _) =>
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(0x2A, 0x00, 0xE6, 0x76));
                appLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            };
            chip.MouseLeave += (_, _) =>
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0xE6, 0x76));
                appLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            };

            panel.Children.Add(chip);
        }
    }

    private void PopulateRunningApps(int idx)
    {
        var picker = _appsAddPickers[idx];
        picker.ClearItems();

        if (_mixer == null) return;
        var runningApps = _mixer.GetRunningAudioApps();

        var knob = _config?.Knobs.FirstOrDefault(k => k.Idx == idx);
        var existing = knob?.Apps ?? new List<string>();

        // Add placeholder
        picker.AddItem("+ Add running app...", null);

        foreach (var app in runningApps)
        {
            if (!existing.Contains(app, StringComparer.OrdinalIgnoreCase))
                picker.AddItem(app, app);
        }

        if (runningApps.Count == 0 || runningApps.All(a => existing.Contains(a, StringComparer.OrdinalIgnoreCase)))
            picker.AddItem("No new apps available", null);
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

            var labelText = _channelLabels[i].Text.Trim();
            var selectedTarget = GetSelectedTarget(_targetPickers[i]);
            var autoName = FormatTargetName(selectedTarget);
            knob.Label = (labelText == autoName || labelText == $"Knob {i + 1}") ? "" : labelText;

            if (HATargetDomains.ContainsKey(selectedTarget))
            {
                var entityId = _haEntityPickers[i].SelectedTag as string ?? "";
                knob.Target = !string.IsNullOrEmpty(entityId) ? $"{selectedTarget}:{entityId}" : selectedTarget;
            }
            else if (selectedTarget == "apps")
            {
                knob.Target = "apps";
            }
            else
            {
                knob.Target = selectedTarget;
            }

            if (_curvePickers[i].SelectedTag is ResponseCurve curve)
                knob.Curve = curve;

            knob.MinVolume = (int)_rangeSliders[i].LowerValue;
            knob.MaxVolume = (int)_rangeSliders[i].UpperValue;

            knob.DeviceId = _devicePickers[i].SelectedTag as string ?? "";
        }

        _onSave(_config);
    }

    // --- Helpers ---

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 4, 0, 2)
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

    private static string GetDisplayLabel(KnobConfig knob)
    {
        if (!string.IsNullOrWhiteSpace(knob.Label))
            return knob.Label;
        return FormatTargetName(knob.Target);
    }

    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none")
            return "None";

        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        if (HATargetDisplayNames.TryGetValue(baseTarget, out var displayName))
            return displayName;

        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
