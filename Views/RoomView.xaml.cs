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
    private StackPanel? _corsairDeviceRows;
    private DispatcherTimer? _corsairMusicTimer;

    // Music reactive shared state
    private volatile float[]? _globalMusicBands;
    private DispatcherTimer? _goveeLanMusicTimer;
    private string? _goveeLanMusicIp;

    // Room pattern engine — headless RgbController for rendering effects
    private RgbController? _roomRgb;
    private string? _activePattern;
    private bool _roomPatternCorsairOnly;
    private DateTime _lastCloudRoomSend = DateTime.MinValue;

    // Room layout
    private AmpUp.Core.Engine.SpatialMapper? _spatialMapper;
    private Controls.RoomCanvasControl? _roomCanvas;

    // Home Assistant integration (for HA light room sync)
    private HAIntegration? _ha;
    private readonly Dictionary<string, DateTime> _haLastSend = new();

    public void SetHAIntegration(HAIntegration? ha) { _ha = ha; }
    private Color _corsairColor1 = Color.FromRgb(0xFF, 0xD3, 0x00);
    private Color _corsairColor2 = Color.FromRgb(0xFF, 0x70, 0x00);

    // Navigation callback (set by MainWindow to navigate to Settings)
    public Action? NavigateToSettings { get; set; }

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

        // Initialize spatial mapper only if spatial sync is enabled
        if (config.RoomLayout.Devices.Count > 0 && config.Ambience.SpatialSync)
        {
            _spatialMapper = new AmpUp.Core.Engine.SpatialMapper();
            _spatialMapper.Recalculate(config.RoomLayout);
            _sync?.SetSpatialMapper(_spatialMapper);
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

    private int _roomTabIndex = 1; // 0=Layout, 1=Global, 2=Govee, 3=Corsair
    private StackPanel? _roomTabContent;

    private StackPanel? _screenSyncSettingsPanel;
    private StackPanel? _toggleRowContainer;

    private Border BuildRoomCard()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        // ── Pill-style tab bar (Layout / Global / Govee / Corsair) ──
        var accent = ThemeManager.Accent;
        var tabNames = new[] { "LAYOUT", "GLOBAL", "GOVEE", "CORSAIR" };
        var tabCount = tabNames.Length;
        var tabBorders = new Border[tabCount];

        var toggleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < tabCount; i++)
        {
            var idx = i;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(18, 5, 18, 5),
                Cursor = Cursors.Hand,
                MinWidth = 80,
            };
            tab.Child = new TextBlock
            {
                Text = tabNames[i], FontSize = 10, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            tabBorders[i] = tab;
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
        toggleBar.Child = tabRow;
        stack.Children.Add(toggleBar);

        // Dynamic toggle row — rebuilt per tab in RebuildRoomTabContent
        _toggleRowContainer = new StackPanel();
        stack.Children.Add(_toggleRowContainer);

        _roomTabContent = new StackPanel();
        stack.Children.Add(_roomTabContent);

        RebuildRoomTabContent();
        return card;
    }

    private static void SetRoomTabActive(Border tab, bool active, Color accent)
    {
        var label = tab.Child as TextBlock;
        if (active)
        {
            tab.Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
            tab.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B));
            tab.BorderThickness = new Thickness(1);
            tab.Opacity = 1.0;
            if (label != null) label.Foreground = new SolidColorBrush(accent);
        }
        else
        {
            tab.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            tab.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            tab.BorderThickness = new Thickness(1);
            tab.Opacity = 0.4;
            if (label != null) label.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        }
    }

    private void RebuildRoomTabContent()
    {
        if (_roomTabContent == null || _config == null) return;
        _roomTabContent.Children.Clear();
        _toggleRowContainer?.Children.Clear();

        // Build tab-specific toggle row
        if (_toggleRowContainer != null)
        {
            var toggleRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
            BuildTabToggleRow(toggleRow, _roomTabIndex);
            _toggleRowContainer.Children.Add(toggleRow);
        }

        switch (_roomTabIndex)
        {
            case 0: BuildLayoutTab(_roomTabContent); break;
            case 1: BuildGlobalRoomTab(_roomTabContent); break;
            case 2: BuildGoveeRoomTab(_roomTabContent); break;
            case 3: BuildCorsairRoomTab(_roomTabContent); break;
        }

        // Append Screen Sync settings panel (Global tab only)
        if (_roomTabIndex == 1 && _screenSyncSettingsPanel != null)
            _roomTabContent.Children.Add(_screenSyncSettingsPanel);
    }

    private void BuildTabToggleRow(WrapPanel row, int tabIndex)
    {
        if (tabIndex == 0) return; // Layout tab has no toggle row
        switch (tabIndex)
        {
            case 1: // Global
                // Amp Up sync
                bool syncActive = _activePattern == "__sync__";
                row.Children.Add(BuildToggleTile("🔗", "AMP UP", "Mirror knob LEDs to room",
                    syncActive, on =>
                    {
                        if (_config == null) return;
                        if (on) { StopRoomPattern(); _activePattern = "__sync__"; _config.Ambience.LinkToLights = true; _config.Corsair.LightSyncMode = "vu_reactive"; }
                        else { _activePattern = null; _config.Ambience.LinkToLights = false; _config.Corsair.LightSyncMode = "static"; }
                        QueueSave(); RebuildRoomTabContent();
                    }, Color.FromRgb(0x69, 0xF0, 0xAE)));

                // Global Music Reactive (brightness modulation)
                bool globalMusic = _corsairMusicTimer?.IsEnabled == true;
                row.Children.Add(BuildToggleTile("♪", "MUSIC REACTIVE", "Audio-driven brightness",
                    globalMusic, on =>
                    {
                        if (_loading) return;
                        if (on) StartGlobalMusicSync(); else StopCorsairMusicSync();
                    }, Color.FromRgb(0xFF, 0xB8, 0x00)));

                // Screen Sync
                bool gameModeOn = _config!.Ambience.GameModeEnabled;
                bool syncRunning = _config.Ambience.ScreenSync.Enabled;
                var screenSyncTile = BuildToggleTile("⬛", "SCREEN SYNC", "Fullscreen game detection",
                    gameModeOn, on =>
                    {
                        if (_loading || _config == null) return;
                        _config.Ambience.GameModeEnabled = on;
                        if (!on && _config.Ambience.ScreenSync.Enabled)
                        {
                            _config.Ambience.ScreenSync.Enabled = false;
                            if (_config.Corsair.Enabled) _config.Corsair.LightSyncMode = "vu_reactive";
                            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                        }
                        if (_screenSyncSettingsPanel != null)
                            _screenSyncSettingsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                        QueueSave();
                    }, Color.FromRgb(0x44, 0x8A, 0xFF),
                    syncRunning ? "ACTIVE" : "STANDBY",
                    out var statusUpdater);
                row.Children.Add(screenSyncTile);

                // Spatial sync toggle (Mirror vs Spatial)
                bool spatial = _config.Ambience.SpatialSync;
                row.Children.Add(BuildToggleTile("↔", "SPATIAL", spatial ? "Effect flows across devices" : "All devices show same effect",
                    spatial, on =>
                    {
                        if (_loading || _config == null) return;
                        _config.Ambience.SpatialSync = on;
                        QueueSave(); RebuildRoomTabContent();
                    }, Color.FromRgb(0xBB, 0x86, 0xFC)));

                // Screen Sync settings panel (rebuilt each time for simplicity)
                if (_screenSyncSettingsPanel != null)
                    _roomTabContent?.Children.Remove(_screenSyncSettingsPanel);
                var ssPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = gameModeOn ? Visibility.Visible : Visibility.Collapsed };
                BuildScreenSyncSettings(ssPanel, statusUpdater!);
                _screenSyncSettingsPanel = ssPanel;
                break;

            case 2: // Govee
                // Sync to Global
                row.Children.Add(BuildToggleTile("🔗", "SYNC TO GLOBAL", "Follow Global tab effects",
                    _config!.Ambience.GoveeSyncToGlobal, on =>
                    {
                        if (_config == null) return;
                        _config.Ambience.GoveeSyncToGlobal = on;
                        QueueSave(); RebuildRoomTabContent();
                    }, Color.FromRgb(0x69, 0xF0, 0xAE)));

                // Govee LAN Music Sync
                bool goveeMusicOn = _goveeLanMusicTimer?.IsEnabled == true;
                row.Children.Add(BuildToggleTile("♪", "MUSIC SYNC", "Bass=R / Mids=G / Treble=B via LAN",
                    goveeMusicOn, on =>
                    {
                        if (_loading) return;
                        // Find first Govee device with IP for music sync
                        var firstIp = _config?.Ambience.GoveeDevices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Ip))?.Ip;
                        if (firstIp == null) return;
                        if (on) StartGoveeLanMusicSync(firstIp); else StopGoveeLanMusicSync();
                    }, Color.FromRgb(0xFF, 0xB8, 0x00)));
                break;

            case 3: // Corsair
                // Sync to Global
                row.Children.Add(BuildToggleTile("🔗", "SYNC TO GLOBAL", "Follow Global tab effects",
                    _config!.Corsair.SyncToGlobal, on =>
                    {
                        if (_config == null) return;
                        _config.Corsair.SyncToGlobal = on;
                        QueueSave(); RebuildRoomTabContent();
                    }, Color.FromRgb(0x69, 0xF0, 0xAE)));

                // Corsair Music Sync
                bool corsairMusicOn = _corsairMusicTimer?.IsEnabled == true;
                row.Children.Add(BuildToggleTile("♪", "MUSIC SYNC", "Audio frequency → Corsair colors",
                    corsairMusicOn, on =>
                    {
                        if (_loading || _corsairSync == null) return;
                        if (on) StartCorsairMusicSync(); else StopCorsairMusicSync();
                    }, Color.FromRgb(0xFF, 0xB8, 0x00)));
                break;
        }
    }

    // ── LAYOUT TAB ─────────────────────────────────────────────────

    private void BuildLayoutTab(StackPanel stack)
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
                    : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = active
                    ? new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xE6, 0x76))
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
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

        stack.Children.Add(_roomCanvas);

        // ── Selected Device Properties (shown below canvas) ──
        _selectedDevicePanel = new StackPanel();
        stack.Children.Add(_selectedDevicePanel);

        // ── Unplaced Devices Tray ──
        var (trayBar, trayLabel) = MakeSectionHeader("DEVICES");
        stack.Children.Add(WrapHeader(trayBar, trayLabel));
        BuildDeviceTray(stack, layout);
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
        bool anyUnplaced = false;

        var trayRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        if (_config != null)
        {
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                string id = !string.IsNullOrWhiteSpace(dev.Ip) ? dev.Ip : dev.DeviceId;
                if (string.IsNullOrWhiteSpace(id) || placedIds.Contains(id)) continue;

                int segs = AmpUp.Core.Services.AmbienceSync.GetSegmentCount(dev);
                var addBtn = MakeDeviceTrayButton(dev.Name, "govee", () =>
                {
                    layout.Devices.Add(new RoomDevicePlacement
                    {
                        DeviceType = "govee", DeviceId = id, Name = dev.Name,
                        X = layout.WidthFt / 2, Y = layout.DepthFt / 2, Z = 4.0,
                        SegmentCount = Math.Max(segs, 1), LengthFt = segs > 1 ? 1.5 : 0.3,
                    });
                    OnLayoutChanged();
                    RebuildRoomTabContent();
                });
                trayRow.Children.Add(addBtn);
                anyUnplaced = true;
            }
        }

        // HA lights — show "Scan HA" button if no cache, or individual buttons from cache
        if (_config?.HomeAssistant.Enabled == true && _ha != null)
        {
            if (_haLightCache == null)
            {
                var scanBtn = MakeDeviceTrayButton("Scan HA Lights", "ha", () =>
                {
                    _ = ShowHaLightPickerAsync(layout);
                });
                trayRow.Children.Add(scanBtn);
                anyUnplaced = true;
            }
            else
            {
                foreach (var entity in _haLightCache)
                {
                    if (placedIds.Contains(entity.EntityId)) continue;
                    if (entity.EntityId.Contains("segment_")) continue; // skip individual segments
                    var eid = entity.EntityId;
                    var ename = entity.FriendlyName;
                    var addBtn = MakeDeviceTrayButton(ename, "ha", () =>
                    {
                        layout.Devices.Add(new RoomDevicePlacement
                        {
                            DeviceType = "ha", DeviceId = eid, Name = ename,
                            X = layout.WidthFt / 2, Y = layout.DepthFt / 2, Z = 4.0,
                            SegmentCount = 1, LengthFt = 0.3,
                        });
                        OnLayoutChanged();
                        RebuildRoomTabContent();
                    });
                    trayRow.Children.Add(addBtn);
                    anyUnplaced = true;
                }
            }
        }

        if (!anyUnplaced)
        {
            trayRow.Children.Add(new TextBlock
            {
                Text = "All discovered devices are placed in the layout.",
                FontSize = 11, Foreground = FindBrush("TextSecBrush"),
            });
        }
        stack.Children.Add(trayRow);
    }

    private Border MakeDeviceTrayButton(string name, string type, Action onClick)
    {
        var btn = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = "+ ", FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(name) ? name : type,
            FontSize = 11, Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        btn.Child = content;
        btn.MouseLeftButtonDown += (_, _) => onClick();
        return btn;
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

        // Update spatial mapper
        if (_spatialMapper == null)
            _spatialMapper = new AmpUp.Core.Engine.SpatialMapper();
        _spatialMapper.Recalculate(_config.RoomLayout);
        _sync?.SetSpatialMapper(_config.RoomLayout.Devices.Count > 0 ? _spatialMapper : null);

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

        // ── Section 2: PALETTE ──
        stack.Children.Add(MakeSeparator());
        var (preBar, preLabel) = MakeSectionHeader("PALETTE");
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
        };
        stack.Children.Add(paletteEditor);

        // Speed slider
        stack.Children.Add(MakeSubLabel("SPEED"));
        var speedSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Height = 35,
            AccentColor = ThemeManager.Accent,
            ShowLabel = false,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
                    PaletteName = _roomPalette.Name,
                });
            }
        };
        stack.Children.Add(speedSlider);
    }

    private void StartGlobalMusicSync()
    {
        StopCorsairMusicSync(); // stop any existing
        // Keep room pattern running — music modulates its brightness in OnRoomFrame

        App.AudioAnalyzer?.Start();

        _corsairMusicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _corsairMusicTimer.Tick += (_, _) =>
        {
            _globalMusicBands = App.AudioAnalyzer?.SmoothedBands;
        };
        _corsairMusicTimer.Start();
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

        // Shared dot/pill refs so presets can update manual pickers live
        Border? corsairPriDot = null, corsairSecDot = null;
        Border? corsairPriPill = null, corsairSecPill = null;

        // Apply colors to fields + live-update pills + running effect
        void ApplyCorsairColors(Color c1, Color c2)
        {
            _corsairColor1 = c1; _corsairColor2 = c2;
            if (corsairPriDot != null) corsairPriDot.Background = new SolidColorBrush(c1);
            if (corsairPriPill != null)
            {
                corsairPriPill.Background = new SolidColorBrush(Color.FromArgb(0x33, c1.R, c1.G, c1.B));
                corsairPriPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, c1.R, c1.G, c1.B));
            }
            if (corsairSecDot != null) corsairSecDot.Background = new SolidColorBrush(c2);
            if (corsairSecPill != null)
            {
                corsairSecPill.Background = new SolidColorBrush(Color.FromArgb(0x33, c2.R, c2.G, c2.B));
                corsairSecPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, c2.R, c2.G, c2.B));
            }
            if (_roomPatternCorsairOnly && _roomRgb != null && _activePattern != null
                && Enum.TryParse<LightEffect>(_activePattern, true, out var runEff))
            {
                _roomRgb.UpdateGlobalConfig(new GlobalLightConfig
                {
                    Enabled = true, Effect = runEff,
                    R = c1.R, G = c1.G, B = c1.B, R2 = c2.R, G2 = c2.G, B2 = c2.B, EffectSpeed = 50,
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
            tileContent.Children.Add(new Border { Width = 46, Height = 24, CornerRadius = new CornerRadius(4), ClipToBounds = true, Background = gb });
            tileContent.Children.Add(new TextBlock { Text = pname, FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            var tile = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 4, 4, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand, ToolTip = pname, Child = tileContent,
            };
            tile.MouseEnter += (_, _) => { tile.BorderBrush = new SolidColorBrush(Colors.White); tile.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)); };
            tile.MouseLeave += (_, _) => { tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)); tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)); };
            tile.MouseLeftButtonDown += (_, _) => ApplyCorsairColors(captured[0], captured[captured.Length > 1 ? captured.Length - 1 : 0]);
            corsairPresetWrap.Children.Add(tile);
        }
        corsairDeviceContent.Children.Add(corsairPresetWrap);

        // MANUAL pickers
        corsairDeviceContent.Children.Add(MakeSubLabel("MANUAL"));
        var corsairColorRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };

        Border MakeCorsairPill(string lbl, bool isSecondary)
        {
            var c = isSecondary ? _corsairColor2 : _corsairColor1;
            var dot = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(dot);
            inner.Children.Add(new TextBlock { Text = lbl, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), VerticalAlignment = VerticalAlignment.Center });
            var pill = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, c.R, c.G, c.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 4),
                Cursor = Cursors.Hand, ToolTip = $"{lbl} color — click to change", Child = inner,
            };
            if (isSecondary) { corsairSecDot = dot; corsairSecPill = pill; }
            else { corsairPriDot = dot; corsairPriPill = pill; }
            pill.MouseLeftButtonDown += (_, _) =>
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
            };
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
                Minimum = 1, Maximum = 100, Value = Math.Min(_config.Corsair.LightBrightness, 100),
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
            statusTileUpdater(active ? "ACTIVE" : "STANDBY", active);
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
        Logger.Log("Corsair music sync stopped");
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
        ("Sunset",    new[] { Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x4D, 0x2B), Color.FromRgb(0xFF, 0x6B, 0x35), Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0xAF, 0x00), Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0xFF, 0x6E, 0x40), Color.FromRgb(0xFF, 0x45, 0x00) }),
        ("Ocean",     new[] { Color.FromRgb(0x00, 0x1A, 0x4D), Color.FromRgb(0x00, 0x33, 0x66), Color.FromRgb(0x00, 0x55, 0x99), Color.FromRgb(0x00, 0x77, 0xB6), Color.FromRgb(0x00, 0xB4, 0xD8), Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x48, 0xCA, 0xE4), Color.FromRgb(0x90, 0xE0, 0xEF) }),
        ("Neon",      new[] { Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x80), Color.FromRgb(0xFF, 0x40, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0x80), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0x80, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0xCC) }),
        ("Forest",    new[] { Color.FromRgb(0x00, 0x2D, 0x00), Color.FromRgb(0x00, 0x44, 0x00), Color.FromRgb(0x00, 0x66, 0x22), Color.FromRgb(0x00, 0x88, 0x33), Color.FromRgb(0x00, 0xC8, 0x53), Color.FromRgb(0x4C, 0xAF, 0x50), Color.FromRgb(0xAE, 0xD5, 0x81), Color.FromRgb(0x76, 0xFF, 0x03) }),
        ("Lava",      new[] { Color.FromRgb(0x4A, 0x00, 0x00), Color.FromRgb(0x8B, 0x00, 0x00), Color.FromRgb(0xCC, 0x10, 0x00), Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0xFF, 0x6D, 0x00), Color.FromRgb(0xFF, 0x8A, 0x00), Color.FromRgb(0xFF, 0xD6, 0x00) }),
        ("Arctic",    new[] { Color.FromRgb(0x00, 0x97, 0xA7), Color.FromRgb(0x00, 0xAC, 0xC1), Color.FromRgb(0x00, 0xBD, 0xD0), Color.FromRgb(0x26, 0xC6, 0xDA), Color.FromRgb(0x4D, 0xD0, 0xE1), Color.FromRgb(0x80, 0xDE, 0xEA), Color.FromRgb(0xB2, 0xEB, 0xF2), Color.FromRgb(0xE0, 0xF7, 0xFA) }),
        ("Galaxy",    new[] { Color.FromRgb(0x0D, 0x00, 0x2B), Color.FromRgb(0x1A, 0x00, 0x5C), Color.FromRgb(0x4A, 0x14, 0x8C), Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xE0, 0x40, 0xFF), Color.FromRgb(0xFF, 0x80, 0xAB), Color.FromRgb(0xCE, 0x93, 0xD8) }),
        ("Toxic",     new[] { Color.FromRgb(0x00, 0x1A, 0x00), Color.FromRgb(0x00, 0x33, 0x00), Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x00, 0xCC, 0x44), Color.FromRgb(0x00, 0xE6, 0x76), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x76, 0xFF, 0x03), Color.FromRgb(0xCC, 0xFF, 0x00) }),
        ("Inferno",   new[] { Color.FromRgb(0x80, 0x00, 0x00), Color.FromRgb(0xCC, 0x00, 0x00), Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0x33, 0x00), Color.FromRgb(0xFF, 0x66, 0x00), Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0xCC, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00) }),
        ("Vaporwave", new[] { Color.FromRgb(0xFF, 0x00, 0xA0), Color.FromRgb(0xFF, 0x71, 0xCE), Color.FromRgb(0xB9, 0x67, 0xFF), Color.FromRgb(0x7B, 0x2F, 0xFF), Color.FromRgb(0x01, 0xCD, 0xFE), Color.FromRgb(0x05, 0xFC, 0xC1), Color.FromRgb(0xA0, 0xFF, 0xE0), Color.FromRgb(0xFF, 0x6E, 0xB4) }),
        ("Ember",     new[] { Color.FromRgb(0x33, 0x00, 0x00), Color.FromRgb(0x66, 0x00, 0x00), Color.FromRgb(0x8B, 0x00, 0x00), Color.FromRgb(0xAA, 0x11, 0x00), Color.FromRgb(0xCC, 0x33, 0x00), Color.FromRgb(0xFF, 0x22, 0x00), Color.FromRgb(0xFF, 0x45, 0x00), Color.FromRgb(0xFF, 0x66, 0x00) }),
        ("Aurora",    new[] { Color.FromRgb(0x00, 0xFF, 0x87), Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x40, 0xC4, 0xFF), Color.FromRgb(0x7B, 0x2F, 0xFF), Color.FromRgb(0xBB, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0x00, 0xFF, 0x44), Color.FromRgb(0x00, 0xFF, 0xCC) }),
        ("Rainbow",   new[] { Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0x80, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0x80, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x80) }),
    };

    private Color _roomColor1 = ThemeManager.Accent;
    private Color _roomColor2 = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private ColorPalette _roomPalette = BuiltInPalettes.Fire;
    private string? _roomActivePreset;
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
        _sceneContent.Children.Add(MakeSubLabel("PALETTE"));
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
            tile.Background = new SolidColorBrush(isActive
                ? Color.FromArgb(0x22, a.R, a.G, a.B)
                : Color.FromRgb(0x1C, 0x1C, 0x1C));
            tile.BorderBrush = new SolidColorBrush(isActive
                ? Color.FromArgb(0xA0, a.R, a.G, a.B)
                : Color.FromRgb(0x2E, 0x2E, 0x2E));
            titleText.Foreground = new SolidColorBrush(isActive ? a : Color.FromRgb(0xE8, 0xE8, 0xE8));
            subtitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            statusText.Text = isActive ? "ON" : "OFF";
            statusText.Foreground = new SolidColorBrush(isActive ? a : Color.FromRgb(0x55, 0x55, 0x55));
            statusPill.Background = new SolidColorBrush(isActive
                ? Color.FromArgb(0x28, a.R, a.G, a.B)
                : Color.FromRgb(0x24, 0x24, 0x24));
        }
        UpdateVisuals();

        tile.MouseLeftButtonDown += (_, _) =>
        {
            isActive = !isActive;
            UpdateVisuals();
            onToggle(isActive);
        };
        tile.MouseEnter += (_, _) =>
        {
            if (!isActive) tile.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        };
        tile.MouseLeave += (_, _) =>
        {
            if (!isActive) tile.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        };

        return tile;
    }

    private Border BuildStatusTile(string status, bool isActive, out Action<string, bool> updater)
    {
        var tile = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 110,
        };
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
                : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = isActive
                ? new SolidColorBrush(accent)
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
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
                tile.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B));
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (!isActive)
            {
                tile.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
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
                    EffectSpeed = 50,
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
                        EffectSpeed = 50,
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
            EffectSpeed = 50,
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
        // Govee LAN
        if (_config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip) || !dev.PoweredOn) continue;
                // Disable segment mode first (device may still be in razer mode from previous effect)
                if (AmbienceSync.GetSegmentCount(dev) > 0)
                    _sync?.ClearSegmentMode(dev.Ip);
                _ = AmbienceSync.SendColorAsync(dev.Ip, r, g, b);
                _ = AmbienceSync.SendBrightnessAsync(dev.Ip, 100);
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

    private void StartRoomPattern(string patternId, Color? c1 = null, Color? c2 = null, bool corsairOnly = false)
    {
        StopRoomPattern();
        _activePattern = patternId;
        _roomPatternCorsairOnly = corsairOnly;

        var color1 = c1 ?? _roomColor1;
        var color2 = c2 ?? _roomColor2;

        // Disable other sync modes so pattern isn't overwritten
        if (_config != null)
        {
            _config.Ambience.LinkToLights = false;
            _config.Corsair.LightSyncMode = "static";
        }

        // Set all devices to max brightness (dimming is in RGB values)
        if (!corsairOnly && _config?.Ambience.GoveeEnabled == true)
            foreach (var dev in _config.Ambience.GoveeDevices)
                if (!string.IsNullOrWhiteSpace(dev.Ip))
                    _ = AmbienceSync.SendBrightnessAsync(dev.Ip, 100);

        // Create a headless RgbController to render effects
        _roomRgb = new RgbController();
        _roomRgb.SetBrightness(100);
        _roomRgb.UpdateCustomPalettes(_config?.CustomPalettes);

        // Set knob positions to full so effects render at full brightness
        for (int k = 0; k < 5; k++)
            _roomRgb.SetKnobPosition(k, 1.0f);

        // Configure as global lighting with the selected effect + palette
        if (Enum.TryParse<LightEffect>(patternId, true, out var effect))
        {
            var gl = new GlobalLightConfig
            {
                Enabled = true,
                Effect = effect,
                R = color1.R, G = color1.G, B = color1.B,
                R2 = color2.R, G2 = color2.G, B2 = color2.B,
                EffectSpeed = 50,
                PaletteName = _roomPalette.Name,
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
    }

    private int _roomFrameCount;
    private void OnRoomFrame(byte[] linearColors)
    {
        if (_config == null) return;
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

        // Music reactive: modulate brightness from audio energy
        var musicBands = _globalMusicBands;
        if (_corsairMusicTimer?.IsEnabled == true && musicBands != null && musicBands.Length >= 5)
        {
            float energy = Math.Min((musicBands[0] + musicBands[1] + musicBands[2] + musicBands[3] + musicBands[4]) * 2.5f, 1f);
            float brightness = 0.15f + energy * 0.85f; // keep min 15% so effect remains visible
            r = (byte)(r * brightness);
            g = (byte)(g * brightness);
            b = (byte)(b * brightness);
        }

        // Send full frame to Govee via AmbienceSync (rate limited, segment-aware)
        if (!_roomPatternCorsairOnly && _config.Ambience.GoveeEnabled)
        {
            _sync?.OnRoomFrame(linearColors, _config.Ambience);

            // Cloud-only devices (no LAN IP) — throttle to ~1/sec (Cloud API rate limit)
            if (_cloudApi != null && (DateTime.UtcNow - _lastCloudRoomSend).TotalMilliseconds >= 1000)
            {
                _lastCloudRoomSend = DateTime.UtcNow;
                foreach (var dev in _config.Ambience.GoveeDevices)
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

        // Send full 15-LED frame to Corsair (maps across all device LEDs)
        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
        {
            float boost = _config.Corsair.LightBrightness / 100f;
            // Apply music brightness if active
            if (musicBands != null && _corsairMusicTimer?.IsEnabled == true)
            {
                float energy = Math.Min((musicBands[0] + musicBands[1] + musicBands[2] + musicBands[3] + musicBands[4]) * 2.5f, 1f);
                boost *= 0.15f + energy * 0.85f;
            }
            var boosted = new byte[45];
            for (int i = 0; i < 45; i++)
                boosted[i] = (byte)Math.Min(linearColors[i] * boost, 255);
            _corsairSync.SyncColors(boosted);
        }

        // ── HA lights in room layout (~2/sec — HA/BLE devices are slow) ──
        if (_ha != null && _config.HomeAssistant.Enabled && _config.RoomLayout.Devices.Count > 0)
        {
            foreach (var dev in _config.RoomLayout.Devices)
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
                        var sampled = _spatialMapper.SampleForDevice(dev.DeviceId, linearColors, 1);
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
