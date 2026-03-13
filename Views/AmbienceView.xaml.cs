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
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Cloud API
    private GoveeCloudApi? _cloudApi;
    private List<GoveeDeviceInfo> _cloudDevices = new();

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

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

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;
        _loading = false;

        // Initialize cloud API if key is configured
        if (!string.IsNullOrEmpty(config.Ambience.GoveeApiKey))
        {
            _cloudApi?.Dispose();
            _cloudApi = new GoveeCloudApi(config.Ambience.GoveeApiKey);
            _ = FetchCloudDevicesAndRebuild();
        }
        else
        {
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
        bool enabled = _config.Ambience.GoveeEnabled;
        bool hasKey = !string.IsNullOrEmpty(_config.Ambience.GoveeApiKey);
        int devices = _cloudDevices.Count;

        if (!enabled)
        {
            dot.Background = Brush("#555555");
            label.Text = "Govee disabled — enable in Settings";
        }
        else if (!hasKey)
        {
            dot.Background = Brush("#FFB800");
            label.Text = "No API key — add in Settings for full control";
        }
        else if (devices > 0)
        {
            dot.Background = Brush("#00E676");
            label.Text = $"Connected — {devices} device(s)";
        }
        else
        {
            dot.Background = Brush("#FFB800");
            label.Text = "API key set — loading devices...";
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

        if (!_config.Ambience.GoveeEnabled)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "Govee is disabled",
                "Enable Govee LAN sync in Settings to get started.",
                "Open Settings", () => NavigateToSettings?.Invoke()));
            return;
        }

        if (!hasKey && _config.Ambience.GoveeDevices.Count == 0)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "No devices found",
                "Scan for devices in Settings, or add a Cloud API key for scenes and advanced control.\n\nGet your key at developer.govee.com",
                "Open Settings", () => NavigateToSettings?.Invoke()));
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
                Text = "Linked to Lights",
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

            if (hasKey)
            {
                // Has API key but cloud fetch returned empty — offer refresh
                DevicePanel.Children.Add(MakeSetupCard(
                    "Cloud devices not loaded",
                    "Couldn't fetch device list from Govee Cloud. LAN controls are available above.",
                    "Retry", () => _ = FetchCloudDevicesAndRebuild()));
            }
            else
            {
                // LAN-only hint
                DevicePanel.Children.Add(MakeSetupCard(
                    "Want scenes and more?",
                    "Add a Cloud API key in Settings for scenes, segments, and music mode.",
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

        // Color swatch — use LAN if available
        var colorSwatch = MakeColorSwatch(Colors.White, (r, g, b) =>
        {
            if (lanIp != null)
            {
                AmbienceSync.PauseSync(lanIp, 30);
                _ = AmbienceSync.SendColorAsync(lanIp, r, g, b);
            }
            else if (_cloudApi != null)
                _ = SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetColor(r, g, b)));
        });
        controlsRow.Children.Add(colorSwatch);

        stack.Children.Add(controlsRow);

        // ── Scenes section ──
        bool hasScenes = device.Capabilities?.Contains("devices.capabilities.dynamic_scene") == true;
        if (hasScenes)
        {
            stack.Children.Add(MakeSeparator());
            var (scBar, scLabel) = MakeSectionHeader("SCENES");
            stack.Children.Add(WrapHeader(scBar, scLabel));
            var sceneContainer = new StackPanel();
            stack.Children.Add(sceneContainer);
            BuildScenesSection(device, sceneContainer);
        }

        // ── Segments section ──
        bool hasSegments = device.Capabilities?.Contains("devices.capabilities.segment_color_setting") == true;
        if (hasSegments)
        {
            stack.Children.Add(MakeSeparator());
            var (segBar, segLabel) = MakeSectionHeader("SEGMENTS");
            stack.Children.Add(WrapHeader(segBar, segLabel));
            BuildSegmentsSection(device, stack);
        }

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

    // ── Scenes ────────────────────────────────────────────────────────

    private void BuildScenesSection(GoveeDeviceInfo device, StackPanel container)
    {
        // Extract scenes from raw capabilities (no extra API call)
        var scenes = GoveeCloudApi.ExtractScenesFromCapabilities(device.RawCapabilities, "dynamic");

        if (scenes.Count == 0)
        {
            // Try fetching via API as fallback
            var loadingText = new TextBlock
            {
                Text = "Loading scenes...",
                Style = FindStyle("SecondaryText"),
                FontSize = 11,
            };
            container.Children.Add(loadingText);
            _ = FetchScenesAsync(device, container, loadingText);
            return;
        }

        RenderSceneTiles(container, device, scenes);
    }

    private async Task FetchScenesAsync(GoveeDeviceInfo device, StackPanel container, TextBlock loadingText)
    {
        try
        {
            var scenes = await _cloudApi!.GetDynamicScenesAsync(device.Device, device.Sku);
            Dispatcher.Invoke(() =>
            {
                container.Children.Remove(loadingText);
                if (scenes == null || scenes.Count == 0)
                {
                    container.Children.Add(new TextBlock
                    {
                        Text = "No scenes available for this device",
                        Style = FindStyle("SecondaryText"),
                        FontSize = 11,
                        Foreground = FindBrush("TextDimBrush"),
                    });
                    return;
                }
                RenderSceneTiles(container, device, scenes);
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[Ambience] Scene fetch error: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                loadingText.Text = "Failed to load scenes";
                loadingText.Foreground = FindBrush("DangerRedBrush");
            });
        }
    }

    private void RenderSceneTiles(StackPanel container, GoveeDeviceInfo device, List<GoveeScene> scenes)
    {
        string? activeSceneId = null;

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        // Assign a color to each scene based on its name hash for visual variety
        foreach (var scene in scenes)
        {
            var sceneId = scene.Id;
            var tileColor = GetSceneTileColor(scene.Name);

            var tile = new Border
            {
                Width = 110, Height = 42,
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
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Color accent dot
            tileContent.Children.Add(new Border
            {
                Width = 6, Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(tileColor),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var label = new TextBlock
            {
                Text = scene.Name,
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 86,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tileContent.Children.Add(label);
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
            };

            wrap.Children.Add(tile);
        }

        container.Children.Add(wrap);
    }

    // ── Segments ──────────────────────────────────────────────────────

    private void BuildSegmentsSection(GoveeDeviceInfo device, StackPanel parent)
    {
        int segmentCount = GoveeCloudApi.ExtractSegmentCount(device.RawCapabilities);
        if (segmentCount <= 0) segmentCount = 10; // safe default

        var segColors = new (byte R, byte G, byte B)[segmentCount];
        for (int i = 0; i < segmentCount; i++) segColors[i] = (255, 255, 255);

        var segRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        for (int i = 0; i < segmentCount; i++)
        {
            var idx = i;
            var segBorder = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                ToolTip = $"Segment {idx + 1}",
            };

            // Hover glow
            segBorder.MouseEnter += (_, _) =>
            {
                segBorder.BorderBrush = FindBrush("AccentBrush");
            };
            segBorder.MouseLeave += (_, _) =>
            {
                segBorder.BorderBrush = FindBrush("CardBorderBrush");
            };

            segBorder.MouseLeftButtonUp += (_, _) =>
            {
                var (r, g, b) = segColors[idx];
                var current = Color.FromRgb(r, g, b);
                var dialog = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true)
                {
                    var c = dialog.SelectedColor;
                    segColors[idx] = (c.R, c.G, c.B);
                    segBorder.Background = new SolidColorBrush(c);
                }
            };

            segRow.Children.Add(segBorder);
        }

        parent.Children.Add(segRow);

        // Apply button
        var applyBtn = new Button
        {
            Content = "Apply Segments",
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        applyBtn.Click += async (_, _) =>
        {
            for (int si = 0; si < segColors.Length; si++)
            {
                var (sr, sg, sb) = segColors[si];
                int capturedIdx = si;
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku,
                    GoveeCloudApi.SetSegmentColor(capturedIdx, sr, sg, sb)));
            }
        };
        parent.Children.Add(applyBtn);
    }

    // ── Music Mode ────────────────────────────────────────────────────

    private void BuildMusicSection(GoveeDeviceInfo device, StackPanel parent)
    {
        var musicToggle = new CheckBox
        {
            Content = "Enable Music Mode",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10),
        };
        musicToggle.Checked += async (_, _) =>
            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                device.Device, device.Sku, GoveeCloudApi.SetMusicMode(1, 50)));
        musicToggle.Unchecked += async (_, _) =>
            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                device.Device, device.Sku, GoveeCloudApi.SetMusicMode(0, 0)));
        parent.Children.Add(musicToggle);

        // Sensitivity slider
        parent.Children.Add(MakeSubLabel("SENSITIVITY"));
        var sensSlider = new StyledSlider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 40,
            Suffix = "",
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var sensDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        sensDebounce.Tick += async (_, _) =>
        {
            sensDebounce.Stop();
            if (musicToggle.IsChecked == true)
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(1, (int)sensSlider.Value)));
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            sensDebounce.Stop();
            sensDebounce.Start();
        };
        parent.Children.Add(sensSlider);
    }

    // ── Color swatch helper ───────────────────────────────────────────

    private Border MakeColorSwatch(Color initial, Action<byte, byte, byte> onColorSelected)
    {
        var swatch = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(initial),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Click to set color",
        };
        swatch.MouseEnter += (_, _) => swatch.BorderBrush = FindBrush("AccentBrush");
        swatch.MouseLeave += (_, _) => swatch.BorderBrush = FindBrush("CardBorderBrush");

        swatch.MouseLeftButtonUp += (_, _) =>
        {
            var current = (swatch.Background as SolidColorBrush)?.Color ?? Colors.White;
            var dialog = new ColorPickerDialog(current) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                var c = dialog.SelectedColor;
                swatch.Background = new SolidColorBrush(c);
                onColorSelected(c.R, c.G, c.B);
            }
        };

        return swatch;
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
