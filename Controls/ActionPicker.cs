using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Searchable, categorized button ACTION picker. Visually and behaviourally mirrors
/// <see cref="GridPicker"/> (the knob TARGET picker): a 50px trigger with a tinted icon tile,
/// friendly name + muted description and a rotating chevron; a borderless Window flyout (not a
/// Popup — Win11 click-through / HWND isolation) with a search box, accent-bar section headers,
/// icon-tile rows with one-line descriptions, check on the selection, and INLINE accordion
/// expansion for sub-options (devices, profiles, HA entities, Govee lights…) and action groups
/// (Home Assistant / Govee / Spotify / Pocket Casts).
///
/// Public API is unchanged from the old dropdown: AddItem / AddCategory / AddActionGroup /
/// RegisterSubMenu / Select / SelectWithSub / SelectedValue / SelectedSubTag + events.
/// Stored values are the raw action strings; only the presentation changed.
/// </summary>
public class ActionPicker : System.Windows.Controls.Border
{
    // ── Friendly presentation per action value ───────────────────
    // (Material icon, title, one-line description). Values not listed fall back to the
    // display / glyph / tooltip passed to AddItem.
    public static readonly Dictionary<string, (MaterialIconKind Kind, string Title, string Description)> ActionMeta = new()
    {
        ["none"]                    = (MaterialIconKind.Cancel, "Do nothing", "Leave this gesture unassigned"),
        // Media
        ["media_play_pause"]        = (MaterialIconKind.PlayPause, "Play / pause", "Toggle music or video playback"),
        ["media_next"]              = (MaterialIconKind.SkipNext, "Next track", "Skip to the next song or video"),
        ["media_prev"]              = (MaterialIconKind.SkipPrevious, "Previous track", "Go back to the previous song or video"),
        // Mute
        ["mute_master"]             = (MaterialIconKind.VolumeOff, "Mute speakers", "Mute or unmute the Windows master volume"),
        ["mute_mic"]                = (MaterialIconKind.MicrophoneOff, "Mute microphone", "Mute or unmute your default mic"),
        ["mute_program"]            = (MaterialIconKind.VolumeVariantOff, "Mute an app", "Mute or unmute one specific app"),
        ["mute_active_window"]      = (MaterialIconKind.WindowMaximize, "Mute focused app", "Mute whichever app is in front"),
        ["mute_app_group"]          = (MaterialIconKind.SpeakerOff, "Mute app group", "Mute every app on a knob's app group"),
        ["mute_device"]             = (MaterialIconKind.HeadphonesOff, "Mute a device", "Mute a specific speaker or headset"),
        // App control
        ["launch_exe"]              = (MaterialIconKind.RocketLaunch, "Open app", "Launch a program, file or script"),
        ["close_program"]           = (MaterialIconKind.CloseBox, "Close app", "Force-close a running program"),
        ["add_active_app_to_group"] = (MaterialIconKind.PlaylistPlus, "Add focused app to group", "Put the app in front onto a knob's app group"),
        ["open_url"]                = (MaterialIconKind.Web, "Open website", "Open a link in your default browser"),
        ["type_text"]               = (MaterialIconKind.FormTextbox, "Type text", "Type a saved snippet where your cursor is"),
        ["screenshot"]              = (MaterialIconKind.MonitorScreenshot, "Take screenshot", "Capture the screen"),
        // Device
        ["cycle_output"]            = (MaterialIconKind.SpeakerMultiple, "Next speaker / headset", "Switch Windows output to the next device"),
        ["cycle_input"]             = (MaterialIconKind.Microphone, "Next microphone", "Switch Windows input to the next mic"),
        ["select_output"]           = (MaterialIconKind.Speaker, "Use a speaker / headset", "Switch Windows output to a specific device"),
        ["select_input"]            = (MaterialIconKind.MicrophoneVariant, "Use a microphone", "Switch Windows input to a specific mic"),
        // System
        ["macro"]                   = (MaterialIconKind.KeyboardOutline, "Keyboard shortcut", "Press a key combo like Ctrl+Shift+M"),
        ["switch_profile"]          = (MaterialIconKind.AccountSwitch, "Switch profile", "Load a different AmpUp profile"),
        ["cycle_profile"]           = (MaterialIconKind.AccountMultiple, "Next profile", "Cycle through your chosen profiles"),
        ["cycle_brightness"]        = (MaterialIconKind.Brightness6, "LED brightness", "Step through the knob LED brightness levels"),
        ["quick_wheel"]             = (MaterialIconKind.ChartDonut, "Quick wheel", "Hold to open the radial picker"),
        // Power
        ["power_sleep"]             = (MaterialIconKind.Sleep, "Sleep", "Put the PC to sleep"),
        ["power_lock"]              = (MaterialIconKind.Lock, "Lock", "Lock Windows"),
        ["power_off"]               = (MaterialIconKind.Power, "Shut down", "Turn the PC off"),
        ["power_restart"]           = (MaterialIconKind.Restart, "Restart", "Restart the PC"),
        ["power_logoff"]            = (MaterialIconKind.Logout, "Sign out", "Sign out of Windows"),
        ["power_hibernate"]         = (MaterialIconKind.Snowflake, "Hibernate", "Save your session and power down"),
        ["system_power"]            = (MaterialIconKind.Power, "Power (legacy)", "Run the power action picked below"),
        // Room / integrations
        ["room_toggle"]             = (MaterialIconKind.LightbulbGroup, "Room lights on / off", "Toggle all room lights (Govee + Corsair)"),
        ["room_effect"]             = (MaterialIconKind.Palette, "Room effect", "Switch the active room lighting effect"),
        ["group_toggle"]            = (MaterialIconKind.Group, "Device group on / off", "Turn a device group on or off"),
        ["corsair_toggle"]          = (MaterialIconKind.LightbulbOn, "iCUE lights on / off", "Toggle Corsair iCUE lighting"),
        ["ha_toggle"]               = (MaterialIconKind.ToggleSwitch, "Toggle entity", "Turn a Home Assistant entity on or off"),
        ["ha_scene"]                = (MaterialIconKind.MovieOpen, "Activate scene", "Run a Home Assistant scene"),
        ["ha_color"]                = (MaterialIconKind.Palette, "Light color", "Set a Home Assistant light to a color"),
        ["ha_color_temp"]           = (MaterialIconKind.Thermometer, "Light temperature", "Set a light's warmth in Kelvin"),
        ["ha_service"]              = (MaterialIconKind.Cog, "Call service", "Call any Home Assistant service"),
        ["govee_toggle"]            = (MaterialIconKind.Lightbulb, "Light on / off", "Turn a Govee light on or off"),
        ["govee_color"]             = (MaterialIconKind.Palette, "Light color", "Set a Govee light to a color"),
        ["govee_white_toggle"]      = (MaterialIconKind.LightbulbOn, "All lights white", "Every room light to full white; press again for off"),
        ["obs_record"]              = (MaterialIconKind.RecordRec, "OBS record", "Start or stop OBS recording"),
        ["obs_stream"]              = (MaterialIconKind.Broadcast, "OBS stream", "Start or stop OBS streaming"),
        ["obs_scene"]               = (MaterialIconKind.MovieOpen, "OBS scene", "Switch to an OBS scene"),
        ["obs_mute"]                = (MaterialIconKind.MicrophoneOff, "OBS mute source", "Mute or unmute an OBS audio source"),
        ["vm_mute_strip"]           = (MaterialIconKind.TuneVertical, "VoiceMeeter strip mute", "Mute or unmute a VoiceMeeter input strip"),
        ["vm_mute_bus"]             = (MaterialIconKind.TuneVertical, "VoiceMeeter bus mute", "Mute or unmute a VoiceMeeter output bus"),
        ["spotify_play_pause"]      = (MaterialIconKind.PlayPause, "Play / pause", "Play or pause Spotify"),
        ["spotify_next"]            = (MaterialIconKind.SkipNext, "Next track", "Skip to the next Spotify track"),
        ["spotify_prev"]            = (MaterialIconKind.SkipPrevious, "Previous track", "Go back to the previous Spotify track"),
        ["spotify_shuffle"]         = (MaterialIconKind.Shuffle, "Shuffle", "Turn Spotify shuffle on or off"),
        ["spotify_like"]            = (MaterialIconKind.Heart, "Like track", "Like or unlike the playing track"),
        ["pocketcasts_open"]        = (MaterialIconKind.Podcast, "Open Pocket Casts", "Open or focus the Pocket Casts app"),
        ["pocketcasts_play_pause"]  = (MaterialIconKind.PlayPause, "Play / pause", "Play or pause Pocket Casts"),
        ["pocketcasts_skip_back"]   = (MaterialIconKind.Rewind10, "Back 10 seconds", "Jump back 10 seconds"),
        ["pocketcasts_skip_forward"]= (MaterialIconKind.FastForward30, "Forward 30 seconds", "Jump ahead 30 seconds"),
        ["signalrgb_effect"]        = (MaterialIconKind.AutoFix, "SignalRGB effect", "Switch SignalRGB to a lighting effect"),
        ["signalrgb_effect_cycle"]  = (MaterialIconKind.Repeat, "Cycle SignalRGB effects", "Step through the effects listed on this button"),
        ["signalrgb_blackout"]      = (MaterialIconKind.LightbulbOff, "SignalRGB blackout", "Turn all SignalRGB lighting black"),
        ["signalrgb_restore"]       = (MaterialIconKind.Restore, "SignalRGB restore", "Bring back the last SignalRGB effect"),
        // Stream Controller
        ["sc_page_next"]            = (MaterialIconKind.ChevronRight, "Next page", "Go to the next Stream Controller page"),
        ["sc_page_prev"]            = (MaterialIconKind.ChevronLeft, "Previous page", "Go to the previous Stream Controller page"),
        ["sc_page_home"]            = (MaterialIconKind.Home, "First page", "Jump back to page 1"),
        ["sc_go_to_page"]           = (MaterialIconKind.Numeric, "Go to page", "Jump to a specific page number"),
        // Advanced
        ["multi_action"]            = (MaterialIconKind.FormatListNumbered, "Multi-action", "Run several actions in a row"),
        ["toggle_action"]           = (MaterialIconKind.SwapHorizontal, "Toggle A / B", "Alternate between two actions each press"),
        ["open_folder"]             = (MaterialIconKind.FolderOpen, "Open Space", "Jump into a named Space"),
        // Action-group parents
        ["group_ha"]                = (MaterialIconKind.HomeAssistant, "Home Assistant", "Toggle entities, scenes, light colors, services"),
        ["group_govee"]             = (MaterialIconKind.Lightbulb, "Govee", "Power, color and white toggles"),
        ["group_spotify"]           = (MaterialIconKind.Spotify, "Spotify", "Playback, shuffle and likes"),
        ["group_pocketcasts"]       = (MaterialIconKind.Podcast, "Pocket Casts", "Open, play / pause and skip"),
        // Lights tab reactive modes (also use this control)
        ["BeatPulse"]               = (MaterialIconKind.Pulse, "Beat Pulse", "Bass drives all knob brightness together"),
        ["SpectrumBands"]           = (MaterialIconKind.Equalizer, "Spectrum Bands", "Each knob shows its own frequency band"),
        ["ColorShift"]              = (MaterialIconKind.Palette, "Color Shift", "Hue shifts with the audio energy"),
    };

    private static readonly Regex DisabledSuffix = new(@"\s\([^)]*disabled\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Model ─────────────────────────────────────────────────────

    public record SubItem(string Display, string Tag, string? Icon = null, Color? IconColor = null);

    private readonly List<(string Display, string Value, string Icon, Color Color, string Tooltip)> _items = new();
    private readonly List<(int ItemIndex, string CategoryName)> _categories = new();
    private readonly Dictionary<string, Func<List<SubItem>>> _subMenuProviders = new();
    private readonly Dictionary<string, (string Display, string Icon, Color Color, List<string> Children)> _actionGroups = new();
    private readonly HashSet<string> _actionGroupChildren = new();
    private readonly HashSet<string> _actionGroupParents = new();

    private int _selectedIndex = -1;
    private string? _selectedSubTag;

    public event EventHandler? SelectionChanged;

    /// <summary>Fired when a sub-item is chosen. Args: (actionValue, subItemTag).</summary>
    public event Action<string, string>? SubItemSelected;

    public string SelectedValue => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Value : "none";

    /// <summary>The sub-item tag chosen in the flyout (entity ID, device ID, profile…), or null.</summary>
    public string? SelectedSubTag => _selectedSubTag;

    public Color AccentColor => ThemeManager.Accent;

    // ── Trigger visuals ───────────────────────────────────────────

    private readonly TextBlock _label;
    private readonly TextBlock _triggerSub;
    private readonly ContentControl _triggerIconHost;
    private readonly MaterialIcon _chevron;

    // ── Flyout ────────────────────────────────────────────────────

    private Window? _flyout;
    private bool _isOpen;
    private System.Windows.Controls.Border? _popupBorder;
    private TextBox? _searchBox;
    private TextBlock? _searchPlaceholder;
    private ScrollViewer? _scroll;
    private StackPanel? _listPanel;
    private string _query = "";
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SubItem>> _childCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(System.Windows.Controls.Border Row, Action Activate, Action? Expand, Action? Collapse)> _navRows = new();
    private int _navIndex = -1;
    private System.Windows.Controls.Border? _selectedRowVisual;
    private DateTime _lastClose = DateTime.MinValue;
    private const double FlyoutWidth = 440;

    public ActionPicker()
    {
        this.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(8, 7, 12, 7);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        MinHeight = 50;
        this.SetResourceReference(BackgroundProperty, "InputBgBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _triggerIconHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Focusable = false };
        grid.Children.Add(_triggerIconHost);

        _label = new TextBlock { Text = "Choose…", FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _triggerSub = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        _triggerSub.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(_label);
        labels.Children.Add(_triggerSub);
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);

        _chevron = new MaterialIcon { Kind = MaterialIconKind.ChevronDown, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0) };
        _chevron.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        Grid.SetColumn(_chevron, 2);
        grid.Children.Add(_chevron);

        Child = grid;
        UpdateTrigger();

        MouseEnter += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0xAA));
            Background = new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0x14));
            _chevron.Foreground = new SolidColorBrush(AccentColor);
        };
        MouseLeave += (_, _) => { if (!_isOpen) ResetTriggerChrome(); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_isOpen) CloseFlyout();
            else if ((DateTime.UtcNow - _lastClose).TotalMilliseconds > 250) OpenFlyout();
            e.Handled = true;
        };
    }

    private void ResetTriggerChrome()
    {
        this.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        this.SetResourceReference(BackgroundProperty, "InputBgBrush");
        _chevron.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
    }

    private void RotateChevron(double angle)
        => ((RotateTransform)_chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    // ── Item management (public API, unchanged) ──────────────────

    public void AddItem(string display, string value, string icon, Color color, string tooltip)
        => _items.Add((display, value, icon, color, tooltip));

    /// <summary>
    /// Mark existing action values as members of a group. The flyout shows one expandable
    /// parent row; its children are listed inside it and choosing one commits that action.
    /// </summary>
    public void AddActionGroup(string groupValue, string display, string icon, Color color, IEnumerable<string> childValues)
    {
        var children = childValues?.Where(v => !string.IsNullOrEmpty(v)).ToList() ?? new List<string>();
        if (children.Count == 0) return;
        _actionGroups[groupValue] = (display, icon, color, children);
        foreach (var c in children) _actionGroupChildren.Add(c);
        _actionGroupParents.Add(groupValue);
        _items.Add((display, groupValue, icon, color, $"{display} — pick a specific action"));
    }

    /// <summary>Kept for API compatibility — the list is built when the flyout opens.</summary>
    public void BuildPopup() { if (_isOpen) RebuildList(); }

    /// <summary>Register child options for an action; they expand inline under its row.</summary>
    public void RegisterSubMenu(string actionValue, Func<List<SubItem>> provider) => _subMenuProviders[actionValue] = provider;

    public void ClearSubMenus() => _subMenuProviders.Clear();

    public void AddCategory(string categoryName) => _categories.Add((_items.Count, categoryName));

    public void ClearItems()
    {
        _items.Clear();
        _categories.Clear();
        _actionGroups.Clear();
        _actionGroupChildren.Clear();
        _actionGroupParents.Clear();
        _selectedIndex = -1;
        _selectedSubTag = null;
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    public void Select(string value) => SelectCore(value, clearSubTag: true);

    private void SelectCore(string value, bool clearSubTag)
    {
        if (clearSubTag) _selectedSubTag = null;
        int i = _items.FindIndex(x => x.Value == value);
        _selectedIndex = i >= 0 ? i : (_items.Count > 0 ? 0 : -1);
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    /// <summary>Select an action plus a sub-tag (entity / device / profile…).</summary>
    public void SelectWithSub(string value, string? subTag)
    {
        _selectedSubTag = subTag;
        SelectCore(value, clearSubTag: false);
    }

    // ── Presentation helpers ──────────────────────────────────────

    private string TitleOf(int index)
    {
        var it = _items[index];
        if (!ActionMeta.TryGetValue(it.Value, out var m)) return it.Display;
        var suffix = DisabledSuffix.Match(it.Display);
        return suffix.Success ? m.Title + suffix.Value : m.Title;
    }

    private string? DescriptionOf(int index)
    {
        var it = _items[index];
        if (ActionMeta.TryGetValue(it.Value, out var m)) return m.Description;
        return string.IsNullOrEmpty(it.Tooltip) || it.Tooltip == it.Display ? null : it.Tooltip;
    }

    private MaterialIconKind? KindOf(int index)
        => ActionMeta.TryGetValue(_items[index].Value, out var m) ? m.Kind : GridPicker.ParseKind(_items[index].Icon);

    private string? GroupOf(string value)
        => _actionGroups.FirstOrDefault(g => g.Value.Children.Contains(value)).Key;

    /// <summary>Title shown on the trigger — group children get their group name as prefix.</summary>
    private string TriggerTitleOf(int index)
    {
        var title = TitleOf(index);
        var g = GroupOf(_items[index].Value);
        if (g != null)
        {
            int gi = _items.FindIndex(x => x.Value == g);
            if (gi >= 0) title = TitleOf(gi) + ": " + title;
        }
        return title;
    }

    private FrameworkElement Tile(int index, double size)
        => GridPicker.BuildTile(KindOf(index), _items[index].Icon, null, _items[index].Color, size);

    // ── Trigger ───────────────────────────────────────────────────

    private void UpdateTrigger()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            _label.Text = "Choose…";
            _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _triggerSub.Visibility = Visibility.Collapsed;
            _triggerIconHost.Content = GridPicker.BuildTile(MaterialIconKind.GestureTap, null, null, Color.FromRgb(0x88, 0x88, 0x88), 32);
            ToolTip = null;
            return;
        }

        var item = _items[_selectedIndex];
        string title = TriggerTitleOf(_selectedIndex);
        string? sub = DescriptionOf(_selectedIndex);

        if (!string.IsNullOrEmpty(_selectedSubTag))
        {
            var match = GetChildren(item.Value, useCache: false).FirstOrDefault(s => s.Tag == _selectedSubTag);
            if (match != null) { sub = title; title = match.Display; }
        }

        _label.Text = title;
        _label.SetResourceReference(TextBlock.ForegroundProperty, item.Value == "none" ? "TextSecBrush" : "TextPrimaryBrush");
        _triggerSub.Text = sub ?? "";
        _triggerSub.Visibility = string.IsNullOrWhiteSpace(sub) ? Visibility.Collapsed : Visibility.Visible;
        _triggerIconHost.Content = Tile(_selectedIndex, 32);
        ToolTip = item.Tooltip;
    }

    // ── Flyout ────────────────────────────────────────────────────

    private void OpenFlyout()
    {
        _query = "";
        _childCache.Clear();
        _expanded.Clear();
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            var v = _items[_selectedIndex].Value;
            if (GroupOf(v) is { } g) _expanded.Add("grp:" + g);
            if (_selectedSubTag != null && _subMenuProviders.ContainsKey(v)) _expanded.Add(v);
        }

        _searchBox = new TextBox
        {
            FontSize = 12.5,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _searchBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _searchBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        _searchPlaceholder = new TextBlock
        {
            Text = "Search actions, devices, profiles…",
            FontSize = 12.5,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        _searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _searchBox.TextChanged += (_, _) =>
        {
            _query = _searchBox.Text.Trim();
            _searchPlaceholder.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RebuildList();
            _scroll?.ScrollToTop();
        };

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mag = UiKit.Icon(MaterialIconKind.Magnify, 16);
        mag.Margin = new Thickness(0, 0, 8, 0);
        searchGrid.Children.Add(mag);
        var textHost = new Grid();
        textHost.Children.Add(_searchBox);
        textHost.Children.Add(_searchPlaceholder);
        Grid.SetColumn(textHost, 1);
        searchGrid.Children.Add(textHost);

        var searchBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 8),
            Child = searchGrid,
        };
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        _listPanel = new StackPanel();
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _listPanel,
            Focusable = false,
        };

        var hint = new TextBlock { Text = "↑ ↓ to move  ·  Enter to choose  ·  Esc to close", FontSize = 10, Margin = new Thickness(4, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var root = new DockPanel();
        DockPanel.SetDock(searchBorder, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(searchBorder);
        root.Children.Add(hint);
        root.Children.Add(_scroll);

        _popupBorder = new System.Windows.Controls.Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = root,
        };
        _popupBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgDarkBrush");
        _popupBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        // Placement — DPI-aware, flips upward when there's no room below.
        var belowPx = PointToScreen(new Point(0, ActualHeight + 4));
        var abovePx = PointToScreen(new Point(0, -4));
        var source = PresentationSource.FromVisual(this);
        double dpiX = 1, dpiY = 1;
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
        }
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)belowPx.X, (int)belowPx.Y)).WorkingArea;
        double waTop = wa.Top / dpiY, waBottom = wa.Bottom / dpiY, waLeft = wa.Left / dpiX, waRight = wa.Right / dpiX;
        double belowY = belowPx.Y / dpiY, aboveY = abovePx.Y / dpiY;
        double spaceBelow = waBottom - belowY - 8, spaceAbove = aboveY - waTop - 8;
        bool openUp = spaceBelow < 420 && spaceAbove > spaceBelow;
        // Short lists (e.g. Lights tab reactive modes) don't need the full-height panel.
        double wanted = _items.Count <= 6 && _subMenuProviders.Count == 0 ? 120 + _items.Count * 52 : 560;
        double height = Math.Clamp(Math.Min(openUp ? spaceAbove : spaceBelow, wanted), 200, 560);
        double width = Math.Max(FlyoutWidth, ActualWidth);
        _popupBorder.Width = width;
        _popupBorder.Height = height;

        double left = belowPx.X / dpiX;
        if (left + width > waRight) left = Math.Max(waLeft, waRight - width);
        double top = openUp ? aboveY - height : belowY;

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Content = _popupBorder,
            Left = left,
            Top = top,
        };
        _flyout.SetResourceReference(Window.BackgroundProperty, "BgDarkBrush");
        _flyout.Deactivated += (_, _) => CloseFlyout();
        _flyout.PreviewKeyDown += OnFlyoutKeyDown;

        RebuildList();

        var translate = new TranslateTransform(0, openUp ? 8 : -8);
        _popupBorder.RenderTransform = translate;
        _popupBorder.Opacity = 0;

        _flyout.Show();
        _flyout.Activate();
        _isOpen = true;
        _searchBox.Focus();
        Keyboard.Focus(_searchBox);

        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(openUp ? 8 : -8, 0, TimeSpan.FromMilliseconds(130))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _popupBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));

        _flyout.Dispatcher.BeginInvoke(new Action(() => _selectedRowVisual?.BringIntoView(new Rect(0, -40, 10, 120))),
            System.Windows.Threading.DispatcherPriority.Loaded);

        BorderBrush = new SolidColorBrush(AccentColor);
        Background = new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0x14));
        RotateChevron(180);
    }

    private void CloseFlyout()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastClose = DateTime.UtcNow;
        var f = _flyout;
        _flyout = null;
        _navRows.Clear();
        _selectedRowVisual = null;
        f?.Close();
        RotateChevron(0);
        ResetTriggerChrome();
    }

    private void OnFlyoutKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_searchBox != null && _searchBox.Text.Length > 0) _searchBox.Text = "";
                else CloseFlyout();
                e.Handled = true;
                break;
            case Key.Down: MoveNav(+1); e.Handled = true; break;
            case Key.Up: MoveNav(-1); e.Handled = true; break;
            case Key.PageDown: MoveNav(+6); e.Handled = true; break;
            case Key.PageUp: MoveNav(-6); e.Handled = true; break;
            case Key.Enter:
                if (_navIndex >= 0 && _navIndex < _navRows.Count) _navRows[_navIndex].Activate();
                else if (_navRows.Count > 0) _navRows[0].Activate();
                e.Handled = true;
                break;
            case Key.Right:
                if (_navIndex >= 0 && _navIndex < _navRows.Count && _navRows[_navIndex].Expand is { } ex
                    && (_searchBox == null || _searchBox.CaretIndex >= _searchBox.Text.Length))
                { ex(); e.Handled = true; }
                break;
            case Key.Left:
                if (_navIndex >= 0 && _navIndex < _navRows.Count && _navRows[_navIndex].Collapse is { } col
                    && (_searchBox == null || _searchBox.Text.Length == 0))
                { col(); e.Handled = true; }
                break;
        }
    }

    private void MoveNav(int delta)
    {
        if (_navRows.Count == 0) return;
        int next = _navIndex < 0 ? (delta > 0 ? 0 : _navRows.Count - 1) : Math.Clamp(_navIndex + delta, 0, _navRows.Count - 1);
        SetNav(next);
    }

    private void SetNav(int idx)
    {
        if (_navIndex >= 0 && _navIndex < _navRows.Count)
            _navRows[_navIndex].Row.BorderBrush = Brushes.Transparent;
        _navIndex = idx;
        if (idx >= 0 && idx < _navRows.Count)
        {
            var row = _navRows[idx].Row;
            row.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0x88));
            row.BringIntoView();
        }
    }

    // ── List building ─────────────────────────────────────────────

    private List<SubItem> GetChildren(string value, bool useCache = true)
    {
        if (_actionGroupParents.Contains(value)) return new();
        if (!_subMenuProviders.TryGetValue(value, out var provider)) return new();
        if (useCache && _childCache.TryGetValue(value, out var cached)) return cached;
        List<SubItem> list;
        try { list = provider() ?? new(); } catch { list = new(); }
        if (useCache) _childCache[value] = list;
        return list;
    }

    private bool Matches(params string?[] fields)
    {
        if (_query.Length == 0) return true;
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hay = string.Join(" ", fields.Where(f => !string.IsNullOrEmpty(f)));
        return terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private bool ItemMatches(int i, string? category, string? groupTitle = null)
        => Matches(TitleOf(i), DescriptionOf(i), _items[i].Display, _items[i].Value, category, groupTitle);

    private void RebuildList()
    {
        if (_listPanel == null) return;
        var prevNavKey = _navIndex >= 0 && _navIndex < _navRows.Count ? _navRows[_navIndex].Row.Tag : null;
        double keepOffset = _scroll?.VerticalOffset ?? 0;
        _listPanel.Children.Clear();
        _navRows.Clear();
        _navIndex = -1;
        _selectedRowVisual = null;

        var groups = new List<(string? Name, List<int> Indices)>();
        int firstCat = _categories.Count > 0 ? _categories[0].ItemIndex : _items.Count;
        if (firstCat > 0) groups.Add((null, Enumerable.Range(0, Math.Min(firstCat, _items.Count)).ToList()));
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            groups.Add((_categories[c].CategoryName, Enumerable.Range(start, Math.Max(0, end - start)).ToList()));
        }

        // Group parents are appended after all categories by AddActionGroup — file each one
        // under the category holding its first child (Integrations), not at the end.
        var parentHome = new Dictionary<int, string?>();
        foreach (var (gv, info) in _actionGroups)
        {
            int pi = _items.FindIndex(x => x.Value == gv);
            int ci = _items.FindIndex(x => x.Value == info.Children.FirstOrDefault(c => _items.Any(y => y.Value == c)));
            if (pi < 0) continue;
            var homeName = ci >= 0 ? groups.FirstOrDefault(g => g.Indices.Contains(ci)).Name : null;
            parentHome[pi] = homeName;
        }

        int shown = 0;
        foreach (var (name, indices) in groups)
        {
            var section = new StackPanel();
            int count = 0;
            var ordered = indices.Where(i => !parentHome.ContainsKey(i)).ToList();
            ordered.AddRange(parentHome.Where(p => p.Value == name).Select(p => p.Key));

            foreach (int i in ordered)
            {
                var v = _items[i].Value;
                if (_actionGroupChildren.Contains(v) && GroupOf(v) is { } gk && _items.Any(x => x.Value == gk)) continue;
                if (_actionGroupParents.Contains(v))
                {
                    var g = BuildGroup(i, name);
                    if (g != null) { section.Children.Add(g); count++; }
                    continue;
                }
                var row = BuildItemWithChildren(i, name, null, compact: false);
                if (row != null) { section.Children.Add(row); count++; }
            }

            if (count == 0) continue;
            if (name != null)
            {
                var header = UiKit.SectionHeader(name, null);
                ((FrameworkElement)header).Margin = new Thickness(4, shown == 0 ? 2 : 14, 0, 6);
                _listPanel.Children.Add(header);
            }
            _listPanel.Children.Add(section);
            shown++;
        }

        // Group parents whose children are all missing from any category (defensive).
        foreach (var kv in parentHome.Where(p => !groups.Any(g => g.Name == p.Value)))
        {
            var g = BuildGroup(kv.Key, null);
            if (g != null) { _listPanel.Children.Add(g); shown++; }
        }

        if (shown == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 36, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var ic = UiKit.Icon(MaterialIconKind.MagnifyClose, 28);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(ic);
            var t = new TextBlock { Text = $"Nothing matches \"{_query}\"", FontSize = 12, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
            empty.Children.Add(t);
            _listPanel.Children.Add(empty);
        }

        int restore = prevNavKey != null ? _navRows.FindIndex(r => Equals(r.Row.Tag, prevNavKey)) : -1;
        if (restore < 0 && _query.Length > 0 && _navRows.Count > 0) restore = 0;
        if (restore < 0 && _selectedRowVisual != null) restore = _navRows.FindIndex(r => r.Row == _selectedRowVisual);
        if (restore >= 0) SetNav(restore);

        if (_scroll != null && keepOffset > 0)
        {
            _scroll.UpdateLayout();
            _scroll.ScrollToVerticalOffset(Math.Min(keepOffset, _scroll.ScrollableHeight));
        }
    }

    private System.Windows.Controls.Border MakeRowShell(bool selected, bool parentOfSelection = false)
    {
        var row = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 10, 6),
            Margin = new Thickness(0, 1, 0, 1),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            Background = selected
                ? new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0x22))
                : parentOfSelection ? new SolidColorBrush(ThemeManager.WithAlpha(AccentColor, 0x10)) : Brushes.Transparent,
        };
        var normalBg = row.Background;
        row.MouseEnter += (_, _) => { if (!selected) row.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush"); };
        row.MouseLeave += (_, _) => { if (!selected) row.Background = normalBg; };
        return row;
    }

    private TextBlock MakeTitle(string text, bool accent)
    {
        var t = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        if (accent) t.Foreground = new SolidColorBrush(AccentColor);
        else t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return t;
    }

    private static TextBlock MakeDescription(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        return t;
    }

    private static Grid ThreeColumnGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static (Grid Outer, StackPanel Panel) MakeChildContainer()
    {
        var panel = new StackPanel { Margin = new Thickness(26, 0, 0, 6) };
        var outer = new Grid();
        var guide = new System.Windows.Controls.Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(15, 2, 0, 8), CornerRadius = new CornerRadius(1) };
        guide.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBorderBrush");
        outer.Children.Add(guide);
        outer.Children.Add(panel);
        return (outer, panel);
    }

    /// <summary>Action-group accordion (Home Assistant, Govee, Spotify…).</summary>
    private FrameworkElement? BuildGroup(int parentIndex, string? category)
    {
        var gv = _items[parentIndex].Value;
        var expKey = "grp:" + gv;
        var members = _actionGroups[gv].Children
            .Select(c => _items.FindIndex(x => x.Value == c)).Where(i => i >= 0).ToList();
        if (members.Count == 0) return null;

        string groupTitle = TitleOf(parentIndex);
        bool headerMatch = ItemMatches(parentIndex, category);
        var matched = members.Where(m => ItemMatches(m, category, groupTitle)
            || GetChildren(_items[m].Value).Any(s => Matches(s.Display, s.Tag))).ToList();
        if (!headerMatch && matched.Count == 0) return null;

        bool memberSelected = members.Contains(_selectedIndex);
        bool expanded = _expanded.Contains(expKey) || (_query.Length > 0 && !headerMatch);
        var visible = _query.Length > 0 && !headerMatch ? matched : members;

        var container = new StackPanel();
        var row = MakeRowShell(false, memberSelected);
        row.Tag = "group:" + gv;
        var grid = ThreeColumnGrid();
        var tile = Tile(parentIndex, 32);
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MakeTitle(groupTitle, memberSelected));
        var desc = memberSelected ? "Current: " + TitleOf(_selectedIndex) : DescriptionOf(parentIndex);
        if (!string.IsNullOrEmpty(desc)) text.Children.Add(MakeDescription(desc));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var chev = UiKit.Icon(expanded ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown, 18, memberSelected ? "AccentBrush" : "TextDimBrush");
        chev.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(chev, 2);
        grid.Children.Add(chev);
        row.Child = grid;
        row.ToolTip = _items[parentIndex].Tooltip;

        Action toggle = () => { if (!_expanded.Remove(expKey)) _expanded.Add(expKey); RebuildList(); };
        row.MouseLeftButtonUp += (_, ev) => { toggle(); ev.Handled = true; };
        if (memberSelected && !expanded) _selectedRowVisual = row;
        _navRows.Add((row, toggle, expanded ? null : toggle, expanded ? toggle : null));
        container.Children.Add(row);

        if (expanded)
        {
            var (outer, panel) = MakeChildContainer();
            foreach (int m in visible)
            {
                var el = BuildItemWithChildren(m, category, headerMatch ? null : groupTitle, compact: true, force: true);
                if (el != null) panel.Children.Add(el);
            }
            container.Children.Add(outer);
        }
        return container;
    }

    /// <summary>One action row plus its inline sub-options when expanded.</summary>
    private FrameworkElement? BuildItemWithChildren(int index, string? category, string? groupTitle, bool compact, bool force = false)
    {
        var value = _items[index].Value;
        var children = GetChildren(value);
        bool hasChildren = children.Count > 0;
        bool selfMatch = force || ItemMatches(index, category, groupTitle);
        var matchedChildren = hasChildren && _query.Length > 0 ? children.Where(s => Matches(s.Display, s.Tag)).ToList() : children;
        if (!selfMatch && matchedChildren.Count == 0) return null;
        if (force && _query.Length > 0 && !ItemMatches(index, category, groupTitle) && matchedChildren.Count > 0) selfMatch = false;

        bool expanded = hasChildren && (_expanded.Contains(value) || (_query.Length > 0 && !selfMatch && matchedChildren.Count > 0));
        var visibleChildren = _query.Length > 0 && !selfMatch ? matchedChildren : children;

        var row = BuildItemRow(index, value, hasChildren, expanded, compact);
        if (!expanded) return row;
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(BuildChildren(index, value, visibleChildren));
        return stack;
    }

    private System.Windows.Controls.Border BuildItemRow(int index, string value, bool hasChildren, bool expanded, bool compact)
    {
        bool isSelected = index == _selectedIndex;
        bool hasSubSelection = isSelected && _selectedSubTag != null;
        bool showSelected = isSelected && !hasChildren;

        var row = MakeRowShell(showSelected, isSelected && hasChildren);
        row.Tag = "item:" + value;
        row.ToolTip = _items[index].Tooltip;
        if (compact) row.Padding = new Thickness(8, 5, 10, 5);

        var grid = ThreeColumnGrid();
        var tile = Tile(index, compact ? 24 : 32);
        tile.Margin = new Thickness(0, 0, compact ? 10 : 12, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MakeTitle(TitleOf(index), isSelected));
        string? desc = DescriptionOf(index);
        if (hasSubSelection && hasChildren)
        {
            var cur = GetChildren(value).FirstOrDefault(s => s.Tag == _selectedSubTag)?.Display ?? _selectedSubTag;
            desc = "Current: " + cur;
        }
        if (!string.IsNullOrEmpty(desc)) text.Children.Add(MakeDescription(desc));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement? trailing = null;
        if (hasChildren)
            trailing = UiKit.Icon(expanded ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown, 18, isSelected ? "AccentBrush" : "TextDimBrush");
        else if (showSelected)
            trailing = UiKit.Icon(MaterialIconKind.Check, 18, "AccentBrush");
        if (trailing != null)
        {
            trailing.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
        row.Child = grid;

        Action toggle = () => { if (!_expanded.Remove(value)) _expanded.Add(value); RebuildList(); };
        Action activate = hasChildren ? toggle : () => CommitItem(index);
        row.MouseLeftButtonUp += (_, ev) => { activate(); ev.Handled = true; };

        if (isSelected && (!hasChildren || !expanded || !hasSubSelection)) _selectedRowVisual ??= row;

        _navRows.Add((row, activate,
            hasChildren && !expanded ? toggle : null,
            hasChildren && expanded ? toggle : null));
        return row;
    }

    private FrameworkElement BuildChildren(int parentIndex, string parentValue, List<SubItem> children)
    {
        var (outer, panel) = MakeChildContainer();
        bool parentSelected = parentIndex == _selectedIndex;
        var parentKind = KindOf(parentIndex);
        var parentColor = _items[parentIndex].Color;

        foreach (var s in children)
        {
            bool selected = parentSelected && _selectedSubTag == s.Tag;
            var row = MakeRowShell(selected);
            row.Padding = new Thickness(8, 5, 10, 5);
            row.Tag = "sub:" + parentValue + ":" + s.Tag;

            var grid = ThreeColumnGrid();
            // Sub-item glyphs are often emoji (render monochrome in WPF) — prefer a Material
            // kind, falling back to the parent action's icon.
            var lead = GridPicker.BuildTile(GridPicker.ParseKind(s.Icon) ?? parentKind, null, null, s.IconColor ?? parentColor, 24);
            lead.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(lead);

            var title = MakeTitle(s.Display, selected);
            title.FontSize = 12;
            title.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            if (selected)
            {
                var chk = UiKit.Icon(MaterialIconKind.Check, 16, "AccentBrush");
                chk.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(chk, 2);
                grid.Children.Add(chk);
            }
            row.Child = grid;

            var sub = s;
            Action activate = () => CommitSub(parentIndex, sub);
            row.MouseLeftButtonUp += (_, ev) => { activate(); ev.Handled = true; };
            if (selected) _selectedRowVisual = row;
            _navRows.Add((row, activate, null, () => { _expanded.Remove(parentValue); RebuildList(); }));
            panel.Children.Add(row);
        }
        return outer;
    }

    // ── Commit ────────────────────────────────────────────────────

    private void CommitItem(int index)
    {
        _selectedIndex = index;
        _selectedSubTag = null;
        UpdateTrigger();
        CloseFlyout();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitSub(int parentIndex, SubItem s)
    {
        _selectedIndex = parentIndex;
        _selectedSubTag = s.Tag;
        UpdateTrigger();
        CloseFlyout();
        SubItemSelected?.Invoke(_items[parentIndex].Value, s.Tag);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
