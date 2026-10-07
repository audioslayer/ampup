using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// GridPicker-style searchable app chooser: a borderless Window flyout anchored under an
/// element with a search box, icon-tile rows (title + one-line description) and a
/// "Use “query” as process name" row so any process can be typed in.
/// </summary>
public static class AppChooser
{
    public sealed record Choice(string ProcessName, string Title, string? Description, ImageSource? Icon);

    public static void Show(FrameworkElement anchor, IReadOnlyList<Choice> choices, Action<string> onPick,
        string placeholder = "Search apps or type a process name…", string? emptyText = null)
    {
        Window? flyout = null;
        bool closed = false;
        var accent = ThemeManager.Accent;

        var listPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = listPanel,
            Focusable = false,
        };

        var searchBox = new TextBox
        {
            FontSize = 12.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2), VerticalContentAlignment = VerticalAlignment.Center,
        };
        searchBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        searchBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        var ph = new TextBlock { Text = placeholder, FontSize = 12.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        ph.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mag = UiKit.Icon(MaterialIconKind.Magnify, 16);
        mag.Margin = new Thickness(0, 0, 8, 0);
        searchGrid.Children.Add(mag);
        var textHost = new Grid();
        textHost.Children.Add(searchBox);
        textHost.Children.Add(ph);
        Grid.SetColumn(textHost, 1);
        searchGrid.Children.Add(textHost);
        var searchBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 8), Child = searchGrid,
        };
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        var hint = new TextBlock { Text = "↑ ↓ to move  ·  Enter to choose  ·  Esc to close", FontSize = 10, Margin = new Thickness(4, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var root = new DockPanel();
        DockPanel.SetDock(searchBorder, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(searchBorder);
        root.Children.Add(hint);
        root.Children.Add(scroll);

        var popup = new System.Windows.Controls.Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10), Child = root,
        };
        popup.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgDarkBrush");
        popup.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        var rows = new List<(System.Windows.Controls.Border Row, Action Activate)>();
        int nav = -1;

        void Close()
        {
            if (closed) return;
            closed = true;
            flyout?.Close();
        }

        void Pick(string name)
        {
            Close();
            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length > 0) onPick(name.ToLowerInvariant());
        }

        void SetNav(int idx)
        {
            if (nav >= 0 && nav < rows.Count) rows[nav].Row.BorderBrush = Brushes.Transparent;
            nav = idx;
            if (idx >= 0 && idx < rows.Count)
            {
                rows[idx].Row.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x88));
                rows[idx].Row.BringIntoView();
            }
        }

        System.Windows.Controls.Border MakeRow(FrameworkElement tile, string title, string? desc, Action activate)
        {
            var row = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tile.Margin = new Thickness(0, 0, 10, 0);
            g.Children.Add(tile);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            sp.Children.Add(t);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = new TextBlock { Text = desc, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
                d.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
                sp.Children.Add(d);
            }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            row.Child = g;
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x18));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; activate(); };
            rows.Add((row, activate));
            return row;
        }

        FrameworkElement AppTile(ImageSource? icon)
        {
            if (icon == null) return UiKit.IconTile(accent, MaterialIconKind.Application, 32);
            var tile = UiKit.IconTile(accent, MaterialIconKind.Application, 32);
            tile.Child = new Image { Source = icon, Width = 18, Height = 18 };
            return tile;
        }

        void Rebuild()
        {
            listPanel.Children.Clear();
            rows.Clear();
            nav = -1;
            string q = searchBox.Text.Trim();
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matches = choices.Where(c => terms.All(t =>
                (c.Title + " " + c.ProcessName + " " + c.Description).Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();

            if (matches.Count > 0)
            {
                listPanel.Children.Add(UiKit.SectionHeader("APPS", matches.Count.ToString()));
                foreach (var c in matches)
                {
                    var name = c.ProcessName;
                    listPanel.Children.Add(MakeRow(AppTile(c.Icon), c.Title, c.Description, () => Pick(name)));
                }
            }
            else if (q.Length == 0)
            {
                var empty = new TextBlock { Text = emptyText ?? "No apps are playing audio right now.", FontSize = 11.5, Margin = new Thickness(6, 4, 6, 10), TextWrapping = TextWrapping.Wrap };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
                listPanel.Children.Add(empty);
            }

            var custom = q.TrimEnd();
            bool exact = choices.Any(c => c.ProcessName.Equals(custom, StringComparison.OrdinalIgnoreCase));
            if (!exact)
            {
                listPanel.Children.Add(UiKit.SectionHeader("CUSTOM"));
                if (custom.Length > 0)
                    listPanel.Children.Add(MakeRow(UiKit.IconTile(accent, MaterialIconKind.FormTextbox, 32),
                        $"Use “{custom}”", "Match any process whose name contains this text", () => Pick(custom)));
                else
                {
                    var tip = new TextBlock { Text = "Type a process name (e.g. spotify, game.exe) to add an app that isn't running.", FontSize = 11, Margin = new Thickness(6, 0, 6, 4), TextWrapping = TextWrapping.Wrap };
                    tip.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
                    listPanel.Children.Add(tip);
                }
            }
            if (rows.Count > 0 && q.Length > 0) SetNav(0);
        }

        searchBox.TextChanged += (_, _) =>
        {
            ph.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
            scroll.ScrollToTop();
        };

        // ── Placement (DPI-aware, flips upward when there's no room below) ──
        const double width = 360;
        var belowPx = anchor.PointToScreen(new Point(0, anchor.ActualHeight + 4));
        var abovePx = anchor.PointToScreen(new Point(0, -4));
        var source = PresentationSource.FromVisual(anchor);
        double dpiX = 1, dpiY = 1;
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
        }
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)belowPx.X, (int)belowPx.Y)).WorkingArea;
        double waTop = wa.Top / dpiY, waBottom = wa.Bottom / dpiY, waLeft = wa.Left / dpiX, waRight = wa.Right / dpiX;
        double belowY = belowPx.Y / dpiY, aboveY = abovePx.Y / dpiY;
        double spaceBelow = waBottom - belowY - 8, spaceAbove = aboveY - waTop - 8;
        bool openUp = spaceBelow < 360 && spaceAbove > spaceBelow;
        double height = Math.Clamp(openUp ? spaceAbove : spaceBelow, 220, 440);
        popup.Width = width;
        popup.Height = height;
        double left = belowPx.X / dpiX;
        if (left + width > waRight) left = Math.Max(waLeft, waRight - width);
        double top = openUp ? aboveY - height : belowY;

        flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Content = popup,
            Left = left,
            Top = top,
        };
        flyout.SetResourceReference(Window.BackgroundProperty, "BgDarkBrush");
        flyout.Deactivated += (_, _) => Close();
        flyout.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (searchBox.Text.Length > 0) searchBox.Text = ""; else Close();
                    e.Handled = true; break;
                case Key.Down:
                    if (rows.Count > 0) SetNav(nav < 0 ? 0 : Math.Min(nav + 1, rows.Count - 1));
                    e.Handled = true; break;
                case Key.Up:
                    if (rows.Count > 0) SetNav(nav < 0 ? rows.Count - 1 : Math.Max(nav - 1, 0));
                    e.Handled = true; break;
                case Key.Enter:
                    if (nav >= 0 && nav < rows.Count) rows[nav].Activate();
                    else if (rows.Count > 0) rows[0].Activate();
                    e.Handled = true; break;
            }
        };

        Rebuild();
        var translate = new TranslateTransform(0, openUp ? 8 : -8);
        popup.RenderTransform = translate;
        popup.Opacity = 0;
        flyout.Show();
        flyout.Activate();
        searchBox.Focus();
        Keyboard.Focus(searchBox);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(openUp ? 8 : -8, 0, TimeSpan.FromMilliseconds(130))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        popup.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
    }
}
