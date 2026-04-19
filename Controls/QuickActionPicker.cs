using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AmpUp.Controls;

/// <summary>
/// Search-first action picker for the Stream Controller Buttons tab.
///
/// Replaces the stack-of-categories drop-down with:
///   - Top: inline search box ("type 'spot' to find Spotify")
///   - Favorites row (driven by config.N3.FavoriteActions)
///   - Recent row (driven by config.N3.RecentActions)
///   - Categories collapsed by default, expand on click
///
/// API contract — parallel agents integrate via these entry points:
///
///   AddItem(value, display, icon, color, category, tooltip)
///   SelectedValue                                         — current action value
///   Select(value)                                         — programmatic set
///   SelectionChanged                                      — event
///   SetFavorites(IEnumerable&lt;string&gt;)                — pass config.N3.FavoriteActions
///   SetRecents(IEnumerable&lt;string&gt;)                  — pass config.N3.RecentActions
///   OnToggleFavorite                                      — raised when star clicked; caller mutates config
///   OnActionChosen                                        — raised when user picks an action (for recents bookkeeping)
/// </summary>
public class QuickActionPicker : Border
{
    public event EventHandler? SelectionChanged;
    public event Action<string>? OnToggleFavorite;
    public event Action<string>? OnActionChosen;

    public string SelectedValue { get; protected set; } = "none";

    public QuickActionPicker()
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        MinHeight = 44;
    }

    public virtual void AddItem(string value, string display, string icon, Color color, string category, string tooltip = "") { }
    public virtual void Select(string value) { SelectedValue = value; }
    public virtual void SetFavorites(IEnumerable<string> values) { }
    public virtual void SetRecents(IEnumerable<string> values) { }
    public virtual void ClearItems() { }

    protected void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    protected void RaiseToggleFavorite(string value) => OnToggleFavorite?.Invoke(value);
    protected void RaiseActionChosen(string value) => OnActionChosen?.Invoke(value);
}
