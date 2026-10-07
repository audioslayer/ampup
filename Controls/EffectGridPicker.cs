using System.Windows.Media;
using Material.Icons;

namespace AmpUp.Controls;

/// <summary>
/// Compact effect picker used for per-state / activity effects on the Lights tab.
/// It is a <see cref="GridPicker"/> (50px trigger with icon tile + name + description,
/// searchable flyout with category sections) with a small string-value API on top:
/// items carry the LightEffect name (or "none") as their tag.
/// </summary>
public class EffectGridPicker : GridPicker
{
    /// <summary>Tag of the selected item, or "none" when nothing is selected.</summary>
    public string SelectedValue => SelectedTag as string ?? "none";

    /// <summary>Adds an effect row: Material icon in a tinted tile + one-line description.</summary>
    public void AddEffect(string display, string value, MaterialIconKind kind, Color color, string description)
    {
        AddItem(display, value, kind, color, description);
        if (SelectedIndex < 0) SelectedIndex = 0;
    }

    /// <summary>Selects the item whose value matches; falls back to the first item. Does not fire SelectionChanged.</summary>
    public void Select(string value)
    {
        for (int i = 0; i < ItemCount; i++)
        {
            if (GetTagAt(i) as string == value)
            {
                SelectedIndex = i;
                return;
            }
        }
        SelectedIndex = ItemCount > 0 ? 0 : -1;
    }
}
