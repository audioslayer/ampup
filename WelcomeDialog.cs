using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AmpUp;

/// <summary>
/// First-run welcome dialog. Shown once when HasCompletedSetup is false.
/// Pure code-behind window — no XAML file.
/// </summary>
public class WelcomeDialog : Window
{
    private readonly Action _onOpenSettings;

    public WelcomeDialog(Action onOpenSettings)
    {
        _onOpenSettings = onOpenSettings;

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

        // Header
        root.Children.Add(new TextBlock
        {
            Text = "Welcome to Amp Up 🎚️",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

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

        // Steps
        root.Children.Add(BuildStep("🔌", "Connect your Turn Up",
            "Plug in the USB cable. Windows will install the driver automatically."));

        root.Children.Add(BuildStepWithButton("🔍", "Find your device",
            "Head to Settings → click Auto-Detect to find your Turn Up.",
            "Open Settings", accent, () =>
            {
                _onOpenSettings();
                Close();
            }));

        root.Children.Add(BuildStep("🎛️", "Assign your knobs",
            "In the Mixer tab, pick what each knob controls — master volume, Spotify, Discord, anything."));

        root.Children.Add(BuildStep("💡", "Set up your lights",
            "Head to Lights to pick effects and colors for each knob's RGB."));

        root.Children.Add(BuildStep("🎮", "Configure your buttons",
            "Assign actions to each button — macros, mute toggles, profile switches, and more."));

        // Bottom separator
        root.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 20, 0, 20)
        });

        // Got it button (full-width, accent)
        var gotItBtn = new Button
        {
            Content = "Got it, let's go!",
            Background = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            BorderThickness = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0, 12, 0, 12),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        gotItBtn.Style = CreateButtonStyle(accent);
        gotItBtn.Click += (_, _) => Close();
        root.Children.Add(gotItBtn);

        return outer;
    }

    private static UIElement BuildStep(string icon, string title, string description)
    {
        return BuildStepCore(icon, title, description, null, default, null);
    }

    private static UIElement BuildStepWithButton(string icon, string title, string description,
        string btnText, System.Windows.Media.Color accent, Action onClick)
    {
        return BuildStepCore(icon, title, description, btnText, accent, onClick);
    }

    private static UIElement BuildStepCore(string icon, string title, string description,
        string? btnText, System.Windows.Media.Color accent, Action? onClick)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };

        // Icon
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 22,
            Width = 38,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 10, 0)
        };
        DockPanel.SetDock(iconBlock, Dock.Left);
        row.Children.Add(iconBlock);

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
