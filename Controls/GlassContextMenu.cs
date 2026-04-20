using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Declarative item record for <see cref="GlassContextMenuHost.Show"/>.
/// Rendered as one row in a glass-style floating popup menu.
/// </summary>
public sealed record GlassMenuItem(
    string Label,
    MaterialIconKind? Icon,
    Action? OnClick,
    IReadOnlyList<GlassMenuItem>? Submenu = null,
    bool IsSeparator = false,
    bool IsEnabled = true,
    bool IsDanger = false,
    bool IsChecked = false)
{
    public static readonly GlassMenuItem Sep = new("", null, null, IsSeparator: true);
}

/// <summary>
/// Renders <see cref="GlassMenuItem"/> lists as a modern glass-style popup:
/// dark blurred card with rounded corners, material icons, hover accent
/// tint, cascading submenus, and a soft drop shadow. Replaces the default
/// Win32-themed WPF ContextMenu that looks out of place against the app's
/// glassmorphic dark theme.
/// </summary>
public static class GlassContextMenuHost
{
    public static void Show(FrameworkElement anchor, IReadOnlyList<GlassMenuItem> items)
    {
        var popup = BuildPopup(items, anchor, mouse: true, parentPopup: null);
        popup.IsOpen = true;
    }

    private static Popup BuildPopup(IReadOnlyList<GlassMenuItem> items,
                                    FrameworkElement anchor,
                                    bool mouse,
                                    Popup? parentPopup)
    {
        var popup = new Popup
        {
            Placement = mouse ? PlacementMode.MousePoint : PlacementMode.Right,
            PlacementTarget = mouse ? null : anchor,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            HorizontalOffset = mouse ? 0 : -4,
            VerticalOffset = mouse ? 0 : -4,
        };

        var root = new Border
        {
            MinWidth = 210,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Margin = new Thickness(10), // room for drop shadow
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.55,
            },
        };
        root.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        // Subtle accent hairline across the top — matches the app's tab
        // underline style and gives every menu a quick identifying ribbon.
        var accentRibbon = new Border
        {
            Height = 2,
            Margin = new Thickness(6, 0, 6, 6),
            CornerRadius = new CornerRadius(1),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x00, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B), 0.0),
                    new(Color.FromArgb(0xA0, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B), 0.5),
                    new(Color.FromArgb(0x00, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B), 1.0),
                }, new Point(0, 0), new Point(1, 0)),
        };

        var stack = new StackPanel();
        stack.Children.Add(accentRibbon);

        Popup? openSubmenu = null;
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                var sep = new Border
                {
                    Height = 1,
                    Margin = new Thickness(10, 4, 10, 4),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                };
                stack.Children.Add(sep);
                continue;
            }

            var row = BuildRow(item, popup, ref openSubmenu);
            stack.Children.Add(row);
        }

        root.Child = stack;
        popup.Child = root;

        // Dismiss any cascading child popup when the parent closes.
        popup.Closed += (_, _) =>
        {
            if (openSubmenu != null) openSubmenu.IsOpen = false;
        };

        return popup;
    }

    private static Border BuildRow(GlassMenuItem item, Popup owner, ref Popup? openSubmenuSlot)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon slot — accent tint when enabled, dim when disabled.
        UIElement? iconVisual = null;
        if (item.Icon is { } kind)
        {
            var icon = new MaterialIcon
            {
                Kind = kind,
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = item.IsDanger
                    ? (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))
                    : new SolidColorBrush(ThemeManager.Accent),
            };
            if (!item.IsEnabled) icon.Opacity = 0.35;
            iconVisual = icon;
        }
        else if (item.IsChecked)
        {
            var check = new MaterialIcon
            {
                Kind = MaterialIconKind.Check,
                Width = 15, Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeManager.Accent),
            };
            iconVisual = check;
        }
        if (iconVisual != null)
        {
            Grid.SetColumn(iconVisual, 0);
            grid.Children.Add(iconVisual);
        }

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 12, 0),
        };
        if (item.IsDanger)
            label.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        else
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        if (!item.IsEnabled) label.Opacity = 0.4;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        // Submenu chevron.
        if (item.Submenu is { Count: > 0 })
        {
            var chev = new MaterialIcon
            {
                Kind = MaterialIconKind.ChevronRight,
                Width = 14, Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            chev.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
            Grid.SetColumn(chev, 2);
            grid.Children.Add(chev);
        }

        var row = new Border
        {
            Padding = new Thickness(8, 7, 10, 7),
            CornerRadius = new CornerRadius(6),
            Cursor = item.IsEnabled ? Cursors.Hand : Cursors.Arrow,
            Background = System.Windows.Media.Brushes.Transparent,
            Margin = new Thickness(0, 1, 0, 1),
            Child = grid,
            IsHitTestVisible = item.IsEnabled,
        };

        if (item.IsEnabled)
        {
            Color hoverBg = item.IsDanger
                ? Color.FromArgb(0x2A, 0xFF, 0x52, 0x52)
                : Color.FromArgb(0x2A, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B);
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(hoverBg);
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
        }

        // Handler: either fire the action or open a submenu.
        if (item.Submenu is { Count: > 0 })
        {
            var sub = item.Submenu;
            var ownerForCapture = owner;
            var rowForCapture = row;
            row.MouseLeftButtonUp += (_, e) =>
            {
                var subPopup = BuildPopup(sub, rowForCapture, mouse: false, parentPopup: ownerForCapture);
                subPopup.PlacementTarget = rowForCapture;
                subPopup.Placement = PlacementMode.Right;
                subPopup.IsOpen = true;
                e.Handled = true;
            };
        }
        else if (item.OnClick != null)
        {
            var act = item.OnClick;
            var ownerForCapture = owner;
            row.MouseLeftButtonUp += (_, e) =>
            {
                ownerForCapture.IsOpen = false;
                act();
                e.Handled = true;
            };
        }

        return row;
    }
}
