using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AmpUp.Controls;

/// <summary>
/// SteelSeries Sonar-inspired list picker — styled trigger button that opens
/// a floating borderless Window flyout with a clean scrollable list.
/// Uses a borderless Window instead of Popup to avoid Win11 click-through bugs.
/// API-compatible with FlyoutPicker for easy swap.
/// </summary>
public class ListPicker : Border
{
    private readonly TextBlock _labelIcon;
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBox _filterBox;
    private readonly StackPanel _popupStack;
    private Window? _flyout;

    private int _selectedIndex = -1;
    private readonly List<(string Display, object? Tag, string? Icon, Color? IconColor)> _items = new();
    private string _filterText = "";
    private bool _isOpen = false;

    public event EventHandler? SelectionChanged;
    public event EventHandler? DropdownOpening;

    public Color AccentColor { get; set; } = ThemeManager.Accent;

    public void RefreshAccent()
    {
        AccentColor = ThemeManager.Accent;
        if (_selectedIndex >= 0)
            _label.Foreground = new SolidColorBrush(AccentColor);
    }

    public ListPicker()
    {
        // Trigger button
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(8, 5, 8, 5);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        // Layout: [icon] label + chevron
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _labelIcon = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Visibility = Visibility.Collapsed
        };
        labelRow.Children.Add(_labelIcon);

        _label = new TextBlock
        {
            Text = "Select...",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        labelRow.Children.Add(_label);

        Grid.SetColumn(labelRow, 0);
        grid.Children.Add(labelRow);

        _chevron = new TextBlock
        {
            Text = "\u25BE",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(_chevron, 1);
        grid.Children.Add(_chevron);

        Child = grid;

        // Popup content
        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 250,
            Content = _itemsPanel
        };

        var filterPlaceholder = new TextBlock
        {
            Text = "Filter...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 12,
            Padding = new Thickness(10, 7, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _filterBox = new TextBox
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
        _filterBox.TextChanged += (_, _) =>
        {
            _filterText = _filterBox.Text.Trim();
            filterPlaceholder.Visibility = string.IsNullOrEmpty(_filterBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        };

        var filterContainer = new Grid();
        filterContainer.Children.Add(_filterBox);
        filterContainer.Children.Add(filterPlaceholder);

        _popupStack = new StackPanel();
        _popupStack.Children.Add(filterContainer);
        _popupStack.Children.Add(_scrollViewer);

        // Hover effects on trigger button
        MouseEnter += (_, _) =>
        {
            if (!_isOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B));
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            }
        };
        MouseLeave += (_, _) =>
        {
            if (!_isOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            if (!_isOpen)
                DropdownOpening?.Invoke(this, EventArgs.Empty);
            if (_isOpen) CloseFlyout(); else OpenFlyout();
            e.Handled = true;
        };
    }

    // ── Item management ─────────────────────────────────────────

    public void AddItem(string display, object? tag = null)
        => AddItem(display, tag, null, null);

    public void AddItem(string display, object? tag, string? icon, Color? iconColor)
    {
        _items.Add((display, tag, icon, iconColor));
        RebuildPopupItems();
    }

    public void ClearItems()
    {
        _items.Clear();
        _selectedIndex = -1;
        _label.Text = "Select...";
        _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        _labelIcon.Visibility = Visibility.Collapsed;
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
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                UpdateLabelIcon(value);
                HighlightSelectedItem();
            }
            else
            {
                _selectedIndex = -1;
                _label.Text = "Select...";
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                _labelIcon.Visibility = Visibility.Collapsed;
            }
        }
    }

    public object? SelectedTag
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex].Tag
            : null;
    }

    public string SelectedDisplay
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex].Display
            : "";
    }

    public int ItemCount => _items.Count;

    public object? GetTagAt(int index)
    {
        return index >= 0 && index < _items.Count ? _items[index].Tag : null;
    }

    // ── Flyout window ────────────────────────────────────────────

    private void OpenFlyout()
    {
        var showFilter = _items.Count > 8;
        _filterBox.Visibility = showFilter ? Visibility.Visible : Visibility.Collapsed;
        if (showFilter)
            _filterBox.Text = "";

        RebuildPopupItems();

        // Detach from any previous flyout so we can re-parent
        if (_popupStack.Parent is Border oldParent)
            oldParent.Child = null;

        var popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Child = _popupStack,
            MinWidth = Math.Max(ActualWidth, 120),
        };

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            Content = popupBorder
        };

        // Position below the trigger
        var screenPos = PointToScreen(new Point(0, ActualHeight + 2));
        _flyout.Left = screenPos.X;
        _flyout.Top = screenPos.Y;

        _flyout.Deactivated += (_, _) => CloseFlyout();
        _flyout.KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseFlyout(); };

        // Slide-down + fade-in animation (120ms)
        var translate = new TranslateTransform(0, -8);
        popupBorder.RenderTransform = translate;
        popupBorder.Opacity = 0;

        _flyout.Show();
        _isOpen = true;

        var slideAnim = new DoubleAnimation(-8, 0, new Duration(TimeSpan.FromMilliseconds(120)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fadeAnim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(120)));
        translate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        popupBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        BorderBrush = new SolidColorBrush(AccentColor);
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

        if (showFilter)
            _filterBox.Focus();
    }

    private void CloseFlyout()
    {
        if (!_isOpen) return;
        _isOpen = false;

        // Detach child before close so _popupStack can be reused
        if (_flyout?.Content is Border b)
            b.Child = null;

        _flyout?.Close();
        _flyout = null;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }

    // ── Popup item rendering ────────────────────────────────────

    private void ApplyFilter()
    {
        if (string.IsNullOrEmpty(_filterText))
        {
            foreach (UIElement child in _itemsPanel.Children)
                child.Visibility = Visibility.Visible;
            return;
        }

        for (int i = 0; i < _items.Count && i < _itemsPanel.Children.Count; i++)
        {
            var match = _items[i].Display.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
            _itemsPanel.Children[i].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var (display, _, icon, iconColor) = _items[i];
            bool selected = idx == _selectedIndex;

            var accentBar = new Border
            {
                Width = 2,
                Background = selected
                    ? new SolidColorBrush(AccentColor)
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(accentBar, 0);
            rowGrid.Children.Add(accentBar);

            int textCol = 1;

            if (!string.IsNullOrEmpty(icon))
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var iconText = new TextBlock
                {
                    Text = icon,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(iconColor ?? AccentColor),
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
                Text = display,
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
                SnapsToDevicePixels = true,
                Child = rowGrid
            };

            itemBorder.MouseEnter += (_, _) =>
            {
                if (idx != _selectedIndex)
                {
                    itemBorder.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
                    itemText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                }
            };
            itemBorder.MouseLeave += (_, _) =>
            {
                bool sel = idx == _selectedIndex;
                itemBorder.Background = sel
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;
                itemText.Foreground = sel
                    ? new SolidColorBrush(AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            };

            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                _selectedIndex = idx;
                _label.Text = _items[idx].Display;
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                UpdateLabelIcon(idx);
                CloseFlyout();
                HighlightSelectedItem();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            _itemsPanel.Children.Add(itemBorder);
        }
        HighlightSelectedItem();
    }

    private void UpdateLabelIcon(int idx)
    {
        if (idx >= 0 && idx < _items.Count)
        {
            var (_, _, icon, iconColor) = _items[idx];
            if (!string.IsNullOrEmpty(icon))
            {
                _labelIcon.Text = icon;
                _labelIcon.Foreground = new SolidColorBrush(iconColor ?? AccentColor);
                _labelIcon.Visibility = Visibility.Visible;
                return;
            }
        }
        _labelIcon.Visibility = Visibility.Collapsed;
    }

    private void HighlightSelectedItem()
    {
        for (int i = 0; i < _itemsPanel.Children.Count; i++)
        {
            if (_itemsPanel.Children[i] is Border b && b.Child is Grid g)
            {
                bool selected = i == _selectedIndex;

                b.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;

                if (g.Children[0] is Border accentBar)
                {
                    accentBar.Background = selected
                        ? new SolidColorBrush(AccentColor)
                        : Brushes.Transparent;
                }

                if (g.Children[g.Children.Count - 1] is TextBlock t)
                {
                    t.Foreground = selected
                        ? new SolidColorBrush(AccentColor)
                        : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                    t.FontWeight = selected ? FontWeights.Medium : FontWeights.Normal;
                }
            }
        }
    }
}
