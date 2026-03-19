using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace AmpUp;

public partial class SwatchDemo : Window
{
    public SwatchDemo()
    {
        InitializeComponent();
        BuildDemo();
    }

    private void BuildDemo()
    {
        var grid = new UniformGrid { Columns = 5 };

        // Demo colors
        var primary = Color.FromRgb(0x00, 0x50, 0xFF);   // blue
        var secondary = Color.FromRgb(0xFF, 0x00, 0x80);  // pink

        grid.Children.Add(MakeColumn("1 — Current (Plain Circle)", MakeStyle1(primary), MakeStyle1(secondary)));
        grid.Children.Add(MakeColumn("2 — Ring + Inner Glow", MakeStyle2(primary), MakeStyle2(secondary)));
        grid.Children.Add(MakeColumn("3 — Rounded Square + Shine", MakeStyle3(primary), MakeStyle3(secondary)));
        grid.Children.Add(MakeColumn("4 — Double Ring", MakeStyle4(primary), MakeStyle4(secondary)));
        grid.Children.Add(MakeColumn("5 — Glass Orb", MakeStyle5(primary), MakeStyle5(secondary)));

        Root.Children.Add(grid);
    }

    private StackPanel MakeColumn(string title, UIElement swatch1, UIElement swatch2)
    {
        var col = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        col.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        });

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row1.Children.Add(new TextBlock
        {
            Text = "PRIMARY",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xCC, 0xCC, 0xCC)),
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row1.Children.Add(swatch1);
        col.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal };
        row2.Children.Add(new TextBlock
        {
            Text = "SECONDARY",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xCC, 0xCC, 0xCC)),
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row2.Children.Add(swatch2);
        col.Children.Add(row2);

        return col;
    }

    // ── Style 1: Current plain circle ──
    private Border MakeStyle1(Color c)
    {
        return new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(c),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }

    // ── Style 2: Ring with inner glow ──
    private Grid MakeStyle2(Color c)
    {
        var g = new Grid { Width = 40, Height = 40, Cursor = System.Windows.Input.Cursors.Hand };

        // Outer glow
        g.Children.Add(new Ellipse
        {
            Width = 40, Height = 40,
            Fill = Brushes.Transparent,
            Effect = new DropShadowEffect
            {
                Color = c, BlurRadius = 12, Opacity = 0.5, ShadowDepth = 0,
            },
        });

        // Ring border
        g.Children.Add(new Ellipse
        {
            Width = 36, Height = 36,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(c),
            StrokeThickness = 2.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Inner filled circle
        g.Children.Add(new Ellipse
        {
            Width = 22, Height = 22,
            Fill = new SolidColorBrush(c),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return g;
    }

    // ── Style 3: Rounded square with glossy shine ──
    private Grid MakeStyle3(Color c)
    {
        var g = new Grid { Width = 36, Height = 36, Cursor = System.Windows.Input.Cursors.Hand };

        // Background
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(c),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = c, BlurRadius = 8, Opacity = 0.35, ShadowDepth = 0,
            },
        });

        // Top highlight shine
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(7, 7, 0, 0),
            Height = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1, 1, 1, 0),
            Background = new LinearGradientBrush(
                Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                90),
        });

        return g;
    }

    // ── Style 4: Double ring (outlined) ──
    private Grid MakeStyle4(Color c)
    {
        var g = new Grid { Width = 40, Height = 40, Cursor = System.Windows.Input.Cursors.Hand };

        // Outer ring
        g.Children.Add(new Ellipse
        {
            Width = 38, Height = 38,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B)),
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Inner ring
        g.Children.Add(new Ellipse
        {
            Width = 28, Height = 28,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(c),
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Center dot
        g.Children.Add(new Ellipse
        {
            Width = 12, Height = 12,
            Fill = new SolidColorBrush(c),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return g;
    }

    // ── Style 5: Glass orb with radial gradient + highlight ──
    private Grid MakeStyle5(Color c)
    {
        var g = new Grid { Width = 40, Height = 40, Cursor = System.Windows.Input.Cursors.Hand };

        // Glow behind
        g.Children.Add(new Ellipse
        {
            Width = 40, Height = 40,
            Fill = Brushes.Transparent,
            Effect = new DropShadowEffect
            {
                Color = c, BlurRadius = 14, Opacity = 0.4, ShadowDepth = 0,
            },
        });

        // Main orb with radial gradient
        var brightColor = Color.FromArgb(0xFF,
            (byte)System.Math.Min(255, c.R + 80),
            (byte)System.Math.Min(255, c.G + 80),
            (byte)System.Math.Min(255, c.B + 80));
        var darkColor = Color.FromArgb(0xFF,
            (byte)(c.R * 0.4),
            (byte)(c.G * 0.4),
            (byte)(c.B * 0.4));

        var radialBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            Center = new Point(0.4, 0.4),
        };
        radialBrush.GradientStops.Add(new GradientStop(brightColor, 0.0));
        radialBrush.GradientStops.Add(new GradientStop(c, 0.5));
        radialBrush.GradientStops.Add(new GradientStop(darkColor, 1.0));

        g.Children.Add(new Ellipse
        {
            Width = 34, Height = 34,
            Fill = radialBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Specular highlight
        var highlight = new Ellipse
        {
            Width = 16, Height = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 4, 0),
            Fill = new RadialGradientBrush(
                Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
        };
        g.Children.Add(highlight);

        return g;
    }
}
