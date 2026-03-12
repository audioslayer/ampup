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
/// A categorized target picker. Shows current selection as a styled row;
/// click opens a dark popup with categorized items as full-width text rows.
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

    public Color AccentColor { get; set; } = ThemeManager.Accent;

    // Category icons and colors for visual identity
    private static readonly Dictionary<string, (string Icon, Color Color)> CategoryStyles = new()
    {
        { "AUDIO",        ("♪", Color.FromRgb(0x64, 0xB5, 0xF6)) },  // blue
        { "DEVICES",      ("⬡", Color.FromRgb(0xBA, 0x68, 0xC8)) },  // purple
        { "INTEGRATIONS", ("◈", Color.FromRgb(0xFF, 0xB7, 0x4D)) },  // amber
        { "APPS",         ("◉", Color.FromRgb(0x66, 0xBB, 0x6A)) },  // green
    };

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

        // Popup
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

        // Hover
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
        };
    }

    // ── Item management ─────────────────────────────────────────

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

    // ── Popup rebuild ───────────────────────────────────────────

    private void RebuildPopupItems()
    {
        _categoriesPanel.Children.Clear();

        // Map each item to its category
        var categoryForItem = new Dictionary<int, int>();
        for (int c = 0; c < _categories.Count; c++)
        {
            int start = _categories[c].ItemIndex;
            int end = c + 1 < _categories.Count ? _categories[c + 1].ItemIndex : _items.Count;
            for (int i = start; i < end; i++)
                categoryForItem[i] = c;
        }

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
                        // letter-spaced via padding
                    });

                    _categoriesPanel.Children.Add(headerRow);
                }
            }

            // Item row — full width, clean text
            int idx = i;
            var (display, _) = _items[i];
            bool selected = idx == _selectedIndex;
            int catIdx = categoryForItem.GetValueOrDefault(i, -1);
            var catStyle = catIdx >= 0
                ? CategoryStyles.GetValueOrDefault(_categories[catIdx].CategoryName.ToUpperInvariant(), ("•", AccentColor))
                : ("•", AccentColor);
            var catColor = catStyle.Item2;

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
                _selectedIndex = idx;
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
