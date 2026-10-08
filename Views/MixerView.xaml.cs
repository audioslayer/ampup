using Material.Icons;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AmpUp.Controls;

namespace AmpUp.Views;

[SupportedOSPlatform("windows7.0")]
public partial class MixerView : UserControl
{
    private AppConfig? _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private bool _configLoaded; // true after first LoadConfig completes

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
        { "ha_light", "Home Assistant light" },
        { "ha_media", "Home Assistant speaker" },
        { "ha_fan", "Home Assistant fan" },
        { "ha_cover", "Home Assistant cover" },
        { "apps", "App group" },
        { "led_brightness", "Turn Up LED brightness" },
        { "govee", "Govee light" },
        { "room_lights", "Room lights" }
    };

    /// <summary>Friendly names for built-in targets — mirror the labels in the TARGET picker.</summary>
    private static readonly Dictionary<string, string> BuiltInTargetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "none", "Nothing" },
        { "master", "Master volume" },
        { "mic", "Microphone" },
        { "system", "System sounds" },
        { "active_window", "Focused app" },
        { "any", "Auto (next playing app)" },
        { "output_device", "Speaker / headset" },
        { "input_device", "Mic device" },
        { "monitor", "Monitor brightness" },
        { "sc_space_cycle", "Switch Spaces" },
        { "sc_page_cycle", "Switch pages" },
    };

    /// <summary>Old auto-generated title-case name ("Active Window", "Master") — used to detect
    /// labels that were persisted from the previous naming so they upgrade to the friendly name.</summary>
    private static string LegacyTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none") return "None";
        var legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ha_light", "Home Assistant: Light" }, { "ha_media", "Home Assistant: Media" },
            { "ha_fan", "Home Assistant: Fan" }, { "ha_cover", "Home Assistant: Cover" },
            { "apps", "App Group" }, { "led_brightness", "LED Brightness" }, { "room_lights", "Room Lights" },
        };
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        if (legacy.TryGetValue(baseTarget, out var l)) return l;
        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0) words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        return string.Join(' ', words);
    }

    // Per-channel control arrays
    private readonly AnimatedKnobControl[] _knobs = new AnimatedKnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly Material.Icons.WPF.MaterialIcon[] _rangeIcons = new Material.Icons.WPF.MaterialIcon[5];
    private readonly ChannelGlowControl[] _glowControls = new ChannelGlowControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly Image[] _icons = new Image[5];
    private readonly GridPicker[] _targetPickers = new GridPicker[5];
    private readonly CurvePickerControl[] _curvePickers = new CurvePickerControl[5];
    private readonly RangeSlider[] _rangeSliders = new RangeSlider[5];
    private readonly TextBlock[] _muteLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];
    private readonly Color[] _displayedColors = new Color[5]; // last-applied UI tint, skips redundant updates

    // Collapsible settings
    private readonly Border[] _settingsBorders = new Border[5];
    private readonly bool[] _settingsExpanded = new bool[5];

    // Suggestion banner
    private Border? _suggestionBanner;

    // Audio Sessions + Smart Mix state live in MixerView.Sessions.cs / MixerView.SmartMix.cs

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
    private bool _haEntityRefreshInFlight;

    public MixerView()
    {
        InitializeComponent();

        MixerPageHeaderHost.Content = UiKit.PageHeader("Mixer", "What each knob controls and how it responds",
            icon: Material.Icons.MaterialIconKind.TuneVertical);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        // Single 75ms timer drives both the channel-strip visuals and the
        // session peak bars (formerly a separate 75ms _peakTimer) — same
        // cadence, one dispatcher wakeup.
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _liveTimer.Tick += LiveTimer_Tick;

        _sessionRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionRefreshTimer.Tick += (_, _) => RefreshSessionList();

        Loaded += (_, _) => { HookOwnerWindow(); StartTimersIfWindowActive(); };
        Unloaded += (_, _) => { UnhookOwnerWindow(); _liveTimer.Stop(); _sessionRefreshTimer.Stop(); };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildChannelControls();
        InitializeMixerDeviceSelector();
        SetupStripHoverEffects();
        SetupStripContextMenus();
        SetupSuggestionBanner();
        BuildSmartMixSection();
        BuildAudioSessionsSection();
    }

    // ── Hidden-window timer quiesce ─────────────────────────────────────
    // Hide-to-tray uses Hide(), which never raises Unloaded — without these
    // window hooks the 75ms/2s timers tick forever with nothing visible.
    // Mirrors the _hwPreviewTimer pattern in MainWindow.

    private Window? _ownerWindow;

    private void HookOwnerWindow()
    {
        var window = Window.GetWindow(this);
        if (window == null || ReferenceEquals(window, _ownerWindow)) return;
        UnhookOwnerWindow(); // guard against duplicate handlers when Loaded re-fires
        _ownerWindow = window;
        window.IsVisibleChanged += OwnerWindow_IsVisibleChanged;
        window.StateChanged += OwnerWindow_StateChanged;
    }

    private void UnhookOwnerWindow()
    {
        if (_ownerWindow == null) return;
        _ownerWindow.IsVisibleChanged -= OwnerWindow_IsVisibleChanged;
        _ownerWindow.StateChanged -= OwnerWindow_StateChanged;
        _ownerWindow = null;
    }

    private void OwnerWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => UpdateTimersForWindowState();

    private void OwnerWindow_StateChanged(object? sender, EventArgs e)
        => UpdateTimersForWindowState();

    private bool IsOwnerWindowQuiesced => _ownerWindow != null
        && (!_ownerWindow.IsVisible || _ownerWindow.WindowState == WindowState.Minimized);

    private void UpdateTimersForWindowState()
    {
        if (IsOwnerWindowQuiesced)
        {
            _liveTimer.Stop();
            _sessionRefreshTimer.Stop();
        }
        else
        {
            StartTimersIfWindowActive();
        }
    }

    private void StartTimersIfWindowActive()
    {
        if (IsOwnerWindowQuiesced) return;
        _liveTimer.Start();
        if (_audioSessionsExpanded && _sessionListPanel != null)
            _sessionRefreshTimer.Start();
    }

    private void SetupStripHoverEffects()
    {
        var borders = new[] { Ch0Border, Ch1Border, Ch2Border, Ch3Border, Ch4Border };
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
                strip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            };
        }
    }

    private void SetupStripContextMenus()
    {
        var borders = new[] { Ch0Border, Ch1Border, Ch2Border, Ch3Border, Ch4Border };

        var menuFg = FindBrush("TextPrimaryBrush");

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var border = borders[i];

            var copyItem = new MenuItem
            {
                Header = "Copy Channel Config",
                Foreground = menuFg,
            };
            copyItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");
            var pasteItem = new MenuItem
            {
                Header = "Paste Channel Config",
                Foreground = menuFg,
            };
            pasteItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");
            var resetItem = new MenuItem
            {
                Header = "Reset to Default",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
            };
            resetItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");

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
                Background = FindBrush("CardBorderBrush"),
                Foreground = FindBrush("CardBorderBrush"),
                Margin = new Thickness(4, 2, 4, 2),
            };

            var contextMenu = new ContextMenu
            {
                BorderThickness = new Thickness(1),
            };
            contextMenu.SetResourceReference(ContextMenu.BackgroundProperty, "CardBgBrush");
            contextMenu.SetResourceReference(ContextMenu.BorderBrushProperty, "CardBorderBrush");

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
        if (_config?.Knobs.FirstOrDefault(k => k.Idx == idx) is { } knob)
        {
            var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
            bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget == "monitor" || baseTarget == "led_brightness";
            // Only update knob arc directly for non-audio targets.
            // Audio targets are driven by LiveTimer_Tick via WASAPI — updating here
            // would fight with the WASAPI-reported volume causing oscillation.
            if (isNonAudio)
            {
                int pct = (int)Math.Round(knob.MinVolume + position * (knob.MaxVolume - knob.MinVolume));
                _knobs[idx].SetTarget(position);
                _knobs[idx].Tick();
                _knobs[idx].PercentText = $"{pct}%";
                _volLabels[idx].Text = $"{pct}%";
            }
        }
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onConfigChanged)
    {
        _loading = true;
        _config = config;
        _mixer = mixer;
        _onSave = onConfigChanged;

        _audioDevices = mixer.GetAudioDevices();
        LoadStreamControllerMixerConfig(config);

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
                var color = EnsureMinBrightness(Color.FromRgb(
                    (byte)Math.Clamp(light.R, 0, 255),
                    (byte)Math.Clamp(light.G, 0, 255),
                    (byte)Math.Clamp(light.B, 0, 255)));
                _knobs[i].ArcColor = color;
                _displayedColors[i] = color;
                _volLabels[i].Foreground = new SolidColorBrush(color);
                _vuMeters[i].BarColor = color;
                _glowControls[i].GlowColor = color;
            }
        }

        LoadSmartMixConfig(config);

        _loading = false;
        _configLoaded = true;

        if (_ha != null)
            _ = FetchHAEntitiesAsync();

        CheckAndShowSuggestionBanner();

        StartTimersIfWindowActive();
    }

    /// <summary>Refreshes endpoint-backed target choices without reloading the whole view.</summary>
    public void RefreshAudioDevices()
    {
        if (_mixer == null || _config == null) return;

        _loading = true;
        try
        {
            _audioDevices = _mixer.GetAudioDevices();
            RebuildTargetPickerItems(_config);
            for (int i = 0; i < 5; i++)
            {
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (knob != null)
                    SelectTarget(_targetPickers[i], knob.Target, knob.DeviceId);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task FetchHAEntitiesAsync()
    {
        if (_ha == null) return;
        if (_haEntityRefreshInFlight) return;

        try
        {
            _haEntityRefreshInFlight = true;
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

                for (int i = 0; i < ScChannelCount; i++)
                {
                    var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == i);
                    if (knob == null) continue;
                    var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
                    if (HATargetDomains.ContainsKey(baseTarget))
                        SelectTarget(_scTargetPickers[i], knob.Target);
                }

                foreach (var picker in _targetPickers.Concat(_scTargetPickers).Where(p => p != null))
                    picker.RefreshOpenSubMenu();
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"MixerView HA fetch: {ex.Message}");
        }
        finally
        {
            _haEntityRefreshInFlight = false;
        }
    }

    private void LiveTimer_Tick(object? sender, EventArgs e)
    {
        // IsVisible stays true when minimized (minimize changes WindowState, not Visibility)
        if (!IsVisible) return;
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) return;
        if (_mixer == null || _config == null) return;
        var config = _config; // local copy suppresses CS8602

        // Session peak bars share this 75ms timer (merged from the old _peakTimer)
        if (_audioSessionsExpanded && _sessionRows.Count > 0)
            UpdateSessionPeaks();

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
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
                    // If WASAPI returns 0 or -1 (no active sessions / own window), show hardware knob position
                    if (vol <= 0f)
                        vol = App.KnobPositions[i];
                    // WASAPI peak is 0.0–1.0 but typical audio sits around 0.2–0.4.
                    // 3x boost so normal listening hits the upper segments.
                    peak = Math.Min(_mixer.GetPeakLevel(knob) * 2.3f, 1f);
                }

                _knobs[i].SetTarget(vol);
                _knobs[i].Tick();
                int pct = (int)Math.Round(vol * 100);
                _knobs[i].PercentText = $"{pct}%";
                _volLabels[i].Text = $"{pct}%";

                _vuMeters[i].Level = peak;
                _vuMeters[i].Tick();
                _glowControls[i].SetLevel(peak);
                _glowControls[i].Tick();

                // Sync UI tint to the knob's static primary LED color — no longer
                // follows the live effect color, so Rainbow/Fire/Pulse won't flash
                // the knob arc, VU meter, channel glow, or percent label.
                // (Channel glow intensity still responds to audio via SetLevel above.)
                var light = _config?.Lights?.FirstOrDefault(l => l.Idx == i);
                if (light != null)
                {
                    byte cr = (byte)Math.Clamp(light.R, 0, 255);
                    byte cg = (byte)Math.Clamp(light.G, 0, 255);
                    byte cb = (byte)Math.Clamp(light.B, 0, 255);
                    // Fall back to accent if the user hasn't picked a color
                    if (cr == 0 && cg == 0 && cb == 0)
                    {
                        cr = ThemeManager.Accent.R;
                        cg = ThemeManager.Accent.G;
                        cb = ThemeManager.Accent.B;
                    }

                    var target = EnsureMinBrightness(Color.FromRgb(cr, cg, cb));
                    if (target != _displayedColors[i])
                    {
                        _displayedColors[i] = target;
                        _knobs[i].ArcColor = target;
                        _vuMeters[i].BarColor = target;
                        _glowControls[i].GlowColor = target;
                        var brush = new SolidColorBrush(target);
                        brush.Freeze();
                        _volLabels[i].Foreground = brush;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"LiveTimer ch{i}: {ex.Message}");
            }
        }

        UpdateStreamControllerMixerLiveState();
        TickStreamControllerMixer();
    }

    private void BuildChannelControls()
    {
        var panels = new[] { Ch0Panel, Ch1Panel, Ch2Panel, Ch3Panel, Ch4Panel };
        var glows = new[] { Ch0Glow, Ch1Glow, Ch2Glow, Ch3Glow, Ch4Glow };
        for (int g = 0; g < 5; g++) _glowControls[g] = glows[g];

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
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 6),
                Visibility = Visibility.Collapsed
            };
            iconContainer.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
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
                CaretBrush = FindBrush("AccentBrush"),
                SelectionBrush = FindBrush("AccentDimBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 0, 2),
                MaxLength = 20,
                Cursor = System.Windows.Input.Cursors.IBeam,
                ToolTip = "Click to rename this channel",
            };
            label.GotFocus += (_, _) =>
            {
                label.Background = FindBrush("InputBgBrush");
                label.BorderThickness = new Thickness(0, 0, 0, 1);
                label.BorderBrush = FindBrush("AccentBrush");
                label.SelectAll();
            };
            label.LostFocus += (_, _) =>
            {
                label.Background = Brushes.Transparent;
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
            // Volume % with a small range icon to its right. The range editor is rarely used,
            // so it stays hidden inside the card until this icon is clicked.
            var volRow = new Grid();
            volRow.Children.Add(volLabel);
            var rangeIcon = new Material.Icons.WPF.MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.ArrowExpandVertical,
                Width = 15, Height = 15,
            };
            var rangeBtn = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Volume range: limit how low or high this knob goes",
                Child = rangeIcon,
            };
            rangeBtn.MouseEnter += (_, _) => rangeBtn.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            rangeBtn.MouseLeave += (_, _) => rangeBtn.Background = Brushes.Transparent;
            _rangeIcons[i] = rangeIcon;
            volRow.Children.Add(rangeBtn);
            panel.Children.Add(volRow);

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

            var targetCells = new[] { Ch0Target, Ch1Target, Ch2Target, Ch3Target, Ch4Target };
            var curveCells = new[] { Ch0Curve, Ch1Curve, Ch2Curve, Ch3Curve, Ch4Curve };
            var rangeCells = new[] { Ch0Range, Ch1Range, Ch2Range, Ch3Range, Ch4Range };
            _settingsExpanded[i] = true;

            // ── Settings content ──

            // TARGET — GridPicker with categories
            var targetPicker = new GridPicker
            {
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "What this knob controls",
            };

            targetPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var selected = GetSelectedTarget(_targetPickers[idx]);
                UpdatePickerVisibility(idx, selected);
                UpdateTargetDisplay(idx);
                QueueSave();
            };
            _targetPickers[i] = targetPicker;

            // Store reference to update target display
            targetPicker.Tag = targetDisplay;

            // App group picker (hidden unless "apps")
            var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
            appsContainer.Children.Add(MakeLabel("APP GROUP"));
            appsContainer.ToolTip = "Click apps to add or remove from this group";

            var appsListPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _appsListPanels[i] = appsListPanel;
            appsContainer.Children.Add(appsListPanel);

            _appsPanels[i] = appsContainer;

            // Target sits inside the knob card (no separate card / header) — it's the knob's
            // main setting, and a separate card wasted a whole row of space.
            var targetStack = new StackPanel();
            targetStack.Children.Add(targetPicker);
            targetStack.Children.Add(appsContainer);
            targetCells[i].Child = targetStack;

            // CURVE — CurvePickerControl (visual mini graphs)
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
            curveCells[i].Child = MakeSectionCard("CURVE", curvePicker);

            // VOLUME RANGE
            var rangeSlider = new RangeSlider
            {
                Minimum = 0,
                Maximum = 100,
                LowerValue = 0,
                UpperValue = 100,
                Height = 28,
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

            var minLabel = new TextBlock { Text = "0%", FontSize = 10 };
            minLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            var maxLabel = new TextBlock { Text = "100%", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
            maxLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            rangeSlider.LowerValueChanged += (_, _) => minLabel.Text = $"{(int)rangeSlider.LowerValue}%";
            rangeSlider.UpperValueChanged += (_, _) => maxLabel.Text = $"{(int)rangeSlider.UpperValue}%";

            var labelsRow = new Grid();
            labelsRow.Children.Add(minLabel);
            labelsRow.Children.Add(maxLabel);

            // VOLUME RANGE — same section card as TARGET / CURVE so all rows line up.
            // Volume range lives inside the knob card, collapsed until the range icon is clicked.
            var rangeStack = new StackPanel();
            rangeStack.Children.Add(MakeLabel("VOLUME RANGE"));
            rangeStack.Children.Add(rangeSlider);
            rangeStack.Children.Add(labelsRow);
            rangeCells[i].Child = rangeStack;
            var rangeCell = rangeCells[i];
            if (_rangeIcons[i].Parent is Border rb)
                rb.MouseLeftButtonUp += (_, e) =>
                {
                    rangeCell.Visibility = rangeCell.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    UpdateRangeIcon(idx);
                    e.Handled = true;
                };
            rangeSlider.LowerValueChanged += (_, _) => UpdateRangeIcon(idx);
            rangeSlider.UpperValueChanged += (_, _) => UpdateRangeIcon(idx);
            UpdateRangeIcon(idx);

        }
    }

    /// <summary>Range icon is accent-coloured when the editor is open or a custom range is set.</summary>
    private void UpdateRangeIcon(int idx)
    {
        var icon = _rangeIcons[idx];
        var slider = _rangeSliders[idx];
        if (icon == null) return;
        bool custom = slider != null && (slider.LowerValue > 0 || slider.UpperValue < 100);
        var cells = new[] { Ch0Range, Ch1Range, Ch2Range, Ch3Range, Ch4Range };
        bool open = cells[idx].Visibility == Visibility.Visible;
        if (custom || open) icon.Foreground = new SolidColorBrush(ThemeManager.Accent);
        else icon.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        if (icon.Parent is FrameworkElement btn)
            btn.ToolTip = custom && slider != null
                ? $"Volume range {(int)slider.LowerValue}% to {(int)slider.UpperValue}% (click to edit)"
                : "Volume range: limit how low or high this knob goes";
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
                    display.Text = device.Name ?? (target == "input_device" ? "Disconnected mic" : "Disconnected speaker");
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

        var items = _haEntities
            .Where(e => e.Domain == domain)
            .OrderBy(e => e.FriendlyName)
            .Select(e =>
            {
                var (_, color) = HADomainStyles.GetStyle(e.Domain);
                return new GridPicker.SubItem(e.FriendlyName, e.EntityId, null, color, HADomainKind(e.Domain), e.EntityId);
            })
            .ToList();

        if (items.Count == 0 && _ha != null && !_haEntityRefreshInFlight)
            _ = FetchHAEntitiesAsync();

        return items;
    }

    private static MaterialIconKind HADomainKind(string domain) => domain switch
    {
        "light" => MaterialIconKind.Lightbulb,
        "media_player" => MaterialIconKind.CastAudio,
        "fan" => MaterialIconKind.Fan,
        "cover" => MaterialIconKind.WindowShutter,
        _ => MaterialIconKind.HomeAssistant,
    };

    private List<GridPicker.SubItem> GetDeviceSubItems(bool isOutput)
    {
        return _audioDevices
            .Where(d => d.IsOutput == isOutput)
            .OrderBy(d => d.Name)
            .Select(d => new GridPicker.SubItem(d.Name, d.Id, null,
                isOutput ? Color.FromRgb(0xAB, 0x47, 0xBC) : Color.FromRgb(0xEF, 0x53, 0x50),
                isOutput ? MaterialIconKind.Speaker : MaterialIconKind.MicrophoneVariant))
            .ToList();
    }

    private List<GridPicker.SubItem> GetMonitorSubItems()
    {
        var clr = Color.FromRgb(0xFF, 0xA7, 0x26); // orange
        var items = new List<GridPicker.SubItem>
        {
            new("All monitors", "", null, clr, MaterialIconKind.MonitorMultiple)
        };
        foreach (var mon in MonitorBrightness.GetMonitorInfos())
        {
            items.Add(new GridPicker.SubItem(mon.FriendlyName, mon.DeviceName, null, clr, MaterialIconKind.Monitor));
        }
        return items;
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
                return new GridPicker.SubItem(displayName, d.Ip, null, clr, MaterialIconKind.LedStripVariant);
            })
            .ToList();
    }

    // --- Picker helpers ---

    private void RebuildTargetPickerItems(AppConfig config)
    {
        for (int i = 0; i < 5; i++)
        {
            var picker = _targetPickers[i];
            if (picker == null) continue;
            PopulateTargetPickerItems(picker, config, includeN3Nav: false);
        }
    }

    /// <summary>
    /// Populate a single target picker with the full catalogue: Audio,
    /// Devices, conditional Integrations (HA/Groups/Room Lights/Govee/VM/
    /// Corsair), Apps (+ App Group), and optionally N3-only navigation
    /// targets (Cycle Spaces / Cycle Pages). Shared between the Turn Up
    /// mixer and the Stream Controller mixer so both surfaces stay in sync.
    /// </summary>
    internal void PopulateTargetPickerItems(GridPicker picker, AppConfig config, bool includeN3Nav)
    {
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

        bool haEnabled = config.HomeAssistant.Enabled;
        bool goveeEnabled = config.Ambience.GoveeEnabled && config.Ambience.GoveeDevices.Count > 0;
        bool vmEnabled = config.VoiceMeeter.Enabled;
        bool corsairEnabled = config.Corsair.Enabled && config.Corsair.FanEnabled;
        bool hasIntegrations = haEnabled || goveeEnabled || vmEnabled || corsairEnabled;

        var clrGrey = Color.FromRgb(0x88, 0x88, 0x88);
        var clrRoom = Color.FromRgb(0x69, 0xF0, 0xAE);
        var clrVM = Color.FromRgb(0xFF, 0x8F, 0x00);
        var clrCorsair = Color.FromRgb(0xFF, 0xD3, 0x00);

        // ── Audio ──
        picker.AddCategory("Audio");
        picker.AddItem("Nothing",       "none",   MaterialIconKind.CircleOffOutline, clrGrey,  "Knob does nothing", keywords: "none off disabled");
        picker.AddItem("Master volume", "master", MaterialIconKind.VolumeHigh,       clrGreen, "Windows main volume", keywords: "master speakers output");
        picker.AddItem("Microphone",    "mic",    MaterialIconKind.Microphone,       clrRed,   "Default mic input level", keywords: "mic input");
        picker.AddItem("System sounds", "system", MaterialIconKind.BellRing,         clrBlue,  "Windows notification sounds", keywords: "system alerts");

        // ── Apps ──
        picker.AddCategory("Apps");
        picker.AddItem("Focused app", "active_window", MaterialIconKind.CursorDefaultClick, clrPurple,
            "Whatever app window is in front", keywords: "active window foreground");
        picker.AddItem("Auto (next playing app)", "any", MaterialIconKind.AutoFix, clrTeal,
            "First app playing audio that isn't on another knob", keywords: "any automatic");
        picker.AddItem("App group", "apps", MaterialIconKind.Apps, clrTeal,
            "Several apps together on one knob", keywords: "apps multiple group");
        picker.AddItem("Discord", "discord", MaterialIconKind.Headset, Color.FromRgb(0x58, 0x65, 0xF2),
            "Discord voice and app audio", GetAppIcon("discord"));
        picker.AddItem("Spotify", "spotify", MaterialIconKind.Music, Color.FromRgb(0x1D, 0xB9, 0x54),
            "Spotify music", GetAppIcon("spotify"));
        picker.AddItem("Chrome", "chrome", MaterialIconKind.Web, Color.FromRgb(0x42, 0x85, 0xF4),
            "Google Chrome tabs", GetAppIcon("chrome"));
        picker.RegisterDynamicItems("Apps", () =>
        {
            var apps = _mixer?.GetRunningAudioApps() ?? new List<string>();
            return apps
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => new GridPicker.SubItem(FormatTargetName(a), a, null, clrTeal,
                    MaterialIconKind.Application, "Playing audio now", GetAppIcon(a)))
                .ToList();
        });

        // ── Devices ──
        picker.AddCategory("Devices");
        picker.AddItem("Speaker / headset", "output_device", MaterialIconKind.Speaker, clrPurple,
            "Volume of one specific output device", keywords: "output device headphones");
        picker.AddItem("Mic device", "input_device", MaterialIconKind.MicrophoneVariant, clrRed,
            "Level of one specific input device", keywords: "input device microphone");
        picker.AddItem("Monitor brightness", "monitor", MaterialIconKind.MonitorShimmer, clrOrange,
            "Screen brightness over DDC/CI", keywords: "display screen");
        picker.AddItem("Turn Up LED brightness", "led_brightness", MaterialIconKind.Brightness6, clrYellow,
            "Brightness of the knob lights", keywords: "led rgb lights");

        picker.RegisterSubMenu("output_device", () => GetDeviceSubItems(isOutput: true));
        picker.RegisterSubMenu("input_device", () => GetDeviceSubItems(isOutput: false));
        picker.RegisterMultiSelectSubMenu("monitor", () => GetMonitorSubItems());

        // ── Lights & Integrations ──
        bool hasGroups = config.Groups.Count > 0;
        if (hasIntegrations || hasGroups)
        {
            picker.AddCategory("Lights & Integrations");

            if (goveeEnabled || corsairEnabled)
                picker.AddItem("Room lights", "room_lights", MaterialIconKind.HomeLightbulbOutline, clrRoom,
                    "Brightness of all your room lights", keywords: "room brightness govee corsair");

            if (hasGroups)
            {
                picker.AddGroup("Groups", "groups", MaterialIconKind.LightbulbGroup, clrRoom, "Your device groups");
                foreach (var group in config.Groups)
                {
                    var groupColor = clrRoom;
                    try { groupColor = (Color)ColorConverter.ConvertFromString(group.Color); } catch { }
                    picker.AddItem(group.Name, $"group:{group.Name}", MaterialIconKind.LightbulbGroup, groupColor,
                        "Group brightness", "groups");
                }
            }

            if (goveeEnabled)
            {
                picker.AddItem("Govee light", "govee", MaterialIconKind.LedStripVariant, clrGovee,
                    "Brightness of one Govee light");
                picker.RegisterSubMenu("govee", () => GetGoveeSubItems(config));
            }

            if (haEnabled)
            {
                picker.AddItem("Home Assistant light", "ha_light", MaterialIconKind.Lightbulb, clrHA, "Dim a smart light", keywords: "home assistant ha");
                picker.AddItem("Home Assistant speaker", "ha_media", MaterialIconKind.CastAudio, clrHA, "Volume of a media player", keywords: "home assistant ha media");
                picker.AddItem("Home Assistant fan", "ha_fan", MaterialIconKind.Fan, clrHA, "Fan speed", keywords: "home assistant ha");
                picker.AddItem("Home Assistant blinds", "ha_cover", MaterialIconKind.WindowShutter, clrHA, "Open or close a cover", keywords: "home assistant ha cover");
                foreach (var haKey in HATargetDomains.Keys)
                {
                    var key = haKey;
                    picker.RegisterSubMenu(key, () => GetHASubItems(key));
                }
            }

            if (vmEnabled)
            {
                picker.AddGroup("VoiceMeeter", "voicemeeter", MaterialIconKind.TuneVertical, clrVM, "Strip and bus gain");
                for (int s = 0; s < 8; s++)
                    picker.AddItem($"VoiceMeeter strip {s + 1}", $"vm_strip:{s}", MaterialIconKind.TuneVertical, clrVM, "Input strip gain", "voicemeeter");
                for (int b = 0; b < 8; b++)
                    picker.AddItem($"VoiceMeeter bus {b + 1}", $"vm_bus:{b}", MaterialIconKind.SpeakerMultiple, clrVM, "Output bus gain", "voicemeeter");
            }

            if (corsairEnabled)
            {
                picker.AddItem("Corsair pump fan", "corsair_pump_fan", MaterialIconKind.WaterPump, clrCorsair, "Pump fan speed");
                picker.AddItem("Corsair case fans", "corsair_case_fan", MaterialIconKind.Fan, clrCorsair, "Case fan speed");
            }
        }

        if (includeN3Nav)
        {
            picker.AddCategory("Stream Controller");
            picker.AddItem("Switch Spaces", "sc_space_cycle", MaterialIconKind.ViewDashboardOutline, clrTeal,
                "Twist to move between Home and your Spaces", keywords: "cycle spaces folders");
            picker.AddItem("Switch pages", "sc_page_cycle", MaterialIconKind.BookOpenPageVariantOutline, clrOrange,
                "Twist to flip pages in the current Space", keywords: "cycle pages");
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

        // Monitor target with device ID(s) — multi-select sub-tag
        if (baseTarget == "monitor" && !string.IsNullOrEmpty(deviceId))
        {
            var monitorIds = deviceId.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var monInfos = MonitorBrightness.GetMonitorInfos();
            string displayName;
            if (monitorIds.Length == 1)
            {
                var mon = monInfos.FirstOrDefault(m => m.DeviceName.Equals(monitorIds[0], StringComparison.OrdinalIgnoreCase));
                displayName = mon?.FriendlyName ?? monitorIds[0];
            }
            else
            {
                displayName = $"{monitorIds.Length} Monitors";
            }
            picker.SelectByTags("monitor", monitorIds, displayName);
            if (picker.Tag is TextBlock monDisplay)
                monDisplay.Text = displayName;
            return;
        }

        // Device targets with device ID — use sub-tag selection
        if ((baseTarget == "output_device" || baseTarget == "input_device") && !string.IsNullOrEmpty(deviceId))
        {
            var device = _audioDevices.FirstOrDefault(d => d.Id == deviceId);
            var displayName = device.Name ?? (baseTarget == "input_device" ? "Disconnected mic" : "Disconnected speaker");
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
        picker.SelectedIndex = picker.AddItemToCategory("Apps", FormatTargetName(target), target,
            MaterialIconKind.Application, Color.FromRgb(0x26, 0xC6, 0xDA), "Custom app", GetAppIcon(target));
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
        if (_config == null || _mixer == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;
        RebuildAppTogglesFor(_appsListPanels[idx], knob, () => RebuildAppToggles(idx));
    }

    /// <summary>
    /// Shared chip-list renderer used by both the Turn Up mixer and the
    /// Stream Controller mixer. Writes directly to <paramref name="knob"/>.Apps
    /// and calls <paramref name="rebuildCallback"/> so the caller can
    /// re-render after a toggle.
    /// </summary>
    internal void RebuildAppTogglesFor(WrapPanel panel, KnobConfig knob, Action rebuildCallback)
    {
        // "Icon stack" style (user's pick): overlapping app icons + "Spotify + 2 more" + Edit.
        // Membership is edited through a glass menu instead of clicking toggle chips.
        panel.Children.Clear();
        if (_config == null || _mixer == null) return;

        var runningApps = _mixer.GetRunningAudioApps();
        var members = knob.Apps.ToList();

        var row = new Grid { Margin = new Thickness(0, 2, 0, 4), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        const int maxIcons = 5;
        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Math.Min(members.Count, maxIcons); i++)
        {
            var app = members[i];
            bool running = runningApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            stack.Children.Add(StackIcon(app, running, i == 0));
        }
        if (members.Count > maxIcons)
        {
            var more = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2),
                Margin = new Thickness(-8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = $"+{members.Count - maxIcons}", FontSize = 10.5, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            more.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            more.SetResourceReference(Border.BorderBrushProperty, "CardBgBrush");
            ((TextBlock)more.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
            stack.Children.Add(more);
        }
        if (members.Count > 0) row.Children.Add(stack);

        var names = new TextBlock
        {
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(members.Count > 0 ? 10 : 0, 0, 8, 0),
        };
        names.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        if (members.Count == 0)
            names.Text = "No apps yet — click Edit to add some";
        else
        {
            var first = new System.Windows.Documents.Run(FormatTargetName(members[0])) { FontWeight = FontWeights.SemiBold };
            first.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
            names.Inlines.Add(first);
            if (members.Count > 1) names.Inlines.Add(new System.Windows.Documents.Run($" + {members.Count - 1} more"));
        }
        names.ToolTip = members.Count > 0 ? string.Join(", ", members.Select(FormatTargetName)) : null;
        Grid.SetColumn(names, 1);
        row.Children.Add(names);

        var edit = new TextBlock
        {
            Text = "Edit", FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
        };
        Grid.SetColumn(edit, 2);
        row.Children.Add(edit);

        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_loading) return;
            ShowAppGroupMenu(row, knob, runningApps, rebuildCallback);
            e.Handled = true;
        };
        panel.Children.Add(row);
    }

    private FrameworkElement StackIcon(string app, bool running, bool first)
    {
        var icon = GetAppIcon(app); // disk cache covers apps that aren't running
        var tile = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2),
            Margin = new Thickness(first ? 0 : -8, 0, 0, 0),
            ToolTip = FormatTargetName(app) + (running ? "" : " · not running"),
            Opacity = running ? 1.0 : 0.55,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBgBrush");
        if (icon != null)
        {
            var img = new Image { Source = icon, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            tile.Child = img;
        }
        else
        {
            var mi = new Material.Icons.WPF.MaterialIcon { Kind = Material.Icons.MaterialIconKind.Application, Width = 15, Height = 15 };
            mi.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
            tile.Child = mi;
        }
        // Lift on hover so overlapped icons can be identified.
        tile.RenderTransform = new TranslateTransform();
        tile.MouseEnter += (_, _) => { ((TranslateTransform)tile.RenderTransform).Y = -3; Panel.SetZIndex(tile, 2); };
        tile.MouseLeave += (_, _) => { ((TranslateTransform)tile.RenderTransform).Y = 0; Panel.SetZIndex(tile, 0); };
        return tile;
    }

    private void ShowAppGroupMenu(FrameworkElement anchor, KnobConfig knob, IReadOnlyCollection<string> runningApps, Action rebuildCallback)
    {
        void Changed() { QueueSave(); rebuildCallback(); }
        var items = new List<GlassMenuItem>();
        foreach (var app in knob.Apps.ToList())
        {
            var a = app;
            items.Add(new GlassMenuItem(FormatTargetName(a) + (runningApps.Contains(a, StringComparer.OrdinalIgnoreCase) ? "" : "  (not running)"),
                null, () => { knob.Apps.RemoveAll(x => x.Equals(a, StringComparison.OrdinalIgnoreCase)); Changed(); }, IsChecked: true));
        }
        var others = runningApps.Where(r => !knob.Apps.Contains(r, StringComparer.OrdinalIgnoreCase)).OrderBy(r => r).ToList();
        if (items.Count > 0 && others.Count > 0) items.Add(GlassMenuItem.Sep);
        foreach (var app in others)
        {
            var a = app;
            items.Add(new GlassMenuItem(FormatTargetName(a), Material.Icons.MaterialIconKind.Plus, () => { knob.Apps.Add(a); Changed(); }));
        }
        if (items.Count > 0) items.Add(GlassMenuItem.Sep);
        items.Add(new GlassMenuItem("Other app…", Material.Icons.MaterialIconKind.Magnify, () =>
        {
            var choices = runningApps
                .Where(r => !knob.Apps.Contains(r, StringComparer.OrdinalIgnoreCase))
                .Select(r => new AppChooser.Choice(r, FormatTargetName(r), "Playing audio now", GetAppIcon(r)))
                .ToList();
            AppChooser.Show(anchor, choices, picked =>
            {
                if (!knob.Apps.Contains(picked, StringComparer.OrdinalIgnoreCase)) knob.Apps.Add(picked);
                Changed();
            });
        }));
        GlassContextMenuHost.Show(anchor, items);
    }

    // --- Save ---

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || !_configLoaded) return;

        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            knob.Label = _channelLabels[i].Text.Trim();
            var selectedTarget = GetSelectedTarget(_targetPickers[i]);

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

            // Device ID from sub-flyout (output_device / input_device / monitor targets)
            if (selectedTarget is "output_device" or "input_device")
                knob.DeviceId = _targetPickers[i].SelectedSubTag ?? "";
            else if (selectedTarget is "monitor")
            {
                // Multi-select: join checked monitor device names with semicolons
                var tags = _targetPickers[i].SelectedSubTags;
                knob.DeviceId = tags.Count > 0 ? string.Join(";", tags) : "";
            }
            else
                knob.DeviceId = "";
        }

        CollectSmartMixConfig(_config);

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
        RefreshSmartMixAccent();
        RefreshSessionAccent();

        // Re-apply LED/accent colors to knobs, VU meters, and glow
        if (_config != null)
        {
            for (int i = 0; i < 5; i++)
            {
                var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
                Color color = accent;
                if (light != null)
                {
                    color = EnsureMinBrightness(Color.FromRgb(
                        (byte)Math.Clamp(light.R, 0, 255),
                        (byte)Math.Clamp(light.G, 0, 255),
                        (byte)Math.Clamp(light.B, 0, 255)));
                }
                _knobs[i].ArcColor = color;
                _displayedColors[i] = color;
                _volLabels[i].Foreground = new SolidColorBrush(color);
                _vuMeters[i].BarColor = color;
                _glowControls[i].GlowColor = color;
            }
        }
    }

    private Border MakeSectionCard(string title, params UIElement[] children)
    {
        var content = new StackPanel();
        content.Children.Add(MakeSectionHeader(title));
        foreach (var child in children)
            content.Children.Add(child);
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return border;
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
        if (!string.IsNullOrWhiteSpace(knob.Label)
            && !string.Equals(knob.Label.Trim(), LegacyTargetName(knob.Target), StringComparison.OrdinalIgnoreCase))
            return knob.Label;
        return FormatTargetName(knob.Target);
    }

    // Cache app icons so we don't re-extract from exe on every rebuild
    private static readonly Dictionary<string, BitmapSource?> _appIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Try to get the app icon from a running process by name. Cached.
    /// </summary>
    internal static BitmapSource? GetAppIcon(string processName)
    {
        if (_appIconCache.TryGetValue(processName, out var cached) && cached != null)
            return cached;

        BitmapSource? icon = null;
        try
        {
            var procs = Process.GetProcessesByName(processName);
            try
            {
                if (procs.Length > 0)
                {
                    var exePath = procs[0].MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (sysIcon != null)
                        {
                            icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                sysIcon.Handle, Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            icon.Freeze();
                            sysIcon.Dispose();
                        }
                    }
                }
            }
            finally
            {
                foreach (var process in procs) process.Dispose();
            }
        }
        catch { }

        // Icons can only be read from a running process, so remember them on disk. A closed
        // app (e.g. Spotify in an app group) still shows its real icon later. Nulls are not
        // cached, so an app that starts later picks up its icon.
        if (icon != null) SaveIconToDisk(processName, icon);
        else icon = LoadIconFromDisk(processName);
        if (icon != null) _appIconCache[processName] = icon;
        return icon;
    }

    private static string IconCachePath(string processName)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(processName.Select(c => bad.Contains(c) ? '_' : c).ToArray()).ToLowerInvariant();
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp", "icons", safe + ".png");
    }

    private static void SaveIconToDisk(string processName, BitmapSource icon)
    {
        try
        {
            var path = IconCachePath(processName);
            if (System.IO.File.Exists(path)) return;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(icon));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
        catch { }
    }

    private static BitmapSource? LoadIconFromDisk(string processName)
    {
        try
        {
            var path = IconCachePath(processName);
            if (!System.IO.File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>
    /// Ensures a color has enough brightness to be visible on the dark UI.
    /// If the color is too dark, it's boosted while preserving hue.
    /// </summary>
    private static Color EnsureMinBrightness(Color c)
    {
        const byte minChannel = 40; // minimum perceived brightness threshold
        byte maxCh = Math.Max(c.R, Math.Max(c.G, c.B));
        if (maxCh >= minChannel) return c;
        if (maxCh == 0) return ThemeManager.Accent; // pure black → accent

        // Scale up to minimum brightness, preserving hue ratios
        float scale = minChannel / (float)maxCh;
        return Color.FromRgb(
            (byte)Math.Min(255, (int)(c.R * scale)),
            (byte)Math.Min(255, (int)(c.G * scale)),
            (byte)Math.Min(255, (int)(c.B * scale)));
    }

    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none")
            return "None";

        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        if (HATargetDisplayNames.TryGetValue(baseTarget, out var displayName))
            return displayName;
        if (BuiltInTargetNames.TryGetValue(target, out var builtIn))
            return builtIn;

        // Device group — show group name
        if (target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
            return target.Substring(6);

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
            Foreground = FindBrush("BgBaseBrush"),
            BorderThickness = new Thickness(0),
        };

        var dismissBtn = new System.Windows.Controls.Button
        {
            Content = "Dismiss",
            FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = FindBrush("InputBorderBrush"),
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
        // Keep the page header first; the banner sits right below it.
        rootStack.Children.Insert(rootStack.Children.IndexOf(MixerPageHeaderHost) + 1, banner);

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
            textBlock.Text = $"We found {string.Join(", ", appNames)} — apply suggested layout?";

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

    private static bool IsAppGroupMember(KnobConfig? knob, string processName)
    {
        return knob?.Target == "apps"
            && knob.Apps.Any(a => a.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ToggleAppGroupMembership(KnobConfig knob, string processName)
    {
        string app = processName.ToLowerInvariant();

        if (knob.Target == "apps")
        {
            if (knob.Apps.Any(a => a.Equals(app, StringComparison.OrdinalIgnoreCase)))
            {
                knob.Apps.RemoveAll(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                AddAppToGroup(knob, app);
            }
            knob.DeviceId = "";
            return;
        }

        string previousTarget = knob.Target ?? "";
        knob.Target = "apps";
        knob.DeviceId = "";
        knob.Apps.Clear();

        if (IsConvertibleAppTarget(previousTarget))
            AddAppToGroup(knob, previousTarget.ToLowerInvariant());

        AddAppToGroup(knob, app);
    }

    private static void AddAppToGroup(KnobConfig knob, string app)
    {
        if (!knob.Apps.Any(a => a.Equals(app, StringComparison.OrdinalIgnoreCase)))
            knob.Apps.Add(app);
    }

    private static bool IsConvertibleAppTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        if (target.Contains(':')) return false;

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "none", "master", "mic", "system", "any", "active_window",
            "output_device", "input_device", "monitor", "led_brightness",
            "room_lights", "govee", "corsair_pump_fan", "corsair_case_fan",
            "sc_space_cycle", "sc_page_cycle"
        };
        if (reserved.Contains(target)) return false;

        return !target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith("vm_", StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith("sc_", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SuggestedLayout
    {
        public string? CommApp { get; set; }
        public string? MusicApp { get; set; }
        public string? BrowserApp { get; set; }
        public string? GameApp { get; set; }
    }
}
