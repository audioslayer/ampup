using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AmpUp.Controls
{
    public class CurvePickerControl : Border
    {
        // ── Events ───────────────────────────────────────────────────────
        public event EventHandler? SelectionChanged;

        // ── Public properties ────────────────────────────────────────────
        private Color _accentColor = Color.FromRgb(0x00, 0xE6, 0x76);
        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                _accentColor = value;
                UpdateAllVisuals();
            }
        }

        private int _selectedIndex = -1;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value < -1 || value >= _cards.Count) return;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                UpdateAllVisuals();
                // Don't fire event from setter — only from user clicks
            }
        }

        public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _cards.Count
            ? _cards[_selectedIndex].Curve
            : null;

        public int SegmentCount => _cards.Count;

        public object? GetTagAt(int index) =>
            index >= 0 && index < _cards.Count ? (object)_cards[index].Curve : null;

        // ── Internals ────────────────────────────────────────────────────
        private readonly Grid _grid;
        private readonly List<CurveCard> _cards = new();

        private class CurveCard
        {
            public Border Container = null!;
            public Canvas CurveCanvas = null!;
            public Polyline CurveLine = null!;
            public TextBlock Label = null!;
            public ResponseCurve Curve;
            public bool IsHovered;
        }

        // Curve definitions
        private static readonly (ResponseCurve Curve, string Label)[] CurveDefs =
        {
            (ResponseCurve.Linear,      "Linear"),
            (ResponseCurve.Logarithmic, "Log"),
            (ResponseCurve.Exponential, "Exp"),
        };

        // ── Constructor ──────────────────────────────────────────────────
        public CurvePickerControl()
        {
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            SnapsToDevicePixels = true;

            _grid = new Grid();
            Child = _grid;

            foreach (var (curve, label) in CurveDefs)
                AddCard(curve, label);
        }

        // ── Card builder ─────────────────────────────────────────────────
        private void AddCard(ResponseCurve curve, string labelText)
        {
            var info = new CurveCard { Curve = curve };

            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Canvas for curve drawing (60×40 logical)
            var canvas = new Canvas
            {
                Width = 60,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                SnapsToDevicePixels = true,
                ClipToBounds = true,
            };
            info.CurveCanvas = canvas;

            // Grid dots at 25%, 50%, 75% intersections — 2×2 grid = 4 dots
            double[] dotPositions = { 0.25, 0.75 };
            foreach (var dx in dotPositions)
            {
                foreach (var dy in dotPositions)
                {
                    var dot = new Ellipse
                    {
                        Width = 2,
                        Height = 2,
                        Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                        SnapsToDevicePixels = true,
                    };
                    Canvas.SetLeft(dot, dx * 60 - 1);
                    Canvas.SetTop(dot, (1.0 - dy) * 40 - 1);
                    canvas.Children.Add(dot);
                }
            }

            // Curve polyline
            var polyline = new Polyline
            {
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true,
            };
            info.CurveLine = polyline;
            DrawCurvePoints(polyline, curve, 60, 40);
            canvas.Children.Add(polyline);

            // Label
            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0),
            };
            info.Label = label;

            // Card layout: canvas on top, label below
            var cardContent = new StackPanel();
            cardContent.Children.Add(canvas);
            cardContent.Children.Add(label);

            // Card border
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 4, 4, 2),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = cardContent,
                SnapsToDevicePixels = true,
            };
            info.Container = container;

            int index = _cards.Count;
            _cards.Add(info);
            Grid.SetColumn(container, index);
            _grid.Children.Add(container);

            // Mouse handlers
            container.MouseLeftButtonUp += (_, _) =>
            {
                int idx = _cards.IndexOf(info);
                if (idx >= 0 && idx != _selectedIndex)
                {
                    _selectedIndex = idx;
                    UpdateAllVisuals();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            };

            container.MouseEnter += (_, _) =>
            {
                info.IsHovered = true;
                int idx = _cards.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyHoverVisual(info);
            };

            container.MouseLeave += (_, _) =>
            {
                info.IsHovered = false;
                int idx = _cards.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyNormalVisual(info);
            };

            ApplyNormalVisual(info);
        }

        // ── Curve point computation ───────────────────────────────────────
        private static void DrawCurvePoints(Polyline polyline, ResponseCurve curve, double canvasW, double canvasH)
        {
            const int Points = 32;
            const double Pad = 3.0; // inset from edges

            double drawW = canvasW - Pad * 2;
            double drawH = canvasH - Pad * 2;

            var pts = new PointCollection(Points);
            for (int i = 0; i < Points; i++)
            {
                double t = i / (double)(Points - 1); // 0..1

                double y = curve switch
                {
                    ResponseCurve.Linear      => t,
                    ResponseCurve.Logarithmic => Math.Log10(1.0 + t * 9.0) / Math.Log10(10.0),
                    ResponseCurve.Exponential => t * t,
                    _                         => t,
                };

                double px = Pad + t * drawW;
                double py = Pad + (1.0 - y) * drawH; // flip Y: bottom=0, top=1

                pts.Add(new Point(px, py));
            }

            polyline.Points = pts;
        }

        // ── Visual state helpers ─────────────────────────────────────────
        private void UpdateAllVisuals()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (i == _selectedIndex)
                    ApplySelectedVisual(_cards[i]);
                else if (_cards[i].IsHovered)
                    ApplyHoverVisual(_cards[i]);
                else
                    ApplyNormalVisual(_cards[i]);
            }
        }

        private void ApplyNormalVisual(CurveCard info)
        {
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            info.CurveLine.Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }

        private void ApplyHoverVisual(CurveCard info)
        {
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            info.CurveLine.Stroke = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        }

        private void ApplySelectedVisual(CurveCard info)
        {
            info.Container.Background = new SolidColorBrush(
                Color.FromArgb(0x20, _accentColor.R, _accentColor.G, _accentColor.B));
            info.Container.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x66, _accentColor.R, _accentColor.G, _accentColor.B));
            info.CurveLine.Stroke = new SolidColorBrush(_accentColor);
            info.Label.Foreground = new SolidColorBrush(_accentColor);
        }
    }
}
