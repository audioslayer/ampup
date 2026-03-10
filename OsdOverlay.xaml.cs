using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WolfMixer;

public partial class OsdOverlay : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private readonly Storyboard _fadeOut;
    private bool _closing;

    public OsdOverlay()
    {
        InitializeComponent();

        _fadeOut = (Storyboard)FindResource("FadeOut");
        _fadeOut.Completed += (_, _) => Hide();

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            AnimateOut();
        };
    }

    /// <summary>
    /// Show volume change: label, percentage, animated bar.
    /// </summary>
    public void ShowVolume(string label, int percent, string icon = "🔊")
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = "VOLUME";
        OsdIcon.Text = icon;
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
    /// Show profile switch: emoji + profile name.
    /// </summary>
    public void ShowProfileSwitch(string profileName, string emoji)
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = "PROFILE";
        OsdIcon.Text = emoji;
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
        OsdIcon.Text = isOutput ? "🔊" : "🎤";
        OsdTitle.Text = deviceName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2.5);
        _dismissTimer.Start();
    }

    private void PositionAndShow()
    {
        // Position bottom-right above taskbar
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - 120;

        Show();

        var fadeIn = (Storyboard)FindResource("FadeIn");
        fadeIn.Begin(this);
    }

    private void AnimateOut()
    {
        if (_closing) return;
        _closing = true;
        _fadeOut.Begin(this);
    }
}
