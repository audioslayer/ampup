using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AmpUp.Mac.Controls;

public partial class VuMeterControl : UserControl
{
    // 16 segments matching Windows VuMeterControl
    private const int SegmentCount = 16;
    private const double SegmentGap = 1.5;

    private double _level;      // 0.0 – 1.0
    private double _displayLevel;
    private double _peakLevel;
    private int _peakHoldTicks;
    private Color _barColor = Color.Parse("#00E676");

    // Standard segment colors: green (0-9), orange (10-12), red (13-15)
    private static readonly Color Green = Color.Parse("#00E676");
    private static readonly Color Orange = Color.Parse("#FFB800");
    private static readonly Color Red = Color.Parse("#FF4444");
    private static readonly Color DimGreen = Color.Parse("#0D3318");
    private static readonly Color DimOrange = Color.Parse("#2A1F00");
    private static readonly Color DimRed = Color.Parse("#2A0D0D");

    public VuMeterControl()
    {
        InitializeComponent();
    }

    public double Level
    {
        get => _level;
        set => _level = Math.Clamp(value, 0, 1);
    }

    public Color BarColor
    {
        get => _barColor;
        set { _barColor = value; InvalidateVisual(); }
    }

    public void Tick()
    {
        // Smooth: fast attack, slow decay
        if (_level > _displayLevel)
            _displayLevel += (_level - _displayLevel) * 0.5;
        else
            _displayLevel += (_level - _displayLevel) * 0.12;

        // Peak hold (1.5s at ~20fps = 30 ticks)
        if (_displayLevel > _peakLevel)
        {
            _peakLevel = _displayLevel;
            _peakHoldTicks = 30;
        }
        else if (_peakHoldTicks > 0)
        {
            _peakHoldTicks--;
        }
        else
        {
            _peakLevel *= 0.95;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        var segH = (h - (SegmentCount - 1) * SegmentGap) / SegmentCount;
        var litCount = (int)(_displayLevel * SegmentCount);
        var peakSeg = (int)(_peakLevel * SegmentCount);

        for (int i = 0; i < SegmentCount; i++)
        {
            // Segments draw bottom-up: segment 0 is bottom
            int segIdx = SegmentCount - 1 - i;
            double y = i * (segH + SegmentGap);

            Color litColor, dimColor;
            if (segIdx >= 13) { litColor = Red; dimColor = DimRed; }
            else if (segIdx >= 10) { litColor = Orange; dimColor = DimOrange; }
            else { litColor = _barColor; dimColor = DimGreen; }

            bool isLit = segIdx < litCount;
            bool isPeak = segIdx == peakSeg && _peakHoldTicks > 0;

            var color = (isLit || isPeak) ? litColor : dimColor;
            var brush = new SolidColorBrush(color);
            context.DrawRectangle(brush, null, new Rect(0, y, w, segH), 1, 1);
        }
    }
}
