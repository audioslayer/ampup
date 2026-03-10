using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AmpUp.Controls;

/// <summary>
/// A modern flyout picker — Wave Link / SteelSeries Sonar inspired.
/// Clean pill button; click opens a floating popup with smooth hover states.
/// </summary>
public class FlyoutPicker : Border
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

    // Accent color for hover/selected states
    public Color AccentColor { get; set; } = Color.FromRgb(0x00, 0xE6, 0x76);

    public FlyoutPicker()
    {
        // Main button — minimal, borderless by default
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
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3),
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

        // Hover effects — subtle border glow
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
                _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                HighlightSelectedItem();
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

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var (display, _) = _items[i];

            var itemBorder = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true
            };

            var itemText = new TextBlock
            {
                Text = display,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            itemBorder.Child = itemText;

            // Hover
            itemBorder.MouseEnter += (_, _) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                itemText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            };
            itemBorder.MouseLeave += (_, _) =>
            {
                bool selected = idx == _selectedIndex;
                itemBorder.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x18, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;
                itemText.Foreground = selected
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
            if (_itemsPanel.Children[i] is Border b)
            {
                bool selected = i == _selectedIndex;
                b.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x18, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;

                if (b.Child is TextBlock t)
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
