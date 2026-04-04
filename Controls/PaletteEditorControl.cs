using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Core.Models;

namespace AmpUp.Controls;

/// <summary>
/// Gradient bar + color chips palette editor. Shows the current palette as a smooth gradient
/// with draggable color stop chips below. Click chip to edit color, "+" to add, "x" to remove.
/// Preset palette row at the bottom for one-click palette selection.
/// </summary>
public class PaletteEditorControl : FrameworkElement
{
    // Layout constants
    private const double GradientBarHeight = 24;
    private const double ChipRadius = 8;
    private const double ChipY = GradientBarHeight + 14;
    private const double AddButtonSize = 16;
    private const double PresetRowY = GradientBarHeight + 34;
    private const double PresetSwatchW = 32;
    private const double PresetSwatchH = 18;
    private const double PresetGap = 3;
    private const double TotalHeight = PresetRowY + PresetSwatchH + 4;

    // State
    private ColorPalette _palette = new("Custom",
        new ColorStop(0.0, 0x00, 0xE6, 0x76),
        new ColorStop(1.0, 0xFF, 0xFF, 0xFF));
    private int _selectedStop = -1;
    private int _dragStop = -1;
    private double _dragOffsetX;
    private bool _isDragging;

    // Static frozen resources
    private static readonly Brush s_bgBrush;
    private static readonly Pen s_chipBorder;
    private static readonly Pen s_chipSelectedBorder;
    private static readonly Pen s_gradientBorder;
    private static readonly Brush s_addBrush;
    private static readonly Pen s_addPen;
    private static readonly Typeface s_typeface;
    private static readonly Brush s_labelBrush;
    private static readonly Brush s_presetBorder;

    static PaletteEditorControl()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        bg.Freeze();
        s_bgBrush = bg;

        var chipBorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        chipBorderBrush.Freeze();
        s_chipBorder = new Pen(chipBorderBrush, 1.5);
        s_chipBorder.Freeze();

        var accentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        accentBrush.Freeze();
        s_chipSelectedBorder = new Pen(accentBrush, 2);
        s_chipSelectedBorder.Freeze();

        var gradBorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        gradBorderBrush.Freeze();
        s_gradientBorder = new Pen(gradBorderBrush, 1);
        s_gradientBorder.Freeze();

        var addBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        addBrush.Freeze();
        s_addBrush = addBrush;
        s_addPen = new Pen(addBrush, 1.5);
        s_addPen.Freeze();

        s_typeface = new Typeface("Segoe UI");

        var labelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        labelBrush.Freeze();
        s_labelBrush = labelBrush;

        var presetBorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        presetBorderBrush.Freeze();
        s_presetBorder = presetBorderBrush;
    }

    /// <summary>Fired whenever stops are added, removed, moved, or recolored.</summary>
    public event Action<ColorPalette>? PaletteChanged;

    /// <summary>Fired when user clicks a stop chip (for external color picker binding).</summary>
    public event Action<int, Color>? StopClicked;

    public PaletteEditorControl()
    {
        Height = TotalHeight;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Get or set the current palette. Setting triggers a redraw.</summary>
    public ColorPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value ?? new ColorPalette("Custom",
                new ColorStop(0.0, 0, 0, 0), new ColorStop(1.0, 255, 255, 255));
            _selectedStop = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Update the color of the currently selected stop (called from external color picker).</summary>
    public void UpdateSelectedStopColor(Color color)
    {
        if (_selectedStop < 0 || _selectedStop >= _palette.Stops.Count) return;
        var stop = _palette.Stops[_selectedStop];
        stop.R = color.R;
        stop.G = color.G;
        stop.B = color.B;
        InvalidateVisual();
        PaletteChanged?.Invoke(_palette);
    }

    /// <summary>Get the sorted stops for palette position calculations.</summary>
    private List<ColorStop> SortedStops => _palette.Stops.OrderBy(s => s.Position).ToList();

    private double StopToX(double position) => position * (ActualWidth - 2) + 1;
    private double XToStop(double x) => Math.Clamp((x - 1) / (ActualWidth - 2), 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 20) return;

        // 1. Gradient bar
        var gradBrush = BuildGradientBrush();
        var gradRect = new Rect(0, 0, ActualWidth, GradientBarHeight);
        dc.DrawRoundedRectangle(gradBrush, s_gradientBorder, gradRect, 4, 4);

        // 2. Color stop chips
        var sorted = SortedStops;
        for (int i = 0; i < _palette.Stops.Count; i++)
        {
            var stop = _palette.Stops[i];
            double x = StopToX(stop.Position);

            // Chip triangle pointer + circle
            var chipColor = Color.FromRgb(stop.R, stop.G, stop.B);
            var chipBrush = new SolidColorBrush(chipColor);
            chipBrush.Freeze();

            // Draw connecting line from bar to chip
            dc.DrawLine(s_chipBorder, new Point(x, GradientBarHeight), new Point(x, ChipY - ChipRadius));

            // Draw chip circle
            var pen = i == _selectedStop ? s_chipSelectedBorder : s_chipBorder;
            dc.DrawEllipse(chipBrush, pen, new Point(x, ChipY), ChipRadius, ChipRadius);

            // Draw "x" on selected chip if more than 2 stops
            if (i == _selectedStop && _palette.Stops.Count > 2)
            {
                double xOff = ChipRadius * 0.4;
                var xPen = new Pen(Brushes.White, 1.2);
                xPen.Freeze();
                dc.DrawLine(xPen, new Point(x - xOff, ChipY - xOff), new Point(x + xOff, ChipY + xOff));
                dc.DrawLine(xPen, new Point(x + xOff, ChipY - xOff), new Point(x - xOff, ChipY + xOff));
            }
        }

        // 3. "+" button at the right
        double addX = ActualWidth - AddButtonSize - 2;
        dc.DrawEllipse(Brushes.Transparent, s_addPen, new Point(addX + AddButtonSize / 2, ChipY), AddButtonSize / 2, AddButtonSize / 2);
        // Plus sign
        double cx = addX + AddButtonSize / 2, cy = ChipY;
        dc.DrawLine(s_addPen, new Point(cx - 4, cy), new Point(cx + 4, cy));
        dc.DrawLine(s_addPen, new Point(cx, cy - 4), new Point(cx, cy + 4));

        // 4. Preset palette swatches row
        double px = 0;
        foreach (var preset in BuiltInPalettes.All)
        {
            if (px + PresetSwatchW > ActualWidth) break;

            var presetBrush = BuildPresetBrush(preset);
            var rect = new Rect(px, PresetRowY, PresetSwatchW, PresetSwatchH);

            // Highlight if this preset matches current palette
            bool isActive = string.Equals(_palette.Name, preset.Name, StringComparison.OrdinalIgnoreCase);
            var borderPen = isActive ? s_chipSelectedBorder : s_gradientBorder;

            dc.DrawRoundedRectangle(presetBrush, borderPen, rect, 3, 3);
            px += PresetSwatchW + PresetGap;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Check preset row click
        if (pos.Y >= PresetRowY && pos.Y <= PresetRowY + PresetSwatchH)
        {
            int idx = (int)(pos.X / (PresetSwatchW + PresetGap));
            if (idx >= 0 && idx < BuiltInPalettes.All.Length)
            {
                // Clone the preset so edits don't modify the built-in
                var preset = BuiltInPalettes.All[idx];
                _palette = new ColorPalette(preset.Name,
                    preset.Stops.Select(s => new ColorStop(s.Position, s.R, s.G, s.B)).ToArray());
                _selectedStop = -1;
                InvalidateVisual();
                PaletteChanged?.Invoke(_palette);
            }
            return;
        }

        // Check "+" button
        double addCx = ActualWidth - AddButtonSize - 2 + AddButtonSize / 2;
        if (Math.Abs(pos.X - addCx) < AddButtonSize && Math.Abs(pos.Y - ChipY) < AddButtonSize && _palette.Stops.Count < 8)
        {
            AddStopAtGap();
            return;
        }

        // Check chip click
        for (int i = 0; i < _palette.Stops.Count; i++)
        {
            double chipX = StopToX(_palette.Stops[i].Position);
            if (Math.Abs(pos.X - chipX) <= ChipRadius + 2 && Math.Abs(pos.Y - ChipY) <= ChipRadius + 2)
            {
                // If clicking selected stop's "x" area and >2 stops, remove it
                if (i == _selectedStop && _palette.Stops.Count > 2)
                {
                    _palette.Stops.RemoveAt(i);
                    _selectedStop = -1;
                    InvalidateVisual();
                    PaletteChanged?.Invoke(_palette);
                    return;
                }

                _selectedStop = i;
                _dragStop = i;
                _isDragging = true;
                _dragOffsetX = pos.X - chipX;
                CaptureMouse();
                InvalidateVisual();

                // Fire event for external color picker
                var stop = _palette.Stops[i];
                StopClicked?.Invoke(i, Color.FromRgb(stop.R, stop.G, stop.B));
                return;
            }
        }

        // Click on gradient bar = add stop at that position
        if (pos.Y >= 0 && pos.Y <= GradientBarHeight + 4 && _palette.Stops.Count < 8)
        {
            double t = XToStop(pos.X);
            var (r, g, b) = _palette.Sample((float)t);
            var newStop = new ColorStop(t, (byte)r, (byte)g, (byte)b);
            _palette.Stops.Add(newStop);
            _selectedStop = _palette.Stops.Count - 1;
            InvalidateVisual();
            PaletteChanged?.Invoke(_palette);
            return;
        }

        // Click elsewhere = deselect
        _selectedStop = -1;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging || _dragStop < 0) return;
        var pos = e.GetPosition(this);
        double newPos = XToStop(pos.X - _dragOffsetX);
        _palette.Stops[_dragStop].Position = newPos;
        InvalidateVisual();
        PaletteChanged?.Invoke(_palette);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _dragStop = -1;
            ReleaseMouseCapture();
        }
    }

    /// <summary>Add a new stop in the largest gap between existing stops.</summary>
    private void AddStopAtGap()
    {
        var sorted = SortedStops;
        double maxGap = 0;
        int gapIdx = 0;
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            double gap = sorted[i + 1].Position - sorted[i].Position;
            if (gap > maxGap) { maxGap = gap; gapIdx = i; }
        }
        double mid = (sorted[gapIdx].Position + sorted[gapIdx + 1].Position) / 2;
        var (r, g, b) = _palette.Sample((float)mid);
        _palette.Stops.Add(new ColorStop(mid, (byte)r, (byte)g, (byte)b));
        _selectedStop = _palette.Stops.Count - 1;
        InvalidateVisual();
        PaletteChanged?.Invoke(_palette);
    }

    private LinearGradientBrush BuildGradientBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var stop in SortedStops)
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(stop.R, stop.G, stop.B), stop.Position));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush BuildPresetBrush(ColorPalette palette)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var stop in palette.Stops.OrderBy(s => s.Position))
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(stop.R, stop.G, stop.B), stop.Position));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width,
            TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return new Size(finalSize.Width, TotalHeight);
    }
}
