using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Controls;

namespace AmpUp.Views;

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
        { "apps", "App Group" },
        { "led_brightness", "LED Brightness" }
    };

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly Image[] _icons = new Image[5];
    private readonly GridPicker[] _targetPickers = new GridPicker[5];
    private readonly CurvePickerControl[] _curvePickers = new CurvePickerControl[5];
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

    // Suggestion banner
    private Border? _suggestionBanner;

    // Smart Mix (ducking + auto-profile) controls
    private bool _smartMixExpanded = false;
    private Wpf.Ui.Controls.TextBox[] _autoRuleAppBoxes = null!;
    private ComboBox[] _autoRuleProfileCombos = null!;

    // Clipboard for knob copy/paste
    private static KnobConfig? _clipboard;

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Hover border brush (updated on accent change)
    private SolidColorBrush _hoverBorderBrush = new(ThemeManager.WithAlpha(ThemeManager.Accent, 0x60));

    // Audio devices cache
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    // App group picker (for "apps" target)
    private readonly StackPanel[] _appsPanels = new StackPanel[5];
    private readonly StackPanel[] _appsListPanels = new StackPanel[5];


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

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildChannelControls();
        SetupStripHoverEffects();
        SetupStripContextMenus();
        SetupSuggestionBanner();
        SetupSmartMix();
    }

    private void SetupStripHoverEffects()
    {
        var borders = new[] { Ch0Border, Ch1Border, Ch2Border, Ch3Border, Ch4Border };
        var normalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        for (int i = 0; i < 5; i++)
        {
            _stripBorders[i] = borders[i];
            var strip = borders[i];

            strip.MouseEnter += (_, _) =>
            {
                strip.BorderBrush = _hoverBorderBrush;
            };
            strip.MouseLeave += (_, _) =>
            {
                strip.BorderBrush = normalBorder;
            };
        }
    }

    private void SetupStripContextMenus()
    {
        var borders = new[] { Ch0Border, Ch1Border, Ch2Border, Ch3Border, Ch4Border };

        var menuBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        var menuFg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var menuBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var border = borders[i];

            var copyItem = new MenuItem
            {
                Header = "Copy Channel Config",
                Foreground = menuFg,
                Background = menuBg,
            };
            var pasteItem = new MenuItem
            {
                Header = "Paste Channel Config",
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
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
                if (knob == null) return;
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(knob);
                _clipboard = Newtonsoft.Json.JsonConvert.DeserializeObject<KnobConfig>(json);
            };

            pasteItem.Click += (_, _) =>
            {
                if (_clipboard == null || _config == null || _onSave == null) return;
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
                if (knob == null) return;

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_clipboard);
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<KnobConfig>(json)!;
                copy.Idx = idx;

                // Apply all fields from copy
                knob.Label = copy.Label;
                knob.Target = copy.Target;
                knob.DeviceId = copy.DeviceId;
                knob.MinVolume = copy.MinVolume;
                knob.MaxVolume = copy.MaxVolume;
                knob.Curve = copy.Curve;
                knob.Apps = copy.Apps;

                _loading = true;
                _channelLabels[idx].Text = GetDisplayLabel(knob);
                SelectTarget(_targetPickers[idx], knob.Target);
                SelectCurve(_curvePickers[idx], knob.Curve);
                _rangeSliders[idx].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
                _rangeSliders[idx].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);
                SelectPickerByTag(_devicePickers[idx], knob.DeviceId);
                UpdatePickerVisibility(idx, knob.Target);
                _loading = false;

                QueueSave();
            };

            resetItem.Click += (_, _) =>
            {
                if (_config == null || _onSave == null) return;
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
                if (knob == null) return;

                knob.Label = "";
                knob.Target = "master";
                knob.Apps = new List<string>();
                knob.Curve = ResponseCurve.Linear;
                knob.MinVolume = 0;
                knob.MaxVolume = 100;
                knob.DeviceId = "";

                _loading = true;
                _channelLabels[idx].Text = GetDisplayLabel(knob);
                SelectTarget(_targetPickers[idx], "master");
                SelectCurve(_curvePickers[idx], ResponseCurve.Linear);
                _rangeSliders[idx].LowerValue = 0;
                _rangeSliders[idx].UpperValue = 100;
                SelectPickerByTag(_devicePickers[idx], "");
                UpdatePickerVisibility(idx, "master");
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
                pasteItem.IsEnabled = _clipboard != null;
                pasteItem.Opacity = _clipboard != null ? 1.0 : 0.4;
            };

            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(pasteItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(resetItem);

            border.ContextMenu = contextMenu;
        }
    }

    /// <summary>
    /// Called directly from HandleKnob (App.xaml.cs) after SetVolume,
    /// so the knob arc and volume % update immediately without waiting for the 50ms poll.
    /// </summary>
    public void UpdateKnobPosition(int idx, float position)
    {
        if (idx < 0 || idx >= 5) return;
        int pct = (int)(position * 100);
        if (_config?.Knobs.FirstOrDefault(k => k.Idx == idx) is { } knob)
        {
            var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
            // For audio targets, the live timer handles it via WASAPI; only update directly for non-audio
            if (!baseTarget.StartsWith("ha_") && baseTarget != "monitor" && baseTarget != "led_brightness")
            {
                // Apply min/max range remapping same as live timer
                int mapped = (int)Math.Round(knob.MinVolume + position * (knob.MaxVolume - knob.MinVolume));
                pct = mapped;
            }
        }
        _knobs[idx].Value = position;
        _knobs[idx].PercentText = $"{pct}%";
        _volLabels[idx].Text = $"{pct}%";
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

        // Auto-Ducking
        ChkDuckingEnabled.IsChecked = config.Ducking.Enabled;
        var duckRule = config.Ducking.Rules.Count > 0 ? config.Ducking.Rules[0] : new DuckingRule();
        TxtDuckTriggerApp.Text = duckRule.TriggerApp;
        TxtDuckTargetApps.Text = string.Join(", ", duckRule.TargetApps);
        SldDuckPercent.Value = duckRule.DuckPercent;
        TxtDuckPercentLabel.Text = $"{duckRule.DuckPercent}%";
        TxtDuckFadeOut.Text = duckRule.FadeOutMs.ToString();
        TxtDuckFadeIn.Text = duckRule.FadeInMs.ToString();

        // Auto-Profile Switching
        ChkAutoSwitchEnabled.IsChecked = config.AutoSwitch.Enabled;
        ChkAutoSwitchRevert.IsChecked = config.AutoSwitch.RevertToDefault;
        for (int i = 0; i < _autoRuleProfileCombos.Length; i++)
        {
            _autoRuleProfileCombos[i].Items.Clear();
            _autoRuleProfileCombos[i].Items.Add("");
            foreach (var p in config.Profiles)
                _autoRuleProfileCombos[i].Items.Add(p);

            if (i < config.AutoSwitch.Rules.Count)
            {
                var r = config.AutoSwitch.Rules[i];
                _autoRuleAppBoxes[i].Text = r.ProcessName;
                _autoRuleProfileCombos[i].SelectedItem = r.ProfileName;
                if (_autoRuleProfileCombos[i].SelectedIndex < 0)
                    _autoRuleProfileCombos[i].SelectedIndex = 0;
            }
            else
            {
                _autoRuleAppBoxes[i].Text = "";
                _autoRuleProfileCombos[i].SelectedIndex = 0;
            }
        }

        _loading = false;

        if (_ha != null)
            _ = FetchHAEntitiesAsync();

        CheckAndShowSuggestionBanner();

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
                bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget == "monitor" || baseTarget == "led_brightness";

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
                    // WASAPI peak values rarely exceed ~0.5, so boost 2.5x for a full meter
                    peak = Math.Min(_mixer.GetPeakLevel(knob) * 2.5f, 1f);
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
                Cursor = System.Windows.Input.Cursors.IBeam,
                ToolTip = "Click to rename this channel",
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
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Turn the physical knob to adjust volume",
            };
            Grid.SetColumn(knob, 0);
            _knobs[i] = knob;
            knobVuGrid.Children.Add(knob);

            var vuMeter = new VuMeterControl
            {
                Width = 6,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = "Audio level for this channel",
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
                Margin = new Thickness(0, 8, 0, 10)
            };
            panel.Children.Add(divider);

            var settingsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            var settingsBorder = new Border
            {
                Child = settingsPanel,
                Padding = new Thickness(0)
            };
            _settingsBorders[i] = settingsBorder;
            _settingsExpanded[i] = true;

            // ── Settings content ──

            // TARGET — GridPicker with categories
            settingsPanel.Children.Add(MakeSectionHeader("TARGET"));
            var targetPicker = new GridPicker
            {
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "What this knob controls",
            };

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
            targetPicker.AddItem("LED Brightness", "led_brightness");

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

            // ── Separator ──
            settingsPanel.Children.Add(MakeSeparator(8));

            // CURVE — CurvePickerControl (visual mini graphs)
            settingsPanel.Children.Add(MakeSectionHeader("CURVE"));
            var curvePicker = new CurvePickerControl
            {
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Linear: even response. Log: more sensitive at low volumes. Exp: more sensitive at high volumes",
            };
            curvePicker.SelectionChanged += (_, _) =>
            {
                if (!_loading) QueueSave();
            };
            _curvePickers[i] = curvePicker;
            settingsPanel.Children.Add(curvePicker);

            // ── Separator ──
            settingsPanel.Children.Add(MakeSeparator(8));

            // VOLUME RANGE
            settingsPanel.Children.Add(MakeSectionHeader("VOLUME RANGE"));
            var rangeSlider = new RangeSlider
            {
                Minimum = 0,
                Maximum = 100,
                LowerValue = 0,
                UpperValue = 100,
                Height = 38,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Set the min and max volume this knob can reach",
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
            var devicePicker = new ListPicker
            {
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Audio device to control with this knob",
            };
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
            appsContainer.ToolTip = "Check the apps to include in this group";

            var appsListPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            _appsListPanels[i] = appsListPanel;
            appsContainer.Children.Add(appsListPanel);

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
            RebuildAppToggles(idx);
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

    private void SelectCurve(CurvePickerControl picker, ResponseCurve curve)
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

    /// <summary>
    /// Rebuild the app toggle list. Shows all running audio apps with on/off toggles.
    /// Toggled ON = part of the group, OFF = not.
    /// </summary>
    private void RebuildAppToggles(int idx)
    {
        var panel = _appsListPanels[idx];
        panel.Children.Clear();

        if (_config == null || _mixer == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        var runningApps = _mixer.GetRunningAudioApps();

        // Show apps already in the group that might not be running
        var allApps = new List<string>(knob.Apps);
        foreach (var app in runningApps)
        {
            if (!allApps.Contains(app, StringComparer.OrdinalIgnoreCase))
                allApps.Add(app);
        }

        if (allApps.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No audio apps running",
                FontSize = 10,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 2, 0, 4),
            });
            return;
        }

        foreach (var app in allApps.OrderBy(a => a))
        {
            bool isInGroup = knob.Apps.Contains(app, StringComparer.OrdinalIgnoreCase);
            bool isRunning = runningApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            var appCapture = app;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // App name + running indicator
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border
            {
                Width = 6, Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(isRunning
                    ? ThemeManager.Accent  // accent = running
                    : Color.FromRgb(0x55, 0x55, 0x55)), // dim = not running
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = isRunning ? "Running" : "Not running",
            };
            namePanel.Children.Add(dot);

            var label = new TextBlock
            {
                Text = app,
                FontSize = 11,
                Foreground = new SolidColorBrush(isRunning
                    ? Color.FromRgb(0xCC, 0xCC, 0xCC)
                    : Color.FromRgb(0x77, 0x77, 0x77)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            namePanel.Children.Add(label);
            Grid.SetColumn(namePanel, 0);
            row.Children.Add(namePanel);

            // Toggle checkbox
            var toggle = new CheckBox
            {
                IsChecked = isInGroup,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            toggle.Checked += (_, _) =>
            {
                if (_loading) return;
                if (!knob.Apps.Contains(appCapture, StringComparer.OrdinalIgnoreCase))
                    knob.Apps.Add(appCapture);
                QueueSave();
            };
            toggle.Unchecked += (_, _) =>
            {
                if (_loading) return;
                knob.Apps.RemoveAll(a => a.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
                QueueSave();
            };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);

            panel.Children.Add(row);
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

        // Auto-Ducking
        _config.Ducking.Enabled = ChkDuckingEnabled.IsChecked == true;
        var duckRule = _config.Ducking.Rules.Count > 0 ? _config.Ducking.Rules[0] : new DuckingRule();
        duckRule.TriggerApp = TxtDuckTriggerApp.Text.Trim();
        duckRule.TargetApps = TxtDuckTargetApps.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        duckRule.DuckPercent = (int)SldDuckPercent.Value;
        if (int.TryParse(TxtDuckFadeOut.Text.Trim(), out var fadeOut)) duckRule.FadeOutMs = fadeOut;
        if (int.TryParse(TxtDuckFadeIn.Text.Trim(), out var fadeIn)) duckRule.FadeInMs = fadeIn;
        _config.Ducking.Rules = new List<DuckingRule> { duckRule };

        // Auto-Profile Switching
        _config.AutoSwitch.Enabled = ChkAutoSwitchEnabled.IsChecked == true;
        _config.AutoSwitch.RevertToDefault = ChkAutoSwitchRevert.IsChecked == true;
        var switchRules = new List<AutoSwitchRule>();
        for (int i = 0; i < _autoRuleAppBoxes.Length; i++)
        {
            var appName = _autoRuleAppBoxes[i].Text.Trim();
            var profileName = _autoRuleProfileCombos[i].SelectedItem?.ToString() ?? "";
            if (!string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(profileName))
                switchRules.Add(new AutoSwitchRule { ProcessName = appName, ProfileName = profileName });
        }
        _config.AutoSwitch.Rules = switchRules;

        _onSave(_config);
    }

    // --- Helpers ---

    private Grid MakeSectionHeader(string title)
    {
        var accent = ThemeManager.Accent;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            bar.Background = new SolidColorBrush(accent);
            label.Foreground = new SolidColorBrush(accent);
        }

        // Update hover border brush for strip cards
        _hoverBorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x60));

        // Update all custom controls
        for (int i = 0; i < 5; i++)
        {
            _targetPickers[i].RefreshAccent();
            _curvePickers[i].AccentColor = accent;
            _rangeSliders[i].AccentColor = accent;
            _devicePickers[i].RefreshAccent();
            _haEntityPickers[i].RefreshAccent();
        }
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

    // ── Suggestion Banner ──────────────────────────────────────────────

    private void SetupSuggestionBanner()
    {
        // Root is now a ScrollViewer > StackPanel > [ChannelGrid, SmartMixSection]
        // Insert the banner at the top of the StackPanel, before the channel grid.
        if (Content is not ScrollViewer sv) return;
        if (sv.Content is not StackPanel rootStack) return;

        var amber = Color.FromRgb(0xFF, 0xD7, 0x40);
        var amberDim = Color.FromRgb(0x2A, 0x22, 0x00);

        var bannerStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var bannerText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(amber),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        bannerStack.Children.Add(bannerText);

        var applyBtn = new System.Windows.Controls.Button
        {
            Content = "Apply",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(ThemeManager.Accent),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            BorderThickness = new Thickness(0),
        };

        var dismissBtn = new System.Windows.Controls.Button
        {
            Content = "Dismiss",
            FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            BorderThickness = new Thickness(0),
        };
        bannerStack.Children.Add(applyBtn);
        bannerStack.Children.Add(dismissBtn);

        var banner = new Border
        {
            Background = new SolidColorBrush(amberDim),
            BorderBrush = new SolidColorBrush(amber),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = bannerStack,
            Visibility = Visibility.Collapsed,
        };

        // Insert banner before the channel grid (index 0)
        rootStack.Children.Insert(0, banner);

        _suggestionBanner = banner;

        dismissBtn.Click += (_, _) => banner.Visibility = Visibility.Collapsed;
        applyBtn.Click += (_, _) =>
        {
            ApplySuggestedLayout();
            banner.Visibility = Visibility.Collapsed;
        };
    }

    private static readonly string[] CommApps = { "discord", "teams", "zoom", "slack" };
    private static readonly string[] MusicApps = { "spotify", "music", "itunes" };
    private static readonly string[] BrowserApps = { "chrome", "firefox", "edge", "brave" };

    private void CheckAndShowSuggestionBanner()
    {
        if (_suggestionBanner == null || _config == null || _mixer == null) return;
        if (!_config.AutoSuggestLayout)
        {
            _suggestionBanner.Visibility = Visibility.Collapsed;
            return;
        }

        // Count knobs still at default
        int defaultCount = _config.Knobs.Count(k => k.Target == "none" || k.Target == "master");
        if (defaultCount < 2)
        {
            _suggestionBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var running = _mixer.GetRunningAudioApps();

        string? commApp = running.FirstOrDefault(a => CommApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)));
        string? musicApp = running.FirstOrDefault(a => MusicApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)));
        string? browserApp = running.FirstOrDefault(a => BrowserApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)));
        string? gameApp = running.FirstOrDefault(a =>
            !CommApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
            !MusicApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
            !BrowserApps.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase)));

        // Need at least 2 known apps
        var knownApps = new[] { commApp, musicApp, browserApp, gameApp }.Where(a => a != null).ToList();
        if (knownApps.Count < 2)
        {
            _suggestionBanner.Visibility = Visibility.Collapsed;
            return;
        }

        // Build banner text
        var appNames = knownApps.Select(a => a!).Take(3).ToList();
        var textBlock = _suggestionBanner.Child is StackPanel sp
            ? sp.Children.OfType<TextBlock>().FirstOrDefault()
            : null;
        if (textBlock != null)
            textBlock.Text = $"✦ We found {string.Join(", ", appNames)} — apply suggested layout?";

        // Store layout for Apply button — use Tag on the banner
        _suggestionBanner.Tag = new SuggestedLayout
        {
            CommApp = commApp,
            MusicApp = musicApp,
            BrowserApp = browserApp,
            GameApp = gameApp,
        };

        _suggestionBanner.Visibility = Visibility.Visible;
    }

    private void ApplySuggestedLayout()
    {
        if (_config == null || _onSave == null || _suggestionBanner?.Tag is not SuggestedLayout layout) return;

        // Priority: 0=master, 1=game, 2=comm, 3=music, 4=browser
        var targets = new[] { "master", layout.GameApp, layout.CommApp, layout.MusicApp, layout.BrowserApp };

        _loading = true;
        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;
            var t = targets[i] ?? "master";
            knob.Target = t;
            knob.Label = t == "master" ? "" : t;
            SelectTarget(_targetPickers[i], t);
            _channelLabels[i].Text = GetDisplayLabel(knob);
            UpdatePickerVisibility(i, t);
        }
        _loading = false;

        _onSave(_config);
    }

    // ── Smart Mix Setup ────────────────────────────────────────────────

    private void SetupSmartMix()
    {
        // Wire up ducking change events
        ChkDuckingEnabled.Checked += OnSmartMixChanged;
        ChkDuckingEnabled.Unchecked += OnSmartMixChanged;
        TxtDuckTriggerApp.TextChanged += OnSmartMixChanged;
        TxtDuckTargetApps.TextChanged += OnSmartMixChanged;
        TxtDuckFadeOut.TextChanged += OnSmartMixChanged;
        TxtDuckFadeIn.TextChanged += OnSmartMixChanged;
        SldDuckPercent.ValueChanged += (_, e) =>
        {
            TxtDuckPercentLabel.Text = $"{(int)e.NewValue}%";
            OnSmartMixChanged(SldDuckPercent, e);
        };

        // Wire up auto-switch change events
        ChkAutoSwitchEnabled.Checked += OnSmartMixChanged;
        ChkAutoSwitchEnabled.Unchecked += OnSmartMixChanged;
        ChkAutoSwitchRevert.Checked += OnSmartMixChanged;
        ChkAutoSwitchRevert.Unchecked += OnSmartMixChanged;

        // Build indexed arrays for auto-switch rule rows
        _autoRuleAppBoxes = new[]
        {
            TxtAutoRule0App, TxtAutoRule1App, TxtAutoRule2App, TxtAutoRule3App, TxtAutoRule4App
        };
        _autoRuleProfileCombos = new[]
        {
            CmbAutoRule0Profile, CmbAutoRule1Profile, CmbAutoRule2Profile, CmbAutoRule3Profile, CmbAutoRule4Profile
        };
        foreach (var tb in _autoRuleAppBoxes)
            tb.TextChanged += OnSmartMixChanged;
        foreach (var cmb in _autoRuleProfileCombos)
            cmb.SelectionChanged += OnSmartMixChanged;
    }

    private void OnSmartMixChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void SmartMixHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _smartMixExpanded = !_smartMixExpanded;
        SmartMixContent.Visibility = _smartMixExpanded ? Visibility.Visible : Visibility.Collapsed;
        SmartMixArrow.Text = _smartMixExpanded ? "▼" : "▶";
    }

    private sealed class SuggestedLayout
    {
        public string? CommApp { get; set; }
        public string? MusicApp { get; set; }
        public string? BrowserApp { get; set; }
        public string? GameApp { get; set; }
    }
}
