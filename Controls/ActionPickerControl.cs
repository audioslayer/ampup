using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public class ActionPickerControl : Border
    {
        // ── Events ───────────────────────────────────────────────────────
        public event EventHandler? SelectionChanged;

        // ── Action color mapping ─────────────────────────────────────────
        private static readonly Dictionary<string, Color> ActionColors = new()
        {
            { "none",               Color.FromRgb(0x44, 0x44, 0x44) }, // gray
            { "media_play_pause",   Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { "media_next",         Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { "media_prev",         Color.FromRgb(0x66, 0xBB, 0x6A) }, // green
            { "mute_master",        Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { "mute_mic",           Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { "mute_program",       Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { "mute_active_window", Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { "mute_app_group",     Color.FromRgb(0xEF, 0x53, 0x50) }, // red
            { "launch_exe",         Color.FromRgb(0x42, 0xA5, 0xF5) }, // blue
            { "close_program",      Color.FromRgb(0xFF, 0x7C, 0x43) }, // orange
            { "cycle_output",       Color.FromRgb(0xAB, 0x47, 0xBC) }, // purple
            { "cycle_input",        Color.FromRgb(0xAB, 0x47, 0xBC) }, // purple
            { "select_output",      Color.FromRgb(0xAB, 0x47, 0xBC) }, // purple
            { "select_input",       Color.FromRgb(0xAB, 0x47, 0xBC) }, // purple
            { "macro",              Color.FromRgb(0xFF, 0xD5, 0x4F) }, // gold
            { "system_power",       Color.FromRgb(0xFF, 0x44, 0x44) }, // bright red
            { "switch_profile",     Color.FromRgb(0x29, 0xB6, 0xF6) }, // light blue
            { "cycle_brightness",   Color.FromRgb(0xFF, 0xF1, 0x76) }, // yellow
        };

        // ── Public API ───────────────────────────────────────────────────
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
            }
        }

        public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _tiles.Count
            ? (object?)_tiles[_selectedIndex].ActionValue
            : null;

        public int ItemCount => _tiles.Count;

        public object? GetTagAt(int index)
        {
            if (index < 0 || index >= _tiles.Count) return null;
            return _tiles[index].ActionValue;
        }

        public string SelectedAction
        {
            get => _selectedIndex >= 0 && _selectedIndex < _tiles.Count
                ? _tiles[_selectedIndex].ActionValue
                : "none";
            set
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (_tiles[i].ActionValue == value)
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
                SelectedIndex = 0; // fallback to "none"
            }
        }

        // ── Internals ────────────────────────────────────────────────────
        private readonly List<ActionTile> _tiles = new();

        private class ActionTile
        {
            public Border Container = null!;
            public TextBlock Icon = null!;
            public TextBlock Label = null!;
            public string ActionValue = "";
            public bool IsHovered;
            public bool IsEmoji;
            public Color TileColor;
        }

        // ── Constructor ──────────────────────────────────────────────────
        public ActionPickerControl()
        {
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            SnapsToDevicePixels = true;

            var mainPanel = new StackPanel();
            Child = mainPanel;

            // "None" first — no category header
            BuildTileAndAdd(mainPanel, null, "none", "—", "None", false);

            // MEDIA (green)
            AddCategory(mainPanel, "MEDIA", Color.FromRgb(0x66, 0xBB, 0x6A), new[]
            {
                ("media_play_pause", "⏯", "Play/Pause", true),
                ("media_next",       "⏭", "Next",       true),
                ("media_prev",       "⏮", "Prev",       true),
            });

            // MUTE (red)
            AddCategory(mainPanel, "MUTE", Color.FromRgb(0xEF, 0x53, 0x50), new[]
            {
                ("mute_master",        "🔇", "Volume",  true),
                ("mute_mic",           "🎤", "Mic",     true),
                ("mute_program",       "🔇", "App",     true),
                ("mute_active_window", "🔇", "Window",  true),
                ("mute_app_group",     "🔇", "Group",   true),
            });

            // APP CONTROL (blue/orange)
            AddCategory(mainPanel, "APP CONTROL", Color.FromRgb(0x42, 0xA5, 0xF5), new[]
            {
                ("launch_exe",    "🚀", "Launch", true),
                ("close_program", "✕",  "Close",  false),
            });

            // DEVICE (purple)
            AddCategory(mainPanel, "DEVICE", Color.FromRgb(0xAB, 0x47, 0xBC), new[]
            {
                ("cycle_output",  "🔊", "Cycle Out", true),
                ("cycle_input",   "🎙", "Cycle In",  true),
                ("select_output", "🔊", "Set Out",   true),
                ("select_input",  "🎙", "Set In",    true),
            });

            // SYSTEM (gold/mixed)
            AddCategory(mainPanel, "SYSTEM", Color.FromRgb(0xFF, 0xD5, 0x4F), new[]
            {
                ("macro",            "⌨",  "Macro",      false),
                ("system_power",     "⏻",  "Power",      false),
                ("switch_profile",   "📋", "Profile",    true),
                ("cycle_brightness", "💡", "Brightness", true),
            });

            SelectedIndex = 0;
        }

        // ── Category builder ─────────────────────────────────────────────
        private void AddCategory(StackPanel parent, string title, Color headerColor,
            (string action, string icon, string label, bool isEmoji)[] items)
        {
            var header = new TextBlock
            {
                Text = title,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, headerColor.R, headerColor.G, headerColor.B)),
                Margin = new Thickness(2, 6, 0, 4),
            };
            parent.Children.Add(header);

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (action, icon, label, isEmoji) in items)
            {
                var tile = BuildTile(action, icon, label, isEmoji);
                wrap.Children.Add(tile.Container);
            }
            parent.Children.Add(wrap);
        }

        // Helper to add a tile directly to a panel (for "None" without category)
        private void BuildTileAndAdd(StackPanel parent, string? categoryTitle, string action, string icon, string label, bool isEmoji)
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            var tile = BuildTile(action, icon, label, isEmoji);
            wrap.Children.Add(tile.Container);
            parent.Children.Add(wrap);
        }

        // ── Tile builder ─────────────────────────────────────────────────
        private ActionTile BuildTile(string action, string icon, string label, bool isEmoji)
        {
            var tileColor = ActionColors.GetValueOrDefault(action, Color.FromRgb(0x44, 0x44, 0x44));
            var info = new ActionTile { ActionValue = action, IsEmoji = isEmoji, TileColor = tileColor };

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

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(iconBlock);
            content.Children.Add(labelBlock);

            var container = new Border
            {
                Width = 62,
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

        private void ApplyNormalVisual(ActionTile info)
        {
            var c = info.TileColor;
            info.Container.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(
                    Color.FromRgb((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private void ApplyHoverVisual(ActionTile info)
        {
            var c = info.TileColor;
            info.Container.Background = new SolidColorBrush(Color.FromArgb(0x10, c.R, c.G, c.B));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(
                    Color.FromRgb((byte)(c.R * 2 / 3), (byte)(c.G * 2 / 3), (byte)(c.B * 2 / 3)));
            info.Label.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        }

        private void ApplySelectedVisual(ActionTile info)
        {
            var c = info.TileColor;
            info.Container.Background = new SolidColorBrush(Color.FromArgb(0x25, c.R, c.G, c.B));
            info.Container.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, c.R, c.G, c.B));
            if (!info.IsEmoji)
                info.Icon.Foreground = new SolidColorBrush(c);
            info.Label.Foreground = new SolidColorBrush(c);
        }
    }
}
