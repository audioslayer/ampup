using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Controls;
using Newtonsoft.Json.Linq;

namespace AmpUp.Views;

public partial class RoomView : UserControl
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

    // _devicesCard and _scenesCard removed — unified into single room card

    // Corsair iCUE
    private CorsairSync? _corsairSync;

    // LG UltraGear monitor
    private LgMonitorSync? _lgMonitor;
    private StackPanel? _corsairDeviceRows;
    private DispatcherTimer? _corsairMusicTimer;

    // Music reactive shared state
    private volatile float[]? _globalMusicBands;
    private DispatcherTimer? _goveeLanMusicTimer;
    private string? _goveeLanMusicIp;

    // VU Fill mode — per-segment VU meters driven by audio
    private bool _vuFillActive;
    private DispatcherTimer? _vuFillTimer;
    private readonly float[] _vuFillSmoothed = new float[5];
    private readonly float[] _vuFillPeaks = new float[15]; // per-segment brightness for animated modes
    private int _vuFillTick;
    private float _vuAvgEnergy; // running average for onset detection
    private bool _vuLastOnset; // debounce onset detection
    private readonly Dictionary<string, DateTime> _vuBulbLastSend = new();

    // Room pattern engine — headless RgbController for rendering effects
    private RgbController? _roomRgb;
    private string? _activePattern;
    private bool _roomPatternCorsairOnly;
    private DateTime _lastCloudRoomSend = DateTime.MinValue;

    // Room layout
    private AmpUp.Core.Engine.SpatialMapper? _spatialMapper;
    private AmpUp.Core.Engine.ScreenSpatialMapper? _screenSpatialMapper;
    private Controls.RoomCanvasControl? _roomCanvas;
    private Controls.ScreenEdgeControl? _screenEdgeControl;

    // Home Assistant integration (for HA light room sync)
    private HAIntegration? _ha;
    private readonly Dictionary<string, DateTime> _haLastSend = new();

    public void SetHAIntegration(HAIntegration? ha) { _ha = ha; }
    private Color _corsairColor1 = Color.FromRgb(0xFF, 0xD3, 0x00);
    private Color _corsairColor2 = Color.FromRgb(0xFF, 0x70, 0x00);

    // Navigation callback (set by MainWindow to navigate to Settings)
    public Action? NavigateToSettings { get; set; }
    public bool IsMusicReactiveActive => _corsairMusicTimer?.IsEnabled == true || _vuFillActive
        || (_roomRgb != null && _activePattern == "AudioReactive");

    public RoomView()
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

    public void SetLgMonitor(LgMonitorSync? lgMonitor)
    {
        _lgMonitor = lgMonitor;
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;
        _loading = false;

        // Restore persisted room colors and speed
        try
        {
            if (!string.IsNullOrEmpty(config.Ambience.RoomColor1))
                _roomColor1 = (Color)ColorConverter.ConvertFromString(config.Ambience.RoomColor1);
            if (!string.IsNullOrEmpty(config.Ambience.RoomColor2))
                _roomColor2 = (Color)ColorConverter.ConvertFromString(config.Ambience.RoomColor2);
            _roomEffectSpeed = config.Ambience.RoomEffectSpeed > 0 ? config.Ambience.RoomEffectSpeed : 50;
        }
        catch { }

        // Restore active room effect on startup (deferred so sync/dreamSync are wired up first).
        // Skip if screen sync is enabled — it has exclusive control of room lights.
        if (!string.IsNullOrEmpty(config.Ambience.RoomEffect)
            && config.Ambience.GoveeEnabled
            && !config.Ambience.ScreenSync.Enabled)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (_config?.Ambience.GoveeEnabled == true
                    && !string.IsNullOrEmpty(_config.Ambience.RoomEffect)
                    && !_config.Ambience.ScreenSync.Enabled)
                {
                    StartRoomPattern(_config.Ambience.RoomEffect);
                }
            });
        }

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

        // Always initialize spatial mapper when devices exist (spatial mode is the default)
        if (config.RoomLayout.Devices.Count > 0)
        {
            _spatialMapper = new AmpUp.Core.Engine.SpatialMapper();
            _spatialMapper.Recalculate(config.RoomLayout);
            _sync?.SetSpatialMapper(_spatialMapper);
            // Mirror is the default — don't auto-enable spatial mode
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

        // ── Room Lighting (unified — effects, colors, devices, screen sync) ──
        DevicePanel.Children.Add(BuildRoomCard());

    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 1: ROOM LIGHTING (unified)
    // ══════════════════════════════════════════════════════════════════

    private int _roomTabIndex = 0; // 0=Room Effect, 1=Layout, 2=Devices
    private StackPanel? _roomTabContent;

    private StackPanel? _screenSyncSettingsPanel;
    private StackPanel? _toggleRowContainer;

    private Border BuildRoomCard()
    {
        var wrapper = new Border { Margin = new Thickness(0) };
        var stack = new StackPanel();
        wrapper.Child = stack;

        // ── Tab bar in its own card ──
        var tabCard = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var accent = ThemeManager.Accent;
        var tabNames = new[] { "ROOM EFFECT", "LAYOUT", "DEVICES" };
        var tabCount = tabNames.Length;
        var tabBorders = new Border[tabCount];

        var tabContainer = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0),
        };
        tabContainer.SetResourceReference(Border.BorderBrushProperty, "InputBgBrush");
        var tabRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (int i = 0; i < tabCount; i++)
        {
            var idx = i;
            var tab = new Border
            {
                Padding = new Thickness(22, 10, 22, 10),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0, 0, 0, 2), // 2px underline slot
                Background = new SolidColorBrush(Colors.Transparent),
            };
            tab.Child = new TextBlock
            {
                Text = tabNames[i], FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextSecBrush"),
            };
            tabBorders[i] = tab;
            tab.MouseEnter += (_, _) =>
            {
                if (idx != _roomTabIndex && tab.Child is TextBlock t)
                    t.Foreground = FindBrush("TextPrimaryBrush");
            };
            tab.MouseLeave += (_, _) =>
            {
                if (idx != _roomTabIndex && tab.Child is TextBlock t)
                    t.Foreground = FindBrush("TextSecBrush");
            };
            tab.MouseLeftButtonDown += (_, _) =>
            {
                _roomTabIndex = idx;
                for (int j = 0; j < tabCount; j++)
                    SetRoomTabActive(tabBorders[j], j == idx, ThemeManager.Accent);
                RebuildRoomTabContent();
            };
            tabRow.Children.Add(tab);
        }
        for (int i = 0; i < tabCount; i++)
            SetRoomTabActive(tabBorders[i], i == _roomTabIndex, accent);
        tabContainer.Child = tabRow;
        tabCard.Child = tabContainer;
        stack.Children.Add(tabCard);

        // Dynamic toggle row — rebuilt per tab in RebuildRoomTabContent
        _toggleRowContainer = new StackPanel();
        stack.Children.Add(_toggleRowContainer);

        _roomTabContent = new StackPanel();
        stack.Children.Add(_roomTabContent);

        RebuildRoomTabContent();
        return wrapper;
    }

    private static void SetRoomTabActive(Border tab, bool active, Color accent)
    {
        var label = tab.Child as TextBlock;
        if (active)
        {
            // Accent underline, bright label
            tab.BorderBrush = new SolidColorBrush(accent);
            tab.BorderThickness = new Thickness(0, 0, 0, 2);
            if (label != null)
            {
                label.Foreground = new SolidColorBrush(accent);
                label.FontWeight = FontWeights.Bold;
            }
        }
        else
        {
            // No underline, muted label
            tab.BorderBrush = new SolidColorBrush(Colors.Transparent);
            tab.BorderThickness = new Thickness(0, 0, 0, 2);
            if (label != null)
            {
                label.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
                label.FontWeight = FontWeights.SemiBold;
            }
        }
    }

    private void RebuildRoomTabContent()
    {
        if (_roomTabContent == null || _config == null) return;
        _loading = true;
        _roomTabContent.Children.Clear();
        _toggleRowContainer?.Children.Clear();

        // Build tab-specific toggle row (floating, no card wrapper)
        if (_toggleRowContainer != null && _roomTabIndex == 0)
        {
            var toggleRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            BuildTabToggleRow(toggleRow, _roomTabIndex);
            _toggleRowContainer.Children.Add(toggleRow);
            BuildToggleSettingsRow(_toggleRowContainer);
        }

        switch (_roomTabIndex)
        {
            case 0: BuildRoomEffectTab(_roomTabContent); break;
            case 1: BuildLayoutTab(_roomTabContent); break;
            case 2: BuildDevicesTab(_roomTabContent); break;
        }
        _loading = false;
    }

    private void BuildTabToggleRow(WrapPanel row, int tabIndex)
    {
        if (tabIndex != 0 || _config == null) return; // Only Room Effect tab has toggle row

        // Amp Up sync
        bool syncActive = _activePattern == "__sync__";
        row.Children.Add(BuildToggleTile("🔗", "AMP UP", "Mirror knob LEDs to room",
            syncActive, on =>
            {
                if (_config == null) return;
                if (on) { ResumeAllGoveeSync(); StopRoomPattern(); _activePattern = "__sync__"; _config.Ambience.LinkToLights = true; _config.Corsair.LightSyncMode = "vu_reactive"; }
                else { _activePattern = null; _config.Ambience.LinkToLights = false; _config.Corsair.LightSyncMode = "static"; }
                QueueSave(); RebuildRoomTabContent();
            }, Color.FromRgb(0x69, 0xF0, 0xAE)));

        // Music Reactive
        bool globalMusic = _corsairMusicTimer?.IsEnabled == true && !_vuFillActive;
        row.Children.Add(BuildToggleTile("♪", "MUSIC REACTIVE", "Audio-driven brightness",
            globalMusic, on =>
            {
                if (_loading) return;
                if (on) { StopVuFill(); StartGlobalMusicSync(); }
                else { StopCorsairMusicSync(); if (_activePattern == "AudioReactive") StopRoomPattern(); }
                QueueSave(); RebuildRoomTabContent();
            }, Color.FromRgb(0xFF, 0xB8, 0x00)));

        // VU Fill — segments fill up like VU meters with music
        row.Children.Add(BuildToggleTile("≡", "VU FILL", "Segments fill with music energy",
            _vuFillActive, on =>
            {
                if (_loading) return;
                if (on) { StopCorsairMusicSync(); StartVuFill(); }
                else StopVuFill();
                QueueSave(); RebuildRoomTabContent();
            }, Color.FromRgb(0xFF, 0x40, 0x81)));

        // Screen Sync
        bool syncRunning = _config.Ambience.ScreenSync.Enabled;
        var screenSyncTile = BuildToggleTile("⬛", "SCREEN SYNC", "Capture screen colors to room lights",
            syncRunning, on =>
            {
                if (_loading || _config == null) return;
                _config.Ambience.ScreenSync.Enabled = on;
                if (on)
                {
                    // Stop everything so screen sync has exclusive control
                    StopCorsairMusicSync();
                    StopVuFill();
                    ResumeAllGoveeSync();
                    StopRoomPattern();
                    _config.Ambience.LinkToLights = false;
                    if (_config.Corsair.Enabled)
                        _config.Corsair.LightSyncMode = "dreamview";
                }
                else
                {
                    // DreamSync.Stop() disables segments — clear AmbienceSync tracking
                    _sync?.ClearAllSegmentTracking();
                    if (_config.Corsair.Enabled)
                        _config.Corsair.LightSyncMode = "vu_reactive";
                    // Restart the room effect if one was selected
                    if (_activePattern != null && _activePattern != "__sync__")
                        StartRoomPattern(_activePattern);
                }
                _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                QueueSave();
                RebuildRoomTabContent(); // update Music Reactive / VU Fill toggle states
            }, Color.FromRgb(0x44, 0x8A, 0xFF),
            syncRunning ? "ACTIVE" : "STANDBY",
            out var statusUpdater);
        row.Children.Add(screenSyncTile);

        // Game Mode — auto-enable screen sync when fullscreen app detected
        bool gameMode = _config.Ambience.GameModeEnabled;
        row.Children.Add(BuildToggleTile("🎮", "GAME MODE", gameMode
                ? "Screen sync activates in fullscreen apps"
                : "Auto-sync when fullscreen game detected",
            gameMode, on =>
            {
                if (_loading || _config == null) return;
                _config.Ambience.GameModeEnabled = on;
                QueueSave();
            }, Color.FromRgb(0xFF, 0x6B, 0x35)));

        // Screen Sync settings panel — always visible with live preview
        if (_screenSyncSettingsPanel != null)
            _roomTabContent?.Children.Remove(_screenSyncSettingsPanel);
        var ssInner = new StackPanel();
        BuildScreenSyncSettings(ssInner, statusUpdater!);
        var ssCard = MakeSectionCard("SCREEN SYNC", ssInner);
        ssCard.Margin = new Thickness(0, 8, 0, 0);
        _screenSyncSettingsPanel = new StackPanel();
        _screenSyncSettingsPanel.Children.Add(ssCard);
    }

    // ── ROOM EFFECT TAB (unified: effect + palette + direction + canvas) ──

    /// <summary>
    /// Settings row below toggle tiles — SPEED/BRIGHTNESS sliders, Music Reactive sensitivity, VU Fill mode pills.
    /// </summary>
    private void BuildToggleSettingsRow(StackPanel container)
    {
        if (_config == null) return;
        bool globalMusic = _corsairMusicTimer?.IsEnabled == true && !_vuFillActive;
        bool hasEffect = _activePattern != null && _activePattern != "__sync__";

        // ── Full-width slider row: SENSITIVITY (if music reactive) + SPEED + BRIGHTNESS ──
        if (hasEffect || globalMusic || _vuFillActive)
        {
            var ac = ThemeManager.Accent;
            var dimBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
            var acBrush = new SolidColorBrush(ac);

            // Helper: build a labeled slider cell (label above, slider + value below)
            UIElement MakeSliderCell(string label, StyledSlider slider, TextBlock valLabel)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                cell.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.SemiBold,
                    Foreground = dimBrush, Margin = new Thickness(0, 0, 0, 4) });
                var row = new DockPanel();
                DockPanel.SetDock(valLabel, Dock.Right);
                row.Children.Add(valLabel);
                row.Children.Add(slider);
                cell.Children.Add(row);
                return cell;
            }

            var slidersGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            int col = 0;

            // SENSITIVITY (only when Music Reactive is on)
            if (globalMusic)
            {
                slidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var sensSlider = new StyledSlider
                {
                    Minimum = 1, Maximum = 100, Value = _config.Ambience.MusicSensitivity,
                    Height = 28, AccentColor = ac, ShowLabel = false, HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                var sensLabel = new TextBlock { Text = $"{_config.Ambience.MusicSensitivity}%", FontSize = 11,
                    Foreground = acBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                sensSlider.ValueChanged += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    _config.Ambience.MusicSensitivity = (int)sensSlider.Value;
                    sensLabel.Text = $"{(int)sensSlider.Value}%";
                    QueueSave();
                };
                var sensCell = MakeSliderCell("SENSITIVITY", sensSlider, sensLabel);
                Grid.SetColumn(sensCell as FrameworkElement, col);
                slidersGrid.Children.Add(sensCell);
                col++;
            }

            // SPEED
            slidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var speedSlider = new StyledSlider
            {
                Minimum = 1, Maximum = 100, Value = _roomEffectSpeed,
                Height = 28, AccentColor = ac, ShowLabel = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var speedLabel = new TextBlock { Text = $"{_roomEffectSpeed}%", FontSize = 11,
                Foreground = acBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            speedSlider.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                _roomEffectSpeed = (int)speedSlider.Value;
                speedLabel.Text = $"{_roomEffectSpeed}%";
                if (_roomRgb != null && Enum.TryParse<LightEffect>(_activePattern, true, out var eff2))
                {
                    _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                    {
                        Enabled = true, Effect = eff2,
                        R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                        R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                        EffectSpeed = _roomEffectSpeed,
                        PaletteName = _roomPalette.Name,
                    });
                }
            };
            var speedCell = MakeSliderCell("SPEED", speedSlider, speedLabel);
            Grid.SetColumn(speedCell as FrameworkElement, col);
            slidersGrid.Children.Add(speedCell);
            col++;

            // BRIGHTNESS
            slidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var brightSlider = new StyledSlider
            {
                Minimum = 1, Maximum = 100, Value = _config.Ambience.BrightnessScale,
                Height = 28, AccentColor = ac, ShowLabel = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var brightLabel = new TextBlock { Text = $"{_config.Ambience.BrightnessScale}%", FontSize = 11,
                Foreground = acBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            brightSlider.ValueChanged += (_, _) =>
            {
                if (_loading || _config == null) return;
                int pct = (int)brightSlider.Value;
                _config.Ambience.BrightnessScale = pct;
                brightLabel.Text = $"{pct}%";
                foreach (var dev in _config.Ambience.GoveeDevices)
                {
                    if (!string.IsNullOrWhiteSpace(dev.Ip) && dev.PoweredOn)
                        _ = AmbienceSync.SendBrightnessAsync(dev.Ip, pct);
                }
                QueueSave();
            };
            var brightCell = MakeSliderCell("BRIGHTNESS", brightSlider, brightLabel);
            Grid.SetColumn(brightCell as FrameworkElement, col);
            slidersGrid.Children.Add(brightCell);

            container.Children.Add(slidersGrid);
        }

        if (_vuFillActive)
        {
            var accent = Color.FromRgb(0xFF, 0x40, 0x81);
            var modes = new[] {
                (VuFillMode.Classic,  "Classic",  "Bottom-to-top energy fill"),
                (VuFillMode.Split,    "Split",    "Left=bass, right=treble"),
                (VuFillMode.Rainfall, "Rainfall", "Onset-triggered drips"),
                (VuFillMode.Pulse,    "Pulse",    "All segments pulse with bass"),
                (VuFillMode.Spectrum, "Spectrum", "Per-segment frequency band"),
                (VuFillMode.Drip,     "Drip",     "Liquid gravity pool"),
            };
            var tileWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (mode, label, tip) in modes)
            {
                bool active = _config.Ambience.VuFillMode == mode;
                var tileLabel = new TextBlock
                {
                    Text = label, FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Foreground = active ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    Margin = new Thickness(0, 3, 0, 0),
                };
                var preview = new Controls.EffectPreviewControl
                {
                    Width = 68, Height = 30,
                    EffectKind = mode switch
                    {
                        VuFillMode.Classic  => LightEffect.Equalizer,
                        VuFillMode.Split    => LightEffect.DNA,
                        VuFillMode.Rainfall => LightEffect.Rainfall,
                        VuFillMode.Pulse    => LightEffect.Pulse,
                        VuFillMode.Spectrum => LightEffect.Equalizer,
                        VuFillMode.Drip     => LightEffect.Drip,
                        _ => LightEffect.Equalizer,
                    },
                    TileColor = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    Opacity = active ? 1.0 : 0.65,
                };
                var tileContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                tileContent.Children.Add(preview);
                tileContent.Children.Add(tileLabel);
                var tile = new Border
                {
                    Width = 82,
                    Background = active ? new SolidColorBrush(Color.FromArgb(0x25, accent.R, accent.G, accent.B))
                        : FindBrush("CardBgBrush"),
                    BorderBrush = active ? new SolidColorBrush(Color.FromArgb(0x77, accent.R, accent.G, accent.B))
                        : FindBrush("CardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4, 5, 4, 5),
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand,
                    Child = tileContent,
                    ToolTip = tip,
                };
                var capturedMode = mode;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    if (_config == null) return;
                    _config.Ambience.VuFillMode = capturedMode;
                    for (int i = 0; i < 15; i++) _vuFillPeaks[i] = 0;
                    QueueSave(); RebuildRoomTabContent();
                };
                var vuTransform = new TranslateTransform(0, 0);
                tile.RenderTransform = vuTransform;
                tile.MouseEnter += (_, _) =>
                {
                    vuTransform.Y = -1;
                    if (!active) { tile.Background = new SolidColorBrush(Color.FromArgb(0x10, accent.R, accent.G, accent.B));
                        tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)); }
                };
                tile.MouseLeave += (_, _) =>
                {
                    vuTransform.Y = 0;
                    if (!active) { tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
                        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush"); }
                };
                tileWrap.Children.Add(tile);
            }
            container.Children.Add(MakeSectionCard("VU FILL MODE", tileWrap));
        }
    }

    // Tab index: 0=Favorites, 1=Static, 2=Animated, 3=Reactive, 4=Global Span
    private int _effectCategory = 4; // default to Global Span

    /// <summary>
    /// Map our room-tab index (0=Favorites, 1=Static...4=Global) to the EffectPickerControl's
    /// internal category index (0=Static...3=Global, 4=Favorites).
    /// </summary>
    private static int TabIndexToPickerCategory(int tabIdx)
    {
        if (tabIdx == 0) return Controls.EffectPickerControl.FavoritesCategoryIndex;
        return tabIdx - 1;
    }

    /// <summary>
    /// Effects whose colors are fully hardcoded — the palette has no effect on the output,
    /// so we hide the palette editor + presets for these to reduce UI clutter.
    /// </summary>
    private static readonly HashSet<LightEffect> PaletteIgnoredEffects = new()
    {
        // Global hardcoded
        LightEffect.Aurora,
        LightEffect.Ocean,
        LightEffect.FireWall,
        LightEffect.NebulaDrift,
        LightEffect.Plasma,
        LightEffect.RainbowScanner,
        // Rainbow-based effects (hardcoded rainbow hues)
        LightEffect.RainbowWave,
        LightEffect.RainbowCycle,
        LightEffect.RainbowWheel,
        LightEffect.RainbowFill,
        // HSV-generated room sweep effects
        LightEffect.Vortex,
        LightEffect.Shockwave,
        LightEffect.Tidal,
        LightEffect.Prism,
        LightEffect.EmberDrift,
        LightEffect.Glitch,
    };

    private static bool EffectIgnoresPalette(LightEffect e) => PaletteIgnoredEffects.Contains(e);

    private void BuildRoomEffectTab(StackPanel stack)
    {
        if (_config == null) return;
        var layout = _config.RoomLayout;

        // ════════════════════════════════════════════════════════════
        // EFFECT card — Category tabs + Effect picker
        // ════════════════════════════════════════════════════════════

        // Category tab bar — Material underline style
        var categoryBarContainer = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 8),
        };
        categoryBarContainer.SetResourceReference(Border.BorderBrushProperty, "InputBgBrush");
        var categoryTabBar = new StackPanel { Orientation = Orientation.Horizontal };
        // Tab index → visible-category index in EffectPickerControl:
        //   0 → 4 (favorites), 1 → 0 (static), 2 → 1 (anim), 3 → 2 (react), 4 → 3 (global)
        var categoryNames = new[] { "FAVORITES", "STATIC", "ANIMATED", "REACTIVE", "GLOBAL SPAN" };
        var categoryTabs = new Border[5];

        var effectPicker = new Controls.EffectPickerControl(showGlobal: true)
        {
            Margin = new Thickness(0, 0, 0, 0),
            IsEnabled = !_vuFillActive,
            Opacity = _vuFillActive ? 0.4 : 1.0,
        };

        // Load persisted favorites into the picker
        var favList = new List<LightEffect>();
        foreach (var name in _config.FavoriteEffects)
        {
            if (Enum.TryParse<LightEffect>(name, true, out var fe))
                favList.Add(fe);
        }
        effectPicker.SetFavorites(favList);

        // Persist favorite toggles
        effectPicker.FavoritesChanged += (_, favs) =>
        {
            if (_config == null) return;
            _config.FavoriteEffects = favs.Select(f => f.ToString()).ToList();
            QueueSave();
        };

        if (_activePattern != null && _activePattern != "__sync__")
            effectPicker.SelectedEffect = Enum.TryParse<LightEffect>(_activePattern, true, out var eff) ? eff : LightEffect.SingleColor;

        // Auto-detect category from selected effect (maps static=0..global=3 → tab index 1..4)
        if (_activePattern != null && _activePattern != "__sync__" && Enum.TryParse<LightEffect>(_activePattern, true, out var selEff))
        {
            int detectedCat = GetEffectCategory(selEff);
            if (detectedCat >= 0) _effectCategory = detectedCat + 1;
        }

        for (int ci = 0; ci < categoryNames.Length; ci++)
        {
            bool active = _effectCategory == ci;
            var accent = ThemeManager.Accent;
            var tab = new Border
            {
                Padding = new Thickness(18, 8, 18, 8),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(active ? accent : Colors.Transparent),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Background = new SolidColorBrush(Colors.Transparent),
            };
            tab.Child = new TextBlock
            {
                Text = categoryNames[ci], FontSize = 10,
                FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = active ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            };
            categoryTabs[ci] = tab;
            int capturedCat = ci;

            // Hover state for inactive tabs
            tab.MouseEnter += (_, _) =>
            {
                if (_effectCategory != capturedCat && tab.Child is TextBlock t)
                    t.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            };
            tab.MouseLeave += (_, _) =>
            {
                if (_effectCategory != capturedCat && tab.Child is TextBlock t)
                    t.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            };

            tab.MouseLeftButtonUp += (_, _) =>
            {
                _effectCategory = capturedCat;
                effectPicker.SetVisibleCategory(TabIndexToPickerCategory(capturedCat));
                // Update tab visuals
                var ac = ThemeManager.Accent;
                for (int j = 0; j < categoryTabs.Length; j++)
                {
                    bool isActive = j == capturedCat;
                    categoryTabs[j].BorderBrush = new SolidColorBrush(isActive ? ac : Colors.Transparent);
                    var tb = (TextBlock)categoryTabs[j].Child;
                    tb.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
                    tb.Foreground = isActive ? new SolidColorBrush(ac) : new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
                }
            };
            categoryTabBar.Children.Add(tab);
        }
        categoryBarContainer.Child = categoryTabBar;

        // Set initial visible category
        effectPicker.SetVisibleCategory(TabIndexToPickerCategory(_effectCategory));

        stack.Children.Add(MakeSectionCard("EFFECT", categoryBarContainer, effectPicker));

        // Forward-declared so the SelectionChanged closure can reference it; assigned below.
        Border? paletteSection = null;

        // Forward-declared so SelectionChanged can toggle them without forward reference.
        PaletteEditorControl? paletteEditorRef = null;
        Border? singleColorSwatchRef = null;

        effectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _vuFillActive) return;
            var effect = effectPicker.SelectedEffect;
            // Run SingleColor through the pattern loop too — otherwise the Govee
            // device falls out of razer mode after a few seconds (no keepalive frames).
            // For SingleColor we collapse both colors to _roomColor1 so every LED is uniform.
            if (effect == LightEffect.SingleColor)
                StartRoomPattern(effect.ToString(), _roomColor1, _roomColor1);
            else
                StartRoomPattern(effect.ToString());
            if (_config != null)
            {
                _config.Ambience.LinkToLights = false;
                _activePattern = effect.ToString();
            }
            // Hide the palette section for effects whose colors are hardcoded
            if (paletteSection != null)
            {
                paletteSection.Visibility = EffectIgnoresPalette(effect)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            // Swap between single-color swatch and multi-stop editor based on effect
            bool isSingle = effect == LightEffect.SingleColor;
            if (paletteEditorRef != null)
                paletteEditorRef.Visibility = isSingle ? Visibility.Collapsed : Visibility.Visible;
            if (singleColorSwatchRef != null)
            {
                singleColorSwatchRef.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;
                if (isSingle)
                    SetPillColor(singleColorSwatchRef, _roomColor1);
            }
        };

        // ════════════════════════════════════════════════════════════
        // COLORS card — palette editor, single-color swatch, preset tiles
        // ════════════════════════════════════════════════════════════

        var paletteEditor = new PaletteEditorControl
        {
            Palette = _roomPalette,
            Margin = new Thickness(0, 4, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        paletteEditor.PaletteChanged += palette =>
        {
            if (_loading) return;
            _roomPalette = palette;
            if (palette.Stops.Count >= 2)
            {
                var sorted = palette.Stops.OrderBy(s => s.Position).ToList();
                _roomColor1 = Color.FromRgb(sorted[0].R, sorted[0].G, sorted[0].B);
                _roomColor2 = Color.FromRgb(sorted[^1].R, sorted[^1].G, sorted[^1].B);
            }
            if (_activePattern != null && _activePattern != "__sync__")
                StartRoomPattern(_activePattern);
        };
        paletteEditor.StopClicked += (stopIdx, currentColor) =>
        {
            var dialog = new ColorPickerDialog(currentColor) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c => paletteEditor.UpdateSelectedStopColor(c);
            dialog.ShowDialog();
            paletteEditor.ClearSelection();
        };

        // ── Single-color swatch picker (only visible when SingleColor effect is selected) ──
        Border singleColorSwatch = null!;
        singleColorSwatch = MakeColorPill("COLOR", _roomColor1, "Click to pick a solid color", () =>
        {
            var dialog = new ColorPickerDialog(_roomColor1) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c =>
            {
                _roomColor1 = c;
                _roomColor2 = c; // keep in sync so the pattern loop renders uniform
                SetPillColor(singleColorSwatch, c);
                // Restart pattern with new color so continuous frames push it
                if (_activePattern == "SingleColor")
                    StartRoomPattern("SingleColor", c, c);
            };
            dialog.ShowDialog();
        });
        singleColorSwatch.Margin = new Thickness(0, 4, 0, 10);
        singleColorSwatch.HorizontalAlignment = HorizontalAlignment.Left;
        singleColorSwatch.Visibility = Visibility.Collapsed;

        // Publish refs so SelectionChanged can toggle them
        paletteEditorRef = paletteEditor;
        singleColorSwatchRef = singleColorSwatch;

        // Set initial visibility based on currently-selected effect
        bool startSingle = _activePattern == null ||
            string.Equals(_activePattern, "SingleColor", StringComparison.OrdinalIgnoreCase);
        paletteEditor.Visibility = startSingle ? Visibility.Collapsed : Visibility.Visible;
        singleColorSwatch.Visibility = startSingle ? Visibility.Visible : Visibility.Collapsed;

        paletteSection = MakeSectionCard("COLORS", paletteEditor, singleColorSwatch);
        stack.Children.Add(paletteSection);

        // Hide palette section if the currently-selected effect ignores palette colors
        if (_activePattern != null && _activePattern != "__sync__" &&
            Enum.TryParse<LightEffect>(_activePattern, true, out var curEff) &&
            EffectIgnoresPalette(curEff))
        {
            paletteSection.Visibility = Visibility.Collapsed;
        }

        // SPEED and BRIGHTNESS sliders are in the toggle settings row (BuildToggleSettingsRow)

        // Screen Sync settings (if enabled) — below the cards
        if (_screenSyncSettingsPanel != null)
            stack.Children.Add(_screenSyncSettingsPanel);
    }

    // ── LAYOUT TAB (room canvas, device placement, dimensions) ──────────
    private void BuildLayoutTab(StackPanel stack)
    {
        if (_config == null) return;
        var layout = _config.RoomLayout;

        // ── Top card: dimensions + projection ─────────────────────────────
        var topCard = MakeLayoutCard();
        var topContent = new StackPanel();

        // Row 1: Dimensions — three labeled columns
        var dimRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        dimRow.Children.Add(MakeLabeledDimension("WIDTH", layout.WidthFt,
            v => { layout.WidthFt = v; OnLayoutChanged(); _roomCanvas?.Rebuild(); }));
        dimRow.Children.Add(MakeLabeledDimension("DEPTH", layout.DepthFt,
            v => { layout.DepthFt = v; OnLayoutChanged(); _roomCanvas?.Rebuild(); }));
        dimRow.Children.Add(MakeLabeledDimension("HEIGHT", layout.HeightFt,
            v => { layout.HeightFt = v; OnLayoutChanged(); }));
        dimRow.Children.Add(new TextBlock
        {
            Text = "ft",
            FontSize = 11,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 0, 8),
        });
        topContent.Children.Add(dimRow);

        // Thin divider
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 14),
        };
        divider.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        topContent.Children.Add(divider);

        // Row 2+3: Projection mode (Mirror / Spatial) + Direction pills
        // Only Spatial uses Direction — Mirror shows the same effect on all devices.
        topContent.Children.Add(BuildProjectionRow(layout));

        topCard.Child = topContent;
        stack.Children.Add(topCard);

        // ── Room canvas ──
        _roomCanvas = new Controls.RoomCanvasControl
        {
            Height = 420,
            Margin = new Thickness(0, 0, 0, 12),
        };

        // Auto-populate devices if layout is empty (Mirror stays default)
        if (layout.Devices.Count == 0)
            AutoPopulateLayoutDevices(layout);

        _roomCanvas.SetLayout(layout);

        _roomCanvas.DeviceSelected += dev =>
        {
            Dispatcher.BeginInvoke(() => RebuildSelectedDevicePanel(dev));
        };
        _roomCanvas.LayoutChanged += () =>
        {
            OnLayoutChanged();
        };

        // Wrap the canvas in a card too so it matches the design
        var canvasCard = MakeLayoutCard();
        canvasCard.Padding = new Thickness(8);
        canvasCard.Child = _roomCanvas;
        stack.Children.Add(canvasCard);

        // Selected device properties panel
        _selectedDevicePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(_selectedDevicePanel);

        // Unplaced devices tray (wrapped in its own card)
        BuildDeviceTray(stack, layout);
    }

    /// <summary>
    /// Dark card container used throughout the Layout tab.
    /// </summary>
    private Border MakeLayoutCard()
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    /// <summary>
    /// Bigger dimension input with label under it: "[ 12 ]" over "WIDTH".
    /// </summary>
    private StackPanel MakeLabeledDimension(string label, double value, Action<double> onChange)
    {
        var col = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 18, 0) };

        var box = new TextBox
        {
            Text = value.ToString("0.#"),
            Width = 64,
            Height = 34,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Background = FindBrush("CardBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CaretBrush = FindBrush("AccentBrush"),
        };
        box.GotFocus += (_, _) =>
        {
            var ac = ThemeManager.Accent;
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, ac.R, ac.G, ac.B));
            box.SelectAll();
        };
        box.LostFocus += (_, _) =>
        {
            box.BorderBrush = FindBrush("InputBorderBrush");
            if (double.TryParse(box.Text, out double v) && v > 0 && v <= 100)
            {
                double snapped = Math.Round(v * 2) / 2;
                box.Text = snapped.ToString("0.#");
                onChange(snapped);
            }
            else
            {
                box.Text = value.ToString("0.#");
            }
        };
        col.Children.Add(box);

        col.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return col;
    }

    /// <summary>
    /// Builds the projection block: section headers + MIRROR/SPATIAL pills + direction pills.
    /// Direction only applies in Spatial mode, so it's dimmed/disabled when Mirror is active.
    /// </summary>
    private UIElement BuildProjectionRow(RoomLayout layout)
    {
        if (_config == null) return new Border();
        bool isSpatial = _config.Ambience.SpatialSync;

        var root = new StackPanel { Orientation = Orientation.Vertical };

        // ── PROJECTION header + pills ──
        root.Children.Add(MakeCompactHeader("PROJECTION"));
        var modeLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 14) };
        var purple = Color.FromRgb(0xBB, 0x86, 0xFC);
        foreach (var (modeName, modeVal, tooltip) in new[]
        {
            ("MIRROR",  false, "All devices show the same effect"),
            ("SPATIAL", true,  "Effect flows across devices by position"),
        })
        {
            bool modeActive = isSpatial == modeVal;
            var modePill = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Background = modeActive
                    ? new SolidColorBrush(Color.FromArgb(0x38, purple.R, purple.G, purple.B))
                    : FindBrush("CardBgBrush"),
                BorderBrush = modeActive
                    ? new SolidColorBrush(Color.FromArgb(0x80, purple.R, purple.G, purple.B))
                    : FindBrush("InputBorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = tooltip,
            };
            modePill.Child = new TextBlock
            {
                Text = modeName, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = modeActive ? new SolidColorBrush(purple) : FindBrush("TextSecBrush"),
            };
            var capturedVal = modeVal;
            modePill.MouseLeftButtonDown += (_, _) =>
            {
                if (_config == null) return;
                _config.Ambience.SpatialSync = capturedVal;
                OnLayoutChanged();
                QueueSave();
                RebuildRoomTabContent();
            };
            modeLine.Children.Add(modePill);
        }
        root.Children.Add(modeLine);

        // ── DIRECTION header + pills (dimmed when Mirror) ──
        var dirHeader = MakeCompactHeader("DIRECTION");
        dirHeader.Opacity = isSpatial ? 1.0 : 0.45;
        root.Children.Add(dirHeader);

        var dirLine = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var dirNames = new[] { "L \u2192 R", "F \u2192 B", "\u2191", "RADIAL", "DIAGONAL" };
        var dirValues = new[]
        {
            EffectDirection.LeftToRight, EffectDirection.FrontToBack,
            EffectDirection.BottomToTop, EffectDirection.Radial, EffectDirection.Diagonal,
        };
        var accent = ThemeManager.Accent;
        for (int i = 0; i < dirNames.Length; i++)
        {
            var dirVal = dirValues[i];
            bool active = layout.Direction == dirVal;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = isSpatial ? Cursors.Hand : Cursors.Arrow,
                Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B))
                    : FindBrush("CardBgBrush"),
                BorderBrush = active
                    ? new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B))
                    : FindBrush("InputBorderBrush"),
                BorderThickness = new Thickness(1),
                Opacity = isSpatial ? 1.0 : 0.4,
                ToolTip = isSpatial ? null : "Enable SPATIAL mode to use direction",
            };
            pill.Child = new TextBlock
            {
                Text = dirNames[i], FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = active ? new SolidColorBrush(accent) : FindBrush("TextSecBrush"),
            };
            if (isSpatial)
            {
                pill.MouseLeftButtonDown += (_, _) =>
                {
                    layout.Direction = dirVal;
                    OnLayoutChanged();
                    RebuildRoomTabContent();
                };
            }
            dirLine.Children.Add(pill);
        }
        root.Children.Add(dirLine);

        return root;
    }

    /// <summary>
    /// Small dim uppercase header used inside Layout tab cards.
    /// </summary>
    private TextBlock MakeCompactHeader(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = FindBrush("TextDimBrush"),
        Margin = new Thickness(0, 0, 0, 2),
    };

    /// <summary>
    /// Returns category index for an effect: 0=static, 1=animated, 2=reactive, 3=global span.
    /// Returns -1 if unknown.
    /// </summary>
    private static int GetEffectCategory(LightEffect effect)
    {
        return effect switch
        {
            LightEffect.SingleColor or LightEffect.ColorBlend or LightEffect.PositionFill
                or LightEffect.PositionBlend or LightEffect.PositionBlendMute or LightEffect.CycleFill
                or LightEffect.RainbowFill or LightEffect.GradientFill => 0,

            LightEffect.Blink or LightEffect.Pulse or LightEffect.Breathing or LightEffect.Fire
                or LightEffect.Comet or LightEffect.Sparkle or LightEffect.PingPong or LightEffect.Stack
                or LightEffect.Wave or LightEffect.Candle or LightEffect.RainbowWave or LightEffect.RainbowCycle
                or LightEffect.Wheel or LightEffect.RainbowWheel or LightEffect.Heartbeat
                or LightEffect.Plasma or LightEffect.Drip => 1,

            LightEffect.MicStatus or LightEffect.DeviceMute or LightEffect.AudioReactive
                or LightEffect.AudioPositionBlend or LightEffect.ProgramMute or LightEffect.AppGroupMute
                or LightEffect.DeviceSelect => 2,

            LightEffect.Scanner or LightEffect.MeteorRain or LightEffect.ColorWave or LightEffect.Segments
                or LightEffect.TheaterChase or LightEffect.RainbowScanner or LightEffect.SparkleRain
                or LightEffect.BreathingSync or LightEffect.FireWall or LightEffect.DualRacer
                or LightEffect.Lightning or LightEffect.Fillup or LightEffect.Ocean or LightEffect.Collision
                or LightEffect.DNA or LightEffect.Rainfall or LightEffect.PoliceLights or LightEffect.Aurora
                or LightEffect.Matrix or LightEffect.Starfield or LightEffect.Equalizer
                or LightEffect.Waterfall or LightEffect.Lava or LightEffect.VuWave
                or LightEffect.NebulaDrift
                or LightEffect.OpalWave or LightEffect.Bloom or LightEffect.ColorTwinkle
                or LightEffect.Vortex or LightEffect.Shockwave or LightEffect.Tidal
                or LightEffect.Prism or LightEffect.EmberDrift or LightEffect.Glitch => 3,

            _ => -1,
        };
    }

    // ── DEVICES TAB (per-device power/brightness controls) ──

    /// <summary>
    /// Build a small ✕ button that hides a Govee device and persists its ID
    /// to HiddenGoveeDeviceIds so future Settings scans won't re-add it.
    /// Used from both the Room DEVICES tab and the Settings device list.
    /// </summary>
    private FrameworkElement BuildGoveeRemoveButton(GoveeDeviceConfig devConfig)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = "\u2715",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Remove this device (won't reappear on rescan)",
        };
        btn.Click += (_, _) =>
        {
            if (_config == null) return;

            // Tag both DeviceId and Ip so future scans can't sneak it back
            // via either path. Ignore blank strings so we don't poison the
            // hidden list with empty keys.
            if (!string.IsNullOrWhiteSpace(devConfig.DeviceId)
                && !_config.Ambience.HiddenGoveeDeviceIds.Contains(devConfig.DeviceId))
                _config.Ambience.HiddenGoveeDeviceIds.Add(devConfig.DeviceId);
            if (!string.IsNullOrWhiteSpace(devConfig.Ip)
                && !_config.Ambience.HiddenGoveeDeviceIds.Contains(devConfig.Ip))
                _config.Ambience.HiddenGoveeDeviceIds.Add(devConfig.Ip);

            _config.Ambience.GoveeDevices.Remove(devConfig);
            QueueSave();
            RebuildRoomTabContent();
        };
        return btn;
    }

    private void BuildDevicesTab(StackPanel stack)
    {
        if (_config == null) return;

        // ── Govee devices ──
        bool hasGovee = _config.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0;
        if (hasGovee)
        {
            var goveeChildren = new List<UIElement>();

            foreach (var govDev in _config.Ambience.GoveeDevices)
            {
                bool hasLan = !string.IsNullOrWhiteSpace(govDev.Ip);
                bool hasCloud = !hasLan
                                && !string.IsNullOrWhiteSpace(govDev.DeviceId)
                                && !string.IsNullOrWhiteSpace(govDev.Sku);
                if (!hasLan && !hasCloud) continue;
                var devConfig = govDev;

                var devRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                // Device name
                var displayName = !string.IsNullOrWhiteSpace(govDev.Name)
                    ? govDev.Name
                    : (hasLan ? govDev.Ip : govDev.DeviceId);
                devRow.Children.Add(new TextBlock
                {
                    Text = displayName,
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 150, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0),
                });

                // "API" badge for cloud-only devices (H604C G1S Pro etc. — Govee
                // blocks LAN control on camera-equipped SKUs).
                if (hasCloud)
                {
                    devRow.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x33, 0x42, 0xA5, 0xF5)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                        Child = new TextBlock
                        {
                            Text = "API",
                            FontSize = 9,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                        },
                    });
                }

                // On/Off — LAN uses AmbienceSync, cloud-only falls back to Cloud API.
                var onOff = new CheckBox
                {
                    Content = "On", FontSize = 11,
                    IsChecked = devConfig.PoweredOn,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                var capturedIp = devConfig.Ip;
                var capturedDev = devConfig;
                onOff.Checked += async (_, _) =>
                {
                    if (_loading || _config == null) return;
                    capturedDev.PoweredOn = true;
                    if (hasLan)
                    {
                        AmbienceSync.PauseSync(capturedIp, 5);
                        await AmbienceSync.SendTurnAsync(capturedIp, true);
                        await Task.Delay(150);
                    }
                    else if (_cloudApi != null)
                    {
                        await _cloudApi.ControlDeviceAsync(capturedDev.DeviceId, capturedDev.Sku,
                            GoveeCloudApi.TurnOnOff(true));
                    }
                    QueueSave();
                };
                onOff.Unchecked += async (_, _) =>
                {
                    if (_loading || _config == null) return;
                    capturedDev.PoweredOn = false;
                    if (hasLan)
                    {
                        AmbienceSync.PauseSync(capturedIp, 5);
                        await AmbienceSync.SendTurnAsync(capturedIp, false);
                    }
                    else if (_cloudApi != null)
                    {
                        await _cloudApi.ControlDeviceAsync(capturedDev.DeviceId, capturedDev.Sku,
                            GoveeCloudApi.TurnOnOff(false));
                    }
                    QueueSave();
                };
                devRow.Children.Add(onOff);

                // Cloud-only rows stop here — brightness/segment streaming isn't
                // viable via the REST API (100 req/min cap).
                if (!hasLan)
                {
                    devRow.Children.Add(BuildGoveeRemoveButton(devConfig));
                    goveeChildren.Add(devRow);
                    continue;
                }

                // Brightness slider (LAN only)
                var brightSlider = new StyledSlider
                {
                    Minimum = 1, Maximum = 100,
                    Value = _config.Ambience.BrightnessScale,
                    Width = 150, Height = 30,
                    AccentColor = ThemeManager.Accent,
                    ShowLabel = false,
                };
                var brightLabel = new TextBlock
                {
                    Text = $"{(int)brightSlider.Value}%", FontSize = 11,
                    Foreground = FindBrush("TextSecBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                };
                brightSlider.ValueChanged += (_, _) =>
                {
                    if (_loading) return;
                    int pct = (int)brightSlider.Value;
                    brightLabel.Text = $"{pct}%";
                    AmbienceSync.PauseSync(devConfig.Ip, 5);
                    _ = AmbienceSync.SendBrightnessAsync(devConfig.Ip, pct);
                };
                devRow.Children.Add(brightSlider);
                devRow.Children.Add(brightLabel);

                // Segment count badge
                int segs = AmbienceSync.GetSegmentCount(govDev);
                if (segs > 1)
                {
                    devRow.Children.Add(new TextBlock
                    {
                        Text = $"{segs} seg",
                        FontSize = 9, Foreground = FindBrush("TextDimBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                    });
                }

                devRow.Children.Add(BuildGoveeRemoveButton(devConfig));
                goveeChildren.Add(devRow);
            }

            if (goveeChildren.Count > 0)
                stack.Children.Add(MakeSectionCard("GOVEE", goveeChildren.ToArray()));
        }

        // ── Corsair devices ──
        bool hasCorsair = _config.Corsair.Enabled && _corsairSync?.IsAvailable == true;
        if (hasCorsair)
        {
            var corsairChildren = new List<UIElement>();

            if (_corsairSync?.Devices.Count > 0)
            {
                foreach (var dev in _corsairSync.Devices)
                {
                    var devRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                    devRow.Children.Add(new TextBlock
                    {
                        Text = dev.Name,
                        FontSize = 12, FontWeight = FontWeights.SemiBold,
                        Foreground = FindBrush("TextPrimaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = 200, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 12, 0),
                    });

                    devRow.Children.Add(new TextBlock
                    {
                        Text = $"{dev.LedCount} LEDs",
                        FontSize = 10, Foreground = FindBrush("TextSecBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    corsairChildren.Add(devRow);
                }

                // Brightness slider for all Corsair
                var corBrightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
                corBrightRow.Children.Add(MakeSubLabel("BRIGHTNESS"));
                var corBrightSlider = new StyledSlider
                {
                    Minimum = 1, Maximum = 100,
                    Value = Math.Min(_config.Corsair.LightBrightness, 100),
                    Width = 150, Height = 30,
                    AccentColor = ThemeManager.Accent,
                    ShowLabel = false,
                };
                var corBrightLabel = new TextBlock
                {
                    Text = $"{Math.Min(_config.Corsair.LightBrightness, 100)}%",
                    FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                };
                corBrightSlider.ValueChanged += (_, _) =>
                {
                    if (_config == null) return;
                    int pct = (int)corBrightSlider.Value;
                    _config.Corsair.LightBrightness = pct;
                    corBrightLabel.Text = $"{pct}%";
                    QueueSave();
                };
                corBrightRow.Children.Add(corBrightSlider);
                corBrightRow.Children.Add(corBrightLabel);
                corsairChildren.Add(corBrightRow);
            }
            else
            {
                corsairChildren.Add(new TextBlock
                {
                    Text = "No Corsair devices detected — make sure iCUE is running.",
                    FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            stack.Children.Add(MakeSectionCard("CORSAIR", corsairChildren.ToArray()));
        }

        if (!hasGovee && !hasCorsair)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No devices configured — enable Govee or Corsair in Settings.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        // ── AmpUp hardware ──
        var turnUpCheck = new CheckBox
        {
            Content = "Sync Screen to Hardware LEDs",
            IsChecked = _config.Ambience.ScreenSync.SyncToTurnUp,
            Foreground = FindBrush("TextPrimaryBrush"), FontSize = 12,
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "Send screen colors to AmpUp hardware LEDs when Screen Sync is active",
        };
        turnUpCheck.Checked += (_, _) => { if (!_loading && _config != null) { _config.Ambience.ScreenSync.SyncToTurnUp = true; QueueSave(); } };
        turnUpCheck.Unchecked += (_, _) => { if (!_loading && _config != null) { _config.Ambience.ScreenSync.SyncToTurnUp = false; QueueSave(); } };

        var mixerCheck = new CheckBox
        {
            Content = "Mirror Room to Hardware",
            IsChecked = _config.Ambience.SyncRoomToTurnUp,
            Foreground = FindBrush("TextPrimaryBrush"), FontSize = 12,
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "Sync room effect and VU Fill colors to AmpUp hardware LEDs",
        };
        mixerCheck.Checked += (_, _) => { if (!_loading && _config != null) { _config.Ambience.SyncRoomToTurnUp = true; QueueSave(); } };
        mixerCheck.Unchecked += (_, _) =>
        {
            if (!_loading && _config != null)
            {
                _config.Ambience.SyncRoomToTurnUp = false;
                App.Rgb?.SetScreenSyncColors(null);
                QueueSave();
            }
        };

        stack.Children.Add(MakeSectionCard("AMP UP HARDWARE", turnUpCheck, mixerCheck));
    }

    // ── OLD LAYOUT TAB (dead code — kept for reference) ──

    private void BuildLayoutTabOldDead(StackPanel stack)
    {
        if (_config == null) return;
        var layout = _config.RoomLayout;

        // ── Room Dimensions ──
        var (dimBar, dimLabel) = MakeSectionHeader("ROOM DIMENSIONS");
        stack.Children.Add(WrapHeader(dimBar, dimLabel));

        var dimRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        dimRow.Children.Add(MakeSubLabel("WIDTH"));
        var widthBox = MakeDimensionInput(layout.WidthFt, v => { layout.WidthFt = v; OnLayoutChanged(); });
        dimRow.Children.Add(widthBox);

        dimRow.Children.Add(new TextBlock { Text = "×", FontSize = 14, Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });

        dimRow.Children.Add(MakeSubLabel("DEPTH"));
        var depthBox = MakeDimensionInput(layout.DepthFt, v => { layout.DepthFt = v; OnLayoutChanged(); });
        dimRow.Children.Add(depthBox);

        dimRow.Children.Add(new TextBlock { Text = "×", FontSize = 14, Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });

        dimRow.Children.Add(MakeSubLabel("HEIGHT"));
        var heightBox = MakeDimensionInput(layout.HeightFt, v => { layout.HeightFt = v; OnLayoutChanged(); });
        dimRow.Children.Add(heightBox);

        dimRow.Children.Add(new TextBlock { Text = "ft", FontSize = 11, Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        stack.Children.Add(dimRow);

        // ── Effect Direction ──
        var (dirBar, dirLabel) = MakeSectionHeader("EFFECT DIRECTION");
        stack.Children.Add(WrapHeader(dirBar, dirLabel));

        var dirNames = new[] { "L → R", "F → B", "↑", "RADIAL", "DIAGONAL" };
        var dirValues = new[] { EffectDirection.LeftToRight, EffectDirection.FrontToBack,
            EffectDirection.BottomToTop, EffectDirection.Radial, EffectDirection.Diagonal };
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < dirNames.Length; i++)
        {
            var dirVal = dirValues[i];
            bool active = layout.Direction == dirVal;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE6, 0x76))
                    : FindBrush("CardBorderBrush"),
                BorderBrush = active
                    ? new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xE6, 0x76))
                    : FindBrush("InputBorderBrush"),
                BorderThickness = new Thickness(1),
            };
            pill.Child = new TextBlock
            {
                Text = dirNames[i], FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = active ? FindBrush("AccentBrush") : FindBrush("TextSecBrush"),
            };
            pill.MouseLeftButtonDown += (_, _) =>
            {
                layout.Direction = dirVal;
                OnLayoutChanged();
                RebuildRoomTabContent();
            };
            dirRow.Children.Add(pill);
        }
        stack.Children.Add(dirRow);

        // ── Room Canvas ──
        var (canvasBar, canvasLabel) = MakeSectionHeader("ROOM LAYOUT");
        stack.Children.Add(WrapHeader(canvasBar, canvasLabel));

        stack.Children.Add(new TextBlock
        {
            Text = "Drag devices to position them in your room. Effects will follow their spatial positions.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _roomCanvas = new Controls.RoomCanvasControl
        {
            Height = 350,
            Margin = new Thickness(0, 0, 0, 12),
        };

        // Auto-populate devices from Govee + Corsair if layout is empty
        if (layout.Devices.Count == 0)
            AutoPopulateLayoutDevices(layout);

        _roomCanvas.SetLayout(layout);

        _roomCanvas.DeviceSelected += dev =>
        {
            // Rebuild to show selected device properties panel
            // (deferred to avoid rebuild during mouse event)
            Dispatcher.BeginInvoke(() => RebuildSelectedDevicePanel(dev));
        };

        _roomCanvas.LayoutChanged += () =>
        {
            OnLayoutChanged();
        };

        _roomCanvas.MonitorMoved += mon =>
        {
            OnLayoutChanged();
        };

        stack.Children.Add(_roomCanvas);

        // ── Selected Device Properties (shown below canvas) ──
        _selectedDevicePanel = new StackPanel();
        stack.Children.Add(_selectedDevicePanel);

        // ── Unplaced Devices Tray ──
        var (trayBar, trayLabel) = MakeSectionHeader("DEVICES");
        stack.Children.Add(WrapHeader(trayBar, trayLabel));
        BuildDeviceTray(stack, layout);

        // Trigger initial spatial mapper calculation
        OnLayoutChanged();
    }

    private StackPanel? _selectedDevicePanel;

    private TextBox MakeDimensionInput(double value, Action<double> onChange)
    {
        var box = new TextBox
        {
            Text = value.ToString("0.#"),
            Width = 50, Height = 28,
            FontSize = 12,
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        box.LostFocus += (_, _) =>
        {
            if (double.TryParse(box.Text, out double v) && v > 0 && v <= 100)
                onChange(Math.Round(v * 2) / 2); // snap to 0.5 ft
        };
        return box;
    }

    private void AutoPopulateLayoutDevices(RoomLayout layout)
    {
        if (_config == null) return;

        // Add Govee devices
        double xStep = layout.WidthFt / Math.Max(_config.Ambience.GoveeDevices.Count + 1, 2);
        int gi = 1;
        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(dev.Ip) && string.IsNullOrWhiteSpace(dev.DeviceId)) continue;
            int segs = AmpUp.Core.Services.AmbienceSync.GetSegmentCount(dev);
            layout.Devices.Add(new RoomDevicePlacement
            {
                DeviceType = "govee",
                DeviceId = !string.IsNullOrWhiteSpace(dev.Ip) ? dev.Ip : dev.DeviceId,
                Name = dev.Name,
                X = Math.Round(xStep * gi * 2) / 2,
                Y = layout.DepthFt / 2,
                Z = segs > 1 ? 5.0 : 3.5, // bars on wall, bulbs lower
                SegmentCount = Math.Max(segs, 1),
                LengthFt = segs > 1 ? 1.5 : 0.3,
            });
            gi++;
        }

        // Add Corsair devices
        if (_corsairSync?.Devices.Count > 0)
        {
            foreach (var dev in _corsairSync.Devices)
            {
                layout.Devices.Add(new RoomDevicePlacement
                {
                    DeviceType = "corsair",
                    DeviceId = dev.Id,
                    Name = dev.Name,
                    X = layout.WidthFt / 2,
                    Y = 1.0, // near front (desk)
                    Z = 2.5,
                    SegmentCount = Math.Max(dev.LedCount / 3, 1),
                    LengthFt = 0.5,
                });
            }
        }
    }

    private void RebuildSelectedDevicePanel(RoomDevicePlacement dev)
    {
        if (_selectedDevicePanel == null || _config == null) return;
        _selectedDevicePanel.Children.Clear();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        row.Children.Add(new TextBlock
        {
            Text = dev.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        });

        // X input
        row.Children.Add(MakeSubLabel("X"));
        row.Children.Add(MakeDimensionInput(dev.X, v => { dev.X = v; OnLayoutChanged(); _roomCanvas?.Rebuild(); }));

        row.Children.Add(MakeSubLabel("Y"));
        row.Children.Add(MakeDimensionInput(dev.Y, v => { dev.Y = v; OnLayoutChanged(); _roomCanvas?.Rebuild(); }));

        row.Children.Add(MakeSubLabel("Z"));
        row.Children.Add(MakeDimensionInput(dev.Z, v => { dev.Z = v; OnLayoutChanged(); }));

        // Rotation
        row.Children.Add(MakeSubLabel("ROT"));
        var rotBox = MakeDimensionInput(dev.Rotation, v => { dev.Rotation = v; OnLayoutChanged(); _roomCanvas?.Rebuild(); });
        rotBox.Width = 40;
        row.Children.Add(rotBox);
        row.Children.Add(new TextBlock { Text = "°", FontSize = 11, Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });

        // Reverse segments toggle (only for multi-segment devices)
        if (dev.SegmentCount > 1)
        {
            var revCheck = new CheckBox
            {
                Content = "Reverse",
                IsChecked = dev.Reversed,
                FontSize = 10,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "Flip segment order — use when the device's left side faces right in the room",
            };
            revCheck.Checked += (_, _) => { dev.Reversed = true; OnLayoutChanged(); };
            revCheck.Unchecked += (_, _) => { dev.Reversed = false; OnLayoutChanged(); };
            row.Children.Add(revCheck);

            // Split L/R toggle (for paired devices like light bars, wall lights)
            var splitCheck = new CheckBox
            {
                Content = "Split L/R",
                IsChecked = dev.SplitLR,
                FontSize = 10,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "Treat as two separate units (left + right) for spatial effects",
            };
            splitCheck.Checked += (_, _) => { dev.SplitLR = true; OnLayoutChanged(); _roomCanvas?.Rebuild(); };
            splitCheck.Unchecked += (_, _) => { dev.SplitLR = false; OnLayoutChanged(); _roomCanvas?.Rebuild(); };
            row.Children.Add(splitCheck);
        }

        // Remove button
        var removeBtn = new TextBlock
        {
            Text = "✕ Remove", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        removeBtn.MouseLeftButtonDown += (_, _) =>
        {
            _config.RoomLayout.Devices.Remove(dev);
            OnLayoutChanged();
            _selectedDevicePanel?.Children.Clear();
            _roomCanvas?.Rebuild();
        };
        row.Children.Add(removeBtn);

        _selectedDevicePanel.Children.Add(row);
    }

    private void BuildDeviceTray(StackPanel stack, RoomLayout layout)
    {
        // Show devices that are discovered but not yet in the layout
        var placedIds = new HashSet<string>(layout.Devices.Select(d => d.DeviceId));

        var card = MakeLayoutCard();
        card.Padding = new Thickness(16, 14, 16, 14);
        var cardContent = new StackPanel();

        cardContent.Children.Add(new TextBlock
        {
            Text = "ADD DEVICE",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var trayRow = new WrapPanel { Orientation = Orientation.Horizontal };
        bool anyUnplaced = false;

        // "Place Monitor" button (only if no monitor placed yet)
        if (layout.Monitor == null)
        {
            trayRow.Children.Add(MakeDeviceTrayButton("Monitor", "monitor", () =>
            {
                layout.Monitor = new MonitorPlacement
                {
                    X = layout.WidthFt / 2,
                    Y = 1.0,
                    Z = 3.5,
                    WidthFt = 2.8,
                    HeightFt = 1.0,
                };
                OnLayoutChanged();
                RebuildRoomTabContent();
            }));
            anyUnplaced = true;
        }

        if (_config != null)
        {
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                string id = !string.IsNullOrWhiteSpace(dev.Ip) ? dev.Ip : dev.DeviceId;
                if (string.IsNullOrWhiteSpace(id) || placedIds.Contains(id)) continue;

                int segs = AmpUp.Core.Services.AmbienceSync.GetSegmentCount(dev);
                trayRow.Children.Add(MakeDeviceTrayButton(dev.Name, "govee", () =>
                {
                    layout.Devices.Add(new RoomDevicePlacement
                    {
                        DeviceType = "govee", DeviceId = id, Name = dev.Name,
                        X = layout.WidthFt / 2, Y = layout.DepthFt / 2, Z = 4.0,
                        SegmentCount = Math.Max(segs, 1), LengthFt = segs > 1 ? 1.5 : 0.3,
                    });
                    OnLayoutChanged();
                    RebuildRoomTabContent();
                }));
                anyUnplaced = true;
            }
        }

        // HA lights — show "Scan HA" button if no cache, or individual buttons from cache
        if (_config?.HomeAssistant.Enabled == true && _ha != null)
        {
            if (_haLightCache == null)
            {
                trayRow.Children.Add(MakeDeviceTrayButton("Scan HA Lights", "ha", () =>
                {
                    _ = ShowHaLightPickerAsync(layout);
                }));
                anyUnplaced = true;
            }
            else
            {
                foreach (var entity in _haLightCache)
                {
                    if (placedIds.Contains(entity.EntityId)) continue;
                    if (entity.EntityId.Contains("segment_")) continue;
                    var eid = entity.EntityId;
                    var ename = entity.FriendlyName;
                    trayRow.Children.Add(MakeDeviceTrayButton(ename, "ha", () =>
                    {
                        layout.Devices.Add(new RoomDevicePlacement
                        {
                            DeviceType = "ha", DeviceId = eid, Name = ename,
                            X = layout.WidthFt / 2, Y = layout.DepthFt / 2, Z = 4.0,
                            SegmentCount = 1, LengthFt = 0.3,
                        });
                        OnLayoutChanged();
                        RebuildRoomTabContent();
                    }));
                    anyUnplaced = true;
                }
            }
        }

        if (!anyUnplaced)
        {
            cardContent.Children.Add(new TextBlock
            {
                Text = "All discovered devices are placed in the layout.",
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        else
        {
            cardContent.Children.Add(trayRow);
        }

        card.Child = cardContent;
        stack.Children.Add(card);
    }

    /// <summary>
    /// Device tray tile: small colored icon square + name on a dark rounded pill.
    /// </summary>
    private Border MakeDeviceTrayButton(string name, string type, Action onClick)
    {
        Color tint = type switch
        {
            "monitor" => Color.FromRgb(0x7C, 0xB3, 0xFF), // blue
            "govee"   => Color.FromRgb(0xFF, 0x8A, 0x65), // orange
            "ha"      => Color.FromRgb(0x82, 0xCF, 0xFF), // cyan
            _          => ThemeManager.Accent,
        };

        var tile = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal };

        // Colored icon square
        var iconBg = new Border
        {
            Width = 24, Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x30, tint.R, tint.G, tint.B)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBg.Child = new TextBlock
        {
            Text = "+",
            FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(tint),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(iconBg);

        content.Children.Add(new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(name) ? name : type,
            FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        tile.Child = content;

        tile.MouseEnter += (_, _) =>
        {
            var ac = ThemeManager.Accent;
            tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, ac.R, ac.G, ac.B));
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        };
        tile.MouseLeftButtonDown += (_, _) => onClick();
        return tile;
    }

    private List<HAEntity>? _haLightCache;

    private async Task ShowHaLightPickerAsync(RoomLayout layout)
    {
        if (_ha == null) return;
        try
        {
            // Fetch HA lights and rebuild tray to show individual add buttons
            _haLightCache = await _ha.GetEntitiesAsync("light");
            RebuildRoomTabContent(); // tray will now show individual HA light buttons
        }
        catch (Exception ex)
        {
            Logger.Log($"[Room] HA light fetch failed: {ex.Message}");
        }
    }

    private void OnLayoutChanged()
    {
        if (_config == null) return;

        // Update spatial mapper (room effects)
        if (_spatialMapper == null)
            _spatialMapper = new AmpUp.Core.Engine.SpatialMapper();
        _spatialMapper.Recalculate(_config.RoomLayout);
        _sync?.SetSpatialMapper(_config.RoomLayout.Devices.Count > 0 ? _spatialMapper : null);

        // Update screen spatial mapper (screen sync)
        if (_config.RoomLayout.Monitor != null)
        {
            if (_screenSpatialMapper == null)
                _screenSpatialMapper = new AmpUp.Core.Engine.ScreenSpatialMapper();
            _screenSpatialMapper.Recalculate(_config.RoomLayout);
            _dreamSync?.SetSpatialMapper(_screenSpatialMapper);
        }
        else
        {
            _dreamSync?.SetSpatialMapper(null);
        }

        QueueSave();
    }

    // ── GLOBAL TAB ──────────────────────────────────────────────────

    private void BuildGlobalRoomTab(StackPanel stack)
    {
        // ── Effect ──
        var (effBar, effLabel) = MakeSectionHeader("EFFECT");
        stack.Children.Add(WrapHeader(effBar, effLabel));

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

        // ── Section 2: COLORS ──
        stack.Children.Add(MakeSeparator());
        var (preBar, preLabel) = MakeSectionHeader("COLORS");
        stack.Children.Add(WrapHeader(preBar, preLabel));

        // Palette editor — gradient bar + color chips + built-in presets
        var paletteEditor = new PaletteEditorControl
        {
            Palette = _roomPalette,
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        paletteEditor.PaletteChanged += palette =>
        {
            if (_loading) return;
            _roomPalette = palette;
            // Update legacy color1/color2 from palette endpoints for backward compat
            if (palette.Stops.Count >= 2)
            {
                var sorted = palette.Stops.OrderBy(s => s.Position).ToList();
                _roomColor1 = Color.FromRgb(sorted[0].R, sorted[0].G, sorted[0].B);
                _roomColor2 = Color.FromRgb(sorted[^1].R, sorted[^1].G, sorted[^1].B);
            }
            // Restart pattern with new palette
            if (_activePattern != null && _activePattern != "__sync__")
                StartRoomPattern(_activePattern);
        };
        paletteEditor.StopClicked += (stopIdx, currentColor) =>
        {
            var dialog = new ColorPickerDialog(currentColor) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c => paletteEditor.UpdateSelectedStopColor(c);
            dialog.ShowDialog();
            paletteEditor.ClearSelection();
        };
        stack.Children.Add(paletteEditor);

        // Speed slider
        stack.Children.Add(MakeSubLabel("SPEED"));
        var speedSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = _roomEffectSpeed,
            Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        speedSlider.ValueChanged += (_, _) =>
        {
            _roomEffectSpeed = (int)speedSlider.Value;
            if (_roomRgb != null && Enum.TryParse<LightEffect>(_activePattern, true, out var eff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = eff,
                    R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                    R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                    EffectSpeed = _roomEffectSpeed,
                    PaletteName = _roomPalette.Name,
                });
            }
        };
        stack.Children.Add(speedSlider);
    }

    private void StartGlobalMusicSync()
    {
        StopCorsairMusicSync(); // stop any existing

        App.AudioAnalyzer?.Start();

        _corsairMusicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _corsairMusicTimer.Tick += (_, _) =>
        {
            _globalMusicBands = App.AudioAnalyzer?.SmoothedBands;
        };
        _corsairMusicTimer.Start();

        // If no room pattern is running, start an AudioReactive pattern
        if (_roomRgb == null && (_activePattern == null || _activePattern == "__sync__"))
        {
            _activePattern = "AudioReactive";
            _roomPatternCorsairOnly = false;
            ResumeAllGoveeSync();
            _roomRgb = new RgbController();
            _roomRgb.SetBrightness(100);
            _roomRgb.SetAudioBandsProvider(() => App.AudioAnalyzer?.SmoothedBands ?? Array.Empty<float>());
            _roomRgb.UpdateCustomPalettes(_config?.CustomPalettes);
            for (int k = 0; k < 5; k++)
                _roomRgb.SetKnobPosition(k, 1.0f);
            _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
            {
                Enabled = true, Effect = LightEffect.AudioReactive,
                R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                EffectSpeed = _roomEffectSpeed, ReactiveMode = ReactiveMode.SpectrumBands,
                PaletteName = _roomPalette.Name,
            });
            _roomRgb.OnFrameReady += OnRoomFrame;
            _roomRgb.SetOutput((_, _, _) => { }, () => true);
        }
    }

    // ── VU FILL MODE ─────────────────────────────────────────────────

    private void StartVuFill()
    {
        StopVuFill();
        StopRoomPattern(); // stop any running room effect so we don't fight for Govee segments
        _vuFillActive = true;
        App.AudioAnalyzer?.Start();
        ResumeAllGoveeSync();

        _vuFillTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _vuFillTimer.Tick += (_, _) => VuFillTick();
        _vuFillTimer.Start();
    }

    private void StopVuFill()
    {
        if (!_vuFillActive) return;
        _vuFillActive = false;
        _vuFillTimer?.Stop();
        _vuFillTimer = null;
        for (int i = 0; i < 5; i++) _vuFillSmoothed[i] = 0;
        App.Rgb?.SetScreenSyncColors(null);
    }

    private void VuFillTick()
    {
        if (_config == null || !_vuFillActive) return;
        _vuFillTick++;

        var bands = App.AudioAnalyzer?.SmoothedBands;
        if (bands == null || bands.Length < 5) return;

        // Smooth each band: fast attack, slow decay
        for (int b = 0; b < 5; b++)
        {
            float raw = Math.Clamp(bands[b] * 2f, 0f, 1f);
            if (raw > _vuFillSmoothed[b])
                _vuFillSmoothed[b] = raw;
            else
                _vuFillSmoothed[b] += (raw - _vuFillSmoothed[b]) * 0.2f;
        }

        float overall = 0;
        for (int b = 0; b < 5; b++) overall += _vuFillSmoothed[b];
        overall = Math.Clamp(overall / 5f, 0f, 1f);

        var c1 = _roomColor1;
        var c2 = _roomColor2;
        var mode = _config.Ambience.VuFillMode;

        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;

            int segCount = AmbienceSync.GetSegmentCount(dev);
            bool useSegs = segCount > 0 && dev.UseSegmentProtocol;

            if (useSegs)
            {
                bool isPaired = AmbienceSync.IsPairedDevice(dev.Sku);
                var segColors = new (byte R, byte G, byte B)[segCount];

                if (isPaired)
                {
                    int half = segCount / 2;

                    switch (mode)
                    {
                        case VuFillMode.Split:
                        {
                            // Left=bass, Right=treble — independent levels
                            float leftLevel = Math.Clamp((_vuFillSmoothed[0] + _vuFillSmoothed[1]) / 1.5f, 0f, 1f);
                            float rightLevel = Math.Clamp((_vuFillSmoothed[3] + _vuFillSmoothed[4]) / 1.5f, 0f, 1f);
                            for (int s = 0; s < half; s++)
                            {
                                float fillPos = (float)s / Math.Max(half - 1, 1);
                                float br = leftLevel > fillPos ? 1f : Math.Max(0, 1f - (fillPos - leftLevel) * 5f);
                                segColors[s] = BlendVuColor(c1, c2, fillPos, br);
                            }
                            int rc = segCount - half;
                            for (int s = 0; s < rc; s++)
                            {
                                float fillPos = (float)s / Math.Max(rc - 1, 1);
                                float br = rightLevel > fillPos ? 1f : Math.Max(0, 1f - (fillPos - rightLevel) * 5f);
                                segColors[half + s] = BlendVuColor(c1, c2, fillPos, br);
                            }
                            break;
                        }
                        case VuFillMode.Rainfall:
                        {
                            // Onset-triggered drips that fall from top to bottom
                            // Detect onset: current energy significantly above running average
                            float energy = (_vuFillSmoothed[0] + _vuFillSmoothed[1]) * 1.5f;
                            _vuAvgEnergy += (energy - _vuAvgEnergy) * 0.05f; // slow-tracking average
                            bool onset = energy > _vuAvgEnergy + 0.25f && !_vuLastOnset;
                            _vuLastOnset = energy > _vuAvgEnergy + 0.15f; // hysteresis

                            // Shift existing drips down (every 5 ticks = ~6 shifts/sec, visible speed)
                            if (_vuFillTick % 5 == 0)
                            {
                                for (int s = 0; s < half - 1; s++)
                                    _vuFillPeaks[s] = _vuFillPeaks[s + 1] * 0.8f;
                                _vuFillPeaks[half - 1] = 0;
                            }

                            // Spawn drip at top on onset
                            if (onset)
                                _vuFillPeaks[half - 1] = Math.Clamp(energy, 0.5f, 1f);

                            // Render
                            for (int s = 0; s < half; s++)
                            {
                                float t = (float)s / Math.Max(half - 1, 1);
                                segColors[s] = BlendVuColor(c1, c2, t, _vuFillPeaks[s]);
                            }
                            for (int s = 0; s < segCount - half; s++)
                                segColors[half + s] = segColors[Math.Min(s, half - 1)];
                            break;
                        }
                        case VuFillMode.Pulse:
                        {
                            // All segments pulse together with bass
                            float bass = Math.Clamp((_vuFillSmoothed[0] + _vuFillSmoothed[1]) * 1.5f, 0f, 1f);
                            for (int s = 0; s < segCount; s++)
                                segColors[s] = BlendVuColor(c1, c2, bass, bass);
                            break;
                        }
                        case VuFillMode.Spectrum:
                        {
                            // Each segment = a frequency slice (spread 5 bands across segments)
                            for (int s = 0; s < half; s++)
                            {
                                float bandPos = (float)s / Math.Max(half - 1, 1) * 4f; // map to 0-4
                                int lo = Math.Min((int)bandPos, 4);
                                int hi = Math.Min(lo + 1, 4);
                                float frac = bandPos - lo;
                                float level = _vuFillSmoothed[lo] * (1 - frac) + _vuFillSmoothed[hi] * frac;
                                segColors[s] = BlendVuColor(c1, c2, (float)s / Math.Max(half - 1, 1), level);
                            }
                            for (int s = 0; s < segCount - half; s++)
                                segColors[half + s] = segColors[Math.Min(s, half - 1)]; // mirror
                            break;
                        }
                        case VuFillMode.Drip:
                        {
                            // Liquid drip with gravity — onset spawns drip at top, falls and pools at bottom
                            float energy = (_vuFillSmoothed[0] + _vuFillSmoothed[1]) * 1.5f;
                            _vuAvgEnergy += (energy - _vuAvgEnergy) * 0.05f;
                            bool onset = energy > _vuAvgEnergy + 0.25f && !_vuLastOnset;
                            _vuLastOnset = energy > _vuAvgEnergy + 0.15f;

                            // Slow decay on all segments (pool evaporates)
                            for (int s = 0; s < half; s++)
                                _vuFillPeaks[s] = Math.Max(0, _vuFillPeaks[s] - 0.015f);

                            // Gravity: shift brightness downward (every 4 ticks = ~8/sec)
                            if (_vuFillTick % 4 == 0)
                            {
                                for (int s = 0; s < half - 1; s++)
                                {
                                    float transfer = _vuFillPeaks[s + 1] * 0.3f;
                                    _vuFillPeaks[s] = Math.Min(1f, _vuFillPeaks[s] + transfer);
                                    _vuFillPeaks[s + 1] -= transfer;
                                }
                            }

                            // Spawn drip at top on onset
                            if (onset)
                                _vuFillPeaks[half - 1] = Math.Clamp(energy, 0.6f, 1f);

                            // Render
                            for (int s = 0; s < half; s++)
                            {
                                float t = (float)s / Math.Max(half - 1, 1);
                                segColors[s] = BlendVuColor(c1, c2, t, Math.Clamp(_vuFillPeaks[s], 0, 1));
                            }
                            for (int s = 0; s < segCount - half; s++)
                                segColors[half + s] = segColors[Math.Min(s, half - 1)];
                            break;
                        }
                        default: // Classic
                        {
                            var vuColors = new (byte R, byte G, byte B)[half];
                            for (int s = 0; s < half; s++)
                            {
                                float fillPos = (float)s / Math.Max(half - 1, 1);
                                float br = overall > fillPos ? 1f : Math.Max(0, 1f - (fillPos - overall) * 5f);
                                vuColors[s] = BlendVuColor(c1, c2, fillPos, br);
                            }
                            for (int s = 0; s < half; s++)
                                segColors[s] = vuColors[s];
                            int rightCount = segCount - half;
                            for (int s = 0; s < rightCount && s < half; s++)
                                segColors[half + s] = vuColors[s];
                            break;
                        }
                    }
                }
                else
                {
                    // Non-paired: all modes use simple fill (segments aren't split)
                    for (int s = 0; s < segCount; s++)
                    {
                        float fillPos = (float)s / Math.Max(segCount - 1, 1);
                        float brightness = overall > fillPos ? 1f : Math.Max(0, 1f - (fillPos - overall) * 5f);
                        segColors[s] = BlendVuColor(c1, c2, fillPos, brightness);
                    }
                }

                var syncRef = _sync;
                var ipRef = dev.Ip;
                var colorsRef = segColors;
                _ = Task.Run(() => syncRef?.SendSegmentFrame(ipRef, colorsRef));
            }
            else
            {
                // Single-color device: brightness pulse
                string key = "vu_" + dev.Ip;
                if (!_vuBulbLastSend.TryGetValue(key, out var last) ||
                    (DateTime.UtcNow - last).TotalMilliseconds >= 100)
                {
                    _vuBulbLastSend[key] = DateTime.UtcNow;
                    int brightPct = (int)(10 + overall * 90);
                    _ = AmbienceSync.SendBrightnessAsync(dev.Ip, brightPct);
                }
            }
        }

        // Build 15-LED frame for Corsair and Turn Up
        var frame = new byte[45];
        for (int k = 0; k < 5; k++)
        {
            float level = _vuFillSmoothed[k];
            for (int led = 0; led < 3; led++)
            {
                float fillPos = led / 2f;
                float brightness = level > fillPos ? 1f : Math.Max(0, 1f - (fillPos - level) * 5f);
                var c = BlendVuColor(c1, c2, fillPos, brightness);
                int offset = k * 9 + led * 3;
                frame[offset] = c.R;
                frame[offset + 1] = c.G;
                frame[offset + 2] = c.B;
            }
        }

        if (_config.Ambience.SyncRoomToTurnUp)
            App.Rgb?.SetScreenSyncColors(frame);

        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
        {
            float boost = _config.Corsair.LightBrightness / 100f;
            var boosted = new byte[45];
            for (int i = 0; i < 45; i++)
                boosted[i] = (byte)Math.Min(frame[i] * boost, 255);
            _corsairSync.SyncColors(boosted);
        }
    }

    private static (byte R, byte G, byte B) BlendVuColor(Color c1, Color c2, float t, float brightness)
    {
        byte r = (byte)Math.Clamp((c1.R + (c2.R - c1.R) * t) * brightness, 0, 255);
        byte g = (byte)Math.Clamp((c1.G + (c2.G - c1.G) * t) * brightness, 0, 255);
        byte b = (byte)Math.Clamp((c1.B + (c2.B - c1.B) * t) * brightness, 0, 255);
        return (r, g, b);
    }

    // ── GOVEE TAB ───────────────────────────────────────────────────

    private void BuildGoveeRoomTab(StackPanel stack)
    {
        if (_config == null) return;

        bool hasGoveeDevices = _config.Ambience.GoveeDevices.Count > 0 || _cloudDevices.Count > 0;
        if ((!_config.Ambience.GoveeEnabled && !_config.Ambience.GoveeCloudEnabled) || !hasGoveeDevices)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No Govee devices — enable Govee LAN in Settings and scan for devices.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Govee device content — hidden when synced to Global (controlled by header toggle)
        var goveeDeviceContent = new StackPanel
        {
            Visibility = _config.Ambience.GoveeSyncToGlobal ? Visibility.Collapsed : Visibility.Visible,
        };
        stack.Children.Add(goveeDeviceContent);

        // ── Per-device controls ──
        var (devBar, devLabel) = MakeSectionHeader("DEVICES");
        goveeDeviceContent.Children.Add(WrapHeader(devBar, devLabel));

        foreach (var govDev in _config.Ambience.GoveeDevices)
        {
            bool hasLan = !string.IsNullOrWhiteSpace(govDev.Ip);
            bool hasCloud = !string.IsNullOrWhiteSpace(govDev.DeviceId) && _cloudApi != null;
            if (!hasLan && !hasCloud) continue;
            var devConfig = govDev;

            // Find matching cloud device for cloud-only control
            GoveeDeviceInfo? cloudDev = hasCloud
                ? _cloudDevices.FirstOrDefault(c => c.Device == govDev.DeviceId)
                : null;

            var devRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var displayName = !string.IsNullOrWhiteSpace(govDev.Name) ? govDev.Name
                : hasLan ? govDev.Ip : govDev.DeviceId;
            devRow.Children.Add(new TextBlock
            {
                Text = displayName,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 140, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0),
            });

            var onOff = new CheckBox
            {
                Content = "On", FontSize = 12,
                IsChecked = devConfig.PoweredOn,
                Foreground = FindBrush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            onOff.Checked += async (_, _) =>
            {
                if (_loading) return;
                devConfig.PoweredOn = true;
                if (hasLan)
                {
                    AmbienceSync.PauseSync(devConfig.Ip, 5);
                    await AmbienceSync.SendTurnAsync(devConfig.Ip, true);
                }
                else if (cloudDev != null)
                    await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                        cloudDev.Device, cloudDev.Sku, GoveeCloudApi.TurnOnOff(true)));
                _onSave?.Invoke(_config!);
            };
            onOff.Unchecked += async (_, _) =>
            {
                if (_loading) return;
                devConfig.PoweredOn = false;
                if (hasLan)
                    await AmbienceSync.SendTurnAsync(devConfig.Ip, false);
                else if (cloudDev != null)
                    await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                        cloudDev.Device, cloudDev.Sku, GoveeCloudApi.TurnOnOff(false)));
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
                    if (hasLan)
                        await AmbienceSync.SendTurnAsync(devConfig.Ip, false);
                    else if (cloudDev != null)
                        await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                            cloudDev.Device, cloudDev.Sku, GoveeCloudApi.TurnOnOff(false)));
                }
                else
                {
                    if (!devConfig.PoweredOn)
                    {
                        devConfig.PoweredOn = true;
                        if (hasLan)
                            await AmbienceSync.SendTurnAsync(devConfig.Ip, true);
                        else if (cloudDev != null)
                            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                                cloudDev.Device, cloudDev.Sku, GoveeCloudApi.TurnOnOff(true)));
                    }
                    if (hasLan)
                    {
                        AmbienceSync.PauseSync(devConfig.Ip, 5);
                        await AmbienceSync.SendBrightnessAsync(devConfig.Ip, pct);
                    }
                    else if (cloudDev != null)
                        await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                            cloudDev.Device, cloudDev.Sku, GoveeCloudApi.SetBrightness(pct)));
                }
            };
            brightSlider.ValueChanged += (_, _) => { brightDebounce.Stop(); brightDebounce.Start(); };
            devRow.Children.Add(brightSlider);

            // Cloud-only badge
            if (!hasLan)
            {
                devRow.Children.Add(new TextBlock
                {
                    Text = "CLOUD",
                    FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                });
            }

            goveeDeviceContent.Children.Add(devRow);
            string controlKey = hasLan ? devConfig.Ip : devConfig.DeviceId;
            _deviceControls[controlKey] = (onOff, brightSlider);
            if (hasLan)
                _ = QueryDeviceStateAsync(devConfig.Ip, onOff, brightSlider);
        }

        // ── Scenes (Cloud API) ──
        if (_cloudApi != null && _cloudDevices.Count > 0)
        {
            goveeDeviceContent.Children.Add(MakeSeparator());
            var (scBar, scLabel) = MakeSectionHeader("LIGHT EFFECTS");
            goveeDeviceContent.Children.Add(WrapHeader(scBar, scLabel));

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
            goveeDeviceContent.Children.Add(pickerRow);
            goveeDeviceContent.Children.Add(_sceneContent);

            if (_cloudDevices.Count > 0)
                BuildGoveeSceneContent(_cloudDevices[0]);
        }
    }

    // ── CORSAIR TAB ─────────────────────────────────────────────────

    private void BuildCorsairRoomTab(StackPanel stack)
    {
        if (_config == null || !_config.Corsair.Enabled || _corsairSync == null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Corsair iCUE is not enabled — enable it in Settings.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Corsair device content — hidden when synced to Global (controlled by header toggle)
        var corsairDeviceContent = new StackPanel
        {
            Visibility = _config.Corsair.SyncToGlobal ? Visibility.Collapsed : Visibility.Visible,
        };
        stack.Children.Add(corsairDeviceContent);

        // ── Brightness ──
        var (brBar, brLabel) = MakeSectionHeader("BRIGHTNESS");
        brBar.Background = new SolidColorBrush(ThemeManager.Accent);
        brLabel.Foreground = new SolidColorBrush(ThemeManager.Accent);
        corsairDeviceContent.Children.Add(WrapHeader(brBar, brLabel));

        var corBrightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var corBrightSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = Math.Min(_config.Corsair.LightBrightness, 100),
            Width = 200, Height = 35, AccentColor = ThemeManager.Accent, ShowLabel = false,
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
            int pct = (int)corBrightSlider.Value;
            _config.Corsair.LightBrightness = pct;
            corBrightLabel.Text = $"{pct}%";
            if (_corsairSync?.IsAvailable == true)
            {
                float boost = pct / 100f;
                _ = _corsairSync.SetStaticColorAllAsync(
                    (byte)Math.Min(_corsairColor1.R * boost, 255),
                    (byte)Math.Min(_corsairColor1.G * boost, 255),
                    (byte)Math.Min(_corsairColor1.B * boost, 255));
            }
            QueueSave();
        };
        corBrightRow.Children.Add(corBrightSlider);
        corBrightRow.Children.Add(corBrightLabel);
        corsairDeviceContent.Children.Add(corBrightRow);

        // ── Effect picker ──
        corsairDeviceContent.Children.Add(MakeSeparator());
        var (effBar2, effLabel2) = MakeSectionHeader("EFFECT");
        effBar2.Background = new SolidColorBrush(ThemeManager.Accent);
        effLabel2.Foreground = new SolidColorBrush(ThemeManager.Accent);
        corsairDeviceContent.Children.Add(WrapHeader(effBar2, effLabel2));

        var corsairEffectPicker = new Controls.EffectPickerControl(showGlobal: true)
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        corsairEffectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _corsairSync == null) return;
            var eff = corsairEffectPicker.SelectedEffect;
            if (eff == LightEffect.SingleColor)
            {
                StopRoomPattern();
                if (_corsairSync.IsAvailable)
                {
                    float boost = _config!.Corsair.LightBrightness / 100f;
                    _ = _corsairSync.SetStaticColorAllAsync(
                        (byte)Math.Min(_corsairColor1.R * boost, 255),
                        (byte)Math.Min(_corsairColor1.G * boost, 255),
                        (byte)Math.Min(_corsairColor1.B * boost, 255));
                }
            }
            else
            {
                StartRoomPattern(eff.ToString(), _corsairColor1, _corsairColor2, corsairOnly: true);
            }
        };
        corsairDeviceContent.Children.Add(corsairEffectPicker);

        // ── Color ──
        corsairDeviceContent.Children.Add(MakeSeparator());
        var (colBar2, colLabel2) = MakeSectionHeader("COLOR");
        colBar2.Background = new SolidColorBrush(ThemeManager.Accent);
        colLabel2.Foreground = new SolidColorBrush(ThemeManager.Accent);
        corsairDeviceContent.Children.Add(WrapHeader(colBar2, colLabel2));

        // Shared pill refs so presets can update manual pickers live
        Border? corsairPriPill = null, corsairSecPill = null;

        // Apply colors to fields + live-update pills + running effect
        void ApplyCorsairColors(Color c1, Color c2)
        {
            _corsairColor1 = c1; _corsairColor2 = c2;
            SetPillColor(corsairPriPill, c1);
            SetPillColor(corsairSecPill, c2);
            if (_roomPatternCorsairOnly && _roomRgb != null && _activePattern != null
                && Enum.TryParse<LightEffect>(_activePattern, true, out var runEff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = runEff,
                    R = c1.R, G = c1.G, B = c1.B, R2 = c2.R, G2 = c2.G, B2 = c2.B, EffectSpeed = _roomEffectSpeed,
                });
            }
        }

        // PRESETS
        corsairDeviceContent.Children.Add(MakeSubLabel("PRESETS"));
        var corsairPresetWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var (pname, pcolors) in ColorPalettes)
        {
            var gb = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int ci = 0; ci < pcolors.Length; ci++)
                gb.GradientStops.Add(new GradientStop(pcolors[ci], ci / (double)(pcolors.Length - 1)));
            var captured = pcolors;
            var tileContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            tileContent.Children.Add(new Border { Width = 52, Height = 22, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = gb });
            var nameLabel = new TextBlock { Text = pname, FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            tileContent.Children.Add(nameLabel);
            var tile = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 5, 5, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand, ToolTip = pname, Child = tileContent,
            };
            tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            tile.MouseEnter += (_, _) =>
            {
                var accent = ThemeManager.Accent;
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B));
                tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
                nameLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            };
            tile.MouseLeave += (_, _) =>
            {
                tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
                nameLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
            };
            tile.MouseLeftButtonDown += (_, _) => ApplyCorsairColors(captured[0], captured[captured.Length > 1 ? captured.Length - 1 : 0]);
            corsairPresetWrap.Children.Add(tile);
        }
        corsairDeviceContent.Children.Add(corsairPresetWrap);

        // MANUAL pickers
        corsairDeviceContent.Children.Add(MakeSubLabel("MANUAL"));
        var corsairColorRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };

        Border MakeCorsairPill(string lbl, bool isSecondary)
        {
            var initial = isSecondary ? _corsairColor2 : _corsairColor1;
            var pill = MakeColorPill(lbl, initial, $"{lbl} color — click to change", () =>
            {
                var current = isSecondary ? _corsairColor2 : _corsairColor1;
                var dlg = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() != true) return;
                ApplyCorsairColors(isSecondary ? _corsairColor1 : dlg.SelectedColor, isSecondary ? dlg.SelectedColor : _corsairColor2);
                // Also send static color if no pattern running
                if (!_roomPatternCorsairOnly && !isSecondary && _corsairSync?.IsAvailable == true && _config!.Corsair.Enabled)
                {
                    float boost = _config!.Corsair.LightBrightness / 100f;
                    _ = _corsairSync.SetStaticColorAllAsync(
                        (byte)Math.Min(_corsairColor1.R * boost, 255),
                        (byte)Math.Min(_corsairColor1.G * boost, 255),
                        (byte)Math.Min(_corsairColor1.B * boost, 255));
                }
            });
            if (isSecondary) corsairSecPill = pill;
            else corsairPriPill = pill;
            return pill;
        }

        corsairColorRow.Children.Add(MakeCorsairPill("PRIMARY", false));
        corsairColorRow.Children.Add(MakeCorsairPill("SECONDARY", true));
        corsairDeviceContent.Children.Add(corsairColorRow);

        // Speed slider
        corsairDeviceContent.Children.Add(MakeSubLabel("SPEED"));
        var corsairSpeedSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Height = 35, AccentColor = ThemeManager.Accent, ShowLabel = false,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        corsairSpeedSlider.ValueChanged += (_, _) =>
        {
            if (_roomPatternCorsairOnly && _roomRgb != null && _activePattern != null
                && Enum.TryParse<LightEffect>(_activePattern, true, out var eff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = eff,
                    R = _corsairColor1.R, G = _corsairColor1.G, B = _corsairColor1.B,
                    R2 = _corsairColor2.R, G2 = _corsairColor2.G, B2 = _corsairColor2.B,
                    EffectSpeed = (int)corsairSpeedSlider.Value,
                });
            }
        };
        corsairDeviceContent.Children.Add(corsairSpeedSlider);
    }

    private void BuildGoveeSceneContent(GoveeDeviceInfo device)
    {
        if (_sceneContent == null) return;
        string? lanIp = FindLanIp(device);

        var colorsContainer = new StackPanel();
        _sceneContent.Children.Add(colorsContainer);
        BuildColorsSection(device, colorsContainer, lanIp, null);
        // Music Sync handled by toggle card in tab header
    }

    // ══════════════════════════════════════════════════════════════════
    // ██  CARD 2: SCREEN SYNC
    // ══════════════════════════════════════════════════════════════════


    private void BuildScreenSyncSettings(StackPanel stack, Action<string, bool> statusTileUpdater)
    {
        var cfg = _config!.Ambience.ScreenSync;
        var accent = ThemeManager.Accent;
        var dimBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

        // Helper: make a labeled slider row (label left, slider stretches, value right)
        StackPanel MakeSliderRow(string label, StyledSlider slider, TextBlock valLabel)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            outer.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = dimBrush, Margin = new Thickness(0, 0, 0, 4) });
            var dock = new DockPanel();
            valLabel.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(valLabel, Dock.Right);
            dock.Children.Add(valLabel);
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            dock.Children.Add(slider);
            outer.Children.Add(dock);
            return outer;
        }

        // Helper: make a labeled combo row
        StackPanel MakeComboRow(string label, ComboBox combo)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = dimBrush, VerticalAlignment = VerticalAlignment.Center, Width = 90 });
            row.Children.Add(combo);
            return row;
        }

        // ══════════════════════════════════════════════════════════════
        // Single-column layout: config row → sliders → full-width preview
        // ══════════════════════════════════════════════════════════════
        var leftCol = new StackPanel();

        // Monitor
        var screens = System.Windows.Forms.Screen.AllScreens;
        var friendlyNames = NativeMethods.GetMonitorFriendlyNames();
        var monitorCombo = new ComboBox { MinWidth = 160, MaxWidth = 280 };
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
        leftCol.Children.Add(MakeComboRow("MONITOR", monitorCombo));

        // FPS + Zones on same row
        var fpsZoneRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        fpsZoneRow.Children.Add(new TextBlock { Text = "FPS", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = dimBrush, VerticalAlignment = VerticalAlignment.Center, Width = 90 });
        var fpsCombo = new ComboBox { Width = 75, Margin = new Thickness(0, 0, 16, 0) };
        fpsCombo.Items.Add("15"); fpsCombo.Items.Add("30"); fpsCombo.Items.Add("60");
        fpsCombo.SelectedIndex = cfg.TargetFps switch { 15 => 0, 60 => 2, _ => 1 };
        fpsCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.TargetFps = fpsCombo.SelectedIndex switch { 0 => 15, 2 => 60, _ => 30 };
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        fpsZoneRow.Children.Add(fpsCombo);
        fpsZoneRow.Children.Add(new TextBlock { Text = "ZONES", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = dimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var zoneCombo = new ComboBox { Width = 75 };
        zoneCombo.Items.Add("4"); zoneCombo.Items.Add("8"); zoneCombo.Items.Add("16");
        zoneCombo.SelectedIndex = cfg.ZoneCount switch { 4 => 0, 16 => 2, _ => 1 };
        zoneCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.ZoneCount = zoneCombo.SelectedIndex switch { 0 => 4, 2 => 16, _ => 8 };
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            RebuildZonePreview(stack);
            QueueSave();
        };
        fpsZoneRow.Children.Add(zoneCombo);
        leftCol.Children.Add(fpsZoneRow);

        // Saturation slider
        var satSlider = new StyledSlider
        {
            Minimum = 50, Maximum = 300, Value = (int)(cfg.Saturation * 100),
            Height = 28, AccentColor = accent, ShowLabel = false,
        };
        var satLabel = new TextBlock { Text = $"{cfg.Saturation:F1}×", FontSize = 11,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        satSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Saturation = (float)(satSlider.Value / 100.0);
            satLabel.Text = $"{satSlider.Value / 100.0:F1}×";
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };

        // Sensitivity slider
        var sensSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 20, Value = cfg.Sensitivity,
            Height = 28, AccentColor = accent, ShowLabel = false,
        };
        var sensLabel = new TextBlock { Text = $"{cfg.Sensitivity}", FontSize = 11,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        sensSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Sensitivity = (int)sensSlider.Value;
            sensLabel.Text = $"{(int)sensSlider.Value}";
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };

        // Corsair brightness (declared outside if so grid code can reference)
        StyledSlider? corsairBrightSlider = null;
        TextBlock? corsairBrightLabel = null;
        if (_config!.Corsair.Enabled)
        {
            corsairBrightSlider = new StyledSlider
            {
                Minimum = 1, Maximum = 100, Value = Math.Min(_config.Corsair.LightBrightness, 100),
                Height = 28, AccentColor = accent, ShowLabel = false,
            };
            corsairBrightLabel = new TextBlock { Text = $"{_config.Corsair.LightBrightness}%", FontSize = 11,
                Foreground = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var cbs = corsairBrightSlider; var cbl = corsairBrightLabel;
            corsairBrightSlider.ValueChanged += (_, _) =>
            {
                if (_config == null) return;
                _config.Corsair.LightBrightness = (int)cbs.Value;
                cbl.Text = $"{(int)cbs.Value}%";
                QueueSave();
            };
        }

        // Crop Black Bars toggle
        var cropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        cropRow.Children.Add(new Border { Width = 90 }); // align with labels
        var cropCheck = new CheckBox
        {
            Content = "Crop Black Bars",
            IsChecked = cfg.CropBlackBars,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Auto-detect and ignore pillarbox/letterbox black bars\n(for 16:9 content on ultrawide monitors)",
        };
        cropCheck.Checked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.CropBlackBars = true;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        cropCheck.Unchecked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.CropBlackBars = false;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        cropRow.Children.Add(cropCheck);
        leftCol.Children.Add(cropRow);

        // ── Sliders row: Saturation + Sensitivity + iCUE in a grid ──
        var sliderGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        int sCol = 0;

        // Saturation
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var satCell = MakeSliderRow("SATURATION", satSlider, satLabel);
        satCell.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(satCell, sCol++);
        sliderGrid.Children.Add(satCell);

        // Sensitivity
        sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sensCell = MakeSliderRow("SENSITIVITY", sensSlider, sensLabel);
        sensCell.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(sensCell, sCol++);
        sliderGrid.Children.Add(sensCell);

        // Corsair brightness (if enabled)
        if (corsairBrightSlider != null && corsairBrightLabel != null)
        {
            sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var corsairCell = MakeSliderRow("iCUE BRIGHTNESS", corsairBrightSlider, corsairBrightLabel);
            Grid.SetColumn(corsairCell, sCol++);
            sliderGrid.Children.Add(corsairCell);
        }

        leftCol.Children.Add(sliderGrid);

        // ── Full-width live preview ──
        _screenEdgeControl = new Controls.ScreenEdgeControl
        {
            Height = 220,
        };
        _screenEdgeControl.SetContentBounds(cfg.ContentBounds);
        _screenEdgeControl.ContentBoundsChanged += bounds =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.ContentBounds = bounds;
            // CropBlackBars = true when auto-detecting OR when user has manual crop set
            bool hasCrop = bounds.AutoDetect || bounds.LeftPct > 0 || bounds.RightPct > 0 || bounds.TopPct > 0 || bounds.BottomPct > 0;
            _config.Ambience.ScreenSync.CropBlackBars = hasCrop;
            cropCheck.IsChecked = hasCrop;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        if (_dreamSync != null)
        {
            _dreamSync.OnZoneGrid += (grid2, cols, rows) =>
            {
                Dispatcher.BeginInvoke(() => _screenEdgeControl?.UpdateZoneColors(grid2, cols, rows));
            };
        }
        leftCol.Children.Add(_screenEdgeControl);

        _dreamStatusLabel = new TextBlock
        {
            Text = _dreamSync?.Status ?? "Stopped",
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        leftCol.Children.Add(_dreamStatusLabel);

        stack.Children.Add(leftCol);

        // Status update timer — updates status label and Game Mode badge.
        // When sync isn't running the preview timer owns the status label
        // (sets it to "Preview"), so only write _dreamSync.Status here when
        // sync is actually running, otherwise the two timers fight and the
        // label flashes between "Preview" and "Stopped" every second.
        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statusTimer.Tick += (_, _) =>
        {
            if (_dreamStatusLabel != null && _dreamSync != null && _dreamSync.IsRunning)
                _dreamStatusLabel.Text = _dreamSync.Status;
            bool active = _config?.Ambience.ScreenSync.Enabled == true;
            statusTileUpdater(active ? "ACTIVE" : "STANDBY", active);
        };
        statusTimer.Start();

        // Preview capture timer — low-FPS screen capture when sync isn't actively running
        // so the user always sees a live preview of the selected monitor.
        //
        // IsVisible is used instead of .Visibility because .Visibility on
        // _screenSyncSettingsPanel stays Visible when the user navigates to
        // another tab (only the outer RoomView control gets hidden). IsVisible
        // walks the ancestor chain and returns false the moment the RoomView
        // tab isn't active — which stops the expensive GDI screen capture
        // from running 5x per second on a hidden tab.
        var previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; // ~5 FPS
        previewTimer.Tick += (_, _) =>
        {
            if (_dreamSync == null || _dreamSync.IsRunning) return;
            if (!this.IsVisible) return;
            if (_screenSyncSettingsPanel == null || !_screenSyncSettingsPanel.IsVisible) return;

            try
            {
                var grid = _dreamSync.CapturePreviewGrid();
                if (grid != null)
                {
                    int cols = grid.GetLength(1);
                    int rows = grid.GetLength(0);
                    _screenEdgeControl?.UpdateZoneColors(grid, cols, rows);
                    if (_dreamStatusLabel != null)
                        _dreamStatusLabel.Text = "Preview";
                }
            }
            catch { /* preview is best-effort */ }
        };
        previewTimer.Start();

        // ── Device Zone Mapping ──
        if (_config!.Ambience.GoveeDevices.Count > 0)
        {
            stack.Children.Add(MakeSeparator());
            stack.Children.Add(MakeSubLabel("DEVICE ZONE MAPPING"));

            bool hasMonitor = _config.RoomLayout.Monitor != null;

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
                    Width = 140,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                mapRow.Children.Add(devName);

                int segCount = AmbienceSync.GetSegmentCount(goveeDevice);
                var mapping = cfg.DeviceMappings.FirstOrDefault(m => m.DeviceIp == goveeDevice.Ip);
                if (mapping == null)
                {
                    mapping = new ZoneDeviceMapping { DeviceIp = goveeDevice.Ip, Side = ZoneSide.Full };
                    cfg.DeviceMappings.Add(mapping);
                }
                var capturedMapping = mapping;

                if (segCount > 0 && goveeDevice.UseSegmentProtocol)
                {
                    mapRow.Children.Add(new TextBlock
                    {
                        Text = $"{segCount} seg",
                        FontSize = 10,
                        Foreground = FindBrush("TextDimBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                    });
                }

                // Auto spatial checkbox — derive zone from room position
                var autoLabel = new TextBlock
                {
                    FontSize = 10, Foreground = FindBrush("TextSecBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 8, 0),
                    Text = mapping.UseAutoSpatial && hasMonitor ? "auto" : "",
                };

                var sideCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 8, 0) };
                sideCombo.Items.Add("Full"); sideCombo.Items.Add("Left"); sideCombo.Items.Add("Right");
                sideCombo.Items.Add("Top"); sideCombo.Items.Add("Bottom");
                sideCombo.Items.Add("LeftVertical"); sideCombo.Items.Add("RightVertical");
                sideCombo.SelectedItem = mapping.Side.ToString();
                sideCombo.IsEnabled = !mapping.UseAutoSpatial;
                sideCombo.SelectionChanged += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    if (Enum.TryParse<ZoneSide>(sideCombo.SelectedItem?.ToString(), out var side))
                        capturedMapping.Side = side;
                    _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                    QueueSave();
                };

                var autoCheck = new CheckBox
                {
                    Content = "Auto",
                    IsChecked = mapping.UseAutoSpatial,
                    FontSize = 10,
                    Foreground = FindBrush("TextSecBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    IsEnabled = hasMonitor,
                    ToolTip = hasMonitor
                        ? "Automatically determine screen edge from device position in room layout"
                        : "Place a monitor in the room layout to enable spatial mapping",
                };
                autoCheck.Checked += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    capturedMapping.UseAutoSpatial = true;
                    sideCombo.IsEnabled = false;
                    autoLabel.Text = "auto";
                    _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                    QueueSave();
                };
                autoCheck.Unchecked += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    capturedMapping.UseAutoSpatial = false;
                    sideCombo.IsEnabled = true;
                    autoLabel.Text = "";
                    _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                    QueueSave();
                };
                mapRow.Children.Add(autoCheck);
                mapRow.Children.Add(autoLabel);
                mapRow.Children.Add(sideCombo);

                // Crop mode combo — per-device content bounds behavior
                var cropCombo = new ComboBox { Width = 130, Margin = new Thickness(0, 0, 0, 0) };
                cropCombo.Items.Add("Content"); cropCombo.Items.Add("Full Screen"); cropCombo.Items.Add("Ambient");
                cropCombo.SelectedIndex = mapping.CropMode switch
                {
                    DeviceCropMode.FullScreen => 1,
                    DeviceCropMode.Ambient => 2,
                    _ => 0,
                };
                cropCombo.SelectionChanged += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    capturedMapping.CropMode = cropCombo.SelectedIndex switch
                    {
                        1 => DeviceCropMode.FullScreen,
                        2 => DeviceCropMode.Ambient,
                        _ => DeviceCropMode.Content,
                    };
                    _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                    QueueSave();
                };
                mapRow.Children.Add(cropCombo);

                stack.Children.Add(mapRow);
            }
        }

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
                        Margin = new Thickness(0, 0, 4, 4),
                        ToolTip = $"Zone {z + 1}",
                    };
                    swatch.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
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

    // ── Corsair Music Reactive ─────────────────────────────────────

    private void StartCorsairMusicSync()
    {
        if (_corsairMusicTimer != null) return;

        // Ensure AudioAnalyzer is running
        App.AudioAnalyzer?.Start();

        _corsairMusicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _corsairMusicTimer.Tick += (_, _) =>
        {
            var bands = App.AudioAnalyzer?.SmoothedBands;
            if (bands == null || _corsairSync == null) return;
            // Respect Corsair.Enabled — group/iCUE toggle flips it off and we
            // must not repaint through the music-reactive path.
            if (_config == null || !_config.Corsair.Enabled || !_corsairSync.IsAvailable) return;

            // Map 5 frequency bands to RGB color
            float bass = bands[0] + bands[1];     // sub-bass + bass → warm/red
            float mid = bands[2];                  // low-mid → green
            float treble = bands[3] + bands[4];    // high-mid + treble → cool/blue

            byte r = (byte)Math.Min(bass * 400, 255);
            byte g = (byte)Math.Min(mid * 400, 255);
            byte b = (byte)Math.Min(treble * 400, 255);

            // Apply Corsair brightness scale
            if (_config != null)
            {
                float boost = _config.Corsair.LightBrightness / 100f;
                r = (byte)Math.Min(r * boost, 255);
                g = (byte)Math.Min(g * boost, 255);
                b = (byte)Math.Min(b * boost, 255);
            }

            _ = _corsairSync.SetStaticColorAllAsync(r, g, b);
        };
        _corsairMusicTimer.Start();
        Logger.Log("Corsair music sync started");
    }

    private void StopCorsairMusicSync()
    {
        if (_corsairMusicTimer == null) return;
        _corsairMusicTimer.Stop();
        _corsairMusicTimer = null;
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
            var (musBar, musLabel) = MakeSectionHeader("MUSIC REACTIVE");
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
            ("Sync to AmpUp", "vu_reactive", "Mirror AmpUp knob LED effects"),
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
                    : FindBrush("BgDarkBrush"),
                BorderBrush = isActive
                    ? new SolidColorBrush(corsairYellow)
                    : FindBrush("CardBorderBrush"),
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
            Minimum = 1, Maximum = 100, Value = Math.Min(_config.Corsair.LightBrightness, 100),
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
        ("Fire",      new[] { Color.FromRgb(0x1A, 0x00, 0x00), Color.FromRgb(0x6B, 0x08, 0x00), Color.FromRgb(0xCC, 0x22, 0x00), Color.FromRgb(0xFF, 0x5E, 0x00), Color.FromRgb(0xFF, 0x9E, 0x00), Color.FromRgb(0xFF, 0xCC, 0x22), Color.FromRgb(0xFF, 0xE0, 0x66) }),
        ("Ocean",     new[] { Color.FromRgb(0x00, 0x0C, 0x1A), Color.FromRgb(0x00, 0x2E, 0x5C), Color.FromRgb(0x00, 0x6E, 0xAA), Color.FromRgb(0x00, 0xAA, 0xCC), Color.FromRgb(0x00, 0xDD, 0xEE), Color.FromRgb(0x66, 0xEE, 0xFF), Color.FromRgb(0xBB, 0xF5, 0xFF) }),
        ("Sunset",    new[] { Color.FromRgb(0xAA, 0x00, 0x44), Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x6B, 0x35), Color.FromRgb(0xFF, 0xAA, 0x00), Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0xCC, 0x44, 0xCC), Color.FromRgb(0x55, 0x00, 0x88) }),
        ("Neon",      new[] { Color.FromRgb(0xFF, 0x00, 0xDD), Color.FromRgb(0xBB, 0x00, 0xFF), Color.FromRgb(0x00, 0x44, 0xFF), Color.FromRgb(0x00, 0xFF, 0xCC), Color.FromRgb(0x00, 0xFF, 0x44), Color.FromRgb(0xFF, 0x00, 0x88) }),
        ("Arctic",    new[] { Color.FromRgb(0x00, 0x33, 0x66), Color.FromRgb(0x00, 0x6E, 0x99), Color.FromRgb(0x00, 0xAA, 0xCC), Color.FromRgb(0x66, 0xDD, 0xEE), Color.FromRgb(0xBB, 0xEE, 0xF5), Color.FromRgb(0xE8, 0xF8, 0xFF) }),
        ("Forest",    new[] { Color.FromRgb(0x00, 0x22, 0x00), Color.FromRgb(0x00, 0x44, 0x11), Color.FromRgb(0x00, 0x77, 0x22), Color.FromRgb(0x22, 0xAA, 0x44), Color.FromRgb(0x66, 0xCC, 0x55), Color.FromRgb(0xAA, 0xDD, 0x77), Color.FromRgb(0xDD, 0xEE, 0x88) }),
        ("Lava",      new[] { Color.FromRgb(0x11, 0x00, 0x00), Color.FromRgb(0x44, 0x00, 0x00), Color.FromRgb(0x88, 0x00, 0x00), Color.FromRgb(0xDD, 0x22, 0x00), Color.FromRgb(0xFF, 0x55, 0x00), Color.FromRgb(0xFF, 0x99, 0x00), Color.FromRgb(0xFF, 0xDD, 0x22) }),
        ("Galaxy",    new[] { Color.FromRgb(0x06, 0x00, 0x1A), Color.FromRgb(0x14, 0x00, 0x44), Color.FromRgb(0x44, 0x11, 0x88), Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0xBB, 0x66, 0xDD), Color.FromRgb(0xFF, 0x77, 0xAA), Color.FromRgb(0xDD, 0x44, 0xEE) }),
        ("Aurora",    new[] { Color.FromRgb(0x00, 0xEE, 0x77), Color.FromRgb(0x00, 0xCC, 0x99), Color.FromRgb(0x00, 0x88, 0xDD), Color.FromRgb(0x55, 0x33, 0xFF), Color.FromRgb(0xAA, 0x00, 0xEE), Color.FromRgb(0x44, 0xDD, 0x88), Color.FromRgb(0x00, 0xFF, 0x66) }),
        ("Vaporwave", new[] { Color.FromRgb(0xFF, 0x00, 0x99), Color.FromRgb(0xEE, 0x66, 0xDD), Color.FromRgb(0xAA, 0x55, 0xFF), Color.FromRgb(0x22, 0xBB, 0xFF), Color.FromRgb(0x00, 0xEE, 0xCC), Color.FromRgb(0x44, 0xFF, 0xBB), Color.FromRgb(0xFF, 0x44, 0xAA) }),
        ("Cyberpunk", new[] { Color.FromRgb(0x0A, 0x00, 0x1A), Color.FromRgb(0x22, 0x00, 0x44), Color.FromRgb(0x66, 0x00, 0x88), Color.FromRgb(0xFF, 0x00, 0x66), Color.FromRgb(0xDD, 0x00, 0xCC), Color.FromRgb(0x00, 0x88, 0xFF), Color.FromRgb(0x00, 0xCC, 0xFF) }),
        ("Sakura",    new[] { Color.FromRgb(0xFF, 0xDD, 0xE8), Color.FromRgb(0xFF, 0xAA, 0xCC), Color.FromRgb(0xFF, 0x88, 0xAA), Color.FromRgb(0xFF, 0xCC, 0xDD), Color.FromRgb(0xFF, 0xEE, 0xEE), Color.FromRgb(0xCC, 0xEE, 0xBB), Color.FromRgb(0xAA, 0xDD, 0x99) }),
        ("Twilight",  new[] { Color.FromRgb(0x0D, 0x00, 0x22), Color.FromRgb(0x33, 0x00, 0x55), Color.FromRgb(0x77, 0x11, 0x77), Color.FromRgb(0xCC, 0x33, 0x66), Color.FromRgb(0xFF, 0x66, 0x44), Color.FromRgb(0xFF, 0xAA, 0x33), Color.FromRgb(0xFF, 0xDD, 0x77) }),
        ("Coral Reef",new[] { Color.FromRgb(0x00, 0x22, 0x44), Color.FromRgb(0x00, 0x66, 0x77), Color.FromRgb(0x00, 0xAA, 0x99), Color.FromRgb(0x33, 0xDD, 0xAA), Color.FromRgb(0xFF, 0x77, 0x66), Color.FromRgb(0xFF, 0xAA, 0x55), Color.FromRgb(0xFF, 0xDD, 0x88) }),
        ("Lavender",  new[] { Color.FromRgb(0x2A, 0x00, 0x55), Color.FromRgb(0x55, 0x22, 0x99), Color.FromRgb(0x88, 0x55, 0xCC), Color.FromRgb(0xBB, 0x88, 0xEE), Color.FromRgb(0xDD, 0xBB, 0xFF), Color.FromRgb(0xEE, 0xDD, 0xFF) }),
        ("Copper",    new[] { Color.FromRgb(0x1A, 0x0A, 0x00), Color.FromRgb(0x55, 0x22, 0x00), Color.FromRgb(0x99, 0x44, 0x00), Color.FromRgb(0xCC, 0x66, 0x11), Color.FromRgb(0xDD, 0x88, 0x33), Color.FromRgb(0xEE, 0xAA, 0x55), Color.FromRgb(0xFF, 0xCC, 0x88) }),
        ("Opaline",   new[] { Color.FromRgb(0x1F, 0x12, 0x35), Color.FromRgb(0x4B, 0x39, 0x89), Color.FromRgb(0x7A, 0x6F, 0xD7), Color.FromRgb(0x92, 0xE3, 0xE6), Color.FromRgb(0xB8, 0xF1, 0xD7), Color.FromRgb(0xF6, 0xB7, 0xE8), Color.FromRgb(0xF3, 0xF2, 0xFF) }),
        ("Voltage",   new[] { Color.FromRgb(0x05, 0x0A, 0x20), Color.FromRgb(0x00, 0x2A, 0x7A), Color.FromRgb(0x00, 0x6A, 0xFF), Color.FromRgb(0x00, 0xF5, 0xFF), Color.FromRgb(0x4A, 0xFF, 0xD9), Color.FromRgb(0x8A, 0xFF, 0x00), Color.FromRgb(0xF8, 0xFF, 0xD1) }),
        ("Ember Bloom", new[] { Color.FromRgb(0x18, 0x04, 0x00), Color.FromRgb(0x4B, 0x08, 0x00), Color.FromRgb(0x8A, 0x14, 0x00), Color.FromRgb(0xD9, 0x34, 0x00), Color.FromRgb(0xFF, 0x63, 0x00), Color.FromRgb(0xFF, 0xB1, 0x2A), Color.FromRgb(0xFF, 0xE6, 0x9A) }),
        ("Deep Sea",  new[] { Color.FromRgb(0x01, 0x06, 0x10), Color.FromRgb(0x00, 0x16, 0x2D), Color.FromRgb(0x00, 0x2D, 0x52), Color.FromRgb(0x00, 0x76, 0x8F), Color.FromRgb(0x00, 0xA5, 0xAF), Color.FromRgb(0x2E, 0xC4, 0xC7), Color.FromRgb(0xB7, 0xFF, 0xF4) }),
        ("Candy Pop", new[] { Color.FromRgb(0xFF, 0x3C, 0x8E), Color.FromRgb(0xFF, 0x5B, 0x77), Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0xFF, 0xD1, 0x3B), Color.FromRgb(0xB7, 0xF0, 0x48), Color.FromRgb(0x6A, 0xF0, 0x7D), Color.FromRgb(0x47, 0xB8, 0xFF) }),
        ("Midnight City", new[] { Color.FromRgb(0x06, 0x08, 0x16), Color.FromRgb(0x18, 0x12, 0x34), Color.FromRgb(0x32, 0x1B, 0x59), Color.FromRgb(0xA5, 0x23, 0xA7), Color.FromRgb(0xFF, 0x4A, 0xA3), Color.FromRgb(0x00, 0xC2, 0xFF), Color.FromRgb(0xF4, 0x63, 0x7B) }),
        ("Tropical Punch", new[] { Color.FromRgb(0x4A, 0x00, 0x5E), Color.FromRgb(0xA0, 0x11, 0x75), Color.FromRgb(0xFF, 0x4D, 0x6D), Color.FromRgb(0xFF, 0x8C, 0x42), Color.FromRgb(0xFF, 0xB8, 0x50), Color.FromRgb(0xFF, 0xD1, 0x66), Color.FromRgb(0x00, 0xD6, 0xB9) }),
        ("Northern Sky", new[] { Color.FromRgb(0x03, 0x0D, 0x1D), Color.FromRgb(0x00, 0x2C, 0x46), Color.FromRgb(0x00, 0x56, 0x7A), Color.FromRgb(0x00, 0xC9, 0x88), Color.FromRgb(0x35, 0xE1, 0xC3), Color.FromRgb(0x70, 0x6C, 0xFF), Color.FromRgb(0xD8, 0xF5, 0xFF) }),
        ("Rose Gold", new[] { Color.FromRgb(0x20, 0x0B, 0x12), Color.FromRgb(0x55, 0x1F, 0x33), Color.FromRgb(0x8F, 0x3B, 0x57), Color.FromRgb(0xD7, 0x86, 0x8A), Color.FromRgb(0xE8, 0xA0, 0x92), Color.FromRgb(0xF0, 0xB2, 0x91), Color.FromRgb(0xFF, 0xE8, 0xD0) }),
        ("Dream State", new[] { Color.FromRgb(0x12, 0x0E, 0x33), Color.FromRgb(0x2A, 0x1C, 0x61), Color.FromRgb(0x4A, 0x2D, 0x8C), Color.FromRgb(0x9A, 0x63, 0xFF), Color.FromRgb(0xD4, 0x7D, 0xF0), Color.FromRgb(0xFF, 0x8F, 0xD8), Color.FromRgb(0x8D, 0xF1, 0xFF) }),
        ("Ember",     new[] { Color.FromRgb(0x22, 0x00, 0x00), Color.FromRgb(0x44, 0x00, 0x00), Color.FromRgb(0x88, 0x11, 0x00), Color.FromRgb(0xBB, 0x22, 0x00), Color.FromRgb(0xDD, 0x44, 0x00), Color.FromRgb(0xFF, 0x55, 0x00), Color.FromRgb(0xFF, 0x33, 0x00) }),
        ("Rainbow",   new[] { Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0x88, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0x88, 0xFF), Color.FromRgb(0x88, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x88) }),
        ("Inferno",   new[] { Color.FromRgb(0x00, 0x00, 0x04), Color.FromRgb(0x33, 0x00, 0x22), Color.FromRgb(0x77, 0x00, 0x44), Color.FromRgb(0xCC, 0x33, 0x00), Color.FromRgb(0xFF, 0x77, 0x00), Color.FromRgb(0xFF, 0xBB, 0x00), Color.FromRgb(0xFF, 0xEE, 0x55), Color.FromRgb(0xFF, 0xFF, 0xCC) }),
        ("Storm",     new[] { Color.FromRgb(0x0D, 0x0D, 0x1A), Color.FromRgb(0x22, 0x22, 0x44), Color.FromRgb(0x44, 0x44, 0x66), Color.FromRgb(0xBB, 0xBB, 0xEE), Color.FromRgb(0xEE, 0xEE, 0xFF), Color.FromRgb(0x33, 0x33, 0x55), Color.FromRgb(0x11, 0x11, 0x22) }),
    };

    private Color _roomColor1 = ThemeManager.Accent;
    private Color _roomColor2 = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private ColorPalette _roomPalette = BuiltInPalettes.Fire;
    private string? _roomActivePreset;
    private int _roomEffectSpeed = 50;
    private Color[]? _paletteColors;
    private System.Windows.Threading.DispatcherTimer? _paletteCycleTimer;
    private int _paletteIndex;

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

        // ── Palette Editor ──
        _sceneContent.Children.Add(MakeSubLabel("COLORS"));
        var goveePaletteEditor = new PaletteEditorControl
        {
            Palette = _roomPalette,
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        goveePaletteEditor.PaletteChanged += palette =>
        {
            if (_loading) return;
            _roomPalette = palette;
            if (palette.Stops.Count >= 2)
            {
                var sorted = palette.Stops.OrderBy(s => s.Position).ToList();
                _roomColor1 = Color.FromRgb(sorted[0].R, sorted[0].G, sorted[0].B);
                _roomColor2 = Color.FromRgb(sorted[^1].R, sorted[^1].G, sorted[^1].B);
            }
            if (_activePattern != null && _activePattern != "__sync__")
                StartRoomPattern(_activePattern);
        };
        goveePaletteEditor.StopClicked += (stopIdx, currentColor) =>
        {
            var dialog = new ColorPickerDialog(currentColor) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c => goveePaletteEditor.UpdateSelectedStopColor(c);
            dialog.ShowDialog();
            goveePaletteEditor.ClearSelection();
        };
        _sceneContent.Children.Add(goveePaletteEditor);

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
                    PaletteName = _roomPalette.Name,
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

    private Border BuildToggleTile(string icon, string title, string subtitle, bool initialActive, Action<bool> onToggle, Color? iconColor = null)
        => BuildToggleTile(icon, title, subtitle, initialActive, onToggle, iconColor, null, out _);

    private Border BuildToggleTile(string icon, string title, string subtitle, bool initialActive, Action<bool> onToggle, Color? iconColor, string? extraStatus, out Action<string, bool>? extraStatusUpdater)
    {
        extraStatusUpdater = null;
        bool isActive = initialActive;
        var accent = ThemeManager.Accent;
        var icoColor = iconColor ?? accent;

        var tile = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand,
            MinWidth = 180,
        };

        var iconText = new TextBlock { Text = icon, FontSize = 16, Foreground = new SolidColorBrush(icoColor), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var subtitleText = new TextBlock { Text = subtitle, FontSize = 9, Margin = new Thickness(0, 2, 0, 0) };

        var leftStack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(iconText);
        titleRow.Children.Add(titleText);
        leftStack.Children.Add(titleRow);
        leftStack.Children.Add(subtitleText);

        // Optional extra status line (e.g. DreamView ACTIVE/STANDBY)
        Border? extraDot = null;
        TextBlock? extraLabel = null;
        if (extraStatus != null)
        {
            var extraRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            extraDot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            extraLabel = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold };
            extraRow.Children.Add(extraDot);
            extraRow.Children.Add(extraLabel);
            leftStack.Children.Add(extraRow);
            // Set initial state
            var eDot = extraDot; var eLbl = extraLabel;
            extraStatusUpdater = (s, active) =>
            {
                var c = active ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x55, 0x55, 0x55);
                eDot.Background = new SolidColorBrush(c);
                eLbl.Text = s;
                eLbl.Foreground = new SolidColorBrush(active ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x66, 0x66, 0x66));
            };
            extraStatusUpdater(extraStatus, false);
        }

        var statusText = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var statusPill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = statusText,
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftStack, 0);
        Grid.SetColumn(statusPill, 1);
        content.Children.Add(leftStack);
        content.Children.Add(statusPill);
        tile.Child = content;

        void UpdateVisuals()
        {
            var a = ThemeManager.Accent;
            if (isActive)
                tile.Background = new SolidColorBrush(Color.FromArgb(0x22, a.R, a.G, a.B));
            else
                tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            if (isActive)
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, a.R, a.G, a.B));
            else
                tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            titleText.Foreground = new SolidColorBrush(isActive ? a : Color.FromRgb(0xE8, 0xE8, 0xE8));
            subtitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            statusText.Text = isActive ? "ON" : "OFF";
            statusText.Foreground = new SolidColorBrush(isActive ? a : Color.FromRgb(0x55, 0x55, 0x55));
            if (isActive)
                statusPill.Background = new SolidColorBrush(Color.FromArgb(0x28, a.R, a.G, a.B));
            else
                statusPill.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        }
        UpdateVisuals();

        var tileTransform = new TranslateTransform(0, 0);
        tile.RenderTransform = tileTransform;
        tile.MouseLeftButtonDown += (_, _) =>
        {
            isActive = !isActive;
            UpdateVisuals();
            onToggle(isActive);
        };
        tile.MouseEnter += (_, _) =>
        {
            tileTransform.Y = -1;
            if (!isActive)
            {
                tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
                tile.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            tileTransform.Y = 0;
            if (!isActive) tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            UpdateVisuals(); // restore correct border
        };

        return tile;
    }

    private Border BuildStatusTile(string status, bool isActive, out Action<string, bool> updater)
    {
        var tile = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 110,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var titleText = new TextBlock
        {
            Text = "STATUS", FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(0, 0, 0, 3),
        };
        var dot = new Border
        {
            Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var statusText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(dot);
        statusRow.Children.Add(statusText);
        var inner = new StackPanel();
        inner.Children.Add(titleText);
        inner.Children.Add(statusRow);
        tile.Child = inner;

        updater = (s, active) =>
        {
            var c = active ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x66, 0x66, 0x66);
            dot.Background = new SolidColorBrush(c);
            statusText.Text = s;
            statusText.Foreground = new SolidColorBrush(active ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x88, 0x88, 0x88));
        };
        updater(status, isActive);
        return tile;
    }

    private Border BuildSyncToGlobalTile(bool isActive, Action<bool> onToggle)
    {
        var accent = Color.FromRgb(0x69, 0xF0, 0xAE);
        var tile = new Border
        {
            Width = 82, Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = isActive
                ? new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B))
                : FindBrush("BgDarkBrush"),
            BorderBrush = isActive
                ? new SolidColorBrush(accent)
                : FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Cursor = Cursors.Hand,
            ToolTip = isActive
                ? "Following Global effects — click to control independently"
                : "Click to sync with Global tab effects",
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = "🔗", FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Global", FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = isActive
                ? new SolidColorBrush(accent)
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        tile.Child = content;

        tile.MouseEnter += (_, _) =>
        {
            if (!isActive)
            {
                tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B));
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (!isActive)
            {
                tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
                tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            }
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            onToggle(!isActive);
            RebuildRoomTabContent(); // rebuild to update tile state
        };

        return tile;
    }

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
                : FindBrush("BgDarkBrush"),
            CornerRadius = new CornerRadius(6),
            BorderBrush = isActive
                ? new SolidColorBrush(Colors.White)
                : FindBrush("CardBorderBrush"),
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
                tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (_roomActivePreset != name)
            {
                tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
            }
        };

        var capturedColors = colors;
        tile.MouseLeftButtonDown += (_, _) =>
        {
            _roomColor1 = capturedColors[0];
            _roomColor2 = capturedColors[capturedColors.Length > 1 ? capturedColors.Length - 1 : 0];
            _roomActivePreset = name;

            // Start palette cycling if preset has 3+ colors and an effect is running
            StopPaletteCycle();
            if (capturedColors.Length >= 3 && _roomRgb != null && _activePattern != null && _activePattern != "__sync__")
            {
                _paletteColors = capturedColors;
                _paletteIndex = 0;
                _paletteCycleTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3), // cycle every 3 seconds
                };
                _paletteCycleTimer.Tick += (_, _) => CyclePaletteColors();
                _paletteCycleTimer.Start();
                CyclePaletteColors(); // apply first pair immediately
            }
            else if (_roomRgb != null && _activePattern != null && _activePattern != "__sync__"
                && Enum.TryParse<LightEffect>(_activePattern, true, out var eff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = eff,
                    R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                    R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                    EffectSpeed = _roomEffectSpeed,
                });
            }
            else
            {
                SendRoomColor(_roomColor1.R, _roomColor1.G, _roomColor1.B);
            }
            RebuildRoomTabContent();
        };

        return tile;
    }

    private Border MakeColorPill(string label, Color initial, string tooltip, Action onClick)
    {
        var c = initial;
        var darkBg = Color.FromArgb(0x33, c.R, c.G, c.B);
        var borderColor = Color.FromArgb(0x66, c.R, c.G, c.B);

        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(darkBg),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 4),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var dot = new Border
        {
            Width = 16, Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(c),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(dot);

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        pill.Tag = dot; // SetPillColor uses Tag to find inner dot
        pill.Child = row;

        pill.MouseEnter += (_, _) =>
        {
            var dotColor = ((SolidColorBrush)dot.Background).Color;
            pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, dotColor.R, dotColor.G, dotColor.B));
            pill.Background = new SolidColorBrush(Color.FromArgb(0x44, dotColor.R, dotColor.G, dotColor.B));
        };
        pill.MouseLeave += (_, _) =>
        {
            var dotColor = ((SolidColorBrush)dot.Background).Color;
            pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, dotColor.R, dotColor.G, dotColor.B));
            pill.Background = new SolidColorBrush(Color.FromArgb(0x33, dotColor.R, dotColor.G, dotColor.B));
        };

        pill.MouseLeftButtonDown += (_, _) => onClick();
        return pill;
    }

    private static void SetPillColor(Border? pill, Color color)
    {
        if (pill == null) return;
        if (pill.Tag is Border inner)
            inner.Background = new SolidColorBrush(color);
        pill.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
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
                // Update running effect colors or send static
                if (_roomRgb != null && _activePattern != null && _activePattern != "__sync__"
                    && Enum.TryParse<LightEffect>(_activePattern, true, out var eff2))
                {
                    _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                    {
                        Enabled = true, Effect = eff2,
                        R = _roomColor1.R, G = _roomColor1.G, B = _roomColor1.B,
                        R2 = _roomColor2.R, G2 = _roomColor2.G, B2 = _roomColor2.B,
                        EffectSpeed = _roomEffectSpeed,
                    });
                }
                else if (!isSecondary)
                {
                    SendRoomColor(c.R, c.G, c.B);
                }
            }
        };

        return pill;
    }

    private void CyclePaletteColors()
    {
        if (_paletteColors == null || _paletteColors.Length < 2 || _roomRgb == null || _activePattern == null) return;
        if (!Enum.TryParse<LightEffect>(_activePattern, true, out var eff)) return;

        int len = _paletteColors.Length;
        var c1 = _paletteColors[_paletteIndex % len];
        var c2 = _paletteColors[(_paletteIndex + 1) % len];
        _paletteIndex = (_paletteIndex + 1) % len;

        _roomColor1 = c1;
        _roomColor2 = c2;
        _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
        {
            Enabled = true, Effect = eff,
            R = c1.R, G = c1.G, B = c1.B,
            R2 = c2.R, G2 = c2.G, B2 = c2.B,
            EffectSpeed = _roomEffectSpeed,
        });
    }

    private void StopPaletteCycle()
    {
        _paletteCycleTimer?.Stop();
        _paletteCycleTimer = null;
        _paletteColors = null;
    }

    private void SendRoomColor(byte r, byte g, byte b)
    {
        StopRoomPattern();
        int brightness = _config?.Ambience.BrightnessScale ?? 100;

        // Govee LAN — sequential per-device so clear-razer finishes before color.
        // (Previously fire-and-forget, which caused a race where the disable-razer
        // packet arrived after the color command and reverted the device.)
        if (_config?.Ambience.GoveeEnabled == true)
        {
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;
                bool inSegmentMode = AmbienceSync.GetSegmentCount(dev) > 0;
                string ip = dev.Ip;
                _ = Task.Run(async () =>
                {
                    if (inSegmentMode)
                    {
                        await AmbienceSync.DisableSegmentMode(ip);
                        _sync?.ClearSegmentMode(ip);
                        await Task.Delay(120); // let the device settle out of razer mode
                    }
                    await AmbienceSync.SendColorAsync(ip, r, g, b);
                    await AmbienceSync.SendBrightnessAsync(ip, brightness);
                });
            }
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

    // Saved state for game mode restore
    private string? _savedPatternForGameMode;
    private bool _savedMusicReactive;
    private bool _savedVuFill;

    /// <summary>
    /// Stop room effect for screen sync (game mode or manual toggle).
    /// Saves the active pattern so it can be restarted later.
    /// </summary>
    /// <summary>
    /// Called when a device group is turned back on — restart room effect so lights resume
    /// the running effect instead of going to solid color.
    /// </summary>
    /// <summary>
    /// Public entry point used by button actions to set the active room
    /// effect by name (LightEffect enum value as string). Persists the
    /// choice to config.Ambience.RoomEffect so it survives restart.
    /// </summary>
    public void ApplyRoomEffect(string effectName)
    {
        if (_config == null || string.IsNullOrEmpty(effectName)) return;
        _config.Ambience.RoomEffect = effectName;
        _activePattern = effectName;
        StartRoomPattern(effectName);
        QueueSave();
    }

    public void ResumeRoomEffect()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_activePattern != null && _activePattern != "__sync__")
                StartRoomPattern(_activePattern);
        });
    }

    public void StopRoomPatternForScreenSync()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Save current state before stopping
            _savedMusicReactive = _corsairMusicTimer?.IsEnabled == true;
            _savedVuFill = _vuFillActive;
            if (_activePattern != null && _activePattern != "__sync__")
                _savedPatternForGameMode = _activePattern;

            // Stop all room modes — screen sync needs exclusive control
            StopCorsairMusicSync();
            StopVuFill();
            if (_roomRgb != null)
                StopRoomPattern();
            if (_config != null) _config.Ambience.LinkToLights = false;
            RebuildRoomTabContent();
        });
    }

    /// <summary>
    /// Restart room effect after screen sync ends (game mode exit).
    /// </summary>
    public void RestartRoomEffectAfterScreenSync()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Restore room effect
            var pattern = _savedPatternForGameMode;
            if (!string.IsNullOrEmpty(pattern))
                StartRoomPattern(pattern);

            // Restore Music Reactive / VU Fill
            if (_savedVuFill)
                StartVuFill();
            else if (_savedMusicReactive)
                StartGlobalMusicSync();

            RebuildRoomTabContent();
        });
    }

    // ── Room Pattern Engine (headless RgbController) ──────────────

    private void StartRoomPattern(string patternId, Color? c1 = null, Color? c2 = null, bool corsairOnly = false)
    {
        StopRoomPattern();
        _activePattern = patternId;
        _roomPatternCorsairOnly = corsairOnly;

        var color1 = c1 ?? _roomColor1;
        var color2 = c2 ?? _roomColor2;

        // Persist the active room effect so it resumes on next launch
        if (!corsairOnly && _config != null)
        {
            _config.Ambience.RoomEffect = patternId;
            _config.Ambience.RoomColor1 = $"#{color1.R:X2}{color1.G:X2}{color1.B:X2}";
            _config.Ambience.RoomColor2 = $"#{color2.R:X2}{color2.G:X2}{color2.B:X2}";
            _config.Ambience.RoomEffectSpeed = _roomEffectSpeed;
            _onSave?.Invoke(_config);
        }

        // Disable other sync modes so pattern isn't overwritten
        if (_config != null)
        {
            _config.Ambience.LinkToLights = false;
            _config.Corsair.LightSyncMode = "static";
        }

        // Resume any paused sync (don't send brightness — it kicks segment devices out of razer mode)
        if (!corsairOnly && _config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
                if (!string.IsNullOrWhiteSpace(dev.Ip))
                    AmbienceSync.ResumeSync(dev.Ip);

        // Create a headless RgbController to render effects
        _roomFrameCount = 0;
        _roomRgb = new RgbController();
        _roomRgb.SetBrightness(100);
        _roomRgb.SetAudioBandsProvider(() => App.AudioAnalyzer?.SmoothedBands ?? Array.Empty<float>());
        _roomRgb.UpdateCustomPalettes(_config?.CustomPalettes);

        // Set knob positions to full so effects render at full brightness
        for (int k = 0; k < 5; k++)
            _roomRgb.SetKnobPosition(k, 1.0f);

        // Configure as global lighting with the selected effect + palette.
        // For SingleColor we blank the palette name so ResolvePalette falls back to
        // the two-color palette built from color1/color2 — since SendRoomColor sets
        // both to the same color, every LED renders as a uniform solid.
        if (Enum.TryParse<LightEffect>(patternId, true, out var effect))
        {
            var gl = new GlobalLightConfig
            {
                Enabled = true,
                Effect = effect,
                R = color1.R, G = color1.G, B = color1.B,
                R2 = color2.R, G2 = color2.G, B2 = color2.B,
                EffectSpeed = _roomEffectSpeed,
                PaletteName = effect == LightEffect.SingleColor ? "" : _roomPalette.Name,
            };
            _roomRgb.UpdateGlobalConfig(gl);
        }

        // Subscribe to rendered frames
        _roomRgb.OnFrameReady += OnRoomFrame;

        // Start with a dummy output (no serial port — just runs the timer for rendering)
        _roomRgb.SetOutput((_, _, _) => { }, () => true);
    }

    /// <summary>
    /// Resume sync for all Govee devices — clears any PauseSync timers so frames send immediately.
    /// Called when starting room patterns or screen sync to avoid stale pauses from UI interactions.
    /// </summary>
    private void ResumeAllGoveeSync()
    {
        if (_config?.Ambience.GoveeEnabled != true) return;
        foreach (var dev in _config.Ambience.GoveeDevices)
            if (!string.IsNullOrWhiteSpace(dev.Ip))
                AmbienceSync.ResumeSync(dev.Ip);
    }

    private void StopRoomPattern()
    {
        StopPaletteCycle();
        if (_roomRgb != null)
        {
            _roomRgb.OnFrameReady -= OnRoomFrame;
            _roomRgb.SetOutput(null, null);
            _roomRgb.Dispose();
            _roomRgb = null;
        }
        _activePattern = null;
        _roomPatternCorsairOnly = false;
        App.Rgb?.SetScreenSyncColors(null); // restore Lights tab effect on Turn Up
    }

    private int _roomFrameCount;
    private float _musicReactiveBrightness = 1f;
    private void OnRoomFrame(byte[] linearColors)
    {
        var config = _config; // snapshot for thread safety (called from timer thread)
        if (config == null) return;
        _roomFrameCount++;

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

        // Music reactive: modulate brightness with fast attack / slow decay
        // Skip when the active pattern is already AudioReactive — it already renders
        // audio-driven brightness, so double-modulating would crush everything to near-black.
        var musicBands = _globalMusicBands;
        float musicBrightness = 1f;
        bool isAudioReactivePattern = _activePattern == "AudioReactive" || _activePattern == "AudioPositionBlend";
        if (_corsairMusicTimer?.IsEnabled == true && !isAudioReactivePattern
            && musicBands != null && musicBands.Length >= 5)
        {
            // Bass + low-mid drives the pulse (bands 0-2, ≤2kHz). Treble ignored —
            // industry standard: bass/mids control intensity, treble controls sparkle/detail.
            float gain = config.Ambience.MusicSensitivity / 50f;
            float energy = Math.Clamp((musicBands[0] * 0.5f + musicBands[1] * 0.3f + musicBands[2] * 0.2f) * gain, 0f, 1f);

            // Gentle curve for visible contrast without crushing moderate levels
            energy = MathF.Pow(energy, 1.3f);

            float target = 0.15f + energy * 0.85f; // 15% floor, 100% on peak
            if (target > _musicReactiveBrightness)
                _musicReactiveBrightness = target; // instant attack — snap to beat
            else
                _musicReactiveBrightness += (target - _musicReactiveBrightness) * 0.25f; // fast decay between beats

            musicBrightness = _musicReactiveBrightness;
            r = (byte)Math.Min(r * musicBrightness, 255);
            g = (byte)Math.Min(g * musicBrightness, 255);
            b = (byte)Math.Min(b * musicBrightness, 255);
        }

        // Build music-modulated frame for all devices
        byte[] frameForSync = linearColors;
        if (_corsairMusicTimer?.IsEnabled == true && !isAudioReactivePattern)
        {
            // Modulate non-AudioReactive patterns (e.g. Ocean, Scanner) with music brightness
            frameForSync = new byte[45];
            for (int i = 0; i < 45; i++)
                frameForSync[i] = (byte)Math.Min(linearColors[i] * musicBrightness, 255);
        }

        // Sync room effect to Turn Up hardware LEDs
        if (config.Ambience.SyncRoomToTurnUp)
            App.Rgb?.SetScreenSyncColors(frameForSync);

        // Send music-modulated frame to Govee (brightness pulses with beats)
        if (!_roomPatternCorsairOnly && config.Ambience.GoveeEnabled)
        {
            _sync?.OnRoomFrame(frameForSync, config.Ambience);

            // Cloud-only devices (no LAN IP) — throttle to ~1/sec (Cloud API rate limit)
            if (_cloudApi != null && (DateTime.UtcNow - _lastCloudRoomSend).TotalMilliseconds >= 1000)
            {
                _lastCloudRoomSend = DateTime.UtcNow;
                foreach (var dev in config.Ambience.GoveeDevices)
                {
                    if (!string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;
                    if (string.IsNullOrWhiteSpace(dev.DeviceId)) continue;
                    var cloud = _cloudDevices.FirstOrDefault(c => c.Device == dev.DeviceId);
                    if (cloud == null) continue;
                    _ = SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                        cloud.Device, cloud.Sku, GoveeCloudApi.SetColor(r, g, b)));
                }
            }
        }

        // Send full 15-LED frame to Corsair (skip if Turn Up hardware is already feeding Corsair)
        if (_corsairSync?.IsAvailable == true && config.Corsair.Enabled
            && config.Corsair.LightSyncMode != "vu_reactive")
        {
            float boost = config.Corsair.LightBrightness / 100f;
            var boosted = new byte[45];
            for (int i = 0; i < 45; i++)
                boosted[i] = (byte)Math.Min(frameForSync[i] * boost, 255);
            _corsairSync.SyncColors(boosted);
        }

        // ── LG UltraGear monitor LEDs (48 LEDs, maps from 15-LED buffer) ──
        if (_lgMonitor?.IsAvailable == true)
        {
            _lgMonitor.SyncFromRoomEffect(frameForSync);
        }

        // ── HA lights in room layout (~2/sec — HA/BLE devices are slow) ──
        if (_ha != null && config.HomeAssistant.Enabled && config.RoomLayout.Devices.Count > 0)
        {
            foreach (var dev in config.RoomLayout.Devices)
            {
                if (dev.DeviceType != "ha") continue;
                if (!_haLastSend.TryGetValue(dev.DeviceId, out var last) ||
                    (DateTime.UtcNow - last).TotalMilliseconds >= 500)
                {
                    _haLastSend[dev.DeviceId] = DateTime.UtcNow;
                    // Use spatial position if mapper has this device, otherwise averaged room color
                    int hr = r, hg = g, hb = b;
                    if (_spatialMapper?.GetDevicePosition(dev.DeviceId) != null)
                    {
                        var sampled = _spatialMapper.SampleForDevice(dev.DeviceId, frameForSync, 1);
                        if (sampled.Length > 0) { hr = sampled[0].R; hg = sampled[0].G; hb = sampled[0].B; }
                    }
                    // Separate brightness from color for proper dimming
                    int brightness = Math.Max(hr, Math.Max(hg, hb));
                    if (brightness > 0)
                    {
                        int nr = hr * 255 / brightness, ng = hg * 255 / brightness, nb = hb * 255 / brightness;
                        _ha.SetLightColorFireAndForget(dev.DeviceId,
                            (byte)nr, (byte)ng, (byte)nb, Math.Max(brightness, 1));
                    }
                }
            }
        }

        // ── Live preview on room canvas (every 5th frame = ~4fps) ──
        if (_roomCanvas != null && _spatialMapper?.HasLayout == true && _roomFrameCount % 5 == 0)
        {
            // Copy the frame for the dispatcher
            var frameCopy = new byte[45];
            Array.Copy(linearColors, frameCopy, 45);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                if (_spatialMapper == null || _config == null) return;
                foreach (var dev in _config.RoomLayout.Devices)
                {
                    var colors = _spatialMapper.SampleForDevice(dev.DeviceId, frameCopy, dev.SegmentCount);
                    var byteColors = new (byte R, byte G, byte B)[colors.Length];
                    for (int ci = 0; ci < colors.Length; ci++)
                        byteColors[ci] = ((byte)colors[ci].R, (byte)colors[ci].G, (byte)colors[ci].B);
                    _roomCanvas.SetDeviceColors(dev.DeviceId, byteColors);
                }
            });
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
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Set a solid color",
            Tag = "__solid__",
        };
        tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

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
            tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, solidColor.R, solidColor.G, solidColor.B));
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        };

        tile.MouseLeftButtonUp += (_, _) =>
        {
            foreach (var child in wrap.Children)
                if (child is Border b)
                {
                    b.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
                    b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
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
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = scene.Name,
                Tag = sceneId,
            };
            tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

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
                    tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
                    tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, tileColor.R, tileColor.G, tileColor.B));
                }
            };
            tile.MouseLeave += (_, _) =>
            {
                if (tile.Tag as string != activeSceneId)
                {
                    tile.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
                    tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                }
            };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                foreach (var child in wrap.Children)
                {
                    if (child is Border b && b.Tag as string != sceneId)
                    {
                        b.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
                        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
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

    private void BuildMusicSection(GoveeDeviceInfo device, StackPanel parent)
    {
        string? lanIp = FindLanIp(device);

        var musicCheck = new CheckBox
        {
            Content = "Enable Music Sync",
            IsChecked = _goveeLanMusicTimer?.IsEnabled == true && _goveeLanMusicIp == lanIp,
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Drive this device's colors from system audio — bass=red, mids=green, treble=blue",
        };
        musicCheck.Checked += (_, _) =>
        {
            if (string.IsNullOrEmpty(lanIp)) return;
            StartGoveeLanMusicSync(lanIp);
        };
        musicCheck.Unchecked += (_, _) =>
        {
            StopGoveeLanMusicSync();
        };
        parent.Children.Add(musicCheck);
    }

    private void StartGoveeLanMusicSync(string ip)
    {
        StopGoveeLanMusicSync();
        _goveeLanMusicIp = ip;

        App.AudioAnalyzer?.Start();

        _goveeLanMusicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _goveeLanMusicTimer.Tick += (_, _) =>
        {
            var bands = App.AudioAnalyzer?.SmoothedBands;
            if (bands == null || bands.Length < 5) return;

            float bass = bands[0] + bands[1];
            float mid = bands[2];
            float treble = bands[3] + bands[4];
            byte r = (byte)Math.Min(bass * 400, 255);
            byte g = (byte)Math.Min(mid * 400, 255);
            byte b = (byte)Math.Min(treble * 400, 255);

            _ = AmbienceSync.SendColorAsync(ip, r, g, b);
        };
        _goveeLanMusicTimer.Start();
    }

    private void StopGoveeLanMusicSync()
    {
        if (_goveeLanMusicTimer == null) return;
        _goveeLanMusicTimer.Stop();
        _goveeLanMusicTimer = null;
        _goveeLanMusicIp = null;
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
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = FindBrush("CardBorderBrush"),
        Margin = new Thickness(0, 4, 0, 12),
    };

    private Border MakeSectionCard(string title, params UIElement[] children)
    {
        var content = new StackPanel();
        var (bar, label) = MakeSectionHeader(title);
        content.Children.Add(WrapHeader(bar, label));
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
