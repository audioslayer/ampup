using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WolfMixer.Controls;

/// <summary>
/// A modern flyout picker that replaces standard ComboBox.
/// Shows current selection as a pill button; click opens a floating popup menu.
/// </summary>
public class FlyoutPicker : Border
{
    private readonly TextBlock _label;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;

    private int _selectedIndex = -1;
    private readonly List<(string Display, object? Tag)> _items = new();

    public event EventHandler? SelectionChanged;

    // Accent color for hover/selected states
    public Color AccentColor { get; set; } = Color.FromRgb(0x00, 0xE6, 0x76);

    public FlyoutPicker()
    {
        // Main button appearance
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 6, 10, 6);
        Cursor = Cursors.Hand;

        // Layout: label + chevron
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = "Select...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_label, 0);
        grid.Children.Add(_label);

        var chevron = new TextBlock
        {
            Text = "\u25BE", // small down triangle
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);

        Child = grid;

        // Popup
        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
            Content = _itemsPanel
        };

        _popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = _scrollViewer,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                Opacity = 0.5,
                ShadowDepth = 4
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

        // Hover effects on the button
        MouseEnter += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(AccentColor);
            Effect = new DropShadowEffect
            {
                Color = AccentColor,
                BlurRadius = 8,
                Opacity = 0.2,
                ShadowDepth = 0
            };
        };
        MouseLeave += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
                Effect = null;
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            _popupBorder.MinWidth = ActualWidth;
            _popup.IsOpen = !_popup.IsOpen;
            e.Handled = true;
        };

        _popup.Closed += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
            Effect = null;
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
                HighlightSelectedItem();
            }
            else
            {
                _selectedIndex = -1;
                _label.Text = "Select...";
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

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var (display, _) = _items[i];

            var itemBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };

            var itemText = new TextBlock
            {
                Text = display,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
            };
            itemBorder.Child = itemText;

            // Hover
            itemBorder.MouseEnter += (_, _) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(0x20, AccentColor.R, AccentColor.G, AccentColor.B));
            };
            itemBorder.MouseLeave += (_, _) =>
            {
                itemBorder.Background = idx == _selectedIndex
                    ? new SolidColorBrush(Color.FromArgb(0x15, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;
            };

            // Click
            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                _selectedIndex = idx;
                _label.Text = _items[idx].Display;
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
            if (_itemsPanel.Children[i] is Border b)
            {
                bool selected = i == _selectedIndex;
                b.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x15, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;

                if (b.Child is TextBlock t)
                {
                    t.Foreground = selected
                        ? new SolidColorBrush(AccentColor)
                        : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                    t.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }
    }
}
