using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls
{
    /// <summary>
    /// Page-level tab bar shown in a page header: a row of icon + label tabs with a thin hairline
    /// underneath; the active tab is accent-coloured with a glowing accent underline. Left/Right
    /// arrows move the selection while the dock has keyboard focus.
    /// </summary>
    public class IconDock : Border
    {
        private const double ItemHeight = 40;
        private const double IconSize = 15;

        // ── Events ───────────────────────────────────────────────────────
        /// <summary>Raised when the user picks an item (click / arrow keys) or via <see cref="SelectAndNotify"/>.</summary>
        public event EventHandler? SelectionChanged;

        // ── Internals ────────────────────────────────────────────────────
        private readonly StackPanel _row;
        private readonly List<DockItem> _items = new();
        private readonly Action _onAccent;

        private class DockItem
        {
            public string Key = "";
            public string Label = "";
            public string Tooltip = "";
            public Border Container = null!;
            public MaterialIcon Icon = null!;
            public TextBlock Text = null!;
            public Border Underline = null!;
            public bool IsHovered;
            public bool IsEnabled = true;
            public bool IsVisible = true;
        }

        private int _selectedIndex = -1;
        /// <summary>Selected item index. Setting it does not raise <see cref="SelectionChanged"/>.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value < -1 || value >= _items.Count) return;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                UpdateAllVisuals(animate: IsLoaded);
            }
        }

        /// <summary>Key of the selected item, or null when nothing is selected.</summary>
        public string? SelectedKey => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Key : null;

        public int ItemCount => _items.Count;

        // ── Constructor ──────────────────────────────────────────────────
        public IconDock()
        {
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Focusable = true;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            VerticalAlignment = VerticalAlignment.Center;

            _row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Child = _row;
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));

            _onAccent = () => Dispatcher.BeginInvoke(() => UpdateAllVisuals(animate: false));
            Loaded += (_, _) =>
            {
                ThemeManager.OnAccentChanged -= _onAccent; ThemeManager.OnAccentChanged += _onAccent;
                UpdateAllVisuals(animate: false);
            };
            Unloaded += (_, _) => ThemeManager.OnAccentChanged -= _onAccent;
            KeyDown += OnDockKeyDown;
        }

        // ── Public API ───────────────────────────────────────────────────
        /// <summary>Adds an item. <paramref name="key"/> identifies it for <see cref="IndexOf"/> / <see cref="SelectedKey"/>.</summary>
        public void AddItem(string key, string label, MaterialIconKind icon, string? tooltip = null)
        {
            var item = new DockItem { Key = key, Label = label, Tooltip = tooltip ?? label };

            item.Icon = new MaterialIcon
            {
                Kind = icon,
                Width = IconSize,
                Height = IconSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            item.Text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sp.Children.Add(item.Icon);
            sp.Children.Add(item.Text);

            item.Underline = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(1),
                Visibility = Visibility.Collapsed,
            };
            sp.Margin = new Thickness(18, 0, 18, 2);
            var cell = new Grid();
            cell.Children.Add(sp);
            cell.Children.Add(item.Underline);

            item.Container = new Border
            {
                Height = ItemHeight,
                Background = Brushes.Transparent,
                Margin = new Thickness(_items.Count == 0 ? 0 : 2, 0, 0, -1),
                Cursor = Cursors.Hand,
                Child = cell,
                SnapsToDevicePixels = true,
            };

            _items.Add(item);
            _row.Children.Add(item.Container);

            item.Container.MouseEnter += (_, _) => { item.IsHovered = true; PaintItem(item, animate: false); };
            item.Container.MouseLeave += (_, _) => { item.IsHovered = false; PaintItem(item, animate: false); };
            item.Container.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Focus();
                if (!item.IsEnabled) return;
                SelectAndNotify(_items.IndexOf(item));
            };

            PaintItem(item, animate: false);
        }

        /// <summary>Selects an item as if the user picked it (raises <see cref="SelectionChanged"/>).</summary>
        public void SelectAndNotify(int index)
        {
            if (index < 0 || index >= _items.Count || index == _selectedIndex) return;
            _selectedIndex = index;
            UpdateAllVisuals(animate: IsLoaded);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public int IndexOf(string key) => _items.FindIndex(i => i.Key == key);

        public void SetItemVisible(int index, bool visible)
        {
            if (index < 0 || index >= _items.Count) return;
            _items[index].IsVisible = visible;
            _items[index].Container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetItemEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= _items.Count) return;
            _items[index].IsEnabled = enabled;
            _items[index].Container.Opacity = enabled ? 1.0 : 0.4;
            _items[index].Container.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        }

        // ── Keyboard ─────────────────────────────────────────────────────
        private void OnDockKeyDown(object sender, KeyEventArgs e)
        {
            int step = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            if (step == 0 || _items.Count == 0) return;
            e.Handled = true;
            int i = _selectedIndex;
            for (int n = 0; n < _items.Count; n++)
            {
                i += step;
                if (i < 0 || i >= _items.Count) return;
                if (_items[i].IsVisible && _items[i].IsEnabled)
                {
                    SelectAndNotify(i);
                    return;
                }
            }
        }

        // ── Visuals ──────────────────────────────────────────────────────
        private static Effect GlowEffect(Color c, double blur, double opacity = 0.3)
        {
            var fx = new DropShadowEffect { BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, Color = c };
            fx.Freeze();
            return fx;
        }

        private void UpdateAllVisuals(bool animate)
        {
            foreach (var item in _items)
                PaintItem(item, animate);
        }

        private void PaintItem(DockItem item, bool animate)
        {
            bool active = _items.IndexOf(item) == _selectedIndex;
            var a = ThemeManager.Accent;

            if (active)
            {
                item.Underline.Background = new SolidColorBrush(a);
                item.Underline.Effect = GlowEffect(a, 6); // single active underline only
                item.Underline.Visibility = Visibility.Visible;
                item.Icon.Foreground = new SolidColorBrush(a);
                item.Text.Foreground = new SolidColorBrush(a);
                item.Container.ToolTip = item.Tooltip != item.Label ? item.Tooltip : null;
            }
            else
            {
                item.Underline.Visibility = Visibility.Collapsed;
                item.Underline.Effect = null;
                bool hot = item.IsHovered && item.IsEnabled;
                item.Icon.SetResourceReference(MaterialIcon.ForegroundProperty, hot ? "TextPrimaryBrush" : "TextDimBrush");
                item.Text.SetResourceReference(TextBlock.ForegroundProperty, hot ? "TextPrimaryBrush" : "TextSecBrush");
                item.Container.ToolTip = item.Tooltip != item.Label ? item.Tooltip : null;
            }
        }
    }
}
