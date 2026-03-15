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

    // DreamView live preview swatches
    private readonly List<Border> _dreamZoneSwatches = new();
    private TextBlock? _dreamStatusLabel;
    private Border? _dreamActiveBanner;
    private Border? _dreamViewCard;

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
            ToolTip = "Go to Settings to configure Govee connection",
        };
        settingsBtn.MouseLeftButtonUp += (_, _) => NavigateToSettings?.Invoke();
        row.Children.Add(settingsBtn);

        var helpBtn = new TextBlock
        {
            Text = "? Help",
            FontSize = 12,
            Foreground = FindBrush("AccentBrush"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        helpBtn.MouseLeftButtonUp += (_, _) => GlassDialog.ShowInfo(
            "Govee Ambience controls your Govee lights via Cloud API.\n\n" +
            "1. Enable Govee LAN sync in Settings\n" +
            "2. Add your Cloud API key for scenes, segments, and music mode\n" +
            "3. Get your API key at developer.govee.com",
            owner: Window.GetWindow(this));
        row.Children.Add(helpBtn);

        // Status indicator
        var statusDot = new Border
        {
            Width = 8, Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        var statusLabel = new TextBlock
        {
            FontSize = 11,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(statusDot);
        row.Children.Add(statusLabel);

        // Update status on load
        Loaded += (_, _) => UpdateTopBarStatus(statusDot, statusLabel);

        TopBar.Children.Add(row);
    }

    private void UpdateTopBarStatus(Border dot, TextBlock label)
    {
        if (_config == null) return;
        bool lanEnabled = _config.Ambience.GoveeEnabled;
        bool cloudEnabled = _config.Ambience.GoveeCloudEnabled;
        bool hasKey = !string.IsNullOrEmpty(_config.Ambience.GoveeApiKey);
        int lanDevices = _config.Ambience.GoveeDevices.Count;
        int cloudDevices = _cloudDevices.Count;

        if (!lanEnabled && !cloudEnabled)
        {
            dot.Background = Brush("#555555");
            label.Text = "Govee disabled — enable in Settings";
        }
        else if (cloudEnabled && hasKey && cloudDevices > 0)
        {
            dot.Background = Brush("#00E676");
            label.Text = $"Connected — {cloudDevices} device(s)";
        }
        else if (lanEnabled && lanDevices > 0)
        {
            dot.Background = Brush("#00E676");
            label.Text = $"LAN — {lanDevices} device(s)";
        }
        else if (cloudEnabled && hasKey)
        {
            dot.Background = Brush("#FFB800");
            label.Text = "Cloud API — loading devices...";
        }
        else if (cloudEnabled && !hasKey)
        {
            dot.Background = Brush("#FFB800");
            label.Text = "Cloud API enabled — add API key in Settings";
        }
        else
        {
            dot.Background = Brush("#FFB800");
            label.Text = "No devices — scan in Settings";
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

    // ── Device Panel ──────────────────────────────────────────────────

    private void RebuildDevicePanel()
    {
        DevicePanel.Children.Clear();

        // Update top bar status
        var topBarRow = TopBar.Children.Count > 0 ? TopBar.Children[0] as StackPanel : null;
        if (topBarRow != null && topBarRow.Children.Count >= 4)
        {
            UpdateTopBarStatus(
                topBarRow.Children[2] as Border ?? new Border(),
                topBarRow.Children[3] as TextBlock ?? new TextBlock());
        }

        if (_config == null) return;

        bool hasKey = !string.IsNullOrEmpty(_config.Ambience.GoveeApiKey);

        if (!_config.Ambience.GoveeEnabled && !_config.Ambience.GoveeCloudEnabled)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "Govee is disabled",
                "Enable Govee LAN or Cloud API in Settings to get started.",
                "Open Settings", () => NavigateToSettings?.Invoke()));
            AppendDreamViewCard();
            return;
        }

        if (_cloudDevices.Count == 0 && _config.Ambience.GoveeDevices.Count == 0)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "No devices found",
                "Scan for LAN devices or enable the Cloud API in Settings.",
                "Open Settings", () => NavigateToSettings?.Invoke()));
            AppendDreamViewCard();
            return;
        }

        // Show banner when LED sync is active
        if (_config.Ambience.LinkToLights)
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xE6, 0x76)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xE6, 0x76)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12),
            };
            var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
            bannerRow.Children.Add(new TextBlock
            {
                Text = "Linked to Ambience",
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            bannerRow.Children.Add(new TextBlock
            {
                Text = "LED colors are being mirrored to your Govee devices. Unlink in the Lights tab to control independently.",
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            banner.Child = bannerRow;
            DevicePanel.Children.Add(banner);
        }

        if (_cloudDevices.Count > 0)
        {
            // Cloud API mode — full control with scenes, segments, music
            foreach (var device in _cloudDevices)
                DevicePanel.Children.Add(BuildDeviceCard(device));
        }
        else if (_config.Ambience.GoveeDevices.Count > 0)
        {
            // LAN-only mode — create device info from LAN config for basic controls
            foreach (var lan in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(lan.Ip)) continue;
                var deviceInfo = new GoveeDeviceInfo
                {
                    Device = lan.DeviceId,
                    Sku = lan.Sku,
                    DeviceName = lan.Name,
                    Capabilities = new List<string>(),
                };
                DevicePanel.Children.Add(BuildDeviceCard(deviceInfo));
            }

            if (_config.Ambience.GoveeCloudEnabled && hasKey)
            {
                // Cloud enabled but fetch returned empty — offer refresh
                DevicePanel.Children.Add(MakeSetupCard(
                    "Cloud devices not loaded",
                    "Couldn't fetch device list from Govee Cloud. LAN controls are available above.",
                    "Retry", () => _ = FetchCloudDevicesAndRebuild()));
            }
            else if (!_config.Ambience.GoveeCloudEnabled)
            {
                // LAN-only hint
                DevicePanel.Children.Add(MakeSetupCard(
                    "Want scenes and more?",
                    "Enable the Cloud API in Settings for scenes and music mode.",
                    "Open Settings", () => NavigateToSettings?.Invoke()));
            }
        }
        else
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "No devices found",
                "Make sure your Govee devices are online, then scan in Settings.",
                "Open Settings", () => NavigateToSettings?.Invoke()));
        }

        // Always show DreamView card at the bottom
        AppendDreamViewCard();

        // If DreamView was already enabled, dim device cards
        if (_config.Ambience.ScreenSync.Enabled)
            SetDevicePanelDimmed(true);
    }

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

    // ── Device Card ──────────────────────────────────────────────────

    private Border BuildDeviceCard(GoveeDeviceInfo device)
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = FindBrush("AccentBrush"),
        };

        var stack = new StackPanel();
        card.Child = stack;

        // ── Header ──
        var deviceLabel = !string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceName
            : !string.IsNullOrEmpty(device.Sku) ? AmbienceSync.GetProductName(device.Sku)
            : "Govee Device";
        var headerText = deviceLabel != device.Sku && !string.IsNullOrEmpty(device.Sku)
            ? $"{deviceLabel}  ({device.Sku})" : deviceLabel;
        var (headerBar, headerLabel) = MakeSectionHeader(headerText);
        stack.Children.Add(WrapHeader(headerBar, headerLabel));

        // ── Controls row: Power + Brightness + Color ──
        var controlsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

        // Find LAN IP for this device (match by MAC/DeviceId or SKU)
        string? lanIp = FindLanIp(device);

        // Power toggle — use LAN if available, fallback to Cloud API
        var onOffCheck = new CheckBox
        {
            Content = "On",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
        };
        onOffCheck.Checked += async (_, _) =>
        {
            if (_loading) return;
            if (lanIp != null)
            {
                AmbienceSync.PauseSync(lanIp, 30);
                await AmbienceSync.SendTurnAsync(lanIp, true);
            }
            else if (_cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(true)));
        };
        onOffCheck.Unchecked += async (_, _) =>
        {
            if (_loading) return;
            if (lanIp != null)
            {
                AmbienceSync.PauseSync(lanIp, 30);
                await AmbienceSync.SendTurnAsync(lanIp, false);
            }
            else if (_cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(false)));
        };
        controlsRow.Children.Add(onOffCheck);

        // Brightness — use LAN if available
        controlsRow.Children.Add(MakeSubLabel("BRIGHTNESS"));
        var brightnessSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 100,
            Width = 180, Height = 40,
            Suffix = "%",
            AccentColor = ThemeManager.Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
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
        controlsRow.Children.Add(brightnessSlider);

        stack.Children.Add(controlsRow);

        // ── Query actual device state via LAN ──
        if (lanIp != null)
        {
            _ = QueryDeviceStateAsync(lanIp, onOffCheck, brightnessSlider);
        }

        // ── Colors section (Solid tile + Cloud scenes if available) ──
        stack.Children.Add(MakeSeparator());
        var (scBar, scLabel) = MakeSectionHeader("COLORS");
        stack.Children.Add(WrapHeader(scBar, scLabel));
        var colorsContainer = new StackPanel();
        stack.Children.Add(colorsContainer);
        BuildColorsSection(device, colorsContainer, lanIp, onOffCheck);


        // ── Music mode section ──
        bool hasMusic = device.Capabilities?.Contains("devices.capabilities.music_setting") == true;
        if (hasMusic)
        {
            stack.Children.Add(MakeSeparator());
            var (musBar, musLabel) = MakeSectionHeader("MUSIC MODE");
            stack.Children.Add(WrapHeader(musBar, musLabel));
            BuildMusicSection(device, stack);
        }

        return card;
    }

    // ── Colors (Solid + Scenes) ─────────────────────────────────────

    private void BuildColorsSection(GoveeDeviceInfo device, StackPanel container, string? lanIp, CheckBox? powerCheck = null)
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
            // Try fetching via API as fallback
            _ = FetchScenesIntoWrapAsync(device, wrap, powerCheck);
        }

        // When power is turned off, deselect all scene tiles
        if (powerCheck != null)
        {
            powerCheck.Unchecked += (_, _) =>
            {
                foreach (var child in wrap.Children)
                {
                    if (child is Border b)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    }
                }
            };
        }
    }

    private void BuildSolidTile(WrapPanel wrap, GoveeDeviceInfo device, string? lanIp)
    {
        var solidColor = Color.FromRgb(0x00, 0xE6, 0x76); // accent green
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
            // Deselect all tiles in the wrap
            foreach (var child in wrap.Children)
                if (child is Border b)
                {
                    b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }

            // Select solid
            tile.Background = new SolidColorBrush(Color.FromArgb(0x30, solidColor.R, solidColor.G, solidColor.B));
            tile.BorderBrush = new SolidColorBrush(solidColor);

            // Open color picker
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

            // Icon
            tileContent.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(
                    (byte)(tileColor.R * 0.7), (byte)(tileColor.G * 0.7), (byte)(tileColor.B * 0.7))),
            });

            // Label
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

            // Hover effects
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
                // Deselect previous
                foreach (var child in wrap.Children)
                {
                    if (child is Border b && b.Tag as string != sceneId)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    }
                }

                // Select this tile
                tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                tile.BorderBrush = new SolidColorBrush(tileColor);
                activeSceneId = sceneId;

                var sc = scenes.FirstOrDefault(s => s.Id == sceneId);
                var sceneValue = sc?.RawValue ?? (object)new { id = sceneId };
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetScene(sceneValue)));

                // Selecting a scene turns the light on
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

    // ── Segments ──────────────────────────────────────────────────────

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

        // Sensitivity slider (placed after tiles)
        var sensSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 40,
            Suffix = "",
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 4, 0, 8),
        };

        // Mode tiles
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
                // Deselect all
                foreach (var child in wrap.Children)
                    if (child is Border b)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    }

                // Select this
                tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                tile.BorderBrush = new SolidColorBrush(tileColor);
                activeModeId = modeId;

                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(modeId, (int)sensSlider.Value)));
            };

            wrap.Children.Add(tile);
        }

        parent.Children.Add(wrap);

        // Sensitivity
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
        Color.FromRgb(0xFF, 0x6B, 0x35), // orange
        Color.FromRgb(0x64, 0xB5, 0xF6), // blue
        Color.FromRgb(0xFF, 0x80, 0xAB), // pink
        Color.FromRgb(0x69, 0xF0, 0xAE), // green
        Color.FromRgb(0xBA, 0x68, 0xC8), // purple
        Color.FromRgb(0xFF, 0xD7, 0x40), // gold
        Color.FromRgb(0x4D, 0xD0, 0xE1), // cyan
        Color.FromRgb(0xFF, 0x52, 0x52), // red
        Color.FromRgb(0xAE, 0xD5, 0x81), // lime
        Color.FromRgb(0xE0, 0x40, 0xFF), // magenta
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
        // Nature / Weather
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
        // Light / Color
        if (n.Contains("rainbow")) return "🌈";
        if (n.Contains("candle")) return "🕯";
        if (n.Contains("white light") || n.Contains("reading")) return "💡";
        if (n.Contains("glitter") || n.Contains("sparkle")) return "✨";
        if (n.Contains("colorful")) return "🎨";
        if (n.Contains("neon")) return "💜";
        // Mood / Activity
        if (n.Contains("romantic") || n.Contains("romance")) return "💕";
        if (n.Contains("party")) return "🎉";
        if (n.Contains("energetic") || n.Contains("energic")) return "⚡";
        if (n.Contains("breathe") || n.Contains("breathing")) return "🫧";
        if (n.Contains("asleep") || n.Contains("sleep")) return "😴";
        if (n.Contains("fright") || n.Contains("horror")) return "👻";
        if (n.Contains("siren")) return "🚨";
        // Music / Entertainment
        if (n.Contains("drum") || n.Contains("beat")) return "🥁";
        if (n.Contains("movie") || n.Contains("film")) return "🎬";
        if (n.Contains("comedy") || n.Contains("comedies")) return "😂";
        if (n.Contains("action")) return "💥";
        if (n.Contains("suspense") || n.Contains("thriller")) return "😱";
        if (n.Contains("documentary") || n.Contains("documentaries")) return "📽";
        if (n.Contains("war")) return "⚔";
        if (n.Contains("science fiction") || n.Contains("sci-fi")) return "🚀";
        // Seasonal
        if (n.Contains("season")) return "🍂";
        if (n.Contains("christmas") || n.Contains("xmas")) return "🎄";
        if (n.Contains("halloween")) return "🎃";
        // Other
        if (n.Contains("crossing")) return "🚦";
        if (n.Contains("literary")) return "📖";
        if (n.Contains("pulse")) return "💓";
        // Fallback — use the first character as a styled icon
        return "◆";
    }

    // ── Device state query ────────────────────────────────────────────

    private async Task QueryDeviceStateAsync(string ip, CheckBox onOffCheck, StyledSlider brightnessSlider)
    {
        try
        {
            var status = await AmbienceSync.GetDeviceStatusAsync(ip);
            if (status == null) return;

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

    /// <summary>
    /// Find the LAN IP for a cloud device by matching DeviceId (MAC) or SKU against config.
    /// </summary>
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

    // ── Safe API call ─────────────────────────────────────────────────

    private static async Task SafeCloudCall(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Logger.Log($"[Ambience] Cloud API error: {ex.Message}"); }
    }

    // ── DreamView Card ────────────────────────────────────────────────

    /// <summary>
    /// Build and append the DreamView / Screen Sync card to the device panel.
    /// Call from RebuildDevicePanel after the Govee device cards.
    /// </summary>
    private void AppendDreamViewCard()
    {
        if (_config == null) return;

        var cfg = _config.Ambience.ScreenSync;

        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 12, 0, 12),
        };
        _dreamViewCard = card;
        var stack = new StackPanel();
        card.Child = stack;

        // ── Section header ──
        var (headerBar, headerLabel) = MakeSectionHeader("DREAMVIEW — Screen Sync");
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        headerRow.Children.Add(headerBar);
        headerRow.Children.Add(headerLabel);

        // Toggle ON/OFF button in header row
        var enableToggle = new CheckBox
        {
            Content = "Enable",
            IsChecked = cfg.Enabled,
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            ToolTip = "Start/stop real-time screen color sync to Govee lights",
        };
        enableToggle.Checked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Enabled = true;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            SetDevicePanelDimmed(true);
            QueueSave();
        };
        enableToggle.Unchecked += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.Enabled = false;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            if (_dreamStatusLabel != null) _dreamStatusLabel.Text = "Stopped";
            SetDevicePanelDimmed(false);
            QueueSave();
        };
        headerRow.Children.Add(enableToggle);
        stack.Children.Add(headerRow);

        // ── Description ──
        stack.Children.Add(new TextBlock
        {
            Text = "Captures your screen in real time and maps zone colors to Govee lights. Assign each device to a screen region below.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        stack.Children.Add(MakeSeparator());

        // ── Settings row 1: Monitor + FPS + Zones ──
        var row1 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        // Monitor selector — show friendly display name (e.g. "DELL U2723QE")
        var screens = System.Windows.Forms.Screen.AllScreens;
        var monitorCount = screens.Length;
        var friendlyNames = NativeMethods.GetMonitorFriendlyNames();
        row1.Children.Add(MakeSubLabel("MONITOR"));
        var monitorCombo = new ComboBox { MinWidth = 200, MaxWidth = 350, Margin = new Thickness(0, 0, 20, 0), ToolTip = "Which monitor to capture" };
        for (int i = 0; i < monitorCount; i++)
        {
            var screen = screens[i];
            var gdiName = screen.DeviceName.TrimEnd('\0');
            var friendly = friendlyNames.GetValueOrDefault(gdiName, "");
            var label = !string.IsNullOrEmpty(friendly)
                ? (screen.Primary ? $"{friendly} (Primary)" : friendly)
                : (screen.Primary ? $"Display {i + 1} (Primary)" : $"Display {i + 1}");
            monitorCombo.Items.Add(label);
        }
        monitorCombo.SelectedIndex = Math.Clamp(cfg.MonitorIndex, 0, Math.Max(0, monitorCount - 1));
        monitorCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            _config.Ambience.ScreenSync.MonitorIndex = monitorCombo.SelectedIndex;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row1.Children.Add(monitorCombo);

        // FPS selector
        row1.Children.Add(MakeSubLabel("FPS"));
        var fpsCombo = new ComboBox { Width = 90, Margin = new Thickness(0, 0, 20, 0), ToolTip = "Capture rate — 30fps is smooth; 15fps uses less CPU" };
        foreach (var fps in new[] { 15, 30, 60 })
            fpsCombo.Items.Add($"{fps}fps");
        fpsCombo.SelectedIndex = cfg.TargetFps switch { 60 => 2, 15 => 0, _ => 1 };
        fpsCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            int selected = fpsCombo.SelectedIndex switch { 0 => 15, 2 => 60, _ => 30 };
            _config.Ambience.ScreenSync.TargetFps = selected;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row1.Children.Add(fpsCombo);

        // Zone count
        row1.Children.Add(MakeSubLabel("ZONES"));
        var zoneCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 20, 0), ToolTip = "Number of screen zones sampled per frame" };
        foreach (var z in new[] { 4, 8, 16 })
            zoneCombo.Items.Add($"{z} zones");
        zoneCombo.SelectedIndex = cfg.ZoneCount switch { 4 => 0, 16 => 2, _ => 1 };
        zoneCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            int selected = zoneCombo.SelectedIndex switch { 0 => 4, 2 => 16, _ => 8 };
            _config.Ambience.ScreenSync.ZoneCount = selected;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row1.Children.Add(zoneCombo);

        stack.Children.Add(row1);

        // ── Settings row 2: Saturation + Sensitivity ──
        var row2 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        row2.Children.Add(MakeSubLabel("SATURATION"));
        var satLabel = new TextBlock
        {
            Text = $"{cfg.Saturation:F1}×",
            FontSize = 11,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 32,
            Margin = new Thickness(4, 0, 8, 0),
        };
        var satSlider = new AmpUp.Controls.StyledSlider
        {
            Minimum = 0.5, Maximum = 2.0, Value = cfg.Saturation,
            Width = 120, Margin = new Thickness(0, 0, 20, 0),
            ShowLabel = false,
            ToolTip = "Boost color saturation — higher = more vivid room colors",
        };
        satSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            float v = (float)Math.Round(satSlider.Value, 1);
            satLabel.Text = $"{v:F1}×";
            _config.Ambience.ScreenSync.Saturation = v;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row2.Children.Add(satSlider);
        row2.Children.Add(satLabel);

        row2.Children.Add(MakeSubLabel("SENSITIVITY"));
        var sensLabel = new TextBlock
        {
            Text = cfg.Sensitivity.ToString(),
            FontSize = 11,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 24,
            Margin = new Thickness(4, 0, 0, 0),
        };
        var sensSlider = new AmpUp.Controls.StyledSlider
        {
            Minimum = 1, Maximum = 20, Value = cfg.Sensitivity,
            Width = 120, Margin = new Thickness(0, 0, 8, 0),
            ShowLabel = false,
            ToolTip = "Minimum color change to trigger a send — lower = more reactive; higher = less flicker",
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            int v = (int)sensSlider.Value;
            sensLabel.Text = v.ToString();
            _config.Ambience.ScreenSync.Sensitivity = v;
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            QueueSave();
        };
        row2.Children.Add(sensSlider);
        row2.Children.Add(sensLabel);

        stack.Children.Add(row2);

        stack.Children.Add(MakeSeparator());

        // ── Live zone preview ──
        stack.Children.Add(new TextBlock
        {
            Text = "ZONE PREVIEW",
            FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        var previewPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
            ClipToBounds = false,
        };
        _dreamZoneSwatches.Clear();
        for (int i = 0; i < cfg.ZoneCount; i++)
        {
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = $"Zone {i + 1}",
            };
            _dreamZoneSwatches.Add(swatch);
            previewPanel.Children.Add(swatch);
        }
        stack.Children.Add(previewPanel);

        // Status label
        _dreamStatusLabel = new TextBlock
        {
            Text = _dreamSync?.Status ?? "Stopped",
            FontSize = 11,
            Style = FindStyle("SecondaryText"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        stack.Children.Add(_dreamStatusLabel);

        // Start a timer to refresh the status label every second
        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statusTimer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            if (_dreamStatusLabel != null && _dreamSync != null)
                _dreamStatusLabel.Text = _dreamSync.Status;
        };
        card.Unloaded += (_, _) => statusTimer.Stop();
        statusTimer.Start();

        stack.Children.Add(MakeSeparator());

        // ── Device zone assignment ──
        stack.Children.Add(new TextBlock
        {
            Text = "DEVICE ZONE MAPPING",
            FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (_config.Ambience.GoveeDevices.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No Govee LAN devices configured. Add devices in Settings → Ambience.",
                Style = FindStyle("SecondaryText"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip)) continue;

                var devRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8),
                };

                var devName = new TextBlock
                {
                    Text = !string.IsNullOrWhiteSpace(dev.Name) ? dev.Name : dev.Ip,
                    FontSize = 12,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 160,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                devRow.Children.Add(devName);

                var zoneComboDevice = new ComboBox { Width = 100, Margin = new Thickness(8, 0, 0, 0) };
                foreach (ZoneSide side in Enum.GetValues<ZoneSide>())
                    zoneComboDevice.Items.Add(side.ToString());

                // Find or create mapping for this device
                string devIp = dev.Ip; // capture for lambda
                var existing = cfg.DeviceMappings.FirstOrDefault(m => m.DeviceIp == devIp);
                if (existing == null)
                {
                    // Auto-create mapping so the device is always synced
                    existing = new ZoneDeviceMapping { DeviceIp = devIp, Side = ZoneSide.Full };
                    cfg.DeviceMappings.Add(existing);
                    // Push new mapping to DreamSync and save
                    _dreamSync?.UpdateConfig(cfg, _config?.Ambience ?? new AmbienceConfig());
                    QueueSave();
                }
                zoneComboDevice.SelectedIndex = (int)existing.Side;

                zoneComboDevice.SelectionChanged += (_, _) =>
                {
                    if (_loading || _config == null) return;
                    var mappings = _config.Ambience.ScreenSync.DeviceMappings;
                    var m = mappings.FirstOrDefault(x => x.DeviceIp == devIp);
                    if (m == null)
                    {
                        m = new ZoneDeviceMapping { DeviceIp = devIp };
                        mappings.Add(m);
                    }
                    m.Side = (ZoneSide)zoneComboDevice.SelectedIndex;
                    _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                    QueueSave();
                };
                devRow.Children.Add(zoneComboDevice);

                stack.Children.Add(devRow);
            }
        }

        DevicePanel.Children.Add(card);
    }

    private void UpdateDreamZonePreview((byte R, byte G, byte B)[] zones)
    {
        for (int i = 0; i < _dreamZoneSwatches.Count && i < zones.Length; i++)
        {
            _dreamZoneSwatches[i].Background = new SolidColorBrush(
                Color.FromRgb(zones[i].R, zones[i].G, zones[i].B));
        }
    }

    private void SetDevicePanelDimmed(bool dreamActive)
    {
        if (dreamActive)
        {
            if (_dreamActiveBanner == null)
            {
                var accent = ThemeManager.Accent;
                _dreamActiveBanner = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x15, accent.R, accent.G, accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 12),
                    Child = new TextBlock
                    {
                        Text = "Screen Sync is active — disable DreamView to use scenes and colors.",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
                        TextWrapping = TextWrapping.Wrap,
                    }
                };
                DevicePanel.Children.Insert(0, _dreamActiveBanner);
            }
            foreach (UIElement child in DevicePanel.Children)
            {
                if (child == _dreamActiveBanner || child == _dreamViewCard)
                    continue;
                child.Opacity = 0.3;
                child.IsEnabled = false;
            }
        }
        else
        {
            if (_dreamActiveBanner != null)
            {
                DevicePanel.Children.Remove(_dreamActiveBanner);
                _dreamActiveBanner = null;
            }
            foreach (UIElement child in DevicePanel.Children)
            {
                child.Opacity = 1.0;
                child.IsEnabled = true;
            }
        }
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
