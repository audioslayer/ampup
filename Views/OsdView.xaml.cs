using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AmpUp.Controls;
using AmpUp.Core.Services;
using Material.Icons;

namespace AmpUp.Views;

public partial class OsdView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;
    private bool _configLoaded;
    private bool _displaySettingsSubscribed;

    /// <summary>Set by MainWindow — called when Quick Wheel changes require a full view refresh.</summary>
    public Action? OnRequestRefresh;

    // ── Controls built in code (BuildUi) ──────────────────────────────
    private bool _showVolume, _showProfile, _showDevice, _hideInFullscreen;
    private Action<bool> _setVolumeSwitch = _ => { }, _setProfileSwitch = _ => { },
                         _setDeviceSwitch = _ => { }, _setFullscreenSwitch = _ => { };
    private FrameworkElement _volumeDurRow = null!, _profileDurRow = null!, _deviceDurRow = null!;
    private StyledSlider SldOsdVolumeDur = null!, SldOsdProfileDur = null!, SldOsdDeviceDur = null!, SldOsdWheelDur = null!;
    private TextBlock LblOsdVolumeDur = null!, LblOsdProfileDur = null!, LblOsdDeviceDur = null!, LblOsdWheelDur = null!;
    private ListPicker CmbOsdMonitor = null!;
    private readonly Dictionary<OsdPosition, System.Windows.Controls.Border> _posCells = new();
    private OsdPosition _currentPosition = OsdPosition.BottomRight;
    private StackPanel WheelRowsPanel = null!;
    private readonly List<Action> _accentRepaints = new();

    /// <summary>Per-wheel UI state, stored on each wheel card's Tag.</summary>
    private sealed class WheelRowState
    {
        public bool Enabled;
        public int ModeIdx;
        public ComboBox TriggerCombo = null!;
        public StackPanel CustomPanel = null!;
        public TextBlock Title = null!;
        public TextBlock Subtitle = null!;
    }

    public OsdView()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); CollectAndSave(); };

        BuildUi();

        // Duration sliders: 0.1s steps, accent-colored, value in separate label
        var sliderLabels = new[] {
            (SldOsdVolumeDur, LblOsdVolumeDur),
            (SldOsdProfileDur, LblOsdProfileDur),
            (SldOsdDeviceDur, LblOsdDeviceDur),
            (SldOsdWheelDur, LblOsdWheelDur),
        };
        foreach (var (sld, lbl) in sliderLabels)
        {
            sld.Step = 0.1;
            sld.AccentColor = ThemeManager.Accent;
            sld.ValueChanged += (s, _) =>
            {
                lbl.Text = sld.Value < 0.05 ? "Off" : $"{sld.Value:F1}s";
                OnValueChanged(s!, EventArgs.Empty);
            };
        }
        // Update slider / switch / pill accents when theme changes
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(() =>
        {
            foreach (var (sld, lbl) in sliderLabels)
            {
                sld.AccentColor = ThemeManager.Accent;
                lbl.Foreground = new SolidColorBrush(ThemeManager.Accent);
            }
            CmbOsdMonitor.AccentColor = ThemeManager.Accent;
            CmbOsdMonitor.RefreshAccent();
            foreach (var r in _accentRepaints.ToArray()) r();
            HighlightOsdPosition(_currentPosition);
        });
        CmbOsdMonitor.SelectionChanged += CmbOsdMonitor_SelectionChanged;

        // Refresh monitor list when display config changes (monitors added/removed)
        Loaded += (_, _) => SubscribeDisplaySettingsChanged();
        IsVisibleChanged += OnVisibilityChanged;
        Unloaded += (_, _) => UnsubscribeDisplaySettingsChanged();
    }

    // ══════════════════ UI construction ══════════════════

    private Brush FindBrush(string key) => (Brush)FindResource(key);

    private void QueueSave()
    {
        if (_loading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void BuildUi()
    {
        StyledSlider NewDurSlider(double min, double max, double value, string tip) => new()
        {
            Minimum = min, Maximum = max, Value = value, ShowLabel = false, Height = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = tip,
        };
        TextBlock NewValLabel(string text) => new()
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, MinWidth = 34,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
        };

        RootPanel.Children.Add(UiKit.PageHeader("On-Screen Display",
            "Overlays that appear when you turn a knob, switch profiles or change audio devices, plus Quick Wheel radial menus."));

        // ══════════════════ POPUPS ══════════════════
        var popupsBody = new StackPanel();

        SldOsdVolumeDur = NewDurSlider(0.3, 8, 2, "How long the volume overlay stays visible");
        LblOsdVolumeDur = NewValLabel("2.0s");
        SldOsdProfileDur = NewDurSlider(0.3, 8, 3.5, "How long the profile overlay stays visible");
        LblOsdProfileDur = NewValLabel("3.5s");
        SldOsdDeviceDur = NewDurSlider(0.3, 8, 2.5, "How long the device overlay stays visible");
        LblOsdDeviceDur = NewValLabel("2.5s");

        _volumeDurRow = MakeDurationRow(SldOsdVolumeDur, LblOsdVolumeDur);
        _profileDurRow = MakeDurationRow(SldOsdProfileDur, LblOsdProfileDur);
        _deviceDurRow = MakeDurationRow(SldOsdDeviceDur, LblOsdDeviceDur);

        var volSw = MakeSwitch(false, on => { _showVolume = on; _volumeDurRow.Visibility = Vis(on); QueueSave(); },
            out _setVolumeSwitch, "Show overlay when you adjust a knob");
        var profSw = MakeSwitch(false, on => { _showProfile = on; _profileDurRow.Visibility = Vis(on); QueueSave(); },
            out _setProfileSwitch, "Show overlay when switching profiles");
        var devSw = MakeSwitch(false, on => { _showDevice = on; _deviceDurRow.Visibility = Vis(on); QueueSave(); },
            out _setDeviceSwitch, "Show overlay when cycling audio devices");
        var fsSw = MakeSwitch(false, on => { _hideInFullscreen = on; QueueSave(); },
            out _setFullscreenSwitch, "When on, OSD won't appear over fullscreen games or apps");

        popupsBody.Children.Add(MakeSettingRow("Volume changes", "Shown when you turn a knob", volSw));
        popupsBody.Children.Add(_volumeDurRow);
        popupsBody.Children.Add(MakeSettingRow("Profile switch", "Shown when the active profile changes", profSw));
        popupsBody.Children.Add(_profileDurRow);
        popupsBody.Children.Add(MakeSettingRow("Device switch", "Shown when cycling audio devices", devSw));
        popupsBody.Children.Add(_deviceDurRow);
        popupsBody.Children.Add(MakeDivider());
        var fsRow = MakeSettingRow("Hide in fullscreen games", "Keep overlays off fullscreen apps and games", fsSw);
        fsRow.Margin = new Thickness(0);
        popupsBody.Children.Add(fsRow);
        var popupsCard = UiKit.Card(Color.FromRgb(0x42, 0xA5, 0xF5), MaterialIconKind.BellOutline,
            "Popups", "Shown while AmpUp is minimized to the tray", null, popupsBody).Root;

        // ══════════════════ PLACEMENT ══════════════════
        var placeBody = new StackPanel();
        CmbOsdMonitor = new ListPicker { MinWidth = 180, MaxWidth = 280, ToolTip = "Which monitor to show the OSD on" };
        placeBody.Children.Add(MakeSettingRow("Monitor", "Screen the overlay appears on", CmbOsdMonitor));

        placeBody.Children.Add(MakeLabel("Position"));
        placeBody.Children.Add(BuildPositionPicker());
        placeBody.Children.Add(MakeHint(
            "Windows shows its own volume popup at bottom center — avoid it to keep AmpUp's visible.",
            new Thickness(0, 8, 0, 0)));

        var previewBtn = UiKit.IconButton(MaterialIconKind.PlayCircleOutline,
            "Show a sample volume overlay at the chosen monitor and position",
            _ => OnOsdPreview(this, new RoutedEventArgs()));
        var placeActions = new StackPanel { Orientation = Orientation.Horizontal };
        var previewCap = new TextBlock { Text = "Preview", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand };
        previewCap.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        previewCap.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnOsdPreview(this, new RoutedEventArgs()); };
        placeActions.Children.Add(previewCap);
        placeActions.Children.Add(previewBtn);
        var placeCard = UiKit.Card(Color.FromRgb(0xAB, 0x47, 0xBC), MaterialIconKind.Monitor,
            "Placement", "Where overlays appear on screen", placeActions, placeBody).Root;

        RootPanel.Children.Add(UiKit.SectionHeader("OVERLAYS", "3 types"));
        // One column — side-by-side cards read as confusing on this tab.
        RootPanel.Children.Add(popupsCard);
        placeCard.Margin = new Thickness(placeCard.Margin.Left, Math.Max(placeCard.Margin.Top, 12), placeCard.Margin.Right, placeCard.Margin.Bottom);
        RootPanel.Children.Add(placeCard);

        // ══════════════════ QUICK WHEELS ══════════════════
        RootPanel.Children.Add(UiKit.SectionHeader("QUICK WHEELS", "", out _wheelCountText));

        var behaviourBody = new StackPanel();
        SldOsdWheelDur = NewDurSlider(0, 15, 0,
            "Delay before Quick Wheel closes after releasing the trigger button. Off = confirm immediately on release.");
        LblOsdWheelDur = NewValLabel("Off");
        var wheelDurBlock = new StackPanel { ToolTip = SldOsdWheelDur.ToolTip };
        var wheelDurHead = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(LblOsdWheelDur, Dock.Right);
        wheelDurHead.Children.Add(LblOsdWheelDur);
        var wdLeft = new StackPanel();
        wdLeft.Children.Add(MakeLabel("Auto-dismiss delay", new Thickness(0)));
        wdLeft.Children.Add(MakeHint("Off = confirm immediately when the trigger is released", new Thickness(0, 2, 0, 0)));
        wheelDurHead.Children.Add(wdLeft);
        wheelDurBlock.Children.Add(wheelDurHead);
        wheelDurBlock.Children.Add(SldOsdWheelDur);
        behaviourBody.Children.Add(wheelDurBlock);
        RootPanel.Children.Add(UiKit.Card(Color.FromRgb(0x26, 0xC6, 0xDA), MaterialIconKind.GestureTapHold,
            "How wheels work",
            "Hold a button to open a radial overlay. Turn any knob to navigate, release to confirm.",
            null, behaviourBody).Root);

        WheelRowsPanel = new StackPanel();
        RootPanel.Children.Add(WheelRowsPanel);

        var addWheel = UiKit.LinkRow(MaterialIconKind.Plus, "Add Quick Wheel", accent: true,
            () => AddWheelRow(new QuickWheelConfig { Enabled = true }), "Add another Quick Wheel binding");
        addWheel.HorizontalAlignment = HorizontalAlignment.Left;
        RootPanel.Children.Add(addWheel);
    }

    private TextBlock _wheelCountText = null!;

    private FrameworkElement MakeDurationRow(StyledSlider slider, TextBlock valLabel)
    {
        var g = new Grid { Margin = new Thickness(0, -4, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cap = new TextBlock { Text = "Duration", FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0) };
        cap.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        g.Children.Add(cap);
        Grid.SetColumn(slider, 1);
        g.Children.Add(slider);
        valLabel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(valLabel, 2);
        g.Children.Add(valLabel);
        return g;
    }

    private Grid MakeSettingRow(string label, string? hint, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(MakeLabel(label, new Thickness(0)));
        if (hint != null) left.Children.Add(MakeHint(hint, new Thickness(0, 2, 0, 0)));
        g.Children.Add(left);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private TextBlock MakeLabel(string text, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = margin ?? new Thickness(0, 0, 0, 6) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return tb;
    }

    private TextBlock MakeHint(string text, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0, 0, 0, 14) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        return tb;
    }

    private System.Windows.Controls.Border MakeDivider()
    {
        var b = new System.Windows.Controls.Border { Height = 1, Margin = new Thickness(0, 2, 0, 14) };
        b.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBorderBrush");
        return b;
    }

    /// <summary>Compact accent-aware on/off switch (same look as the Room tab).</summary>
    private System.Windows.Controls.Border MakeSwitch(bool initial, Action<bool> onChanged, out Action<bool> setState, string? tooltip = null)
    {
        bool on = initial;
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 14, Height = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var track = new System.Windows.Controls.Border
        {
            Width = 38, Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Cursor = Cursors.Hand,
            Child = knob,
            ToolTip = tooltip,
        };
        void Paint()
        {
            var a = ThemeManager.Accent;
            track.Background = on ? new SolidColorBrush(Color.FromArgb(0x55, a.R, a.G, a.B)) : FindBrush("InputBgBrush");
            track.BorderBrush = on ? new SolidColorBrush(a) : FindBrush("InputBorderBrush");
            knob.Fill = on ? new SolidColorBrush(a) : FindBrush("TextDimBrush");
            knob.Margin = new Thickness(on ? 16 : 0, 0, 0, 0);
        }
        track.MouseLeftButtonDown += (_, _) => { on = !on; Paint(); onChanged(on); };
        setState = v => { on = v; Paint(); };
        _accentRepaints.Add(Paint);
        Paint();
        return track;
    }

    /// <summary>Pill-style single-select row (matches the Room tab pills).</summary>
    private WrapPanel MakePillRow(string[] labels, int activeIdx, Action<int> onSelect)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var pills = new System.Windows.Controls.Border[labels.Length];
        int current = activeIdx;
        void Paint()
        {
            var accent = ThemeManager.Accent;
            for (int i = 0; i < pills.Length; i++)
            {
                bool active = i == current;
                pills[i].Background = active ? new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)) : FindBrush("InputBgBrush");
                pills[i].BorderBrush = active ? new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)) : FindBrush("InputBorderBrush");
                if (pills[i].Child is TextBlock tb)
                    tb.Foreground = active ? new SolidColorBrush(accent) : FindBrush("TextSecBrush");
            }
        }
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var pill = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 6, 6),
                MinWidth = 48,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = labels[i], FontSize = 10, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center },
            };
            pill.MouseLeftButtonDown += (_, _) =>
            {
                if (current == idx) return;
                current = idx;
                Paint();
                onSelect(idx);
            };
            pills[i] = pill;
            row.Children.Add(pill);
        }
        _accentRepaints.Add(Paint);
        Paint();
        return row;
    }

    /// <summary>Mini-monitor 3x2 grid picker for OSD position.</summary>
    private FrameworkElement BuildPositionPicker()
    {
        var grid = new Grid();
        for (int r = 0; r < 2; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cells = new (OsdPosition Pos, string Label, int Row, int Col)[]
        {
            (OsdPosition.TopLeft, "Top left", 0, 0),
            (OsdPosition.TopCenter, "Top center", 0, 1),
            (OsdPosition.TopRight, "Top right", 0, 2),
            (OsdPosition.BottomLeft, "Bottom left", 1, 0),
            (OsdPosition.BottomCenter, "Bottom center", 1, 1),
            (OsdPosition.BottomRight, "Bottom right", 1, 2),
        };
        foreach (var (pos, label, row, col) in cells)
        {
            var cell = new System.Windows.Controls.Border
            {
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = pos.ToString(),
                ToolTip = label,
                Child = new TextBlock
                {
                    Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                },
            };
            cell.MouseLeftButtonDown += OsdPosition_Click;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
            _posCells[pos] = cell;
        }

        // Monitor bezel + stand
        var screen = new System.Windows.Controls.Border
        {
            Height = 120,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(4),
            Child = grid,
        };
        screen.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        screen.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        var stand = new System.Windows.Controls.Border { Width = 40, Height = 6, CornerRadius = new CornerRadius(0, 0, 3, 3),
            HorizontalAlignment = HorizontalAlignment.Center };
        stand.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBorderBrush");

        var stack = new StackPanel();
        stack.Children.Add(screen);
        stack.Children.Add(stand);
        // Fill the card up to 340px, left-aligned, shrinking on narrow widths
        var wrap = new Grid();
        wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
        wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        wrap.Children.Add(stack);
        return wrap;
    }

    private void SubscribeDisplaySettingsChanged()
    {
        if (_displaySettingsSubscribed) return;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _displaySettingsSubscribed = true;
    }

    private void UnsubscribeDisplaySettingsChanged()
    {
        if (!_displaySettingsSubscribed) return;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _displaySettingsSubscribed = false;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_config == null) return;
            _loading = true;
            var resolvedIndex = DisplayMonitorResolver.ResolveOsdMonitorIndex(_config.Osd);
            _config.Osd.MonitorIndex = resolvedIndex;
            PopulateOsdMonitorPicker(resolvedIndex);
            _loading = false;
        });
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _config != null)
        {
            _loading = true;
            var resolvedIndex = DisplayMonitorResolver.ResolveOsdMonitorIndex(_config.Osd);
            _config.Osd.MonitorIndex = resolvedIndex;
            PopulateOsdMonitorPicker(resolvedIndex);
            _loading = false;
        }
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // OSD
        _showVolume = config.Osd.ShowVolume;
        _showProfile = config.Osd.ShowProfileSwitch;
        _showDevice = config.Osd.ShowDeviceSwitch;
        _hideInFullscreen = config.Osd.HideInFullscreen;
        _setVolumeSwitch(_showVolume);
        _setProfileSwitch(_showProfile);
        _setDeviceSwitch(_showDevice);
        _setFullscreenSwitch(_hideInFullscreen);
        _volumeDurRow.Visibility = Vis(_showVolume);
        _profileDurRow.Visibility = Vis(_showProfile);
        _deviceDurRow.Visibility = Vis(_showDevice);
        SldOsdVolumeDur.Value = config.Osd.VolumeDuration;
        LblOsdVolumeDur.Text = $"{config.Osd.VolumeDuration:F1}s";
        SldOsdProfileDur.Value = config.Osd.ProfileDuration;
        LblOsdProfileDur.Text = $"{config.Osd.ProfileDuration:F1}s";
        SldOsdDeviceDur.Value = config.Osd.DeviceDuration;
        LblOsdDeviceDur.Text = $"{config.Osd.DeviceDuration:F1}s";
        SldOsdWheelDur.Value = config.Osd.WheelDuration;
        LblOsdWheelDur.Text = config.Osd.WheelDuration < 0.05 ? "Off" : $"{config.Osd.WheelDuration:F1}s";
        HighlightOsdPosition(config.Osd.Position);
        var resolvedMonitorIndex = DisplayMonitorResolver.ResolveOsdMonitorIndex(config.Osd);
        config.Osd.MonitorIndex = resolvedMonitorIndex;
        PopulateOsdMonitorPicker(resolvedMonitorIndex);

        // Quick wheels
        WheelRowsPanel.Children.Clear();
        foreach (var qw in config.Osd.QuickWheels)
            AddWheelRow(qw);

        _loading = false;
        _configLoaded = true;
    }

    private void OnValueChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || !_configLoaded) return;

        _config.Osd.ShowVolume = _showVolume;
        _config.Osd.ShowProfileSwitch = _showProfile;
        _config.Osd.ShowDeviceSwitch = _showDevice;
        _config.Osd.VolumeDuration = Math.Round(SldOsdVolumeDur.Value, 1);
        _config.Osd.ProfileDuration = Math.Round(SldOsdProfileDur.Value, 1);
        _config.Osd.DeviceDuration = Math.Round(SldOsdDeviceDur.Value, 1);
        _config.Osd.WheelDuration = Math.Round(SldOsdWheelDur.Value, 1);
        _config.Osd.HideInFullscreen = _hideInFullscreen;

        // Collect quick wheels from dynamic rows — key each trigger by
        // (Device, LocalIdx) so a Turn Up Button 0 and an SC Side Button 0
        // don't collide in the diff below.
        var oldBindings = CollectTriggerBindings(_config.Osd.QuickWheels);
        _config.Osd.QuickWheels = CollectWheelConfigs();
        var newBindings = CollectTriggerBindings(_config.Osd.QuickWheels);

        SyncWheelButtonActions(oldBindings, newBindings);

        _onSave(_config);

        if (!oldBindings.SetEquals(newBindings)) OnRequestRefresh?.Invoke();
    }

    private static HashSet<(QuickWheelDevice Device, int LocalIdx)> CollectTriggerBindings(List<QuickWheelConfig> wheels)
    {
        var set = new HashSet<(QuickWheelDevice, int)>();
        foreach (var w in wheels)
            if (w.Enabled) set.Add((w.Device, w.TriggerButton));
        return set;
    }

    // ── Quick Wheel dynamic rows ──────────────────────────────────────

    // Available actions for custom wheel slots
    private static readonly (string id, string label)[] CustomSlotActions =
    {
        ("media_play_pause", "Play / Pause"),
        ("media_next", "Next Track"),
        ("media_prev", "Previous Track"),
        ("mute_master", "Mute Master"),
        ("mute_mic", "Mute Mic"),
        ("mute_active_window", "Mute Active Window"),
        ("cycle_brightness", "Cycle LED Brightness"),
        ("power_sleep", "Sleep"),
        ("power_lock", "Lock"),
        ("power_off", "Shutdown"),
        ("power_restart", "Restart"),
        ("launch_exe", "Launch App"),
        ("macro", "Macro"),
    };

    /// <summary>
    /// Populate the trigger dropdown with inputs appropriate to the user's
    /// active hardware (mirrors the Buttons/Mixer tab surface gating via
    /// HardwareMode). Each item's Tag is a (QuickWheelDevice, int) tuple
    /// that CollectWheelConfigs reads back.
    /// </summary>
    private void PopulateTriggerCombo(ComboBox combo, QuickWheelDevice selectedDevice, int selectedIdx)
    {
        combo.Items.Clear();

        var mode = _config?.HardwareMode ?? HardwareMode.Auto;
        bool showTurnUp = mode != HardwareMode.StreamControllerOnly;
        bool showSc = mode != HardwareMode.TurnUpOnly;

        int selectIndex = 0;
        int itemCount = 0;

        if (showTurnUp)
        {
            for (int i = 0; i < 5; i++)
            {
                var item = new ComboBoxItem
                {
                    Content = $"Turn Up: Button {i + 1}",
                    Tag = (QuickWheelDevice.TurnUp, i),
                };
                combo.Items.Add(item);
                if (selectedDevice == QuickWheelDevice.TurnUp && selectedIdx == i)
                    selectIndex = itemCount;
                itemCount++;
            }
        }

        if (showSc)
        {
            for (int i = 0; i < 3; i++)
            {
                var item = new ComboBoxItem
                {
                    Content = $"SC: Side Button {i + 1}",
                    Tag = (QuickWheelDevice.ScSideButton, i),
                };
                combo.Items.Add(item);
                if (selectedDevice == QuickWheelDevice.ScSideButton && selectedIdx == i)
                    selectIndex = itemCount;
                itemCount++;
            }
            for (int i = 0; i < 3; i++)
            {
                var item = new ComboBoxItem
                {
                    Content = $"SC: Encoder {i + 1} Press",
                    Tag = (QuickWheelDevice.ScEncoderPress, i),
                };
                combo.Items.Add(item);
                if (selectedDevice == QuickWheelDevice.ScEncoderPress && selectedIdx == i)
                    selectIndex = itemCount;
                itemCount++;
            }
        }

        if (combo.Items.Count == 0)
        {
            // Shouldn't happen (HardwareMode guarantees at least one), but
            // keep the dropdown non-empty so layout doesn't collapse.
            combo.Items.Add(new ComboBoxItem
            {
                Content = "Turn Up: Button 1",
                Tag = (QuickWheelDevice.TurnUp, 0),
            });
        }

        combo.SelectedIndex = Math.Clamp(selectIndex, 0, combo.Items.Count - 1);
    }

    private static readonly string[] WheelModeLabels = { "Profiles", "Output Device", "Media Controls", "Custom" };

    private void AddWheelRow(QuickWheelConfig qw)
    {
        var state = new WheelRowState { Enabled = qw.Enabled, ModeIdx = Math.Clamp((int)qw.Mode, 0, 3) };

        var body = new StackPanel();
        var enableSw = MakeSwitch(state.Enabled, on => { state.Enabled = on; QueueSave(); },
            out _, "Enable or disable this wheel (disabling releases the button's hold action)");
        System.Windows.Controls.Border wrapper = null!;
        var more = UiKit.MoreButton(() => new List<GlassMenuItem>
        {
            new("Remove wheel", MaterialIconKind.DeleteOutline, () =>
            {
                WheelRowsPanel.Children.Remove(wrapper);
                RenumberWheels();
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }, IsDanger: true),
        });
        more.Margin = new Thickness(8, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(enableSw);
        actions.Children.Add(more);

        var card = UiKit.Card(Color.FromRgb(0xFF, 0x70, 0x43), MaterialIconKind.ChartDonut, "Wheel", null, actions, body);
        wrapper = card.Root;
        wrapper.Tag = state;
        state.Title = card.Title;
        state.Subtitle = card.Subtitle;

        // Trigger
        var btnCombo = new ComboBox
        {
            MinWidth = 180,
            ToolTip = "Which hardware input opens this wheel (hold)",
        };
        btnCombo.SetResourceReference(Control.BackgroundProperty, "InputBgBrush");
        btnCombo.SetResourceReference(Control.BorderBrushProperty, "InputBorderBrush");
        btnCombo.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        PopulateTriggerCombo(btnCombo, qw.Device, qw.TriggerButton);
        btnCombo.SelectionChanged += (_, _) => { UpdateWheelSubtitle(state); if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
        state.TriggerCombo = btnCombo;
        body.Children.Add(MakeSettingRow("Trigger", "Hold this input to open the wheel", btnCombo));

        // Mode pills
        body.Children.Add(MakeLabel("Shows"));

        // Custom slots panel (shown only when mode = Custom)
        var customPanel = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = state.ModeIdx == (int)QuickWheelMode.Custom ? Visibility.Visible : Visibility.Collapsed,
            Tag = "customPanel",
        };
        state.CustomPanel = customPanel;

        var modePills = MakePillRow(WheelModeLabels, state.ModeIdx, idx =>
        {
            state.ModeIdx = idx;
            UpdateWheelSubtitle(state);
            customPanel.Visibility = idx == (int)QuickWheelMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); }
        });
        body.Children.Add(modePills);

        customPanel.Children.Add(MakeHint("Pick an action and the label shown on its wheel segment (max 8 slots).", new Thickness(0, 0, 0, 8)));

        // Populate existing custom slots
        foreach (var slot in qw.CustomSlots)
            AddCustomSlotRow(customPanel, slot);

        var addSlotBtn = UiKit.LinkRow(MaterialIconKind.Plus, "Add slot", accent: true, () =>
        {
            int slotCount = 0;
            foreach (var c in customPanel.Children)
                if (c is Grid g && g.Tag is string s && s == "slotRow") slotCount++;
            if (slotCount >= 8) return;
            AddCustomSlotRow(customPanel, new CustomWheelSlot());
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }, "Add a custom action slot (max 8)");
        addSlotBtn.Tag = "addSlotBtn";
        addSlotBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addSlotBtn.Margin = new Thickness(-8, 2, 0, 0);
        customPanel.Children.Add(addSlotBtn);
        body.Children.Add(customPanel);

        WheelRowsPanel.Children.Add(wrapper);
        RenumberWheels();
        UpdateWheelSubtitle(state);
    }

    private static void UpdateWheelSubtitle(WheelRowState st)
    {
        if (st.Subtitle == null) return;
        string trig = (st.TriggerCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string mode = WheelModeLabels[Math.Clamp(st.ModeIdx, 0, WheelModeLabels.Length - 1)];
        st.Subtitle.Text = string.IsNullOrEmpty(trig) ? mode : $"Hold {trig}  ·  {mode}";
        st.Subtitle.Visibility = Visibility.Visible;
    }

    private void RenumberWheels()
    {
        int n = 1;
        foreach (var child in WheelRowsPanel.Children)
            if (child is FrameworkElement fe && fe.Tag is WheelRowState st)
                st.Title.Text = $"Wheel {n++}";
        if (_wheelCountText != null)
            _wheelCountText.Text = n - 1 == 1 ? "1 wheel" : $"{n - 1} wheels";
    }

    private void AddCustomSlotRow(StackPanel customPanel, CustomWheelSlot slot)
    {
        var slotRow = new Grid { Margin = new Thickness(0, 0, 0, 4), Tag = "slotRow" };
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var actionCombo = new ComboBox
        {
            Width = 175,
            Background = (System.Windows.Media.Brush)FindResource("BgBaseBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "Action to execute when this slot is selected",
        };
        int selectedIdx = -1;
        for (int i = 0; i < CustomSlotActions.Length; i++)
        {
            actionCombo.Items.Add(new ComboBoxItem { Content = CustomSlotActions[i].label, Tag = CustomSlotActions[i].id });
            if (CustomSlotActions[i].id == slot.ActionId) selectedIdx = i;
        }
        actionCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
        actionCombo.SelectionChanged += (_, _) =>
        {
            // Auto-fill label if it was empty or matched the previous action label
            if (actionCombo.SelectedItem is ComboBoxItem ci && slotRow.Children[1] is TextBox labelBox)
            {
                if (string.IsNullOrEmpty(labelBox.Text) || CustomSlotActions.Any(a => a.label == labelBox.Text))
                    labelBox.Text = ci.Content?.ToString() ?? "";
            }
            if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); }
        };
        Grid.SetColumn(actionCombo, 0);
        slotRow.Children.Add(actionCombo);

        var labelBox = new TextBox
        {
            Text = string.IsNullOrEmpty(slot.Label) && selectedIdx >= 0 ? CustomSlotActions[selectedIdx].label : slot.Label,
            Width = 145,
            Background = (System.Windows.Media.Brush)FindResource("BgBaseBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "Display label on the wheel segment",
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 28,
        };
        labelBox.TextChanged += (_, _) => { if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
        Grid.SetColumn(labelBox, 2);
        slotRow.Children.Add(labelBox);

        var removeSlotBtn = UiKit.IconButton(MaterialIconKind.Close, "Remove this slot", _ =>
        {
            customPanel.Children.Remove(slotRow);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }, danger: true, size: 24);
        Grid.SetColumn(removeSlotBtn, 4);
        slotRow.Children.Add(removeSlotBtn);

        // Insert before the "Add Slot" button (last child)
        int insertIdx = customPanel.Children.Count - 1;
        if (insertIdx < 0) insertIdx = 0;
        // Find the add button — it's the last child with Tag "addSlotBtn"
        bool inserted = false;
        for (int i = customPanel.Children.Count - 1; i >= 0; i--)
        {
            if (customPanel.Children[i] is FrameworkElement fe && fe.Tag is string t && t == "addSlotBtn")
            {
                customPanel.Children.Insert(i, slotRow);
                inserted = true;
                break;
            }
        }
        if (!inserted) customPanel.Children.Add(slotRow);
    }

    private List<QuickWheelConfig> CollectWheelConfigs()
    {
        var list = new List<QuickWheelConfig>();
        foreach (var child in WheelRowsPanel.Children)
        {
            if (child is not FrameworkElement fe || fe.Tag is not WheelRowState st) continue;

            // Trigger device + local index live in the selected item's Tag,
            // set up by PopulateTriggerCombo. Fall back to Turn Up button 0.
            var device = QuickWheelDevice.TurnUp;
            int triggerIdx = 0;
            if (st.TriggerCombo.SelectedItem is ComboBoxItem trigCi && trigCi.Tag is ValueTuple<QuickWheelDevice, int> tag)
            {
                device = tag.Item1;
                triggerIdx = tag.Item2;
            }

            var cfg = new QuickWheelConfig
            {
                Enabled = st.Enabled,
                Mode = (QuickWheelMode)Math.Clamp(st.ModeIdx, 0, 3),
                Device = device,
                TriggerButton = triggerIdx,
                TriggerGesture = "hold",
            };

            // Collect custom slots if mode is Custom
            if (cfg.Mode == QuickWheelMode.Custom)
            {
                foreach (var slotChild in st.CustomPanel.Children)
                {
                    if (slotChild is Grid slotRow && slotRow.Tag is string s && s == "slotRow"
                        && slotRow.Children.Count >= 2)
                    {
                        var actionCombo = slotRow.Children[0] as ComboBox;
                        var labelBox = slotRow.Children[1] as TextBox;
                        string actionId = "";
                        if (actionCombo?.SelectedItem is ComboBoxItem ci && ci.Tag is string aid)
                            actionId = aid;
                        cfg.CustomSlots.Add(new CustomWheelSlot
                        {
                            ActionId = actionId,
                            Label = labelBox?.Text ?? "",
                        });
                    }
                }
            }

            list.Add(cfg);
        }
        return list;
    }

    private void OsdPosition_Click(object sender, MouseButtonEventArgs e)
    {
        if (_loading || _config == null) return;
        if (sender is System.Windows.Controls.Border border && border.Tag is string posStr)
        {
            if (Enum.TryParse<OsdPosition>(posStr, out var pos))
            {
                _config.Osd.Position = pos;
                HighlightOsdPosition(pos);
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
    }

    private void HighlightOsdPosition(OsdPosition active)
    {
        _currentPosition = active;
        var accent = ThemeManager.Accent;
        foreach (var (pos, cell) in _posCells)
        {
            bool isActive = pos == active;
            if (isActive)
            {
                cell.Background = new SolidColorBrush(accent);
                cell.BorderBrush = new SolidColorBrush(accent);
            }
            else
            {
                cell.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBgBrush");
                cell.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
            }
            if (cell.Child is TextBlock tb)
            {
                if (isActive) tb.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
                else tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            }
        }
    }

    /// <summary>
    /// Sync HoldAction="quick_wheel" across the Turn Up + N3 button lists.
    /// Routes each binding to the right config slot based on Device.
    /// </summary>
    private void SyncWheelButtonActions(
        HashSet<(QuickWheelDevice Device, int LocalIdx)> oldBindings,
        HashSet<(QuickWheelDevice Device, int LocalIdx)> newBindings)
    {
        if (_config == null) return;

        foreach (var b in oldBindings.Except(newBindings))
            SetQuickWheelHoldAction(b.Device, b.LocalIdx, clear: true);

        foreach (var b in newBindings)
            SetQuickWheelHoldAction(b.Device, b.LocalIdx, clear: false);
    }

    /// <summary>
    /// Set or clear HoldAction="quick_wheel" on the right ButtonConfig.
    /// Turn Up uses position-indexed _config.Buttons; N3 uses sparse
    /// _config.N3.Buttons keyed by virtual Idx (find-or-create on set).
    /// Clears only stomp our own action so user edits survive.
    /// </summary>
    private void SetQuickWheelHoldAction(QuickWheelDevice device, int localIdx, bool clear)
    {
        if (_config == null) return;

        if (device == QuickWheelDevice.TurnUp)
        {
            var buttons = _config.Buttons;
            if (localIdx < 0 || localIdx >= buttons.Count) return;
            if (clear)
            {
                if (buttons[localIdx].HoldAction == "quick_wheel")
                    buttons[localIdx].HoldAction = "none";
            }
            else
            {
                buttons[localIdx].HoldAction = "quick_wheel";
            }
            return;
        }

        int virtualIdx = device switch
        {
            QuickWheelDevice.ScSideButton    => 10000 + localIdx,
            QuickWheelDevice.ScEncoderPress  => 10003 + localIdx,
            _                                => -1,
        };
        if (virtualIdx < 0) return;

        var list = _config.N3.Buttons;
        var btn = list.FirstOrDefault(b => b.Idx == virtualIdx);

        if (clear)
        {
            if (btn != null && btn.HoldAction == "quick_wheel")
                btn.HoldAction = "none";
            return;
        }

        if (btn == null)
        {
            btn = new ButtonConfig { Idx = virtualIdx, Action = "none", HoldAction = "quick_wheel" };
            list.Add(btn);
        }
        else
        {
            btn.HoldAction = "quick_wheel";
        }
    }

    private void PopulateOsdMonitorPicker(int selectedIndex)
    {
        CmbOsdMonitor.ClearItems();
        var monitors = DisplayMonitorResolver.GetMonitors();

        foreach (var monitor in monitors)
            CmbOsdMonitor.AddItem(monitor.Label, monitor.Index);

        var pickerIndex = monitors.FindIndex(m => m.Index == selectedIndex);
        CmbOsdMonitor.SelectedIndex = pickerIndex >= 0 ? pickerIndex : 0;
    }

    private void CmbOsdMonitor_SelectionChanged(object? sender, EventArgs e)
    {
        if (_loading || _config == null || CmbOsdMonitor.SelectedIndex < 0) return;
        if (CmbOsdMonitor.SelectedTag is int monitorIndex)
            DisplayMonitorResolver.RememberOsdMonitor(_config.Osd, monitorIndex);
        else
            _config.Osd.MonitorIndex = CmbOsdMonitor.SelectedIndex;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnOsdPreview(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        var overlay = new OsdOverlay();
        overlay.SetPosition(_config.Osd.Position, DisplayMonitorResolver.ResolveOsdMonitorIndex(_config.Osd));
        overlay.ShowVolume("Preview", 75, "VolumeHigh");
        }
}
