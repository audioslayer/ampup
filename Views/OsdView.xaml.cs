using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AmpUp.Controls;
using AmpUp.Core.Services;
using Material.Icons;
using Material.Icons.WPF;

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
    // Customize Wheels card
    private StyledSlider _sldLookScale = null!, _sldLookBg = null!, _sldLookGlow = null!;
    private TextBlock _lblLookScale = null!, _lblLookBg = null!, _lblLookGlow = null!;
    private WrapPanel _lookSwatches = null!;
    private TextBlock LblOsdVolumeDur = null!, LblOsdProfileDur = null!, LblOsdDeviceDur = null!, LblOsdWheelDur = null!;
    private ListPicker CmbOsdMonitor = null!;
    private readonly Dictionary<OsdPosition, System.Windows.Controls.Border> _posCells = new();
    private OsdPosition _currentPosition = OsdPosition.BottomRight;
    private StackPanel WheelRowsPanel = null!;
    private StackPanel _wheelTabs = null!;
    private System.Windows.Controls.Border _wheelActionsHost = null!;
    private TextBlock _wheelEmpty = null!;
    private WheelRowState? _selectedWheel;
    private readonly List<Action> _accentRepaints = new();
    private static readonly Color WheelColor = Color.FromRgb(0xFF, 0x70, 0x43);

    /// <summary>Per-wheel UI state, stored on each wheel card's Tag.</summary>
    private sealed class WheelRowState
    {
        public bool Enabled;
        public string Name = "";
        public QuickWheelMode Mode;
        public ComboBox TriggerCombo = null!;
        public StackPanel CustomPanel = null!;
        public StackPanel SlotList = null!;
        public StackPanel ItemsPanel = null!;
        public Controls.HaloWheel? Mini;
        public System.Windows.Controls.Border ShowsHelp = null!;
        public DispatcherTimer? MiniTimer;
        public TextBlock Subtitle = null!;
        public System.Windows.Controls.Border Tab = null!;
        public TextBlock TabLabel = null!;
        public MaterialIcon TabIcon = null!;
        public FrameworkElement TabPencil = null!;
        public FrameworkElement Actions = null!;
        public List<string> OutputDeviceIds = new();
        public List<string> InputDeviceIds = new();
        public List<string> SignalRgbEffects = new();
        public List<string> MediaActions = new();
        public List<string> ProfileNames = new();
        public Dictionary<string, WheelItemIcon> ItemIcons = new();
        public readonly List<CustomSlotRowState> Slots = new();
    }

    /// <summary>Editor state for one Custom wheel slot row.</summary>
    private sealed class CustomSlotRowState
    {
        public Grid Row = null!;
        public TextBlock Number = null!;
        public ComboBox Action = null!;
        public TextBox Label = null!;
        public Func<string> GetPath = () => "", GetKeys = () => "", GetProfile = () => "", GetDevice = () => "";
        /// <summary>Picked icon (Material name or image path) and color; empty = automatic.</summary>
        public string Icon = "", Color = "";
        /// <summary>Redraws the row's icon tile (after the icon is changed from the mini wheel).</summary>
        public Action RefreshIcon = () => { };
        public string ActionId => (Action?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
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

        // No page header, like the Room page: the tab bar sits at the top. Two tabs (same underline bar as Room): pop-up overlays, and Quick Wheels
        var overlaysTab = new StackPanel();
        var wheelsTab = new StackPanel { Visibility = Visibility.Collapsed };
        RootPanel.Children.Add(BuildOsdTabBar(overlaysTab, wheelsTab));
        RootPanel.Children.Add(overlaysTab);
        RootPanel.Children.Add(wheelsTab);

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

        popupsBody.Children.Add(MakeSettingRow("Volume changes", null, volSw,
            "Shows the knob's name and new level while you turn it."));
        popupsBody.Children.Add(_volumeDurRow);
        popupsBody.Children.Add(MakeSettingRow("Profile switch", null, profSw,
            "Shows the new profile's name and icon when you switch profiles, by button or automatically."));
        popupsBody.Children.Add(_profileDurRow);
        popupsBody.Children.Add(MakeSettingRow("Audio device switch", null, devSw,
            "Shows which speaker, headset or mic is now the default when a button cycles or selects a device."));
        popupsBody.Children.Add(_deviceDurRow);
        // Last of the on/off rows, with the others
        var fsRow = MakeSettingRow("Hide over fullscreen apps", null, fsSw,
            "Stops overlays from covering fullscreen games, videos and presentations. The knobs and buttons still work as normal.");
        fsRow.Margin = new Thickness(0, 0, 0, 4);
        popupsBody.Children.Add(fsRow);

        // Where they show up: monitor + position as one centered block, dropdown as wide as the grid
        popupsBody.Children.Add(MakeDivider());
        var placeGroup = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        CmbOsdMonitor = new ListPicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 14) };
        placeGroup.Children.Add(MakeLabel("Monitor"));
        placeGroup.Children.Add(CmbOsdMonitor);
        placeGroup.Children.Add(MakeLabelWithHelp("Position",
            "Windows draws its own volume popup near the top-left or bottom-center. Pick a different spot so the two don't overlap.",
            "Position", new Thickness(0, 0, 0, 8)));
        var posPicker = BuildPositionPicker();
        posPicker.HorizontalAlignment = HorizontalAlignment.Center;
        placeGroup.Children.Add(posPicker);
        popupsBody.Children.Add(placeGroup);

        // Live preview on the right, like the Quick Wheel cards
        var overlaySplit = new Grid();
        overlaySplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overlaySplit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        overlaySplit.Children.Add(popupsBody);
        var osdPreview = BuildOsdMiniPreview();
        Grid.SetColumn(osdPreview, 1);
        overlaySplit.Children.Add(osdPreview);

        var popupsCard = UiKit.Card(Color.FromRgb(0x42, 0xA5, 0xF5), MaterialIconKind.BellOutline,
            "On-Screen Overlays", "Quick feedback over any app, and where it shows up", null, overlaySplit,
            help: "Small cards that fade in when something changes, then fade out on their own. " +
                  "Turn each one on or off, set how long it stays, and pick the monitor and corner.\n\n" +
                  "The preview on the right shows where it lands; click it to see a real one on your screen.").Root;
        overlaysTab.Children.Add(popupsCard);

        // ══════════════════ QUICK WHEELS ══════════════════
        // Card header: title + subtitle, count · (?) on the right. Body: Auto-dismiss row, wheels, add link.
        var wheelBody = new StackPanel();

        SldOsdWheelDur = NewDurSlider(0, 15, 0, null!);
        LblOsdWheelDur = NewValLabel("Off");
        wheelBody.Children.Add(MakeLabelWithHelp("Stay open after release",
            "Off: letting go of the button picks the highlighted item right away.\n" +
            "On: the wheel stays up this long after you let go, so you can keep turning the knob or click with the mouse.",
            "Stay open after release", new Thickness(0, 4, 0, 10)));
        wheelBody.Children.Add(MakeDurationRow(SldOsdWheelDur, LblOsdWheelDur));

        // Underline tab bar: one tab per wheel (green dot = enabled), then "+ Add".
        var tabStrip = new System.Windows.Controls.Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 2, 0, 14) };
        tabStrip.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
        _wheelTabs = new StackPanel { Orientation = Orientation.Horizontal };
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _wheelTabs,
        };
        // Tabs on the left, the selected wheel's on/off switch + remove on the right.
        var tabRow = new Grid();
        tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabRow.Children.Add(tabScroll);
        _wheelActionsHost = new System.Windows.Controls.Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 2) };
        Grid.SetColumn(_wheelActionsHost, 1);
        tabRow.Children.Add(_wheelActionsHost);
        tabStrip.Child = tabRow;
        wheelBody.Children.Add(tabStrip);

        _wheelEmpty = MakeHint("No wheels yet. Press + Add to make one.", new Thickness(0, 0, 0, 4));
        wheelBody.Children.Add(_wheelEmpty);

        // Every wheel's editor lives here; only the selected one is visible.
        WheelRowsPanel = new StackPanel();
        wheelBody.Children.Add(WheelRowsPanel);

        // Look for every wheel (was its own card)
        wheelBody.Children.Add(MakeDivider());
        wheelBody.Children.Add(MakeLabelWithHelp("Look",
            "Changes how every Quick Wheel looks. The volume / profile / device pop-ups are not affected.\n\n" +
            "Start from a preset, then fine-tune. The wheel preview above updates as you go; click it to see the wheel full size.",
            "Look", new Thickness(0, 4, 0, 10)));
        wheelBody.Children.Add(BuildLookSection());

        var wheelCard = UiKit.Card(WheelColor, MaterialIconKind.ChartDonut,
            "Quick Wheels", "Hold a button, turn a knob, let go to pick", null, wheelBody,
            help: "A radial menu for things you switch often, like profiles, audio devices, media controls or your own shortcuts.\n\n" +
                  "1. Hold the trigger button\n2. Turn any knob to move the highlight\n3. Let go to pick\n\n" +
                  "You can also point and click with the mouse. Taking a button for a wheel replaces its hold action.").Root;
        wheelsTab.Children.Add(wheelCard);
    }


    // ══════════════════ TABS ══════════════════

    private int _osdTabIndex;

    /// <summary>Material underline tab bar (OVERLAYS / QUICK WHEELS), styled like the Room page's.</summary>
    private FrameworkElement BuildOsdTabBar(params FrameworkElement[] pages)
    {
        var names = new[] { "OVERLAYS", "QUICK WHEELS" };
        var tabs = new System.Windows.Controls.Border[names.Length];

        void Paint()
        {
            var accent = ThemeManager.Accent;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == _osdTabIndex;
                tabs[i].BorderBrush = new SolidColorBrush(on ? accent : Colors.Transparent);
                if (tabs[i].Child is TextBlock t)
                {
                    t.Foreground = on ? new SolidColorBrush(accent) : FindBrush("TextSecBrush");
                    t.FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold;
                }
                pages[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var label = new TextBlock { Text = names[i], FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center };
            var tab = new System.Windows.Controls.Border
            {
                Padding = new Thickness(22, 10, 22, 10),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0, 0, 0, 2), // 2px underline slot
                Background = Brushes.Transparent,
                Child = label,
            };
            tab.MouseEnter += (_, _) => { if (idx != _osdTabIndex) label.Foreground = FindBrush("TextPrimaryBrush"); };
            tab.MouseLeave += (_, _) => { if (idx != _osdTabIndex) label.Foreground = FindBrush("TextSecBrush"); };
            tab.MouseLeftButtonDown += (_, _) => { _osdTabIndex = idx; Paint(); };
            tabs[i] = tab;
            row.Children.Add(tab);
        }
        _accentRepaints.Add(Paint);
        Paint();

        var underline = new System.Windows.Controls.Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        underline.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBgBrush");
        var card = new System.Windows.Controls.Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
            Child = underline,
        };
        card.SetResourceReference(StyleProperty, "CardPanel");
        return card;
    }

    // ══════════════════ CUSTOMIZE WHEELS ══════════════════

    /// <summary>A complete wheel look, shown as a tile with a mini wheel drawn in that style.</summary>
    private sealed record WheelPreset(string Name, int Scale, int Bg, string Colors, string Ring, string Gap,
        int Glow, string Highlight, string Center, string Animation)
    {
        public bool Matches(OsdConfig o) =>
            o.Scale == Scale && o.BackgroundOpacity == Bg && o.WheelLook.SegmentColors == Colors
            && o.WheelLook.RingThickness == Ring && o.WheelLook.SegmentGap == Gap && o.WheelLook.Glow == Glow
            && o.WheelLook.Highlight == Highlight && o.WheelLook.CenterInfo == Center && o.WheelLook.Animation == Animation;

        /// <summary>Copy this look onto a config. The Color pick is left alone.</summary>
        public void ApplyTo(OsdConfig o)
        {
            o.Scale = Scale;
            o.BackgroundOpacity = Bg;
            o.WheelLook = new QuickWheelLook
            {
                SegmentColors = Colors, RingThickness = Ring, SegmentGap = Gap, Glow = Glow,
                Highlight = Highlight, CenterInfo = Center, Animation = Animation,
            };
        }
    }

    private static readonly WheelPreset[] WheelPresets =
    {
        new("Classic", 100, 100, "Item", "Normal", "Small", 100, "Fill", "Full", "Fade"),
        new("Neon", 100, 90, "Rainbow", "Thick", "Small", 180, "Fill", "Full", "Pop"),
        new("Minimal", 90, 70, "Accent", "Thin", "Wide", 0, "Outline", "Simple", "Fade"),
        new("Glass", 100, 40, "Item", "Normal", "Small", 60, "Outline", "Full", "Fade"),
        new("Compact", 75, 100, "Item", "Normal", "Small", 100, "Fill", "Simple", "Fade"),
        new("Bold", 125, 100, "Item", "Thick", "None", 140, "Fill", "Icon", "Spin"),
    };

    private static readonly (string Name, string Hex)[] LookColors =
    {
        ("Green", "#00E676"), ("Cyan", "#00E5FF"), ("Blue", "#448AFF"), ("Purple", "#B388FF"), ("Pink", "#FF4081"),
        ("Red", "#FF5252"), ("Orange", "#FF9100"), ("Yellow", "#FFD740"), ("White", "#F5F5F5"),
    };

    private readonly List<(WheelPreset Preset, System.Windows.Controls.Border Tile, Controls.HaloWheel Halo)> _presetTiles = new();
    private SegmentedControl _segColors = null!, _segRing = null!, _segGap = null!, _segHighlight = null!,
        _segCenter = null!, _segAnim = null!;

    /// <summary>
    /// Quick Wheel look section (inside the Quick Wheels card): preset tiles, then Size / Color
    /// beside Ring / Animation. Each wheel's mini preview above shows the result.
    /// </summary>
    private FrameworkElement BuildLookSection()
    {
        var left = new StackPanel();

        StyledSlider NewSlider(double min, double max, string tip) => new()
        {
            Minimum = min, Maximum = max, Value = 100, Step = 5, ShowLabel = false, Height = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = tip, AccentColor = ThemeManager.Accent,
        };
        TextBlock NewVal() => new()
        {
            Text = "100%", FontSize = 11, FontWeight = FontWeights.SemiBold, MinWidth = 38,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
        };
        SegmentedControl NewSeg(Action<QuickWheelLook, string> set, params (string Value, string Text)[] options)
        {
            // Fixed width per segment so they come out even (star columns shrink-wrap when left-aligned)
            var seg = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left, AccentColor = ThemeManager.Accent,
                Width = options.Length * 92 };
            foreach (var (value, text) in options) seg.AddSegment(text, value);
            seg.SelectionChanged += (_, _) =>
            {
                if (_loading || _config == null || seg.SelectedTag is not string v) return;
                set(_config.Osd.WheelLook, v);
                LookChanged();
                if (seg == _segAnim) _selectedWheel?.Mini?.PlayIntro();
            };
            return seg;
        }

        // ── PRESETS ── one click sets every option below
        left.Children.Add(UiKit.SectionHeader("PRESETS"));
        var presetWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var p in WheelPresets) presetWrap.Children.Add(MakePresetTile(p));
        left.Children.Add(presetWrap);

        var colA = new StackPanel();
        var colB = new StackPanel();

        // ── SIZE ──
        colA.Children.Add(UiKit.SectionHeader("SIZE"));
        _sldLookScale = NewSlider(60, 160, "How big the Quick Wheels are");
        _lblLookScale = NewVal();
        _sldLookBg = NewSlider(10, 100, "How solid the dark background is. Lower lets more of the screen show through.");
        _lblLookBg = NewVal();
        _sldLookGlow = NewSlider(0, 200, "How strongly the wheel and the highlighted item glow");
        _lblLookGlow = NewVal();
        colA.Children.Add(MakeDurationRow(_sldLookScale, _lblLookScale, "Size"));
        colA.Children.Add(MakeDurationRow(_sldLookBg, _lblLookBg, "Background"));
        colA.Children.Add(MakeDurationRow(_sldLookGlow, _lblLookGlow, "Glow"));
        HookLookSlider(_sldLookScale, _lblLookScale, (o, v) => o.Scale = v);
        HookLookSlider(_sldLookBg, _lblLookBg, (o, v) => o.BackgroundOpacity = v);
        HookLookSlider(_sldLookGlow, _lblLookGlow, (o, v) => o.WheelLook.Glow = v);

        // ── COLOR ──
        colA.Children.Add(UiKit.SectionHeader("COLOR"));
        _lookSwatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        colA.Children.Add(_lookSwatches);
        _segColors = NewSeg((l, v) => l.SegmentColors = v, ("Item", "Per item"), ("Accent", "One color"), ("Rainbow", "Rainbow"));
        colA.Children.Add(MakeLookRow("Segments", _segColors,
            "Per item: each item keeps its own color. One color: every segment uses the color above. Rainbow: a rainbow around the ring."));

        // ── RING ──
        colB.Children.Add(UiKit.SectionHeader("RING"));
        _segRing = NewSeg((l, v) => l.RingThickness = v, ("Thin", "Thin"), ("Normal", "Normal"), ("Thick", "Thick"));
        _segGap = NewSeg((l, v) => l.SegmentGap = v, ("None", "None"), ("Small", "Small"), ("Wide", "Wide"));
        _segHighlight = NewSeg((l, v) => l.Highlight = v, ("Fill", "Solid"), ("Outline", "Outline"));
        _segCenter = NewSeg((l, v) => l.CenterInfo = v, ("Full", "Full"), ("Simple", "Icon + name"), ("Icon", "Icon only"));
        colB.Children.Add(MakeLookRow("Thickness", _segRing));
        colB.Children.Add(MakeLookRow("Spacing", _segGap, "Space between segments. None makes one continuous ring."));
        colB.Children.Add(MakeLookRow("Highlight", _segHighlight, "How the item you're pointing at stands out"));
        colB.Children.Add(MakeLookRow("Center", _segCenter,
            "What the middle shows: Full = icon, name, item type and the ACTIVE badge"));

        // ── ANIMATION ──
        colB.Children.Add(UiKit.SectionHeader("ANIMATION"));
        _segAnim = NewSeg((l, v) => l.Animation = v, ("Fade", "Fade"), ("Pop", "Pop"), ("Spin", "Spin"), ("None", "None"));
        var animRow = MakeLookRow("Open with", _segAnim, "How the wheel appears when you hold the button");
        animRow.Margin = new Thickness(0);
        colB.Children.Add(animRow);

        var root = new StackPanel();
        root.Children.Add(left);
        root.Children.Add(UiKit.ResponsivePair(colA, colB, 700));

        _accentRepaints.Add(() =>
        {
            foreach (var s in new[] { _sldLookScale, _sldLookBg, _sldLookGlow }) s.AccentColor = ThemeManager.Accent;
            foreach (var l in new[] { _lblLookScale, _lblLookBg, _lblLookGlow }) l.Foreground = new SolidColorBrush(ThemeManager.Accent);
            foreach (var g in new[] { _segColors, _segRing, _segGap, _segHighlight, _segCenter, _segAnim }) g.AccentColor = ThemeManager.Accent;
            if (_config != null) SyncLookControls(_config.Osd);
        });
        return root;
    }

    private void HookLookSlider(StyledSlider slider, TextBlock label, Action<OsdConfig, int> set)
    {
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"{slider.Value:F0}%";
            if (_loading || _config == null) return;
            set(_config.Osd, (int)Math.Round(slider.Value));
            LookChanged();
        };
    }

    /// <summary>Muted label column + control, the layout used for every option row on the card.</summary>
    private Grid MakeLookRow(string label, FrameworkElement control, string? help = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cap = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, ToolTip = help };
        cap.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        g.Children.Add(cap);
        control.VerticalAlignment = VerticalAlignment.Center;
        if (help != null) control.ToolTip ??= help;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>Preset tile: a mini wheel drawn in the preset's style with its name under it.</summary>
    private System.Windows.Controls.Border MakePresetTile(WheelPreset p)
    {
        var halo = new Controls.HaloWheel { IsHitTestVisible = false };
        var stack = new StackPanel();
        stack.Children.Add(new Viewbox { Width = 64, Height = 64, Child = halo, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center });
        var name = new TextBlock { Text = p.Name, FontSize = 11, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(name);

        var tile = new System.Windows.Controls.Border
        {
            Width = 92,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(8, 10, 8, 8),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand,
            ToolTip = $"Use the {p.Name} look",
            Child = stack,
        };
        tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        tile.MouseEnter += (_, _) => PaintPresetTile(p, tile, hover: true);
        tile.MouseLeave += (_, _) => PaintPresetTile(p, tile, hover: false);
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_config == null) return;
            p.ApplyTo(_config.Osd);
            LookChanged();
            _selectedWheel?.Mini?.PlayIntro();
        };
        _presetTiles.Add((p, tile, halo));
        return tile;
    }

    private void PaintPresetTile(WheelPreset p, System.Windows.Controls.Border tile, bool hover)
    {
        var a = ThemeManager.Accent;
        bool on = _config != null && p.Matches(_config.Osd);
        if (on)
        {
            tile.BorderBrush = new SolidColorBrush(a);
            tile.Background = new SolidColorBrush(Color.FromArgb(0x1F, a.R, a.G, a.B));
        }
        else
        {
            if (hover) tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, a.R, a.G, a.B));
            else tile.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
            tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        }
    }

    private void LoadLook(OsdConfig osd)
    {
        osd.WheelLook ??= new QuickWheelLook();
        SyncLookControls(osd);
    }

    /// <summary>Show the config's current look in every control on the card (no change events fire).</summary>
    private void SyncLookControls(OsdConfig osd)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _sldLookScale.Value = Math.Clamp(osd.Scale, 60, 160);
            _lblLookScale.Text = $"{_sldLookScale.Value:F0}%";
            _sldLookBg.Value = Math.Clamp(osd.BackgroundOpacity, 10, 100);
            _lblLookBg.Text = $"{_sldLookBg.Value:F0}%";
            _sldLookGlow.Value = Math.Clamp(osd.WheelLook.Glow, 0, 200);
            _lblLookGlow.Text = $"{_sldLookGlow.Value:F0}%";

            static void Select(SegmentedControl seg, string value)
            {
                for (int i = 0; i < seg.SegmentCount; i++)
                    if (seg.GetTagAt(i) as string == value) { seg.SelectedIndex = i; return; }
            }
            Select(_segColors, osd.WheelLook.SegmentColors);
            Select(_segRing, osd.WheelLook.RingThickness);
            Select(_segGap, osd.WheelLook.SegmentGap);
            Select(_segHighlight, osd.WheelLook.Highlight);
            Select(_segCenter, osd.WheelLook.CenterInfo);
            Select(_segAnim, osd.WheelLook.Animation);
        }
        finally { _loading = wasLoading; }

        BuildLookSwatches(osd);

        // Preset tiles draw their own look in the current color
        if (_config != null)
        {
            var sample = Services.QuickWheelContent.Build(_config, new QuickWheelConfig { Mode = QuickWheelMode.MediaControls });
            foreach (var (p, tile, halo) in _presetTiles)
            {
                var o = new OsdConfig { AccentColor = osd.AccentColor };
                p.ApplyTo(o);
                halo.ApplyStyle(o);
                halo.SetContent(sample);
                PaintPresetTile(p, tile, hover: tile.IsMouseOver);
            }
        }
    }

    private void BuildLookSwatches(OsdConfig osd)
    {
        var accent = ThemeManager.Accent;
        _lookSwatches.Children.Clear();

        System.Windows.Controls.Border Swatch(Brush fill, bool selected, string tip, UIElement? child = null)
        {
            var sw = new System.Windows.Controls.Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = fill,
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? Brushes.White : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = child,
            };
            _lookSwatches.Children.Add(sw);
            return sw;
        }
        void Pick(string hex)
        {
            if (_config == null) return;
            _config.Osd.AccentColor = hex;
            LookChanged();
        }

        // "Theme" follows the app accent (Settings -> Appearance)
        var themeSw = Swatch(new SolidColorBrush(accent), string.IsNullOrEmpty(osd.AccentColor),
            "Match the app's theme color",
            new MaterialIcon { Kind = MaterialIconKind.Autorenew, Width = 14, Height = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)) });
        themeSw.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(""); };

        foreach (var (name, hex) in LookColors)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var sw = Swatch(new SolidColorBrush(c), hex.Equals(osd.AccentColor, StringComparison.OrdinalIgnoreCase), name);
            string h = hex;
            sw.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(h); };
        }

        bool isCustom = !string.IsNullOrEmpty(osd.AccentColor)
            && !LookColors.Any(p => p.Hex.Equals(osd.AccentColor, StringComparison.OrdinalIgnoreCase));
        var rainbow = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Red, 0.0), new(Colors.Yellow, 0.17), new(Colors.Lime, 0.33),
            new(Colors.Cyan, 0.5), new(Colors.Blue, 0.67), new(Colors.Magenta, 0.83), new(Colors.Red, 1.0),
        }, new Point(0, 0), new Point(1, 1));
        var customSw = Swatch(rainbow, isCustom, "Custom color",
            new TextBlock { Text = "+", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        customSw.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_config == null) return;
            string before = _config.Osd.AccentColor;
            var initial = Controls.HaloWheel.ParseColor(before) ?? accent;
            var dialog = new ColorPickerDialog(initial) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c => Pick($"#{c.R:X2}{c.G:X2}{c.B:X2}"); // live preview while dragging
            if (dialog.ShowDialog() == true)
            {
                var c = dialog.SelectedColor;
                Pick($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
            else
            {
                Pick(before); // cancelled: undo the live preview
            }
        };
    }

    /// <summary>A look setting changed: save, then refresh the card controls and every mini wheel.</summary>
    private void LookChanged()
    {
        if (_config == null) return;
        SyncLookControls(_config.Osd);
        foreach (var child in WheelRowsPanel.Children)
            if (child is FrameworkElement fe && fe.Tag is WheelRowState st)
                RefreshMiniNow(st);
        QueueSave();
    }

    private FrameworkElement MakeDurationRow(StyledSlider slider, TextBlock valLabel, string caption = "Duration")
    {
        var g = new Grid { Margin = new Thickness(0, -4, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cap = new TextBlock { Text = caption, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
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

    private Grid MakeSettingRow(string label, string? hint, FrameworkElement control, string? help = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(MakeLabelWithHelp(label, help, label));
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

    /// <summary>Bold label with an optional "?" help card right after it.</summary>
    private FrameworkElement MakeLabelWithHelp(string text, string? help, string? helpTitle = null, Thickness? margin = null)
    {
        var lbl = MakeLabel(text, new Thickness(0));
        lbl.VerticalAlignment = VerticalAlignment.Center;
        if (help == null) { lbl.Margin = margin ?? new Thickness(0); return lbl; }
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin ?? new Thickness(0) };
        sp.Children.Add(lbl);
        var tip = UiKit.HelpTip(help, helpTitle);
        tip.Margin = new Thickness(4, 0, 0, 0);
        sp.Children.Add(tip);
        return sp;
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
        LoadLook(config.Osd);
        var resolvedMonitorIndex = DisplayMonitorResolver.ResolveOsdMonitorIndex(config.Osd);
        config.Osd.MonitorIndex = resolvedMonitorIndex;
        PopulateOsdMonitorPicker(resolvedMonitorIndex);

        // Quick wheels
        WheelRowsPanel.Children.Clear();
        _selectedWheel = null;
        foreach (var qw in config.Osd.QuickWheels)
            AddWheelRow(qw);
        RenumberWheels(); // also builds the strip when there are no wheels

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
        // Media
        ("media_play_pause", "Play / Pause"),
        ("media_next", "Next Track"),
        ("media_prev", "Previous Track"),
        ("media_stop", "Stop"),
        ("volume_up", "Volume Up"),
        ("volume_down", "Volume Down"),
        // Mute
        ("mute_master", "Mute Master"),
        ("mute_mic", "Mute Mic"),
        ("mute_active_window", "Mute Active Window"),
        ("mute_device", "Mute Output Device"),
        ("mute_input_device", "Mute Input Device"),
        // Devices
        ("select_output", "Use Output Device"),
        ("select_input", "Use Input Device"),
        ("cycle_output", "Next Output Device"),
        ("cycle_input", "Next Input Device"),
        // Apps & system
        ("launch_exe", "Launch App"),
        ("open_url", "Open URL"),
        ("macro", "Macro"),
        ("screenshot", "Screenshot"),
        ("switch_profile", "Switch Profile"),
        ("cycle_brightness", "Cycle LED Brightness"),
        ("signalrgb_effect", "SignalRGB Effect"),
        // Power
        ("power_sleep", "Sleep"),
        ("power_hibernate", "Hibernate"),
        ("power_lock", "Lock"),
        ("power_logoff", "Sign Out"),
        ("power_off", "Shutdown"),
        ("power_restart", "Restart"),
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

    // Dropdown order shown to the user → QuickWheelMode stored in config.
    private static readonly (QuickWheelMode mode, string label)[] WheelModes =
    {
        (QuickWheelMode.Profile, "Profiles"),
        (QuickWheelMode.OutputDevice, "Output devices"),
        (QuickWheelMode.InputDevice, "Input devices"),
        (QuickWheelMode.MediaControls, "Media controls"),
        (QuickWheelMode.SignalRgbEffect, "SignalRGB effects"),
        (QuickWheelMode.Custom, "Custom"),
    };
    private const int WheelMaxItems = RadialWheelOverlay.MaxSlots;

    // Custom slot row columns: number | action | setting | label | remove
    private static readonly GridLength[] SlotColumns =
    {
        new(18), new(42), new(168), new(1, GridUnitType.Star), new(132), new(28),
    };

    private static int WheelModeIndex(QuickWheelMode mode) =>
        Math.Max(0, Array.FindIndex(WheelModes, m => m.mode == mode));

    private void AddWheelRow(QuickWheelConfig qw)
    {
        var mode = Enum.IsDefined(qw.Mode) ? qw.Mode : QuickWheelMode.Profile;
        var state = new WheelRowState
        {
            Enabled = qw.Enabled,
            Name = qw.Name?.Trim() ?? "",
            Mode = mode,
            OutputDeviceIds = new List<string>(qw.OutputDeviceIds),
            InputDeviceIds = new List<string>(qw.InputDeviceIds),
            SignalRgbEffects = new List<string>(qw.SignalRgbEffects),
            MediaActions = new List<string>(qw.MediaActions ?? new()),
            ProfileNames = new List<string>(qw.ProfileNames ?? new()),
            ItemIcons = new Dictionary<string, WheelItemIcon>(qw.ItemIcons ?? new()),
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        var left = new StackPanel();
        body.Children.Add(left);

        var enableSw = MakeSwitch(state.Enabled, on => { state.Enabled = on; PaintWheelTab(state); QueueSave(); },
            out _, "Enable or disable this wheel (disabling releases the button's hold action)");
        StackPanel wrapper = null!;
        var remove = UiKit.IconButton(MaterialIconKind.DeleteOutline, "Remove wheel", _ =>
        {
            int at = WheelRowsPanel.Children.IndexOf(wrapper);
            WheelRowsPanel.Children.Remove(wrapper);
            var next = WheelStates().ElementAtOrDefault(Math.Max(0, at - 1));
            _selectedWheel = next;
            RenumberWheels();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }, danger: true);
        remove.Margin = new Thickness(8, 0, 0, 0);

        // On/off + remove live in the tab row (right side) while this wheel is selected.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(enableSw);
        actions.Children.Add(remove);
        state.Actions = actions;

        wrapper = new StackPanel { Tag = state };
        wrapper.Children.Add(body);

        // ── Trigger + Shows on one line ──
        var btnCombo = new ComboBox { MinWidth = 180, ToolTip = "Which hardware input opens this wheel (hold)" };
        PopulateTriggerCombo(btnCombo, qw.Device, qw.TriggerButton);
        btnCombo.SelectionChanged += (_, _) => { UpdateWheelSubtitle(state); if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
        state.TriggerCombo = btnCombo;

        var showsCombo = new ComboBox { MinWidth = 170, ToolTip = "What this wheel lets you pick" };
        foreach (var m in WheelModes) showsCombo.Items.Add(new ComboBoxItem { Content = m.label });
        showsCombo.SelectedIndex = WheelModeIndex(state.Mode);
        showsCombo.SelectionChanged += (_, _) =>
        {
            if (showsCombo.SelectedIndex < 0) return;
            state.Mode = WheelModes[showsCombo.SelectedIndex].mode;
            UpdateWheelSubtitle(state);
            PaintWheelTab(state);
            RefreshWheelModePanels(state);
            WheelChanged(state);
        };

        var fieldRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        fieldRow.Children.Add(MakeInlineField("Trigger", btnCombo, new Thickness(0, 0, 22, 6)));
        var showsField = (StackPanel)MakeInlineField("Shows", showsCombo, new Thickness(0, 0, 0, 6));
        state.ShowsHelp = UiKit.HelpTip("");
        state.ShowsHelp.Margin = new Thickness(6, 0, 0, 0);
        showsField.Children.Add(state.ShowsHelp);
        fieldRow.Children.Add(showsField);
        left.Children.Add(fieldRow);
        left.Children.Add(MakeDivider());

        // ── Custom slots (shown when Shows = Custom) ──
        var customPanel = new StackPanel();
        state.CustomPanel = customPanel;
        customPanel.Children.Add(MakeSlotHeader());
        var slotList = new StackPanel();
        state.SlotList = slotList;
        customPanel.Children.Add(slotList);
        foreach (var slot in qw.CustomSlots)
            AddCustomSlotRow(state, slot);

        var addSlotBtn = UiKit.LinkRow(MaterialIconKind.Plus, "Add slot", accent: true, () =>
        {
            if (state.Slots.Count >= WheelMaxItems) return;
            AddCustomSlotRow(state, new CustomWheelSlot());
            WheelChanged(state);
        }, $"Add an action slot (max {WheelMaxItems})");
        addSlotBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addSlotBtn.Margin = new Thickness(-8, 2, 0, 0);
        customPanel.Children.Add(addSlotBtn);
        left.Children.Add(customPanel);

        // ── Item checklist / hint for the other modes — rebuilt on mode change ──
        var itemsPanel = new StackPanel();
        state.ItemsPanel = itemsPanel;
        left.Children.Add(itemsPanel);

        // ── Live mini wheel ──
        var mini = BuildMiniWheel(state);
        Grid.SetColumn(mini, 2);
        body.Children.Add(mini);

        WheelRowsPanel.Children.Add(wrapper);
        // Loading keeps the first wheel selected; a user-added wheel opens straight away.
        if (_selectedWheel == null || !_loading) _selectedWheel = state;
        RenumberWheels();
        UpdateWheelSubtitle(state);
        RefreshWheelModePanels(state);
        RefreshMiniNow(state);
    }

    private FrameworkElement MakeInlineField(string label, FrameworkElement control, Thickness margin)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
        var lbl = MakeLabel(label, new Thickness(0, 0, 10, 0));
        lbl.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(lbl);
        control.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(control);
        return sp;
    }

    private Grid MakeSlotGrid()
    {
        var g = new Grid();
        foreach (var w in SlotColumns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    private Grid MakeSlotHeader()
    {
        var g = MakeSlotGrid();
        g.Margin = new Thickness(0, 0, 0, 4);
        string[] heads = { "", "ICON", "ACTION", "SETTING", "LABEL ON WHEEL" };
        for (int i = 1; i < heads.Length; i++)
        {
            var tb = MakeHint(heads[i], new Thickness(i == 1 ? 0 : 0, 0, 0, 0));
            tb.FontSize = 9.5;
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    /// <summary>Small live Halo wheel on the right of the card. Click it for the full-size preview.</summary>
    private FrameworkElement BuildMiniWheel(WheelRowState st)
    {
        var halo = new Controls.HaloWheel();
        halo.SegmentClicked += _ => PreviewWheel(st);
        halo.SegmentRightClicked += (idx, anchor) => ShowItemIconMenu(st, idx, anchor);
        st.Mini = halo;

        var stack = new StackPanel();
        stack.Children.Add(new Viewbox { Width = 166, Height = 166, Child = halo, Stretch = Stretch.Uniform });

        var frame = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Live preview. Click to open this wheel full size (picking an item runs nothing).\n" +
                      "Right-click an item to give it your own icon.",
            Background = new RadialGradientBrush(Color.FromRgb(0x18, 0x21, 0x2B), Color.FromRgb(0x0B, 0x0D, 0x10))
            {
                GradientOrigin = new Point(0.3, 0.2), Center = new Point(0.3, 0.2), RadiusX = 1, RadiusY = 1,
            },
            Child = stack,
        };
        frame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
        frame.MouseEnter += (_, _) => frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
        frame.MouseLeave += (_, _) => frame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
        frame.MouseLeftButtonUp += (_, _) => PreviewWheel(st);
        return frame;
    }

    /// <summary>
    /// Icon tile for one item in a wheel's list: shows the icon the wheel will draw.
    /// Click = choose an icon or image, right-click = back to the default.
    /// </summary>
    private FrameworkElement MakeItemIconTile(WheelRowState st, string id)
    {
        var tile = new System.Windows.Controls.Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = FindBrush("InputBgBrush"),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Paint()
        {
            var (symbol, color, image) = ResolveItemVisual(st, id);
            bool custom = st.ItemIcons.ContainsKey(id);
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(custom ? (byte)0xCC : (byte)0x55, color.R, color.G, color.B));
            tile.Child = image != null
                ? new Image { Source = image, Width = 16, Height = 16, Stretch = Stretch.Uniform }
                : new MaterialIcon
                {
                    Kind = Enum.TryParse<MaterialIconKind>(symbol, out var k) ? k : MaterialIconKind.CircleOutline,
                    Width = 16, Height = 16,
                    Foreground = new SolidColorBrush(color),
                };
            tile.ToolTip = custom
                ? "Your icon. Click to change, right-click to go back to the default."
                : "Change icon: click to pick an icon or upload an image";
        }
        Paint();

        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // don't toggle the row's checkbox
            string color = st.ItemIcons.TryGetValue(id, out var cur) ? cur.Color : "";
            PickWheelIcon(color, (icon, c) =>
            {
                st.ItemIcons[id] = new WheelItemIcon { Icon = icon, Color = c };
                Paint();
                WheelChanged(st);
            });
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!st.ItemIcons.Remove(id)) return;
            Paint();
            WheelChanged(st);
        };
        return WithEditBadge(tile);
    }

    /// <summary>
    /// Wrap an icon tile with a small pencil badge in its corner and an accent border on hover,
    /// so it reads as "click to change". The badge ignores the mouse; clicks reach the tile.
    /// </summary>
    private FrameworkElement WithEditBadge(System.Windows.Controls.Border tile)
    {
        var a = ThemeManager.Accent;
        var badge = new System.Windows.Controls.Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(a),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -5, -5),
            IsHitTestVisible = false,
            Child = new MaterialIcon { Kind = MaterialIconKind.Pencil, Width = 8, Height = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)) },
        };
        badge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBgBrush");

        Brush? restBorder = null;
        tile.MouseEnter += (_, _) =>
        {
            restBorder = tile.BorderBrush;
            tile.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
        };
        tile.MouseLeave += (_, _) => { if (restBorder != null) tile.BorderBrush = restBorder; };

        var wrap = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(tile);
        wrap.Children.Add(badge);
        return wrap;
    }

    /// <summary>What an item looks like on this wheel: its default, with the wheel's override on top.</summary>
    private (string symbol, Color color, ImageSource? image) ResolveItemVisual(WheelRowState st, string id)
    {
        var (symbol, color) = _config != null
            ? Services.QuickWheelContent.GetDefaultItemVisual(_config, st.Mode, id)
            : ("CircleOutline", Colors.Gray);
        if (!st.ItemIcons.TryGetValue(id, out var ov)) return (symbol, color, null);
        var (oSym, oColor, image) = Services.QuickWheelContent.GetSlotVisuals(new CustomWheelSlot { Icon = ov.Icon, Color = ov.Color });
        return (string.IsNullOrEmpty(ov.Icon) || image != null ? symbol : oSym,
            string.IsNullOrEmpty(ov.Color) ? color : oColor, image);
    }

    /// <summary>Open the icon picker; <paramref name="set"/> gets (icon or image path, color hex).</summary>
    private void PickWheelIcon(string currentColor, Action<string, string> set)
    {
        var dialog = new StreamControllerIconPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        if (!string.IsNullOrWhiteSpace(dialog.SelectedDownloadedImagePath))
            set(dialog.SelectedDownloadedImagePath!, currentColor);
        else if (!string.IsNullOrWhiteSpace(dialog.SelectedIconKind))
            set(dialog.SelectedIconKind!, dialog.SelectedAccent is Color a ? $"#{a.R:X2}{a.G:X2}{a.B:X2}" : currentColor);
    }

    /// <summary>Right-click menu on a mini wheel item: choose its icon, or go back to the default.</summary>
    private void ShowItemIconMenu(WheelRowState st, int index, FrameworkElement anchor)
    {
        if (_config == null) return;
        var items = Services.QuickWheelContent.Build(_config, BuildWheelConfig(st)).Items;
        if (index < 0 || index >= items.Count) return;
        string id = items[index].Id;

        // Custom wheels keep the icon on the slot (same as its row's icon tile)
        CustomSlotRowState? slot = null;
        if (st.Mode == QuickWheelMode.Custom
            && id.StartsWith(Services.QuickWheelContent.CustomSlotIdPrefix)
            && int.TryParse(id[Services.QuickWheelContent.CustomSlotIdPrefix.Length..], out int si)
            && si >= 0 && si < st.Slots.Count)
            slot = st.Slots[si];
        if (st.Mode == QuickWheelMode.Custom && slot == null) return;

        bool hasOverride = slot != null
            ? !string.IsNullOrEmpty(slot.Icon) || !string.IsNullOrEmpty(slot.Color)
            : st.ItemIcons.ContainsKey(id);

        void Set(string icon, string color)
        {
            if (slot != null)
            {
                slot.Icon = icon;
                slot.Color = color;
                slot.RefreshIcon();
            }
            else
            {
                if (string.IsNullOrEmpty(icon) && string.IsNullOrEmpty(color))
                    st.ItemIcons.Remove(id);
                else
                    st.ItemIcons[id] = new WheelItemIcon { Icon = icon, Color = color };
                RefreshWheelModePanels(st); // redraw the list's icon tiles
            }
            WheelChanged(st);
        }

        var menu = new List<GlassMenuItem>
        {
            new($"Choose icon for \"{items[index].Label}\"…", MaterialIconKind.ImageEditOutline, () =>
                PickWheelIcon(slot?.Color ?? (st.ItemIcons.TryGetValue(id, out var cur) ? cur.Color : ""), Set)),
            new("Use default icon", MaterialIconKind.Restore, () => Set("", ""), IsEnabled: hasOverride),
        };
        GlassContextMenuHost.Show(anchor, menu);
    }

    /// <summary>Something on the card changed: save (debounced) and redraw the mini wheel.</summary>
    private void WheelChanged(WheelRowState st)
    {
        QueueSave();
        st.MiniTimer ??= CreateMiniTimer(st);
        st.MiniTimer.Stop();
        st.MiniTimer.Start();
    }

    private DispatcherTimer CreateMiniTimer(WheelRowState st)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) => { t.Stop(); RefreshMiniNow(st); };
        return t;
    }

    private void RefreshMiniNow(WheelRowState st)
    {
        if (_config == null || st.Mini == null) return;
        st.Mini.ApplyStyle(_config.Osd);
        st.Mini.SetContent(Services.QuickWheelContent.Build(_config, BuildWheelConfig(st)));
    }

    private static void UpdateWheelSubtitle(WheelRowState st)
    {
        if (st.Subtitle == null) return;
        string trig = (st.TriggerCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string mode = WheelModes[WheelModeIndex(st.Mode)].label;
        st.Subtitle.Text = string.IsNullOrEmpty(trig) ? mode : $"Hold {trig}  ·  {mode}";
        st.Subtitle.Visibility = Visibility.Visible;
    }

    private IEnumerable<WheelRowState> WheelStates() =>
        WheelRowsPanel.Children.OfType<FrameworkElement>().Select(fe => fe.Tag).OfType<WheelRowState>();

    /// <summary>Rebuild the tab strip (one tab per wheel, then "+ Add") and show only the selected wheel.</summary>
    private void RenumberWheels()
    {
        var states = WheelStates().ToList();
        if (_selectedWheel != null && !states.Contains(_selectedWheel)) _selectedWheel = null;
        _selectedWheel ??= states.FirstOrDefault();

        _wheelTabs.Children.Clear();
        for (int i = 0; i < states.Count; i++)
        {
            var st = states[i];
            int number = i + 1;
            st.Tab = MakeWheelTab(WheelTabName(st, number), out st.TabLabel, out st.TabIcon,
                () => SelectWheel(st), onRename: () => BeginWheelRename(st, number));
            st.TabPencil = ((StackPanel)st.Tab.Child).Children.OfType<System.Windows.Controls.Border>().First();
            _wheelTabs.Children.Add(st.Tab);
            PaintWheelTab(st);
        }
        var add = MakeWheelTab("Add", out _, out var addIcon, () =>
        {
            AddWheelRow(new QuickWheelConfig { Enabled = true });
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }, isAdd: true);
        addIcon.Kind = MaterialIconKind.Plus;
        add.ToolTip = "Add another Quick Wheel";
        _wheelTabs.Children.Add(add);

        foreach (var child in WheelRowsPanel.Children.OfType<FrameworkElement>())
            child.Visibility = Vis(child.Tag == _selectedWheel);
        _wheelEmpty.Visibility = Vis(states.Count == 0);
        ShowSelectedWheelActions();
    }

    /// <summary>Put the selected wheel's on/off switch + remove button in the tab row.</summary>
    private void ShowSelectedWheelActions()
    {
        var actions = _selectedWheel?.Actions;
        if (actions != null && actions.Parent is System.Windows.Controls.Border oldHost && oldHost != _wheelActionsHost)
            oldHost.Child = null;
        _wheelActionsHost.Child = actions;
    }

    private static string WheelTabName(WheelRowState st, int number) =>
        string.IsNullOrWhiteSpace(st.Name) ? $"Wheel {number}" : st.Name;

    private static MaterialIconKind WheelModeIcon(QuickWheelMode mode) => mode switch
    {
        QuickWheelMode.Profile => MaterialIconKind.AccountSwitchOutline,
        QuickWheelMode.OutputDevice => MaterialIconKind.Speaker,
        QuickWheelMode.InputDevice => MaterialIconKind.Microphone,
        QuickWheelMode.MediaControls => MaterialIconKind.PlayPause,
        QuickWheelMode.SignalRgbEffect => MaterialIconKind.Palette,
        _ => MaterialIconKind.ViewGridPlusOutline,
    };

    private void SelectWheel(WheelRowState st)
    {
        if (_selectedWheel == st) return;
        _selectedWheel = st;
        foreach (var child in WheelRowsPanel.Children.OfType<FrameworkElement>())
            child.Visibility = Vis(child.Tag == st);
        foreach (var other in WheelStates()) PaintWheelTab(other);
        ShowSelectedWheelActions();
    }

    /// <summary>
    /// Underline tab: small mode icon + label, 2px wheel-colour underline when selected.
    /// Double-clicking a wheel tab renames it in place.
    /// </summary>
    private System.Windows.Controls.Border MakeWheelTab(string text, out TextBlock label, out MaterialIcon icon,
        Action onClick, Action? onRename = null, bool isAdd = false)
    {
        icon = new MaterialIcon { Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        string restKey = isAdd ? "AccentBrush" : "TextDimBrush";
        label.SetResourceReference(TextBlock.ForegroundProperty, restKey);
        if (isAdd) icon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(label);
        if (onRename != null)
        {
            // Small pencil right after the name — shown on the selected tab only.
            var pencilIcon = new MaterialIcon { Kind = MaterialIconKind.PencilOutline, Width = 12, Height = 12 };
            pencilIcon.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
            var pencil = new System.Windows.Controls.Border
            {
                Child = pencilIcon, Padding = new Thickness(3), Margin = new Thickness(5, 0, 0, 0),
                CornerRadius = new CornerRadius(4), Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Rename",
                Visibility = Visibility.Collapsed,
            };
            pencil.MouseEnter += (_, _) => pencilIcon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
            pencil.MouseLeave += (_, _) => pencilIcon.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
            pencil.MouseLeftButtonUp += (_, e) => { e.Handled = true; onRename(); };
            row.Children.Add(pencil);
        }
        var tab = new System.Windows.Controls.Border
        {
            Padding = new Thickness(2, 8, 2, 8),
            Margin = new Thickness(0, 0, 22, -1),
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = row,
        };
        // Hover brightens unselected tabs only (a selected tab has a coloured underline).
        var lbl = label;
        bool Unselected() => tab.BorderBrush == Brushes.Transparent;
        tab.MouseEnter += (_, _) => { if (Unselected() && !isAdd) lbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush"); if (isAdd) row.Opacity = 0.8; };
        tab.MouseLeave += (_, _) => { if (Unselected()) lbl.SetResourceReference(TextBlock.ForegroundProperty, restKey); row.Opacity = 1; };
        tab.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && onRename != null) { e.Handled = true; onRename(); }
        };
        tab.MouseLeftButtonUp += (_, e) => { if (e.Source is TextBox) return; e.Handled = true; onClick(); };
        return tab;
    }

    private void PaintWheelTab(WheelRowState st)
    {
        if (st.Tab == null) return;
        bool on = st == _selectedWheel;
        st.Tab.BorderBrush = on ? new SolidColorBrush(WheelColor) : Brushes.Transparent;
        if (st.TabPencil != null) st.TabPencil.Visibility = Vis(on);
        st.TabLabel.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextPrimaryBrush" : "TextDimBrush");
        st.TabIcon.Kind = WheelModeIcon(st.Mode);
        st.TabIcon.Foreground = st.Enabled ? new SolidColorBrush(WheelColor) : (Brush)FindResource("TextDimBrush");
        st.TabIcon.Opacity = st.Enabled ? 1 : 0.5;
        string mode = WheelModes[WheelModeIndex(st.Mode)].label;
        st.Tab.ToolTip = $"{mode}{(st.Enabled ? "" : " · turned off")}\nDouble-click to rename";
    }

    /// <summary>Swap the tab label for a text box. Enter or clicking away saves, Esc cancels, empty = default name.</summary>
    private void BeginWheelRename(WheelRowState st, int number)
    {
        if (st.Tab?.Child is not StackPanel row || row.Children.OfType<TextBox>().Any()) return;
        SelectWheel(st);
        var box = new TextBox
        {
            Text = WheelTabName(st, number),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 70,
            MaxLength = 24,
            Padding = new Thickness(2, 0, 2, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        box.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        box.SetResourceReference(TextBox.BorderBrushProperty, "AccentBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        st.TabLabel.Visibility = Visibility.Collapsed;
        if (st.TabPencil != null) st.TabPencil.Visibility = Visibility.Collapsed;
        row.Children.Insert(row.Children.IndexOf(st.TabLabel) + 1, box);

        bool done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            if (save)
            {
                var name = box.Text.Trim();
                st.Name = name == $"Wheel {number}" ? "" : name;
                QueueSave();
            }
            row.Children.Remove(box);
            st.TabLabel.Text = WheelTabName(st, number);
            st.TabLabel.Visibility = Visibility.Visible;
            PaintWheelTab(st);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    /// <summary>Show the slot editor or the item checklist / hint that matches the wheel's mode.</summary>
    private void RefreshWheelModePanels(WheelRowState st)
    {
        st.CustomPanel.Visibility = st.Mode == QuickWheelMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        st.ItemsPanel.Children.Clear();
        st.ShowsHelp.ToolTip = UiKit.HelpToolTip(WheelModeHelp(st.Mode), WheelModes[WheelModeIndex(st.Mode)].label);

        switch (st.Mode)
        {
            case QuickWheelMode.OutputDevice:
                BuildWheelItemChecklist(st, st.OutputDeviceIds,
                    GetAudioDevices(NAudio.CoreAudioApi.DataFlow.Render),
                    GetDefaultDeviceId(NAudio.CoreAudioApi.DataFlow.Render), "devices");
                break;
            case QuickWheelMode.InputDevice:
                BuildWheelItemChecklist(st, st.InputDeviceIds,
                    GetAudioDevices(NAudio.CoreAudioApi.DataFlow.Capture),
                    GetDefaultDeviceId(NAudio.CoreAudioApi.DataFlow.Capture), "mics");
                break;
            case QuickWheelMode.SignalRgbEffect:
            {
                var effects = GetSignalRgbEffectNames();
                if (effects.Count == 0 && st.SignalRgbEffects.Count == 0)
                {
                    st.ItemsPanel.Children.Add(MakeWarning(
                        "SignalRGB isn't installed on this PC, so there are no effects to pick. Install SignalRGB and reopen this page."));
                    break;
                }
                BuildWheelItemChecklist(st, st.SignalRgbEffects,
                    effects.Select(n => (n, n)).ToList(),
                    AmpUp.Services.SignalRgbEffectCatalog.LastAppliedEffectName, "effects");
                break;
            }
            case QuickWheelMode.MediaControls:
                BuildWheelItemChecklist(st, st.MediaActions,
                    Services.QuickWheelContent.MediaControls.Select(m => (m.id, m.label)).ToList(), "", "media controls");
                break;
            case QuickWheelMode.Profile:
                BuildWheelItemChecklist(st, st.ProfileNames,
                    (_config?.Profiles ?? new List<string>()).Select(p => (p, p)).ToList(),
                    _config?.ActiveProfile ?? "", "profiles");
                break;
        }
        st.ItemsPanel.Visibility = st.ItemsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string WheelModeHelp(QuickWheelMode mode) => mode switch
    {
        QuickWheelMode.OutputDevice => $"Tick the output devices to show (up to {WheelMaxItems}). The number is their order on the wheel. Tick none to show the first {WheelMaxItems}.\n\nClick an icon to change it.",
        QuickWheelMode.InputDevice => $"Tick the mics to show (up to {WheelMaxItems}). The number is their order on the wheel. Tick none to show the first {WheelMaxItems}.\n\nClick an icon to change it.",
        QuickWheelMode.SignalRgbEffect => $"Tick the SignalRGB effects to show (up to {WheelMaxItems}). The number is their order on the wheel. Tick none to show the first {WheelMaxItems}.\n\nClick an icon to change it.",
        QuickWheelMode.MediaControls => "Tick the media controls to show. The number is their order on the wheel. Tick none to show all of them.\n\nClick an icon to change it.",
        QuickWheelMode.Custom => $"Each slot is one item on the wheel (up to {WheelMaxItems}). Pick an action, fill in its setting if it has one, and type the name to show on the wheel.",
        _ => $"Tick the profiles to show (up to {WheelMaxItems}). The number is their order on the wheel. Tick none to show every profile. The one you're using now is marked ACTIVE.\n\nClick an icon to change it.",
    };

    private static List<(string id, string name)> GetAudioDevices(NAudio.CoreAudioApi.DataFlow flow)
    {
        var list = new List<(string id, string name)>();
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(flow, NAudio.CoreAudioApi.DeviceState.Active);
            for (int i = 0; i < devices.Count; i++)
            {
                using var d = devices[i];
                list.Add((d.ID, d.FriendlyName));
            }
        }
        catch (Exception ex) { Logger.Log($"OSD wheel device list error: {ex.Message}"); }
        return list;
    }

    private static string GetDefaultDeviceId(NAudio.CoreAudioApi.DataFlow flow)
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var d = enumerator.GetDefaultAudioEndpoint(flow, NAudio.CoreAudioApi.Role.Multimedia);
            return d.ID;
        }
        catch { return ""; }
    }

    private static List<string> GetSignalRgbEffectNames()
    {
        try { return AmpUp.Services.SignalRgbEffectCatalog.GetInstalledEffects().Select(e => e.Name).ToList(); }
        catch (Exception ex) { Logger.Log($"OSD wheel SignalRGB list error: {ex.Message}"); return new(); }
    }

    private static readonly Color WarnColor = Color.FromRgb(0xFF, 0xB8, 0x00);

    private FrameworkElement MakeWarning(string text, FrameworkElement? extra = null)
    {
        var row = new DockPanel();
        var icon = new Material.Icons.WPF.MaterialIcon
        {
            Kind = MaterialIconKind.AlertOutline, Width = 14, Height = 14,
            Foreground = new SolidColorBrush(WarnColor), Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (extra != null)
        {
            DockPanel.SetDock(extra, Dock.Right);
            extra.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(extra);
        }
        row.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCF, 0x66)),
        });
        return new System.Windows.Controls.Border
        {
            Child = row,
            Padding = new Thickness(8, 4, 6, 4),
            MinHeight = 30,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, WarnColor.R, WarnColor.G, WarnColor.B)),
            Background = new SolidColorBrush(Color.FromArgb(0x12, WarnColor.R, WarnColor.G, WarnColor.B)),
        };
    }

    /// <summary>
    /// Checklist of available items; <paramref name="picked"/> is edited in place and keeps
    /// check order (= wheel order, shown as a number). Picked items that aren't available right
    /// now (unplugged device, removed effect) stay listed so they aren't silently dropped.
    /// </summary>
    private void BuildWheelItemChecklist(WheelRowState st, List<string> picked,
        List<(string id, string name)> available, string inUseId, string noun)
    {
        var host = st.ItemsPanel;

        var items = new List<(string id, string name, bool missing)>();
        foreach (var id in picked)
        {
            var match = available.FirstOrDefault(a => a.id == id);
            items.Add(match.id != null ? (id, match.name, false) : (id, id, true));
        }
        foreach (var a in available)
            if (!picked.Contains(a.id)) items.Add((a.id, a.name, false));

        if (items.Count == 0)
        {
            host.Children.Add(MakeHint($"No {noun} found on this PC.", new Thickness(0, 0, 0, 4)));
            return;
        }

        var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        hintRow.Children.Add(new MaterialIcon { Kind = MaterialIconKind.Pencil, Width = 11, Height = 11,
            Foreground = new SolidColorBrush(ThemeManager.Accent), Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center });
        hintRow.Children.Add(MakeHint("Tick items to show them on the wheel. Click an icon to change it.", new Thickness(0)));
        host.Children.Add(hintRow);

        var rows = new List<(CheckBox box, TextBlock order, string id)>();
        void Refresh()
        {
            foreach (var (box, order, id) in rows)
            {
                int at = picked.IndexOf(id);
                order.Text = at >= 0 ? (at + 1).ToString() : "";
                box.IsEnabled = at >= 0 || picked.Count < WheelMaxItems;
            }
        }

        var list = new StackPanel();
        foreach (var (id, name, missing) in items)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox { IsChecked = picked.Contains(id), VerticalAlignment = VerticalAlignment.Center };
            var order = new TextBlock
            {
                FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ThemeManager.Accent),
            };
            var label = new TextBlock
            {
                Text = missing ? $"{name}  (not available)" : name, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 8, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, missing ? "TextDimBrush" : "TextPrimaryBrush");
            var iconTile = MakeItemIconTile(st, id);
            Grid.SetColumn(order, 1);
            Grid.SetColumn(iconTile, 2);
            Grid.SetColumn(label, 3);
            g.Children.Add(cb);
            g.Children.Add(order);
            g.Children.Add(iconTile);
            g.Children.Add(label);

            if (!string.IsNullOrEmpty(inUseId) && string.Equals(id, inUseId, StringComparison.OrdinalIgnoreCase))
            {
                var a = ThemeManager.Accent;
                var chip = new System.Windows.Controls.Border
                {
                    CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6, 0, 6, 1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, a.R, a.G, a.B)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "In use", FontSize = 10, Foreground = new SolidColorBrush(a) },
                };
                Grid.SetColumn(chip, 4);
                g.Children.Add(chip);
            }

            // Click anywhere on the row to toggle
            g.Cursor = Cursors.Hand;
            g.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox && cb.IsEnabled) cb.IsChecked = cb.IsChecked != true; };
            cb.Checked += (_, _) => { if (!picked.Contains(id)) picked.Add(id); Refresh(); WheelChanged(st); };
            cb.Unchecked += (_, _) => { picked.Remove(id); Refresh(); WheelChanged(st); };
            rows.Add((cb, order, id));
            list.Children.Add(g);
        }

        host.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Refresh();
    }

    private void AddCustomSlotRow(WheelRowState st, CustomWheelSlot slot)
    {
        var slotState = new CustomSlotRowState();
        var row = MakeSlotGrid();
        row.Margin = new Thickness(0, 0, 0, 6);
        slotState.Row = row;

        var num = MakeHint("", new Thickness(0, 0, 6, 0));
        num.TextAlignment = TextAlignment.Right;
        num.VerticalAlignment = VerticalAlignment.Center;
        slotState.Number = num;
        row.Children.Add(num);

        var actionCombo = new ComboBox { Margin = new Thickness(0, 0, 8, 0), ToolTip = "What this slot does" };
        int selectedIdx = -1;
        for (int i = 0; i < CustomSlotActions.Length; i++)
        {
            actionCombo.Items.Add(new ComboBoxItem { Content = CustomSlotActions[i].label, Tag = CustomSlotActions[i].id });
            if (CustomSlotActions[i].id == slot.ActionId) selectedIdx = i;
        }
        actionCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
        slotState.Action = actionCombo;
        Grid.SetColumn(actionCombo, 2);
        row.Children.Add(actionCombo);

        // Setting cell (column 2) is rebuilt when the action changes
        var settingHost = new ContentControl { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(settingHost, 3);
        row.Children.Add(settingHost);

        var labelBox = new TextBox
        {
            Text = string.IsNullOrEmpty(slot.Label) && selectedIdx >= 0 ? CustomSlotActions[selectedIdx].label : slot.Label,
            ToolTip = "Name shown on the wheel",
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 4, 0),
        };
        labelBox.TextChanged += (_, _) => WheelChanged(st);
        slotState.Label = labelBox;
        Grid.SetColumn(labelBox, 4);
        row.Children.Add(labelBox);

        var removeSlotBtn = UiKit.IconButton(MaterialIconKind.Close, "Remove this slot", _ =>
        {
            st.SlotList.Children.Remove(row);
            st.Slots.Remove(slotState);
            RenumberSlots(st);
            WheelChanged(st);
        }, danger: true, size: 24);
        Grid.SetColumn(removeSlotBtn, 5);
        row.Children.Add(removeSlotBtn);

        // Param values survive switching the action back and forth while editing
        string path = slot.Path, keys = slot.MacroKeys, profile = slot.ProfileName, device = slot.DeviceId;
        slotState.GetPath = () => path;
        slotState.GetKeys = () => keys;
        slotState.GetProfile = () => profile;
        slotState.GetDevice = () => device;
        slotState.Icon = slot.Icon;
        slotState.Color = slot.Color;

        // Icon tile (column 1): shows what the wheel will draw. Click = pick, right-click = automatic.
        var iconTile = new System.Windows.Controls.Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("InputBgBrush"),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void RefreshIcon()
        {
            var (symbol, color, image) = Services.QuickWheelContent.GetSlotVisuals(new CustomWheelSlot
            {
                ActionId = slotState.ActionId, Path = path, Icon = slotState.Icon, Color = slotState.Color,
            });
            iconTile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
            iconTile.Child = image != null
                ? new Image { Source = image, Width = 20, Height = 20, Stretch = Stretch.Uniform }
                : new MaterialIcon
                {
                    Kind = Enum.TryParse<MaterialIconKind>(symbol, out var k) ? k : MaterialIconKind.CircleOutline,
                    Width = 20, Height = 20,
                    Foreground = new SolidColorBrush(color),
                };
            iconTile.ToolTip = string.IsNullOrEmpty(slotState.Icon) && string.IsNullOrEmpty(slotState.Color)
                ? "Change icon: click to pick an icon or upload an image (automatic for now)"
                : "Icon on the wheel. Click to change, right-click to go back to automatic.";
        }
        iconTile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var dialog = new StreamControllerIconPickerDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            if (!string.IsNullOrWhiteSpace(dialog.SelectedDownloadedImagePath))
            {
                slotState.Icon = dialog.SelectedDownloadedImagePath!;
            }
            else if (!string.IsNullOrWhiteSpace(dialog.SelectedIconKind))
            {
                slotState.Icon = dialog.SelectedIconKind!;
                if (dialog.SelectedAccent is Color a)
                    slotState.Color = $"#{a.R:X2}{a.G:X2}{a.B:X2}";
            }
            else return;
            RefreshIcon();
            WheelChanged(st);
        };
        iconTile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrEmpty(slotState.Icon) && string.IsNullOrEmpty(slotState.Color)) return;
            slotState.Icon = "";
            slotState.Color = "";
            RefreshIcon();
            WheelChanged(st);
        };
        var iconWithBadge = WithEditBadge(iconTile);
        Grid.SetColumn(iconWithBadge, 1);
        row.Children.Add(iconWithBadge);

        void BuildSetting()
        {
            switch (slotState.ActionId)
            {
                case "macro":
                {
                    var tb = new TextBox { Text = keys, ToolTip = "Key combo, e.g. win+shift+s or ctrl+alt+m", VerticalContentAlignment = VerticalAlignment.Center };
                    tb.TextChanged += (_, _) => { keys = tb.Text; WheelChanged(st); };
                    settingHost.Content = tb;
                    break;
                }
                case "open_url":
                {
                    var tb = new TextBox { Text = path, ToolTip = "Web address to open, e.g. https://example.com", VerticalContentAlignment = VerticalAlignment.Center };
                    tb.TextChanged += (_, _) => { path = tb.Text; WheelChanged(st); };
                    settingHost.Content = tb;
                    break;
                }
                case "launch_exe":
                {
                    var picker = new AppPathPicker();
                    picker.SetValue(path);
                    picker.ValuePicked += v => { path = v; RefreshIcon(); WheelChanged(st); };
                    settingHost.Content = picker;
                    break;
                }
                case "switch_profile":
                    settingHost.Content = MakeSlotCombo(_config?.Profiles ?? new List<string>(), profile,
                        v => { profile = v; WheelChanged(st); });
                    break;
                case "select_output" or "mute_device" or "select_input" or "mute_input_device":
                {
                    var flow = slotState.ActionId is "select_input" or "mute_input_device"
                        ? NAudio.CoreAudioApi.DataFlow.Capture : NAudio.CoreAudioApi.DataFlow.Render;
                    settingHost.Content = MakeSlotDeviceCombo(GetAudioDevices(flow), device, (id, name) =>
                    {
                        device = id;
                        // Name the slot after the device while the label is still an action name
                        if (string.IsNullOrEmpty(labelBox.Text) || CustomSlotActions.Any(a => a.label == labelBox.Text))
                            labelBox.Text = name;
                        WheelChanged(st);
                    });
                    break;
                }
                case "signalrgb_effect":
                {
                    var effects = GetSignalRgbEffectNames();
                    if (effects.Count == 0)
                    {
                        // No SignalRGB on this PC: say so, and still let them type an effect name
                        var tb = new TextBox { Text = path, Width = 110, ToolTip = "Effect name, e.g. Aurora", VerticalContentAlignment = VerticalAlignment.Center };
                        tb.TextChanged += (_, _) => { path = tb.Text; WheelChanged(st); };
                        settingHost.Content = MakeWarning("SignalRGB not installed. Effect:", tb);
                    }
                    else
                    {
                        settingHost.Content = MakeSlotCombo(effects, path, v => { path = v; WheelChanged(st); });
                    }
                    break;
                }
                default:
                {
                    var none = MakeHint("No settings", new Thickness(4, 0, 0, 0));
                    none.VerticalAlignment = VerticalAlignment.Center;
                    settingHost.Content = none;
                    break;
                }
            }
        }

        string prevAction = slotState.ActionId;
        actionCombo.SelectionChanged += (_, _) =>
        {
            // Path is shared by app / URL / effect — don't carry an exe path into a URL box
            if (slotState.ActionId != prevAction) path = "";
            static bool IsInputAction(string a) => a is "select_input" or "mute_input_device";
            if (IsInputAction(slotState.ActionId) != IsInputAction(prevAction)) device = "";
            prevAction = slotState.ActionId;
            // Auto-fill label if it was empty or matched an action name
            if (actionCombo.SelectedItem is ComboBoxItem ci
                && (string.IsNullOrEmpty(labelBox.Text) || CustomSlotActions.Any(a => a.label == labelBox.Text)))
                labelBox.Text = ci.Content?.ToString() ?? "";
            BuildSetting();
            RefreshIcon();
            WheelChanged(st);
        };
        BuildSetting();
        RefreshIcon();
        slotState.RefreshIcon = RefreshIcon;

        st.Slots.Add(slotState);
        st.SlotList.Children.Add(row);
        RenumberSlots(st);
    }

    private static void RenumberSlots(WheelRowState st)
    {
        for (int i = 0; i < st.Slots.Count; i++)
            st.Slots[i].Number.Text = (i + 1).ToString();
    }

    /// <summary>
    /// Device dropdown for a slot (outputs or inputs). A saved device that's unplugged right now stays
    /// listed as "not connected" instead of being dropped. Picks the first device when none is saved.
    /// </summary>
    private ComboBox MakeSlotDeviceCombo(List<(string id, string name)> devices, string selectedId, Action<string, string> onPick)
    {
        var combo = new ComboBox { ToolTip = "Which device" };
        var all = new List<(string id, string name)>(devices);
        if (!string.IsNullOrEmpty(selectedId) && all.All(d => d.id != selectedId))
            all.Insert(0, (selectedId, "Saved device (not connected)"));
        if (all.Count == 0)
        {
            combo.Items.Add(new ComboBoxItem { Content = "No devices found", IsEnabled = false });
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return combo;
        }
        foreach (var (id, name) in all) combo.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        int at = all.FindIndex(d => d.id == selectedId);
        combo.SelectedIndex = Math.Max(0, at);
        if (at < 0) onPick(all[0].id, all[0].name); // save the shown default
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is string id) onPick(id, ci.Content?.ToString() ?? "");
        };
        return combo;
    }

    private ComboBox MakeSlotCombo(List<string> options, string selected, Action<string> onPick)
    {
        var combo = new ComboBox();
        // Keep a saved value that isn't currently available rather than dropping it
        var all = new List<string>(options);
        if (!string.IsNullOrEmpty(selected) && !all.Contains(selected)) all.Insert(0, selected);
        if (all.Count == 0)
        {
            combo.Items.Add(new ComboBoxItem { Content = "None available", IsEnabled = false });
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return combo;
        }
        foreach (var o in all) combo.Items.Add(new ComboBoxItem { Content = o, Tag = o });
        combo.SelectedIndex = Math.Max(0, all.IndexOf(selected));
        // Store the shown default so the slot isn't saved with an empty value
        if (string.IsNullOrEmpty(selected)) onPick(all[0]);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is string v) onPick(v);
        };
        return combo;
    }

    private List<QuickWheelConfig> CollectWheelConfigs()
    {
        var list = new List<QuickWheelConfig>();
        foreach (var child in WheelRowsPanel.Children)
            if (child is FrameworkElement fe && fe.Tag is WheelRowState st)
                list.Add(BuildWheelConfig(st));
        return list;
    }

    /// <summary>Read one wheel card's current (possibly unsaved) state into a config.</summary>
    private static QuickWheelConfig BuildWheelConfig(WheelRowState st)
    {
        // Trigger device + local index live in the selected item's Tag,
        // set up by PopulateTriggerCombo. Fall back to Turn Up button 0.
        var device = QuickWheelDevice.TurnUp;
        int triggerIdx = 0;
        if (st.TriggerCombo.SelectedItem is ComboBoxItem trigCi && trigCi.Tag is ValueTuple<QuickWheelDevice, int> tag)
        {
            device = tag.Item1;
            triggerIdx = tag.Item2;
        }

        // Every mode's contents are kept, so switching modes doesn't wipe the others.
        var cfg = new QuickWheelConfig
        {
            Enabled = st.Enabled,
            Name = st.Name,
            Mode = st.Mode,
            Device = device,
            TriggerButton = triggerIdx,
            TriggerGesture = "hold",
            ItemIcons = new Dictionary<string, WheelItemIcon>(st.ItemIcons),
            OutputDeviceIds = new List<string>(st.OutputDeviceIds),
            InputDeviceIds = new List<string>(st.InputDeviceIds),
            SignalRgbEffects = new List<string>(st.SignalRgbEffects),
            MediaActions = new List<string>(st.MediaActions),
            ProfileNames = new List<string>(st.ProfileNames),
        };

        foreach (var slot in st.Slots)
        {
            string actionId = slot.ActionId;
            cfg.CustomSlots.Add(new CustomWheelSlot
            {
                ActionId = actionId,
                Label = slot.Label.Text ?? "",
                // Only keep the parameter the chosen action actually uses
                Path = actionId is "launch_exe" or "open_url" or "signalrgb_effect" ? slot.GetPath() : "",
                MacroKeys = actionId == "macro" ? slot.GetKeys() : "",
                ProfileName = actionId == "switch_profile" ? slot.GetProfile() : "",
                DeviceId = actionId is "select_output" or "select_input" or "mute_device" or "mute_input_device" ? slot.GetDevice() : "",
                Icon = slot.Icon,
                Color = slot.Color,
            });
        }
        return cfg;
    }

    private RadialWheelOverlay? _previewWheel;

    /// <summary>
    /// Open the real wheel overlay full size with this card's current items (from the mini wheel).
    /// Hover, arrow keys or the mouse wheel move the highlight; picking an item only closes it.
    /// </summary>
    private void PreviewWheel(WheelRowState st)
    {
        if (_config == null) return;
        _previewWheel?.Dismiss();

        var content = Services.QuickWheelContent.Build(_config, BuildWheelConfig(st));
        if (content.Items.Count == 0) return; // the mini wheel already shows "Nothing to show"

        var wheel = new RadialWheelOverlay();
        wheel.SetMonitor(DisplayMonitorResolver.ResolveOsdMonitorIndex(_config.Osd));
        wheel.ApplyStyle(_config.Osd);
        wheel.SetContent(content);
        wheel.OnSegmentClicked = _ => { }; // preview only: picking an item runs nothing
        wheel.Deactivated += (_, _) => wheel.Dismiss(); // click anywhere else to close
        wheel.Closed += (_, _) => { if (_previewWheel == wheel) _previewWheel = null; };
        _previewWheel = wheel;
        wheel.Show();
        wheel.Activate();
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

    private System.Windows.Controls.Border? _osdMiniCard;
    private Action? _paintOsdMini;

    /// <summary>
    /// Small monitor with a scaled-down volume overlay sitting in the chosen corner.
    /// Follows the Position picker and accent; click it for a real overlay on screen.
    /// </summary>
    private FrameworkElement BuildOsdMiniPreview()
    {
        var screen = new Grid { ClipToBounds = true };
        var monitor = new System.Windows.Controls.Border
        {
            Width = 264, Height = 150,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0B)),
            Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x2A, 0x3A), Color.FromRgb(0x0B, 0x10, 0x18), 60),
            Cursor = Cursors.Hand,
            ToolTip = "Click to show a sample overlay on your screen",
            Child = screen,
        };
        // Faint "window" so it reads as a desktop
        screen.Children.Add(new System.Windows.Controls.Border
        {
            Width = 120, Height = 64, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });

        // Mini overlay: label, icon + name + value, level bar
        var catLabel = new TextBlock { Text = "VOLUME", FontSize = 5.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
        var icon = new MaterialIcon { Kind = MaterialIconKind.VolumeHigh, Width = 9, Height = 9, Margin = new Thickness(0, 0, 4, 0) };
        var name = new TextBlock { Text = "Master", FontSize = 7.5, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)), VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = "75%", FontSize = 8.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var mid = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        mid.Children.Add(icon);
        mid.Children.Add(value);
        mid.Children.Add(name);
        var track = new System.Windows.Controls.Border { Height = 2.5, CornerRadius = new CornerRadius(1.25) };
        var fill = new System.Windows.Controls.Border { Height = 2.5, CornerRadius = new CornerRadius(1.25),
            HorizontalAlignment = HorizontalAlignment.Left, Width = 62 };
        var bar = new Grid();
        bar.Children.Add(track);
        bar.Children.Add(fill);
        var body = new StackPanel();
        body.Children.Add(catLabel);
        body.Children.Add(mid);
        body.Children.Add(bar);

        _osdMiniCard = new System.Windows.Controls.Border
        {
            Width = 96,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(0.75),
            Padding = new Thickness(7, 5, 7, 6),
            Margin = new Thickness(8),
            Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x14, 0x14), Color.FromRgb(0x0F, 0x0F, 0x0F), 45),
            Child = body,
        };
        screen.Children.Add(_osdMiniCard);

        _paintOsdMini = () =>
        {
            var a = ThemeManager.Accent;
            _osdMiniCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, a.R, a.G, a.B));
            _osdMiniCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = a, BlurRadius = 10, Opacity = 0.35, ShadowDepth = 0 };
            catLabel.Foreground = new SolidColorBrush(Color.FromArgb(0x88, a.R, a.G, a.B));
            icon.Foreground = new SolidColorBrush(a);
            value.Foreground = new SolidColorBrush(a);
            track.Background = new SolidColorBrush(Color.FromArgb(0x22, a.R, a.G, a.B));
            fill.Background = new SolidColorBrush(a);
        };
        _paintOsdMini();
        _accentRepaints.Add(_paintOsdMini);
        PlaceOsdMini(_currentPosition);

        monitor.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnOsdPreview(this, new RoutedEventArgs()); };

        var stack = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(monitor);
        // Stand
        stack.Children.Add(new System.Windows.Controls.Border { Width = 34, Height = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0B)), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new System.Windows.Controls.Border { Width = 86, Height = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0B)), HorizontalAlignment = HorizontalAlignment.Center });
        var cap = MakeHint("Click to preview on your screen", new Thickness(0, 8, 0, 0));
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(cap);
        return stack;
    }

    private void PlaceOsdMini(OsdPosition pos)
    {
        if (_osdMiniCard == null) return;
        string p = pos.ToString();
        _osdMiniCard.VerticalAlignment = p.StartsWith("Top") ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _osdMiniCard.HorizontalAlignment = p.EndsWith("Left") ? HorizontalAlignment.Left
            : p.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center;
    }

    private void HighlightOsdPosition(OsdPosition active)
    {
        _currentPosition = active;
        PlaceOsdMini(active);
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
