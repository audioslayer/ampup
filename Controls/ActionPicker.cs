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
/// Custom action picker dropdown — replaces ComboBox for button action selection.
/// Shows icon + label on a dark button, opens a popup with hoverable rows.
/// Self-contained; does not depend on Theme.xaml styles.
/// </summary>
public class ActionPicker : Border
{
    private readonly TextBlock _iconBlock;
    private readonly TextBlock _displayText;
    private readonly TextBlock _chevron;
    private readonly Popup _popup;
    private readonly StackPanel _itemsPanel;

    private int _selectedIndex = -1;
    private readonly List<(string Display, string Value, string Icon, Color Color, string Tooltip)> _items = new();

    public event EventHandler? SelectionChanged;

    public string SelectedValue => _selectedIndex >= 0 ? _items[_selectedIndex].Value : "none";

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

        // Popup
        _itemsPanel = new StackPanel();

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
            Content = _itemsPanel,
        };

        var popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = scrollViewer,
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
            Child = popupBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 2,
        };

        _popup.Opened += (_, _) =>
        {
            popupBorder.MinWidth = ActualWidth;
            BorderBrush = new SolidColorBrush(ThemeManager.Accent);
            _chevron.Foreground = new SolidColorBrush(ThemeManager.Accent);
        };

        _popup.Closed += (_, _) =>
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            _chevron.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
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

    public void ClearItems()
    {
        _items.Clear();
        _itemsPanel.Children.Clear();
        _selectedIndex = -1;
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
