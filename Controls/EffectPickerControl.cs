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
        public event EventHandler<LightEffect>? EffectHovered;
        public event EventHandler? EffectHoverEnd;

        // ── Public properties ────────────────────────────────────────────
        private Color _accentColor = ThemeManager.Accent;
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

        // ── Category visibility ──────────────────────────────────────────
        private readonly List<TextBlock> _categoryHeaders = new();
        private readonly List<WrapPanel> _categoryPanels = new();
        private int _visibleCategory = -1;

        public int VisibleCategory => _visibleCategory;

        /// <summary>
        /// Show only the specified category (0=static, 1=animated, 2=reactive, 3=global).
        /// Pass -1 to show all categories.
        /// </summary>
        public void SetVisibleCategory(int cat)
        {
            _visibleCategory = cat;
            for (int i = 0; i < _categoryHeaders.Count; i++)
            {
                var vis = (cat == -1 || cat == i) ? Visibility.Visible : Visibility.Collapsed;
                _categoryHeaders[i].Visibility = vis;
                _categoryPanels[i].Visibility = vis;
            }
        }

        // ── Internals ────────────────────────────────────────────────────
        private readonly bool _showGlobal;
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
        public static readonly Dictionary<LightEffect, Color> EffectColors = new()
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
            { LightEffect.AudioReactive,ThemeManager.Accent }, // accent green
            { LightEffect.AudioPositionBlend, Color.FromRgb(0x00, 0xBF, 0xA5) }, // teal

            // New static effects
            { LightEffect.PositionBlend,    Color.FromRgb(0x80, 0xDE, 0xEA) }, // light cyan
            { LightEffect.PositionBlendMute,Color.FromRgb(0xFF, 0x70, 0x43) }, // orange-red (blend + mute)
            { LightEffect.CycleFill,        Color.FromRgb(0xE0, 0x6C, 0x9F) }, // pink
            { LightEffect.RainbowFill,      Color.FromRgb(0xFF, 0xD5, 0x4F) }, // gold

            // New per-knob 3-LED effects
            { LightEffect.PingPong,     Color.FromRgb(0x29, 0xB6, 0xF6) }, // light blue
            { LightEffect.Stack,        Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { LightEffect.Wave,         Color.FromRgb(0x26, 0xC6, 0xDA) }, // cyan
            { LightEffect.Candle,       Color.FromRgb(0xFF, 0xCA, 0x28) }, // amber
            { LightEffect.Wheel,        Color.FromRgb(0xB3, 0x9D, 0xDB) }, // lavender
            { LightEffect.RainbowWheel, Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { LightEffect.Heartbeat,    Color.FromRgb(0xE5, 0x39, 0x35) }, // deep red
            { LightEffect.Plasma,       Color.FromRgb(0xCE, 0x93, 0xD8) }, // light purple
            { LightEffect.Drip,         Color.FromRgb(0x42, 0xA5, 0xF5) }, // blue

            // Reactive/Status effects
            { LightEffect.ProgramMute,   Color.FromRgb(0xFF, 0x8A, 0x65) }, // deep orange
            { LightEffect.AppGroupMute,  Color.FromRgb(0xAB, 0x47, 0xBC) }, // purple
            { LightEffect.DeviceSelect,  Color.FromRgb(0x4D, 0xD0, 0xE1) }, // cyan

            // Global-spanning effects
            { LightEffect.Scanner,       Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { LightEffect.MeteorRain,    Color.FromRgb(0x7C, 0x8C, 0xF8) }, // indigo
            { LightEffect.ColorWave,     Color.FromRgb(0xBA, 0x68, 0xC8) }, // purple
            { LightEffect.Segments,      Color.FromRgb(0xFF, 0x8A, 0x3D) }, // orange
            { LightEffect.TheaterChase,  Color.FromRgb(0x26, 0xC6, 0xDA) }, // cyan
            { LightEffect.RainbowScanner,Color.FromRgb(0xEC, 0x40, 0x7A) }, // pink-red
            { LightEffect.SparkleRain,   Color.FromRgb(0xFF, 0xF1, 0x76) }, // bright yellow
            { LightEffect.BreathingSync, Color.FromRgb(0x80, 0xCB, 0xC4) }, // teal
            { LightEffect.FireWall,      Color.FromRgb(0xFF, 0x57, 0x22) }, // deep orange

            // New global-spanning effects
            { LightEffect.DualRacer,     Color.FromRgb(0x42, 0xA5, 0xF5) }, // blue
            { LightEffect.Lightning,     Color.FromRgb(0xFF, 0xF1, 0x76) }, // bright yellow
            { LightEffect.Fillup,        Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { LightEffect.Ocean,         Color.FromRgb(0x29, 0xB6, 0xF6) }, // ocean blue
            { LightEffect.Collision,     Color.FromRgb(0xFF, 0x8A, 0x3D) }, // orange
            { LightEffect.DNA,           Color.FromRgb(0xBA, 0x68, 0xC8) }, // purple
            { LightEffect.Rainfall,      Color.FromRgb(0x4D, 0xD0, 0xE1) }, // cyan
            { LightEffect.PoliceLights,  Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { LightEffect.Aurora,        Color.FromRgb(0x69, 0xF0, 0xAE) }, // aurora green
            { LightEffect.Matrix,        Color.FromRgb(0x00, 0xE6, 0x76) }, // matrix green
            { LightEffect.Starfield,     Color.FromRgb(0xB3, 0x9D, 0xDB) }, // lavender
            { LightEffect.Equalizer,     Color.FromRgb(0x00, 0xE6, 0x76) }, // green (VU)
            { LightEffect.Waterfall,     Color.FromRgb(0x40, 0xC4, 0xFF) }, // sky blue
            { LightEffect.Lava,          Color.FromRgb(0xFF, 0x6E, 0x40) }, // deep orange
            { LightEffect.VuWave,        Color.FromRgb(0xE0, 0x40, 0xFB) }, // magenta
            { LightEffect.NebulaDrift,   Color.FromRgb(0xCE, 0x93, 0xD8) }, // nebula purple
        };

        // ── Constructor ──────────────────────────────────────────────────
        public EffectPickerControl(bool showGlobal = false)
        {
            _showGlobal = showGlobal;
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
                (LightEffect.PositionBlend,    "▂▄▇", "PosBlend",  false),
                (LightEffect.PositionBlendMute,"▂▄⊘", "Blend+Mute",false),
                (LightEffect.CycleFill,    "▂▅⟳", "CycleFill", false),
                (LightEffect.RainbowFill,  "▂▅🌈","RbwFill",   false),
                (LightEffect.GradientFill, "◐",   "Gradient", false),
            });

            AddCategory(mainPanel, "ANIMATED", new[]
            {
                (LightEffect.Blink,        "⚡",  "Blink",   false),
                (LightEffect.Pulse,        "◉",   "Pulse",   false),
                (LightEffect.Breathing,    "〰",  "Breathe", false),
                (LightEffect.Fire,         "♨",   "Fire",    false),
                (LightEffect.Comet,        "☄",   "Comet",   false),
                (LightEffect.Sparkle,      "✦",   "Sparkle", false),
                (LightEffect.PingPong,     "⟷",  "Pong",    false),
                (LightEffect.Stack,        "▁▃▆", "Stack",   false),
                (LightEffect.Wave,         "∿",   "Wave",    false),
                (LightEffect.Candle,       "♢",   "Candle",  false),
                (LightEffect.RainbowWave,  "≈",   "Rainbow", false),
                (LightEffect.RainbowCycle, "⟳",   "Cycle",   false),
                (LightEffect.Wheel,        "⟲",   "Wheel",   false),
                (LightEffect.RainbowWheel, "⊚",   "R.Wheel", false),
                (LightEffect.Heartbeat,    "♥",   "Heart",   false),
                (LightEffect.Plasma,       "◈",   "Plasma",  false),
                (LightEffect.Drip,         "◊",   "Drip",    false),
            });

            AddCategory(mainPanel, "REACTIVE", new[]
            {
                (LightEffect.MicStatus,     "⏦",   "Mic",      false),
                (LightEffect.DeviceMute,    "⊘",   "Mute",     false),
                (LightEffect.AudioReactive, "♫",   "Audio",    false),
                (LightEffect.AudioPositionBlend, "♫⬍", "Audio+Pos", false),
                (LightEffect.ProgramMute,   "⊗",   "App Mute", false),
                (LightEffect.AppGroupMute,  "⊞",   "Group",    false),
                (LightEffect.DeviceSelect,  "⬡",   "Device",   false),
            });

            if (_showGlobal)
            {
                AddCategory(mainPanel, "GLOBAL SPAN", new[]
                {
                    (LightEffect.Scanner,        "▬",   "Scanner",  false),
                    (LightEffect.MeteorRain,     "☄",   "Meteor",   false),
                    (LightEffect.ColorWave,      "≋",   "Wave",     false),
                    (LightEffect.Segments,       "▮▯",  "Bands",    false),
                    (LightEffect.TheaterChase,   "⋯",   "Chase",    false),
                    (LightEffect.RainbowScanner, "⟿",   "R.Scan",   false),
                    (LightEffect.SparkleRain,    "✧",   "Sparkle",  false),
                    (LightEffect.BreathingSync,  "≈",   "Breath",   false),
                    (LightEffect.FireWall,       "♨",   "Inferno",  false),
                    (LightEffect.DualRacer,      "⇌",   "Racers",   false),
                    (LightEffect.Lightning,      "⚡",  "Bolt",     false),
                    (LightEffect.Fillup,         "▁▃▅▇","Fill Up",  false),
                    (LightEffect.Ocean,          "∿",   "Ocean",    false),
                    (LightEffect.Collision,      "⊕",   "Collide",  false),
                    (LightEffect.DNA,            "⧖",   "Helix",    false),
                    (LightEffect.Rainfall,       "⋮",   "Rain",     false),
                    (LightEffect.PoliceLights,   "⊘",   "Police",   false),
                    (LightEffect.Aurora,         "≈",   "Aurora",   false),
                    (LightEffect.Matrix,         "⋮",   "Matrix",   false),
                    (LightEffect.Starfield,      "✦",   "Stars",    false),
                    (LightEffect.Equalizer,      "▁▃▅",  "EQ",       false),
                    (LightEffect.Waterfall,      "≋",   "Waterfall",false),
                    (LightEffect.Lava,           "◉",   "Lava",     false),
                    (LightEffect.VuWave,         "∿",   "VU Wave",  false),
                    (LightEffect.NebulaDrift,    "✧",   "Nebula",   false),
                });
            }
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
            _categoryHeaders.Add(header);

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (effect, icon, label, isEmoji) in items)
            {
                var tile = BuildTile(effect, icon, label, isEmoji);
                wrap.Children.Add(tile.Container);
            }
            parent.Children.Add(wrap);
            _categoryPanels.Add(wrap);
        }

        // ── Tile builder ─────────────────────────────────────────────────
        private EffectTile BuildTile(LightEffect effect, string icon, string label, bool isEmoji)
        {
            var tileColor = EffectColors.GetValueOrDefault(effect, ThemeManager.Accent);
            var info = new EffectTile { Effect = effect, IsEmoji = isEmoji, TileColor = tileColor };

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = _showGlobal ? (isEmoji ? 20 : 24) : (isEmoji ? 16 : 18),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb((byte)(tileColor.R * 0.6), (byte)(tileColor.G * 0.6), (byte)(tileColor.B * 0.6))),
            };
            info.Icon = iconBlock;

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = _showGlobal ? 10 : 9,
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
                Width = _showGlobal ? 70 : 54,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = _showGlobal ? new Thickness(4, 8, 4, 6) : new Thickness(2, 6, 2, 5),
                Margin = new Thickness(2),
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
                EffectHovered?.Invoke(this, info.Effect);
            };

            container.MouseLeave += (_, _) =>
            {
                info.IsHovered = false;
                int idx = _tiles.IndexOf(info);
                if (idx != _selectedIndex)
                    ApplyNormalVisual(info);
                EffectHoverEnd?.Invoke(this, EventArgs.Empty);
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
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(
                    Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A));
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
                    Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8)));
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
