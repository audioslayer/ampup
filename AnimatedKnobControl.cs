using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WolfMixer;

public class AnimatedKnobControl : UserControl
{
    private float _value = 0f;
    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    private Color _arcColor = Color.FromArgb(0, 180, 216);
    public Color ArcColor
    {
        get => _arcColor;
        set { _arcColor = value; Invalidate(); }
    }

    public string PercentText { get; set; } = "0%";

    public AnimatedKnobControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Size = new Size(88, 88);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(88, 88);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;

        // 1. Background
        g.Clear(BackColor);

        int w = Width;
        int h = Height;
        float cx = w / 2f;
        float cy = h / 2f;

        // 2. Outer ring
        float outerMargin = 4f;
        var outerRect = new RectangleF(outerMargin, outerMargin, w - outerMargin * 2, h - outerMargin * 2);
        using (var outerPen = new Pen(Color.FromArgb(0x2A, 0x2A, 0x2A), 2f))
        {
            g.DrawEllipse(outerPen, outerRect);
        }

        // Arc bounds (slightly inset from outer ring)
        float arcMargin = 10f;
        var arcRect = new RectangleF(arcMargin, arcMargin, w - arcMargin * 2, h - arcMargin * 2);

        const float startAngle = 135f;
        const float totalSweep = 270f;
        float valueSweep = _value * totalSweep;

        // 3. Track arc
        using (var trackPen = new Pen(Color.FromArgb(0x36, 0x36, 0x36), 5f))
        {
            trackPen.StartCap = LineCap.Round;
            trackPen.EndCap = LineCap.Round;
            g.DrawArc(trackPen, arcRect, startAngle, totalSweep);
        }

        if (_value > 0.001f)
        {
            // 5. Glow effect (drawn behind value arc)
            using (var glowPen = new Pen(Color.FromArgb((int)(255 * 0.35f), _arcColor), 10f))
            {
                glowPen.StartCap = LineCap.Round;
                glowPen.EndCap = LineCap.Round;
                g.DrawArc(glowPen, arcRect, startAngle, valueSweep);
            }

            // 4. Value arc
            using (var valuePen = new Pen(_arcColor, 5f))
            {
                valuePen.StartCap = LineCap.Round;
                valuePen.EndCap = LineCap.Round;
                g.DrawArc(valuePen, arcRect, startAngle, valueSweep);
            }
        }

        // 6. Needle dot
        float tipAngleRad = (float)((startAngle + valueSweep) * Math.PI / 180.0);
        float radius = (w / 2f) - 10f;
        float tipX = cx + radius * (float)Math.Cos(tipAngleRad);
        float tipY = cy + radius * (float)Math.Sin(tipAngleRad);

        // Glow circle 14px
        using (var glowBrush = new SolidBrush(Color.FromArgb(128, _arcColor)))
        {
            g.FillEllipse(glowBrush, tipX - 7f, tipY - 7f, 14f, 14f);
        }
        // Filled circle 8px
        using (var dotBrush = new SolidBrush(_arcColor))
        {
            g.FillEllipse(dotBrush, tipX - 4f, tipY - 4f, 8f, 8f);
        }

        // 7. Center circle
        float centerSize = 32f;
        float centerX = cx - centerSize / 2f;
        float centerY = cy - centerSize / 2f;
        using (var centerBrush = new SolidBrush(Color.FromArgb(0x1A, 0x1A, 0x1A)))
        {
            g.FillEllipse(centerBrush, centerX, centerY, centerSize, centerSize);
        }
        using (var centerBorderPen = new Pen(Color.FromArgb(0x2A, 0x2A, 0x2A), 1f))
        {
            g.DrawEllipse(centerBorderPen, centerX, centerY, centerSize, centerSize);
        }

        // 8. Percentage text
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.FromArgb(0xE8, 0xE8, 0xE8));
        var textSize = g.MeasureString(PercentText, font);
        g.DrawString(PercentText, font, textBrush,
            cx - textSize.Width / 2f,
            cy - textSize.Height / 2f);
    }
}
