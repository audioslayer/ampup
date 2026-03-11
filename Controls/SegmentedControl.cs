using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public class SegmentedControl : Border
    {
        // ── Events ───────────────────────────────────────────────────────
        public event EventHandler? SelectionChanged;

        // ── Public properties ────────────────────────────────────────────
        private Color _accentColor = ThemeManager.Accent;
        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                _accentColor = value;
                UpdateAllSegmentVisuals();
            }
        }

        private int _selectedIndex = -1;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value < -1 || value >= _segments.Count) return;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                UpdateAllSegmentVisuals();
                // Don't fire event from setter — only from user clicks
            }
        }

        public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _segments.Count
            ? _segments[_selectedIndex].Tag
            : null;

        public int SegmentCount => _segments.Count;

        public object? GetTagAt(int index) =>
            index >= 0 && index < _segments.Count ? _segments[index].Tag : null;

        // ── Internals ────────────────────────────────────────────────────
        private readonly Grid _grid;
        private readonly List<SegmentInfo> _segments = new();

        private class SegmentInfo
        {
            public Border Container = null!;
            public TextBlock Label = null!;
            public object? Tag;
            public bool IsHovered;
        }

        // ── Constructor ──────────────────────────────────────────────────
        public SegmentedControl()
        {
            // Outer border styling
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            SnapsToDevicePixels = true;

            _grid = new Grid();
            Child = _grid;
        }

        // ── Public API ───────────────────────────────────────────────────
        public void AddSegment(string display, object? tag = null)
        {
            var info = new SegmentInfo { Tag = tag };

            // Add column
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Label
            var textBlock = new TextBlock
            {
                Text = display,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            info.Label = textBlock;

            // Segment border
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                Child = textBlock,
                SnapsToDevicePixels = true,
            };
            info.Container = border;

            int index = _segments.Count;
            _segments.Add(info);
            Grid.SetColumn(border, index);
            _grid.Children.Add(border);

            // Mouse handlers
            border.MouseLeftButtonUp += (_, _) =>
            {
                int idx = _segments.IndexOf(info);
                if (idx >= 0 && idx != _selectedIndex)
                {
                    _selectedIndex = idx;
                    UpdateAllSegmentVisuals();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            };

            border.MouseEnter += (_, _) =>
            {
                info.IsHovered = true;
                int idx = _segments.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyHoverVisual(info);
            };

            border.MouseLeave += (_, _) =>
            {
                info.IsHovered = false;
                int idx = _segments.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyNormalVisual(info);
            };

            // Apply initial state
            if (_selectedIndex == index)
                ApplySelectedVisual(info);
            else
                ApplyNormalVisual(info);
        }

        public void ClearSegments()
        {
            _segments.Clear();
            _grid.Children.Clear();
            _grid.ColumnDefinitions.Clear();
            _selectedIndex = -1;
        }

        // ── Visual state helpers ─────────────────────────────────────────
        private void UpdateAllSegmentVisuals()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                if (i == _selectedIndex)
                    ApplySelectedVisual(_segments[i]);
                else if (_segments[i].IsHovered)
                    ApplyHoverVisual(_segments[i]);
                else
                    ApplyNormalVisual(_segments[i]);
            }
        }

        private void ApplyNormalVisual(SegmentInfo info)
        {
            info.Container.Background = Brushes.Transparent;
            info.Container.BorderBrush = Brushes.Transparent;
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            info.Label.FontWeight = FontWeights.Normal;
        }

        private void ApplyHoverVisual(SegmentInfo info)
        {
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            info.Container.BorderBrush = Brushes.Transparent;
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
            info.Label.FontWeight = FontWeights.Normal;
        }

        private void ApplySelectedVisual(SegmentInfo info)
        {
            // Background: accent at 25% opacity
            info.Container.Background = new SolidColorBrush(
                Color.FromArgb(0x40, _accentColor.R, _accentColor.G, _accentColor.B));
            // Border: accent at 40% opacity
            info.Container.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x66, _accentColor.R, _accentColor.G, _accentColor.B));
            // Text: full accent color
            info.Label.Foreground = new SolidColorBrush(_accentColor);
            info.Label.FontWeight = FontWeights.Medium;
        }
    }
}
