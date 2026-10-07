using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Searchable, categorized target picker (knob / encoder TARGET).
///
/// Trigger: an input-style row showing the current choice's icon tile + friendly name.
/// Click opens a borderless Window flyout (not a Popup — Win11 click-through + HWND
/// isolation issues) with a search box at the top and accent-bar category sections.
/// Each item is an icon tile + title + one-line description. Items that have children
/// (devices, HA entities, Govee lights…) expand INLINE as an accordion — there are no
/// hover-flyout chains. The search box filters items AND their children.
///
/// Keyboard: type to search, Up/Down to move, Right/Left to expand/collapse,
/// Enter to choose, Esc to clear the search or close.
/// </summary>
public class GridPicker : System.Windows.Controls.Border
{
    // ── Model ─────────────────────────────────────────────────────

    private sealed class Entry
    {
        public string Display = "";
        public object? Tag;
        public string? Icon;           // legacy glyph text or a MaterialIconKind name
        public MaterialIconKind? Kind;
        public Color? IconColor;
        public string? Subtitle;
        public ImageSource? Image;
        public string? Keywords;
        public string? GroupKey;      // member of an accordion group (rendered under its header)
        public string? HeaderKey;     // this entry is an accordion group header (not selectable)
    }

    /// <summary>
    /// A child option of an expandable item (device, HA entity, monitor…).
    /// <paramref name="Icon"/> may be a glyph or a MaterialIconKind name; <paramref name="Kind"/>
    /// / <paramref name="Image"/> take precedence when set.
    /// </summary>
    public record SubItem(string Display, string Tag, string? Icon = null, Color? IconColor = null,
        MaterialIconKind? Kind = null, string? Subtitle = null, ImageSource? Image = null);

    private readonly List<Entry> _items = new();
    private readonly List<(int ItemIndex, string CategoryName)> _categories = new();
    private readonly Dictionary<string, Func<List<SubItem>>> _subMenuProviders = new();
    private readonly HashSet<string> _multiSelectParents = new();
    private readonly Dictionary<string, Func<List<SubItem>>> _dynamicProviders = new(StringComparer.OrdinalIgnoreCase);

    private int _selectedIndex = -1;
    private string? _selectedSubTag;
    private readonly HashSet<string> _selectedSubTags = new(StringComparer.OrdinalIgnoreCase);
    private string? _labelOverride;
    private SubItem? _selectedSubItem;

    // ── Trigger visuals ───────────────────────────────────────────

    private readonly TextBlock _label;
    private readonly ContentControl _triggerIconHost;
    private readonly MaterialIcon _chevron;
    private readonly TextBlock _triggerSub;

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

    public event EventHandler? SelectionChanged;

    public Color AccentColor { get; set; } = ThemeManager.Accent;

    /// <summary>
    /// Sub-item tag of the current selection (device id, entity id, Govee IP…), or null.
    /// For multi-select parents, returns semicolon-separated tags.
    /// </summary>
    public string? SelectedSubTag =>
        SelectedTag is string t && _multiSelectParents.Contains(t)
            ? (_selectedSubTags.Count > 0 ? string.Join(";", _selectedSubTags) : null)
            : _selectedSubTag;

    /// <summary>Checked tags for multi-select sub-menus.</summary>
    public IReadOnlyCollection<string> SelectedSubTags => _selectedSubTags;

    public GridPicker()
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

        _label = new TextBlock
        {
            Text = "Choose…",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        // Muted one-line description under the name, same as the rows in the open list.
        _triggerSub = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
        };
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

    public void RefreshAccent()
    {
        AccentColor = ThemeManager.Accent;
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    // ── Item management (public API) ─────────────────────────────

    public void AddCategory(string categoryName) => _categories.Add((_items.Count, categoryName));

    /// <summary>Legacy overload: <paramref name="icon"/> may be a glyph or a MaterialIconKind name.</summary>
    public void AddItem(string display, object? tag = null, string? icon = null, Color? iconColor = null, string? subtitle = null)
    {
        _items.Add(new Entry { Display = display, Tag = tag, Icon = icon, IconColor = iconColor, Subtitle = subtitle });
    }

    /// <summary>Modern overload: Material icon in a tinted tile + one-line description.</summary>
    public void AddItem(string display, object? tag, MaterialIconKind kind, Color color, string? description = null,
        ImageSource? image = null, string? keywords = null)
    {
        _items.Add(new Entry { Display = display, Tag = tag, Kind = kind, IconColor = color, Subtitle = description, Image = image, Keywords = keywords });
    }

    /// <summary>Modern overload that files the item under an accordion group created with <see cref="AddGroup"/>.</summary>
    public void AddItem(string display, object? tag, MaterialIconKind kind, Color color, string? description, string group)
    {
        _items.Add(new Entry { Display = display, Tag = tag, Kind = kind, IconColor = color, Subtitle = description, GroupKey = group });
    }

    /// <summary>
    /// Adds a non-selectable accordion header. Items added with the same <paramref name="groupKey"/>
    /// are shown inside it (still regular items: SelectedTag / GetTagAt / SelectByTag work as usual).
    /// </summary>
    public void AddGroup(string display, string groupKey, MaterialIconKind kind, Color color, string? description = null)
    {
        _items.Add(new Entry { Display = display, Tag = null, Kind = kind, IconColor = color, Subtitle = description, HeaderKey = groupKey });
    }

    /// <summary>
    /// Insert an item at the end of the named category (falls back to appending when the
    /// category doesn't exist). Returns the new item's index.
    /// </summary>
    public int AddItemToCategory(string categoryName, string display, object? tag, MaterialIconKind kind, Color color,
        string? description = null, ImageSource? image = null)
    {
        var entry = new Entry { Display = display, Tag = tag, Kind = kind, IconColor = color, Subtitle = description, Image = image };
        int c = _categories.FindIndex(x => string.Equals(x.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));
        if (c < 0)
        {
            _items.Add(entry);
            return _items.Count - 1;
        }
        int insertAt = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
        _items.Insert(insertAt, entry);
        for (int k = c + 1; k < _categories.Count; k++)
            _categories[k] = (_categories[k].ItemIndex + 1, _categories[k].CategoryName);
        if (_selectedIndex >= insertAt) _selectedIndex++;
        return insertAt;
    }

    /// <summary>
    /// Items computed each time the flyout opens (e.g. currently-playing apps), shown at the
    /// end of <paramref name="categoryName"/>. Entries whose tag already exists are skipped.
    /// Choosing one adds it to the picker permanently so SelectedTag works as usual.
    /// </summary>
    public void RegisterDynamicItems(string categoryName, Func<List<SubItem>> provider)
        => _dynamicProviders[categoryName] = provider;

    public void RegisterSubMenu(string parentTag, Func<List<SubItem>> provider) => _subMenuProviders[parentTag] = provider;

    public void RegisterMultiSelectSubMenu(string parentTag, Func<List<SubItem>> provider)
    {
        _subMenuProviders[parentTag] = provider;
        _multiSelectParents.Add(parentTag);
    }

    public void ClearSubMenus()
    {
        _subMenuProviders.Clear();
        _multiSelectParents.Clear();
    }

    public void ClearItems()
    {
        _items.Clear();
        _categories.Clear();
        _subMenuProviders.Clear();
        _multiSelectParents.Clear();
        _dynamicProviders.Clear();
        _selectedIndex = -1;
        _selectedSubTag = null;
        _selectedSubItem = null;
        _labelOverride = null;
        _selectedSubTags.Clear();
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    public void RefreshOpenSubMenu()
    {
        if (!_isOpen) return;
        _childCache.Clear();
        RebuildList();
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value >= 0 && value < _items.Count ? value : -1;
            _selectedSubTag = null;
            _selectedSubItem = null;
            _labelOverride = null;
            _selectedSubTags.Clear();
            UpdateTrigger();
            if (_isOpen) RebuildList();
        }
    }

    public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Tag : null;

    public string SelectedDisplay => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Display : "";

    public int ItemCount => _items.Count;

    public object? GetTagAt(int index) => index >= 0 && index < _items.Count ? _items[index].Tag : null;

    /// <summary>Select by tag, with optional sub-tag; the trigger shows displayOverride if provided.</summary>
    public void SelectByTag(string tag, string? subTag = null, string? displayOverride = null)
    {
        int i = _items.FindIndex(x => x.Tag as string == tag);
        if (i < 0) return;
        _selectedIndex = i;
        _selectedSubTag = subTag;
        _selectedSubTags.Clear();
        _labelOverride = displayOverride;
        _selectedSubItem = subTag != null ? GetChildren(tag, useCache: false).FirstOrDefault(s => s.Tag == subTag) : null;
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    /// <summary>Select a multi-select parent with several checked sub-tags.</summary>
    public void SelectByTags(string parentTag, IEnumerable<string> subTags, string displayOverride)
    {
        int i = _items.FindIndex(x => x.Tag as string == parentTag);
        if (i < 0) return;
        _selectedSubTags.Clear();
        foreach (var t in subTags) _selectedSubTags.Add(t);
        _selectedIndex = i;
        _selectedSubTag = null;
        _selectedSubItem = null;
        _labelOverride = displayOverride;
        UpdateTrigger();
        if (_isOpen) RebuildList();
    }

    // ── Trigger ───────────────────────────────────────────────────

    private void UpdateTrigger()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            _label.Text = "Choose…";
            _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _triggerSub.Visibility = Visibility.Collapsed;
            _triggerIconHost.Content = BuildTile(MaterialIconKind.GestureTap, null, null, Color.FromRgb(0x88, 0x88, 0x88), 32);
            return;
        }
        var e = _items[_selectedIndex];
        _label.Text = _labelOverride ?? _selectedSubItem?.Display ?? e.Display;
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        ToolTipService.SetToolTip(_label, e.Subtitle);

        var s = _selectedSubItem;
        // Sub-selection (a specific device / entity): show which kind of target it is underneath.
        string? subText = s != null ? e.Display : e.Subtitle;
        _triggerSub.Text = subText ?? "";
        _triggerSub.Visibility = string.IsNullOrWhiteSpace(subText) || _labelOverride != null && subText == _label.Text
            ? Visibility.Collapsed : Visibility.Visible;
        _triggerIconHost.Content = s != null && (s.Kind != null || s.Image != null)
            ? BuildTile(s.Kind, s.Icon, s.Image, s.IconColor ?? e.IconColor ?? AccentColor, 32)
            : BuildTile(e.Kind, e.Icon, e.Image, e.IconColor ?? AccentColor, 32);
    }

    // ── Icon helpers ──────────────────────────────────────────────

    private static MaterialIconKind? ParseKind(string? icon)
        => !string.IsNullOrEmpty(icon) && icon.Length > 2 && Enum.TryParse<MaterialIconKind>(icon, out var k) ? k : null;

    /// <summary>Tinted rounded tile holding an app image, a Material icon, or a legacy glyph.</summary>
    private static FrameworkElement BuildTile(MaterialIconKind? kind, string? glyph, ImageSource? image, Color color, double size)
    {
        UIElement inner;
        double glyphSize = Math.Round(size * 0.55);
        if (image != null)
        {
            inner = new Image { Source = image, Width = size * 0.64, Height = size * 0.64, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(inner, BitmapScalingMode.HighQuality);
        }
        else
        {
            var k = kind ?? ParseKind(glyph);
            if (k != null || string.IsNullOrWhiteSpace(glyph))
                inner = new MaterialIcon { Kind = k ?? MaterialIconKind.CircleSmall, Width = glyphSize, Height = glyphSize, Foreground = new SolidColorBrush(color) };
            else
                inner = new TextBlock
                {
                    Text = glyph, FontSize = glyphSize * 0.9, Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
        }
        return new System.Windows.Controls.Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x50)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = inner,
        };
    }

    // ── Flyout ────────────────────────────────────────────────────

    private void OpenFlyout()
    {
        _query = "";
        _childCache.Clear();
        _expanded.Clear();
        // Auto-expand the parent of the current sub-selection so the user sees it.
        if (SelectedTag is string st && (_selectedSubTag != null || _selectedSubTags.Count > 0) && _subMenuProviders.ContainsKey(st))
            _expanded.Add(st);
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count && _items[_selectedIndex].GroupKey is { } gk)
            _expanded.Add("grp:" + gk);

        // Search box
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
            Text = "Search targets, apps, devices…",
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
        searchGrid.Children.Add(UiKit.Icon(MaterialIconKind.Magnify, 16));
        ((FrameworkElement)searchGrid.Children[0]).Margin = new Thickness(0, 0, 8, 0);
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

        // Footer hint
        var hint = new TextBlock
        {
            Text = "↑ ↓ to move  ·  Enter to choose  ·  Esc to close",
            FontSize = 10,
            Margin = new Thickness(4, 8, 0, 0),
        };
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

        // ── Placement (DPI-aware, flips upward when there's no room below) ──
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
        double height = Math.Clamp(openUp ? spaceAbove : spaceBelow, 260, 560);
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

        // Bring the current selection into view once layout has run.
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
            case Key.Down:
                MoveNav(+1); e.Handled = true; break;
            case Key.Up:
                MoveNav(-1); e.Handled = true; break;
            case Key.PageDown:
                MoveNav(+6); e.Handled = true; break;
            case Key.PageUp:
                MoveNav(-6); e.Handled = true; break;
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

    private List<SubItem> GetChildren(string tag, bool useCache = true)
    {
        if (!_subMenuProviders.TryGetValue(tag, out var provider)) return new();
        if (useCache && _childCache.TryGetValue(tag, out var cached)) return cached;
        List<SubItem> list;
        try { list = provider() ?? new(); } catch { list = new(); }
        if (useCache) _childCache[tag] = list;
        return list;
    }

    private bool Matches(params string?[] fields)
    {
        if (_query.Length == 0) return true;
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hay = string.Join(" ", fields.Where(f => !string.IsNullOrEmpty(f)));
        return terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildList()
    {
        if (_listPanel == null) return;
        var prevNavKey = _navIndex >= 0 && _navIndex < _navRows.Count ? _navRows[_navIndex].Row.Tag : null;
        // Clearing the panel collapses the scroll extent, which snaps the list back to the top
        // (expanding a section mid-list jumped to the top). Remember and restore the offset;
        // search explicitly ScrollToTop()s after rebuilding, which still wins.
        double keepOffset = _scroll?.VerticalOffset ?? 0;
        _listPanel.Children.Clear();
        _navRows.Clear();
        _navIndex = -1;
        _selectedRowVisual = null;

        // Group item indices by category (items before the first category form an unnamed group)
        var groups = new List<(string? Name, List<int> Indices)>();
        int firstCat = _categories.Count > 0 ? _categories[0].ItemIndex : _items.Count;
        if (firstCat > 0) groups.Add((null, Enumerable.Range(0, Math.Min(firstCat, _items.Count)).ToList()));
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            groups.Add((_categories[c].CategoryName, Enumerable.Range(start, Math.Max(0, end - start)).ToList()));
        }

        var existingTags = new HashSet<string>(_items.Select(i => i.Tag as string ?? "").Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);
        int shown = 0;

        foreach (var (name, indices) in groups)
        {
            var section = new StackPanel();
            int sectionCount = 0;

            foreach (int i in indices)
            {
                var e = _items[i];
                if (e.GroupKey != null) continue; // rendered under its header
                if (e.HeaderKey != null)
                {
                    var g = BuildGroup(i, e, name);
                    if (g != null) { section.Children.Add(g); sectionCount++; }
                    continue;
                }
                string tag = e.Tag as string ?? "";
                var children = tag.Length > 0 ? GetChildren(tag) : new List<SubItem>();
                bool hasChildren = children.Count > 0;
                bool selfMatch = Matches(e.Display, e.Subtitle, e.Keywords, tag, name);
                var matchedChildren = hasChildren
                    ? (_query.Length == 0 ? children : children.Where(s => Matches(s.Display, s.Subtitle, s.Tag)).ToList())
                    : children;
                if (!selfMatch && matchedChildren.Count == 0) continue;

                bool expanded = hasChildren && (_expanded.Contains(tag) || (_query.Length > 0 && matchedChildren.Count > 0 && !selfMatch));
                var visibleChildren = _query.Length > 0 && !selfMatch ? matchedChildren : children;

                section.Children.Add(BuildItemRow(i, e, tag, hasChildren, expanded));
                sectionCount++;
                if (expanded)
                    section.Children.Add(BuildChildren(i, tag, visibleChildren));
            }

            // Dynamic items (e.g. currently-playing apps)
            if (name != null && _dynamicProviders.TryGetValue(name, out var dyn))
            {
                List<SubItem> extras;
                try { extras = dyn() ?? new(); } catch { extras = new(); }
                foreach (var s in extras)
                {
                    if (existingTags.Contains(s.Tag) || !Matches(s.Display, s.Subtitle, s.Tag, name)) continue;
                    existingTags.Add(s.Tag);
                    section.Children.Add(BuildDynamicRow(name, s));
                    sectionCount++;
                }
            }

            if (sectionCount == 0) continue;
            if (name != null)
            {
                var header = UiKit.SectionHeader(name, null);
                ((FrameworkElement)header).Margin = new Thickness(4, shown == 0 ? 2 : 14, 0, 6);
                _listPanel.Children.Add(header);
            }
            _listPanel.Children.Add(section);
            shown++;
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

        // Restore / initialise keyboard highlight
        int restore = prevNavKey != null ? _navRows.FindIndex(r => Equals(r.Row.Tag, prevNavKey)) : -1;
        if (restore < 0 && _query.Length > 0 && _navRows.Count > 0) restore = 0;
        if (restore < 0 && _selectedRowVisual != null) restore = _navRows.FindIndex(r => r.Row == _selectedRowVisual);
        if (restore >= 0) SetNav(restore);
        RestoreScroll(keepOffset);
    }

    private void RestoreScroll(double offset)
    {
        if (_scroll == null || offset <= 0) return;
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(Math.Min(offset, _scroll.ScrollableHeight));
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

    private FrameworkElement? BuildGroup(int headerIndex, Entry header, string? category)
    {
        string key = header.HeaderKey!;
        string expKey = "grp:" + key;
        var members = Enumerable.Range(0, _items.Count).Where(i => _items[i].GroupKey == key).ToList();
        if (members.Count == 0) return null;

        bool headerMatch = Matches(header.Display, header.Subtitle, header.Keywords, category);
        var matched = members.Where(i => Matches(_items[i].Display, _items[i].Subtitle, _items[i].Tag as string, header.Display)).ToList();
        if (!headerMatch && matched.Count == 0) return null;

        bool memberSelected = members.Contains(_selectedIndex);
        bool expanded = _expanded.Contains(expKey) || (_query.Length > 0 && !headerMatch);
        var visible = _query.Length > 0 && !headerMatch ? matched : members;

        var container = new StackPanel();
        var row = MakeRowShell(false, memberSelected);
        row.Tag = "group:" + key;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = BuildTile(header.Kind, header.Icon, header.Image, header.IconColor ?? AccentColor, 32);
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MakeTitle(header.Display, memberSelected));
        var desc = memberSelected ? "Current: " + _items[_selectedIndex].Display : header.Subtitle;
        if (!string.IsNullOrEmpty(desc)) text.Children.Add(MakeDescription(desc));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var chev = UiKit.Icon(expanded ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown, 18, memberSelected ? "AccentBrush" : "TextDimBrush");
        chev.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(chev, 2);
        grid.Children.Add(chev);
        row.Child = grid;

        Action toggle = () => { if (!_expanded.Remove(expKey)) _expanded.Add(expKey); RebuildList(); };
        row.MouseLeftButtonUp += (_, ev) => { toggle(); ev.Handled = true; };
        if (memberSelected && !expanded) _selectedRowVisual = row;
        _navRows.Add((row, toggle, expanded ? null : toggle, expanded ? toggle : null));
        container.Children.Add(row);

        if (expanded)
        {
            var panel = new StackPanel { Margin = new Thickness(26, 0, 0, 6) };
            var outer = new Grid();
            var guide = new System.Windows.Controls.Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(15, 2, 0, 8), CornerRadius = new CornerRadius(1) };
            guide.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBorderBrush");
            outer.Children.Add(guide);
            outer.Children.Add(panel);
            foreach (int m in visible)
                panel.Children.Add(BuildItemRow(m, _items[m], _items[m].Tag as string ?? "", false, false, compact: true));
            container.Children.Add(outer);
        }
        return container;
    }

    private FrameworkElement BuildItemRow(int index, Entry e, string tag, bool hasChildren, bool expanded, bool compact = false)
    {
        bool isSelected = index == _selectedIndex;
        bool hasSubSelection = isSelected && (_selectedSubTag != null || _selectedSubTags.Count > 0);
        bool showSelected = isSelected && !hasChildren;
        bool multi = _multiSelectParents.Contains(tag);
        if (isSelected && hasChildren && multi && _selectedSubTags.Count == 0) showSelected = false;

        var row = MakeRowShell(showSelected, isSelected && hasChildren);
        row.Tag = "item:" + (tag.Length > 0 ? tag : index.ToString());

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = BuildTile(e.Kind, e.Icon, e.Image, e.IconColor ?? AccentColor, compact ? 24 : 32);
        tile.Margin = new Thickness(0, 0, compact ? 10 : 12, 0);
        grid.Children.Add(tile);
        if (compact) row.Padding = new Thickness(8, 5, 10, 5);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MakeTitle(e.Display, showSelected || (isSelected && hasChildren)));
        string? desc = e.Subtitle;
        if (isSelected && hasChildren)
        {
            if (multi)
                desc = _selectedSubTags.Count == 0 ? "Current: all" : $"Current: {_labelOverride ?? _selectedSubTags.Count + " selected"}";
            else if (hasSubSelection)
                desc = "Current: " + (_selectedSubItem?.Display ?? _labelOverride ?? _selectedSubTag);
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

        Action toggle = () =>
        {
            if (!_expanded.Remove(tag)) _expanded.Add(tag);
            RebuildList();
        };
        Action activate = hasChildren ? toggle : () => CommitItem(index);
        row.MouseLeftButtonUp += (_, ev) => { activate(); ev.Handled = true; };

        if (showSelected || (isSelected && !hasSubSelection && !hasChildren)) _selectedRowVisual = row;
        if (isSelected && hasChildren && _selectedRowVisual == null && !expanded) _selectedRowVisual = row;

        _navRows.Add((row, activate,
            hasChildren && !expanded ? toggle : null,
            hasChildren && expanded ? toggle : null));
        return row;
    }

    private FrameworkElement BuildChildren(int parentIndex, string parentTag, List<SubItem> children)
    {
        bool multi = _multiSelectParents.Contains(parentTag);
        var parent = _items[parentIndex];
        var panel = new StackPanel { Margin = new Thickness(26, 0, 0, 6) };

        // Thin guide line on the left, like a tree
        var outer = new Grid();
        var guide = new System.Windows.Controls.Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(15, 2, 0, 8), CornerRadius = new CornerRadius(1) };
        guide.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBorderBrush");
        outer.Children.Add(guide);
        outer.Children.Add(panel);

        bool isParentSelected = parentIndex == _selectedIndex;
        foreach (var s in children)
        {
            bool selected = multi
                ? isParentSelected && (string.IsNullOrEmpty(s.Tag) ? _selectedSubTags.Count == 0 : _selectedSubTags.Contains(s.Tag))
                : isParentSelected && _selectedSubTag == s.Tag;

            var row = MakeRowShell(selected && !multi);
            row.Padding = new Thickness(8, 5, 10, 5);
            row.Tag = "sub:" + parentTag + ":" + s.Tag;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement lead = multi
                ? UiKit.Icon(selected ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline, 18, selected ? "AccentBrush" : "TextDimBrush")
                : BuildTile(s.Kind, s.Icon, s.Image, s.IconColor ?? parent.IconColor ?? AccentColor, 24);
            lead.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(lead);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = MakeTitle(s.Display, selected);
            title.FontSize = 12;
            title.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            text.Children.Add(title);
            if (!string.IsNullOrEmpty(s.Subtitle)) text.Children.Add(MakeDescription(s.Subtitle));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (selected && !multi)
            {
                var chk = UiKit.Icon(MaterialIconKind.Check, 16, "AccentBrush");
                chk.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(chk, 2);
                grid.Children.Add(chk);
            }
            row.Child = grid;

            var sub = s;
            Action activate = multi ? () => ToggleMulti(parentIndex, parentTag, sub) : () => CommitSub(parentIndex, sub);
            row.MouseLeftButtonUp += (_, ev) => { activate(); ev.Handled = true; };
            if (selected && _selectedRowVisual == null) _selectedRowVisual = row;
            _navRows.Add((row, activate, null, () => { _expanded.Remove(parentTag); RebuildList(); }));
            panel.Children.Add(row);
        }
        return outer;
    }

    private FrameworkElement BuildDynamicRow(string category, SubItem s)
    {
        var row = MakeRowShell(false);
        row.Tag = "dyn:" + s.Tag;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = BuildTile(s.Kind, s.Icon, s.Image, s.IconColor ?? AccentColor, 32);
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MakeTitle(s.Display, false));
        if (!string.IsNullOrEmpty(s.Subtitle)) text.Children.Add(MakeDescription(s.Subtitle));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        row.Child = grid;

        Action activate = () =>
        {
            int idx = _items.FindIndex(x => string.Equals(x.Tag as string, s.Tag, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                idx = AddItemToCategory(category, s.Display, s.Tag, s.Kind ?? MaterialIconKind.Application,
                    s.IconColor ?? AccentColor, s.Subtitle, s.Image);
            }
            CommitItem(idx);
        };
        row.MouseLeftButtonUp += (_, ev) => { activate(); ev.Handled = true; };
        _navRows.Add((row, activate, null, null));
        return row;
    }

    // ── Commit ────────────────────────────────────────────────────

    private void CommitItem(int index)
    {
        _selectedIndex = index;
        _selectedSubTag = null;
        _selectedSubItem = null;
        _labelOverride = null;
        _selectedSubTags.Clear();
        UpdateTrigger();
        CloseFlyout();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitSub(int parentIndex, SubItem s)
    {
        _selectedIndex = parentIndex;
        _selectedSubTag = s.Tag;
        _selectedSubItem = s;
        _labelOverride = null;
        _selectedSubTags.Clear();
        UpdateTrigger();
        CloseFlyout();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleMulti(int parentIndex, string parentTag, SubItem s)
    {
        if (_selectedIndex != parentIndex)
        {
            _selectedSubTags.Clear();
            _selectedSubTag = null;
            _selectedSubItem = null;
        }
        _selectedIndex = parentIndex;
        if (string.IsNullOrEmpty(s.Tag)) _selectedSubTags.Clear();
        else if (!_selectedSubTags.Remove(s.Tag)) _selectedSubTags.Add(s.Tag);

        var children = GetChildren(parentTag);
        var parentName = _items[parentIndex].Display;
        if (_selectedSubTags.Count == 0)
            _labelOverride = children.FirstOrDefault(c => string.IsNullOrEmpty(c.Tag))?.Display ?? parentName;
        else if (_selectedSubTags.Count == 1)
        {
            var one = _selectedSubTags.First();
            _labelOverride = children.FirstOrDefault(c => string.Equals(c.Tag, one, StringComparison.OrdinalIgnoreCase))?.Display ?? one;
        }
        else
            _labelOverride = $"{parentName} ({_selectedSubTags.Count})";

        UpdateTrigger();
        RebuildList();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
