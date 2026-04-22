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
/// Custom action picker dropdown — replaces ComboBox for button action selection.
/// Shows icon + label on a dark button, opens a borderless Window flyout with hoverable rows.
/// Items can optionally have sub-menus that expand inline to the right within the same Window.
/// Uses borderless Windows instead of Popups to avoid Win11 click-through bugs.
/// </summary>
public class ActionPicker : Border
{
    private readonly TextBlock _iconBlock;
    private readonly TextBlock _displayText;
    private readonly TextBlock _chevron;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;

    // Main flyout
    private Window? _flyout;
    private bool _isOpen = false;

    // Inline sub-panel (replaces second Window sub-flyout)
    private readonly Border _subPanelBorder;
    private readonly Border _subDivider;
    private readonly StackPanel _subItemsPanel;
    private readonly ScrollViewer _subScrollViewer;
    private readonly TextBox _subFilterBox;
    private readonly TextBlock _subFilterPlaceholder;
    private readonly TextBlock _subHeaderLabel;
    private string _subFilterText = "";
    private string? _activeSubParentValue;
    private List<SubItem> _activeSubItems = new();
    private readonly DispatcherTimer _subOpenTimer;
    private int _hoveredSubMenuIdx = -1;

    private int _selectedIndex = -1;
    private string? _selectedSubTag; // entity ID from sub-menu selection
    private readonly List<(string Display, string Value, string Icon, Color Color, string Tooltip)> _items = new();
    private readonly List<(int ItemIndex, string CategoryName)> _categories = new();

    // Category header styles (icon + color)
    public static readonly Dictionary<string, (string Icon, Color Color)> CategoryStyles = new()
    {
        { "MEDIA",              ("\u266B", Color.FromRgb(0x00, 0xE6, 0x76)) },
        { "MUTE",               ("\uD83D\uDD07", Color.FromRgb(0xFF, 0x52, 0x52)) },
        { "APP CONTROL",        ("\u2B21", Color.FromRgb(0x44, 0x8A, 0xFF)) },
        { "DEVICE",             ("\uD83D\uDD0C", Color.FromRgb(0xB3, 0x88, 0xFF)) },
        { "SYSTEM",             ("\u2699", Color.FromRgb(0xFF, 0xD7, 0x40)) },
        { "POWER",              ("\u23FB", Color.FromRgb(0xFF, 0x52, 0x52)) },
        { "INTEGRATIONS",       ("\u26A1", Color.FromRgb(0x26, 0xC6, 0xDA)) },
    };

    // Sub-menu providers: keyed by action value
    private readonly Dictionary<string, Func<List<SubItem>>> _subMenuProviders = new();

    // Action groups — parent rows with synthetic values ("group_spotify")
    // that hide a set of child action values behind a flyout. The children
    // remain in _items (so SelectedValue / rendering still work) but are
    // omitted from the main popup list and shown in the group's flyout
    // instead. Picking a child commits it as the selection, which then
    // triggers any existing sub-menu the child has registered (e.g. HA
    // entity picker).
    private readonly Dictionary<string, (string Display, string Icon, Color Color, List<string> Children)> _actionGroups = new();
    private readonly HashSet<string> _actionGroupChildren = new();
    private readonly HashSet<string> _actionGroupParents = new();

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
        this.SetResourceReference(Border.BorderBrushProperty, "BgDarkBrush");
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 0, 8, 0);
        Height = 36;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        this.SetResourceReference(BackgroundProperty, "BgBaseBrush");

        // Layout: [icon] [label -- fills] [chevron]
        var dock = new DockPanel { LastChildFill = true };

        _iconBlock = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            Text = "\u2014",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        DockPanel.SetDock(_iconBlock, Dock.Left);
        dock.Children.Add(_iconBlock);

        _chevron = new TextBlock
        {
            Text = "\u25BE",
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

        // Main flyout content
        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
            Content = _itemsPanel,
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
            MaxHeight = 360,
            Content = _subItemsPanel
        };

        _subFilterBox = new TextBox
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(4, 0, 4, 6),
        };
        _subFilterBox.SetResourceReference(TextBox.BackgroundProperty, "CardBgBrush");
        _subFilterBox.SetResourceReference(TextBox.BorderBrushProperty, "CardBorderBrush");
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

        var subFilterContainer = new Grid();
        subFilterContainer.Children.Add(_subFilterBox);
        subFilterContainer.Children.Add(_subFilterPlaceholder);

        var subStack = new StackPanel();
        subStack.Children.Add(_subHeaderLabel);
        subStack.Children.Add(subFilterContainer);
        subStack.Children.Add(_subScrollViewer);

        _subPanelBorder = new Border
        {
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(4, 6, 4, 6),
            Child = subStack,
            MinWidth = 200,
            MaxWidth = 300,
            Visibility = Visibility.Collapsed,
        };
        _subPanelBorder.SetResourceReference(Border.BackgroundProperty, "BgBaseBrush");

        // Thin vertical divider between main items and sub-panel
        _subDivider = new Border
        {
            Width = 1,
            Margin = new Thickness(0, 10, 0, 10),
            Visibility = Visibility.Collapsed,
        };
        _subDivider.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");

        // Horizontal layout: [main items] [divider] [sub-panel]
        var hStack = new StackPanel { Orientation = Orientation.Horizontal };
        hStack.Children.Add(_scrollViewer);
        hStack.Children.Add(_subDivider);
        hStack.Children.Add(_subPanelBorder);

        _popupBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = hStack,
        };
        _popupBorder.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        _popupBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");

        _subOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _subOpenTimer.Tick += (_, _) =>
        {
            _subOpenTimer.Stop();
            if (_hoveredSubMenuIdx >= 0 && _hoveredSubMenuIdx < _items.Count)
            {
                var value = _items[_hoveredSubMenuIdx].Value;
                if (_subMenuProviders.ContainsKey(value))
                    OpenSubMenu(value);
            }
        };

        // Hover on button
        MouseEnter += (_, _) =>
        {
            if (!_isOpen)
            {
                var accent = ThemeManager.Accent;
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));
                _chevron.Foreground = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));
            }
        };
        MouseLeave += (_, _) =>
        {
            if (!_isOpen)
            {
                this.SetResourceReference(Border.BorderBrushProperty, "BgDarkBrush");
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        // Toggle flyout on click
        MouseLeftButtonUp += (_, e) =>
        {
            if (_isOpen) CloseFlyout(); else OpenFlyout();
            e.Handled = true;
        };

        Unloaded += (_, _) => _subOpenTimer.Stop();
    }

    // ── Main flyout ───────────────────────────────────────────────

    private void OpenFlyout()
    {
        CloseSubPanel();
        _popupBorder.MinWidth = ActualWidth;
        RebuildAllPopupItems();

        // Detach from any previous flyout so we can re-parent
        if (_popupBorder.Parent is Border oldParent)
            oldParent.Child = null;

        var outerBorder = new Border
        {
            Child = _popupBorder,
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = (Brush)Application.Current.FindResource("BgDarkBrush"),
            Content = outerBorder
        };

        // Position below the trigger (convert physical pixels back to DIPs for PerMonitorV2)
        var screenPos = PointToScreen(new Point(0, ActualHeight + 2));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;
            screenPos = new Point(screenPos.X / dpiX, screenPos.Y / dpiY);
        }
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

        BorderBrush = new SolidColorBrush(ThemeManager.Accent);
        _chevron.Foreground = new SolidColorBrush(ThemeManager.Accent);
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

        this.SetResourceReference(Border.BorderBrushProperty, "BgDarkBrush");
        _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }

    // ── Inline sub-panel ──────────────────────────────────────────

    private void OpenSubMenu(string actionValue)
    {
        if (!_subMenuProviders.TryGetValue(actionValue, out var provider))
            return;

        var items = provider();
        if (items.Count == 0) return;

        _activeSubParentValue = actionValue;
        _activeSubItems = items;

        // Set header from parent item display name
        var parentDisplay = _items.FirstOrDefault(i => i.Value == actionValue).Display ?? actionValue;
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

        // Refresh main items to highlight active parent
        RebuildAllPopupItems();

        if (showFilter)
            _subFilterBox.Focus();
    }

    private void CloseSubPanel()
    {
        _hoveredSubMenuIdx = -1;
        _subDivider.Visibility = Visibility.Collapsed;
        _subPanelBorder.Visibility = Visibility.Collapsed;
    }

    // ── Item management ─────────────────────────────────────────────

    public void AddItem(string display, string value, string icon, Color color, string tooltip)
    {
        _items.Add((display, value, icon, color, tooltip));
    }

    /// <summary>
    /// Mark a set of existing action values as members of a group. The
    /// main popup will hide the children and render a single parent row
    /// with a right-chevron; clicking it opens a flyout listing the
    /// children. Pick one of those and the picker commits it as the real
    /// action selection (including any sub-menu that action may have
    /// already registered — e.g. HA entity picker).
    /// </summary>
    public void AddActionGroup(string groupValue, string display, string icon, Color color, IEnumerable<string> childValues)
    {
        var children = childValues?.Where(v => !string.IsNullOrEmpty(v)).ToList() ?? new List<string>();
        if (children.Count == 0) return;
        _actionGroups[groupValue] = (display, icon, color, children);
        foreach (var c in children) _actionGroupChildren.Add(c);
        _actionGroupParents.Add(groupValue);
        // Insert as an _items row so category layout + BuildPopupItem work.
        _items.Add((display, groupValue, icon, color, $"{display} — pick a specific action"));

        // Register a sub-menu provider that returns the children so the
        // existing inline side-panel UI handles the flyout. The children's
        // Tag is the real action value; the sub-item click handler checks
        // _actionGroupParents to know it should commit that as the main
        // action instead of treating it as a secondary tag.
        _subMenuProviders[groupValue] = () =>
        {
            var result = new List<SubItem>();
            foreach (var childValue in children)
            {
                var entry = _items.FirstOrDefault(i => i.Value == childValue);
                if (string.IsNullOrEmpty(entry.Value)) continue;
                result.Add(new SubItem(entry.Display, entry.Value, entry.Icon, entry.Color));
            }
            return result;
        };
    }

    /// <summary>
    /// Rebuild the popup after all items and categories have been added.
    /// Must be called after AddItem/AddCategory to display items.
    /// </summary>
    public void BuildPopup()
    {
        RebuildAllPopupItems();
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

    public void AddCategory(string categoryName)
    {
        _categories.Add((_items.Count, categoryName));
    }

    public void ClearItems()
    {
        _items.Clear();
        _categories.Clear();
        _itemsPanel.Children.Clear();
        _selectedIndex = -1;
        _selectedSubTag = null;
        _displayText.Text = "None";
        _iconBlock.Text = "\u2014";
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

    // ── Sub-items rebuild ────────────────────────────────────────

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
                    itemBorder.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
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

                // Action-group parent: the sub-item's Tag is the REAL
                // action value — commit it as the selection and open its
                // own sub-menu if it has one (e.g. HA entity picker).
                if (_actionGroupParents.Contains(parentValue))
                {
                    int childIdx = _items.FindIndex(i => i.Value == subItem.Tag);
                    if (childIdx >= 0)
                    {
                        _selectedSubTag = null;
                        CloseFlyout();
                        SetSelectedIndex(childIdx, fireEvent: true);
                    }
                    e.Handled = true;
                    return;
                }

                // Legacy path: parent becomes the action, subItem.Tag is a
                // secondary value (entity ID / device ID / etc.).
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

                CloseFlyout();

                RebuildAllPopupItems();
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
            _iconBlock.Text = "\u2014";
            _iconBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        RebuildAllPopupItems();

        if (fireEvent)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildPopupItem(int idx)
    {
        var item = _items[idx];
        bool selected = idx == _selectedIndex;
        bool isActionGroup = _actionGroupParents.Contains(item.Value);
        bool hasSubMenu = _subMenuProviders.TryGetValue(item.Value, out var subProvider)
            && subProvider().Count > 0;
        bool isActiveSubParent = hasSubMenu && _subPanelBorder.Visibility == Visibility.Visible
            && item.Value == _activeSubParentValue;
        // Highlight a group parent when any of its children is the
        // currently-selected action.
        bool groupChildSelected = isActionGroup && _selectedIndex >= 0
            && _actionGroups.TryGetValue(item.Value, out var groupInfo)
            && groupInfo.Children.Contains(_items[_selectedIndex].Value);

        var accentBar = new Border
        {
            Width = 3,
            Background = selected || isActiveSubParent
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
            Foreground = selected || isActiveSubParent
                ? new SolidColorBrush(ThemeManager.Accent)
                : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontWeight = selected || isActiveSubParent ? FontWeights.Medium : FontWeights.Normal,
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

        // Sub-menu arrow (shown for legacy sub-menus + action-group parents)
        if (hasSubMenu)
        {
            bool highlight = isActiveSubParent || groupChildSelected;
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = new TextBlock
            {
                Text = "\u203A",
                FontSize = 14,
                Foreground = new SolidColorBrush(highlight ? ThemeManager.Accent : Color.FromRgb(0x66, 0x66, 0x66)),
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
        if (isActiveSubParent && !selected)
            row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        int capturedIdx = idx;

        row.MouseEnter += (_, _) =>
        {
            if (capturedIdx != _selectedIndex)
            {
                row.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
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
                // Don't close sub-panel here -- prevents premature close during
                // diagonal mouse movement between main item and sub-panel.
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
                OpenSubMenu(item.Value);
                e.Handled = true;
                return;
            }

            _selectedSubTag = null;
            CloseFlyout();
            SetSelectedIndex(capturedIdx, fireEvent: true);
            e.Handled = true;
        };

        _itemsPanel.Children.Add(row);
    }

    private Dictionary<int, int> BuildCategoryMap()
    {
        var map = new Dictionary<int, int>();
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            for (int i = start; i < end; i++)
                map[i] = c;
        }
        return map;
    }

    private void RebuildAllPopupItems()
    {
        _itemsPanel.Children.Clear();
        var categoryMap = BuildCategoryMap();
        int currentCategory = -1;
        var accent = ThemeManager.Accent;

        for (int i = 0; i < _items.Count; i++)
        {
            // Skip children of an action group — they only appear in the
            // group's flyout, not the main popup list.
            if (_actionGroupChildren.Contains(_items[i].Value)) continue;

            int cat = categoryMap.GetValueOrDefault(i, -1);

            // Category header
            if (cat != currentCategory)
            {
                currentCategory = cat;
                if (cat >= 0)
                {
                    var catName = _categories[cat].CategoryName.ToUpperInvariant();
                    var (icon, color) = CategoryStyles.GetValueOrDefault(catName, ("\u2022", accent));

                    var headerRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, _itemsPanel.Children.Count > 0 ? 10 : 2, 0, 4)
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

                    _itemsPanel.Children.Add(headerRow);
                }
            }

            BuildPopupItem(i);
        }
    }
}
