using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AmpUp;

/// <summary>
/// First-run welcome dialog. Shown once when HasCompletedSetup is false.
/// Pure code-behind window — no XAML file.
/// </summary>
public class WelcomeDialog : Window
{
    private readonly Action _onOpenSettings;
    private readonly Action? _onImport;

    public WelcomeDialog(Action onOpenSettings, Action? onImport = null)
    {
        _onOpenSettings = onOpenSettings;
        _onImport = onImport;

        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var accent = ThemeManager.Accent;

        var outer = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(250, 0x0F, 0x0F, 0x0F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44,
                accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 32,
                ShadowDepth = 6,
                Opacity = 0.8,
                Direction = 270
            },
            Margin = new Thickness(12)
        };

        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 28) };
        outer.Child = root;

        // Header with logo
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        try
        {
            var logo = new Image
            {
                Source = new BitmapImage(new System.Uri("pack://application:,,,/Assets/icon/ampup-64.png")),
                Width = 42,
                Height = 42,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(logo);
        }
        catch { /* icon not found, skip */ }

        headerRow.Children.Add(new TextBlock
        {
            Text = "Welcome to Amp Up",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(headerRow);

        root.Children.Add(new TextBlock
        {
            Text = "Let's get your Turn Up connected in a few steps.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 24),
            TextWrapping = TextWrapping.Wrap
        });

        // Separator
        root.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 20)
        });

        // Steps with colored icon circles
        root.Children.Add(BuildStep("1", "Connect your Turn Up",
            "Plug in the USB cable. Windows will install the driver automatically.",
            Color.FromRgb(0x00, 0xE6, 0x76))); // green

        root.Children.Add(BuildStepWithButton("2", "Find your device",
            "Head to Settings → click Auto-Detect to find your Turn Up.",
            "Open Settings", accent, () =>
            {
                _onOpenSettings();
                Close();
            }, Color.FromRgb(0x00, 0xB0, 0xFF))); // blue

        root.Children.Add(BuildStepWithButton("3", "Import from Turn Up",
            "Already have a Turn Up config? Import your knob assignments, button bindings, and light settings.",
            "Import Config", accent, () =>
            {
                _onImport?.Invoke();
                Close();
            }, Color.FromRgb(0xFF, 0xB8, 0x00))); // amber

        root.Children.Add(BuildStep("4", "Assign your knobs",
            "In the Mixer tab, pick what each knob controls — master volume, Spotify, Discord, anything.",
            Color.FromRgb(0xE0, 0x40, 0xFF))); // purple

        root.Children.Add(BuildStep("5", "Set up your lights",
            "Head to Lights to pick effects and colors for each knob's RGB.",
            Color.FromRgb(0x00, 0xB0, 0xFF))); // blue

        root.Children.Add(BuildStep("6", "Configure your buttons",
            "Assign actions to each button — macros, mute toggles, profile switches, and more.",
            Color.FromRgb(0xFF, 0x44, 0x44))); // red

        // Bottom separator
        root.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 20, 0, 20)
        });

        // Got it button (full-width, accent, properly rounded)
        var gotItBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(0, 14, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = "Got it, let's go!",
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };

        var normalBg = Color.FromRgb(accent.R, accent.G, accent.B);
        var hoverBg = Color.FromRgb(
            (byte)Math.Min(255, accent.R + 25),
            (byte)Math.Min(255, accent.G + 25),
            (byte)Math.Min(255, accent.B + 25));

        gotItBorder.MouseEnter += (_, _) => gotItBorder.Background = new SolidColorBrush(hoverBg);
        gotItBorder.MouseLeave += (_, _) => gotItBorder.Background = new SolidColorBrush(normalBg);
        gotItBorder.MouseLeftButtonUp += (_, _) => Close();
        root.Children.Add(gotItBorder);

        return outer;
    }

    private static UIElement BuildStep(string icon, string title, string description,
        Color iconColor = default)
    {
        return BuildStepCore(icon, title, description, null, default, null, iconColor);
    }

    private static UIElement BuildStepWithButton(string icon, string title, string description,
        string btnText, Color accent, Action onClick, Color iconColor = default)
    {
        return BuildStepCore(icon, title, description, btnText, accent, onClick, iconColor);
    }

    private static UIElement BuildStepCore(string icon, string title, string description,
        string? btnText, Color accent, Action? onClick, Color iconColor = default)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };

        // Icon — colored circle with text symbol
        var iconCircle = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(iconColor == default
                ? Color.FromArgb(0x20, 0x00, 0xE6, 0x76)
                : Color.FromArgb(0x20, iconColor.R, iconColor.G, iconColor.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 16,
                Foreground = new SolidColorBrush(iconColor == default
                    ? Color.FromRgb(0x00, 0xE6, 0x76)
                    : iconColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            }
        };
        DockPanel.SetDock(iconCircle, Dock.Left);
        row.Children.Add(iconCircle);

        var textStack = new StackPanel();

        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        });

        textStack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, btnText != null ? 8 : 0)
        });

        if (btnText != null && onClick != null)
        {
            var btn = new Button
            {
                Content = btnText,
                Background = new SolidColorBrush(Color.FromArgb(0x20,
                    accent.R, accent.G, accent.B)),
                Foreground = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50,
                    accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                Padding = new Thickness(14, 6, 14, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btn.Style = CreateOutlineButtonStyle(accent);
            btn.Click += (_, _) => onClick();
            textStack.Children.Add(btn);
        }

        row.Children.Add(textStack);
        return row;
    }

    private static Style CreateButtonStyle(System.Windows.Media.Color accent)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.TemplateProperty, CreateBtnTemplate(
            Color.FromRgb(accent.R, accent.G, accent.B),
            Color.FromRgb(
                (byte)Math.Min(255, accent.R + 30),
                (byte)Math.Min(255, accent.G + 30),
                (byte)Math.Min(255, accent.B + 30)),
            Color.FromRgb(0x0F, 0x0F, 0x0F))));
        return style;
    }

    private static Style CreateOutlineButtonStyle(System.Windows.Media.Color accent)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.TemplateProperty, CreateBtnTemplate(
            Color.FromArgb(0x20, accent.R, accent.G, accent.B),
            Color.FromArgb(0x35, accent.R, accent.G, accent.B),
            Color.FromRgb(accent.R, accent.G, accent.B))));
        return style;
    }

    private static System.Windows.Controls.ControlTemplate CreateBtnTemplate(
        System.Windows.Media.Color normalBg,
        System.Windows.Media.Color hoverBg,
        System.Windows.Media.Color fg)
    {
        var template = new System.Windows.Controls.ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        borderFactory.SetValue(Border.BackgroundProperty,
            new SolidColorBrush(normalBg));

        var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        cpFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cpFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(cpFactory);

        template.VisualTree = borderFactory;

        // Hover trigger
        var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(hoverBg), "PART_Border"));
        // Can't easily target the border by name in simple template — use style triggers instead
        var triggerBg = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        triggerBg.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(hoverBg)));
        template.Triggers.Add(triggerBg);

        return template;
    }
}
