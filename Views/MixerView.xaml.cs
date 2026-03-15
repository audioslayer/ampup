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
        { "ha_light", "Home Assistant: Light" },
        { "ha_media", "Home Assistant: Media" },
        { "ha_fan", "Home Assistant: Fan" },
        { "ha_cover", "Home Assistant: Cover" },
        { "apps", "App Group" },
        { "led_brightness", "LED Brightness" },
        { "govee", "Govee" }
    };

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly ChannelGlowControl[] _glowControls = new ChannelGlowControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly Image[] _icons = new Image[5];
    private readonly GridPicker[] _targetPickers = new GridPicker[5];
    private readonly CurvePickerControl[] _curvePickers = new CurvePickerControl[5];
    private readonly RangeSlider[] _rangeSliders = new RangeSlider[5];
    private readonly TextBlock[] _muteLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];

    // Collapsible settings
    private readonly Border[] _settingsBorders = new Border[5];
    private readonly bool[] _settingsExpanded = new bool[5];

    // Suggestion banner
    private Border? _suggestionBanner;

    // Smart Mix (ducking + auto-profile) controls — built in code-behind
    private bool _smartMixExpanded = false;
    private CheckBox? _chkDuckingEnabled;
    private ListPicker? _duckTriggerPicker;
    private StyledSlider? _duckAmountSlider;
    private TextBlock? _duckAmountLabel;
    private TextBox? _duckFadeOutBox;
    private TextBox? _duckFadeInBox;
    private StackPanel? _duckAdvancedPanel;

    private CheckBox? _chkAutoSwitchEnabled;
    private CheckBox? _chkAutoSwitchRevert;
    private StackPanel? _autoSwitchRulesPanel;

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
    private readonly WrapPanel[] _appsListPanels = new WrapPanel[5];


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
        BuildSmartMixSection();
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
                SelectTarget(_targetPickers[idx], knob.Target, knob.DeviceId);
                SelectCurve(_curvePickers[idx], knob.Curve);
                _rangeSliders[idx].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
                _rangeSliders[idx].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);
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

        RebuildTargetPickerItems(config);

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            _channelLabels[i].Text = GetDisplayLabel(knob);
            SelectTarget(_targetPickers[i], knob.Target, knob.DeviceId);
            SelectCurve(_curvePickers[i], knob.Curve);

            _rangeSliders[i].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
            _rangeSliders[i].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);

            UpdatePickerVisibility(i, knob.Target);

            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light != null)
            {
                var color = Color.FromRgb(
                    (byte)Math.Clamp(light.R, 0, 255),
                    (byte)Math.Clamp(light.G, 0, 255),
                    (byte)Math.Clamp(light.B, 0, 255));
                // If LED color is black/too dark, fall back to accent
                if (color.R < 10 && color.G < 10 && color.B < 10)
                    color = ThemeManager.Accent;
                _knobs[i].ArcColor = color;
                _volLabels[i].Foreground = new SolidColorBrush(color);
                _vuMeters[i].BarColor = color;
                _glowControls[i].GlowColor = color;
            }
        }

        // Smart Mix — Ducking
        if (_chkDuckingEnabled != null)
        {
            _chkDuckingEnabled.IsChecked = config.Ducking.Enabled;
            var duckRule = config.Ducking.Rules.Count > 0 ? config.Ducking.Rules[0] : new DuckingRule();

            // Populate trigger picker with the saved app so it appears selected
            if (_duckTriggerPicker != null && !string.IsNullOrEmpty(duckRule.TriggerApp))
            {
                _duckTriggerPicker.ClearItems();
                _duckTriggerPicker.AddItem(duckRule.TriggerApp, tag: duckRule.TriggerApp);
                _duckTriggerPicker.SelectedIndex = 0;
            }

            if (_duckAmountSlider != null)
            {
                _duckAmountSlider.Value = duckRule.DuckPercent;
                if (_duckAmountLabel != null)
                    _duckAmountLabel.Text = $"{duckRule.DuckPercent}%";
            }
            if (_duckFadeOutBox != null) _duckFadeOutBox.Text = duckRule.FadeOutMs.ToString();
            if (_duckFadeInBox != null) _duckFadeInBox.Text = duckRule.FadeInMs.ToString();
        }

        // Smart Mix — Auto-Switch
        if (_chkAutoSwitchEnabled != null)
            _chkAutoSwitchEnabled.IsChecked = config.AutoSwitch.Enabled;
        if (_chkAutoSwitchRevert != null)
            _chkAutoSwitchRevert.IsChecked = config.AutoSwitch.RevertToDefault;
        RebuildAutoSwitchRules();

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
                // Re-select HA targets to resolve friendly names from the entity cache
                if (_config == null) return;
                for (int i = 0; i < 5; i++)
                {
                    var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
                    if (knob == null) continue;
                    var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
                    if (HATargetDomains.ContainsKey(baseTarget))
                        SelectTarget(_targetPickers[i], knob.Target);
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
                _glowControls[i].SetLevel(peak);
                _glowControls[i].Tick();
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
            // Ambient glow behind the knob — audio-reactive, tinted with LED color
            var glow = new ChannelGlowControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(glow, 0);
            _glowControls[i] = glow;
            knobVuGrid.Children.Add(glow); // added first = rendered behind knob

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

            var _clrGreen  = Color.FromRgb(0x66, 0xBB, 0x6A);
            var _clrRed    = Color.FromRgb(0xEF, 0x53, 0x50);
            var _clrBlue   = Color.FromRgb(0x42, 0xA5, 0xF5);
            var _clrTeal   = Color.FromRgb(0x26, 0xC6, 0xDA);
            var _clrPurple = Color.FromRgb(0xAB, 0x47, 0xBC);
            var _clrOrange = Color.FromRgb(0xFF, 0xA7, 0x26);
            var _clrYellow = Color.FromRgb(0xFF, 0xD5, 0x4F);

            targetPicker.AddCategory("Audio");
            targetPicker.AddItem("Master",        "master",        "♪",  _clrGreen);
            targetPicker.AddItem("Mic",           "mic",           "◎",  _clrRed);
            targetPicker.AddItem("System",        "system",        "◆",  _clrBlue);
            targetPicker.AddItem("Any",           "any",           "◈",  _clrTeal);
            targetPicker.AddItem("Active Window", "active_window", "▣",  _clrPurple);

            targetPicker.AddCategory("Devices");
            targetPicker.AddItem("Output Device",  "output_device",  "▶",  _clrPurple);
            targetPicker.AddItem("Input Device",   "input_device",   "◀",  _clrRed);
            targetPicker.AddItem("Monitor",        "monitor",        "▭",  _clrOrange);
            targetPicker.AddItem("LED Brightness", "led_brightness", "◉",  _clrYellow);

            // Integration items (HA / Govee) are added conditionally in RebuildTargetPickerItems
            // called from LoadConfig once we know which integrations are enabled.

            targetPicker.AddCategory("Apps");
            targetPicker.AddItem("Discord",   "discord",  "◉", Color.FromRgb(0x58, 0x65, 0xF2));
            targetPicker.AddItem("Spotify",   "spotify",  "♪", Color.FromRgb(0x1D, 0xB9, 0x54));
            targetPicker.AddItem("Chrome",    "chrome",   "◆", Color.FromRgb(0x42, 0x85, 0xF4));
            targetPicker.AddItem("App Group", "apps",     "▣", _clrTeal);

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

            // App group picker (hidden unless "apps")
            var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
            appsContainer.Children.Add(MakeLabel("APP GROUP"));
            appsContainer.ToolTip = "Click apps to add or remove from this group";

            var appsListPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
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
            var picker = _targetPickers[idx];
            var target = GetSelectedTarget(picker);

            // If a sub-item is selected (HA entity or device), show its friendly name
            if (!string.IsNullOrEmpty(picker.SelectedSubTag))
            {
                if (HATargetDomains.ContainsKey(target))
                {
                    var entity = _haEntities.FirstOrDefault(e => e.EntityId == picker.SelectedSubTag);
                    display.Text = entity?.FriendlyName ?? picker.SelectedSubTag;
                }
                else if (target is "output_device" or "input_device")
                {
                    var device = _audioDevices.FirstOrDefault(d => d.Id == picker.SelectedSubTag);
                    display.Text = device.Name ?? picker.SelectedSubTag;
                }
                else
                {
                    display.Text = picker.SelectedSubTag;
                }
            }
            else
            {
                display.Text = HATargetDisplayNames.TryGetValue(target, out var dn) ? dn : FormatTargetName(target);
            }
        }
    }

    // --- Visibility ---

    private void UpdatePickerVisibility(int idx, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        bool showApps = baseTarget == "apps";
        _appsPanels[idx].Visibility = showApps ? Visibility.Visible : Visibility.Collapsed;

        if (showApps)
        {
            RebuildAppToggles(idx);
        }

        UpdateTargetDisplay(idx);
    }

    private List<GridPicker.SubItem> GetHASubItems(string haTarget)
    {
        if (!HATargetDomains.TryGetValue(haTarget, out var domain))
            return new();

        return _haEntities
            .Where(e => e.Domain == domain)
            .OrderBy(e => e.FriendlyName)
            .Select(e =>
            {
                var (icon, color) = HADomainStyles.GetStyle(e.Domain);
                return new GridPicker.SubItem(e.FriendlyName, e.EntityId, icon, color);
            })
            .ToList();
    }

    private List<GridPicker.SubItem> GetDeviceSubItems(bool isOutput)
    {
        return _audioDevices
            .Where(d => d.IsOutput == isOutput)
            .OrderBy(d => d.Name)
            .Select(d => new GridPicker.SubItem(d.Name, d.Id, isOutput ? "🔊" : "🎙",
                isOutput ? Color.FromRgb(0xB3, 0x88, 0xFF) : Color.FromRgb(0x26, 0xC6, 0xDA)))
            .ToList();
    }

    private List<GridPicker.SubItem> GetGoveeSubItems(AppConfig config)
    {
        var clr = Color.FromRgb(0xFF, 0xB7, 0x4D);
        return config.Ambience.GoveeDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Ip))
            .Select(d =>
            {
                var nameIsIp = d.Name == d.Ip || System.Net.IPAddress.TryParse(d.Name, out _);
                var displayName = !string.IsNullOrWhiteSpace(d.Name) && !nameIsIp ? d.Name
                    : !string.IsNullOrEmpty(d.Sku) ? AmbienceSync.GetProductName(d.Sku)
                    : d.Ip;
                return new GridPicker.SubItem(displayName, d.Ip, "◈", clr);
            })
            .ToList();
    }

    // --- Picker helpers ---

    private void RebuildTargetPickerItems(AppConfig config)
    {
        bool haEnabled = config.HomeAssistant.Enabled;
        bool goveeEnabled = config.Ambience.GoveeEnabled && config.Ambience.GoveeDevices.Count > 0;

        for (int i = 0; i < 5; i++)
        {
            var picker = _targetPickers[i];
            if (picker == null) continue;

            picker.ClearItems();

            var clrGreen  = Color.FromRgb(0x66, 0xBB, 0x6A);
            var clrRed    = Color.FromRgb(0xEF, 0x53, 0x50);
            var clrBlue   = Color.FromRgb(0x42, 0xA5, 0xF5);
            var clrTeal   = Color.FromRgb(0x26, 0xC6, 0xDA);
            var clrPurple = Color.FromRgb(0xAB, 0x47, 0xBC);
            var clrOrange = Color.FromRgb(0xFF, 0xA7, 0x26);
            var clrYellow = Color.FromRgb(0xFF, 0xD5, 0x4F);
            var clrGovee  = Color.FromRgb(0xFF, 0x6F, 0x00);
            var clrHA     = Color.FromRgb(0x26, 0xC6, 0xDA);

            picker.AddCategory("Audio");
            picker.AddItem("Master",        "master",        "♪",  clrGreen);
            picker.AddItem("Mic",           "mic",           "◎",  clrRed);
            picker.AddItem("System",        "system",        "◆",  clrBlue);
            picker.AddItem("Any",           "any",           "◈",  clrTeal);
            picker.AddItem("Active Window", "active_window", "▣",  clrPurple);

            picker.AddCategory("Devices");
            picker.AddItem("Output Device", "output_device", "▶",  clrPurple);
            picker.AddItem("Input Device",  "input_device",  "◀",  clrRed);
            picker.AddItem("Monitor",       "monitor",       "▭",  clrOrange);
            picker.AddItem("LED Brightness","led_brightness","◉",  clrYellow);

            // Register sub-flyout providers for device pickers
            picker.RegisterSubMenu("output_device", () => GetDeviceSubItems(isOutput: true));
            picker.RegisterSubMenu("input_device", () => GetDeviceSubItems(isOutput: false));

            bool hasIntegrations = haEnabled || goveeEnabled;
            if (hasIntegrations)
            {
                picker.AddCategory("Integrations");

                if (haEnabled)
                {
                    picker.AddItem("Home Assistant",  "ha_light",  "◈", clrHA, "Light");
                    picker.AddItem("Home Assistant",  "ha_media",  "♪", clrHA, "Media Player");
                    picker.AddItem("Home Assistant",  "ha_fan",    "◎", clrHA, "Fan");
                    picker.AddItem("Home Assistant",  "ha_cover",  "▭", clrHA, "Cover");

                    // Register sub-flyout providers for HA items
                    foreach (var haKey in HATargetDomains.Keys)
                    {
                        var key = haKey; // capture for closure
                        picker.RegisterSubMenu(key, () => GetHASubItems(key));
                    }
                }

                if (goveeEnabled)
                {
                    picker.AddItem("Govee", "govee", "◈", clrGovee, "Room Lighting");
                    picker.RegisterSubMenu("govee", () => GetGoveeSubItems(config));
                }
            }

            picker.AddCategory("Apps");
            picker.AddItem("Discord",   "discord",  "◉", Color.FromRgb(0x58, 0x65, 0xF2));
            picker.AddItem("Spotify",   "spotify",  "♪", Color.FromRgb(0x1D, 0xB9, 0x54));
            picker.AddItem("Chrome",    "chrome",   "◆", Color.FromRgb(0x42, 0x85, 0xF4));
            picker.AddItem("App Group", "apps",     "▣", clrTeal);
        }
    }

    private void SelectTarget(GridPicker picker, string target, string? deviceId = null)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;

        // HA targets with entity ID — use sub-tag selection
        if (HATargetDomains.ContainsKey(baseTarget) && target.Contains(':'))
        {
            var entityId = target.Substring(baseTarget.Length + 1);
            var entity = _haEntities.FirstOrDefault(e => e.EntityId == entityId);
            var displayName = entity?.FriendlyName ?? entityId;
            picker.SelectByTag(baseTarget, entityId, displayName);
            if (picker.Tag is TextBlock display)
                display.Text = displayName;
            return;
        }

        // Device targets with device ID — use sub-tag selection
        if ((baseTarget == "output_device" || baseTarget == "input_device") && !string.IsNullOrEmpty(deviceId))
        {
            var device = _audioDevices.FirstOrDefault(d => d.Id == deviceId);
            var displayName = device.Name ?? deviceId;
            picker.SelectByTag(baseTarget, deviceId, displayName);
            if (picker.Tag is TextBlock display)
                display.Text = displayName;
            return;
        }

        // Govee target with device IP — use sub-tag selection
        if (baseTarget == "govee" && target.Contains(':'))
        {
            var deviceIp = target.Substring(6); // skip "govee:"
            var goveeDevice = _config?.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == deviceIp);
            var displayName = goveeDevice?.Name ?? deviceIp;
            picker.SelectByTag("govee", deviceIp, displayName);
            return;
        }

        // Try exact match first
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == target)
            {
                picker.SelectedIndex = i;
                if (picker.Tag is TextBlock display)
                    display.Text = HATargetDisplayNames.TryGetValue(baseTarget, out var dn) ? dn : FormatTargetName(baseTarget);
                return;
            }
        }

        // Fallback: base target match
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

        var accent = ThemeManager.Accent;
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

        // Sort: in-group first, then alphabetical
        foreach (var app in allApps.OrderByDescending(a => knob.Apps.Contains(a, StringComparer.OrdinalIgnoreCase)).ThenBy(a => a))
        {
            bool isInGroup = knob.Apps.Contains(app, StringComparer.OrdinalIgnoreCase);
            bool isRunning = runningApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            var appCapture = app;

            // Chip content: dot + name
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (isInGroup && isRunning)
            {
                chipContent.Children.Add(new Border
                {
                    Width = 5, Height = 5,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }

            chipContent.Children.Add(new TextBlock
            {
                Text = app,
                FontSize = 10.5,
                FontStyle = (!isRunning && isInGroup) ? FontStyles.Italic : FontStyles.Normal,
                Foreground = new SolidColorBrush(
                    isInGroup ? Color.FromRgb(0xE8, 0xE8, 0xE8)
                    : isRunning ? Color.FromRgb(0x77, 0x77, 0x77)
                    : Color.FromRgb(0x55, 0x55, 0x55)),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var chip = new Border
            {
                Background = new SolidColorBrush(isInGroup
                    ? Color.FromArgb(0x28, accent.R, accent.G, accent.B)
                    : Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(isInGroup
                    ? Color.FromArgb(0x66, accent.R, accent.G, accent.B)
                    : Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = chipContent,
                ToolTip = isInGroup
                    ? (isRunning ? "Click to remove from group" : "Not running — click to remove")
                    : "Click to add to group",
            };

            // Hover effect
            chip.MouseEnter += (_, _) =>
            {
                chip.Background = new SolidColorBrush(isInGroup
                    ? Color.FromArgb(0x3A, accent.R, accent.G, accent.B)
                    : Color.FromRgb(0x24, 0x24, 0x24));
            };
            chip.MouseLeave += (_, _) =>
            {
                chip.Background = new SolidColorBrush(isInGroup
                    ? Color.FromArgb(0x28, accent.R, accent.G, accent.B)
                    : Color.FromRgb(0x1A, 0x1A, 0x1A));
            };

            // Click to toggle
            chip.MouseLeftButtonUp += (_, e) =>
            {
                if (_loading) return;
                if (knob.Apps.Contains(appCapture, StringComparer.OrdinalIgnoreCase))
                    knob.Apps.RemoveAll(a => a.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
                else
                    knob.Apps.Add(appCapture);
                QueueSave();
                RebuildAppToggles(idx);
                e.Handled = true;
            };

            panel.Children.Add(chip);
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
                var entityId = _targetPickers[i].SelectedSubTag ?? "";
                knob.Target = !string.IsNullOrEmpty(entityId) ? $"{selectedTarget}:{entityId}" : selectedTarget;
            }
            else if (selectedTarget == "govee")
            {
                var deviceIp = _targetPickers[i].SelectedSubTag ?? "";
                knob.Target = !string.IsNullOrEmpty(deviceIp) ? $"govee:{deviceIp}" : "govee";
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

            // Device ID from sub-flyout (output_device / input_device targets)
            if (selectedTarget is "output_device" or "input_device")
                knob.DeviceId = _targetPickers[i].SelectedSubTag ?? "";
            else
                knob.DeviceId = "";
        }

        // Smart Mix — Ducking
        _config.Ducking.Enabled = _chkDuckingEnabled?.IsChecked == true;
        var duckRule = _config.Ducking.Rules.Count > 0 ? _config.Ducking.Rules[0] : new DuckingRule();
        if (_config.Ducking.Rules.Count == 0) _config.Ducking.Rules.Add(duckRule);
        duckRule.TriggerApp = _duckTriggerPicker?.SelectedTag as string ?? "";
        duckRule.TargetApps = new List<string>(); // empty = duck all
        duckRule.DuckPercent = (int)(_duckAmountSlider?.Value ?? 50);
        duckRule.FadeOutMs = int.TryParse(_duckFadeOutBox?.Text, out var fadeOut) ? fadeOut : 200;
        duckRule.FadeInMs = int.TryParse(_duckFadeInBox?.Text, out var fadeIn) ? fadeIn : 500;

        // Smart Mix — Auto-Switch
        _config.AutoSwitch.Enabled = _chkAutoSwitchEnabled?.IsChecked == true;
        _config.AutoSwitch.RevertToDefault = _chkAutoSwitchRevert?.IsChecked == true;
        var switchRules = new List<AutoSwitchRule>();
        if (_autoSwitchRulesPanel != null)
        {
            foreach (var child in _autoSwitchRulesPanel.Children)
            {
                if (child is Border row && row.Child is Grid g)
                {
                    var appPicker = g.Children.OfType<ListPicker>().FirstOrDefault();
                    var profilePicker = g.Children.OfType<ListPicker>().Skip(1).FirstOrDefault();
                    var appName = appPicker?.SelectedTag?.ToString() ?? "";
                    var profileName = profilePicker?.SelectedTag?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(profileName))
                        switchRules.Add(new AutoSwitchRule { ProcessName = appName, ProfileName = profileName });
                }
            }
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
            _rangeSliders[i].AccentColor = accent;
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

    private void BuildSmartMixSection()
    {
        var cardBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        var cardBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        var inputBg = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        var inputBorder = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        var textPrimary = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var textSec = FindBrush("TextSecBrush") ?? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        var accent = ThemeManager.Accent;
        var accentBrush = new SolidColorBrush(accent);

        // ── VOICE DUCKING CARD ─────────────────────────────────────
        var duckCard = new Border
        {
            Background = cardBg,
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var duckStack = new StackPanel();

        // Header: accent bar + title + checkbox
        var duckHeader = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        duckHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        duckHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        duckHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        duckHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var duckBar = new Border
        {
            Width = 3, CornerRadius = new CornerRadius(2),
            Background = accentBrush, Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(duckBar, 0);
        duckHeader.Children.Add(duckBar);

        var duckTitle = new TextBlock
        {
            Text = "VOICE DUCKING",
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(duckTitle, 1);
        duckHeader.Children.Add(duckTitle);

        _chkDuckingEnabled = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Automatically lower other apps when trigger app is speaking",
        };
        Grid.SetColumn(_chkDuckingEnabled, 3);
        duckHeader.Children.Add(_chkDuckingEnabled);

        _sectionHeaders.Add((duckBar, duckTitle));
        duckStack.Children.Add(duckHeader);

        // Content panel (disabled/dimmed when unchecked)
        var duckContent = new StackPanel();

        // "When this app is active:"
        duckContent.Children.Add(new TextBlock
        {
            Text = "When this app is active:",
            Foreground = textSec, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
        });

        // Trigger app ListPicker
        _duckTriggerPicker = new ListPicker
        {
            ToolTip = "App that triggers volume reduction when active",
            Margin = new Thickness(0, 0, 0, 14),
        };
        _duckTriggerPicker.DropdownOpening += (_, _) => PopulateDuckTriggerPicker();
        _duckTriggerPicker.SelectionChanged += OnSmartMixChanged;
        duckContent.Children.Add(_duckTriggerPicker);

        // "Lower other apps by:"
        duckContent.Children.Add(new TextBlock
        {
            Text = "Lower other apps by:",
            Foreground = textSec, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
        });

        // Slider row: StyledSlider + percent label
        var sliderBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var sliderGrid = new Grid();
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _duckAmountSlider = new StyledSlider
        {
            Minimum = 0, Maximum = 100, Value = 50,
            AccentColor = accent,
            ToolTip = "How much to reduce volume (100% = fully muted)",
        };
        _duckAmountSlider.ValueChanged += (_, _) =>
        {
            if (_duckAmountLabel != null)
                _duckAmountLabel.Text = $"{(int)_duckAmountSlider.Value}%";
            OnSmartMixChanged(_duckAmountSlider, EventArgs.Empty);
        };
        Grid.SetColumn(_duckAmountSlider, 0);
        sliderGrid.Children.Add(_duckAmountSlider);

        _duckAmountLabel = new TextBlock
        {
            Text = "50%",
            Foreground = accentBrush,
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(_duckAmountLabel, 1);
        sliderGrid.Children.Add(_duckAmountLabel);

        sliderBorder.Child = sliderGrid;
        duckContent.Children.Add(sliderBorder);

        // "Advanced" toggle
        var advancedToggle = new TextBlock
        {
            Text = "\u25B8 Advanced",
            Foreground = textSec, FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 6),
        };

        _duckAdvancedPanel = new StackPanel { Visibility = Visibility.Collapsed };

        advancedToggle.MouseLeftButtonDown += (_, _) =>
        {
            if (_duckAdvancedPanel!.Visibility == Visibility.Visible)
            {
                _duckAdvancedPanel.Visibility = Visibility.Collapsed;
                advancedToggle.Text = "\u25B8 Advanced";
            }
            else
            {
                _duckAdvancedPanel.Visibility = Visibility.Visible;
                advancedToggle.Text = "\u25BE Advanced";
            }
        };
        duckContent.Children.Add(advancedToggle);

        // Advanced: Fade Out / Fade In
        var fadeGrid = new Grid();
        fadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        fadeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Fade Out
        var fadeOutBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10),
        };
        var fadeOutStack = new StackPanel();
        fadeOutStack.Children.Add(new TextBlock
        {
            Text = "Fade Out", Foreground = textSec, FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
        });
        var fadeOutRow = new Grid();
        fadeOutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fadeOutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _duckFadeOutBox = new TextBox
        {
            Text = "200",
            Background = inputBg, BorderBrush = inputBorder, Foreground = textPrimary,
            FontSize = 12, Padding = new Thickness(6, 4, 6, 4),
            ToolTip = "Time to fade volume down when trigger starts (ms)",
        };
        _duckFadeOutBox.TextChanged += (_, _) => OnSmartMixChanged(_duckFadeOutBox, EventArgs.Empty);
        Grid.SetColumn(_duckFadeOutBox, 0);
        fadeOutRow.Children.Add(_duckFadeOutBox);
        var fadeOutMs = new TextBlock
        {
            Text = "ms", Foreground = textSec, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(fadeOutMs, 1);
        fadeOutRow.Children.Add(fadeOutMs);
        fadeOutStack.Children.Add(fadeOutRow);
        fadeOutBorder.Child = fadeOutStack;
        Grid.SetColumn(fadeOutBorder, 0);
        fadeGrid.Children.Add(fadeOutBorder);

        // Fade In
        var fadeInBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10),
        };
        var fadeInStack = new StackPanel();
        fadeInStack.Children.Add(new TextBlock
        {
            Text = "Fade In", Foreground = textSec, FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
        });
        var fadeInRow = new Grid();
        fadeInRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fadeInRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _duckFadeInBox = new TextBox
        {
            Text = "500",
            Background = inputBg, BorderBrush = inputBorder, Foreground = textPrimary,
            FontSize = 12, Padding = new Thickness(6, 4, 6, 4),
            ToolTip = "Time to restore volume after trigger stops (ms)",
        };
        _duckFadeInBox.TextChanged += (_, _) => OnSmartMixChanged(_duckFadeInBox, EventArgs.Empty);
        Grid.SetColumn(_duckFadeInBox, 0);
        fadeInRow.Children.Add(_duckFadeInBox);
        var fadeInMs = new TextBlock
        {
            Text = "ms", Foreground = textSec, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(fadeInMs, 1);
        fadeInRow.Children.Add(fadeInMs);
        fadeInStack.Children.Add(fadeInRow);
        fadeInBorder.Child = fadeInStack;
        Grid.SetColumn(fadeInBorder, 2);
        fadeGrid.Children.Add(fadeInBorder);

        _duckAdvancedPanel.Children.Add(fadeGrid);
        duckContent.Children.Add(_duckAdvancedPanel);

        // Wire enabled toggle to dim/disable content
        _chkDuckingEnabled.Checked += (_, _) =>
        {
            duckContent.Opacity = 1.0;
            duckContent.IsEnabled = true;
            OnSmartMixChanged(_chkDuckingEnabled, EventArgs.Empty);
        };
        _chkDuckingEnabled.Unchecked += (_, _) =>
        {
            duckContent.Opacity = 0.4;
            duckContent.IsEnabled = false;
            OnSmartMixChanged(_chkDuckingEnabled, EventArgs.Empty);
        };
        // Set initial state
        duckContent.Opacity = 0.4;
        duckContent.IsEnabled = false;

        duckStack.Children.Add(duckContent);
        duckCard.Child = duckStack;
        SmartMixContent.Children.Add(duckCard);

        // ── APP PROFILES CARD ──────────────────────────────────────
        var profileCard = new Border
        {
            Background = cardBg,
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
        };

        var profileStack = new StackPanel();

        // Header: accent bar + title + checkbox
        var profileHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        profileHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profileHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profileHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profileHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var profileBar = new Border
        {
            Width = 3, CornerRadius = new CornerRadius(2),
            Background = accentBrush, Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(profileBar, 0);
        profileHeader.Children.Add(profileBar);

        var profileTitle = new TextBlock
        {
            Text = "APP PROFILES",
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(profileTitle, 1);
        profileHeader.Children.Add(profileTitle);

        _chkAutoSwitchEnabled = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Switch profiles automatically based on the focused app",
        };
        Grid.SetColumn(_chkAutoSwitchEnabled, 3);
        profileHeader.Children.Add(_chkAutoSwitchEnabled);

        _sectionHeaders.Add((profileBar, profileTitle));
        profileStack.Children.Add(profileHeader);

        // Content panel (disabled/dimmed when unchecked)
        var profileContent = new StackPanel();

        // Revert checkbox
        _chkAutoSwitchRevert = new CheckBox
        {
            Content = "Revert to default when no rule matches",
            Foreground = textSec, FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Go back to Default profile when no rule matches the focused app",
        };
        _chkAutoSwitchRevert.Checked += OnSmartMixChanged;
        _chkAutoSwitchRevert.Unchecked += OnSmartMixChanged;
        profileContent.Children.Add(_chkAutoSwitchRevert);

        // Dynamic rules panel
        _autoSwitchRulesPanel = new StackPanel();
        profileContent.Children.Add(_autoSwitchRulesPanel);

        // "Add Rule" button
        var addRuleBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var addRuleText = new TextBlock
        {
            Text = "+ Add Rule",
            Foreground = accentBrush,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        addRuleBtn.Child = addRuleText;
        addRuleBtn.MouseEnter += (_, _) => addRuleBtn.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
        addRuleBtn.MouseLeave += (_, _) => addRuleBtn.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        addRuleBtn.MouseLeftButtonUp += (_, _) =>
        {
            AddAutoSwitchRule("", "");
            OnSmartMixChanged(addRuleBtn, EventArgs.Empty);
        };
        profileContent.Children.Add(addRuleBtn);

        // Wire enabled toggle
        _chkAutoSwitchEnabled.Checked += (_, _) =>
        {
            profileContent.Opacity = 1.0;
            profileContent.IsEnabled = true;
            OnSmartMixChanged(_chkAutoSwitchEnabled, EventArgs.Empty);
        };
        _chkAutoSwitchEnabled.Unchecked += (_, _) =>
        {
            profileContent.Opacity = 0.4;
            profileContent.IsEnabled = false;
            OnSmartMixChanged(_chkAutoSwitchEnabled, EventArgs.Empty);
        };
        profileContent.Opacity = 0.4;
        profileContent.IsEnabled = false;

        profileStack.Children.Add(profileContent);
        profileCard.Child = profileStack;
        SmartMixContent.Children.Add(profileCard);
    }

    private void PopulateDuckTriggerPicker()
    {
        if (_duckTriggerPicker == null || _mixer == null) return;

        var currentTag = _duckTriggerPicker.SelectedTag?.ToString() ?? "";
        _duckTriggerPicker.ClearItems();

        // Common voice apps (always shown)
        var commonApps = new[] { "discord", "teams", "zoom", "slack" };
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get running audio apps
        List<string> running;
        try { running = _mixer.GetRunningAudioApps(); }
        catch { running = new List<string>(); }

        // Add running apps first
        foreach (var app in running)
        {
            _duckTriggerPicker.AddItem(app, tag: app);
            added.Add(app);
        }

        // Add common apps that aren't already listed
        foreach (var app in commonApps)
        {
            if (!added.Contains(app))
            {
                _duckTriggerPicker.AddItem($"{app} (not running)", tag: app);
                added.Add(app);
            }
        }

        // Re-select previous value
        if (!string.IsNullOrEmpty(currentTag))
            SelectPickerByTag(_duckTriggerPicker, currentTag);
    }

    private void AddAutoSwitchRule(string app, string profile)
    {
        if (_autoSwitchRulesPanel == null) return;

        var textSec = FindBrush("TextSecBrush") ?? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        var accent = ThemeManager.Accent;
        var accentBrush = new SolidColorBrush(accent);

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // App ListPicker
        var appPicker = new ListPicker
        {
            ToolTip = "When this app is in the foreground, switch to the profile",
        };
        appPicker.DropdownOpening += (_, _) => PopulateAppPicker(appPicker);
        // Pre-populate with current selection if any
        if (!string.IsNullOrEmpty(app))
        {
            appPicker.AddItem(app, tag: app);
            appPicker.SelectedIndex = 0;
        }
        appPicker.SelectionChanged += OnSmartMixChanged;
        Grid.SetColumn(appPicker, 0);
        grid.Children.Add(appPicker);

        // Arrow
        var arrow = new TextBlock
        {
            Text = "\u2192",
            Foreground = accentBrush,
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        // Profile ListPicker
        var profilePicker = new ListPicker
        {
            ToolTip = "Profile to activate when the app is focused",
        };
        if (_config != null)
        {
            foreach (var p in _config.Profiles)
                profilePicker.AddItem(p, tag: p);
        }
        if (!string.IsNullOrEmpty(profile))
            SelectPickerByTag(profilePicker, profile);
        profilePicker.SelectionChanged += OnSmartMixChanged;
        Grid.SetColumn(profilePicker, 2);
        grid.Children.Add(profilePicker);

        // Delete button
        var deleteBtn = new TextBlock
        {
            Text = "\u00D7",
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 16, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        deleteBtn.MouseEnter += (_, _) => deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        deleteBtn.MouseLeave += (_, _) => deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        deleteBtn.MouseLeftButtonUp += (_, _) =>
        {
            _autoSwitchRulesPanel?.Children.Remove(row);
            OnSmartMixChanged(deleteBtn, EventArgs.Empty);
        };
        Grid.SetColumn(deleteBtn, 3);
        grid.Children.Add(deleteBtn);

        row.Child = grid;
        _autoSwitchRulesPanel.Children.Add(row);
    }

    private void PopulateAppPicker(ListPicker picker)
    {
        if (_mixer == null) return;

        var currentTag = picker.SelectedTag?.ToString() ?? "";
        picker.ClearItems();

        List<string> running;
        try { running = _mixer.GetRunningAudioApps(); }
        catch { running = new List<string>(); }

        foreach (var app in running)
            picker.AddItem(app, tag: app);

        if (!string.IsNullOrEmpty(currentTag))
            SelectPickerByTag(picker, currentTag);
    }

    private void RebuildAutoSwitchRules()
    {
        if (_autoSwitchRulesPanel == null || _config == null) return;
        _autoSwitchRulesPanel.Children.Clear();
        foreach (var rule in _config.AutoSwitch.Rules)
            AddAutoSwitchRule(rule.ProcessName, rule.ProfileName);
    }

    private static void SelectPickerByTag(ListPicker picker, string tag)
    {
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i)?.ToString() == tag)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnSmartMixChanged(object? sender, EventArgs e)
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
