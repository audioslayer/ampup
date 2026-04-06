using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AmpUp.Core.Models;

namespace AmpUp.Controls;

/// <summary>
/// Front-view monitor visualization with draggable content crop lines, live zone color preview,
/// and device edge indicators. Embedded in Room tab's Screen Sync settings.
/// </summary>
public class ScreenEdgeControl : Canvas
{
    // ── Layout constants ──────────────────────────────────────────────
    private const double ControlPadding = 20;
    private const double DefaultHeight = 200;
    private const double AspectRatio = 16.0 / 9.0;
    private const double HandleWidth = 6;
    private const double HandleLength = 20;
    private const double CropLineThickness = 1.5;
    private const double MinCropPct = 0.0;
    private const double MaxCropPct = 0.5;
    private const double ZoneCellCornerRadius = 2;
    private const double ZoneCellPadding = 1;

    // ── Static frozen brushes/pens ────────────────────────────────────
    private static readonly SolidColorBrush s_bgFill = new(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly SolidColorBrush s_blackBarFill = new(Color.FromRgb(0x0A, 0x0A, 0x0A));
    private static readonly SolidColorBrush s_accentBrush = new(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly SolidColorBrush s_accentDimBrush = new(Color.FromRgb(0x00, 0xA8, 0x54));
    private static readonly SolidColorBrush s_dimBrush = new(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush s_zoneFallback = new(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly SolidColorBrush s_transparent = Brushes.Transparent;
    private static readonly Pen s_outerBorderPen = new(s_borderBrush, 1.5);
    private static readonly Pen s_contentBorderPenActive = new(s_accentBrush, 1.5);
    private static readonly Pen s_contentBorderPenDim = new(s_dimBrush, 1.0);

    static ScreenEdgeControl()
    {
        s_bgFill.Freeze(); s_borderBrush.Freeze(); s_blackBarFill.Freeze();
        s_accentBrush.Freeze(); s_accentDimBrush.Freeze(); s_dimBrush.Freeze();
        s_zoneFallback.Freeze(); s_outerBorderPen.Freeze();
        s_contentBorderPenActive.Freeze(); s_contentBorderPenDim.Freeze();
    }

    // ── State ─────────────────────────────────────────────────────────
    private ContentBounds _bounds = new();
    private bool _isAutoDetected = true;

    // Calculated screen rect (in canvas coordinates)
    private double _screenX, _screenY, _screenW, _screenH;

    // Visual elements
    private readonly Rectangle _outerRect = new();
    private readonly Rectangle _blackBarLeft = new();
    private readonly Rectangle _blackBarRight = new();
    private readonly Rectangle _blackBarTop = new();
    private readonly Rectangle _blackBarBottom = new();
    private readonly Rectangle _contentRect = new();
    private readonly List<Rectangle> _zoneCells = new();
    private int _zoneCols, _zoneRows;

    // Crop lines: 0=Left, 1=Right, 2=Top, 3=Bottom
    private readonly Line[] _cropLines = new Line[4];
    private readonly Rectangle[] _cropHandles = new Rectangle[4];
    private int _draggingIndex = -1;

    // ── Events ────────────────────────────────────────────────────────
    public event Action<ContentBounds>? ContentBoundsChanged;

    // ── Constructor ───────────────────────────────────────────────────
    public ScreenEdgeControl()
    {
        Background = s_transparent;
        ClipToBounds = true;
        Height = DefaultHeight;

        BuildVisuals();
        SizeChanged += (_, _) => Rebuild();
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>Update crop bounds from config.</summary>
    public void SetContentBounds(ContentBounds bounds)
    {
        _bounds = bounds;
        _isAutoDetected = bounds.AutoDetect;
        Rebuild();
    }

    /// <summary>Update live zone colors from DreamSyncController.</summary>
    public void UpdateZoneColors((byte R, byte G, byte B)[,] grid, int cols, int rows)
    {
        if (cols != _zoneCols || rows != _zoneRows)
        {
            _zoneCols = cols;
            _zoneRows = rows;
            RebuildZoneCells();
        }

        int idx = 0;
        for (int r = 0; r < rows && r < grid.GetLength(0); r++)
        {
            for (int c = 0; c < cols && c < grid.GetLength(1); c++)
            {
                if (idx < _zoneCells.Count)
                {
                    var color = grid[r, c];
                    var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                    brush.Freeze();
                    _zoneCells[idx].Fill = brush;
                }
                idx++;
            }
        }
    }

    /// <summary>Toggle auto-detected appearance (dim lines when auto).</summary>
    public void SetAutoDetected(bool isAuto)
    {
        _isAutoDetected = isAuto;
        UpdateCropLineAppearance();
        UpdateContentBorder();
    }

    // ── Visual construction ───────────────────────────────────────────

    private void BuildVisuals()
    {
        // Outer screen rectangle
        _outerRect.Fill = s_bgFill;
        _outerRect.Stroke = s_borderBrush;
        _outerRect.StrokeThickness = 1.5;
        _outerRect.RadiusX = 4;
        _outerRect.RadiusY = 4;
        Children.Add(_outerRect);

        // Black bar regions (letterbox/pillarbox)
        foreach (var bar in new[] { _blackBarLeft, _blackBarRight, _blackBarTop, _blackBarBottom })
        {
            bar.Fill = s_blackBarFill;
            bar.IsHitTestVisible = false;
            Children.Add(bar);
        }

        // Content area border
        _contentRect.Fill = s_transparent;
        _contentRect.Stroke = s_accentBrush;
        _contentRect.StrokeThickness = 1.5;
        _contentRect.IsHitTestVisible = false;
        Children.Add(_contentRect);

        // Crop lines and handles
        for (int i = 0; i < 4; i++)
        {
            _cropLines[i] = new Line
            {
                StrokeThickness = CropLineThickness,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Children.Add(_cropLines[i]);

            _cropHandles[i] = new Rectangle
            {
                Fill = s_accentBrush,
                RadiusX = 2,
                RadiusY = 2,
                Cursor = i < 2 ? Cursors.SizeWE : Cursors.SizeNS
            };
            int capturedIndex = i;
            _cropHandles[i].MouseLeftButtonDown += (_, e) => OnHandleMouseDown(capturedIndex, e);
            Children.Add(_cropHandles[i]);
        }

        // Global mouse handlers for drag
        MouseMove += OnCanvasMouseMove;
        MouseLeftButtonUp += OnCanvasMouseUp;
    }

    // ── Layout rebuild ────────────────────────────────────────────────

    private void Rebuild()
    {
        double canvasW = ActualWidth;
        double canvasH = ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return;

        // Calculate screen rect within padding, maintaining 16:9
        double availW = canvasW - ControlPadding * 2;
        double availH = canvasH - ControlPadding * 2;
        if (availW <= 0 || availH <= 0) return;

        double fitW = availW;
        double fitH = fitW / AspectRatio;
        if (fitH > availH)
        {
            fitH = availH;
            fitW = fitH * AspectRatio;
        }

        _screenX = ControlPadding + (availW - fitW) / 2;
        _screenY = ControlPadding + (availH - fitH) / 2;
        _screenW = fitW;
        _screenH = fitH;

        // Position outer rectangle
        SetLeft(_outerRect, _screenX);
        SetTop(_outerRect, _screenY);
        _outerRect.Width = _screenW;
        _outerRect.Height = _screenH;

        // Calculate content area
        double cLeft = _bounds.LeftPct * _screenW;
        double cRight = _bounds.RightPct * _screenW;
        double cTop = _bounds.TopPct * _screenH;
        double cBottom = _bounds.BottomPct * _screenH;

        double contentX = _screenX + cLeft;
        double contentY = _screenY + cTop;
        double contentW = _screenW - cLeft - cRight;
        double contentH = _screenH - cTop - cBottom;

        if (contentW < 2) contentW = 2;
        if (contentH < 2) contentH = 2;

        // Position content rectangle
        SetLeft(_contentRect, contentX);
        SetTop(_contentRect, contentY);
        _contentRect.Width = contentW;
        _contentRect.Height = contentH;

        // Position black bars
        PositionBlackBar(_blackBarLeft, _screenX, _screenY, cLeft, _screenH);
        PositionBlackBar(_blackBarRight, _screenX + _screenW - cRight, _screenY, cRight, _screenH);
        PositionBlackBar(_blackBarTop, _screenX + cLeft, _screenY, contentW, cTop);
        PositionBlackBar(_blackBarBottom, _screenX + cLeft, _screenY + _screenH - cBottom, contentW, cBottom);

        // Position crop lines and handles
        PositionCropLine(0, contentX, _screenY, contentX, _screenY + _screenH, true);           // Left
        PositionCropLine(1, contentX + contentW, _screenY, contentX + contentW, _screenY + _screenH, true); // Right
        PositionCropLine(2, _screenX, contentY, _screenX + _screenW, contentY, false);           // Top
        PositionCropLine(3, _screenX, contentY + contentH, _screenX + _screenW, contentY + contentH, false); // Bottom

        UpdateCropLineAppearance();
        UpdateContentBorder();
        RebuildZoneCells();
    }

    private static void PositionBlackBar(Rectangle bar, double x, double y, double w, double h)
    {
        if (w < 0.5 || h < 0.5)
        {
            bar.Visibility = Visibility.Collapsed;
            return;
        }
        bar.Visibility = Visibility.Visible;
        SetLeft(bar, x);
        SetTop(bar, y);
        bar.Width = w;
        bar.Height = h;
    }

    private void PositionCropLine(int idx, double x1, double y1, double x2, double y2, bool isVertical)
    {
        var line = _cropLines[idx];
        line.X1 = x1; line.Y1 = y1;
        line.X2 = x2; line.Y2 = y2;

        var handle = _cropHandles[idx];
        if (isVertical)
        {
            handle.Width = HandleWidth;
            handle.Height = HandleLength;
            SetLeft(handle, x1 - HandleWidth / 2);
            SetTop(handle, (y1 + y2) / 2 - HandleLength / 2);
        }
        else
        {
            handle.Width = HandleLength;
            handle.Height = HandleWidth;
            SetLeft(handle, (x1 + x2) / 2 - HandleLength / 2);
            SetTop(handle, y1 - HandleWidth / 2);
        }
    }

    private void UpdateCropLineAppearance()
    {
        var lineBrush = _isAutoDetected ? s_dimBrush : s_accentBrush;
        var handleBrush = _isAutoDetected ? s_dimBrush : s_accentBrush;

        for (int i = 0; i < 4; i++)
        {
            _cropLines[i].Stroke = lineBrush;
            _cropHandles[i].Fill = handleBrush;
        }
    }

    private void UpdateContentBorder()
    {
        _contentRect.Stroke = _isAutoDetected ? s_dimBrush : s_accentBrush;
    }

    // ── Zone cells ────────────────────────────────────────────────────

    private void RebuildZoneCells()
    {
        // Remove old zone cells
        foreach (var cell in _zoneCells)
            Children.Remove(cell);
        _zoneCells.Clear();

        if (_zoneCols <= 0 || _zoneRows <= 0) return;
        if (_screenW <= 0 || _screenH <= 0) return;

        double cLeft = _bounds.LeftPct * _screenW;
        double cRight = _bounds.RightPct * _screenW;
        double cTop = _bounds.TopPct * _screenH;
        double cBottom = _bounds.BottomPct * _screenH;

        double contentX = _screenX + cLeft;
        double contentY = _screenY + cTop;
        double contentW = _screenW - cLeft - cRight;
        double contentH = _screenH - cTop - cBottom;

        if (contentW < 2 || contentH < 2) return;

        double cellW = contentW / _zoneCols;
        double cellH = contentH / _zoneRows;

        for (int r = 0; r < _zoneRows; r++)
        {
            for (int c = 0; c < _zoneCols; c++)
            {
                var cell = new Rectangle
                {
                    Width = Math.Max(1, cellW - ZoneCellPadding * 2),
                    Height = Math.Max(1, cellH - ZoneCellPadding * 2),
                    RadiusX = ZoneCellCornerRadius,
                    RadiusY = ZoneCellCornerRadius,
                    Fill = s_zoneFallback,
                    IsHitTestVisible = false,
                    Opacity = 0.7
                };

                SetLeft(cell, contentX + c * cellW + ZoneCellPadding);
                SetTop(cell, contentY + r * cellH + ZoneCellPadding);

                // Insert before crop lines (so lines draw on top)
                int insertIdx = Children.IndexOf(_cropLines[0]);
                if (insertIdx >= 0)
                    Children.Insert(insertIdx, cell);
                else
                    Children.Add(cell);

                _zoneCells.Add(cell);
            }
        }
    }

    // ── Drag handling ─────────────────────────────────────────────────

    private void OnHandleMouseDown(int index, MouseButtonEventArgs e)
    {
        _draggingIndex = index;
        _cropHandles[index].CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0) return;

        var pos = e.GetPosition(this);

        switch (_draggingIndex)
        {
            case 0: // Left
            {
                double pct = (pos.X - _screenX) / _screenW;
                _bounds.LeftPct = Clamp(pct, MinCropPct, MaxCropPct);
                break;
            }
            case 1: // Right
            {
                double pct = (_screenX + _screenW - pos.X) / _screenW;
                _bounds.RightPct = Clamp(pct, MinCropPct, MaxCropPct);
                break;
            }
            case 2: // Top
            {
                double pct = (pos.Y - _screenY) / _screenH;
                _bounds.TopPct = Clamp(pct, MinCropPct, MaxCropPct);
                break;
            }
            case 3: // Bottom
            {
                double pct = (_screenY + _screenH - pos.Y) / _screenH;
                _bounds.BottomPct = Clamp(pct, MinCropPct, MaxCropPct);
                break;
            }
        }

        // User dragged manually — disable auto-detect
        _bounds.AutoDetect = false;
        _isAutoDetected = false;

        Rebuild();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingIndex < 0) return;

        _cropHandles[_draggingIndex].ReleaseMouseCapture();
        _draggingIndex = -1;

        // Fire event with a copy of bounds
        ContentBoundsChanged?.Invoke(new ContentBounds
        {
            LeftPct = _bounds.LeftPct,
            RightPct = _bounds.RightPct,
            TopPct = _bounds.TopPct,
            BottomPct = _bounds.BottomPct,
            AutoDetect = _bounds.AutoDetect
        });
    }

    // ── Utility ───────────────────────────────────────────────────────

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
