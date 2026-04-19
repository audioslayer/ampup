using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls;

/// <summary>
/// Unified visual tile for the Stream Controller editor. Represents one of:
///   - an LCD key (with live preview image),
///   - a physical side button, or
///   - an encoder press target (with optional rotate hint).
///
/// Shares the Room tab's card aesthetic so the Buttons tab matches the rest
/// of the app.
///
/// API contract — all parallel agents integrate against this surface:
///   Kind            — visual mode
///   Title           — caption under the tile
///   Subtitle        — small secondary line (e.g. "None" or action summary)
///   PreviewImage    — optional BitmapSource (for LCD keys)
///   IconKind        — MaterialIconKind name for a glyph-only tile (buttons/encoders)
///   AccentColor     — border/ring color; defaults to ThemeManager.Accent
///   IsSelected      — highlight state
///   OnClick         — raised when the tile is left-clicked
///   OnRightClick    — raised when the tile is right-clicked (for context menus)
/// </summary>
public class StreamControllerTile : Border
{
    public enum TileKind
    {
        LcdKey,
        SideButton,
        Encoder,
    }

    public TileKind Kind { get; set; } = TileKind.LcdKey;
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public System.Windows.Media.Imaging.BitmapSource? PreviewImage { get; set; }
    public string IconKind { get; set; } = "";
    public Color AccentColor { get; set; } = ThemeManager.Accent;
    public bool IsSelected { get; set; }

    public event Action? OnClick;
    public event Action<MouseButtonEventArgs>? OnRightClick;

    public StreamControllerTile()
    {
        // Implementation filled in by parallel agent.
        Height = 140;
        MinWidth = 140;
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
    }

    /// <summary>Rebuild visuals after any property changes.</summary>
    public virtual void Refresh() { }
}
