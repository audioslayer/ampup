using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Material.Icons;

namespace AmpUp.Controls;

/// <summary>
/// Small modal for editing one line of text (e.g. a Quick Wheel item's label), styled like the
/// app's cards: icon tile + title / subtitle header, a text box, an optional live preview, and
/// Cancel / Save. Enter saves, Esc cancels. Use <see cref="Prompt"/>.
/// </summary>
public sealed class TextEditDialog : Window
{
    private readonly TextBox _box;
    private string? _result;

    /// <summary>
    /// Show the dialog. Returns the trimmed text on Save, or null when cancelled.
    /// <paramref name="preview"/> (optional) draws what the text will look like and is redrawn as the user types.
    /// </summary>
    public static string? Prompt(Window? owner, string title, string subtitle, string initial,
        MaterialIconKind icon = MaterialIconKind.PencilOutline, Func<string, FrameworkElement>? preview = null,
        int maxLength = 60)
    {
        var dlg = new TextEditDialog(title, subtitle, initial, icon, preview, maxLength) { Owner = owner };
        dlg.ShowDialog();
        return dlg._result;
    }

    private TextEditDialog(string title, string subtitle, string initial, MaterialIconKind icon,
        Func<string, FrameworkElement>? preview, int maxLength)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; // no AllowsTransparency: layered windows break hit-testing on Win11
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgDarkBrush");

        var root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        root.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Child = stack;

        // Header: icon tile + title / subtitle, close on the right (drag to move)
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = UiKit.IconTile(ThemeManager.Accent, icon, 34);
        tile.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var st = new TextBlock { Text = subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(t);
        titles.Children.Add(st);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = UiKit.IconButton(MaterialIconKind.Close, "Cancel (Esc)", _ => Close(), size: 26);
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Grid or TextBlock or StackPanel)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        stack.Children.Add(header);

        _box = new TextBox
        {
            Text = initial,
            MaxLength = maxLength,
            FontSize = 13,
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(_box);

        var count = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 2, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        stack.Children.Add(count);

        ContentControl? previewHost = null;
        if (preview != null)
        {
            var cap = new TextBlock { Text = "PREVIEW", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 8) };
            cap.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            stack.Children.Add(cap);
            previewHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(previewHost);
        }

        void Refresh()
        {
            count.Text = $"{_box.Text.Length} / {maxLength}";
            if (previewHost != null) previewHost.Content = preview!(_box.Text.Trim());
        }
        _box.TextChanged += (_, _) => Refresh();
        Refresh();

        // Cancel / Save
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(MakeButton("Cancel", primary: false, Close));
        buttons.Children.Add(MakeButton("Save", primary: true, Save));
        stack.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Save(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    private void Save()
    {
        _result = _box.Text.Trim();
        Close();
    }

    private static Border MakeButton(string text, bool primary, Action onClick)
    {
        var a = ThemeManager.Accent;
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold };
        var b = new Border
        {
            MinWidth = 84,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = label,
        };
        label.HorizontalAlignment = HorizontalAlignment.Center;
        void Paint(bool hover)
        {
            if (primary)
            {
                // Hover: 15% toward white
                var bg = hover
                    ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                    : a;
                b.Background = new SolidColorBrush(bg);
                b.BorderBrush = new SolidColorBrush(bg);
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
            }
            else
            {
                b.Background = hover ? (Brush)Application.Current.FindResource("InputBgBrush") : Brushes.Transparent;
                b.BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush");
                label.Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush");
            }
        }
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, _) => onClick();
        Paint(false);
        return b;
    }

    /// <summary>
    /// Preview of a Quick Wheel center card: the label the way the wheel shows it
    /// (two wrapped lines, ellipsis after that), scaled down.
    /// </summary>
    public static FrameworkElement WheelLabelPreview(string text, MaterialIconKind? icon, Color color)
    {
        var a = ThemeManager.Accent;
        var grid = new Grid { Width = 150, Height = 150 };
        grid.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(0xEB, 0x0C, 0x0C, 0x0C)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x55, a.R, a.G, a.B)),
            StrokeThickness = 1,
        });
        var center = new StackPanel { Width = 118, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon is MaterialIconKind k)
            center.Children.Add(new Material.Icons.WPF.MaterialIcon
            {
                Kind = k, Width = 20, Height = 20, Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
            });
        center.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(text) ? "(no name)" : text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30, // two lines, like the wheel
        });
        grid.Children.Add(center);
        return grid;
    }
}
