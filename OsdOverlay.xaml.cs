using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AmpUp;

public partial class OsdOverlay : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private readonly Storyboard _fadeOut;
    private bool _closing;
    private OsdPosition _position = OsdPosition.BottomRight;

    public OsdOverlay()
    {
        InitializeComponent();
        ApplyAccentColors();

        _fadeOut = (Storyboard)FindResource("FadeOut");
        _fadeOut.Completed += (_, _) => Hide();

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            AnimateOut();
        };
    }

    private void ApplyAccentColors()
    {
        var accent = ThemeManager.Accent;

        // Border gradient
        var borderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(accent, 0x55),
            ThemeManager.WithAlpha(accent, 0x22),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        borderBrush.Freeze();
        RootPanel.BorderBrush = borderBrush;

        // Category label
        CategoryLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));

        // Value text
        OsdValue.Foreground = new SolidColorBrush(accent);

        // Bar track background
        BarTrack.Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x1A));

        // Bar fill gradient (AccentDim -> Accent -> AccentGlow)
        var fillBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(ThemeManager.AccentDim, 0),
            new GradientStop(accent, 0.7),
            new GradientStop(ThemeManager.AccentGlow, 1),
        }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        fillBrush.Freeze();
        BarFill.Background = fillBrush;

        // Bar glow background
        BarGlow.Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));
    }

    public void SetPosition(OsdPosition position)
    {
        _position = position;
    }

    private void SetTextIcon(string text, double fontSize = 24)
    {
        OsdIconHost.Content = new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void SetSymbolIcon(string symbolName, string colorHex)
    {
        var icon = new SymbolIcon { FontSize = 26, VerticalAlignment = VerticalAlignment.Center };
        if (Enum.TryParse<SymbolRegular>(symbolName, out var sym))
            icon.Symbol = sym;
        try { icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }
        OsdIconHost.Content = icon;
    }

    /// <summary>
    /// Show volume change: label, percentage, animated bar.
    /// </summary>
    public void ShowVolume(string label, int percent, string symbolName = "Speaker224")
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = "VOLUME";
        SetSymbolIcon(symbolName, ThemeManager.AccentHex);
        OsdTitle.Text = label;
        OsdValue.Text = $"{percent}%";
        OsdValue.Visibility = Visibility.Visible;
        BarContainer.Visibility = Visibility.Visible;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2);

        // Animate bar fill after layout
        Dispatcher.InvokeAsync(() =>
        {
            double maxWidth = BarContainer.ActualWidth > 0 ? BarContainer.ActualWidth : 300;
            double targetWidth = maxWidth * Math.Clamp(percent / 100.0, 0, 1);

            var fillAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BarFill.BeginAnimation(WidthProperty, fillAnim);

            var glowAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BarGlow.BeginAnimation(WidthProperty, glowAnim);

            // Pulse the glow
            var pulse = (Storyboard)FindResource("BarGlowPulse");
            pulse.Begin(this);
        }, DispatcherPriority.Loaded);

        _dismissTimer.Start();
    }

    /// <summary>
    /// Show profile switch: colored Fluent icon + profile name.
    /// </summary>
    public void ShowProfileSwitch(string profileName, ProfileIconConfig iconCfg)
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = "PROFILE";
        SetSymbolIcon(iconCfg.Symbol, iconCfg.Color);
        OsdTitle.Text = profileName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2.5);
        _dismissTimer.Start();
    }

    /// <summary>
    /// Show device switch: device name + output/input type.
    /// </summary>
    public void ShowDevice(string deviceName, bool isOutput)
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = isOutput ? "OUTPUT DEVICE" : "INPUT DEVICE";
        SetSymbolIcon(isOutput ? "Speaker224" : "Mic24", ThemeManager.AccentHex);
        OsdTitle.Text = deviceName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2.5);
        _dismissTimer.Start();
    }

    private void PositionAndShow()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 20;

        switch (_position)
        {
            case OsdPosition.TopLeft:
                Left = workArea.Left + margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopCenter:
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopRight:
                Left = workArea.Right - Width - margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.BottomLeft:
                Left = workArea.Left + margin;
                Top = workArea.Bottom - 120;
                break;
            case OsdPosition.BottomCenter:
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Bottom - 120;
                break;
            case OsdPosition.BottomRight:
            default:
                Left = workArea.Right - Width - margin;
                Top = workArea.Bottom - 120;
                break;
        }

        bool alreadyVisible = IsVisible && !_closing;

        // Stop any in-progress fade-out
        if (_closing)
        {
            _fadeOut.Stop(this);
            _closing = false;
        }

        Show();

        // Only animate fade-in if not already showing
        if (!alreadyVisible)
        {
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        }
        else
        {
            // Ensure fully opaque (in case fade-out was mid-animation)
            RootPanel.Opacity = 1;
            RootPanel.Margin = new Thickness(0);
        }
    }

    private void AnimateOut()
    {
        if (_closing) return;
        _closing = true;
        _fadeOut.Begin(this);
    }
}
