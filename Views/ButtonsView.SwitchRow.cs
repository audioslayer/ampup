using System.Windows;
using System.Windows.Controls;
using AmpUp.Controls;

namespace AmpUp.Views;

/// <summary>
/// Label + <see cref="UiKit.Switch"/> row used in place of the old CheckBoxes in the Buttons
/// designer. Exposes a CheckBox-like <see cref="IsChecked"/> so existing call sites keep working.
/// Programmatic sets update the visual only; <see cref="Toggled"/> fires on user interaction.
/// </summary>
internal sealed class SwitchRow : Grid
{
    private readonly System.Action<bool> _setState;
    private bool _on;

    public event System.Action<bool>? Toggled;

    public SwitchRow(string label, bool initial = false, string? tooltip = null)
    {
        _on = initial;
        ToolTip = tooltip;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Children.Add(text);

        var sw = UiKit.Switch(initial, v => { _on = v; Toggled?.Invoke(v); }, out _setState);
        Grid.SetColumn(sw, 1);
        Children.Add(sw);
    }

    public bool? IsChecked
    {
        get => _on;
        set { _on = value == true; _setState(_on); }
    }
}
