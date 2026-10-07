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
    // Soft pill style: thick rounded track, thumb hidden until hover/drag/focus.
    private const double TrackHeight = 8.0;
    private const double ThumbRadius = 7.0;
    private const double HaloRadius = 11.0;
    private const double TrackMargin = 8.0;

    private static readonly Typeface s_typeface = new("Segoe UI");

    private Brush GetTrackBg() => (Brush)Application.Current.FindResource("InputBgBrush");
    private Brush GetThumbBorder() => (Brush)Application.Current.FindResource("BgDarkBrush");

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
    public bool ShowLabel { get; set; } = true;
    /// <summary>Rounding step for values. 1.0 = integer snap (default), 0.1 = one decimal, 0 = continuous.</summary>
    public double Step { get; set; } = 1.0;
    /// <summary>Format string for the label. Default "F0" (integer). Use "F1" for one decimal.</summary>
    public string LabelFormat { get; set; } = "F0";

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

    private double SnapValue(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        if (Step > 0) v = Math.Round(v / Step) * Step;
        return Math.Clamp(v, Minimum, Maximum);
    }

    // ── Rendering ───────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double cy = TrackY;
        double trackTop = cy - TrackHeight / 2;

        // Background track
        dc.DrawRoundedRectangle(GetTrackBg(), null,
            new Rect(TrackLeft, trackTop, TrackWidth, TrackHeight), TrackHeight / 2, TrackHeight / 2);

        // Filled portion (accent colored)
        double vx = ValueToX(Value);
        var fillBrush = new SolidColorBrush(AccentColor);
        fillBrush.Freeze();
        if (vx > TrackLeft)
        {
            dc.DrawRoundedRectangle(fillBrush, null,
                new Rect(TrackLeft, trackTop, vx - TrackLeft, TrackHeight), TrackHeight / 2, TrackHeight / 2);
        }

        // Thumb: only while hovered / dragged / focused — quiet at rest.
        if (IsMouseOver || _dragging || IsKeyboardFocused)
        {
            var halo = new SolidColorBrush(Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B));
            halo.Freeze();
            dc.DrawEllipse(halo, null, new Point(vx, cy), HaloRadius, HaloRadius);
            dc.DrawEllipse(Brushes.White, null, new Point(vx, cy), ThumbRadius, ThumbRadius);
        }

        // Value label below thumb
        if (ShowLabel)
        {
            var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            textBrush.Freeze();
            var text = new FormattedText($"{Value.ToString(LabelFormat)}{Suffix}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, s_typeface, 10, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(vx - text.Width / 2, cy + ThumbRadius + 4));
        }
    }

    // ── Mouse interaction ───────────────────────────────────────

    public StyledSlider()
    {
        Cursor = Cursors.Hand;
        Focusable = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Step > 0 ? Step : (Maximum - Minimum) / 100.0;
        double big = Math.Max(step, (Maximum - Minimum) / 10.0);
        double? v = e.Key switch
        {
            Key.Right or Key.Up => Value + step,
            Key.Left or Key.Down => Value - step,
            Key.PageUp => Value + big,
            Key.PageDown => Value - big,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (v == null) return;
        Value = SnapValue(v.Value);
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        Value = SnapValue(XToValue(pos.X));
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        Value = SnapValue(XToValue(pos.X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width,
            38);
    }
}
