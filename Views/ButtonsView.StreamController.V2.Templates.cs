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
    private TextBlock? _v2TemplatesSectionArrow;
    private StackPanel? _v2TemplatesSectionContent;
    private bool _v2TemplatesExpanded;

    private Border BuildV2TemplatesSection()
    {
        var section = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        section.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        section.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var stack = new StackPanel();

        // Header matches SPACES — accent bar + uppercase label + chevron.
        var headerRow = new Border
        {
            Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 10, 0),
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "TEMPLATES",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _v2TemplatesSectionArrow = new TextBlock
        {
            Text = "▶",
            FontSize = 9,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        headerStack.Children.Add(_v2TemplatesSectionArrow);
        headerRow.Child = headerStack;
        headerRow.MouseLeftButtonDown += (_, _) => ToggleV2TemplatesExpanded();
        stack.Children.Add(headerRow);

        _v2TemplatesSectionContent = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0),
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
            _v2TemplatesSectionArrow.Text = _v2TemplatesExpanded ? "▼" : "▶";

        if (_v2TemplatesExpanded) RefreshV2TemplatesList();
    }

    private void RefreshV2TemplatesList()
    {
        if (_v2TemplatesSectionContent == null) return;
        _v2TemplatesSectionContent.Children.Clear();

        _v2TemplatesSectionContent.Children.Add(new TextBlock
        {
            Text = "Pre-built Space layouts — click Add to copy into your Spaces. Editable afterwards.",
            FontSize = 11,
            Foreground = FindBrush("TextDimBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 10),
        });

        foreach (var tmpl in SpaceTemplates.All)
            _v2TemplatesSectionContent.Children.Add(BuildTemplateRow(tmpl));
    }

    private Border BuildTemplateRow(SpaceTemplates.Template tmpl)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
        };
        row.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        // Parse the accent hex so the badge and "+" button tint match the template.
        Color accentColor = ThemeManager.Accent;
        try { accentColor = (Color)ColorConverter.ConvertFromString(tmpl.AccentHex); }
        catch { }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // accent dot
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name + desc
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Add button

        // Accent dot (colored pill so each template is visually distinct in the list).
        var accentPill = new Border
        {
            Width = 8,
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(accentColor),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(accentPill, 0);
        grid.Children.Add(accentPill);

        // Name + description.
        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = tmpl.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
        });
        labelStack.Children.Add(new TextBlock
        {
            Text = tmpl.Description,
            FontSize = 11,
            Foreground = FindBrush("TextDimBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(labelStack, 1);
        grid.Children.Add(labelStack);

        var addBtn = MakeEditorButton("Add", (_, _) => AddTemplateToSpaces(tmpl));
        addBtn.Margin = new Thickness(12, 0, 0, 0);
        addBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(addBtn, 2);
        grid.Children.Add(addBtn);

        row.Child = grid;
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
    }
}
