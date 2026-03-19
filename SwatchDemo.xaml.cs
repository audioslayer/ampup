using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AmpUp;

public partial class SwatchDemo : Window
{
    private readonly Color _primary = Color.FromRgb(0x00, 0x50, 0xFF);
    private readonly Color _secondary = Color.FromRgb(0xFF, 0x00, 0x80);
    private readonly Color _accent = Color.FromRgb(0x00, 0xE6, 0x76);
    private readonly Color _cardBg = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private readonly Color _cardBorder = Color.FromRgb(0x2A, 0x2A, 0x2A);
    private readonly Color _textDim = Color.FromRgb(0x55, 0x55, 0x55);
    private readonly Color _textSec = Color.FromRgb(0x9A, 0x9A, 0x9A);

    public SwatchDemo()
    {
        InitializeComponent();
        BuildDemo();
    }

    private void BuildDemo()
    {
        var grid = new UniformGrid { Columns = 3, Rows = 2 };

        grid.Children.Add(WrapInCard("1 — Color Bars", MakeStyle1()));
        grid.Children.Add(WrapInCard("2 — Color Pills", MakeStyle2()));
        grid.Children.Add(WrapInCard("3 — Split Swatch", MakeStyle3()));
        grid.Children.Add(WrapInCard("4 — LED Preview Strip", MakeStyle4()));
        grid.Children.Add(WrapInCard("5 — Inline Hex Fields", MakeStyle5()));
        grid.Children.Add(WrapInCard("6 — Color Dots in Header", MakeStyle6()));

        Root.Children.Add(grid);
    }

    private Border WrapInCard(string title, UIElement content)
    {
        var stack = new StackPanel { Margin = new Thickness(12) };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(_accent),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14),
        });

        stack.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(_cardBg),
            BorderBrush = new SolidColorBrush(_cardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
            Padding = new Thickness(16),
            Child = stack,
        };
    }

    // ── Style 1: Horizontal color bars ──
    private StackPanel MakeStyle1()
    {
        var panel = new StackPanel();

        panel.Children.Add(MakeLabel("COLOR"));

        // Primary bar
        var bar1 = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(_primary),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { Color = _primary, BlurRadius = 8, Opacity = 0.25, ShadowDepth = 0 },
        };
        bar1.Child = new TextBlock
        {
            Text = "PRIMARY",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        panel.Children.Add(bar1);

        // Secondary bar
        var bar2 = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(_secondary),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new DropShadowEffect { Color = _secondary, BlurRadius = 8, Opacity = 0.25, ShadowDepth = 0 },
        };
        bar2.Child = new TextBlock
        {
            Text = "SECONDARY",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        panel.Children.Add(bar2);

        return panel;
    }

    // ── Style 2: Color pills (chip/tag style) ──
    private StackPanel MakeStyle2()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeLabel("COLOR"));

        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

        wrap.Children.Add(MakePill("PRIMARY", _primary));
        wrap.Children.Add(MakePill("SECONDARY", _secondary));

        panel.Children.Add(wrap);
        return panel;
    }

    private Border MakePill(string label, Color c)
    {
        var darkBg = Color.FromArgb(0x33, c.R, c.G, c.B);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(darkBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, c.R, c.G, c.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // Color dot inside pill
        row.Children.Add(new Ellipse
        {
            Width = 16, Height = 16,
            Fill = new SolidColorBrush(c),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        pill.Child = row;
        return pill;
    }

    // ── Style 3: Single split swatch (diagonal or left/right) ──
    private StackPanel MakeStyle3()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeLabel("COLOR"));

        var container = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        // Split rectangle — left half primary, right half secondary
        var splitGrid = new Grid
        {
            Width = 64, Height = 36,
            Cursor = System.Windows.Input.Cursors.Hand,
            ClipToBounds = true,
        };

        // Clip with rounded corners
        splitGrid.Clip = new RectangleGeometry(new Rect(0, 0, 64, 36), 6, 6);

        // Left half
        splitGrid.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(_primary),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 32,
        });

        // Right half
        splitGrid.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(_secondary),
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 32,
        });

        // Divider line
        splitGrid.Children.Add(new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        container.Children.Add(splitGrid);

        // Labels to the right
        var labels = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var l1 = new StackPanel { Orientation = Orientation.Horizontal };
        l1.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(_primary), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        l1.Children.Add(new TextBlock { Text = "Primary", FontSize = 9, Foreground = new SolidColorBrush(_textSec) });
        labels.Children.Add(l1);
        var l2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        l2.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(_secondary), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        l2.Children.Add(new TextBlock { Text = "Secondary", FontSize = 9, Foreground = new SolidColorBrush(_textSec) });
        labels.Children.Add(l2);
        container.Children.Add(labels);

        panel.Children.Add(container);

        // Also show diagonal variant
        panel.Children.Add(new TextBlock { Text = "diagonal variant:", FontSize = 9, Foreground = new SolidColorBrush(_textDim), Margin = new Thickness(0, 12, 0, 4) });

        var diagGrid = new Grid
        {
            Width = 48, Height = 48,
            Cursor = System.Windows.Input.Cursors.Hand,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        diagGrid.Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 8, 8);

        // Bottom layer (secondary)
        diagGrid.Children.Add(new Rectangle { Fill = new SolidColorBrush(_secondary) });

        // Top-left triangle (primary)
        var triangle = new Polygon
        {
            Points = new PointCollection { new Point(0, 0), new Point(48, 0), new Point(0, 48) },
            Fill = new SolidColorBrush(_primary),
        };
        diagGrid.Children.Add(triangle);

        panel.Children.Add(diagGrid);

        return panel;
    }

    // ── Style 4: LED preview strip (3 LEDs like the hardware) ──
    private StackPanel MakeStyle4()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeLabel("LED PREVIEW"));

        var strip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        var ledRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        for (int i = 0; i < 3; i++)
        {
            var ledGrid = new Grid { Margin = new Thickness(6, 0, 6, 0) };

            // Glow
            ledGrid.Children.Add(new Ellipse
            {
                Width = 28, Height = 28,
                Fill = new RadialGradientBrush(
                    Color.FromArgb(0x44, _primary.R, _primary.G, _primary.B),
                    Colors.Transparent),
            });

            // LED
            ledGrid.Children.Add(new Ellipse
            {
                Width = 14, Height = 14,
                Fill = new SolidColorBrush(_primary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = _primary, BlurRadius = 6, Opacity = 0.6, ShadowDepth = 0 },
            });

            ledRow.Children.Add(ledGrid);
        }

        strip.Child = ledRow;
        panel.Children.Add(strip);

        // Color controls below
        var controls = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var r1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        r1.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(_primary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        r1.Children.Add(new TextBlock { Text = "Click to change color", FontSize = 9, Foreground = new SolidColorBrush(_textDim) });
        controls.Children.Add(r1);
        panel.Children.Add(controls);

        return panel;
    }

    // ── Style 5: Inline hex fields ──
    private StackPanel MakeStyle5()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeLabel("COLOR"));

        panel.Children.Add(MakeHexField("PRIMARY", _primary));
        panel.Children.Add(MakeHexField("SECONDARY", _secondary));

        return panel;
    }

    private Border MakeHexField(string label, Color c)
    {
        var field = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // Color preview square
        row.Children.Add(new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(c),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Label + hex
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_textDim),
        });
        text.Children.Add(new TextBlock
        {
            Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
        });
        row.Children.Add(text);

        field.Child = row;
        return field;
    }

    // ── Style 6: Color dots embedded in section header ──
    private StackPanel MakeStyle6()
    {
        var panel = new StackPanel();

        // Header row with dots inline
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        headerRow.Children.Add(new TextBlock
        {
            Text = "COLOR",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });

        // Primary dot with label
        var dot1 = MakeHeaderDot(_primary);
        headerRow.Children.Add(dot1);

        // Secondary dot with label
        var dot2 = MakeHeaderDot(_secondary);
        headerRow.Children.Add(dot2);

        panel.Children.Add(headerRow);

        // Description
        panel.Children.Add(new TextBlock
        {
            Text = "Colors shown inline with section header.\nClick dots to change. Compact single-line layout.",
            FontSize = 9,
            Foreground = new SolidColorBrush(_textDim),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        return panel;
    }

    private Border MakeHeaderDot(Color c)
    {
        var container = new Border
        {
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var g = new Grid();

        // Glow ring
        g.Children.Add(new Ellipse
        {
            Width = 24, Height = 24,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
            StrokeThickness = 1,
        });

        // Dot
        g.Children.Add(new Ellipse
        {
            Width = 16, Height = 16,
            Fill = new SolidColorBrush(c),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = c, BlurRadius = 6, Opacity = 0.4, ShadowDepth = 0 },
        });

        container.Child = g;
        return container;
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_accent),
        };
    }
}
