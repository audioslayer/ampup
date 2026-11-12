using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// "Open canvas" layout: no boxes. Sections are a small caps title over a 1px hairline; items are
/// plain rows on the page background separated by faint 1px lines. Frozen brushes only, no effects.
/// See Controls/OPEN_CANVAS.md. Call from the UI thread.
/// </summary>
public static class OpenCanvas
{
    public const double RowMinHeight = 52;
    public const double SectionSpacing = 22;

    private static Brush? _hover;
    private static Brush? _selFill;
    private static Brush? _selBorder;

    static OpenCanvas()
    {
        ThemeManager.OnAccentChanged += () => { _selFill = null; _selBorder = null; };
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Row hover background (white ~0x08).</summary>
    public static Brush HoverBrush => _hover ??= Frozen(new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)));
    /// <summary>Selected list row fill (accent 0x28).</summary>
    public static Brush SelectedFill => _selFill ??= Frozen(new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x28)));
    /// <summary>Selected list row border (accent 0x96).</summary>
    public static Brush SelectedBorder => _selBorder ??= Frozen(new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x96)));

    private static TextBlock Text(string text, double size, string brushKey, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>1px horizontal rule in the theme border colour (live via CardBorderBrush).</summary>
    public static FrameworkElement Divider()
    {
        var b = new Border { Height = 1, SnapsToDevicePixels = true };
        b.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        return b;
    }

    /// <summary>Section header: small caps title, optional dim count, optional right-aligned actions, hairline under it.</summary>
    public static FrameworkElement Section(string title, string? count = null, FrameworkElement? actions = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        names.Children.Add(Text(title.ToUpperInvariant(), 11, "TextDimBrush", FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(count))
        {
            var c = Text(count, 11, "TextDimBrush");
            c.Margin = new Thickness(8, 0, 0, 0);
            c.Opacity = 0.7;
            names.Children.Add(c);
        }
        grid.Children.Add(names);
        if (actions != null)
        {
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
        }
        var panel = new StackPanel { Margin = new Thickness(0, SectionSpacing, 0, 0) };
        panel.Children.Add(grid);
        panel.Children.Add(Divider());
        return panel;
    }

    /// <summary>Item row: [leading] [title + dim sub] [trailing], bottom hairline when <paramref name="divider"/>.</summary>
    public static Border Row(FrameworkElement? leading, string title, string? sub, FrameworkElement? trailing,
        bool divider = true, double minHeight = RowMinHeight)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (leading != null)
        {
            leading.VerticalAlignment = VerticalAlignment.Center;
            leading.Margin = new Thickness(0, 0, 12, 0);
            grid.Children.Add(leading);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(Text(title, 13, "TextPrimaryBrush"));
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Text(sub, 11, "TextDimBrush");
            s.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(s);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
        return RowShell(grid, divider, minHeight);
    }

    /// <summary>Setting row: title + dim hint left, control right. Replaces the boxed GlassStyle.SettingRow.</summary>
    public static Border SettingRow(string title, string? hint, FrameworkElement control, bool divider = true) =>
        Row(null, title, hint, control, divider);

    /// <summary>Wraps any content in the open row shell (padding, min height, optional bottom hairline, hover).</summary>
    public static Border RowShell(UIElement content, bool divider = true, double minHeight = RowMinHeight)
    {
        var row = new Border
        {
            Child = content,
            MinHeight = minHeight,
            Padding = new Thickness(4, 10, 4, 10),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, divider ? 1 : 0),
            SnapsToDevicePixels = true,
        };
        row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        AttachHover(row);
        return row;
    }

    /// <summary>Subtle hover background (no animation).</summary>
    public static void AttachHover(Border b)
    {
        b.MouseEnter += (_, _) => b.Background = HoverBrush;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
    }

    /// <summary>
    /// Side list + open detail: a slim glass list on the left (icon, label, status dot) and an open
    /// pane on the right (<see cref="ContentHost"/>). Below <see cref="CollapseWidth"/> the list moves on top.
    /// </summary>
    public class MasterDetail : Grid
    {
        public const double ListWidth = 248;
        public const double CollapseWidth = 700;

        private readonly StackPanel _list = new();
        private readonly Border _listPanel;
        private readonly List<(string Key, Border Row)> _items = new();
        private bool _collapsed;

        /// <summary>Right-hand pane; set its Child to the selected item's detail.</summary>
        public Border ContentHost { get; } = new() { Padding = new Thickness(28, 4, 8, 8) };
        public string? SelectedKey { get; private set; }
        public event Action<string>? SelectionChanged;

        public MasterDetail()
        {
            _listPanel = new Border { Padding = new Thickness(6), Child = _list, Focusable = true, FocusVisualStyle = null, VerticalAlignment = VerticalAlignment.Top };
            GlassStyle.ApplyGlassCard(_listPanel, 14);
            _listPanel.PreviewKeyDown += OnKey;
            Children.Add(_listPanel);
            Children.Add(ContentHost);
            ApplyLayout(false);
            SizeChanged += (_, e) =>
            {
                bool c = e.NewSize.Width < CollapseWidth;
                if (c != _collapsed) ApplyLayout(c);
            };
            ThemeManager.OnAccentChanged += RefreshRows;
            Unloaded += (_, _) => ThemeManager.OnAccentChanged -= RefreshRows;
            Loaded += (_, _) => { ThemeManager.OnAccentChanged -= RefreshRows; ThemeManager.OnAccentChanged += RefreshRows; RefreshRows(); };
        }

        private void ApplyLayout(bool collapsed)
        {
            _collapsed = collapsed;
            ColumnDefinitions.Clear(); RowDefinitions.Clear();
            _list.Orientation = collapsed ? Orientation.Horizontal : Orientation.Vertical;
            if (collapsed)
            {
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                _listPanel.Width = double.NaN;
                SetColumn(_listPanel, 0); SetRow(_listPanel, 0);
                SetColumn(ContentHost, 0); SetRow(ContentHost, 1);
                ContentHost.Padding = new Thickness(4, 16, 4, 8);
            }
            else
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _listPanel.Width = ListWidth;
                SetColumn(_listPanel, 0); SetRow(_listPanel, 0);
                SetColumn(ContentHost, 1); SetRow(ContentHost, 0);
                ContentHost.Padding = new Thickness(28, 4, 8, 8);
            }
        }

        public void AddItem(string key, string label, MaterialIconKind icon, FrameworkElement? statusDot = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new MaterialIcon { Kind = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            ic.SetResourceReference(Control.ForegroundProperty, "TextSecBrush");
            grid.Children.Add(ic);
            var t = Text(label, 13, "TextPrimaryBrush");
            t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(t, 1); grid.Children.Add(t);
            if (statusDot != null)
            {
                statusDot.VerticalAlignment = VerticalAlignment.Center;
                statusDot.Margin = new Thickness(8, 0, 0, 0);
                SetColumn(statusDot, 2); grid.Children.Add(statusDot);
            }
            var row = new Border
            {
                Child = grid, Padding = new Thickness(10, 9, 10, 9), CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1),
                SnapsToDevicePixels = true,
            };
            row.MouseLeftButtonUp += (_, _) => { Select(key); _listPanel.Focus(); };
            row.MouseEnter += (_, _) => { if (SelectedKey != key) row.Background = HoverBrush; };
            row.MouseLeave += (_, _) => { if (SelectedKey != key) row.Background = Brushes.Transparent; };
            _items.Add((key, row));
            _list.Children.Add(row);
            Paint(key, row);
            if (SelectedKey == null) Select(key);
        }

        public void Select(string key)
        {
            if (SelectedKey == key || !_items.Any(i => i.Key == key)) return;
            SelectedKey = key;
            RefreshRows();
            SelectionChanged?.Invoke(key);
        }

        private void RefreshRows() { foreach (var (k, r) in _items) Paint(k, r); }

        private void Paint(string key, Border row)
        {
            bool sel = key == SelectedKey;
            row.Background = sel ? SelectedFill : Brushes.Transparent;
            row.BorderBrush = sel ? SelectedBorder : Brushes.Transparent;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (_items.Count == 0) return;
            int i = _items.FindIndex(x => x.Key == SelectedKey);
            int next = e.Key switch
            {
                Key.Up or Key.Left => Math.Max(0, i - 1),
                Key.Down or Key.Right => Math.Min(_items.Count - 1, i + 1),
                Key.Home => 0,
                Key.End => _items.Count - 1,
                _ => -1,
            };
            if (next < 0) return;
            Select(_items[next].Key);
            e.Handled = true;
        }
    }
}
