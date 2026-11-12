using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AmpUp.Core.Services;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Discovery modal for adding Home Assistant lights to Room > Devices. Same chrome as
/// <see cref="GroupNameDialog"/> (glowing icon tile header, close, drag to move). Shows a scanning
/// animation while light.* entities load, then a searchable grid of light cards grouped by HA area,
/// with tick overlays, Select all, a live "N selected" footer, already-added lights greyed out,
/// and a refresh button. Enter adds, Esc cancels.
/// </summary>
public sealed class HALightPickerDialog : Window
{
    private static readonly Color HaColor = Color.FromRgb(0x82, 0xCF, 0xFF);

    private readonly HAIntegration _ha;
    private readonly HashSet<string> _alreadyAdded;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _repaintCard = new(StringComparer.OrdinalIgnoreCase);
    private List<HALightInfo> _lights = new();
    private List<HALightInfo>? _result;
    private bool _loadingLights;

    private readonly ContentControl _bodyHost;
    private readonly TextBox _search;
    private readonly TextBlock _searchPlaceholder;
    private readonly Border _searchBorder;
    private readonly TextBlock _footerCount;
    private readonly TextBlock _selectAllLabel;
    private readonly Border _selectAll;
    private readonly Border _primary;
    private readonly Border _refresh;

    /// <summary>Show the dialog. Returns the lights the user picked, or null when cancelled.</summary>
    public static List<HALightInfo>? Prompt(Window? owner, HAIntegration ha, IEnumerable<string> alreadyAddedEntityIds)
    {
        var dlg = new HALightPickerDialog(ha, alreadyAddedEntityIds) { Owner = owner };
        dlg.ShowDialog();
        return dlg._result;
    }

    private HALightPickerDialog(HAIntegration ha, IEnumerable<string> alreadyAdded)
    {
        _ha = ha;
        _alreadyAdded = new HashSet<string>(alreadyAdded, StringComparer.OrdinalIgnoreCase);
        var a = ThemeManager.Accent;

        Title = "Add Home Assistant lights";
        Width = 720;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; // no AllowsTransparency: layered windows break hit-testing on Win11
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgDarkBrush");

        var root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        root.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var layout = new Grid { Margin = new Thickness(20, 16, 20, 18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // header
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // search row
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // footer
        root.Child = layout;

        // Header: glowing HA tile + title / subtitle, refresh + close on the right (drag to move)
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = UiKit.IconTile(HaColor, MaterialIconKind.HomeAssistant, 38);
        tile.Margin = new Thickness(0, 0, 12, 0);
        tile.Effect = GlassStyle.GlowEffect(HaColor, 18, 0.35);
        header.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = "Add Home Assistant lights", FontSize = 15, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var st = new TextBlock
        {
            Text = "Pick lights to join your room effects. Updates a few times per second, so static colors and slow effects look best.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(t);
        titles.Children.Add(st);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        _refresh = UiKit.IconButton(MaterialIconKind.Refresh, "Scan again", el => { _ = LoadAsync(); }, size: 26);
        _refresh.VerticalAlignment = VerticalAlignment.Top;
        _refresh.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(_refresh, 2);
        header.Children.Add(_refresh);
        var close = UiKit.IconButton(MaterialIconKind.Close, "Cancel (Esc)", _ => Close(), size: 26);
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Grid or TextBlock or StackPanel)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        layout.Children.Add(header);

        // Search field + Select all
        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = new TextBox
        {
            FontSize = 13, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0, 1, 0, 1), VerticalContentAlignment = VerticalAlignment.Center,
        };
        _search.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _search.CaretBrush = new SolidColorBrush(a);
        _searchPlaceholder = new TextBlock
        {
            Text = "Search lights or rooms", FontSize = 13, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0),
        };
        _searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var fieldGrid = new Grid();
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fieldIcon = UiKit.Icon(MaterialIconKind.Magnify, 16);
        fieldIcon.Margin = new Thickness(0, 0, 8, 0);
        fieldGrid.Children.Add(fieldIcon);
        var textHost = new Grid();
        textHost.Children.Add(_search);
        textHost.Children.Add(_searchPlaceholder);
        Grid.SetColumn(textHost, 1);
        fieldGrid.Children.Add(textHost);
        _searchBorder = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7), Child = fieldGrid,
        };
        _searchBorder.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        _searchBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        searchRow.Children.Add(_searchBorder);
        _selectAll = MakeButton("Select all", primary: false, ToggleSelectAll, out _selectAllLabel);
        Grid.SetColumn(_selectAll, 1);
        searchRow.Children.Add(_selectAll);
        Grid.SetRow(searchRow, 1);
        layout.Children.Add(searchRow);

        _search.TextChanged += (_, _) =>
        {
            _searchPlaceholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_loadingLights) RenderGrid();
        };
        _search.GotKeyboardFocus += (_, _) => _searchBorder.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x88));
        _search.LostKeyboardFocus += (_, _) => _searchBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        // Body: scanning animation / grid / empty state
        _bodyHost = new ContentControl();
        Grid.SetRow(_bodyHost, 2);
        layout.Children.Add(_bodyHost);

        // Footer: live count (left), Cancel / Add (right)
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footerCount = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _footerCount.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        footer.Children.Add(_footerCount);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("Cancel", primary: false, Close, out _));
        _primary = MakeButton("Add", primary: true, Confirm, out _);
        buttons.Children.Add(_primary);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter && _selected.Count > 0) { e.Handled = true; Confirm(); }
        };
        Loaded += (_, _) => _ = LoadAsync();
        UpdateFooter();
    }

    // ── Loading ──────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (_loadingLights) return;
        _loadingLights = true;
        _refresh.IsEnabled = false;
        _refresh.Opacity = 0.4;
        _bodyHost.Content = BuildScanning();
        var started = DateTime.UtcNow;

        List<HALightInfo> lights;
        try { lights = await _ha.GetLightDetailsAsync(); }
        catch { lights = new List<HALightInfo>(); }

        // Keep the scan visible briefly so a fast LAN reply doesn't just flash.
        var elapsed = DateTime.UtcNow - started;
        if (elapsed < TimeSpan.FromMilliseconds(700))
            await Task.Delay(TimeSpan.FromMilliseconds(700) - elapsed);
        if (!IsLoaded) return;

        _lights = lights;
        _selected.RemoveWhere(id => !_lights.Any(l => string.Equals(l.EntityId, id, StringComparison.OrdinalIgnoreCase)));
        _loadingLights = false;
        _refresh.IsEnabled = true;
        _refresh.Opacity = 1;
        RenderGrid();
        _search.Focus();
    }

    private FrameworkElement BuildScanning()
    {
        // Radar: three expanding rings behind a pulsing HA tile.
        var radar = new Grid { Width = 140, Height = 140, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 140, Height = 140,
                Stroke = new SolidColorBrush(HaColor), StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.3, 0.3),
                Opacity = 0,
            };
            radar.Children.Add(ring);
            var begin = TimeSpan.FromMilliseconds(i * 600);
            var dur = new Duration(TimeSpan.FromMilliseconds(1800));
            var scale = new DoubleAnimation(0.3, 1.0, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0.8, 0.0, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            ring.BeginAnimation(OpacityProperty, fade);
        }
        var tile = UiKit.IconTile(HaColor, MaterialIconKind.HomeAssistant, 48);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        tile.Effect = GlassStyle.GlowEffect(HaColor, 24, 0.5);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.55, new Duration(TimeSpan.FromMilliseconds(900)))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        });
        radar.Children.Add(tile);

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(radar);
        var label = new TextBlock { Text = "Scanning Home Assistant for lights…", FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        sp.Children.Add(label);
        return sp;
    }

    // ── Grid ─────────────────────────────────────────────────────────

    private IEnumerable<HALightInfo> FilteredLights()
    {
        var q = _search.Text.Trim();
        return q.Length == 0
            ? _lights
            : _lights.Where(l => l.FriendlyName.Contains(q, StringComparison.OrdinalIgnoreCase)
                              || l.EntityId.Contains(q, StringComparison.OrdinalIgnoreCase)
                              || l.Area.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void RenderGrid()
    {
        _repaintCard.Clear();
        if (_lights.Count == 0)
        {
            _bodyHost.Content = UiKit.EmptyState(MaterialIconKind.LightbulbOutline, "No lights found",
                "Home Assistant didn't return any light entities. Check the URL and token in Settings > Integrations > Home Assistant, then scan again.");
            UpdateFooter();
            return;
        }

        var visible = FilteredLights().ToList();
        if (visible.Count == 0)
        {
            _bodyHost.Content = UiKit.EmptyState(MaterialIconKind.Magnify, "No matches", $"Nothing matches \"{_search.Text.Trim()}\".");
            UpdateFooter();
            return;
        }

        var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        bool anyArea = visible.Any(l => l.Area.Length > 0);
        var groups = visible
            .GroupBy(l => l.Area.Length > 0 ? l.Area : (anyArea ? "Other" : "Lights"))
            .OrderBy(g => g.Key == "Other" ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var cap = new TextBlock
            {
                Text = $"{group.Key.ToUpperInvariant()}  ·  {group.Count()}",
                FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 8, 0, 8),
            };
            cap.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            stack.Children.Add(cap);
            var wrap = new WrapPanel();
            foreach (var light in group.OrderBy(l => l.FriendlyName, StringComparer.OrdinalIgnoreCase))
                wrap.Children.Add(BuildLightCard(light));
            stack.Children.Add(wrap);
        }

        _bodyHost.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        };
        UpdateFooter();
    }

    private Border BuildLightCard(HALightInfo light)
    {
        var a = ThemeManager.Accent;
        bool added = _alreadyAdded.Contains(light.EntityId);

        // Tile shows the light's current colour when HA reports one, else a capability tint.
        Color tileColor = light.Rgb is { } rgb && light.IsOn
            ? Color.FromRgb(rgb.R, rgb.G, rgb.B)
            : light.SupportsColor ? Color.FromRgb(0xB3, 0x88, 0xFF)
            : light.SupportsColorTemp ? Color.FromRgb(0xFF, 0xC1, 0x07)
            : HaColor;
        var icon = light.SupportsColor ? MaterialIconKind.Palette
            : light.IsOn ? MaterialIconKind.LightbulbOn : MaterialIconKind.LightbulbOutline;
        var tile = UiKit.IconTile(tileColor, icon, 36);
        tile.Margin = new Thickness(0, 0, 10, 0);
        tile.VerticalAlignment = VerticalAlignment.Top;
        // Repeated list rows: no per-row glow (perf); the tile colour already shows on/off

        var name = new TextBlock { Text = light.FriendlyName, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var detail = new TextBlock { Text = light.EntityId, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        // State + capabilities line: "● On · 70% · Color"
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6, Height = 6, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(light.IsUnavailable ? Color.FromRgb(0xFF, 0x44, 0x44)
                : light.IsOn ? Color.FromRgb(0x00, 0xDD, 0x77) : Color.FromRgb(0x77, 0x77, 0x77)),
        };
        stateRow.Children.Add(dot);
        var parts = new List<string> { light.IsUnavailable ? "Unavailable" : light.IsOn ? "On" : light.State.Length > 0 ? "Off" : "Unknown" };
        if (light.IsOn && light.Brightness is int bri) parts.Add($"{bri * 100 / 255}%");
        parts.Add(light.SupportsColor ? "Color" : light.SupportsColorTemp ? "White tones" : light.SupportsBrightness ? "Dimmable" : "On/off");
        var stateText = new TextBlock { Text = string.Join("  ·  ", parts), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        stateText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stateRow.Children.Add(stateText);

        var texts = new StackPanel();
        texts.Children.Add(name);
        texts.Children.Add(detail);
        texts.Children.Add(stateRow);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(tile);
        Grid.SetColumn(texts, 1);
        content.Children.Add(texts);

        // Tick overlay (top-right) / "Added" badge
        var tick = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -4, 0),
            Background = new SolidColorBrush(a),
            Child = new MaterialIcon { Kind = MaterialIconKind.Check, Width = 12, Height = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)) },
            Visibility = Visibility.Collapsed,
        };
        var addedBadge = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -4, 0),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(HaColor, 0x26)),
            Child = new TextBlock { Text = "Added", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(HaColor) },
            Visibility = added ? Visibility.Visible : Visibility.Collapsed,
        };
        var overlay = new Grid();
        overlay.Children.Add(content);
        overlay.Children.Add(tick);
        overlay.Children.Add(addedBadge);

        var card = new Border
        {
            Width = 214, Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Cursor = added ? Cursors.Arrow : Cursors.Hand,
            Opacity = added ? 0.45 : 1.0,
            ToolTip = added ? "Already in Room > Devices" : light.Area.Length > 0 ? $"{light.FriendlyName} ({light.Area})" : light.FriendlyName,
            Child = overlay,
        };

        bool hover = false;
        void Paint()
        {
            bool sel = _selected.Contains(light.EntityId);
            tick.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
            if (sel)
            {
                card.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x1C));
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0xAA));
            }
            else
            {
                card.SetResourceReference(Border.BackgroundProperty, hover && !added ? "InputBgBrush" : "CardBgBrush");
                card.BorderBrush = hover && !added
                    ? new SolidColorBrush(ThemeManager.WithAlpha(a, 0x55))
                    : (Brush)Application.Current.FindResource("CardBorderBrush");
            }
        }
        _repaintCard[light.EntityId] = Paint;
        if (!added)
        {
            card.MouseEnter += (_, _) => { hover = true; Paint(); };
            card.MouseLeave += (_, _) => { hover = false; Paint(); };
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (!_selected.Remove(light.EntityId)) _selected.Add(light.EntityId);
                Paint();
                UpdateFooter();
            };
        }
        Paint();
        return card;
    }

    private List<HALightInfo> SelectableVisible()
        => FilteredLights().Where(l => !_alreadyAdded.Contains(l.EntityId)).ToList();

    private void ToggleSelectAll()
    {
        if (_loadingLights) return;
        var selectable = SelectableVisible();
        if (selectable.Count == 0) return;
        bool allSelected = selectable.All(l => _selected.Contains(l.EntityId));
        foreach (var l in selectable)
        {
            if (allSelected) _selected.Remove(l.EntityId);
            else _selected.Add(l.EntityId);
            if (_repaintCard.TryGetValue(l.EntityId, out var paint)) paint();
        }
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        int total = _lights.Count;
        int added = _lights.Count(l => _alreadyAdded.Contains(l.EntityId));
        int sel = _selected.Count;
        _footerCount.Text = _loadingLights || total == 0 ? ""
            : sel > 0 ? $"{sel} selected"
            : added > 0 ? $"{total} lights  ·  {added} already added"
            : total == 1 ? "1 light found" : $"{total} lights found";
        _footerCount.Foreground = sel > 0
            ? new SolidColorBrush(ThemeManager.Accent)
            : (Brush)Application.Current.FindResource("TextSecBrush");

        var selectable = _loadingLights ? new List<HALightInfo>() : SelectableVisible();
        _selectAll.Opacity = selectable.Count > 0 ? 1.0 : 0.4;
        _selectAllLabel.Text = selectable.Count > 0 && selectable.All(l => _selected.Contains(l.EntityId)) ? "Select none" : "Select all";

        _primary.Opacity = sel > 0 ? 1.0 : 0.4;
        _primary.Cursor = sel > 0 ? Cursors.Hand : Cursors.Arrow;
        if (_primary.Child is TextBlock lbl)
            lbl.Text = sel > 1 ? $"Add {sel} lights" : sel == 1 ? "Add 1 light" : "Add";
    }

    private void Confirm()
    {
        if (_selected.Count == 0) return;
        _result = _lights.Where(l => _selected.Contains(l.EntityId) && !_alreadyAdded.Contains(l.EntityId)).ToList();
        Close();
    }

    private Border MakeButton(string text, bool primary, Action onClick, out TextBlock label)
    {
        var a = ThemeManager.Accent;
        var lbl = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        label = lbl;
        var b = new Border
        {
            MinWidth = 88,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = lbl,
        };
        void Paint(bool hover)
        {
            if (primary)
            {
                bool lit = hover && _selected.Count > 0;
                var bg = lit
                    ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                    : a;
                b.Background = new SolidColorBrush(bg);
                b.BorderBrush = new SolidColorBrush(bg);
                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
            }
            else
            {
                b.Background = hover ? (Brush)Application.Current.FindResource("InputBgBrush") : Brushes.Transparent;
                b.BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush");
                lbl.Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush");
            }
        }
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, _) => onClick();
        Paint(false);
        return b;
    }
}
