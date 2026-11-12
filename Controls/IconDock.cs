using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls
{
    /// <summary>
    /// Page-level tab bar shown in a page header: a row of small round icon buttons where the
    /// active item expands into an accent pill with its label. Inactive items show their name as
    /// a tooltip. Left/Right arrows move the selection while the dock has keyboard focus.
    /// </summary>
    public class IconDock : Border
    {
        private const double ItemSize = 44;
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
            Focusable = true;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            VerticalAlignment = VerticalAlignment.Center;

            _row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Child = _row;

            _onAccent = () => Dispatcher.BeginInvoke(() => UpdateAllVisuals(animate: false));
            Loaded += (_, _) => { ThemeManager.OnAccentChanged -= _onAccent; ThemeManager.OnAccentChanged += _onAccent; };
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
                Visibility = Visibility.Collapsed,
            };
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sp.Children.Add(item.Icon);
            sp.Children.Add(item.Text);

            item.Container = new Border
            {
                Height = ItemSize,
                Width = ItemSize,
                CornerRadius = new CornerRadius(ItemSize / 2),
                BorderThickness = new Thickness(1.2),
                Margin = new Thickness(_items.Count == 0 ? 0 : 6, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = sp,
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
        private void UpdateAllVisuals(bool animate)
        {
            foreach (var item in _items)
                PaintItem(item, animate);
        }

        private void PaintItem(DockItem item, bool animate)
        {
            bool active = _items.IndexOf(item) == _selectedIndex;
            var a = ThemeManager.Accent;
            var b = item.Container;

            if (active)
            {
                b.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x28));
                b.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x96));
                b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 9, ShadowDepth = 0, Opacity = 0.3, Color = a };
                item.Icon.Foreground = new SolidColorBrush(a);
                item.Text.Foreground = new SolidColorBrush(a);
                b.ToolTip = item.Tooltip != item.Label ? item.Tooltip : null;
                b.Padding = new Thickness(16, 0, 18, 0);
                if (item.Text.Visibility != Visibility.Visible)
                {
                    item.Text.Visibility = Visibility.Visible;
                    ExpandPill(b, animate);
                }
            }
            else
            {
                b.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                b.BorderBrush = new SolidColorBrush(item.IsHovered && item.IsEnabled
                    ? Color.FromRgb(0x5A, 0x5A, 0x5A)
                    : Color.FromRgb(0x34, 0x34, 0x34));
                b.Effect = null;
                item.Icon.SetResourceReference(MaterialIcon.ForegroundProperty,
                    item.IsHovered && item.IsEnabled ? "TextPrimaryBrush" : "TextDimBrush");
                b.ToolTip = item.Tooltip;
                b.Padding = new Thickness(0);
                if (item.Text.Visibility != Visibility.Collapsed)
                {
                    item.Text.Visibility = Visibility.Collapsed;
                    b.BeginAnimation(WidthProperty, null);
                    b.Width = ItemSize;
                }
            }
        }

        /// <summary>Grows the pill from the circle width to fit icon + label (140ms ease-out).</summary>
        private static void ExpandPill(Border b, bool animate)
        {
            b.BeginAnimation(WidthProperty, null);
            var content = (UIElement)b.Child;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double target = Math.Max(ItemSize,
                Math.Ceiling(content.DesiredSize.Width + b.Padding.Left + b.Padding.Right
                             + b.BorderThickness.Left + b.BorderThickness.Right));

            if (animate && SystemParameters.ClientAreaAnimation)
            {
                b.Width = target;
                b.BeginAnimation(WidthProperty,
                    new DoubleAnimation(ItemSize, target, TimeSpan.FromMilliseconds(140))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.Stop,
                    });
            }
            else
            {
                b.Width = target;
            }
        }
    }
}
