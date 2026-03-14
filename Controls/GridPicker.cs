using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AmpUp.Controls;

/// <summary>
/// A categorized target picker. Shows current selection as a styled row;
/// click opens a dark borderless Window flyout with categorized items as full-width text rows.
/// Items with sub-menus expand an inline sub-panel to the right within the same Window.
/// Uses a borderless Window instead of Popup to avoid Win11 click-through bugs.
/// </summary>
public class GridPicker : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly StackPanel _categoriesPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly Border _popupBorder;

    // Main flyout
    private Window? _flyout;
    private bool _isOpen = false;

    // Inline sub-panel (replaces second Window)
    private readonly Border _subPanelBorder;
    private readonly Border _subDivider;
    private readonly StackPanel _subItemsPanel;
    private readonly ScrollViewer _subScrollViewer;
    private readonly TextBox _subFilterBox;
    private readonly TextBlock _subFilterPlaceholder;
    private readonly TextBlock _subHeaderLabel;
    private string _subFilterText = "";
    private object? _activeSubParentTag;
    private List<SubItem> _activeSubItems = new();
    private readonly DispatcherTimer _subOpenTimer;
    private int _hoveredSubMenuIdx = -1;

    private int _selectedIndex = -1;
    private string? _selectedSubTag; // stores the sub-item tag when a sub-menu item is selected
    private readonly List<(string Display, object? Tag, string? Icon, Color? IconColor)> _items = new();
    private readonly List<(int ItemIndex, string CategoryName)> _categories = new();

    // Sub-menu providers: keyed by item tag string
    private readonly Dictionary<string, Func<List<SubItem>>> _subMenuProviders = new();

    public event EventHandler? SelectionChanged;

    public Color AccentColor { get; set; } = ThemeManager.Accent;

    /// <summary>
    /// When a sub-menu item is selected, SelectedTag returns "parentTag:subTag".
    /// SelectedSubTag returns just the sub-item tag, or null if no sub-item.
    /// </summary>
    public string? SelectedSubTag => _selectedSubTag;

    // Category icons and colors for visual identity
    private static readonly Dictionary<string, (string Icon, Color Color)> CategoryStyles = new()
    {
        { "AUDIO",        ("♪", Color.FromRgb(0x64, 0xB5, 0xF6)) },  // blue
        { "DEVICES",      ("⬡", Color.FromRgb(0xBA, 0x68, 0xC8)) },  // purple
        { "INTEGRATIONS", ("◈", Color.FromRgb(0xFF, 0xB7, 0x4D)) },  // amber
        { "APPS",         ("◉", Color.FromRgb(0x66, 0xBB, 0x6A)) },  // green
    };

    public record SubItem(string Display, string Tag, string? Icon = null, Color? IconColor = null);

    public void RefreshAccent()
    {
        AccentColor = ThemeManager.Accent;
        if (_selectedIndex >= 0)
            _label.Foreground = new SolidColorBrush(AccentColor);
        RebuildPopupItems();
    }

    public GridPicker()
    {
        // Main trigger — looks like a proper input field
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12, 8, 8, 8);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        MinHeight = 36;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = "Select...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_label, 0);
        grid.Children.Add(_label);

        _chevron = new TextBlock
        {
            Text = "▾",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        };
        Grid.SetColumn(_chevron, 1);
        grid.Children.Add(_chevron);

        Child = grid;

        // Main flyout content
        _categoriesPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            MaxWidth = 280,
            Content = _categoriesPanel
        };

        // ── Inline sub-panel content ──
        _subHeaderLabel = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(8, 2, 0, 6),
        };

        _subItemsPanel = new StackPanel();
        _subScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 380,
            Content = _subItemsPanel
        };

        _subFilterBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(4, 0, 4, 6),
        };
        _subFilterPlaceholder = new TextBlock
        {
            Text = "Search...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            FontSize = 11,
            Padding = new Thickness(14, 6, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _subFilterBox.TextChanged += (_, _) =>
        {
            _subFilterText = _subFilterBox.Text.Trim();
            _subFilterPlaceholder.Visibility = string.IsNullOrEmpty(_subFilterBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            ApplySubFilter();
        };

        var subFilterContainer = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        subFilterContainer.Children.Add(_subFilterBox);
        subFilterContainer.Children.Add(_subFilterPlaceholder);

        var subStack = new StackPanel();
        subStack.Children.Add(_subHeaderLabel);
        subStack.Children.Add(subFilterContainer);
        subStack.Children.Add(_subScrollViewer);

        _subPanelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(4, 6, 4, 6),
            Child = subStack,
            MinWidth = 200,
            MaxWidth = 300,
            Visibility = Visibility.Collapsed,
        };

        // Thin vertical divider between main items and sub-panel
        _subDivider = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Margin = new Thickness(0, 10, 0, 10),
            Visibility = Visibility.Collapsed,
        };

        // Horizontal layout: main items | divider | sub-panel
        var hStack = new StackPanel { Orientation = Orientation.Horizontal };
        hStack.Children.Add(_scrollViewer);
        hStack.Children.Add(_subDivider);
        hStack.Children.Add(_subPanelBorder);

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 8, 6, 8),
            Child = hStack,
        };

        // Timer for delayed sub-menu open on hover
        _subOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _subOpenTimer.Tick += (_, _) =>
        {
            _subOpenTimer.Stop();
            if (_hoveredSubMenuIdx >= 0 && _hoveredSubMenuIdx < _items.Count)
            {
                var tag = _items[_hoveredSubMenuIdx].Tag as string;
                if (tag != null && _subMenuProviders.ContainsKey(tag))
                    OpenSubMenu(tag);
            }
        };

        // Hover on trigger
        MouseEnter += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, AccentColor.R, AccentColor.G, AccentColor.B));
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
            _chevron.Foreground = new SolidColorBrush(AccentColor);
        };
        MouseLeave += (_, _) =>
        {
            if (!_isOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            _popupBorder.MinWidth = Math.Max(ActualWidth, 200);
            if (_isOpen) CloseFlyout(); else OpenFlyout();
            e.Handled = true;
        };

        Unloaded += (_, _) => _subOpenTimer.Stop();
    }

    // ── Main flyout ───────────────────────────────────────────────

    private void OpenFlyout()
    {
        RebuildPopupItems();

        // Detach from any previous flyout so we can re-parent
        if (_popupBorder.Parent is Border oldParent)
            oldParent.Child = null;

        var outerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Child = _popupBorder,
        };

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Content = outerBorder
        };

        // Position below the trigger
        var screenPos = PointToScreen(new Point(0, ActualHeight + 4));
        _flyout.Left = screenPos.X;
        _flyout.Top = screenPos.Y;

        _flyout.Deactivated += (_, _) =>
        {
            CloseFlyout();
        };
        _flyout.KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseFlyout(); };

        // Slide-down + fade-in animation (120ms)
        var translate = new TranslateTransform(0, -8);
        _popupBorder.RenderTransform = translate;
        _popupBorder.Opacity = 0;

        _flyout.Show();
        _isOpen = true;

        var slideAnim = new DoubleAnimation(-8, 0, new Duration(TimeSpan.FromMilliseconds(120)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fadeAnim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(120)));
        translate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        _popupBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        BorderBrush = new SolidColorBrush(AccentColor);
        Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
    }

    private void CloseFlyout()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _subOpenTimer.Stop();
        CloseSubPanel();

        // Detach child before close so _popupBorder can be reused
        if (_flyout?.Content is Border b)
            b.Child = null;

        _flyout?.Close();
        _flyout = null;

        BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }

    // ── Inline sub-panel ──────────────────────────────────────────

    private void OpenSubMenu(string parentTag)
    {
        if (!_subMenuProviders.TryGetValue(parentTag, out var provider))
            return;

        var items = provider();
        if (items.Count == 0) return;

        _activeSubParentTag = parentTag;
        _activeSubItems = items;

        // Set header from parent item display name
        var parentDisplay = _items.FirstOrDefault(i => i.Tag as string == parentTag).Display ?? parentTag;
        _subHeaderLabel.Text = parentDisplay.ToUpperInvariant();

        RebuildSubItems();

        var showFilter = _activeSubItems.Count > 8;
        _subFilterBox.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterPlaceholder.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterBox.Text = "";
        _subFilterText = "";

        // Show the inline sub-panel and divider
        _subDivider.Visibility = Visibility.Visible;
        _subPanelBorder.Visibility = Visibility.Visible;

        // Refresh main items to highlight the active parent
        RebuildPopupItems();

        if (showFilter)
            _subFilterBox.Focus();
    }

    private void CloseSubPanel()
    {
        _hoveredSubMenuIdx = -1;
        _subDivider.Visibility = Visibility.Collapsed;
        _subPanelBorder.Visibility = Visibility.Collapsed;
    }

    // ── Item management ─────────────────────────────────────────

    public void AddCategory(string categoryName)
    {
        _categories.Add((_items.Count, categoryName));
    }

    public void AddItem(string display, object? tag = null, string? icon = null, Color? iconColor = null)
    {
        _items.Add((display, tag, icon, iconColor));
        RebuildPopupItems();
    }

    /// <summary>
    /// Register a sub-menu provider for items with the given tag.
    /// When hovered/clicked, an inline panel shows the provider's items.
    /// </summary>
    public void RegisterSubMenu(string parentTag, Func<List<SubItem>> provider)
    {
        _subMenuProviders[parentTag] = provider;
    }

    public void ClearSubMenus()
    {
        _subMenuProviders.Clear();
    }

    public void ClearItems()
    {
        _items.Clear();
        _categories.Clear();
        _selectedIndex = -1;
        _selectedSubTag = null;
        _label.Text = "Select...";
        _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        RebuildPopupItems();
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value >= 0 && value < _items.Count)
            {
                _selectedIndex = value;
                _label.Text = _items[value].Display;
                _label.Foreground = new SolidColorBrush(AccentColor);
                RebuildPopupItems();
            }
            else
            {
                _selectedIndex = -1;
                _label.Text = "Select...";
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
        }
    }

    public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex].Tag : null;

    public string SelectedDisplay => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex].Display : "";

    public int ItemCount => _items.Count;

    public object? GetTagAt(int index) =>
        index >= 0 && index < _items.Count ? _items[index].Tag : null;

    /// <summary>
    /// Select by tag, with optional sub-tag. Sets display label to displayOverride if provided.
    /// </summary>
    public void SelectByTag(string tag, string? subTag = null, string? displayOverride = null)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Tag as string == tag)
            {
                _selectedIndex = i;
                _selectedSubTag = subTag;
                _label.Text = displayOverride ?? _items[i].Display;
                _label.Foreground = new SolidColorBrush(AccentColor);
                RebuildPopupItems();
                return;
            }
        }
    }

    // ── Sub-items rebuild ────────────────────────────────────────

    private void RebuildSubItems()
    {
        _subItemsPanel.Children.Clear();

        for (int i = 0; i < _activeSubItems.Count; i++)
        {
            int idx = i;
            var sub = _activeSubItems[i];
            bool selected = _selectedSubTag == sub.Tag
                && _activeSubParentTag as string == _items[_selectedIndex >= 0 ? _selectedIndex : 0].Tag as string;

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // accent bar

            // Accent bar
            var accentBar = new Border
            {
                Width = 2,
                Background = selected ? new SolidColorBrush(AccentColor) : Brushes.Transparent,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 2, 6, 2)
            };
            Grid.SetColumn(accentBar, 0);
            rowGrid.Children.Add(accentBar);

            int textCol = 1;

            // Optional icon
            if (!string.IsNullOrEmpty(sub.Icon))
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var iconText = new TextBlock
                {
                    Text = sub.Icon,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(sub.IconColor ?? AccentColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(iconText, 1);
                rowGrid.Children.Add(iconText);
                textCol = 2;
            }

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var itemText = new TextBlock
            {
                Text = sub.Display,
                FontSize = 11.5,
                Foreground = selected
                    ? new SolidColorBrush(AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontWeight = selected ? FontWeights.Medium : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(itemText, textCol);
            rowGrid.Children.Add(itemText);

            var itemBorder = new Border
            {
                Padding = new Thickness(6, 6, 8, 6),
                Cursor = Cursors.Hand,
                Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 1, 2, 1),
                SnapsToDevicePixels = true,
                Child = rowGrid
            };

            // Hover
            itemBorder.MouseEnter += (_, _) =>
            {
                if (!selected)
                {
                    itemBorder.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                    itemText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                }
            };
            itemBorder.MouseLeave += (_, _) =>
            {
                if (!selected)
                {
                    itemBorder.Background = Brushes.Transparent;
                    itemText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
            };

            // Click — select sub-item
            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                var parentTag = _activeSubParentTag as string ?? "";
                var subItem = _activeSubItems[idx];

                // Find parent item index
                for (int pi = 0; pi < _items.Count; pi++)
                {
                    if (_items[pi].Tag as string == parentTag)
                    {
                        _selectedIndex = pi;
                        break;
                    }
                }

                _selectedSubTag = subItem.Tag;

                // Show friendly name on the trigger label
                _label.Text = subItem.Display;
                _label.Foreground = new SolidColorBrush(AccentColor);

                CloseFlyout();

                RebuildPopupItems();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            _subItemsPanel.Children.Add(itemBorder);
        }
    }

    private void ApplySubFilter()
    {
        if (string.IsNullOrEmpty(_subFilterText))
        {
            foreach (UIElement child in _subItemsPanel.Children)
                child.Visibility = Visibility.Visible;
            return;
        }

        for (int i = 0; i < _activeSubItems.Count && i < _subItemsPanel.Children.Count; i++)
        {
            var match = _activeSubItems[i].Display.Contains(_subFilterText, StringComparison.OrdinalIgnoreCase);
            _subItemsPanel.Children[i].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── Popup rebuild ───────────────────────────────────────────

    private Dictionary<int, int> BuildCategoryMap()
    {
        var categoryForItem = new Dictionary<int, int>();
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            for (int i = start; i < end; i++)
                categoryForItem[i] = c;
        }
        return categoryForItem;
    }

    private void RebuildPopupItems()
    {
        _categoriesPanel.Children.Clear();

        var categoryForItem = BuildCategoryMap();
        int currentCategory = -1;

        for (int i = 0; i < _items.Count; i++)
        {
            int cat = categoryForItem.GetValueOrDefault(i, -1);

            // Category header
            if (cat != currentCategory)
            {
                currentCategory = cat;

                if (cat >= 0)
                {
                    var catName = _categories[cat].CategoryName.ToUpperInvariant();
                    var (icon, color) = CategoryStyles.GetValueOrDefault(catName, ("•", AccentColor));

                    var headerRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, _categoriesPanel.Children.Count > 0 ? 10 : 2, 0, 4)
                    };

                    headerRow.Children.Add(new TextBlock
                    {
                        Text = icon,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(color),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    });

                    headerRow.Children.Add(new TextBlock
                    {
                        Text = catName,
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    });

                    _categoriesPanel.Children.Add(headerRow);
                }
            }

            // Item row
            int idx = i;
            var (display, itemTag, itemIcon, itemIconColor) = _items[i];
            bool selected = idx == _selectedIndex;
            bool hasSubMenu = itemTag is string tagStr
                && _subMenuProviders.TryGetValue(tagStr, out var subProvider)
                && subProvider().Count > 0;
            bool isActiveSubParent = hasSubMenu && _subPanelBorder.Visibility == Visibility.Visible
                && itemTag as string == _activeSubParentTag as string;

            var itemRow = new Border
            {
                Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x18, AccentColor.R, AccentColor.G, AccentColor.B))
                    : isActiveSubParent
                        ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                        : Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(2, 1, 2, 1),
                Cursor = Cursors.Hand,
            };

            var itemPanel = new DockPanel();

            // Selected indicator — left accent bar
            if (selected)
            {
                var accentBar = new Border
                {
                    Width = 3,
                    Height = 14,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(AccentColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                DockPanel.SetDock(accentBar, Dock.Left);
                itemPanel.Children.Add(accentBar);
            }

            // Sub-menu arrow on the right
            if (hasSubMenu)
            {
                var arrow = new TextBlock
                {
                    Text = "›",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(isActiveSubParent ? AccentColor : Color.FromRgb(0x66, 0x66, 0x66)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    FontWeight = FontWeights.Bold
                };
                DockPanel.SetDock(arrow, Dock.Right);
                itemPanel.Children.Add(arrow);
            }

            // Optional icon
            if (!string.IsNullOrEmpty(itemIcon))
            {
                var iconBlock = new TextBlock
                {
                    Text = itemIcon,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(itemIconColor ?? AccentColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                DockPanel.SetDock(iconBlock, Dock.Left);
                itemPanel.Children.Add(iconBlock);
            }

            var itemText = new TextBlock
            {
                Text = display,
                FontSize = 12,
                Foreground = new SolidColorBrush(selected || isActiveSubParent ? AccentColor : Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontWeight = selected || isActiveSubParent ? FontWeights.Medium : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            itemPanel.Children.Add(itemText);

            itemRow.Child = itemPanel;

            // Hover
            itemRow.MouseEnter += (_, _) =>
            {
                if (idx != _selectedIndex)
                {
                    itemRow.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                    itemText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                }

                // Start sub-menu open timer if this item has a sub-menu
                if (hasSubMenu)
                {
                    _hoveredSubMenuIdx = idx;
                    _subOpenTimer.Stop();
                    _subOpenTimer.Start();
                }
                else
                {
                    // Non-sub-menu item: stop timer, clear hover index, but don't close sub-panel
                    _subOpenTimer.Stop();
                    _hoveredSubMenuIdx = -1;
                }
            };
            itemRow.MouseLeave += (_, _) =>
            {
                bool sel = idx == _selectedIndex;
                itemRow.Background = sel
                    ? new SolidColorBrush(Color.FromArgb(0x18, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;
                itemText.Foreground = new SolidColorBrush(sel ? AccentColor : Color.FromRgb(0xCC, 0xCC, 0xCC));
            };

            // Click
            itemRow.MouseLeftButtonUp += (_, e) =>
            {
                if (hasSubMenu)
                {
                    // Open sub-menu immediately on click
                    _subOpenTimer.Stop();
                    OpenSubMenu(itemTag as string ?? "");
                    e.Handled = true;
                    return;
                }

                _selectedIndex = idx;
                _selectedSubTag = null;
                _label.Text = _items[idx].Display;
                _label.Foreground = new SolidColorBrush(AccentColor);
                CloseFlyout();
                RebuildPopupItems();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            _categoriesPanel.Children.Add(itemRow);
        }
    }
}
