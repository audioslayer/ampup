using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public class EffectPickerControl : Border
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
                if (value < -1 || value >= _tiles.Count) return;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                UpdateAllVisuals();
                // Don't fire event from setter — only from user clicks
            }
        }

        public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _tiles.Count
            ? (object)_tiles[_selectedIndex].Effect
            : null;

        public LightEffect SelectedEffect
        {
            get => _selectedIndex >= 0 && _selectedIndex < _tiles.Count
                ? _tiles[_selectedIndex].Effect
                : LightEffect.SingleColor;
            set
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (_tiles[i].Effect == value)
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        // ── Internals ────────────────────────────────────────────────────
        private readonly List<EffectTile> _tiles = new();

        private class EffectTile
        {
            public Border Container = null!;
            public TextBlock Icon = null!;
            public TextBlock Label = null!;
            public LightEffect Effect;
            public bool IsHovered;
            public bool IsEmoji; // emoji icons ignore Foreground color
            public Color TileColor; // unique color per effect
        }

        // Per-effect colors — each tile gets its own personality
        private static readonly Dictionary<LightEffect, Color> EffectColors = new()
        {
            { LightEffect.SingleColor,  Color.FromRgb(0x64, 0xB5, 0xF6) }, // soft blue
            { LightEffect.ColorBlend,   Color.FromRgb(0xBA, 0x68, 0xC8) }, // purple
            { LightEffect.PositionFill, Color.FromRgb(0x4D, 0xD0, 0xE1) }, // cyan
            { LightEffect.GradientFill, Color.FromRgb(0xAE, 0xD5, 0x81) }, // lime green
            { LightEffect.Blink,        Color.FromRgb(0xFF, 0xD5, 0x4F) }, // gold
            { LightEffect.Pulse,        Color.FromRgb(0xE0, 0x6C, 0x9F) }, // pink
            { LightEffect.Breathing,    Color.FromRgb(0x80, 0xCB, 0xC4) }, // teal
            { LightEffect.Fire,         Color.FromRgb(0xFF, 0x8A, 0x3D) }, // orange
            { LightEffect.Comet,        Color.FromRgb(0x7C, 0x8C, 0xF8) }, // indigo
            { LightEffect.Sparkle,      Color.FromRgb(0xFF, 0xF1, 0x76) }, // bright yellow
            { LightEffect.RainbowWave,  Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { LightEffect.RainbowCycle, Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { LightEffect.MicStatus,    Color.FromRgb(0x42, 0xA5, 0xF5) }, // blue
            { LightEffect.DeviceMute,   Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { LightEffect.AudioReactive,Color.FromRgb(0x00, 0xE6, 0x76) }, // accent green
        };

        // ── Constructor ──────────────────────────────────────────────────
        public EffectPickerControl()
        {
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            SnapsToDevicePixels = true;

            var mainPanel = new StackPanel();
            Child = mainPanel;

            AddCategory(mainPanel, "STATIC", new[]
            {
                (LightEffect.SingleColor,  "●",   "Solid",    false),
                (LightEffect.ColorBlend,   "◑",   "Blend",    false),
                (LightEffect.PositionFill, "▂▅█", "Fill",     false),
                (LightEffect.GradientFill, "◐",   "Gradient", false),
            });

            AddCategory(mainPanel, "ANIMATED", new[]
            {
                (LightEffect.Blink,        "⚡",  "Blink",   true),
                (LightEffect.Pulse,        "◉",   "Pulse",   false),
                (LightEffect.Breathing,    "〰",  "Breathe", false),
                (LightEffect.Fire,         "🔥",  "Fire",    true),
                (LightEffect.Comet,        "☄",   "Comet",   false),
                (LightEffect.Sparkle,      "✦",   "Sparkle", false),
                (LightEffect.RainbowWave,  "🌊",  "Rainbow", true),
                (LightEffect.RainbowCycle, "🔄",  "Cycle",   true),
            });

            AddCategory(mainPanel, "REACTIVE", new[]
            {
                (LightEffect.MicStatus,    "🎤",  "Mic",   true),
                (LightEffect.DeviceMute,   "🔇",  "Mute",  true),
                (LightEffect.AudioReactive,"🎵",  "Audio", true),
            });
        }

        // ── Category builder ─────────────────────────────────────────────
        private void AddCategory(StackPanel parent, string title, (LightEffect effect, string icon, string label, bool isEmoji)[] items)
        {
            var header = new TextBlock
            {
                Text = title,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(2, 6, 0, 4),
            };
            parent.Children.Add(header);

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (effect, icon, label, isEmoji) in items)
            {
                var tile = BuildTile(effect, icon, label, isEmoji);
                wrap.Children.Add(tile.Container);
            }
            parent.Children.Add(wrap);
        }

        // ── Tile builder ─────────────────────────────────────────────────
        private EffectTile BuildTile(LightEffect effect, string icon, string label, bool isEmoji)
        {
            var tileColor = EffectColors.GetValueOrDefault(effect, Color.FromRgb(0x00, 0xE6, 0x76));
            var info = new EffectTile { Effect = effect, IsEmoji = isEmoji, TileColor = tileColor };

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = isEmoji ? 16 : 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            };
            info.Icon = iconBlock;

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0),
            };
            info.Label = labelBlock;

            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            content.Children.Add(iconBlock);
            content.Children.Add(labelBlock);

            var container = new Border
            {
                Width = 58,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 6, 4, 5),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Child = content,
                SnapsToDevicePixels = true,
            };
            info.Container = container;

            int index = _tiles.Count;
            _tiles.Add(info);

            // Mouse handlers
            container.MouseLeftButtonUp += (_, _) =>
            {
                int idx = _tiles.IndexOf(info);
                if (idx >= 0 && idx != _selectedIndex)
                {
                    _selectedIndex = idx;
                    UpdateAllVisuals();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (idx == _selectedIndex)
                {
                    // Already selected — still fire so callers can react
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            };

            container.MouseEnter += (_, _) =>
            {
                info.IsHovered = true;
                int idx = _tiles.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyHoverVisual(info);
            };

            container.MouseLeave += (_, _) =>
            {
                info.IsHovered = false;
                int idx = _tiles.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyNormalVisual(info);
            };

            ApplyNormalVisual(info);
            return info;
        }

        // ── Visual state helpers ─────────────────────────────────────────
        private void UpdateAllVisuals()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (i == _selectedIndex)
                    ApplySelectedVisual(_tiles[i]);
                else if (_tiles[i].IsHovered)
                    ApplyHoverVisual(_tiles[i]);
                else
                    ApplyNormalVisual(_tiles[i]);
            }
        }

        private void ApplyNormalVisual(EffectTile info)
        {
            var c = info.TileColor;
            // Dim version of the tile's color for unselected state
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(
                    Color.FromRgb((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private void ApplyHoverVisual(EffectTile info)
        {
            var c = info.TileColor;
            info.Container.Background = new SolidColorBrush(
                Color.FromArgb(0x10, c.R, c.G, c.B));
            info.Container.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x33, c.R, c.G, c.B));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(
                    Color.FromRgb((byte)(c.R * 2 / 3), (byte)(c.G * 2 / 3), (byte)(c.B * 2 / 3)));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        }

        private void ApplySelectedVisual(EffectTile info)
        {
            var c = info.TileColor;
            info.Container.Background = new SolidColorBrush(
                Color.FromArgb(0x25, c.R, c.G, c.B));
            info.Container.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x77, c.R, c.G, c.B));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(c);
            info.Label.Foreground = new SolidColorBrush(c);
        }
    }
}
