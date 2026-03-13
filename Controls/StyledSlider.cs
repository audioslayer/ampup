using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls;

/// <summary>
/// A single-thumb slider matching the RangeSlider visual style.
/// </summary>
public class StyledSlider : FrameworkElement
{
    private const double TrackHeight = 3.0;
    private const double ThumbRadius = 6.0;
    private const double TrackMargin = 8.0;

    private static readonly Brush s_trackBg;
    private static readonly Brush s_thumbBorder;
    private static readonly Typeface s_typeface = new("Segoe UI");

    static StyledSlider()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        bg.Freeze();
        s_trackBg = bg;

        var border = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        border.Freeze();
        s_thumbBorder = border;
    }

    // ── Dependency Properties ───────────────────────────────────

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(StyledSlider),
            new FrameworkPropertyMetadata(ThemeManager.Accent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }

    public string Suffix { get; set; } = "%";

    // ── Events ──────────────────────────────────────────────────

    public event EventHandler? ValueChanged;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StyledSlider)d).ValueChanged?.Invoke(d, EventArgs.Empty);
    }

    // ── Drag state ──────────────────────────────────────────────

    private bool _dragging;

    // ── Layout ──────────────────────────────────────────────────

    private double TrackLeft => TrackMargin;
    private double TrackRight => ActualWidth - TrackMargin;
    private double TrackWidth => TrackRight - TrackLeft;
    private double TrackY => 14.0;

    private double ValueToX(double value)
    {
        double range = Maximum - Minimum;
        if (range <= 0) return TrackLeft;
        double ratio = (value - Minimum) / range;
        return TrackLeft + ratio * TrackWidth;
    }

    private double XToValue(double x)
    {
        double ratio = Math.Clamp((x - TrackLeft) / TrackWidth, 0, 1);
        return Minimum + ratio * (Maximum - Minimum);
    }

    // ── Rendering ───────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double cy = TrackY;
        double trackTop = cy - TrackHeight / 2;

        // Background track
        dc.DrawRoundedRectangle(s_trackBg, null,
            new Rect(TrackLeft, trackTop, TrackWidth, TrackHeight), 1.5, 1.5);

        // Filled portion (accent colored)
        double vx = ValueToX(Value);
        var fillBrush = new SolidColorBrush(AccentColor);
        fillBrush.Freeze();
        if (vx > TrackLeft)
        {
            dc.DrawRoundedRectangle(fillBrush, null,
                new Rect(TrackLeft, trackTop, vx - TrackLeft, TrackHeight), 1.5, 1.5);
        }

        // Thumb (accent colored)
        var thumbFillBrush = new SolidColorBrush(AccentColor);
        thumbFillBrush.Freeze();
        var thumbPen = new Pen(s_thumbBorder, 1.5);
        thumbPen.Freeze();
        dc.DrawEllipse(thumbFillBrush, thumbPen, new Point(vx, cy), ThumbRadius, ThumbRadius);

        // Inner dot (white center)
        var whiteDot = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        whiteDot.Freeze();
        dc.DrawEllipse(whiteDot, null, new Point(vx, cy), 2, 2);

        // Value label below thumb
        var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        textBrush.Freeze();
        var text = new FormattedText($"{(int)Value}{Suffix}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_typeface, 10, textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(vx - text.Width / 2, cy + ThumbRadius + 4));
    }

    // ── Mouse interaction ───────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        Value = Math.Round(Math.Clamp(XToValue(pos.X), Minimum, Maximum));
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        Value = Math.Round(Math.Clamp(XToValue(pos.X), Minimum, Maximum));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width,
            38);
    }
}
