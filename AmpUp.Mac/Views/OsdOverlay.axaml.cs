using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

/// <summary>
/// Transparent topmost overlay that shows knob/volume changes for 2s then fades out.
/// Positioned in the configured OSD corner (BottomRight by default).
/// Not hit-testable — clicks pass through.
/// </summary>
public partial class OsdOverlay : Window
{
    private DispatcherTimer? _dismissTimer;
    private DispatcherTimer? _fadeTimer;
    private bool _visible;
    private double _targetWidth;

    // Margin from screen edge (DIPs)
    private const double EdgeMargin = 32;
    private const double CardWidth = 320;
    private const double CardHeight = 90;

    public OsdOverlay()
    {
        InitializeComponent();
        Opened += (_, _) => PositionWindow(OsdPosition.BottomRight);
    }

    /// <summary>
    /// Show a volume change notification.
    /// Call from any thread — dispatches to UI thread internally.
    /// </summary>
    public void ShowVolume(string label, float volume, OsdConfig config)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LabelText.Text = label;
            ValueText.Text = $"{(int)Math.Round(volume * 100)}%";

            // Fill bar width proportional to volume
            _targetWidth = Math.Clamp(volume, 0f, 1f) * (CardWidth - 36);
            FillBar.Width = _targetWidth;

            // Accent glow: dim at low volumes, bright at high
            var accent = Color.Parse("#00E676");
            var glow = Color.Parse("#4000E676");
            ((Border)Card).BorderBrush = new SolidColorBrush(
                volume > 0.95f ? Color.Parse("#80FF6B6B") : glow);

            PositionWindow(config.Position, config.MonitorIndex);

            if (!IsVisible) Show();
            FadeIn();
            ResetDismissTimer(config.VolumeDuration);
        });
    }

    /// <summary>
    /// Show a profile switch notification.
    /// </summary>
    public void ShowProfileSwitch(string profileName, OsdConfig config)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LabelText.Text = "Profile";
            ValueText.Text = profileName.Length > 12 ? profileName[..12] + "…" : profileName;
            FillBar.Width = 0;
            ((Border)Card).BorderBrush = new SolidColorBrush(Color.Parse("#4029B6F6"));

            PositionWindow(config.Position, config.MonitorIndex);
            if (!IsVisible) Show();
            FadeIn();
            ResetDismissTimer(config.ProfileDuration);
        });
    }

    /// <summary>
    /// Show a device switch notification.
    /// </summary>
    public void ShowDeviceSwitch(string deviceName, OsdConfig config)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LabelText.Text = "Output Device";
            var truncated = deviceName.Length > 18 ? deviceName[..18] + "…" : deviceName;
            ValueText.Text = truncated;
            ValueText.FontSize = truncated.Length > 12 ? 14 : 20;
            FillBar.Width = 0;
            ((Border)Card).BorderBrush = new SolidColorBrush(Color.Parse("#40AB47BC"));

            PositionWindow(config.Position, config.MonitorIndex);
            if (!IsVisible) Show();
            FadeIn();
            ResetDismissTimer(config.DeviceDuration);
        });
    }

    // ── Positioning ───────────────────────────────────────────────────────────

    private void PositionWindow(OsdPosition position, int monitorIndex = 0)
    {
        var screens = Screens?.All;
        if (screens == null || screens.Count == 0) return;

        var screenIdx = Math.Clamp(monitorIndex, 0, screens.Count - 1);
        var screen = screens[screenIdx];
        var workArea = screen.WorkingArea;

        // Convert physical pixels → DIPs using screen scaling
        double scale = screen.Scaling;
        double areaX = workArea.X / scale;
        double areaY = workArea.Y / scale;
        double areaW = workArea.Width / scale;
        double areaH = workArea.Height / scale;

        double x = position switch
        {
            OsdPosition.TopLeft or OsdPosition.BottomLeft => areaX + EdgeMargin,
            OsdPosition.TopCenter or OsdPosition.BottomCenter => areaX + (areaW - CardWidth) / 2,
            _ => areaX + areaW - CardWidth - EdgeMargin, // TopRight, BottomRight
        };

        double y = position switch
        {
            OsdPosition.TopLeft or OsdPosition.TopCenter or OsdPosition.TopRight => areaY + EdgeMargin,
            _ => areaY + areaH - CardHeight - EdgeMargin, // Bottom variants
        };

        Position = new PixelPoint((int)x, (int)y);
    }

    // ── Fade animations ───────────────────────────────────────────────────────

    private void FadeIn()
    {
        _fadeTimer?.Stop();

        if (RootGrid.Opacity >= 1.0)
        {
            _visible = true;
            return;
        }

        _visible = true;
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += (_, _) =>
        {
            RootGrid.Opacity = Math.Min(1.0, RootGrid.Opacity + 0.12);
            if (RootGrid.Opacity >= 1.0)
            {
                RootGrid.Opacity = 1.0;
                _fadeTimer?.Stop();
            }
        };
        _fadeTimer.Start();
    }

    private void FadeOut()
    {
        _visible = false;
        _fadeTimer?.Stop();
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += (_, _) =>
        {
            RootGrid.Opacity = Math.Max(0.0, RootGrid.Opacity - 0.06);
            if (RootGrid.Opacity <= 0.0)
            {
                RootGrid.Opacity = 0.0;
                _fadeTimer?.Stop();
                Hide();
            }
        };
        _fadeTimer.Start();
    }

    private void ResetDismissTimer(double durationSeconds)
    {
        _dismissTimer?.Stop();
        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(0.5, durationSeconds))
        };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer?.Stop();
            FadeOut();
        };
        _dismissTimer.Start();
    }
}
