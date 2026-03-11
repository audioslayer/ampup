using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AmpUp.Controls;

/// <summary>
/// A categorized grid picker — SteelSeries Sonar / Wave Link inspired.
/// Shows current selection as a styled pill; click opens a floating popup
/// with categorized options displayed as wrapped pill buttons.
/// API-compatible with FlyoutPicker for easy swap.
/// </summary>
public class GridPicker : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _categoriesPanel;
    private readonly ScrollViewer _scrollViewer;

    private int _selectedIndex = -1;
    private readonly List<(string Display, object? Tag)> _items = new();
    private readonly List<(int ItemIndex, string CategoryName)> _categories = new();

    public event EventHandler? SelectionChanged;

    // Accent color for hover/selected states
    public Color AccentColor { get; set; } = ThemeManager.Accent;

    public void RefreshAccent()
    {
        AccentColor = ThemeManager.Accent;
        if (_selectedIndex >= 0)
            _label.Foreground = new SolidColorBrush(AccentColor);
        RebuildPopupItems();
    }

    public GridPicker()
    {
        // Main trigger pill
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(8, 5, 8, 5);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        // Layout: label + chevron
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = "Select...",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_label, 0);
        grid.Children.Add(_label);

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

        // Popup content — vertical stack of category sections
        _categoriesPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
            Content = _categoriesPanel
        };

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = _scrollViewer,
            MaxWidth = 300,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                Opacity = 0.6,
                ShadowDepth = 6
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
            VerticalOffset = 2
        };

        // Trigger hover effects
        MouseEnter += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B));
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        };
        MouseLeave += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            _popupBorder.MinWidth = ActualWidth;
            _popup.IsOpen = !_popup.IsOpen;
            e.Handled = true;
        };

        _popup.Opened += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(AccentColor);
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        };

        _popup.Closed += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        };
    }

    // ── Item management ─────────────────────────────────────────

    /// <summary>
    /// Starts a new category group. Items added after this call
    /// belong to this category until the next AddCategory call.
    /// </summary>
    public void AddCategory(string categoryName)
    {
        _categories.Add((_items.Count, categoryName));
    }

    public void AddItem(string display, object? tag = null)
    {
        _items.Add((display, tag));
        RebuildPopupItems();
    }

    public void ClearItems()
    {
        _items.Clear();
        _categories.Clear();
        _selectedIndex = -1;
        _label.Text = "Select...";
        _label.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
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
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
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

    // ── Popup rebuild ───────────────────────────────────────────

    private void RebuildPopupItems()
    {
        _categoriesPanel.Children.Clear();

        // Build a lookup: itemIndex -> categoryName
        // Determine which category each item belongs to
        var categoryForItem = new Dictionary<int, int>(); // itemIndex -> categoryListIndex
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            for (int i = start; i < end; i++)
                categoryForItem[i] = c;
        }

        // Group items by category, preserving order
        int currentCategory = -1;
        WrapPanel? currentWrap = null;

        for (int i = 0; i < _items.Count; i++)
        {
            int cat = categoryForItem.ContainsKey(i) ? categoryForItem[i] : -1;

            // If we entered a new category, add header + new WrapPanel
            if (cat != currentCategory)
            {
                currentCategory = cat;

                if (cat >= 0)
                {
                    // Category header
                    var header = new TextBlock
                    {
                        Text = _categories[cat].CategoryName.ToUpperInvariant(),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(2, _categoriesPanel.Children.Count > 0 ? 8 : 0, 0, 4)
                    };
                    _categoriesPanel.Children.Add(header);
                }

                currentWrap = new WrapPanel
                {
                    Orientation = Orientation.Horizontal
                };
                _categoriesPanel.Children.Add(currentWrap);
            }

            // Create pill button for this item
            int idx = i;
            var (display, _) = _items[i];
            bool selected = idx == _selectedIndex;

            var pillText = new TextBlock
            {
                Text = display,
                FontSize = 11,
                Foreground = new SolidColorBrush(selected ? AccentColor : Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontWeight = selected ? FontWeights.Medium : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var pill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true,
                Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x33, AccentColor.R, AccentColor.G, AccentColor.B))
                    : new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                BorderBrush = selected
                    ? new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B))
                    : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Child = pillText
            };

            // Hover
            pill.MouseEnter += (_, _) =>
            {
                if (idx != _selectedIndex)
                {
                    pill.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                    pillText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                }
            };
            pill.MouseLeave += (_, _) =>
            {
                bool sel = idx == _selectedIndex;
                pill.Background = sel
                    ? new SolidColorBrush(Color.FromArgb(0x33, AccentColor.R, AccentColor.G, AccentColor.B))
                    : new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                pillText.Foreground = sel
                    ? new SolidColorBrush(AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            };

            // Click
            pill.MouseLeftButtonUp += (_, e) =>
            {
                _selectedIndex = idx;
                _label.Text = _items[idx].Display;
                _label.Foreground = new SolidColorBrush(AccentColor);
                _popup.IsOpen = false;
                RebuildPopupItems();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            currentWrap?.Children.Add(pill);
        }
    }
}
