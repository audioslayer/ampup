using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Services;

namespace AmpUp.Views;

/// <summary>
/// V2 Left Panel — TEMPLATES section (collapsible card under SPACES).
///
/// Shows a catalogue of <see cref="SpaceTemplates"/>. Clicking a card's
/// "Add" button materializes a fresh ButtonFolderConfig into the user's
/// N3 config, auto-renaming on collision, then refreshes the Spaces
/// list so the new Space is immediately visible and ready to open.
/// </summary>
public partial class ButtonsView
{
    private Material.Icons.WPF.MaterialIcon? _v2TemplatesSectionArrow;
    private StackPanel? _v2TemplatesSectionContent;
    private bool _v2TemplatesExpanded;

    private Border BuildV2TemplatesSection()
    {
        var section = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        section.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        section.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var stack = new StackPanel();
        // Header matches SPACES — accent bar + label + count + chevron.
        stack.Children.Add(BuildV2CollapsibleHeader("TEMPLATES", ToggleV2TemplatesExpanded,
            out var arrow, out var count));
        _v2TemplatesSectionArrow = arrow;
        count.Text = SpaceTemplates.All.Count.ToString();

        _v2TemplatesSectionContent = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0),
        };
        stack.Children.Add(_v2TemplatesSectionContent);

        section.Child = stack;
        return section;
    }

    private void ToggleV2TemplatesExpanded()
    {
        _v2TemplatesExpanded = !_v2TemplatesExpanded;
        if (_v2TemplatesSectionContent != null)
            _v2TemplatesSectionContent.Visibility = _v2TemplatesExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_v2TemplatesSectionArrow != null)
            _v2TemplatesSectionArrow.Kind = _v2TemplatesExpanded
                ? Material.Icons.MaterialIconKind.ChevronUp : Material.Icons.MaterialIconKind.ChevronDown;

        if (_v2TemplatesExpanded) RefreshV2TemplatesList();
    }

    private void RefreshV2TemplatesList()
    {
        if (_v2TemplatesSectionContent == null) return;
        _v2TemplatesSectionContent.Children.Clear();

        var intro = AmpUp.Controls.UiKit.MutedText("Pre-built Space layouts. Add one to copy it into your Spaces, then edit it like any other.");
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(2, 0, 2, 10);
        _v2TemplatesSectionContent.Children.Add(intro);

        var list = AmpUp.Controls.UiKit.ListContainer(out var rows);
        foreach (var tmpl in SpaceTemplates.All)
            rows.Children.Add(BuildTemplateRow(tmpl));
        _v2TemplatesSectionContent.Children.Add(list);
    }

    private Border BuildTemplateRow(SpaceTemplates.Template tmpl)
    {
        // Parse the accent hex so the icon tile tint matches the template.
        Color accentColor = ThemeManager.Accent;
        try { accentColor = (Color)ColorConverter.ConvertFromString(tmpl.AccentHex); }
        catch { }

        var tile = AmpUp.Controls.UiKit.IconTile(accentColor, Material.Icons.MaterialIconKind.ViewDashboardOutline, 30);
        var add = AmpUp.Controls.UiKit.LinkRow(Material.Icons.MaterialIconKind.Plus, "Add", accent: true,
            () => AddTemplateToSpaces(tmpl), $"Add {tmpl.Name} to your Spaces");
        var row = AmpUp.Controls.UiKit.ListRow(tile, tmpl.Name, tmpl.Description, add);
        row.ToolTip = tmpl.Description;
        return row;
    }

    private void AddTemplateToSpaces(SpaceTemplates.Template tmpl)
    {
        if (_config == null) return;

        var folder = tmpl.Build();
        // Unique-name on collision — same pattern as the manual "+ New Space"
        // button above in BuildV2FoldersSection.
        if (_config.N3.Folders.Any(f => f.Name == folder.Name))
        {
            int counter = 2;
            string candidate;
            do { candidate = $"{folder.Name} ({counter++})"; }
            while (_config.N3.Folders.Any(f => f.Name == candidate));
            folder.Name = candidate;
        }
        _config.N3.Folders.Add(folder);
        QueueSave();

        // Refresh both lists — the new folder shows up in SPACES and the user
        // can Open it immediately.
        RefreshV2FoldersList();
        RefreshV2TemplatesList();

        // Drop the user straight into the new Space so they see their add
        // reflected on the device chassis.
        NavigateToFolderInEditor(folder.Name);
    }
}
