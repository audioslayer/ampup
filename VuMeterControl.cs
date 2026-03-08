using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WolfMixer;

public class VuMeterControl : UserControl
{
    private float _level = 0f;
    public float Level
    {
        get => _level;
        set
        {
            _level = Math.Clamp(value, 0f, 1f);
            if (_level > _peak)
            {
                _peak = _level;
                _peakHoldCount = 0;
            }
            Invalidate();
        }
    }

    private Color _barColor = Color.FromArgb(0, 180, 216);
    public Color BarColor
    {
        get => _barColor;
        set { _barColor = value; Invalidate(); }
    }

    private float _peak = 0f;
    private int _peakHoldCount = 0;
    private readonly System.Windows.Forms.Timer _animTimer;

    private static readonly Color ColorUnlit = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color ColorYellow = Color.FromArgb(255, 184, 0);
    private static readonly Color ColorRed = Color.FromArgb(255, 68, 68);
    private static readonly Color ColorPeak = Color.FromArgb(230, 230, 230);

    private const int SegmentCount = 16;
    private const int SegmentHeight = 3;
    private const int SegmentGap = 1;

    public VuMeterControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Size = new Size(12, 64);

        _animTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(12, 64);

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (_peak > _level)
        {
            _peakHoldCount++;
            if (_peakHoldCount > 30)
            {
                float peakSeg = _peak * SegmentCount;
                peakSeg = Math.Max(peakSeg - 1f, 0f);
                _peak = peakSeg / SegmentCount;
                if (_peak < _level) _peak = _level;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int litCount = (int)(_level * SegmentCount);
        int peakSeg = (int)(_peak * SegmentCount);

        for (int i = 0; i < SegmentCount; i++)
        {
            // Segments stacked bottom-to-top: segment 0 is bottom
            int y = Height - (i + 1) * (SegmentHeight + SegmentGap);

            Color litColor;
            if (i < 11)
                litColor = _barColor;
            else if (i < 14)
                litColor = ColorYellow;
            else
                litColor = ColorRed;

            bool isLit = i < litCount;
            Color segColor = isLit ? litColor : ColorUnlit;

            using var brush = new SolidBrush(segColor);
            g.FillRectangle(brush, 0, y, Width, SegmentHeight);

            // Peak hold indicator
            if (i == peakSeg && _peak > 0.001f)
            {
                using var peakBrush = new SolidBrush(ColorPeak);
                g.FillRectangle(peakBrush, 0, y, Width, SegmentHeight);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
