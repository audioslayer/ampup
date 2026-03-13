using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls;

/// <summary>
/// A dual-thumb range slider for selecting min/max volume.
/// Wave Link / Sonar inspired — clean track with two draggable thumbs.
/// </summary>
public class RangeSlider : FrameworkElement
{
    private const double TrackHeight = 3.0;
    private const double ThumbRadius = 6.0;
    private const double TrackMargin = 8.0;

    // Frozen pens/brushes
    private static readonly Brush s_trackBg;
    private static readonly Brush s_thumbBorder;
    private static readonly Typeface s_typeface = new("Segoe UI");

    static RangeSlider()
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
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowerValueProperty =
        DependencyProperty.Register(nameof(LowerValue), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty UpperValueProperty =
        DependencyProperty.Register(nameof(UpperValue), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(RangeSlider),
            new FrameworkPropertyMetadata(ThemeManager.Accent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => (double)GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => (double)GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }

    // ── Events ──────────────────────────────────────────────────

    public event EventHandler? LowerValueChanged;
    public event EventHandler? UpperValueChanged;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        if (e.Property == LowerValueProperty)
            slider.LowerValueChanged?.Invoke(slider, EventArgs.Empty);
        else
            slider.UpperValueChanged?.Invoke(slider, EventArgs.Empty);
    }

    // ── Drag state ──────────────────────────────────────────────

    private enum DragTarget { None, Lower, Upper }
    private DragTarget _dragging = DragTarget.None;

    // ── Layout ──────────────────────────────────────────────────

    private double TrackLeft => TrackMargin;
    private double TrackRight => ActualWidth - TrackMargin;
    private double TrackWidth => TrackRight - TrackLeft;
    private double TrackY => 14.0; // fixed: labels go below

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

        // Filled range (accent colored)
        double lx = ValueToX(LowerValue);
        double ux = ValueToX(UpperValue);
        var rangeFillBrush = new SolidColorBrush(AccentColor);
        rangeFillBrush.Freeze();
        if (ux > lx)
        {
            dc.DrawRoundedRectangle(rangeFillBrush, null,
                new Rect(lx, trackTop, ux - lx, TrackHeight), 1.5, 1.5);
        }

        // Thumbs (accent colored)
        var thumbFillBrush = new SolidColorBrush(AccentColor);
        thumbFillBrush.Freeze();
        var thumbPen = new Pen(s_thumbBorder, 1.5);
        thumbPen.Freeze();
        dc.DrawEllipse(thumbFillBrush, thumbPen, new Point(lx, cy), ThumbRadius, ThumbRadius);
        dc.DrawEllipse(thumbFillBrush, thumbPen, new Point(ux, cy), ThumbRadius, ThumbRadius);

        // Inner dot on thumbs (white center)
        var whiteDot = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        whiteDot.Freeze();
        dc.DrawEllipse(whiteDot, null, new Point(lx, cy), 2, 2);
        dc.DrawEllipse(whiteDot, null, new Point(ux, cy), 2, 2);

        // Value labels below thumbs
        var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        textBrush.Freeze();

        var lowerText = new FormattedText($"{(int)LowerValue}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_typeface, 10, textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var upperText = new FormattedText($"{(int)UpperValue}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_typeface, 10, textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(lowerText, new Point(lx - lowerText.Width / 2, cy + ThumbRadius + 4));
        dc.DrawText(upperText, new Point(ux - upperText.Width / 2, cy + ThumbRadius + 4));
    }

    // ── Mouse interaction ───────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        double lx = ValueToX(LowerValue);
        double ux = ValueToX(UpperValue);

        double distLower = Math.Abs(pos.X - lx);
        double distUpper = Math.Abs(pos.X - ux);

        if (distLower <= distUpper && distLower < ThumbRadius + 8)
            _dragging = DragTarget.Lower;
        else if (distUpper < ThumbRadius + 8)
            _dragging = DragTarget.Upper;
        else
        {
            double val = XToValue(pos.X);
            if (distLower <= distUpper)
            {
                LowerValue = Math.Min(Math.Round(val), UpperValue);
                _dragging = DragTarget.Lower;
            }
            else
            {
                UpperValue = Math.Max(Math.Round(val), LowerValue);
                _dragging = DragTarget.Upper;
            }
        }

        if (_dragging != DragTarget.None)
            CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging == DragTarget.None) return;

        var pos = e.GetPosition(this);
        double val = Math.Round(XToValue(pos.X));

        if (_dragging == DragTarget.Lower)
            LowerValue = Math.Clamp(val, Minimum, UpperValue);
        else
            UpperValue = Math.Clamp(val, LowerValue, Maximum);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = DragTarget.None;
        ReleaseMouseCapture();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width,
            38); // enough for track + labels
    }
}
