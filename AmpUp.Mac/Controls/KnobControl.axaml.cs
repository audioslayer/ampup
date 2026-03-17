using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AmpUp.Mac.Controls;

public partial class KnobControl : UserControl
{
    // Arc geometry: 225° start, 270° sweep (matching Windows AnimatedKnobControl)
    private const double StartAngle = 135;  // degrees from 12 o'clock (225° from 3 o'clock)
    private const double SweepAngle = 270;
    private const double Radius = 40;
    private const double TrackWidth = 6;
    private static readonly Point Center = new(50, 50);

    private double _position;   // 0.0 – 1.0
    private double _target;
    private Color _arcColor = Color.Parse("#00E676");

    public KnobControl()
    {
        InitializeComponent();
        KnobCanvas.RenderTransform = null;
    }

    public Color ArcColor
    {
        get => _arcColor;
        set { _arcColor = value; InvalidateVisual(); }
    }

    public void SetTarget(double t) => _target = Math.Clamp(t, 0, 1);

    public void Tick()
    {
        // Smooth lerp toward target (0.5 per tick, matching Windows)
        _position += (_target - _position) * 0.5;
        InvalidateVisual();
    }

    public string PercentText { get; set; } = "0%";

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Background track arc
        var trackPen = new Pen(new SolidColorBrush(Color.Parse("#2A2A2A")), TrackWidth, lineCap: PenLineCap.Round);
        DrawArc(context, trackPen, 1.0);

        // Value arc
        if (_position > 0.005)
        {
            var valuePen = new Pen(new SolidColorBrush(_arcColor), TrackWidth, lineCap: PenLineCap.Round);
            DrawArc(context, valuePen, _position);
        }

        // Center circle (knob face)
        var faceBrush = new SolidColorBrush(Color.Parse("#1C1C1C"));
        var facePen = new Pen(new SolidColorBrush(Color.Parse("#333333")), 1.5);
        context.DrawEllipse(faceBrush, facePen, Center, 28, 28);

        // Position indicator dot
        var dotAngle = StartAngle + SweepAngle * _position;
        var dotRad = dotAngle * Math.PI / 180;
        var dotX = Center.X + Math.Cos(dotRad) * 20;
        var dotY = Center.Y + Math.Sin(dotRad) * 20;
        context.DrawEllipse(new SolidColorBrush(_arcColor), null, new Point(dotX, dotY), 3, 3);
    }

    private void DrawArc(DrawingContext context, Pen pen, double fraction)
    {
        if (fraction <= 0) return;

        var sweep = SweepAngle * Math.Clamp(fraction, 0, 1);
        var startRad = StartAngle * Math.PI / 180;
        var endRad = (StartAngle + sweep) * Math.PI / 180;

        var startPt = new Point(Center.X + Math.Cos(startRad) * Radius, Center.Y + Math.Sin(startRad) * Radius);
        var endPt = new Point(Center.X + Math.Cos(endRad) * Radius, Center.Y + Math.Sin(endRad) * Radius);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(startPt, false);
            ctx.ArcTo(endPt, new Size(Radius, Radius), 0, sweep > 180, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geo);
    }
}
