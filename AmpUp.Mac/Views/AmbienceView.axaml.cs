using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AmpUp.Core.Models;
using AmpUp.Core.Services;

namespace AmpUp.Mac.Views;

public partial class AmbienceView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    // Cloud API
    private GoveeCloudApi? _cloudApi;
    private List<GoveeDeviceInfo> _cloudDevices = new();

    // Per-device UI references for live updates
    private readonly Dictionary<string, (CheckBox onOff, Slider brightness)> _deviceControls = new();

    // DreamView zone preview swatches
    private readonly List<Border> _dreamZoneSwatches = new();

    public Action? NavigateToSettings { get; set; }

    public AmbienceView()
    {
        InitializeComponent();
        TxtSettingsLink.PointerPressed += (_, _) => NavigateToSettings?.Invoke();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;
        _loading = false;

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

    // ── Status Bar ────────────────────────────────────────────────

    private void UpdateStatus()
    {
        if (_config == null) return;
        bool cloudEnabled = _config.Ambience.GoveeCloudEnabled;
        bool hasKey = !string.IsNullOrEmpty(_config.Ambience.GoveeApiKey);

        if (!cloudEnabled)
        {
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#555555"));
            TxtStatus.Text = "Govee disabled — enable in Settings";
        }
        else if (cloudEnabled && hasKey && _cloudDevices.Count > 0)
        {
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#00E676"));
            TxtStatus.Text = $"Connected — {_cloudDevices.Count} device(s)";
        }
        else if (cloudEnabled && hasKey)
        {
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#FFB800"));
            TxtStatus.Text = "Loading devices...";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#FFB800"));
            TxtStatus.Text = "No API key — configure in Settings";
        }
    }

    // ── Cloud Fetch ───────────────────────────────────────────────

    private async Task FetchCloudDevicesAndRebuild()
    {
        if (_cloudApi == null) return;
        try
        {
            var devices = await _cloudApi.GetDevicesAsync();
            if (devices != null)
            {
                _cloudDevices = devices;
                RebuildDevicePanel();
            }
        }
        catch (Exception ex)
        {
            DevicePanel.Children.Clear();
            DevicePanel.Children.Add(MakeErrorText($"Failed to load devices: {ex.Message}"));
        }
    }

    // ── Device Panel ──────────────────────────────────────────────

    private void RebuildDevicePanel()
    {
        DevicePanel.Children.Clear();
        _deviceControls.Clear();
        UpdateStatus();

        if (_config == null) return;

        if (!_config.Ambience.GoveeCloudEnabled)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "Govee is disabled",
                "Enable Govee Cloud API in Settings to control your lights."));
            AppendDreamViewPlaceholder();
            return;
        }

        if (_cloudDevices.Count == 0)
        {
            DevicePanel.Children.Add(MakeSetupCard(
                "No devices found",
                "Check your API key in Settings, or wait for devices to load."));
            AppendDreamViewPlaceholder();
            return;
        }

        // Build device cards
        foreach (var device in _cloudDevices)
        {
            DevicePanel.Children.Add(BuildDeviceCard(device));
        }

        AppendDreamViewPlaceholder();
    }

    // ── Device Card ───────────────────────────────────────────────

    private Border BuildDeviceCard(GoveeDeviceInfo device)
    {
        var card = new Border
        {
            Classes = { "CardPanel" },
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#00E676")),
        };

        var stack = new StackPanel();
        card.Child = stack;

        // Header
        var deviceLabel = !string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceName : "Govee Device";
        var headerText = !string.IsNullOrEmpty(device.Sku) ? $"{deviceLabel}  ({device.Sku})" : deviceLabel;
        stack.Children.Add(MakeSectionHeader(headerText));

        // Controls row: Power + Brightness
        var controlsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

        var onOffCheck = new CheckBox
        {
            Content = "On",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
        };
        onOffCheck.IsCheckedChanged += async (_, _) =>
        {
            if (_loading || _cloudApi == null) return;
            bool turnOn = onOffCheck.IsChecked == true;
            await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                device.Device, device.Sku, GoveeCloudApi.TurnOnOff(turnOn)));
        };
        controlsRow.Children.Add(onOffCheck);

        controlsRow.Children.Add(MakeSubLabel("BRIGHTNESS"));
        var brightnessSlider = new Slider
        {
            Minimum = 1, Maximum = 100, Value = 100,
            Width = 180, Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
        };
        brightnessSlider.PropertyChanged += async (_, e) =>
        {
            if (e.Property.Name != "Value" || _loading || _cloudApi == null) return;
            await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                device.Device, device.Sku, GoveeCloudApi.SetBrightness((int)brightnessSlider.Value)));
        };
        controlsRow.Children.Add(brightnessSlider);

        stack.Children.Add(controlsRow);

        // Store for live updates
        if (!string.IsNullOrEmpty(device.Device))
            _deviceControls[device.Device] = (onOffCheck, brightnessSlider);

        // Colors section
        stack.Children.Add(MakeSeparator());
        stack.Children.Add(MakeSectionHeader("COLORS"));
        var scenesWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(scenesWrap);

        // Load scenes async
        if (_cloudApi != null)
            _ = LoadScenesAsync(device, scenesWrap, onOffCheck);

        // Music mode placeholder
        bool hasMusic = device.Capabilities?.Contains("devices.capabilities.music_setting") == true;
        if (hasMusic)
        {
            stack.Children.Add(MakeSeparator());
            stack.Children.Add(MakeSectionHeader("MUSIC MODE"));
            BuildMusicSection(device, stack);
        }

        return card;
    }

    // ── Scenes ────────────────────────────────────────────────────

    private async Task LoadScenesAsync(GoveeDeviceInfo device, WrapPanel wrap, CheckBox powerCheck)
    {
        if (_cloudApi == null) return;
        try
        {
            var scenes = GoveeCloudApi.ExtractScenesFromCapabilities(device.RawCapabilities, "dynamic");
            if (scenes.Count == 0)
                scenes = await _cloudApi.GetDynamicScenesAsync(device.Device, device.Sku) ?? new();

            string? activeId = null;
            foreach (var scene in scenes)
            {
                var tileColor = GetSceneTileColor(scene.Name);
                var tile = MakeSceneTile(scene.Name, GetSceneIcon(scene.Name), tileColor);

                tile.PointerPressed += async (_, _) =>
                {
                    // Deselect all
                    foreach (var child in wrap.Children)
                        if (child is Border b)
                        {
                            b.Background = new SolidColorBrush(Color.Parse("#1A1A1A"));
                            b.BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
                        }

                    // Select
                    tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                    tile.BorderBrush = new SolidColorBrush(tileColor);
                    activeId = scene.Id;

                    var sceneValue = scene.RawValue ?? (object)new { id = scene.Id };
                    await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                        device.Device, device.Sku, GoveeCloudApi.SetScene(sceneValue)));

                    if (powerCheck.IsChecked != true)
                    {
                        _loading = true;
                        powerCheck.IsChecked = true;
                        _loading = false;
                    }
                };

                wrap.Children.Add(tile);
            }
        }
        catch { }
    }

    private Border MakeSceneTile(string name, string icon, Color tileColor)
    {
        var tile = new Border
        {
            Width = 82, Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#1A1A1A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = icon, FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(
                (byte)(tileColor.R * 0.7), (byte)(tileColor.G * 0.7), (byte)(tileColor.B * 0.7))),
        });
        content.Children.Add(new TextBlock
        {
            Text = name, FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            MaxWidth = 74,
            Margin = new Thickness(0, 2, 0, 0),
        });
        tile.Child = content;

        tile.PointerEntered += (_, _) =>
        {
            tile.Background = new SolidColorBrush(Color.Parse("#242424"));
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, tileColor.R, tileColor.G, tileColor.B));
        };
        tile.PointerExited += (_, _) =>
        {
            tile.Background = new SolidColorBrush(Color.Parse("#1A1A1A"));
            tile.BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
        };

        return tile;
    }

    // ── Music Mode ────────────────────────────────────────────────

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
        int sensitivity = 50;

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        foreach (var (modeId, modeName, icon, tileColor) in MusicModes)
        {
            var tile = MakeSceneTile(modeName, icon, tileColor);
            tile.PointerPressed += async (_, _) =>
            {
                foreach (var child in wrap.Children)
                    if (child is Border b)
                    {
                        b.Background = new SolidColorBrush(Color.Parse("#1A1A1A"));
                        b.BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
                    }

                tile.Background = new SolidColorBrush(Color.FromArgb(0x30, tileColor.R, tileColor.G, tileColor.B));
                tile.BorderBrush = new SolidColorBrush(tileColor);
                activeModeId = modeId;

                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(modeId, sensitivity)));
            };
            wrap.Children.Add(tile);
        }
        parent.Children.Add(wrap);

        // Sensitivity slider
        var sensRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        sensRow.Children.Add(MakeSubLabel("SENSITIVITY"));
        var sensSlider = new Slider
        {
            Minimum = 1, Maximum = 100, Value = 50,
            Width = 200, Height = 30,
        };
        sensSlider.PropertyChanged += async (_, e) =>
        {
            if (e.Property.Name != "Value") return;
            sensitivity = (int)sensSlider.Value;
            if (activeModeId.HasValue && _cloudApi != null)
                await SafeCloudCall(() => _cloudApi.ControlDeviceAsync(
                    device.Device, device.Sku, GoveeCloudApi.SetMusicMode(activeModeId.Value, sensitivity)));
        };
        sensRow.Children.Add(sensSlider);
        parent.Children.Add(sensRow);
    }

    // ── DreamView Placeholder ─────────────────────────────────────

    private void AppendDreamViewPlaceholder()
    {
        if (_config == null) return;

        var card = new Border
        {
            Classes = { "CardPanel" },
            Margin = new Thickness(0, 12, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;

        stack.Children.Add(MakeSectionHeader("DREAMVIEW — Screen Sync"));
        stack.Children.Add(new TextBlock
        {
            Text = "Screen capture sync is not yet available on macOS. This feature requires CGWindowList implementation.",
            Classes = { "SecondaryText" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Zone preview placeholder
        stack.Children.Add(MakeSubLabel("ZONE PREVIEW"));
        var previewPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        _dreamZoneSwatches.Clear();
        for (int i = 0; i < 8; i++)
        {
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#1C1C1C")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
            };
            _dreamZoneSwatches.Add(swatch);
            previewPanel.Children.Add(swatch);
        }
        stack.Children.Add(previewPanel);

        DevicePanel.Children.Add(card);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private Border MakeSetupCard(string title, string description)
    {
        var card = new Border
        {
            Classes = { "CardPanel" },
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")),
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Classes = { "SecondaryText" },
            TextWrapping = TextWrapping.Wrap,
        });
        return card;
    }

    private StackPanel MakeSectionHeader(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new Border
        {
            Width = 3, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse("#00E676")),
            Margin = new Thickness(0, 0, 10, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#00E676")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private TextBlock MakeSubLabel(string text) => new()
    {
        Text = text,
        FontSize = 9, FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#555555")),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
        Margin = new Thickness(0, 4, 0, 12),
    };

    private TextBlock MakeErrorText(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#FF4444")),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 8),
    };

    private static async Task SafeCloudCall(Func<Task> action)
    {
        try { await action(); }
        catch { }
    }

    // ── Scene Helpers ─────────────────────────────────────────────

    private static readonly Color[] ScenePalette =
    {
        Color.FromRgb(0xFF, 0x6B, 0x35), Color.FromRgb(0x64, 0xB5, 0xF6),
        Color.FromRgb(0xFF, 0x80, 0xAB), Color.FromRgb(0x69, 0xF0, 0xAE),
        Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xFF, 0xD7, 0x40),
        Color.FromRgb(0x4D, 0xD0, 0xE1), Color.FromRgb(0xFF, 0x52, 0x52),
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
        if (n.Contains("fire") || n.Contains("flame")) return "🔥";
        if (n.Contains("snow")) return "❄";
        if (n.Contains("rain")) return "🌧";
        if (n.Contains("aurora")) return "🌌";
        if (n.Contains("ocean")) return "🌊";
        if (n.Contains("rainbow")) return "🌈";
        if (n.Contains("party")) return "🎉";
        if (n.Contains("romantic")) return "💕";
        if (n.Contains("candle")) return "🕯";
        if (n.Contains("breathe")) return "🫧";
        if (n.Contains("sleep")) return "😴";
        if (n.Contains("energic")) return "⚡";
        return "◆";
    }

    /// <summary>Update device brightness from knob events.</summary>
    public void UpdateDeviceBrightness(string deviceId, float normalized, bool poweredOn)
    {
        if (!_deviceControls.TryGetValue(deviceId, out var controls)) return;
        _loading = true;
        controls.onOff.IsChecked = poweredOn;
        controls.brightness.Value = Math.Max(1, (int)Math.Round(normalized * 100));
        _loading = false;
    }
}
