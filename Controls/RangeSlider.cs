using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WolfMixer.Controls;

/// <summary>
/// A dual-thumb range slider for selecting min/max volume.
/// Renders a track with two draggable thumbs and a filled range between them.
/// </summary>
public class RangeSlider : FrameworkElement
{
    private const double TrackHeight = 4.0;
    private const double ThumbRadius = 7.0;
    private const double TrackMargin = 8.0; // left/right padding for thumb overhang

    // Frozen pens/brushes
    private static readonly Brush s_trackBg;
    private static readonly Brush s_thumbBorder;
    private static readonly Typeface s_typeface = new("Segoe UI");

    static RangeSlider()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        bg.Freeze();
        s_trackBg = bg;

        var border = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
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
            new FrameworkPropertyMetadata(Color.FromRgb(0x00, 0xE6, 0x76), FrameworkPropertyMetadataOptions.AffectsRender));

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
    private double TrackY => ActualHeight / 2.0;

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
            new Rect(TrackLeft, trackTop, TrackWidth, TrackHeight), 2, 2);

        // Filled range
        double lx = ValueToX(LowerValue);
        double ux = ValueToX(UpperValue);
        var accentBrush = new SolidColorBrush(AccentColor);
        accentBrush.Freeze();
        if (ux > lx)
        {
            dc.DrawRoundedRectangle(accentBrush, null,
                new Rect(lx, trackTop, ux - lx, TrackHeight), 2, 2);
        }

        // Glow behind thumbs
        var glowBrush = new RadialGradientBrush(
            Color.FromArgb(0x40, AccentColor.R, AccentColor.G, AccentColor.B),
            Colors.Transparent);
        glowBrush.Freeze();
        dc.DrawEllipse(glowBrush, null, new Point(lx, cy), ThumbRadius + 4, ThumbRadius + 4);
        dc.DrawEllipse(glowBrush, null, new Point(ux, cy), ThumbRadius + 4, ThumbRadius + 4);

        // Thumbs
        var thumbPen = new Pen(s_thumbBorder, 2);
        thumbPen.Freeze();
        dc.DrawEllipse(accentBrush, thumbPen, new Point(lx, cy), ThumbRadius, ThumbRadius);
        dc.DrawEllipse(accentBrush, thumbPen, new Point(ux, cy), ThumbRadius, ThumbRadius);

        // Value labels
        var textBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        textBrush.Freeze();

        var lowerText = new FormattedText($"{(int)LowerValue}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_typeface, 10, textBrush, 1.0);
        var upperText = new FormattedText($"{(int)UpperValue}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_typeface, 10, textBrush, 1.0);

        dc.DrawText(lowerText, new Point(lx - lowerText.Width / 2, cy + ThumbRadius + 3));
        dc.DrawText(upperText, new Point(ux - upperText.Width / 2, cy + ThumbRadius + 3));
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

        // Pick the closer thumb (prefer lower on tie)
        if (distLower <= distUpper && distLower < ThumbRadius + 8)
            _dragging = DragTarget.Lower;
        else if (distUpper < ThumbRadius + 8)
            _dragging = DragTarget.Upper;
        else
        {
            // Click on track — move closest thumb to click position
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
            Math.Max(32, ThumbRadius * 2 + 20));
    }
}
