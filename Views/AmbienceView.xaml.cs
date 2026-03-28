using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Controls;
using Newtonsoft.Json.Linq;

namespace AmpUp.Views;

public partial class AmbienceView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private AmbienceSync? _sync;
    private DreamSyncController? _dreamSync;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Cloud API
    private GoveeCloudApi? _cloudApi;
    private List<GoveeDeviceInfo> _cloudDevices = new();

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Per-device UI references for live knob updates
    private readonly Dictionary<string, (CheckBox onOff, Controls.StyledSlider brightness)> _deviceControls = new();

    // DreamView live preview swatches
    private readonly List<Border> _dreamZoneSwatches = new();
    private TextBlock? _dreamStatusLabel;

    // Card references for dimming
    private Border? _screenSyncCard;
    // _devicesCard and _scenesCard removed — unified into single room card

    // Corsair iCUE
    private CorsairSync? _corsairSync;
    private StackPanel? _corsairDeviceRows;

    // Room pattern engine — headless RgbController for rendering effects
    private RgbController? _roomRgb;
    private string? _activePattern;

    // Navigation callback (set by MainWindow to navigate to Settings)
    public Action? NavigateToSettings { get; set; }

    public AmbienceView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Save(); };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildTopBar();
    }

    public void SetSync(AmbienceSync sync)
    {
        _sync = sync;
    }

    public void SetDreamSync(DreamSyncController dreamSync)
    {
        _dreamSync = dreamSync;

        // Wire zone color preview updates
        _dreamSync.OnZoneColors += colors =>
            Dispatcher.BeginInvoke(() => UpdateDreamZonePreview(colors));
    }

    public void SetCorsairSync(CorsairSync corsairSync)
    {
        _corsairSync = corsairSync;
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;
        _loading = false;

        // Initialize cloud API if enabled and key is configured
        if (config.Ambience.GoveeCloudEnabled && !string.IsNullOrEmpty(config.Ambience.GoveeApiKey))
        {
            _cloudApi?.Dispose();
            _cloudApi = new GoveeCloudApi(config.Ambience.GoveeApiKey);
            _ = FetchCloudDevicesAndRebuild();
        }
        else
        {
            _cloudApi?.Dispose();
            _cloudApi = null;
            _cloudDevices.Clear();
            RebuildDevicePanel();
        }
    }

    // ── Top Bar ──────────────────────────────────────────────────────

    private Border? _topStatusDot;
    private TextBlock? _topStatusLabel;

    private void BuildTopBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var settingsBtn = new TextBlock
        {
            Text = "⚙ Settings",
            FontSize = 12,
            Foreground = FindBrush("AccentBrush"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "Go to Settings to configure integrations",
        };
        settingsBtn.MouseLeftButtonUp += (_, _) => NavigateToSettings?.Invoke();
        row.Children.Add(settingsBtn);

        // Status indicator
        _topStatusDot = new Border
        {
            Width = 8, Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        _topStatusLabel = new TextBlock
        {
            FontSize = 11,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(_topStatusDot);
        row.Children.Add(_topStatusLabel);

        Loaded += (_, _) => UpdateTopBarStatus();

        TopBar.Children.Add(row);
    }

    private void UpdateTopBarStatus()
    {
        if (_config == null || _topStatusDot == null || _topStatusLabel == null) return;

        var parts = new List<string>();

        // Govee status
        bool lanEnabled = _config.Ambience.GoveeEnabled;
        bool cloudEnabled = _config.Ambience.GoveeCloudEnabled;
        int lanDevices = _config.Ambience.GoveeDevices.Count;
        int cloudDevices = _cloudDevices.Count;

        if (cloudEnabled && cloudDevices > 0)
            parts.Add($"{cloudDevices} Govee");
        else if (lanEnabled && lanDevices > 0)
            parts.Add($"{lanDevices} Govee (LAN)");

        // Corsair status
        if (_config.Corsair.Enabled && _corsairSync?.IsAvailable == true)
            parts.Add($"{_corsairSync.Devices.Count} iCUE");
        else if (_config.Corsair.Enabled)
            parts.Add("iCUE connecting...");

        if (parts.Count > 0)
        {
            _topStatusDot.Background = Brush("#00E676");
            _topStatusLabel.Text = string.Join(" + ", parts) + " device(s)";
        }
        else if (lanEnabled || cloudEnabled || _config.Corsair.Enabled)
        {
            _topStatusDot.Background = Brush("#FFB800");
            _topStatusLabel.Text = "No devices — configure in Settings";
        }
        else
        {
            _topStatusDot.Background = Brush("#555555");
            _topStatusLabel.Text = "No integrations enabled";
        }
    }

    // ── Cloud Device Fetching ─────────────────────────────────────────

    private async Task FetchCloudDevicesAndRebuild()
    {
        if (_cloudApi == null) return;

        try
        {
            var devices = await _cloudApi.GetDevicesAsync();
            if (devices == null) return;

            Dispatcher.Invoke(() =>
            {
                _cloudDevices = devices;

                // Also enrich LAN devices with cloud names
                if (_config != null)
                    GoveeCloudApi.EnrichLanDevicesWithCloudNames(_config.Ambience.GoveeDevices, devices);

                RebuildDevicePanel();
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[Ambience] Cloud fetch error: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                DevicePanel.Children.Clear();
                DevicePanel.Children.Add(MakeErrorText($"Failed to load devices: {ex.Message}"));
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  MAIN PANEL — 3-CARD LAYOUT
    // ══════════════════════════════════════════════════════════════════

    private void RebuildDevicePanel()
    {
        DevicePanel.Children.Clear();
        _deviceControls.Clear();
        _sectionHeaders.Clear();
        _dreamZoneSwatches.Clear();

        UpdateTopBarStatus();

        if (_config == null) return;

        bool hasGovee = _config.Ambience.GoveeEnabled || _config.Ambience.GoveeCloudEnabled;
        bool hasCorsair = _config.Corsair.Enabled;
        bool hasAnyDevices = _config.Ambience.GoveeDevices.Count > 0 || _cloudDevices.Count > 0
            || (_corsairSync?.Devices.Count > 0);

        if (!hasGovee && !hasCorsair)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "No integrations enabled",
                "Enable Govee or Corsair iCUE in Settings to get started.",
                "Open Settings", () => NavigateToSettings?.Invoke()));
            return;
        }

        // ── Card 1: Room Lighting (unified — effects, colors, devices) ──
        DevicePanel.Children.Add(BuildRoomCard());

        // ── Card 2: Screen Sync — Game Mode ──
        _screenSyncCard = BuildScreenSyncCard();
        DevicePanel.Children.Add(_screenSyncCard);

    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 1: ROOM LIGHTING (unified)
    // ══════════════════════════════════════════════════════════════════

    private Border BuildRoomCard()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        // ── Section 1: EFFECT ──
        var (effBar, effLabel) = MakeSectionHeader("EFFECT");
        stack.Children.Add(WrapHeader(effBar, effLabel));

        // "Sync to Amp Up" tile before the effect picker
        var syncActive = _activePattern == "__sync__";
        var syncTile = new Border
        {
            Width = 82, Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = syncActive
                ? new SolidColorBrush(Color.FromArgb(0x30, 0x69, 0xF0, 0xAE))
                : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = syncActive
                ? new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE))
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Mirror your Turn Up knob LED effects to all room devices",
        };
        var syncContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        syncContent.Children.Add(new TextBlock { Text = "🔗", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
        syncContent.Children.Add(new TextBlock
        {
            Text = "Amp Up", FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = syncActive ? new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        syncTile.Child = syncContent;
        syncTile.MouseLeftButtonUp += (_, _) =>
        {
            if (_config == null) return;
            if (_activePattern == "__sync__")
            {
                // Deactivate sync
                _activePattern = null;
                _config.Ambience.LinkToLights = false;
                _config.Corsair.LightSyncMode = "static";
            }
            else
            {
                // Activate sync
                StopRoomPattern();
                _activePattern = "__sync__";
                _config.Ambience.LinkToLights = true;
                _config.Corsair.LightSyncMode = "vu_reactive";
            }
            QueueSave();
            RebuildDevicePanel();
        };
        stack.Children.Add(syncTile);

        // Effect picker (all 43 effects)
        var effectPicker = new Controls.EffectPickerControl(showGlobal: true)
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        if (_activePattern != null && _activePattern != "__sync__")
            effectPicker.SelectedEffect = Enum.TryParse<LightEffect>(_activePattern, true, out var eff) ? eff : LightEffect.SingleColor;
        effectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var effect = effectPicker.SelectedEffect;
            if (effect == LightEffect.SingleColor)
            {
                StopRoomPattern();
                SendRoomColor(_roomColor1.R, _roomColor1.G, _roomColor1.B);
            }
            else
            {
                StartRoomPattern(effect.ToString());
            }
            // Deactivate sync mode if user picks an effect
            if (_config != null)
            {
                _config.Ambience.LinkToLights = false;
                _activePattern = effect == LightEffect.SingleColor ? null : effect.ToString();
            }
        };
        stack.Children.Add(effectPicker);

        // ── Section 2: PRESETS ──
        stack.Children.Add(MakeSeparator());
        var (preBar, preLabel) = MakeSectionHeader("COLOR");
        stack.Children.Add(WrapHeader(preBar, preLabel));

        stack.Children.Add(MakeSubLabel("PRESETS"));
        var presetWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        presetWrap.Children.Add(MakePresetTile("Solid", new SolidColorBrush(_roomColor1), new[] { _roomColor1 }, presetWrap));
        foreach (var (name, colors) in ColorPalettes)
        {
            var gb = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int ci = 0; ci < colors.Length; ci++)
                gb.GradientStops.Add(new GradientStop(colors[ci], ci / (double)(colors.Length - 1)));
            presetWrap.Children.Add(MakePresetTile(name, gb, colors, presetWrap));
        }
        stack.Children.Add(presetWrap);

        // Manual color pickers
        stack.Children.Add(MakeSubLabel("MANUAL"));
        var colorRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        colorRow.Children.Add(MakeRoomColorPill("PRIMARY", _roomColor1, false));
        colorRow.Children.Add(MakeRoomColorPill("SECONDARY", _roomColor2, true));
        stack.Children.Add(colorRow);

        // Speed slider
        stack.Children.Add(MakeSubLabel("SPEED"));
        var speedSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
            Margin = new Thickness(0, 0, 0, 8),
        };
        speedSlider.ValueChanged += (_, _) =>
        {
            if (_roomRgb != null && Enum.TryParse<LightEffect>(_activePattern, true, out var eff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = eff,
                    R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                    R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                    EffectSpeed = (int)speedSlider.Value,
                });
            }
        };
        stack.Children.Add(speedSlider);

        // ── Section 3: GOVEE DEVICES ──
        if (_config!.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0)
        {
            stack.Children.Add(MakeSeparator());
            var (govBar, govLabel) = MakeSectionHeader("GOVEE DEVICES");
            stack.Children.Add(WrapHeader(govBar, govLabel));

            foreach (var govDev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(govDev.Ip)) continue;
                var devConfig = govDev;

                var devRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                devRow.Children.Add(new TextBlock
                {
                    Text = !string.IsNullOrWhiteSpace(govDev.Name) ? govDev.Name : govDev.Ip,
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 140, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0),
                });

                var onOff = new CheckBox
                {
                    Content = "On", FontSize = 12,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                onOff.Checked += async (_, _) =>
                {
                    if (_loading) return;
                    devConfig.PoweredOn = true;
                    AmbienceSync.PauseSync(devConfig.Ip, 5);
                    await AmbienceSync.SendTurnAsync(devConfig.Ip, true);
                    _onSave?.Invoke(_config!);
                };
                onOff.Unchecked += async (_, _) =>
                {
                    if (_loading) return;
                    devConfig.PoweredOn = false;
                    await AmbienceSync.SendTurnAsync(devConfig.Ip, false);
                    _onSave?.Invoke(_config!);
                };
                devRow.Children.Add(onOff);

                var brightSlider = new StyledSlider
                {
                    Minimum = 0, Maximum = 100, Value = 100,
                    Width = 140, Height = 35, Suffix = "%",
                    AccentColor = ThemeManager.Accent,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var brightDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                brightDebounce.Tick += async (_, _) =>
                {
                    brightDebounce.Stop();
                    int pct = (int)brightSlider.Value;
                    if (pct == 0)
                    {
                        devConfig.PoweredOn = false;
                        await AmbienceSync.SendTurnAsync(devConfig.Ip, false);
                    }
                    else
                    {
                        if (!devConfig.PoweredOn)
                        {
                            devConfig.PoweredOn = true;
                            await AmbienceSync.SendTurnAsync(devConfig.Ip, true);
                        }
                        AmbienceSync.PauseSync(devConfig.Ip, 5);
                        await AmbienceSync.SendBrightnessAsync(devConfig.Ip, pct);
                    }
                };
                brightSlider.ValueChanged += (_, _) => { brightDebounce.Stop(); brightDebounce.Start(); };
                devRow.Children.Add(brightSlider);

                stack.Children.Add(devRow);

                // Store for knob updates
                _deviceControls[devConfig.Ip] = (onOff, brightSlider);

                // Query actual state
                _ = QueryDeviceStateAsync(devConfig.Ip, onOff, brightSlider);
            }
        }

        // ── Section 4: GOVEE SCENES (Cloud API) ──
        if (_cloudApi != null && _cloudDevices.Count > 0)
        {
            stack.Children.Add(MakeSeparator());
            var (scBar, scLabel) = MakeSectionHeader("GOVEE SCENES");
            stack.Children.Add(WrapHeader(scBar, scLabel));

            // Device picker for scenes
            var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            pickerRow.Children.Add(MakeSubLabel("DEVICE"));
            _sceneDevicePicker = new ComboBox { Width = 240 };
            foreach (var dev in _cloudDevices)
                _sceneDevicePicker.Items.Add(!string.IsNullOrWhiteSpace(dev.DeviceName) ? dev.DeviceName : dev.Sku);
            if (_cloudDevices.Count > 0) _sceneDevicePicker.SelectedIndex = 0;

            _sceneContent = new StackPanel();

            _sceneDevicePicker.SelectionChanged += (_, _) =>
            {
                _sceneContent.Children.Clear();
                int idx = _sceneDevicePicker.SelectedIndex;
                if (idx >= 0 && idx < _cloudDevices.Count)
                    BuildGoveeSceneContent(_cloudDevices[idx]);
            };
            pickerRow.Children.Add(_sceneDevicePicker);
            stack.Children.Add(pickerRow);
            stack.Children.Add(_sceneContent);

            if (_cloudDevices.Count > 0)
                BuildGoveeSceneContent(_cloudDevices[0]);
        }

        // ── Section 5: CORSAIR iCUE ──
        if (_config.Corsair.Enabled && _corsairSync != null)
        {
            stack.Children.Add(MakeSeparator());
            var corsairYellow = Color.FromRgb(0xFF, 0xD3, 0x00);
            var (corBar, corLabel) = MakeSectionHeader("CORSAIR iCUE");
            corBar.Background = new SolidColorBrush(corsairYellow);
            corLabel.Foreground = new SolidColorBrush(corsairYellow);
            stack.Children.Add(WrapHeader(corBar, corLabel));

            // Brightness
            var corBrightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            corBrightRow.Children.Add(MakeSubLabel("BRIGHTNESS"));
            var corBrightSlider = new StyledSlider
            {
                Minimum = 50, Maximum = 200, Value = _config.Corsair.LightBrightness,
                Width = 140, Height = 35, AccentColor = corsairYellow, ShowLabel = false,
            };
            var corBrightLabel = new TextBlock
            {
                Text = $"{_config.Corsair.LightBrightness}%", FontSize = 12,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            corBrightSlider.ValueChanged += (_, _) =>
            {
                if (_config == null) return;
                _config.Corsair.LightBrightness = (int)corBrightSlider.Value;
                corBrightLabel.Text = $"{(int)corBrightSlider.Value}%";
                QueueSave();
            };
            corBrightRow.Children.Add(corBrightSlider);
            corBrightRow.Children.Add(corBrightLabel);
            stack.Children.Add(corBrightRow);

            // Detected devices
            _corsairDeviceRows = new StackPanel();
            if (_corsairSync.IsAvailable && _corsairSync.Devices.Count > 0)
            {
                foreach (var dev in _corsairSync.Devices)
                    _corsairDeviceRows.Children.Add(BuildCorsairDeviceRow(dev));
            }
            else
            {
                _ = RefreshCorsairDevices();
                _corsairDeviceRows.Children.Add(new TextBlock
                {
                    Text = "Connecting to iCUE...", FontSize = 11,
                    Foreground = FindBrush("TextSecBrush"),
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }
            stack.Children.Add(_corsairDeviceRows);
        }

        return card;
    }

    private void BuildGoveeSceneContent(GoveeDeviceInfo device)
    {
        if (_sceneContent == null) return;
        string? lanIp = FindLanIp(device);

        var colorsContainer = new StackPanel();
        _sceneContent.Children.Add(colorsContainer);
        BuildColorsSection(device, colorsContainer, lanIp, null);

        bool hasMusic = device.Capabilities?.Contains("devices.capabilities.music_setting") == true;
        if (hasMusic)
        {
            _sceneContent.Children.Add(MakeSeparator());
            var (musBar, musLabel) = MakeSectionHeader("MUSIC MODE");
            _sceneContent.Children.Add(WrapHeader(musBar, musLabel));
            BuildMusicSection(device, _sceneContent);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 2: SCREEN SYNC
    // ══════════════════════════════════════════════════════════════════

    private Border BuildScreenSyncCard()
    {
        var cfg = _config!.Ambience.ScreenSync;

        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        // ── Header row ──
        var (headerBar, headerLabel) = MakeSectionHeader("SCREEN SYNC — Game Mode");
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        headerRow.Children.Add(headerBar);
        headerRow.Children.Add(headerLabel);

        // Game Mode toggle — only way to activate screen sync
        var gameModeToggle = new CheckBox
        {
            Content = "Enable",
            IsChecked = _config!.Ambience.GameModeEnabled,
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            ToolTip = "When a fullscreen game is detected, screen colors sync to Govee and Corsair. Returns to Amp Up LED sync when you exit the game.",
        };
        gameModeToggle.Checked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.GameModeEnabled = true;
            QueueSave();
        };
        gameModeToggle.Unchecked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.GameModeEnabled = false;
            // If screen sync was on from game mode, turn it off
            if (_config.Ambience.ScreenSync.Enabled)
            {
                _config.Ambience.ScreenSync.Enabled = false;
                if (_config.Corsair.Enabled)
                    _config.Corsair.LightSyncMode = "vu_reactive";
                _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            }
            QueueSave();
        };
        headerRow.Children.Add(gameModeToggle);

        // Status indicator
        var statusBadge = new TextBlock
        {
            Text = cfg.Enabled ? "ACTIVE" : "Standby",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = cfg.Enabled ? Brush("#00E676") : FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        headerRow.Children.Add(statusBadge);
        stack.Children.Add(headerRow);

        // ── Description ──
        stack.Children.Add(new TextBlock
        {
            Text = "Automatically syncs screen colors to Govee and Corsair when a fullscreen game is detected. Returns to Amp Up LED sync when you exit.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        stack.Children.Add(MakeSeparator());

        // ── Row 1: Monitor + FPS + Zones ──
        var row1 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        var screens = System.Windows.Forms.Screen.AllScreens;
        var friendlyNames = NativeMethods.GetMonitorFriendlyNames();
        row1.Children.Add(MakeSubLabel("MONITOR"));
        var monitorCombo = new ComboBox { MinWidth = 200, MaxWidth = 350, Margin = new Thickness(0, 0, 20, 0) };
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var gdiName = screen.DeviceName.TrimEnd('\0');
            var friendly = friendlyNames.GetValueOrDefault(gdiName, "");
            string label = !string.IsNullOrEmpty(friendly) ? friendly : $"Display {i + 1}";
            if (screen.Primary) label += " (Primary)";
            monitorCombo.Items.Add(label);
        }
        monitorCombo.SelectedIndex = Math.Min(cfg.MonitorIndex, screens.Length - 1);
        monitorCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.MonitorIndex = monitorCombo.SelectedIndex;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row1.Children.Add(monitorCombo);

        row1.Children.Add(MakeSubLabel("FPS"));
        var fpsCombo = new ComboBox { Width = 90, Margin = new Thickness(0, 0, 20, 0) };
        fpsCombo.Items.Add("15fps"); fpsCombo.Items.Add("30fps"); fpsCombo.Items.Add("60fps");
        fpsCombo.SelectedIndex = cfg.TargetFps switch { 15 => 0, 60 => 2, _ => 1 };
        fpsCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.TargetFps = fpsCombo.SelectedIndex switch { 0 => 15, 2 => 60, _ => 30 };
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row1.Children.Add(fpsCombo);

        row1.Children.Add(MakeSubLabel("ZONES"));
        var zoneCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 0, 0) };
        zoneCombo.Items.Add("4 zones"); zoneCombo.Items.Add("8 zones"); zoneCombo.Items.Add("16 zones");
        zoneCombo.SelectedIndex = cfg.ZoneCount switch { 4 => 0, 16 => 2, _ => 1 };
        zoneCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.ZoneCount = zoneCombo.SelectedIndex switch { 0 => 4, 2 => 16, _ => 8 };
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            RebuildZonePreview(stack);
            QueueSave();
        };
        row1.Children.Add(zoneCombo);
        stack.Children.Add(row1);

        // ── Row 2: Saturation + Sensitivity ──
        var row2 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        row2.Children.Add(MakeSubLabel("SATURATION"));
        var satSlider = new StyledSlider
        {
            Minimum = 50, Maximum = 200, Value = (int)(cfg.Saturation * 100),
            Width = 120, Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
        };
        var satLabel = new TextBlock
        {
            Text = $"{cfg.Saturation:F1}×",
            FontSize = 12,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 20, 0),
        };
        satSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Saturation = (float)(satSlider.Value / 100.0);
            satLabel.Text = $"{satSlider.Value / 100.0:F1}×";
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row2.Children.Add(satSlider);
        row2.Children.Add(satLabel);

        row2.Children.Add(MakeSubLabel("SENSITIVITY"));
        var sensSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 20, Value = cfg.Sensitivity,
            Width = 120, Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
        };
        var sensLabel = new TextBlock
        {
            Text = $"{cfg.Sensitivity}",
            FontSize = 12,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Sensitivity = (int)sensSlider.Value;
            sensLabel.Text = $"{(int)sensSlider.Value}";
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row2.Children.Add(sensSlider);
        row2.Children.Add(sensLabel);

        // Corsair brightness (only when Corsair is enabled)
        if (_config!.Corsair.Enabled)
        {
            row2.Children.Add(new Border { Width = 20 }); // spacer
            row2.Children.Add(MakeSubLabel("iCUE BRIGHTNESS"));
            var corsairBrightSlider = new StyledSlider
            {
                Minimum = 50, Maximum = 200, Value = _config.Corsair.LightBrightness,
                Width = 120, Height = 35,
                AccentColor = Color.FromRgb(0xFF, 0xD3, 0x00), // Corsair yellow
                ShowLabel = false,
            };
            var corsairBrightLabel = new TextBlock
            {
                Text = $"{_config.Corsair.LightBrightness}%",
                FontSize = 12,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            corsairBrightSlider.ValueChanged += (_, _) =>
            {
                if (_config == null) return;
                _config.Corsair.LightBrightness = (int)corsairBrightSlider.Value;
                corsairBrightLabel.Text = $"{(int)corsairBrightSlider.Value}%";
                QueueSave();
            };
            row2.Children.Add(corsairBrightSlider);
            row2.Children.Add(corsairBrightLabel);
        }

        stack.Children.Add(row2);

        stack.Children.Add(MakeSeparator());

        // ── Zone Preview ──
        stack.Children.Add(MakeSubLabel("ZONE PREVIEW"));
        var previewWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        previewWrap.Tag = "zonePreview";
        int zoneCount = cfg.ZoneCount;
        for (int i = 0; i < zoneCount; i++)
        {
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(0, 0, 4, 4),
                ToolTip = $"Zone {i + 1}",
            };
            _dreamZoneSwatches.Add(swatch);
            previewWrap.Children.Add(swatch);
        }
        stack.Children.Add(previewWrap);

        _dreamStatusLabel = new TextBlock
        {
            Text = _dreamSync?.Status ?? "Stopped",
            Style = FindStyle("SecondaryText"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        stack.Children.Add(_dreamStatusLabel);

        // Status update timer — updates status label and Game Mode badge
        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statusTimer.Tick += (_, _) =>
        {
            if (_dreamStatusLabel != null && _dreamSync != null)
                _dreamStatusLabel.Text = _dreamSync.Status;
            bool active = _config?.Ambience.ScreenSync.Enabled == true;
            statusBadge.Text = active ? "ACTIVE" : "Standby";
            statusBadge.Foreground = active ? Brush("#00E676") : FindBrush("TextSecBrush");
        };
        statusTimer.Start();

        // ── Device Zone Mapping ──
        if (_config!.Ambience.GoveeDevices.Count > 0)
        {
            stack.Children.Add(MakeSeparator());
            stack.Children.Add(MakeSubLabel("DEVICE ZONE MAPPING"));

            foreach (var goveeDevice in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(goveeDevice.Ip)) continue;

                var mapRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                var devName = new TextBlock
                {
                    Text = !string.IsNullOrWhiteSpace(goveeDevice.Name) ? goveeDevice.Name : goveeDevice.Ip,
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 160,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                mapRow.Children.Add(devName);

                int segCount = AmbienceSync.GetSegmentCount(goveeDevice);
                if (segCount > 0 && goveeDevice.UseSegmentProtocol)
                {
                    mapRow.Children.Add(new TextBlock
                    {
                        Text = $"Per-segment ({segCount} zones)",
                        FontSize = 12,
                        Foreground = FindBrush("AccentBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                else
                {
                    // Zone side picker
                    var mapping = cfg.DeviceMappings.FirstOrDefault(m => m.DeviceIp == goveeDevice.Ip);
                    if (mapping == null)
                    {
                        mapping = new ZoneDeviceMapping { DeviceIp = goveeDevice.Ip, Side = ZoneSide.Full };
                        cfg.DeviceMappings.Add(mapping);
                    }
                    var sideCombo = new ComboBox { Width = 120 };
                    sideCombo.Items.Add("Full"); sideCombo.Items.Add("Top"); sideCombo.Items.Add("Bottom");
                    sideCombo.Items.Add("Left"); sideCombo.Items.Add("Right");
                    sideCombo.SelectedItem = mapping.Side.ToString();
                    var capturedMapping = mapping;
                    sideCombo.SelectionChanged += (_, _) =>
                    {
                        if (_loading || _config == null) return;
                        if (Enum.TryParse<ZoneSide>(sideCombo.SelectedItem?.ToString(), out var side))
                            capturedMapping.Side = side;
                        QueueSave();
                    };
                    mapRow.Children.Add(sideCombo);
                }

                stack.Children.Add(mapRow);
            }
        }

        return card;
    }

    private void RebuildZonePreview(StackPanel stack)
    {
        _dreamZoneSwatches.Clear();
        // Find and replace the zone preview wrap
        for (int i = 0; i < stack.Children.Count; i++)
        {
            if (stack.Children[i] is WrapPanel wp && wp.Tag as string == "zonePreview")
            {
                wp.Children.Clear();
                int zoneCount = _config?.Ambience.ScreenSync.ZoneCount ?? 8;
                for (int z = 0; z < zoneCount; z++)
                {
                    var swatch = new Border
                    {
                        Width = 28, Height = 28,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        Margin = new Thickness(0, 0, 4, 4),
                        ToolTip = $"Zone {z + 1}",
                    };
                    _dreamZoneSwatches.Add(swatch);
                    wp.Children.Add(swatch);
                }
                break;
            }
        }
    }

    private void UpdateDreamZonePreview((byte R, byte G, byte B)[] colors)
    {
        for (int i = 0; i < _dreamZoneSwatches.Count && i < colors.Length; i++)
            _dreamZoneSwatches[i].Background = new SolidColorBrush(
                Color.FromRgb(colors[i].R, colors[i].G, colors[i].B));
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 2: DEVICES
    // ══════════════════════════════════════════════════════════════════

    private Border BuildDevicesCard()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        // ── Header ──
        var (headerBar, headerLabel) = MakeSectionHeader("DEVICES");
        stack.Children.Add(WrapHeader(headerBar, headerLabel));

        bool hasAnyDevice = false;

        // ── Govee devices ──
        var goveeDevices = _cloudDevices.Count > 0 ? _cloudDevices : null;
        var lanDevices = _config!.Ambience.GoveeDevices;

        if (goveeDevices != null)
        {
            foreach (var device in goveeDevices)
            {
                string? ip = FindLanIp(device);
                if (ip == null)
                {
                    var lan = lanDevices.FirstOrDefault(d =>
                        d.Name == device.DeviceName || d.DeviceId == device.Device);
                    ip = lan?.Ip;
                }
                stack.Children.Add(BuildGoveeDeviceRow(device, ip));
                hasAnyDevice = true;
            }
        }
        else if (lanDevices.Count > 0)
        {
            foreach (var lan in lanDevices)
            {
                if (string.IsNullOrWhiteSpace(lan.Ip)) continue;
                var deviceInfo = new GoveeDeviceInfo
                {
                    Device = lan.DeviceId,
                    Sku = lan.Sku,
                    DeviceName = lan.Name,
                    Capabilities = new List<string>(),
                };
                stack.Children.Add(BuildGoveeDeviceRow(deviceInfo, lan.Ip));
                hasAnyDevice = true;
            }
        }

        // ── Corsair devices ──
        if (_config.Corsair.Enabled && _corsairSync != null)
        {
            if (hasAnyDevice)
                stack.Children.Add(MakeSeparator());

            _corsairDeviceRows = new StackPanel();

            if (_corsairSync.IsAvailable && _corsairSync.Devices.Count > 0)
            {
                foreach (var dev in _corsairSync.Devices)
                    _corsairDeviceRows.Children.Add(BuildCorsairDeviceRow(dev));
                hasAnyDevice = true;
            }
            else
            {
                // Trigger async device discovery
                _ = RefreshCorsairDevices();

                _corsairDeviceRows.Children.Add(new TextBlock
                {
                    Text = _corsairSync.IsAvailable ? "Discovering devices..." : "Connecting to iCUE...",
                    FontSize = 11,
                    Foreground = FindBrush("TextSecBrush"),
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }

            stack.Children.Add(_corsairDeviceRows);
        }

        if (!hasAnyDevice && !_config.Corsair.Enabled)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No devices found — configure Govee or Corsair in Settings.",
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 4, 0, 8),
            });
        }

        return card;
    }

    private Border BuildGoveeDeviceRow(GoveeDeviceInfo device, string? overrideIp)
    {
        var row = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = FindBrush("CardBorderBrush"),
            Padding = new Thickness(0, 8, 0, 8),
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        // Device name
        var deviceLabel = !string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceName
            : !string.IsNullOrEmpty(device.Sku) ? AmbienceSync.GetProductName(device.Sku)
            : "Govee Device";

        content.Children.Add(new TextBlock
        {
            Text = deviceLabel,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        });

        string? lanIp = overrideIp ?? FindLanIp(device);
        var devConfig = lanIp != null
            ? _config?.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == lanIp)
            : null;

        // Power toggle
        var onOffCheck = new CheckBox
        {
            Content = "On",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        onOffCheck.Checked += async (_, _) =>
        {
            if (_loading) return;
            if (devConfig != null) { devConfig.PoweredOn = true; _onSave?.Invoke(_config!); }
            if (lanIp != null)
            {
                AmbienceSync.PauseSync(lanIp, 5);
                await AmbienceSync.SendTurnAsync(lanIp, true);
            }
            else if (_cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(true)));
        };
        onOffCheck.Unchecked += async (_, _) =>
        {
            if (_loading) return;
            if (devConfig != null) { devConfig.PoweredOn = false; _onSave?.Invoke(_config!); }
            if (lanIp != null)
                await AmbienceSync.SendTurnAsync(lanIp, false);
            else if (_cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(false)));
        };
        content.Children.Add(onOffCheck);

        // Brightness slider
        var brightnessSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 100,
            Width = 140, Height = 35,
            Suffix = "%",
            AccentColor = ThemeManager.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var brightnessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        brightnessDebounce.Tick += async (_, _) =>
        {
            brightnessDebounce.Stop();
            if (lanIp != null)
            {
                AmbienceSync.PauseSync(lanIp, 30);
                await AmbienceSync.SendBrightnessAsync(lanIp, (int)brightnessSlider.Value);
            }
            else if (_cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetBrightness((int)brightnessSlider.Value)));
        };
        brightnessSlider.ValueChanged += (_, _) =>
        {
            brightnessDebounce.Stop();
            brightnessDebounce.Start();
        };
        content.Children.Add(brightnessSlider);

        // Store references for live knob updates
        if (lanIp != null)
            _deviceControls[lanIp] = (onOffCheck, brightnessSlider);
        if (devConfig != null && !string.IsNullOrWhiteSpace(devConfig.Ip))
            _deviceControls[devConfig.Ip] = (onOffCheck, brightnessSlider);

        // Query actual device state
        if (lanIp != null)
            _ = QueryDeviceStateAsync(lanIp, onOffCheck, brightnessSlider);

        row.Child = content;
        return row;
    }

    private Border BuildCorsairDeviceRow(CorsairDevice dev)
    {
        var row = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = FindBrush("CardBorderBrush"),
            Padding = new Thickness(0, 8, 0, 8),
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        // Device name
        content.Children.Add(new TextBlock
        {
            Text = dev.Name,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x00)), // Corsair yellow
            VerticalAlignment = VerticalAlignment.Center,
            Width = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        });

        // Type badge
        content.Children.Add(new TextBlock
        {
            Text = dev.Type.Replace("_", " "),
            FontSize = 10,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        // LED count
        content.Children.Add(new TextBlock
        {
            Text = $"{dev.LedCount} LEDs",
            FontSize = 10,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        row.Child = content;
        return row;
    }

    private async Task RefreshCorsairDevices()
    {
        if (_corsairSync == null || _corsairDeviceRows == null) return;

        await Task.Delay(800); // give Start() a moment to finish
        var devices = await _corsairSync.GetDevicesAsync();

        _ = Dispatcher.BeginInvoke(() =>
        {
            _corsairDeviceRows.Children.Clear();

            if (devices.Count > 0)
            {
                foreach (var dev in devices)
                    _corsairDeviceRows.Children.Add(BuildCorsairDeviceRow(dev));
            }
            else
            {
                _corsairDeviceRows.Children.Add(new TextBlock
                {
                    Text = _corsairSync.IsAvailable
                        ? "No devices found — check iCUE"
                        : "iCUE not detected — make sure it's running with SDK enabled",
                    FontSize = 11,
                    Foreground = FindBrush("TextSecBrush"),
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }

            UpdateTopBarStatus();
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 3: SCENES & COLORS (Govee Cloud only)
    // ══════════════════════════════════════════════════════════════════

    private ComboBox? _sceneDevicePicker;
    private StackPanel? _sceneContent;
    private int _sceneTabIndex = 2; // 0=Govee, 1=Corsair, 2=Global (default to Global)

    private Border BuildScenesCard()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        // ── Header ──
        var (headerBar, headerLabel) = MakeSectionHeader("SCENES & COLORS");
        stack.Children.Add(WrapHeader(headerBar, headerLabel));

        // ── Segmented selector: Govee | Corsair | Global ──
        var segmented = new Controls.SegmentedControl
        {
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        segmented.AddSegment("Govee", "govee");
        segmented.AddSegment("Corsair", "corsair");
        segmented.AddSegment("Global", "global");
        segmented.SelectedIndex = _sceneTabIndex;
        stack.Children.Add(segmented);

        // ── Tab content ──
        _sceneContent = new StackPanel();
        stack.Children.Add(_sceneContent);

        segmented.SelectionChanged += (_, _) =>
        {
            _sceneTabIndex = segmented.SelectedIndex;
            RefreshSceneContent();
        };

        RefreshSceneContent();

        return card;
    }

    private void RefreshSceneContent()
    {
        if (_sceneContent == null) return;
        _sceneContent.Children.Clear();

        switch (_sceneTabIndex)
        {
            case 0: BuildGoveeTab(); break;
            case 1: BuildCorsairTab(); break;
            case 2: BuildGlobalTab(); break;
        }
    }

    private void BuildGoveeTab()
    {
        if (_sceneContent == null || _cloudApi == null) return;

        // Device picker
        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        pickerRow.Children.Add(MakeSubLabel("DEVICE"));
        _sceneDevicePicker = new ComboBox { Width = 240 };
        foreach (var dev in _cloudDevices)
        {
            var name = !string.IsNullOrWhiteSpace(dev.DeviceName) ? dev.DeviceName : dev.Sku;
            _sceneDevicePicker.Items.Add(name);
        }
        if (_cloudDevices.Count > 0) _sceneDevicePicker.SelectedIndex = 0;
        _sceneDevicePicker.SelectionChanged += (_, _) =>
        {
            // Rebuild just the Govee content below the picker
            while (_sceneContent!.Children.Count > 1)
                _sceneContent.Children.RemoveAt(1);
            int idx = _sceneDevicePicker.SelectedIndex;
            if (idx >= 0 && idx < _cloudDevices.Count)
                BuildGoveeDeviceContent(_cloudDevices[idx]);
        };
        _sceneContent.Children.Add(pickerRow);

        if (_cloudDevices.Count > 0)
            BuildGoveeDeviceContent(_cloudDevices[0]);

        if (_cloudDevices.Count == 0)
        {
            _sceneContent.Children.Add(new TextBlock
            {
                Text = "No Govee Cloud devices — enable Cloud API in Settings.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
    }

    private void BuildGoveeDeviceContent(GoveeDeviceInfo device)
    {
        if (_sceneContent == null) return;
        string? lanIp = FindLanIp(device);

        var colorsContainer = new StackPanel();
        _sceneContent.Children.Add(colorsContainer);
        BuildColorsSection(device, colorsContainer, lanIp, null);

        bool hasMusic = device.Capabilities?.Contains("devices.capabilities.music_setting") == true;
        if (hasMusic)
        {
            _sceneContent.Children.Add(MakeSeparator());
            var (musBar, musLabel) = MakeSectionHeader("MUSIC MODE");
            _sceneContent.Children.Add(WrapHeader(musBar, musLabel));
            BuildMusicSection(device, _sceneContent);
        }
    }

    private void BuildCorsairTab()
    {
        if (_sceneContent == null) return;

        if (_corsairSync == null || !(_config?.Corsair.Enabled ?? false))
        {
            _sceneContent.Children.Add(new TextBlock
            {
                Text = "Corsair iCUE is not enabled — enable it in Settings.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Corsair-specific description
        _sceneContent.Children.Add(new TextBlock
        {
            Text = "Corsair iCUE receives colors directly from Amp Up. Choose a mode below.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // Mode options as tiles
        var modeWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var corsairYellow = Color.FromRgb(0xFF, 0xD3, 0x00);

        var modes = new (string label, string mode, string desc)[]
        {
            ("Sync to Amp Up", "vu_reactive", "Mirror Turn Up knob LED effects"),
            ("Static Color", "static", "Set a single color for all LEDs"),
            ("Off", "off", "No colors sent to Corsair"),
        };

        foreach (var (label, mode, desc) in modes)
        {
            bool isActive = _config!.Corsair.LightSyncMode == mode;
            var tile = new Border
            {
                Width = 140, Height = 60,
                CornerRadius = new CornerRadius(8),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x30, corsairYellow.R, corsairYellow.G, corsairYellow.B))
                    : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = isActive
                    ? new SolidColorBrush(corsairYellow)
                    : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                ToolTip = desc,
                Padding = new Thickness(10, 8, 10, 8),
            };

            var tileStack = new StackPanel();
            tileStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = isActive
                    ? new SolidColorBrush(corsairYellow)
                    : FindBrush("TextPrimaryBrush"),
            });
            tileStack.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 9,
                Foreground = FindBrush("TextSecBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            tile.Child = tileStack;

            var capturedMode = mode;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (_config == null) return;
                _config.Corsair.LightSyncMode = capturedMode;
                QueueSave();
                RefreshSceneContent(); // rebuild to update active state
            };

            modeWrap.Children.Add(tile);
        }
        _sceneContent.Children.Add(modeWrap);

        // Static color picker (only when in static mode)
        if (_config!.Corsair.LightSyncMode == "static")
        {
            _sceneContent.Children.Add(MakeSubLabel("CORSAIR COLOR"));
            var colorWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };

            var quickColors = new (string name, byte r, byte g, byte b)[]
            {
                ("White", 255, 255, 255), ("Red", 255, 30, 30), ("Orange", 255, 120, 0),
                ("Gold", 255, 200, 0), ("Green", 0, 230, 118), ("Cyan", 0, 220, 240),
                ("Blue", 40, 80, 255), ("Purple", 140, 60, 255), ("Pink", 255, 50, 150),
            };

            foreach (var (name, r, g, b) in quickColors)
            {
                var color = Color.FromRgb(r, g, b);
                var swatch = new Border
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand,
                    ToolTip = name,
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    if (_corsairSync?.IsAvailable == true)
                        _ = _corsairSync.SetStaticColorAllAsync(r, g, b);
                };
                colorWrap.Children.Add(swatch);
            }
            _sceneContent.Children.Add(colorWrap);
        }

        // Brightness
        var brightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        brightRow.Children.Add(MakeSubLabel("BRIGHTNESS"));
        var brightSlider = new StyledSlider
        {
            Minimum = 50, Maximum = 200, Value = _config.Corsair.LightBrightness,
            Width = 140, Height = 35,
            AccentColor = corsairYellow,
            ShowLabel = false,
        };
        var brightLabel = new TextBlock
        {
            Text = $"{_config.Corsair.LightBrightness}%",
            FontSize = 12, Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        brightSlider.ValueChanged += (_, _) =>
        {
            if (_config == null) return;
            _config.Corsair.LightBrightness = (int)brightSlider.Value;
            brightLabel.Text = $"{(int)brightSlider.Value}%";
            QueueSave();
        };
        brightRow.Children.Add(brightSlider);
        brightRow.Children.Add(brightLabel);
        _sceneContent.Children.Add(brightRow);
    }

    // Color palettes (same as Lights tab)
    private static readonly (string Name, Color[] Colors)[] ColorPalettes = new[]
    {
        ("Sunset",    new[] { Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x6B, 0x35), Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0x45, 0x00) }),
        ("Ocean",     new[] { Color.FromRgb(0x00, 0x33, 0x66), Color.FromRgb(0x00, 0x77, 0xB6), Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x00, 0xB4, 0xD8), Color.FromRgb(0x48, 0xCA, 0xE4) }),
        ("Neon",      new[] { Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0xFF, 0x00, 0x80), Color.FromRgb(0x80, 0x00, 0xFF), Color.FromRgb(0x00, 0xFF, 0x80) }),
        ("Forest",    new[] { Color.FromRgb(0x00, 0x44, 0x00), Color.FromRgb(0x00, 0x88, 0x33), Color.FromRgb(0x00, 0xC8, 0x53), Color.FromRgb(0xAE, 0xD5, 0x81), Color.FromRgb(0x76, 0xFF, 0x03) }),
        ("Lava",      new[] { Color.FromRgb(0x8B, 0x00, 0x00), Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0xFF, 0x8A, 0x00), Color.FromRgb(0xFF, 0xD6, 0x00) }),
        ("Arctic",    new[] { Color.FromRgb(0xE0, 0xF7, 0xFA), Color.FromRgb(0x80, 0xDE, 0xEA), Color.FromRgb(0x00, 0xBD, 0xD0), Color.FromRgb(0x00, 0x97, 0xA7), Color.FromRgb(0xB2, 0xEB, 0xF2) }),
        ("Galaxy",    new[] { Color.FromRgb(0x1A, 0x00, 0x5C), Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0xFF, 0x80, 0xAB), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xE0, 0x40, 0xFF) }),
        ("Toxic",     new[] { Color.FromRgb(0x00, 0x33, 0x00), Color.FromRgb(0x00, 0xE6, 0x76), Color.FromRgb(0x76, 0xFF, 0x03), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0xCC, 0xFF, 0x00) }),
        ("Inferno",   new[] { Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0xD6, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00) }),
        ("Vaporwave", new[] { Color.FromRgb(0xFF, 0x71, 0xCE), Color.FromRgb(0x01, 0xCD, 0xFE), Color.FromRgb(0xB9, 0x67, 0xFF), Color.FromRgb(0x05, 0xFC, 0xC1), Color.FromRgb(0xFF, 0x00, 0xA0) }),
        ("Ember",     new[] { Color.FromRgb(0x8B, 0x00, 0x00), Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0xFF, 0x22, 0x00), Color.FromRgb(0xCC, 0x33, 0x00), Color.FromRgb(0x66, 0x00, 0x00) }),
        ("Aurora",    new[] { Color.FromRgb(0x00, 0xFF, 0x87), Color.FromRgb(0x7B, 0x2F, 0xFF), Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0x00, 0xFF, 0x00) }),
    };

    private Color _roomColor1 = ThemeManager.Accent;
    private Color _roomColor2 = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private string? _roomActivePreset;

    private void BuildGlobalTab()
    {
        if (_sceneContent == null) return;

        _sceneContent.Children.Add(new TextBlock
        {
            Text = "Shared controls that sync to all room devices (Govee + Corsair) simultaneously.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // ── Effect Picker ──
        _sceneContent.Children.Add(MakeSubLabel("EFFECT"));
        var effectPicker = new Controls.EffectPickerControl(showGlobal: true)
        {
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Choose the room lighting effect — synced to Govee + Corsair",
        };
        if (_activePattern != null)
        {
            // Try to select the matching effect
            effectPicker.SelectedEffect = Enum.TryParse<LightEffect>(_activePattern, true, out var eff) ? eff : LightEffect.SingleColor;
        }
        effectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var effect = effectPicker.SelectedEffect;
            string effectName = effect.ToString();

            if (effect == LightEffect.SingleColor)
            {
                // Static color — stop patterns, send current room color
                StopRoomPattern();
                SendRoomColor(_roomColor1.R, _roomColor1.G, _roomColor1.B);
            }
            else
            {
                // Start the pattern based on selected effect
                StartRoomPattern(effectName);
            }
        };
        _sceneContent.Children.Add(effectPicker);

        // ── Color Presets ──
        _sceneContent.Children.Add(MakeSubLabel("PRESETS"));
        var presetWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };

        // Solid preset
        var solidBrush = new SolidColorBrush(_roomColor1);
        var solidTile = MakePresetTile("Solid", solidBrush, new[] { _roomColor1 }, presetWrap);
        presetWrap.Children.Add(solidTile);

        // Gradient presets
        foreach (var (name, colors) in ColorPalettes)
        {
            var gradientBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int ci = 0; ci < colors.Length; ci++)
                gradientBrush.GradientStops.Add(new GradientStop(colors[ci], ci / (double)(colors.Length - 1)));
            presetWrap.Children.Add(MakePresetTile(name, gradientBrush, colors, presetWrap));
        }
        _sceneContent.Children.Add(presetWrap);

        // ── Manual Color Pickers ──
        _sceneContent.Children.Add(MakeSubLabel("COLOR"));
        var colorRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };

        // Primary color picker
        var primary = MakeRoomColorPill("PRIMARY", _roomColor1, false);
        colorRow.Children.Add(primary);

        // Secondary color picker
        var secondary = MakeRoomColorPill("SECONDARY", _roomColor2, true);
        colorRow.Children.Add(secondary);
        _sceneContent.Children.Add(colorRow);

        // ── Speed slider ──
        _sceneContent.Children.Add(MakeSubLabel("SPEED"));
        var speedSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
            Margin = new Thickness(0, 0, 0, 12),
        };
        speedSlider.ValueChanged += (_, _) =>
        {
            // Update the headless RgbController's effect speed
            if (_roomRgb != null)
            {
                var gl = new GlobalLightConfig
                {
                    Enabled = true,
                    Effect = Enum.TryParse<LightEffect>(_activePattern, true, out var eff) ? eff : LightEffect.RainbowWave,
                    R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                    R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                    EffectSpeed = (int)speedSlider.Value,
                };
                _roomRgb.UpdateGlobalConfig(gl);
            }
        };
        _sceneContent.Children.Add(speedSlider);

        // Sync to Amp Up toggle
        _sceneContent.Children.Add(MakeSeparator());
        var syncCheck = new CheckBox
        {
            Content = "Sync to Amp Up — mirror knob LED effects to all room devices",
            IsChecked = _config?.Ambience.LinkToLights == true,
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        syncCheck.Checked += (_, _) =>
        {
            if (_loading || _config == null) return;
            StopRoomPattern();
            _config.Ambience.LinkToLights = true;
            _config.Corsair.LightSyncMode = "vu_reactive";
            QueueSave();
        };
        syncCheck.Unchecked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.LinkToLights = false;
            QueueSave();
        };
        _sceneContent.Children.Add(syncCheck);
    }

    // ── Room preset/color helpers ──────────────────────────────────

    private Border MakePresetTile(string name, Brush tileBg, Color[] colors, WrapPanel wrap)
    {
        var tileContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var gradientBorder = new Border
        {
            Width = 46, Height = 24,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = tileBg,
        };
        tileContent.Children.Add(gradientBorder);
        tileContent.Children.Add(new TextBlock
        {
            Text = name, FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });

        bool isActive = _roomActivePreset == name;
        var tile = new Border
        {
            Background = isActive
                ? new SolidColorBrush(Color.FromArgb(0x30, colors[0].R, colors[0].G, colors[0].B))
                : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = isActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 4, 4, 3),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            Child = tileContent,
            ToolTip = name,
        };

        tile.MouseEnter += (_, _) =>
        {
            if (_roomActivePreset != name)
            {
                tile.BorderBrush = new SolidColorBrush(Colors.White);
                tile.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (_roomActivePreset != name)
            {
                tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            }
        };

        var capturedColors = colors;
        tile.MouseLeftButtonDown += (_, _) =>
        {
            _roomColor1 = capturedColors[0];
            _roomColor2 = capturedColors[capturedColors.Length > 1 ? capturedColors.Length - 1 : 0];
            _roomActivePreset = name;

            // Send primary color to all room devices
            SendRoomColor(_roomColor1.R, _roomColor1.G, _roomColor1.B);
            RefreshSceneContent();
        };

        return tile;
    }

    private Border MakeRoomColorPill(string label, Color initial, bool isSecondary)
    {
        var dot = new Border
        {
            Width = 16, Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(initial),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var labelBlock = new TextBlock
        {
            Text = label, FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(labelBlock);

        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x33, initial.R, initial.G, initial.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, initial.R, initial.G, initial.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 4),
            Cursor = Cursors.Hand,
            ToolTip = $"{label} color — click to change",
            Child = row,
        };

        pill.MouseLeftButtonDown += (_, _) =>
        {
            var current = isSecondary ? _roomColor2 : _roomColor1;
            var dialog = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                var c = dialog.SelectedColor;
                if (isSecondary) _roomColor2 = c; else _roomColor1 = c;
                dot.Background = new SolidColorBrush(c);
                pill.Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
                pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, c.R, c.G, c.B));
                _roomActivePreset = null;
                if (!isSecondary) SendRoomColor(c.R, c.G, c.B);
            }
        };

        return pill;
    }

    private void SendRoomColor(byte r, byte g, byte b)
    {
        StopRoomPattern();
        // Govee LAN
        if (_config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;
                AmbienceSync.PauseSync(dev.Ip, 30);
                _ = AmbienceSync.SendColorAsync(dev.Ip, r, g, b);
            }
        // Corsair
        if (_corsairSync?.IsAvailable == true && _config?.Corsair.Enabled == true)
        {
            _config!.Corsair.LightSyncMode = "static";
            _ = _corsairSync.SetStaticColorAllAsync(r, g, b);
        }
        // Govee Cloud
        if (_cloudApi != null)
            foreach (var dev in _cloudDevices)
                _ = SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                    dev.Device, dev.Sku, GoveeCloudApi.SetColor(r, g, b)));
    }

    // ── Room Pattern Engine (headless RgbController) ──────────────

    private void StartRoomPattern(string patternId)
    {
        StopRoomPattern();
        _activePattern = patternId;

        // Disable other sync modes so pattern isn't overwritten
        if (_config != null)
        {
            _config.Ambience.LinkToLights = false;
            _config.Corsair.LightSyncMode = "static";
        }

        // Pause Govee ambient sync
        if (_config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
                if (!string.IsNullOrWhiteSpace(dev.Ip))
                    AmbienceSync.PauseSync(dev.Ip, 99999);

        // Create a headless RgbController to render effects
        _roomRgb = new RgbController();
        _roomRgb.SetBrightness(100);

        // Set knob positions to full so effects render at full brightness
        for (int k = 0; k < 5; k++)
            _roomRgb.SetKnobPosition(k, 1.0f);

        // Configure as global lighting with the selected effect
        if (Enum.TryParse<LightEffect>(patternId, true, out var effect))
        {
            var gl = new GlobalLightConfig
            {
                Enabled = true,
                Effect = effect,
                R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                EffectSpeed = 50,
            };
            _roomRgb.UpdateGlobalConfig(gl);
        }

        // Subscribe to rendered frames
        _roomRgb.OnFrameReady += OnRoomFrame;

        // Start with a dummy output (no serial port — just runs the timer for rendering)
        _roomRgb.SetOutput((_, _, _) => { }, () => true);
    }

    private void StopRoomPattern()
    {
        if (_roomRgb != null)
        {
            _roomRgb.OnFrameReady -= OnRoomFrame;
            _roomRgb.SetOutput(null, null);
            _roomRgb.Dispose();
            _roomRgb = null;
        }
        _activePattern = null;

        // Resume Govee sync
        if (_config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
                if (!string.IsNullOrWhiteSpace(dev.Ip))
                    AmbienceSync.PauseSync(dev.Ip, 0);
    }

    private void OnRoomFrame(byte[] linearColors)
    {
        if (_config == null) return;

        // Average the 15 LED colors to get a single room color
        int totalR = 0, totalG = 0, totalB = 0;
        for (int i = 0; i < 15; i++)
        {
            totalR += linearColors[i * 3];
            totalG += linearColors[i * 3 + 1];
            totalB += linearColors[i * 3 + 2];
        }
        byte r = (byte)(totalR / 15);
        byte g = (byte)(totalG / 15);
        byte b = (byte)(totalB / 15);

        // Send to Govee (rate limited by AmbienceSync)
        if (_config.Ambience.GoveeEnabled)
        {
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;
                _ = AmbienceSync.SendColorAsync(dev.Ip, r, g, b);
            }
        }

        // Send full 15-LED frame to Corsair (maps across all device LEDs)
        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
        {
            float boost = _config.Corsair.LightBrightness / 100f;
            var boosted = new byte[45];
            for (int i = 0; i < 45; i++)
                boosted[i] = (byte)Math.Min(linearColors[i] * boost, 255);
            _corsairSync.SyncColors(boosted);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  COLORS (Solid + Scenes)
    // ══════════════════════════════════════════════════════════════════

    private void BuildColorsSection(GoveeDeviceInfo device, StackPanel container, string? lanIp, CheckBox? powerCheck)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(wrap);

        // Always add Solid tile first
        BuildSolidTile(wrap, device, lanIp);

        // Add scene tiles only if Cloud API is connected
        if (_cloudApi == null) return;

        var scenes = GoveeCloudApi.ExtractScenesFromCapabilities(device.RawCapabilities, "dynamic");
        if (scenes.Count > 0)
        {
            RenderSceneTilesIntoWrap(wrap, device, scenes, powerCheck);
        }
        else
        {
            _ = FetchScenesIntoWrapAsync(device, wrap, powerCheck);
        }
    }

    private void BuildSolidTile(WrapPanel wrap, GoveeDeviceInfo device, string? lanIp)
    {
        var solidColor = Color.FromRgb(0x00, 0xE6, 0x76);
        var tile = new Border
        {
            Width = 82, Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Set a solid color",
            Tag = "__solid__",
        };

        var tileContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tileContent.Children.Add(new TextBlock
        {
            Text = "🎨",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(solidColor),
        });
        tileContent.Children.Add(new TextBlock
        {
            Text = "Solid",
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        tile.Child = tileContent;

        tile.MouseEnter += (_, _) =>
        {
            tile.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, solidColor.R, solidColor.G, solidColor.B));
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        };

        tile.MouseLeftButtonUp += (_, _) =>
        {
            foreach (var child in wrap.Children)
                if (child is Border b)
                {
                    b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }

            tile.Background = new SolidColorBrush(Color.FromArgb(0x30, solidColor.R, solidColor.G, solidColor.B));
            tile.BorderBrush = new SolidColorBrush(solidColor);

            var current = Colors.White;
            var dialog = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                var c = dialog.SelectedColor;
                if (lanIp != null)
                {
                    AmbienceSync.PauseSync(lanIp, 30);
                    _ = AmbienceSync.SendColorAsync(lanIp, c.R, c.G, c.B);
                }
                else if (_cloudApi != null)
                    _ = SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                        device.Device, device.Sku, GoveeCloudApi.SetColor(c.R, c.G, c.B)));

                // Sync to Corsair iCUE
                if (_corsairSync?.IsAvailable == true && _config?.Corsair.Enabled == true)
                    _ = _corsairSync.SetStaticColorAllAsync(c.R, c.G, c.B);
            }
        };

        wrap.Children.Add(tile);
    }

    private async Task FetchScenesIntoWrapAsync(GoveeDeviceInfo device, WrapPanel wrap, CheckBox? powerCheck = null)
    {
        try
        {
            var scenes = await _cloudApi!.GetDynamicScenesAsync(device.Device, device.Sku);
            Dispatcher.Invoke(() =>
            {
                if (scenes != null && scenes.Count > 0)
                    RenderSceneTilesIntoWrap(wrap, device, scenes, powerCheck);
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[Ambience] Scene fetch error: {ex.Message}");
        }
    }

    private void RenderSceneTilesIntoWrap(WrapPanel wrap, GoveeDeviceInfo device, List<GoveeScene> scenes, CheckBox? powerCheck = null)
    {
        string? activeSceneId = null;

        foreach (var scene in scenes)
        {
            var sceneId = scene.Id;
            var tileColor = GetSceneTileColor(scene.Name);
            var icon = GetSceneIcon(scene.Name);

            var tile = new Border
            {
                Width = 82, Height = 58,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = scene.Name,
                Tag = sceneId,
            };

            var tileContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tileContent.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(
                    (byte)(tileColor.R * 0.7), (byte)(tileColor.G * 0.7), (byte)(tileColor.B * 0.7))),
            });
            tileContent.Children.Add(new TextBlock
            {
                Text = scene.Name,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 74,
                Margin = new Thickness(0, 2, 0, 0),
            });
            tile.Child = tileContent;

            tile.MouseEnter += (_, _) =>
            {
                if (tile.Tag as string != activeSceneId)
                {
                    tile.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
                    tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, tileColor.R, tileColor.G, tileColor.B));
                }
            };
            tile.MouseLeave += (_, _) =>
            {
                if (tile.Tag as string != activeSceneId)
                {
                    tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }
            };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                foreach (var child in wrap.Children)
                {
                    if (child is Border b && b.Tag as string != sceneId)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    }
                }

                tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                tile.BorderBrush = new SolidColorBrush(tileColor);
                activeSceneId = sceneId;

                var sc = scenes.FirstOrDefault(s => s.Id == sceneId);
                var sceneValue = sc?.RawValue ?? (object)new { id = sceneId };
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetScene(sceneValue)));

                // Sync scene color to Corsair iCUE
                if (_corsairSync?.IsAvailable == true && _config?.Corsair.Enabled == true)
                    _ = _corsairSync.SetStaticColorAllAsync(tileColor.R, tileColor.G, tileColor.B);

                if (powerCheck != null && powerCheck.IsChecked != true)
                {
                    _loading = true;
                    powerCheck.IsChecked = true;
                    _loading = false;
                }
            };

            wrap.Children.Add(tile);
        }
    }

    // ── Music Mode ────────────────────────────────────────────────────

    private static readonly (int id, string name, string icon, Color color)[] MusicModes =
    {
        (3, "Rhythm", "🎵", Color.FromRgb(0x69, 0xF0, 0xAE)),
        (4, "Rolling", "🌊", Color.FromRgb(0x64, 0xB5, 0xF6)),
        (5, "Energic", "⚡", Color.FromRgb(0xFF, 0xD7, 0x40)),
        (6, "Spectrum", "🌈", Color.FromRgb(0xBA, 0x68, 0xC8)),
    };

    private void BuildMusicSection(GoveeDeviceInfo device, StackPanel parent)
    {
        int? activeModeId = null;

        var sensSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 40,
            Suffix = "",
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 4, 0, 8),
        };

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        foreach (var (modeId, modeName, icon, tileColor) in MusicModes)
        {
            var tile = new Border
            {
                Width = 82, Height = 58,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = modeName,
                Tag = modeId,
            };

            var tileContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tileContent.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(
                    (byte)(tileColor.R * 0.7), (byte)(tileColor.G * 0.7), (byte)(tileColor.B * 0.7))),
            });
            tileContent.Children.Add(new TextBlock
            {
                Text = modeName,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0),
            });
            tile.Child = tileContent;

            tile.MouseEnter += (_, _) =>
            {
                if (tile.Tag is int id && id != activeModeId)
                {
                    tile.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
                    tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, tileColor.R, tileColor.G, tileColor.B));
                }
            };
            tile.MouseLeave += (_, _) =>
            {
                if (tile.Tag is int id && id != activeModeId)
                {
                    tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }
            };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                foreach (var child in wrap.Children)
                    if (child is Border b)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    }

                tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                tile.BorderBrush = new SolidColorBrush(tileColor);
                activeModeId = modeId;

                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(modeId, (int)sensSlider.Value)));
            };

            wrap.Children.Add(tile);
        }

        parent.Children.Add(wrap);

        var sensRow = new StackPanel { Orientation = Orientation.Horizontal };
        sensRow.Children.Add(MakeSubLabel("SENSITIVITY"));
        var sensDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        sensDebounce.Tick += async (_, _) =>
        {
            sensDebounce.Stop();
            if (activeModeId.HasValue)
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(activeModeId.Value, (int)sensSlider.Value)));
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            sensDebounce.Stop();
            sensDebounce.Start();
        };
        sensRow.Children.Add(sensSlider);
        parent.Children.Add(sensRow);
    }

    // ── Scene tile colors ─────────────────────────────────────────────

    private static readonly Color[] ScenePalette =
    {
        Color.FromRgb(0xFF, 0x6B, 0x35),
        Color.FromRgb(0x64, 0xB5, 0xF6),
        Color.FromRgb(0xFF, 0x80, 0xAB),
        Color.FromRgb(0x69, 0xF0, 0xAE),
        Color.FromRgb(0xBA, 0x68, 0xC8),
        Color.FromRgb(0xFF, 0xD7, 0x40),
        Color.FromRgb(0x4D, 0xD0, 0xE1),
        Color.FromRgb(0xFF, 0x52, 0x52),
        Color.FromRgb(0xAE, 0xD5, 0x81),
        Color.FromRgb(0xE0, 0x40, 0xFF),
    };

    private static Color GetSceneTileColor(string name)
    {
        int hash = 0;
        foreach (var c in name) hash = hash * 31 + c;
        return ScenePalette[Math.Abs(hash) % ScenePalette.Length];
    }

    private static string GetSceneIcon(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("snow") || n.Contains("flake")) return "❄";
        if (n.Contains("fire") || n.Contains("flame")) return "🔥";
        if (n.Contains("rain")) return "🌧";
        if (n.Contains("aurora")) return "🌌";
        if (n.Contains("ocean") || n.Contains("stream")) return "🌊";
        if (n.Contains("desert")) return "🏜";
        if (n.Contains("bloom") || n.Contains("flower")) return "🌸";
        if (n.Contains("sand")) return "⏳";
        if (n.Contains("meteor")) return "☄";
        if (n.Contains("tunnel")) return "🕳";
        if (n.Contains("forest")) return "🌲";
        if (n.Contains("sunrise")) return "🌅";
        if (n.Contains("sunset")) return "🌇";
        if (n.Contains("rainbow")) return "🌈";
        if (n.Contains("candle")) return "🕯";
        if (n.Contains("white light") || n.Contains("reading")) return "💡";
        if (n.Contains("glitter") || n.Contains("sparkle")) return "✨";
        if (n.Contains("colorful")) return "🎨";
        if (n.Contains("neon")) return "💜";
        if (n.Contains("romantic") || n.Contains("romance")) return "💕";
        if (n.Contains("party")) return "🎉";
        if (n.Contains("energetic") || n.Contains("energic")) return "⚡";
        if (n.Contains("breathe") || n.Contains("breathing")) return "🫧";
        if (n.Contains("asleep") || n.Contains("sleep")) return "😴";
        if (n.Contains("fright") || n.Contains("horror")) return "👻";
        if (n.Contains("siren")) return "🚨";
        if (n.Contains("drum") || n.Contains("beat")) return "🥁";
        if (n.Contains("movie") || n.Contains("film")) return "🎬";
        if (n.Contains("comedy") || n.Contains("comedies")) return "😂";
        if (n.Contains("action")) return "💥";
        if (n.Contains("suspense") || n.Contains("thriller")) return "😱";
        if (n.Contains("documentary") || n.Contains("documentaries")) return "📽";
        if (n.Contains("war")) return "⚔";
        if (n.Contains("science fiction") || n.Contains("sci-fi")) return "🚀";
        if (n.Contains("season")) return "🍂";
        if (n.Contains("christmas") || n.Contains("xmas")) return "🎄";
        if (n.Contains("halloween")) return "🎃";
        if (n.Contains("crossing")) return "🚦";
        if (n.Contains("literary")) return "📖";
        if (n.Contains("pulse")) return "💓";
        return "◆";
    }

    // ── Device state & knob updates ──────────────────────────────────

    public void UpdateDeviceBrightness(string ip, float normalized, bool poweredOn)
    {
        if (!_deviceControls.TryGetValue(ip, out var controls)) return;
        _loading = true;
        controls.onOff.IsChecked = poweredOn;
        controls.brightness.Value = Math.Max(1, (int)Math.Round(normalized * 100));
        _loading = false;
    }

    public void UpdateAllDeviceBrightness(float normalized, bool poweredOn)
    {
        _loading = true;
        foreach (var (_, controls) in _deviceControls)
        {
            controls.onOff.IsChecked = poweredOn;
            controls.brightness.Value = Math.Max(1, (int)Math.Round(normalized * 100));
        }
        _loading = false;
    }

    private async Task QueryDeviceStateAsync(string ip, CheckBox onOffCheck, StyledSlider brightnessSlider)
    {
        try
        {
            var status = await AmbienceSync.GetDeviceStatusAsync(ip);
            if (status == null) return;

            var dev = _config?.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == ip);
            if (dev != null) dev.PoweredOn = status.Value.On;

            Dispatcher.Invoke(() =>
            {
                _loading = true;
                onOffCheck.IsChecked = status.Value.On;
                brightnessSlider.Value = Math.Max(1, status.Value.Brightness);
                _loading = false;
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[Ambience] Device state query failed ({ip}): {ex.Message}");
        }
    }

    // ── LAN/Cloud routing ──────────────────────────────────────────────

    private string? FindLanIp(GoveeDeviceInfo device)
    {
        if (_config == null || !_config.Ambience.GoveeEnabled) return null;
        foreach (var lan in _config.Ambience.GoveeDevices)
        {
            if (!string.IsNullOrWhiteSpace(lan.DeviceId) && lan.DeviceId == device.Device)
                return lan.Ip;
            if (!string.IsNullOrWhiteSpace(lan.Sku) && lan.Sku == device.Sku)
                return lan.Ip;
        }
        return null;
    }

    private static async Task SafeCloudCall(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Logger.Log($"[Ambience] Cloud API error: {ex.Message}"); }
    }

    // ── Panel dimming (Screen Sync active) ─────────────────────────────

    private void SetDevicePanelDimmed(bool syncActive)
    {
        // All cards stay interactive — scenes/devices work independently of Screen Sync
    }

    // ── Setup card ──────────────────────────────────────────────────────

    private Border MakeSetupCard(string title, string description, string buttonText, Action onClick)
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        var btn = new Button
        {
            Content = buttonText,
            Padding = new Thickness(16, 6, 16, 6),
            FontSize = 12,
        };
        btn.Click += (_, _) => onClick();
        stack.Children.Add(btn);

        return card;
    }

    // ── Save ──────────────────────────────────────────────────────────

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Save()
    {
        if (_config == null || _onSave == null || _loading) return;
        _onSave(_config);
    }

    // ── UI Helpers ────────────────────────────────────────────────────

    private (Border bar, TextBlock label) MakeSectionHeader(string text)
    {
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sectionHeaders.Add((bar, label));
        return (bar, label);
    }

    private StackPanel WrapHeader(Border bar, TextBlock label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        row.Children.Add(bar);
        row.Children.Add(label);
        return row;
    }

    private TextBlock MakeSubLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = FindBrush("CardBorderBrush"),
        Margin = new Thickness(0, 4, 0, 12),
    };

    private TextBlock MakeErrorText(string text) => new()
    {
        Text = text,
        Foreground = FindBrush("DangerRedBrush"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 8),
    };

    // ── Accent color refresh ──────────────────────────────────────────

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            if (bar != null) bar.Background = new SolidColorBrush(accent);
            if (label != null) label.Foreground = new SolidColorBrush(accent);
        }
    }

    // ── Resource helpers ──────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
