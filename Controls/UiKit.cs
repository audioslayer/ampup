using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Small builders for the "Groups page" visual language: page header, accent-bar
/// section headers with counts, cards with a thin colour bar + icon tile, list rows
/// with hover-only actions, subtle link rows and tidy empty states.
/// All theme colours use DynamicResource so card-theme / accent changes apply live.
/// </summary>
public static class UiKit
{
    /// <summary>Parts of a card built by <see cref="Card"/> so callers can update text later.</summary>
    public sealed class CardParts
    {
        public System.Windows.Controls.Border Root = null!;
        public TextBlock Title = null!;
        public TextBlock Subtitle = null!;
        public StackPanel Body = null!;
    }

    public static FrameworkElement PageHeader(string title, string subtitle, FrameworkElement? action = null)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var t = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        left.Children.Add(t);
        var sub = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        left.Children.Add(sub);
        grid.Children.Add(left);

        if (action != null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }
        return grid;
    }

    /// <summary>Accent bar + bold accent label + optional muted count.</summary>
    public static FrameworkElement SectionHeader(string text, string? count = null) =>
        SectionHeader(text, count, out _);

    public static FrameworkElement SectionHeader(string text, string? count, out TextBlock countText)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        var bar = new System.Windows.Controls.Border
        {
            Width = 3, Height = 14,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        bar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        row.Children.Add(bar);
        row.Children.Add(label);
        countText = new TextBlock { Text = count ?? "", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        row.Children.Add(countText);
        return row;
    }

    public static MaterialIcon Icon(MaterialIconKind kind, double size, string brushKey = "TextDimBrush")
    {
        var ic = new MaterialIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        ic.SetResourceReference(MaterialIcon.ForegroundProperty, brushKey);
        return ic;
    }

    /// <summary>Small rounded tile tinted with <paramref name="color"/> holding an icon.</summary>
    public static System.Windows.Controls.Border IconTile(Color color, MaterialIconKind kind, double size = 34, string? tooltip = null)
    {
        return new System.Windows.Controls.Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.26),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x55)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
            Child = new MaterialIcon { Kind = kind, Width = size * 0.53, Height = size * 0.53, Foreground = new SolidColorBrush(color) },
        };
    }

    /// <summary>
    /// Card: [thin colour bar] [icon tile] [title / subtitle] [actions], then <paramref name="body"/>.
    /// </summary>
    public static CardParts Card(Color color, MaterialIconKind icon, string title, string? subtitle,
        FrameworkElement? actions = null, UIElement? body = null)
    {
        var card = new System.Windows.Controls.Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(0),
        };
        card.SetResourceReference(FrameworkElement.StyleProperty, "CardPanel");
        card.Padding = new Thickness(0);

        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = outer;

        outer.Children.Add(new System.Windows.Controls.Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 14, 0, 14),
            Background = new SolidColorBrush(color),
        });

        var stack = new StackPanel { Margin = new Thickness(14, 14, 16, 14) };
        Grid.SetColumn(stack, 1);
        outer.Children.Add(stack);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.Children.Add(header);

        var tile = IconTile(color, icon);
        tile.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(tile);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var t = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        names.Children.Add(t);
        var s = new TextBlock
        {
            Text = subtitle ?? "", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible,
        };
        s.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        names.Children.Add(s);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);

        if (actions != null)
        {
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);
        }

        var bodyHost = new StackPanel();
        if (body != null)
        {
            bodyHost.Margin = new Thickness(0, 14, 0, 0);
            bodyHost.Children.Add(body);
        }
        stack.Children.Add(bodyHost);

        return new CardParts { Root = card, Title = t, Subtitle = s, Body = bodyHost };
    }

    /// <summary>Two elements side by side; stacks vertically when narrower than <paramref name="breakpoint"/>.</summary>
    public static Grid ResponsivePair(FrameworkElement left, FrameworkElement right, double breakpoint = 760)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(left);
        g.Children.Add(right);
        bool? wide = null;
        void Apply(double width)
        {
            bool isWide = width >= breakpoint;
            if (wide == isWide) return;
            wide = isWide;
            g.ColumnDefinitions[1].Width = isWide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(right, isWide ? 1 : 0);
            Grid.SetRow(right, isWide ? 0 : 1);
            left.Margin = new Thickness(0, 0, isWide ? 6 : 0, 12);
            right.Margin = new Thickness(isWide ? 6 : 0, 0, 0, 12);
        }
        g.SizeChanged += (_, e) => Apply(e.NewSize.Width);
        Apply(1000);
        return g;
    }

    /// <summary>Dark rounded container for list rows. Add rows to the returned panel.</summary>
    public static System.Windows.Controls.Border ListContainer(out StackPanel rows)
    {
        var list = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };
        list.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgDarkBrush");
        list.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
        rows = new StackPanel();
        list.Child = rows;
        return list;
    }

    /// <summary>
    /// List row: [icon] [name  detail] [right] [hoverActions]. Background highlights on hover
    /// and <paramref name="hoverActions"/> only become visible while hovered.
    /// </summary>
    public static System.Windows.Controls.Border ListRow(FrameworkElement icon, string name, string? detail,
        FrameworkElement? right = null, FrameworkElement? hoverActions = null)
        => ListRow(icon, name, detail, right, hoverActions, out _);

    public static System.Windows.Controls.Border ListRow(FrameworkElement icon, string name, string? detail,
        FrameworkElement? right, FrameworkElement? hoverActions, out TextBlock detailText)
    {
        var row = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 4, 6), Background = Brushes.Transparent };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var n = new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        n.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameStack.Children.Add(n);
        detailText = new TextBlock
        {
            Text = detail ?? "", FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible,
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        nameStack.Children.Add(detailText);
        Grid.SetColumn(nameStack, 1);
        grid.Children.Add(nameStack);

        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }
        if (hoverActions != null)
        {
            hoverActions.VerticalAlignment = VerticalAlignment.Center;
            hoverActions.Opacity = 0;
            Grid.SetColumn(hoverActions, 3);
            grid.Children.Add(hoverActions);
        }

        row.MouseEnter += (_, _) =>
        {
            row.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBgBrush");
            if (hoverActions != null) hoverActions.Opacity = 1;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            if (hoverActions != null) hoverActions.Opacity = 0;
        };
        return row;
    }

    /// <summary>Muted text for the right side of a list row (e.g. type / count).</summary>
    public static TextBlock MutedText(string text, double size = 11)
    {
        var tb = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        return tb;
    }

    /// <summary>Subtle "+ Add …" style row. Accent = add action; otherwise secondary (show more etc.).</summary>
    public static System.Windows.Controls.Border LinkRow(MaterialIconKind icon, string text, bool accent, Action? onClick, string? tooltip = null)
    {
        var row = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tooltip,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = new MaterialIcon { Kind = icon, Width = 14, Height = 14, Margin = new Thickness(1, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBlock { Text = text, FontSize = 11.5, FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        string key = accent ? "AccentBrush" : "TextSecBrush";
        ic.SetResourceReference(MaterialIcon.ForegroundProperty, key);
        tb.SetResourceReference(TextBlock.ForegroundProperty, key);
        sp.Children.Add(ic);
        sp.Children.Add(tb);
        row.Child = sp;
        row.MouseEnter += (_, _) => row.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "CardBgBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (onClick != null) row.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return row;
    }

    /// <summary>Small borderless icon button. Danger turns the icon red on hover.</summary>
    public static System.Windows.Controls.Border IconButton(MaterialIconKind kind, string tooltip, Action<FrameworkElement> onClick,
        bool danger = false, double size = 26)
    {
        var icon = Icon(kind, size * 0.58);
        var b = new System.Windows.Controls.Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = icon,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) =>
        {
            b.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, danger ? "DangerRedBrush" : "TextPrimaryBrush");
        };
        b.MouseLeave += (_, _) =>
        {
            b.Background = Brushes.Transparent;
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(b); };
        return b;
    }

    /// <summary>"…" button that opens a glass context menu.</summary>
    public static System.Windows.Controls.Border MoreButton(Func<IReadOnlyList<GlassMenuItem>> items, string tooltip = "More options")
        => IconButton(MaterialIconKind.DotsHorizontal, tooltip, anchor => GlassContextMenuHost.Show(anchor, items()));

    /// <summary>Muted caption followed by a control (e.g. "Power" + switch).</summary>
    public static StackPanel Captioned(string caption, FrameworkElement control, string? tooltip = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBlock { Text = caption, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        sp.Children.Add(tb);
        control.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(control);
        return sp;
    }

    /// <summary>Empty state: muted icon, title and one line of explanation, optional action on the right.</summary>
    public static System.Windows.Controls.Border EmptyState(MaterialIconKind icon, string title, string body, FrameworkElement? action = null)
    {
        var card = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(0, 0, 0, 12),
        };
        card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgDarkBrush");
        card.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var ic = Icon(icon, 22);
        ic.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(ic);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t1 = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold };
        t1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var t2 = new TextBlock { Text = body, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        t2.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stack.Children.Add(t1);
        stack.Children.Add(t2);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        if (action != null)
        {
            action.Margin = new Thickness(16, 0, 0, 0);
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }
        return card;
    }

    /// <summary>Small status dot + uppercase label (e.g. ACTIVE / STANDBY). Returns an updater.</summary>
    public static StackPanel StatusBadge(out Action<string, bool> update)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(dot);
        sp.Children.Add(tb);
        update = (text, active) =>
        {
            tb.Text = text;
            if (active)
            {
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SuccessGrnBrush");
                tb.SetResourceReference(TextBlock.ForegroundProperty, "SuccessGrnBrush");
            }
            else
            {
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextDimBrush");
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            }
        };
        return sp;
    }
}
