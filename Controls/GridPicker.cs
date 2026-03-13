using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AmpUp.Controls;

/// <summary>
/// A categorized target picker. Shows current selection as a styled row;
/// click opens a dark popup with categorized items as full-width text rows.
/// Items can optionally have sub-menus that slide out to the right.
/// </summary>
public class GridPicker : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _categoriesPanel;
    private readonly ScrollViewer _scrollViewer;

    // Sub-flyout
    private readonly Popup _subPopup;
    private readonly Border _subPopupBorder;
    private readonly StackPanel _subItemsPanel;
    private readonly ScrollViewer _subScrollViewer;
    private readonly TextBox _subFilterBox;
    private readonly TextBlock _subFilterPlaceholder;
    private string _subFilterText = "";
    private object? _activeSubParentTag;
    private List<SubItem> _activeSubItems = new();
    private readonly DispatcherTimer _subOpenTimer;
    private readonly DispatcherTimer _closeTimer;
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

        // Main popup
        _categoriesPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = _categoriesPanel
        };

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 8, 6, 8),
            Child = _scrollViewer,
            MaxWidth = 280,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                Opacity = 0.7,
                ShadowDepth = 8,
                Direction = 270
            }
        };

        _popup = new Popup
        {
            Child = _popupBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 4
        };

        // ── Sub-flyout popup ──
        _subItemsPanel = new StackPanel();
        _subScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
            Content = _subItemsPanel
        };

        _subFilterBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            Visibility = Visibility.Collapsed,
        };
        _subFilterPlaceholder = new TextBlock
        {
            Text = "Filter...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 12,
            Padding = new Thickness(10, 7, 0, 0),
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

        var subFilterContainer = new Grid();
        subFilterContainer.Children.Add(_subFilterBox);
        subFilterContainer.Children.Add(_subFilterPlaceholder);

        var subStack = new StackPanel();
        subStack.Children.Add(subFilterContainer);
        subStack.Children.Add(_subScrollViewer);

        _subPopupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 6, 4, 6),
            Child = subStack,
            MinWidth = 200,
            MaxWidth = 300,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                Opacity = 0.6,
                ShadowDepth = 6,
                Direction = 270
            }
        };

        _subPopup = new Popup
        {
            Child = _subPopupBorder,
            PlacementTarget = _popupBorder,
            Placement = PlacementMode.Right,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Slide,
            HorizontalOffset = -2,
        };

        // Polling timer: only runs while sub-popup is open, closes both when mouse leaves
        int _closeGraceTicks = 0;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _closeTimer.Tick += (_, _) =>
        {
            // Grace period: skip first 3 ticks (750ms) after sub-popup opens
            if (_closeGraceTicks > 0) { _closeGraceTicks--; return; }

            // Check mouse position via hit-test (more reliable than IsMouseOver across HWNDs)
            var pos = System.Windows.Input.Mouse.GetPosition(_popupBorder);
            bool overMain = pos.X >= 0 && pos.Y >= 0
                && pos.X <= _popupBorder.ActualWidth && pos.Y <= _popupBorder.ActualHeight;

            var subPos = System.Windows.Input.Mouse.GetPosition(_subPopupBorder);
            bool overSub = subPos.X >= 0 && subPos.Y >= 0
                && subPos.X <= _subPopupBorder.ActualWidth && subPos.Y <= _subPopupBorder.ActualHeight;

            if (IsMouseOver || overMain || overSub)
                return;

            _closeTimer.Stop();
            _subPopup.IsOpen = false;
            _popup.IsOpen = false;
        };

        // Keep main popup open while sub is open; poll to close when mouse leaves both
        _subPopup.Opened += (_, _) =>
        {
            _popup.StaysOpen = true;
            _closeGraceTicks = 3;
            _closeTimer.Start();
        };
        _subPopup.Closed += (_, _) =>
        {
            _closeTimer.Stop();
            _popup.StaysOpen = false;
            _hoveredSubMenuIdx = -1;
        };

        _popup.Closed += (_, _) => _closeTimer.Stop();

        // Timer for delayed sub-menu open on hover
        _subOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _subOpenTimer.Tick += (_, _) =>
        {
            _subOpenTimer.Stop();
            if (_hoveredSubMenuIdx >= 0 && _hoveredSubMenuIdx < _items.Count)
            {
                var tag = _items[_hoveredSubMenuIdx].Tag as string;
                if (tag != null && _subMenuProviders.ContainsKey(tag))
                    OpenSubMenu(tag, _hoveredSubMenuIdx);
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
            if (!_popup.IsOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            _popupBorder.MinWidth = Math.Max(ActualWidth, 200);
            _popup.IsOpen = !_popup.IsOpen;
            e.Handled = true;
        };

        _popup.Opened += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(AccentColor);
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
        };

        _popup.Closed += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            _subPopup.IsOpen = false;
        };
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
    /// When hovered/clicked, a slide-out panel shows the provider's items.
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

    // ── Sub-flyout ───────────────────────────────────────────────

    private void OpenSubMenu(string parentTag, int itemIdx)
    {
        if (!_subMenuProviders.TryGetValue(parentTag, out var provider))
            return;

        var items = provider();
        if (items.Count == 0) return; // nothing to show

        _activeSubParentTag = parentTag;
        _activeSubItems = items;

        RebuildSubItems();

        // Position sub-popup next to the hovered item
        if (itemIdx < _categoriesPanel.Children.Count)
        {
            var target = _categoriesPanel.Children[FindPopupChildForItemIdx(itemIdx)];
            _subPopup.PlacementTarget = target;
            _subPopup.Placement = PlacementMode.Right;
            _subPopup.HorizontalOffset = 6;
            _subPopup.VerticalOffset = -6;
        }

        // Show filter for lists > 8
        var showFilter = _activeSubItems.Count > 8;
        _subFilterBox.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterPlaceholder.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterBox.Text = "";
        _subFilterText = "";

        _subPopup.IsOpen = true;

        if (showFilter)
            _subFilterBox.Focus();
    }

    private int FindPopupChildForItemIdx(int itemIdx)
    {
        // The popup panel has category headers + item rows interleaved.
        // We need to map item index to child index.
        int childIdx = 0;
        var categoryForItem = BuildCategoryMap();

        int currentCat = -1;
        for (int i = 0; i <= itemIdx && childIdx < _categoriesPanel.Children.Count; i++)
        {
            int cat = categoryForItem.GetValueOrDefault(i, -1);
            if (cat != currentCat)
            {
                currentCat = cat;
                if (cat >= 0) childIdx++; // skip category header
            }
            if (i == itemIdx) return childIdx;
            childIdx++;
        }
        return Math.Min(childIdx, _categoriesPanel.Children.Count - 1);
    }

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

                _subPopup.IsOpen = false;
                _popup.IsOpen = false;

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

            var itemRow = new Border
            {
                Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x18, AccentColor.R, AccentColor.G, AccentColor.B))
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
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                Foreground = new SolidColorBrush(selected ? AccentColor : Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontWeight = selected ? FontWeights.Medium : FontWeights.Normal,
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
                    // Close sub-popup if hovering a non-sub-menu item
                    _subOpenTimer.Stop();
                    _hoveredSubMenuIdx = -1;
                    if (_subPopup.IsOpen)
                        _subPopup.IsOpen = false;
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
                    OpenSubMenu(itemTag as string ?? "", idx);
                    e.Handled = true;
                    return;
                }

                _selectedIndex = idx;
                _selectedSubTag = null;
                _label.Text = _items[idx].Display;
                _label.Foreground = new SolidColorBrush(AccentColor);
                _popup.IsOpen = false;
                RebuildPopupItems();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            _categoriesPanel.Children.Add(itemRow);
        }
    }
}
