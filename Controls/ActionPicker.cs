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
/// Custom action picker dropdown — replaces ComboBox for button action selection.
/// Shows icon + label on a dark button, opens a popup with hoverable rows.
/// Items can optionally have sub-menus that slide out to the right.
/// </summary>
public class ActionPicker : Border
{
    private readonly TextBlock _iconBlock;
    private readonly TextBlock _displayText;
    private readonly TextBlock _chevron;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;

    // Sub-flyout
    private readonly Popup _subPopup;
    private readonly Border _subPopupBorder;
    private readonly StackPanel _subItemsPanel;
    private readonly ScrollViewer _subScrollViewer;
    private readonly TextBox _subFilterBox;
    private readonly TextBlock _subFilterPlaceholder;
    private string _subFilterText = "";
    private string? _activeSubParentValue;
    private List<SubItem> _activeSubItems = new();
    private readonly DispatcherTimer _subOpenTimer;
    private readonly DispatcherTimer _closeTimer;
    private int _hoveredSubMenuIdx = -1;

    private int _selectedIndex = -1;
    private string? _selectedSubTag; // entity ID from sub-menu selection
    private readonly List<(string Display, string Value, string Icon, Color Color, string Tooltip)> _items = new();

    // Sub-menu providers: keyed by action value
    private readonly Dictionary<string, Func<List<SubItem>>> _subMenuProviders = new();

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Fired when a sub-menu item is selected. Args: (actionValue, subItemTag).
    /// </summary>
    public event Action<string, string>? SubItemSelected;

    public string SelectedValue => _selectedIndex >= 0 ? _items[_selectedIndex].Value : "none";

    /// <summary>The sub-item tag selected from the flyout (e.g. entity ID), or null.</summary>
    public string? SelectedSubTag => _selectedSubTag;

    public record SubItem(string Display, string Tag, string? Icon = null, Color? IconColor = null);

    public ActionPicker()
    {
        // Button appearance
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 0, 8, 0);
        Height = 36;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        // Layout: [icon] [label — fills] [chevron]
        var dock = new DockPanel { LastChildFill = true };

        _iconBlock = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            Text = "—",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        DockPanel.SetDock(_iconBlock, Dock.Left);
        dock.Children.Add(_iconBlock);

        _chevron = new TextBlock
        {
            Text = "▾",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        DockPanel.SetDock(_chevron, Dock.Right);
        dock.Children.Add(_chevron);

        _displayText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        dock.Children.Add(_displayText);

        Child = dock;

        // Main popup
        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
            Content = _itemsPanel,
        };

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = _scrollViewer,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                Opacity = 0.7,
                ShadowDepth = 6,
            },
        };

        _popup = new Popup
        {
            Child = _popupBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 2,
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

        _subPopup.Opened += (_, _) => _popup.StaysOpen = true;
        _subPopup.Closed += (_, _) =>
        {
            _popup.StaysOpen = false;
            _hoveredSubMenuIdx = -1;
        };

        // Timer to close both popups when mouse leaves all popup areas
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (_subPopup.IsOpen) _subPopup.IsOpen = false;
            _popup.IsOpen = false;
        };

        // Cancel close when mouse enters either popup
        _popupBorder.MouseEnter += (_, _) => _closeTimer.Stop();
        _subPopupBorder.MouseEnter += (_, _) => _closeTimer.Stop();

        // Start close timer when mouse leaves either popup
        _popupBorder.MouseLeave += (_, _) =>
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        };
        _subPopupBorder.MouseLeave += (_, _) =>
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        };

        _subOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _subOpenTimer.Tick += (_, _) =>
        {
            _subOpenTimer.Stop();
            if (_hoveredSubMenuIdx >= 0 && _hoveredSubMenuIdx < _items.Count)
            {
                var value = _items[_hoveredSubMenuIdx].Value;
                if (_subMenuProviders.ContainsKey(value))
                    OpenSubMenu(value, _hoveredSubMenuIdx);
            }
        };

        // Popup events
        _popup.Opened += (_, _) =>
        {
            _popupBorder.MinWidth = ActualWidth;
            BorderBrush = new SolidColorBrush(ThemeManager.Accent);
            _chevron.Foreground = new SolidColorBrush(ThemeManager.Accent);
        };

        _popup.Closed += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            _subPopup.IsOpen = false;
        };

        // Hover on button
        MouseEnter += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                var accent = ThemeManager.Accent;
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));
                _chevron.Foreground = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));
            }
        };
        MouseLeave += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        // Toggle popup on click
        MouseLeftButtonUp += (_, e) =>
        {
            _popup.IsOpen = !_popup.IsOpen;
            e.Handled = true;
        };
    }

    // ── Item management ─────────────────────────────────────────────

    public void AddItem(string display, string value, string icon, Color color, string tooltip)
    {
        _items.Add((display, value, icon, color, tooltip));
        BuildPopupItem(_items.Count - 1);
    }

    /// <summary>
    /// Register a sub-menu provider for items with the given action value.
    /// When hovered/clicked, a slide-out panel shows the provider's items.
    /// </summary>
    public void RegisterSubMenu(string actionValue, Func<List<SubItem>> provider)
    {
        _subMenuProviders[actionValue] = provider;
    }

    public void ClearSubMenus()
    {
        _subMenuProviders.Clear();
    }

    public void ClearItems()
    {
        _items.Clear();
        _itemsPanel.Children.Clear();
        _selectedIndex = -1;
        _selectedSubTag = null;
        _displayText.Text = "None";
        _iconBlock.Text = "—";
        _iconBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }

    public void Select(string value)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Value == value)
            {
                SetSelectedIndex(i, fireEvent: false);
                return;
            }
        }
        SetSelectedIndex(0, fireEvent: false);
    }

    /// <summary>
    /// Select an action and optionally set the sub-tag (entity ID).
    /// If subTag is provided and a sub-menu exists, show the entity name on the button.
    /// </summary>
    public void SelectWithSub(string value, string? subTag)
    {
        _selectedSubTag = subTag;
        Select(value);

        // If we have a sub-tag, try to resolve its display name
        if (!string.IsNullOrEmpty(subTag) && _subMenuProviders.TryGetValue(value, out var provider))
        {
            var items = provider();
            var match = items.FirstOrDefault(i => i.Tag == subTag);
            if (match != null)
                _displayText.Text = match.Display;
        }
    }

    // ── Sub-flyout ──────────────────────────────────────────────────

    private void OpenSubMenu(string actionValue, int itemIdx)
    {
        if (!_subMenuProviders.TryGetValue(actionValue, out var provider))
            return;

        _activeSubParentValue = actionValue;
        _activeSubItems = provider();

        RebuildSubItems();

        // Position sub-popup next to the hovered item
        if (itemIdx < _itemsPanel.Children.Count)
        {
            _subPopup.PlacementTarget = _itemsPanel.Children[itemIdx];
            _subPopup.Placement = PlacementMode.Right;
            _subPopup.HorizontalOffset = 6;
            _subPopup.VerticalOffset = -6;
        }

        var showFilter = _activeSubItems.Count > 8;
        _subFilterBox.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterPlaceholder.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        _subFilterBox.Text = "";
        _subFilterText = "";

        _subPopup.IsOpen = true;

        if (showFilter)
            _subFilterBox.Focus();
    }

    private void RebuildSubItems()
    {
        _subItemsPanel.Children.Clear();
        var accent = ThemeManager.Accent;

        for (int i = 0; i < _activeSubItems.Count; i++)
        {
            int idx = i;
            var sub = _activeSubItems[i];
            bool selected = _selectedSubTag == sub.Tag && _activeSubParentValue == SelectedValue;

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var accentBar = new Border
            {
                Width = 2,
                Background = selected ? new SolidColorBrush(accent) : Brushes.Transparent,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 2, 6, 2)
            };
            Grid.SetColumn(accentBar, 0);
            rowGrid.Children.Add(accentBar);

            int textCol = 1;

            if (!string.IsNullOrEmpty(sub.Icon))
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var iconText = new TextBlock
                {
                    Text = sub.Icon,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(sub.IconColor ?? accent),
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
                    ? new SolidColorBrush(accent)
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
                    ? new SolidColorBrush(Color.FromArgb(0x1F, accent.R, accent.G, accent.B))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 1, 2, 1),
                SnapsToDevicePixels = true,
                Child = rowGrid
            };

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

            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                var parentValue = _activeSubParentValue ?? "";
                var subItem = _activeSubItems[idx];

                // Select the parent action
                for (int pi = 0; pi < _items.Count; pi++)
                {
                    if (_items[pi].Value == parentValue)
                    {
                        _selectedIndex = pi;
                        var item = _items[pi];
                        _iconBlock.Text = item.Icon;
                        _iconBlock.Foreground = new SolidColorBrush(item.Color);
                        ToolTip = item.Tooltip;
                        break;
                    }
                }

                _selectedSubTag = subItem.Tag;
                // Show entity name on the button
                _displayText.Text = subItem.Display;

                _subPopup.IsOpen = false;
                _popup.IsOpen = false;

                RefreshPopupHighlights();
                SubItemSelected?.Invoke(parentValue, subItem.Tag);
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

    // ── Internal ────────────────────────────────────────────────────

    private void SetSelectedIndex(int idx, bool fireEvent)
    {
        _selectedIndex = idx;

        if (idx >= 0 && idx < _items.Count)
        {
            var item = _items[idx];
            _displayText.Text = item.Display;
            _iconBlock.Text = item.Icon;
            _iconBlock.Foreground = new SolidColorBrush(item.Color);
            ToolTip = item.Tooltip;
        }
        else
        {
            _displayText.Text = "None";
            _iconBlock.Text = "—";
            _iconBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        RefreshPopupHighlights();

        if (fireEvent)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildPopupItem(int idx)
    {
        var item = _items[idx];
        bool selected = idx == _selectedIndex;
        bool hasSubMenu = _subMenuProviders.ContainsKey(item.Value);

        var accentBar = new Border
        {
            Width = 3,
            Background = selected
                ? new SolidColorBrush(ThemeManager.Accent)
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 2, 0, 2),
        };

        var iconText = new TextBlock
        {
            Text = item.Icon,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = new SolidColorBrush(item.Color),
        };

        var nameText = new TextBlock
        {
            Text = item.Display,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected
                ? new SolidColorBrush(ThemeManager.Accent)
                : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontWeight = selected ? FontWeights.Medium : FontWeights.Normal,
        };

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // accent bar
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        Grid.SetColumn(accentBar, 0);
        Grid.SetColumn(iconText, 1);
        Grid.SetColumn(nameText, 2);
        rowGrid.Children.Add(accentBar);
        rowGrid.Children.Add(iconText);
        rowGrid.Children.Add(nameText);

        // Sub-menu arrow
        if (hasSubMenu)
        {
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = new TextBlock
            {
                Text = "›",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(arrow, 3);
            rowGrid.Children.Add(arrow);
        }

        var row = new Border
        {
            Padding = new Thickness(0, 5, 10, 5),
            Cursor = Cursors.Hand,
            Background = selected
                ? new SolidColorBrush(Color.FromArgb(0x1F, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B))
                : Brushes.Transparent,
            SnapsToDevicePixels = true,
            Child = rowGrid,
            ToolTip = item.Tooltip,
        };

        int capturedIdx = idx;

        row.MouseEnter += (_, _) =>
        {
            if (capturedIdx != _selectedIndex)
            {
                row.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                nameText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            }

            if (hasSubMenu)
            {
                _hoveredSubMenuIdx = capturedIdx;
                _subOpenTimer.Stop();
                _subOpenTimer.Start();
            }
            else
            {
                _subOpenTimer.Stop();
                _hoveredSubMenuIdx = -1;
                if (_subPopup.IsOpen)
                    _subPopup.IsOpen = false;
            }
        };
        row.MouseLeave += (_, _) =>
        {
            bool sel = capturedIdx == _selectedIndex;
            var accent = ThemeManager.Accent;
            row.Background = sel
                ? new SolidColorBrush(Color.FromArgb(0x1F, accent.R, accent.G, accent.B))
                : Brushes.Transparent;
            nameText.Foreground = sel
                ? new SolidColorBrush(accent)
                : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        };

        row.MouseLeftButtonUp += (_, e) =>
        {
            if (hasSubMenu)
            {
                _subOpenTimer.Stop();
                OpenSubMenu(item.Value, capturedIdx);
                e.Handled = true;
                return;
            }

            _selectedSubTag = null;
            _popup.IsOpen = false;
            SetSelectedIndex(capturedIdx, fireEvent: true);
            e.Handled = true;
        };

        _itemsPanel.Children.Add(row);
    }

    private void RefreshPopupHighlights()
    {
        var accent = ThemeManager.Accent;
        for (int i = 0; i < _itemsPanel.Children.Count; i++)
        {
            if (_itemsPanel.Children[i] is not Border row) continue;
            if (row.Child is not Grid grid) continue;

            bool sel = i == _selectedIndex;

            row.Background = sel
                ? new SolidColorBrush(Color.FromArgb(0x1F, accent.R, accent.G, accent.B))
                : Brushes.Transparent;

            if (grid.Children[0] is Border bar)
                bar.Background = sel ? new SolidColorBrush(accent) : Brushes.Transparent;

            if (grid.Children[2] is TextBlock name)
            {
                name.Foreground = sel
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                name.FontWeight = sel ? FontWeights.Medium : FontWeights.Normal;
            }
        }
    }
}
