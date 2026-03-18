using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

/// <summary>
/// Radial pie-segment overlay for quick profile/device switching via hardware wheel.
/// </summary>
public partial class RadialWheelOverlay : Window
{
    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Fires when user clicks a segment (index) or -1 on Escape.</summary>
    public Action<int>? OnSegmentClicked;

    private const int TotalSlots = 8;
    private List<string> _items = new();
    private readonly string[] _slotLabels = new string[TotalSlots];
    private readonly Color[] _slotColors = new Color[TotalSlots];
    private int _highlighted = -1;
    private bool _dismissing;

    // Geometry constants
    private const double CenterX = 210;
    private const double CenterY = 210;
    private const double OuterR = 200;
    private const double InnerR = 62;

    // Colors
    private static readonly Color AccentColor = Color.Parse("#00E676");
    private static readonly Color SegmentEmpty = Color.FromArgb(0x88, 0x12, 0x12, 0x12);

    private readonly List<Path> _segPaths = new();
    private readonly List<TextBlock> _segLabels = new();

    public RadialWheelOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            FadeIn();
        };
        KeyDown += Window_KeyDown;
    }

    /// <summary>
    /// Populate the wheel with profiles and show it.
    /// </summary>
    public void SetProfiles(List<string> profiles, int currentIndex,
                            Dictionary<string, ProfileIconConfig>? icons = null)
    {
        _items = profiles;
        for (int i = 0; i < TotalSlots; i++)
        {
            _slotLabels[i] = i < profiles.Count ? profiles[i] : "";
            if (i < profiles.Count && icons != null
                && icons.TryGetValue(profiles[i], out var cfg))
            {
                try { _slotColors[i] = Color.Parse(cfg.Color); }
                catch { _slotColors[i] = AccentColor; }
            }
            else
            {
                _slotColors[i] = AccentColor;
            }
        }
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
    }

    /// <summary>
    /// Populate the wheel with audio output devices.
    /// </summary>
    public void SetDevices(List<(string id, string name)> devices, int currentIndex)
    {
        _items = devices.Select(d => d.id).ToList();
        for (int i = 0; i < TotalSlots; i++)
        {
            _slotLabels[i] = i < devices.Count ? devices[i].name : "";
            _slotColors[i] = i < devices.Count
                ? Color.Parse("#AB47BC")
                : AccentColor;
        }
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
    }

    public int GetTotalSlots() => TotalSlots;

    public string? GetSelectedId()
    {
        if (_highlighted >= 0 && _highlighted < _items.Count)
            return _items[_highlighted];
        return null;
    }

    /// <summary>
    /// Move the highlight to the given segment index.
    /// </summary>
    public void Highlight(int index)
    {
        index = ((index % TotalSlots) + TotalSlots) % TotalSlots;
        if (index == _highlighted) return;
        _highlighted = index;
        RefreshSegmentColors();
        CenterLabel.Text = !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted] : "";
    }

    public int GetSelectedIndex() => _highlighted;

    /// <summary>
    /// Fade out and close without selecting.
    /// </summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        FadeOut(() => Close());
    }

    // ── Geometry helpers ─────────────────────────────────────────────

    private void BuildSegments()
    {
        SegmentCanvas.Children.Clear();
        _segPaths.Clear();
        _segLabels.Clear();

        double sweep = 360.0 / TotalSlots;

        for (int i = 0; i < TotalSlots; i++)
        {
            bool isEmpty = string.IsNullOrEmpty(_slotLabels[i]);
            double startAngle = -90.0 + i * sweep;
            var path = BuildSegmentPath(startAngle, sweep, i == _highlighted, isEmpty, _slotColors[i]);
            int cap = i;
            if (!isEmpty)
            {
                path.PointerEntered += (_, _) => OnSegHover(cap);
                path.PointerPressed += (_, _) => ConfirmAndDismiss(cap);
                path.Cursor = new Cursor(StandardCursorType.Hand);
            }
            SegmentCanvas.Children.Add(path);
            _segPaths.Add(path);

            // Label
            double midAngle = startAngle + sweep / 2.0;
            double labelR = (OuterR + InnerR) / 2.0;
            double lx = CenterX + labelR * Math.Cos(midAngle * Math.PI / 180.0);
            double ly = CenterY + labelR * Math.Sin(midAngle * Math.PI / 180.0);

            var tb = new TextBlock
            {
                Text = isEmpty ? "" : _slotLabels[i],
                FontSize = 10,
                FontWeight = isEmpty ? FontWeight.Normal : FontWeight.SemiBold,
                Foreground = new SolidColorBrush(isEmpty
                    ? Color.Parse("#333333")
                    : Colors.White),
                TextAlignment = TextAlignment.Center,
                MaxWidth = 75,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };

            // Measure for centering
            tb.Measure(Size.Infinity);
            Canvas.SetLeft(tb, lx - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, ly - tb.DesiredSize.Height / 2);
            SegmentCanvas.Children.Add(tb);
            _segLabels.Add(tb);
        }

        CenterLabel.Text = _highlighted >= 0 && _highlighted < TotalSlots
            && !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted]
            : "";
    }

    private static Path BuildSegmentPath(double startAngleDeg, double sweepDeg, bool highlighted,
                                         bool isEmpty, Color slotColor)
    {
        var geo = BuildPieSlice(startAngleDeg, sweepDeg);
        Color fill, stroke;
        double strokeW;

        if (isEmpty)
        {
            fill = highlighted ? Color.FromArgb(0xAA, 0x18, 0x18, 0x18) : SegmentEmpty;
            stroke = highlighted
                ? Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B)
                : Color.Parse("#222222");
            strokeW = 1;
        }
        else if (highlighted)
        {
            fill = Color.FromArgb(0x35, slotColor.R, slotColor.G, slotColor.B);
            stroke = Color.FromArgb(0xCC, slotColor.R, slotColor.G, slotColor.B);
            strokeW = 2;
        }
        else
        {
            fill = Color.FromArgb(0x18, slotColor.R, slotColor.G, slotColor.B);
            stroke = Color.FromArgb(0x40, slotColor.R, slotColor.G, slotColor.B);
            strokeW = 1;
        }

        return new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = strokeW,
        };
    }

    private static PathGeometry BuildPieSlice(double startAngleDeg, double sweepDeg)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg - 0.5) * Math.PI / 180.0;

        var outerStart = new Point(CenterX + OuterR * Math.Cos(startRad), CenterY + OuterR * Math.Sin(startRad));
        var outerEnd = new Point(CenterX + OuterR * Math.Cos(endRad), CenterY + OuterR * Math.Sin(endRad));
        var innerStart = new Point(CenterX + InnerR * Math.Cos(startRad), CenterY + InnerR * Math.Sin(startRad));
        var innerEnd = new Point(CenterX + InnerR * Math.Cos(endRad), CenterY + InnerR * Math.Sin(endRad));

        bool isLargeArc = sweepDeg > 180;

        var fig = new PathFigure { StartPoint = innerStart, IsClosed = true };
        fig.Segments!.Add(new LineSegment { Point = outerStart });
        fig.Segments.Add(new ArcSegment
        {
            Point = outerEnd,
            Size = new Size(OuterR, OuterR),
            RotationAngle = 0,
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });
        fig.Segments.Add(new LineSegment { Point = innerEnd });
        fig.Segments.Add(new ArcSegment
        {
            Point = innerStart,
            Size = new Size(InnerR, InnerR),
            RotationAngle = 0,
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.CounterClockwise
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    private void RefreshSegmentColors()
    {
        for (int i = 0; i < _segPaths.Count; i++)
        {
            bool hl = i == _highlighted;
            bool empty = string.IsNullOrEmpty(_slotLabels[i]);
            var sc = _slotColors[i];

            if (empty)
            {
                _segPaths[i].Fill = new SolidColorBrush(hl
                    ? Color.FromArgb(0xAA, 0x18, 0x18, 0x18) : SegmentEmpty);
                _segPaths[i].Stroke = new SolidColorBrush(hl
                    ? Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B)
                    : Color.Parse("#222222"));
                _segPaths[i].StrokeThickness = 1;
            }
            else if (hl)
            {
                _segPaths[i].Fill = new SolidColorBrush(Color.FromArgb(0x35, sc.R, sc.G, sc.B));
                _segPaths[i].Stroke = new SolidColorBrush(Color.FromArgb(0xCC, sc.R, sc.G, sc.B));
                _segPaths[i].StrokeThickness = 2;
            }
            else
            {
                _segPaths[i].Fill = new SolidColorBrush(Color.FromArgb(0x18, sc.R, sc.G, sc.B));
                _segPaths[i].Stroke = new SolidColorBrush(Color.FromArgb(0x40, sc.R, sc.G, sc.B));
                _segPaths[i].StrokeThickness = 1;
            }
        }
        CenterLabel.Text = _highlighted >= 0 && _highlighted < TotalSlots
            && !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted]
            : "";
    }

    private void OnSegHover(int idx)
    {
        if (idx == _highlighted) return;
        _highlighted = idx;
        RefreshSegmentColors();
    }

    private void ConfirmAndDismiss(int idx)
    {
        if (_dismissing) return;
        if (idx >= 0 && idx < TotalSlots && string.IsNullOrEmpty(_slotLabels[idx])) return;
        _dismissing = true;
        FadeOut(() =>
        {
            Close();
            OnSegmentClicked?.Invoke(idx);
        });
    }

    // ── Animation helpers (code-behind opacity transitions) ──────────

    private void FadeIn()
    {
        RootGrid.Opacity = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            RootGrid.Opacity = Math.Min(1.0, RootGrid.Opacity + 0.08);
            if (RootGrid.Opacity >= 1.0) timer.Stop();
        };
        timer.Start();
    }

    private void FadeOut(Action onComplete)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            RootGrid.Opacity = Math.Max(0.0, RootGrid.Opacity - 0.08);
            if (RootGrid.Opacity <= 0.0)
            {
                timer.Stop();
                onComplete();
            }
        };
        timer.Start();
    }

    // ── Input handlers ───────────────────────────────────────────────

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_dismissing) return;
            _dismissing = true;
            FadeOut(() =>
            {
                Close();
                OnSegmentClicked?.Invoke(-1);
            });
        }
        else if (e.Key == Key.Return || e.Key == Key.Space)
        {
            ConfirmAndDismiss(_highlighted);
        }
        else if (e.Key == Key.Left || e.Key == Key.Up)
        {
            Highlight((_highlighted - 1 + _items.Count) % _items.Count);
        }
        else if (e.Key == Key.Right || e.Key == Key.Down)
        {
            Highlight((_highlighted + 1) % _items.Count);
        }
    }
}
