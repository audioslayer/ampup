using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WolfMixer.Controls;

/// <summary>
/// SteelSeries Sonar-inspired list picker — styled trigger button that opens
/// a floating panel with a clean scrollable list. Replaces dropdown-style
/// FlyoutPicker for variable-length device/entity/controller selection.
/// API-compatible with FlyoutPicker for easy swap.
/// </summary>
public class ListPicker : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;

    private int _selectedIndex = -1;
    private readonly List<(string Display, object? Tag)> _items = new();

    public event EventHandler? SelectionChanged;
    public event EventHandler? DropdownOpening;

    public Color AccentColor { get; set; } = Color.FromRgb(0x00, 0xE6, 0x76);

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

        // Layout: label + chevron
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = "Select...",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
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

        // Popup content
        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 250,
            Content = _itemsPanel
        };

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Child = _scrollViewer,
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

        // Hover effects on trigger button
        MouseEnter += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B));
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            }
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

        // Open popup on click
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_popup.IsOpen)
                DropdownOpening?.Invoke(this, EventArgs.Empty);
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

    public void AddItem(string display, object? tag = null)
    {
        _items.Add((display, tag));
        RebuildPopupItems();
    }

    public void ClearItems()
    {
        _items.Clear();
        _selectedIndex = -1;
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
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                HighlightSelectedItem();
            }
            else
            {
                _selectedIndex = -1;
                _label.Text = "Select...";
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
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

    // ── Popup item rendering ────────────────────────────────────

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var (display, _) = _items[i];
            bool selected = idx == _selectedIndex;

            // Left accent bar (visible only when selected)
            var accentBar = new Border
            {
                Width = 2,
                Background = selected
                    ? new SolidColorBrush(AccentColor)
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 2, 0, 2)
            };

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

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(accentBar, 0);
            Grid.SetColumn(itemText, 1);
            rowGrid.Children.Add(accentBar);
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

            // Hover
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

            // Click
            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                _selectedIndex = idx;
                _label.Text = _items[idx].Display;
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                _popup.IsOpen = false;
                HighlightSelectedItem();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            _itemsPanel.Children.Add(itemBorder);
        }
        HighlightSelectedItem();
    }

    private void HighlightSelectedItem()
    {
        for (int i = 0; i < _itemsPanel.Children.Count; i++)
        {
            if (_itemsPanel.Children[i] is Border b && b.Child is Grid g)
            {
                bool selected = i == _selectedIndex;

                // Update item border background
                b.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;

                // Update accent bar visibility
                if (g.Children[0] is Border accentBar)
                {
                    accentBar.Background = selected
                        ? new SolidColorBrush(AccentColor)
                        : Brushes.Transparent;
                }

                // Update text style
                if (g.Children[1] is TextBlock t)
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
