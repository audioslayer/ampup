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
/// Multi-select list picker with checkboxes — for cycle device subset selection.
/// Opens a floating borderless Window flyout with a scrollable checklist.
/// Uses Window instead of Popup to avoid Win11 click-through bugs. API mirrors ListPicker.
/// </summary>
public class CheckListPicker : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _chevron;
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;
    private Window? _flyout;
    private bool _isOpen = false;

    private readonly List<(string Display, string Id, bool Checked)> _items = new();

    public event EventHandler? SelectionChanged;

    public Color AccentColor { get; set; } = Color.FromRgb(0x00, 0xE6, 0x76);

    public CheckListPicker()
    {
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(8, 5, 8, 5);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        this.SetResourceReference(BackgroundProperty, "BgDarkBrush");
        this.SetResourceReference(BorderBrushProperty, "CardBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = "All devices",
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

        _itemsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 250,
            Content = _itemsPanel
        };

        MouseEnter += (_, _) =>
        {
            if (!_isOpen)
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B));
                this.SetResourceReference(BackgroundProperty, "InputBgBrush");
            }
        };
        MouseLeave += (_, _) =>
        {
            if (!_isOpen)
            {
                this.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
                this.SetResourceReference(BackgroundProperty, "BgDarkBrush");
            }
        };

        MouseLeftButtonUp += (_, e) =>
        {
            if (_isOpen) CloseFlyout(); else OpenFlyout();
            e.Handled = true;
        };
    }

    public void AddItem(string display, string id, bool isChecked = false)
    {
        _items.Add((display, id, isChecked));
        RebuildPopupItems();
        UpdateLabel();
    }

    public void ClearItems()
    {
        _items.Clear();
        RebuildPopupItems();
        UpdateLabel();
    }

    /// <summary>Returns device IDs of all checked items. Empty list = cycle all.</summary>
    public List<string> GetCheckedIds()
    {
        var result = new List<string>();
        foreach (var (_, id, ch) in _items)
            if (ch) result.Add(id);
        return result;
    }

    /// <summary>Sets checked state by matching IDs. If ids is empty, unchecks all.</summary>
    public void SetCheckedIds(List<string> ids)
    {
        var idSet = new HashSet<string>(ids);
        for (int i = 0; i < _items.Count; i++)
        {
            var (display, id, _) = _items[i];
            _items[i] = (display, id, idSet.Contains(id));
        }
        RebuildPopupItems();
        UpdateLabel();
    }

    private void ToggleItem(int idx)
    {
        var (display, id, ch) = _items[idx];
        _items[idx] = (display, id, !ch);
        RebuildPopupItems();
        UpdateLabel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLabel()
    {
        var checkedCount = 0;
        foreach (var (_, _, ch) in _items)
            if (ch) checkedCount++;

        if (checkedCount == 0)
        {
            _label.Text = "All devices";
            _label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
        else if (checkedCount == 1)
        {
            foreach (var (display, _, ch) in _items)
                if (ch) { _label.Text = display; break; }
            _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        }
        else
        {
            _label.Text = $"{checkedCount} devices";
            _label.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        }
    }

    // ── Flyout window ────────────────────────────────────────────

    private void OpenFlyout()
    {
        RebuildPopupItems();

        // Detach scroll viewer from any previous flyout
        if (_scrollViewer.Parent != null)
        {
            var oldParent = _scrollViewer.Parent as Border;
            if (oldParent != null) oldParent.Child = null;
        }

        var popupBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Child = _scrollViewer,
            MinWidth = Math.Max(ActualWidth, 120),
        };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = (Brush)Application.Current.FindResource("BgDarkBrush"),
            Content = popupBorder
        };

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
        this.SetResourceReference(BackgroundProperty, "InputBgBrush");
    }

    private void CloseFlyout()
    {
        if (!_isOpen) return;
        _isOpen = false;

        // Detach child before close so ScrollViewer can be reused
        if (_flyout?.Content is Border b)
            b.Child = null;

        _flyout?.Close();
        _flyout = null;

        this.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
        this.SetResourceReference(BackgroundProperty, "BgDarkBrush");
    }

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var (display, _, isChecked) = _items[i];

            var checkBox = new Border
            {
                Width = 14, Height = 14,
                BorderBrush = isChecked
                    ? new SolidColorBrush(AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Background = isChecked
                    ? new SolidColorBrush(Color.FromArgb(0x40, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0)
            };

            if (isChecked)
            {
                checkBox.Child = new TextBlock
                {
                    Text = "\u2713",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(AccentColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var itemText = new TextBlock
            {
                Text = display,
                FontSize = 11.5,
                Foreground = isChecked
                    ? new SolidColorBrush(AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(itemText, 1);
            rowGrid.Children.Add(checkBox);
            rowGrid.Children.Add(itemText);

            var itemBorder = new Border
            {
                Padding = new Thickness(6, 6, 8, 6),
                Cursor = Cursors.Hand,
                Background = isChecked
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent,
                SnapsToDevicePixels = true,
                Child = rowGrid
            };

            itemBorder.MouseEnter += (_, _) =>
            {
                if (!_items[idx].Checked)
                    itemBorder.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            };
            itemBorder.MouseLeave += (_, _) =>
            {
                itemBorder.Background = _items[idx].Checked
                    ? new SolidColorBrush(Color.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B))
                    : Brushes.Transparent;
            };

            itemBorder.MouseLeftButtonUp += (_, e) =>
            {
                ToggleItem(idx);
                e.Handled = true;
            };

            _itemsPanel.Children.Add(itemBorder);
        }
    }
}
