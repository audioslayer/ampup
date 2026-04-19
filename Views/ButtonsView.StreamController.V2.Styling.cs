using System.Windows;
using System.Windows.Controls;

namespace AmpUp.Views;

/// <summary>
/// [Agent E] V2 Buttons styling layer.
///
/// Provides shared styling constants that the other V2 region agents reference,
/// plus the <see cref="ButtonsView.ApplyV2Styling"/> implementation which tunes
/// the root container so every region feels like a sibling of the Room tab.
///
/// Design language (mirrors <c>Views/RoomView.xaml.cs</c>):
///   - Card radius 10 (MakeSectionCard) / tile radius 14 (pills)
///   - Card padding 16 / compact padding 14
///   - Section spacing ~10-12 between cards, ~16 around groupings
///   - Background brushes are all <see cref="DynamicResource"/> so card themes swap at runtime
/// </summary>
public partial class ButtonsView
{
    /// <summary>
    /// Shared sizing / spacing constants for the V2 Buttons designer.
    /// Keep in sync with Room tab dimensions so the two tabs feel like siblings.
    /// </summary>
    internal static class V2Style
    {
        // ── Spacing ────────────────────────────────────────────────────
        /// <summary>Gap between major stacked sections.</summary>
        public const double SectionGap = 16;

        /// <summary>Gap between tiles inside a card (e.g. key grid).</summary>
        public const double TileGap = 10;

        /// <summary>Gap between consecutive rows inside a card.</summary>
        public const double FieldGap = 10;

        // ── Corner radii (match Room tab) ──────────────────────────────
        /// <summary>Corner radius for section cards / panels (matches MakeSectionCard).</summary>
        public const double CardCorner = 10;

        /// <summary>Corner radius for pill / tile elements (matches MakeColorPill, tray tiles).</summary>
        public const double TileCorner = 14;

        /// <summary>Corner radius for compact inline chips.</summary>
        public const double ChipCorner = 8;

        /// <summary>Corner radius for input fields (textbox / combo).</summary>
        public const double FieldCorner = 6;

        // ── Padding ────────────────────────────────────────────────────
        /// <summary>Default card interior padding (matches MakeSectionCard).</summary>
        public const double PanelPadding = 14;

        /// <summary>Roomy card interior padding for big hosts.</summary>
        public const double PanelPaddingLarge = 16;

        /// <summary>Padding for compact inline tiles.</summary>
        public const double TilePadding = 10;

        // ── Typography ─────────────────────────────────────────────────
        /// <summary>Section header (e.g. "ACTION", "PREVIEW") — semi-bold uppercase.</summary>
        public const double HeaderFontSize = 14;

        /// <summary>Standard field / body text.</summary>
        public const double FieldFontSize = 12;

        /// <summary>Small caption labels underneath tiles.</summary>
        public const double LabelFontSize = 10;

        /// <summary>Tiny meta caption (hardware kind badges etc.).</summary>
        public const double MetaFontSize = 9;

        // ── Column widths ──────────────────────────────────────────────
        /// <summary>Gap between the left canvas column and the right editor column.</summary>
        public const double ColumnGap = 24;

        /// <summary>Minimum width of the right editor column.</summary>
        public const double RightColumnMinWidth = 360;

        // ── Thicknesses ────────────────────────────────────────────────
        /// <summary>Bottom margin between section cards in a stack.</summary>
        public static readonly Thickness SectionMargin = new Thickness(0, 0, 0, SectionGap);

        /// <summary>Bottom margin between fields inside a card.</summary>
        public static readonly Thickness FieldMargin = new Thickness(0, 0, 0, FieldGap);

        /// <summary>Standard card interior padding (all sides).</summary>
        public static readonly Thickness CardPadding = new Thickness(PanelPadding);

        /// <summary>Roomy card interior padding (all sides).</summary>
        public static readonly Thickness CardPaddingLarge = new Thickness(PanelPaddingLarge);

        /// <summary>Outer breathing-room margin for the V2 root grid.</summary>
        public static readonly Thickness RootMargin = new Thickness(0);

        /// <summary>Left column trailing gap — half of <see cref="ColumnGap"/> goes each side.</summary>
        public static readonly Thickness LeftColumnMargin = new Thickness(0, 0, ColumnGap / 2, 0);

        /// <summary>Right column leading gap — half of <see cref="ColumnGap"/> goes each side.</summary>
        public static readonly Thickness RightColumnMargin = new Thickness(ColumnGap / 2, 0, 0, 0);

        /// <summary>Section-header bottom margin (label directly above content).</summary>
        public static readonly Thickness HeaderMargin = new Thickness(0, 0, 0, 10);
    }

    /// <summary>
    /// Final styling pass applied at the end of
    /// <see cref="BuildStreamControllerDesignerV2"/>. Keeps the V2 Buttons
    /// root visually aligned with the Room tab's section-card aesthetic.
    /// </summary>
    partial void ApplyV2Styling()
    {
        if (_v2Root == null) return;

        // ── Root: outer breathing room + grid hygiene ───────────────────
        _v2Root.Margin = V2Style.RootMargin;

        // Rebuild the two-column layout with a hairline separator between
        // the columns. Grid columns: [ left | separator | right ].
        // Agents that fill panels only know about _v2LeftPanel and the
        // ScrollViewer in column 1, so we add the separator as column 1
        // and push the ScrollViewer to column 2.
        if (_v2Root.ColumnDefinitions.Count == 2)
        {
            // Convert 2-col layout to 3-col: left | separator | right
            _v2Root.ColumnDefinitions.Clear();
            _v2Root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(2, GridUnitType.Star),
            });
            _v2Root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
            _v2Root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = V2Style.RightColumnMinWidth,
            });

            // Re-home existing children: whatever was in column 1 moves to column 2.
            foreach (UIElement child in _v2Root.Children)
            {
                if (child == null) continue;
                int col = Grid.GetColumn(child);
                if (col == 1) Grid.SetColumn(child, 2);
            }

            // Subtle vertical separator — 1px, theme-aware, soft opacity.
            var separator = new Border
            {
                Width = 1,
                Margin = new Thickness(V2Style.ColumnGap / 2, 8, V2Style.ColumnGap / 2, 8),
                Opacity = 0.6,
                SnapsToDevicePixels = true,
            };
            separator.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            Grid.SetColumn(separator, 1);
            _v2Root.Children.Add(separator);
        }

        // ── Left canvas column ─────────────────────────────────────────
        if (_v2LeftPanel != null)
        {
            // The separator now owns the inter-column gap; the left panel
            // just needs a little trailing breathing room.
            _v2LeftPanel.Margin = new Thickness(0);
        }

        // ── Right editor: give each region subtle card framing ─────────
        // Only apply framing if the panel has children — otherwise leave
        // empty hosts invisible so an unfilled region doesn't show an
        // empty card outline.
        StyleRightSection(_v2PreviewPanel);
        StyleRightSection(_v2ActionPanel);
        StyleRightSection(_v2ActionFieldsPanel);
    }

    /// <summary>
    /// Applies consistent section-card framing to a right-column panel.
    /// No-op when the panel is null or empty.
    /// </summary>
    private static void StyleRightSection(StackPanel? panel)
    {
        if (panel == null) return;

        // Use margin for inter-section spacing. Padding belongs to the
        // individual cards each agent builds — applying it to the host
        // stack would double-pad content.
        panel.Margin = V2Style.SectionMargin;
    }
}
