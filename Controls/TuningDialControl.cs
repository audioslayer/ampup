using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls;

/// <summary>A compact mouse-wheel and drag controlled rotary dial.</summary>
public class TuningDialControl : FrameworkElement
{
    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;
    private Point _dragOrigin;
    private double _dragValue;
    private bool _dragging;

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TuningDialControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TuningDialControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TuningDialControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged, CoerceValue));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(TuningDialControl),
        new FrameworkPropertyMetadata(ThemeManager.Accent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public double Step { get; set; } = 1.0;
    public string Suffix { get; set; } = "";
    public string ValueFormat { get; set; } = "F0";

    public event EventHandler? ValueChanged;

    public TuningDialControl()
    {
        Cursor = Cursors.SizeNS;
        Focusable = true;
    }

    private static object CoerceValue(DependencyObject d, object value)
    {
        var dial = (TuningDialControl)d;
        return Math.Clamp((double)value, dial.Minimum, dial.Maximum);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TuningDialControl)d).ValueChanged?.Invoke(d, EventArgs.Empty);

    protected override Size MeasureOverride(Size availableSize) => new(68, 68);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth > 0 ? ActualWidth : 68;
        double height = ActualHeight > 0 ? ActualHeight : 68;
        var center = new Point(width / 2, height / 2);
        double radius = Math.Min(width, height) / 2 - 7;
        double ratio = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;

        var face = new RadialGradientBrush(
            Color.FromRgb(0x34, 0x36, 0x3B), Color.FromRgb(0x16, 0x17, 0x1A));
        face.Freeze();
        dc.DrawEllipse(face, new Pen(new SolidColorBrush(Color.FromRgb(0x48, 0x4A, 0x50)), 1),
            center, radius - 4, radius - 4);

        var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3C, 0x42)), 4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        trackPen.Freeze();
        dc.DrawGeometry(null, trackPen, CreateArc(center, radius, StartAngle, SweepAngle));

        if (ratio > 0.001)
        {
            var accentBrush = new SolidColorBrush(AccentColor);
            accentBrush.Freeze();
            var valuePen = new Pen(accentBrush, 4)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            valuePen.Freeze();
            dc.DrawGeometry(null, valuePen, CreateArc(center, radius, StartAngle, SweepAngle * ratio));
        }

        double pointerAngle = (StartAngle + SweepAngle * ratio) * Math.PI / 180.0;
        var pointerEnd = new Point(
            center.X + Math.Cos(pointerAngle) * (radius - 10),
            center.Y + Math.Sin(pointerAngle) * (radius - 10));
        var pointerPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)), 2.2)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pointerPen.Freeze();
        dc.DrawLine(pointerPen, center, pointerEnd);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xB8, 0xBA, 0xC2)), null, center, 2.5, 2.5);

        var valueText = new FormattedText(
            $"{Value.ToString(ValueFormat, CultureInfo.InvariantCulture)}{Suffix}",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, height - 17));
    }

    private static StreamGeometry CreateArc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        Point PointAt(double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Point(center.X + Math.Cos(radians) * radius,
                center.Y + Math.Sin(radians) * radius);
        }

        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(PointAt(startDegrees), false, false);
        context.ArcTo(PointAt(startDegrees + sweepDegrees), new Size(radius, radius), 0,
            sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        geometry.Freeze();
        return geometry;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        _dragValue = Value;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        Point current = e.GetPosition(this);
        double pixels = (_dragOrigin.Y - current.Y) + (current.X - _dragOrigin.X) * 0.45;
        SetSnappedValue(_dragValue + pixels / 120.0 * (Maximum - Minimum));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetSnappedValue(Value + (e.Delta > 0 ? Step : -Step));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Right)
        {
            SetSnappedValue(Value + Step);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Left)
        {
            SetSnappedValue(Value - Step);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void SetSnappedValue(double value)
    {
        if (Step > 0) value = Math.Round(value / Step) * Step;
        Value = Math.Clamp(value, Minimum, Maximum);
    }
}
