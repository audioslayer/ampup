using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AmpUp.Core.Models;

namespace AmpUp.Controls;

/// <summary>
/// Top-down 2D room canvas for placing Govee/Corsair devices spatially.
/// Draws room outline with grid, device icons at X/Y positions, and live color preview.
/// Supports drag-to-reposition and click-to-select.
/// </summary>
public class RoomCanvasControl : Canvas
{
    private RoomLayout _layout = new();
    private readonly List<DeviceVisual> _deviceVisuals = new();
    private DeviceVisual? _selectedDevice;
    private DeviceVisual? _draggingDevice;
    private Point _dragOffset;
    private double _scale = 1.0; // pixels per foot

    // Live colors from effect engine (deviceId → RGB array)
    private readonly Dictionary<string, (byte R, byte G, byte B)[]> _liveColors = new();

    // Events
    public event Action<RoomDevicePlacement>? DeviceSelected;
    public event Action<RoomDevicePlacement>? DeviceMoved;
    public event Action? LayoutChanged;

    // Theme colors
    private static readonly SolidColorBrush BgBrush = new(Color.FromRgb(0x0F, 0x0F, 0x0F));
    private static readonly SolidColorBrush WallBrush = new(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private static readonly SolidColorBrush GridBrush = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush GridBrushMajor = new(Color.FromRgb(0x22, 0x22, 0x22));
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly SolidColorBrush DeviceBrush = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly SolidColorBrush LabelBrush = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Pen WallPen = new(WallBrush, 2);
    private static readonly Pen GridPen = new(GridBrush, 0.5);
    private static readonly Pen GridPenMajor = new(GridBrushMajor, 0.8);
    private static readonly Pen SelectedPen = new(SelectedBrush, 2);

    private const double Padding = 40; // canvas padding in pixels
    private const double DeviceMinSize = 8;

    static RoomCanvasControl()
    {
        WallPen.Freeze(); GridPen.Freeze(); GridPenMajor.Freeze(); SelectedPen.Freeze();
        BgBrush.Freeze(); WallBrush.Freeze(); GridBrush.Freeze(); GridBrushMajor.Freeze();
        TextBrush.Freeze(); DeviceBrush.Freeze(); SelectedBrush.Freeze(); LabelBrush.Freeze();
    }

    public RoomCanvasControl()
    {
        Background = BgBrush;
        ClipToBounds = true;
        Focusable = true;
    }

    public void SetLayout(RoomLayout layout)
    {
        _layout = layout;
        Rebuild();
    }

    public RoomDevicePlacement? SelectedPlacement => _selectedDevice?.Placement;

    /// <summary>
    /// Update live preview colors for a device.
    /// </summary>
    public void SetDeviceColors(string deviceId, (byte R, byte G, byte B)[] colors)
    {
        _liveColors[deviceId] = colors;
        // Update the visual without full rebuild
        var visual = _deviceVisuals.FirstOrDefault(v => v.Placement.DeviceId == deviceId);
        if (visual != null)
            UpdateDeviceVisualColors(visual);
    }

    /// <summary>
    /// Full rebuild of the canvas from the layout.
    /// </summary>
    public void Rebuild()
    {
        Children.Clear();
        _deviceVisuals.Clear();

        if (ActualWidth < 10 || ActualHeight < 10) return;

        double availW = ActualWidth - Padding * 2;
        double availH = ActualHeight - Padding * 2;
        if (availW <= 0 || availH <= 0) return;

        double scaleX = availW / Math.Max(_layout.WidthFt, 1);
        double scaleY = availH / Math.Max(_layout.DepthFt, 1);
        _scale = Math.Min(scaleX, scaleY);

        double roomPxW = _layout.WidthFt * _scale;
        double roomPxH = _layout.DepthFt * _scale;
        double offsetX = Padding + (availW - roomPxW) / 2;
        double offsetY = Padding + (availH - roomPxH) / 2;

        // ── Grid lines ──
        for (double ft = 0; ft <= _layout.WidthFt; ft += 1)
        {
            double x = offsetX + ft * _scale;
            bool major = ft % 5 == 0;
            var line = new Line
            {
                X1 = x, Y1 = offsetY, X2 = x, Y2 = offsetY + roomPxH,
                Stroke = major ? GridBrushMajor : GridBrush,
                StrokeThickness = major ? 0.8 : 0.5,
            };
            Children.Add(line);
        }
        for (double ft = 0; ft <= _layout.DepthFt; ft += 1)
        {
            double y = offsetY + ft * _scale;
            bool major = ft % 5 == 0;
            var line = new Line
            {
                X1 = offsetX, Y1 = y, X2 = offsetX + roomPxW, Y2 = y,
                Stroke = major ? GridBrushMajor : GridBrush,
                StrokeThickness = major ? 0.8 : 0.5,
            };
            Children.Add(line);
        }

        // ── Room outline ──
        var roomRect = new Rectangle
        {
            Width = roomPxW, Height = roomPxH,
            Stroke = WallBrush, StrokeThickness = 2,
            Fill = Brushes.Transparent,
        };
        SetLeft(roomRect, offsetX);
        SetTop(roomRect, offsetY);
        Children.Add(roomRect);

        // ── Dimension labels ──
        AddDimensionLabel($"{_layout.WidthFt:0.#} ft", offsetX + roomPxW / 2, offsetY - 16, true);
        AddDimensionLabel($"{_layout.DepthFt:0.#} ft", offsetX - 20, offsetY + roomPxH / 2, false);

        // ── Compass label ──
        AddDimensionLabel("FRONT", offsetX + roomPxW / 2, offsetY + roomPxH + 14, true);

        // ── Devices ──
        foreach (var dev in _layout.Devices)
        {
            var visual = CreateDeviceVisual(dev, offsetX, offsetY);
            _deviceVisuals.Add(visual);
        }
    }

    private void AddDimensionLabel(string text, double x, double y, bool horizontal)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = TextBrush,
        };
        if (!horizontal)
        {
            tb.RenderTransform = new RotateTransform(-90);
            tb.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        SetLeft(tb, x - (horizontal ? 20 : 5));
        SetTop(tb, y - (horizontal ? 5 : 20));
        Children.Add(tb);
    }

    private DeviceVisual CreateDeviceVisual(RoomDevicePlacement dev, double offsetX, double offsetY)
    {
        double px = offsetX + dev.X * _scale;
        double py = offsetY + dev.Y * _scale;

        bool isSegment = dev.SegmentCount > 1;
        double devW, devH;

        if (isSegment)
        {
            devW = Math.Max(dev.LengthFt * _scale, DeviceMinSize * 3);
            devH = Math.Max(_scale * 0.4, DeviceMinSize);
        }
        else
        {
            // Bulb: circle
            devW = Math.Max(_scale * 0.6, DeviceMinSize * 2);
            devH = devW;
        }

        // Container for the device
        var container = new Canvas { Width = devW, Height = devH };
        if (dev.Rotation != 0)
        {
            container.RenderTransform = new RotateTransform(dev.Rotation, devW / 2, devH / 2);
        }

        // Background shape
        if (isSegment)
        {
            // Segment bar: rounded rectangle with segment divisions
            var bg = new Rectangle
            {
                Width = devW, Height = devH,
                RadiusX = 4, RadiusY = 4,
                Fill = DeviceBrush,
                Stroke = DeviceBrush,
                StrokeThickness = 1,
            };
            container.Children.Add(bg);

            // Segment color cells
            double segW = devW / dev.SegmentCount;
            for (int s = 0; s < dev.SegmentCount; s++)
            {
                var segRect = new Rectangle
                {
                    Width = Math.Max(segW - 1, 2), Height = Math.Max(devH - 2, 2),
                    RadiusX = 2, RadiusY = 2,
                    Fill = DeviceBrush,
                    Tag = s, // segment index
                };
                SetLeft(segRect, s * segW + 0.5);
                SetTop(segRect, 1);
                container.Children.Add(segRect);
            }
        }
        else
        {
            // Bulb: circle
            var circle = new Ellipse
            {
                Width = devW, Height = devH,
                Fill = DeviceBrush,
                Stroke = DeviceBrush,
                StrokeThickness = 1,
            };
            container.Children.Add(circle);
        }

        SetLeft(container, px - devW / 2);
        SetTop(container, py - devH / 2);
        container.Cursor = Cursors.Hand;
        Children.Add(container);

        // Name label below device
        var label = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(dev.Name) ? dev.Name : dev.DeviceId,
            FontSize = 9,
            Foreground = LabelBrush,
            TextAlignment = TextAlignment.Center,
            MaxWidth = devW + 40,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(label, px - label.DesiredSize.Width / 2);
        SetTop(label, py + devH / 2 + 4);
        Children.Add(label);

        // Height badge
        var heightBadge = new TextBlock
        {
            Text = $"{dev.Z:0.#}ft",
            FontSize = 8,
            Foreground = TextBrush,
            TextAlignment = TextAlignment.Center,
        };
        heightBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(heightBadge, px - heightBadge.DesiredSize.Width / 2);
        SetTop(heightBadge, py - devH / 2 - 12);
        Children.Add(heightBadge);

        var visual = new DeviceVisual
        {
            Placement = dev,
            Container = container,
            Label = label,
            HeightBadge = heightBadge,
            OffsetX = offsetX,
            OffsetY = offsetY,
        };

        // Mouse handlers
        container.MouseLeftButtonDown += (_, e) =>
        {
            SelectDevice(visual);
            _draggingDevice = visual;
            var pos = e.GetPosition(this);
            double cx = GetLeft(container) + container.Width / 2;
            double cy = GetTop(container) + container.Height / 2;
            _dragOffset = new Point(pos.X - cx, pos.Y - cy);
            container.CaptureMouse();
            e.Handled = true;
        };

        container.MouseMove += (_, e) =>
        {
            if (_draggingDevice != visual || !container.IsMouseCaptured) return;
            var pos = e.GetPosition(this);
            double newCx = pos.X - _dragOffset.X;
            double newCy = pos.Y - _dragOffset.Y;

            // Convert pixel back to feet
            double newX = (newCx - visual.OffsetX) / _scale;
            double newY = (newCy - visual.OffsetY) / _scale;

            // Clamp to room bounds
            newX = Math.Clamp(newX, 0, _layout.WidthFt);
            newY = Math.Clamp(newY, 0, _layout.DepthFt);

            dev.X = Math.Round(newX * 4) / 4; // snap to 0.25 ft
            dev.Y = Math.Round(newY * 4) / 4;

            // Update visual position
            double px2 = visual.OffsetX + dev.X * _scale;
            double py2 = visual.OffsetY + dev.Y * _scale;
            SetLeft(container, px2 - container.Width / 2);
            SetTop(container, py2 - container.Height / 2);

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(label, px2 - label.DesiredSize.Width / 2);
            SetTop(label, py2 + container.Height / 2 + 4);

            SetLeft(heightBadge, px2 - heightBadge.DesiredSize.Width / 2);
            SetTop(heightBadge, py2 - container.Height / 2 - 12);

            e.Handled = true;
        };

        container.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingDevice == visual)
            {
                _draggingDevice = null;
                container.ReleaseMouseCapture();
                DeviceMoved?.Invoke(dev);
                LayoutChanged?.Invoke();
                e.Handled = true;
            }
        };

        // Apply live colors if available
        if (_liveColors.TryGetValue(dev.DeviceId, out var colors))
        {
            ApplyColorsToVisual(visual, colors);
        }

        return visual;
    }

    private void SelectDevice(DeviceVisual? visual)
    {
        // Deselect previous
        if (_selectedDevice != null)
        {
            foreach (var child in _selectedDevice.Container.Children.OfType<Shape>())
            {
                if (child.Tag is int) continue; // segment cells
                child.Stroke = DeviceBrush;
                child.StrokeThickness = 1;
            }
        }

        _selectedDevice = visual;

        // Highlight selected
        if (visual != null)
        {
            foreach (var child in visual.Container.Children.OfType<Shape>())
            {
                if (child.Tag is int) continue;
                child.Stroke = SelectedBrush;
                child.StrokeThickness = 2;
            }
            DeviceSelected?.Invoke(visual.Placement);
        }
    }

    private void UpdateDeviceVisualColors(DeviceVisual visual)
    {
        if (!_liveColors.TryGetValue(visual.Placement.DeviceId, out var colors)) return;
        ApplyColorsToVisual(visual, colors);
    }

    private void ApplyColorsToVisual(DeviceVisual visual, (byte R, byte G, byte B)[] colors)
    {
        if (visual.Placement.SegmentCount > 1)
        {
            // Update segment cell fills
            int i = 0;
            foreach (var child in visual.Container.Children.OfType<Rectangle>())
            {
                if (child.Tag is int segIdx && segIdx < colors.Length)
                {
                    var c = colors[segIdx];
                    child.Fill = new SolidColorBrush(Color.FromRgb(
                        Math.Max(c.R, (byte)20), Math.Max(c.G, (byte)20), Math.Max(c.B, (byte)20)));
                    i++;
                }
            }
        }
        else if (colors.Length > 0)
        {
            // Update circle fill
            var c = colors[0];
            foreach (var child in visual.Container.Children.OfType<Ellipse>())
            {
                child.Fill = new SolidColorBrush(Color.FromRgb(
                    Math.Max(c.R, (byte)20), Math.Max(c.G, (byte)20), Math.Max(c.B, (byte)20)));
            }
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Click on empty space deselects
        if (_draggingDevice == null)
            SelectDevice(null);
    }

    private class DeviceVisual
    {
        public RoomDevicePlacement Placement { get; set; } = null!;
        public Canvas Container { get; set; } = null!;
        public TextBlock Label { get; set; } = null!;
        public TextBlock HeightBadge { get; set; } = null!;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }
}
