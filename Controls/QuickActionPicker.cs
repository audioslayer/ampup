using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Action picker for the Stream Controller Buttons tab.
///
/// Layout:
///   - Top row: "SELECTED <action>" + a slim magnifying-glass icon. The
///     icon expands into a full search box when clicked; typing filters
///     the categories into a flat search-results list.
///   - Categories: accordion of collapsible panels. Clicking a header
///     toggles its list of action rows. First category auto-expands on
///     initial render so the picker isn't a wall of closed sections.
///
/// API:
///   AddItem(value, display, icon, color, category, tooltip)
///   SelectedValue                       — current action value
///   Select(value)                       — programmatic set
///   SelectionChanged                    — event
///   OnActionChosen                      — raised when user picks an action
/// </summary>
public class QuickActionPicker : Border
{
    public event EventHandler? SelectionChanged;
    public event Action<string>? OnActionChosen;

    public string SelectedValue { get; protected set; } = "none";

    // ── Item record ─────────────────────────────────────────────────
    private record PickerItem(string Value, string Display, string Icon, Color Color, string Category, string Tooltip);

    private readonly List<PickerItem> _items = new();

    // ── UI ──────────────────────────────────────────────────────────
    private readonly StackPanel _root;
    private readonly Border _selectedDisplay;
    private readonly TextBlock _selectedIcon;
    private readonly TextBlock _selectedName;
    private readonly StackPanel _optionsHost;
    private readonly Border _searchBoxHost;
    private readonly TextBox _searchBox;
    private readonly TextBlock _searchPlaceholder;
    private readonly Border _categoriesSection;
    private readonly StackPanel _categoryAccordion;
    private readonly Border _searchResultsSection;
    private readonly StackPanel _searchResultsPanel;
    private readonly TextBlock _searchEmptyText;

    // Per-category expanded state. Survives rebuilds so the user's open/closed
    // choices stick while they're navigating the picker.
    private readonly Dictionary<string, bool> _categoryExpanded = new(StringComparer.OrdinalIgnoreCase);
    private string _searchText = "";
    private bool _built;
    private bool _firstCategoryAutoExpanded;
    private bool _searchExpanded;

    // Category "palette" — icons + accent colors to make the chip bar feel lively
    private static readonly Dictionary<string, (MaterialIconKind? Icon, string Fallback, Color Color)> CategoryStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        { "MEDIA",             (MaterialIconKind.MusicNote,      "\u266B", Color.FromRgb(0x00, 0xE6, 0x76)) },
        { "MUTE",              (MaterialIconKind.VolumeOff,      "\uD83D\uDD07", Color.FromRgb(0xFF, 0x52, 0x52)) },
        { "APP CONTROL",       (MaterialIconKind.AppsBox,        "\u2B21", Color.FromRgb(0x44, 0x8A, 0xFF)) },
        { "DEVICE",            (MaterialIconKind.UsbPort,        "\uD83D\uDD0C", Color.FromRgb(0xB3, 0x88, 0xFF)) },
        { "SYSTEM",            (MaterialIconKind.Cog,            "\u2699", Color.FromRgb(0xFF, 0xD7, 0x40)) },
        { "POWER",             (MaterialIconKind.Power,          "\u23FB", Color.FromRgb(0xFF, 0x52, 0x52)) },
        { "INTEGRATIONS",      (MaterialIconKind.FlashOutline,   "\u26A1", Color.FromRgb(0x26, 0xC6, 0xDA)) },
        { "STREAM CONTROLLER", (MaterialIconKind.ViewGrid,       "\u25A6", Color.FromRgb(0xFF, 0x9E, 0x55)) },
        { "ROOM",              (MaterialIconKind.Lightbulb,      "\u2600", Color.FromRgb(0xE8, 0x6F, 0xFF)) },
        { "ADVANCED",          (MaterialIconKind.Tune,           "\u2699", Color.FromRgb(0x9C, 0x8C, 0xFF)) },
    };

    public QuickActionPicker()
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        MinHeight = 44;
        Padding = new Thickness(10);
        SnapsToDevicePixels = true;
        this.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        this.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        _root = new StackPanel { Orientation = Orientation.Vertical };

        // ── Selected row (SELECTED <icon> <name>) ──────────────────
        // Search trigger lives OUTSIDE the picker — it's rendered next to
        // the ACTION tab in the right-pane tab bar and calls ShowSearch().
        _selectedIcon = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Text = "\u2014",
        };
        _selectedIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        _selectedName = new TextBlock
        {
            Text = "None selected",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _selectedName.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var selectedLabel = new TextBlock
        {
            Text = "SELECTED",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        selectedLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var iconNameStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconNameStack.Children.Add(_selectedIcon);
        iconNameStack.Children.Add(_selectedName);

        var selectedRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        selectedRow.Children.Add(selectedLabel);
        selectedRow.Children.Add(iconNameStack);

        _selectedDisplay = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10),
            Child = selectedRow,
        };
        _selectedDisplay.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        _selectedDisplay.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        _selectedDisplay.BorderThickness = new Thickness(1);

        // Build order below adds: [search box (collapsed)] → SELECTED →
        // options host → (categories / search results). The tab-bar search
        // icon flips the search box visible so it appears at the very top,
        // right under the tab bar — before the SELECTED row.

        // ── Search box (collapsed by default) ─────────────────────
        _searchBoxHost = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 0, 6, 0),
            BorderThickness = new Thickness(1),
            Height = 34,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
        };
        _searchBoxHost.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        _searchBoxHost.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Magnify,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        searchIcon.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        Grid.SetColumn(searchIcon, 0);
        searchGrid.Children.Add(searchIcon);

        _searchBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Padding = new Thickness(0),
        };
        _searchBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _searchBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        Grid.SetColumn(_searchBox, 1);
        searchGrid.Children.Add(_searchBox);

        _searchPlaceholder = new TextBlock
        {
            Text = "Search actions...",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(0),
        };
        _searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        Grid.SetColumn(_searchPlaceholder, 1);
        searchGrid.Children.Add(_searchPlaceholder);

        // Collapse-back "X" so the user can dismiss search without clearing it field-by-field.
        var closeGlyph = new MaterialIcon
        {
            Kind = MaterialIconKind.Close,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeGlyph.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        var closeBtn = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
            Child = closeGlyph,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Close search",
        };
        closeBtn.MouseEnter += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x52, 0x52));
        closeBtn.MouseLeave += (_, _) => closeBtn.Background = System.Windows.Media.Brushes.Transparent;
        closeBtn.MouseLeftButtonUp += (_, e) =>
        {
            _searchBox.Text = "";
            ToggleSearch(false);
            e.Handled = true;
        };
        Grid.SetColumn(closeBtn, 2);
        searchGrid.Children.Add(closeBtn);

        _searchBoxHost.Child = searchGrid;
        _root.Children.Add(_searchBoxHost);

        // SELECTED row under the search box.
        _root.Children.Add(_selectedDisplay);

        // Action-specific options slot — attached directly to the
        // currently-selected action, no separate card chrome.
        _optionsHost = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _root.Children.Add(_optionsHost);

        _searchBox.TextChanged += (_, _) =>
        {
            _searchText = _searchBox.Text.Trim();
            _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            RebuildVisibility();
        };
        _searchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _searchBox.Text = "";
                ToggleSearch(false);
                e.Handled = true;
            }
        };

        // ── Categories (accordion) ────────────────────────────────
        _categoryAccordion = new StackPanel { Orientation = Orientation.Vertical };

        var catsHeader = BuildSectionHeader("BROWSE BY CATEGORY", MaterialIconKind.FormatListBulletedSquare);
        var catsInner = new StackPanel { Orientation = Orientation.Vertical };
        catsInner.Children.Add(catsHeader);
        catsInner.Children.Add(_categoryAccordion);

        _categoriesSection = new Border
        {
            Margin = new Thickness(0, 0, 0, 0),
            Child = catsInner,
        };
        _root.Children.Add(_categoriesSection);

        // ── Search results (replaces categories when searching) ───
        _searchResultsPanel = new StackPanel { Orientation = Orientation.Vertical };
        _searchEmptyText = new TextBlock
        {
            Text = "No actions match your search.",
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(4, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _searchEmptyText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var searchHeader = BuildSectionHeader("RESULTS", MaterialIconKind.Magnify);
        var searchInner = new StackPanel { Orientation = Orientation.Vertical };
        searchInner.Children.Add(searchHeader);
        searchInner.Children.Add(_searchResultsPanel);
        searchInner.Children.Add(_searchEmptyText);

        _searchResultsSection = new Border
        {
            Child = searchInner,
            Visibility = Visibility.Collapsed,
        };
        _root.Children.Add(_searchResultsSection);

        Child = _root;

        // Ensure initial build happens before first render
        Loaded += (_, _) =>
        {
            if (!_built)
            {
                _built = true;
                RebuildAll();
            }
        };
    }

    // ── Public API ──────────────────────────────────────────────────

    public virtual void AddItem(string value, string display, string icon, Color color, string category, string tooltip = "")
    {
        _items.Add(new PickerItem(value, display, icon ?? "", color, category ?? "", tooltip ?? ""));
        // defer UI build — RebuildAll triggered later by Select/Loaded
    }

    public virtual void Select(string value)
    {
        SelectedValue = value;
        UpdateSelectedDisplay();
        EnsureBuilt();
        RebuildAll();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public virtual void ClearItems()
    {
        _items.Clear();
        SelectedValue = "none";
        if (_built) RebuildAll();
    }

    /// <summary>
    /// Panel for action-specific option controls (device pickers, macro
    /// textboxes, path pickers, etc.). Rendered inside the picker right
    /// below the SELECTED row so options feel like they belong to the
    /// chosen action instead of floating below in a separate card.
    /// </summary>
    public StackPanel OptionsHost => _optionsHost;

    // ── Internal build ──────────────────────────────────────────────

    private void EnsureBuilt()
    {
        if (!_built)
        {
            _built = true;
        }
    }

    private void RebuildAll()
    {
        UpdateSelectedDisplay();
        RebuildCategoryAccordion();
        RebuildVisibility();
    }

    private void RebuildVisibility()
    {
        bool searching = !string.IsNullOrEmpty(_searchText);
        if (searching)
        {
            _categoriesSection.Visibility = Visibility.Collapsed;
            _searchResultsSection.Visibility = Visibility.Visible;
            RebuildSearchResults();
        }
        else
        {
            _searchResultsSection.Visibility = Visibility.Collapsed;
            _categoriesSection.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Reveal the search box and focus it so the user can type immediately.
    /// Called by the tab bar's search-icon button so the trigger can live
    /// outside the picker's own chrome.
    /// </summary>
    public void ShowSearch() => ToggleSearch(true);

    private void ToggleSearch(bool show)
    {
        _searchExpanded = show;
        _searchBoxHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }
        else
        {
            _searchBox.Text = "";
            _searchText = "";
            _searchPlaceholder.Visibility = Visibility.Visible;
            RebuildVisibility();
        }
    }

    private void UpdateSelectedDisplay()
    {
        var match = _items.FirstOrDefault(i => i.Value == SelectedValue);
        if (match != null)
        {
            ApplyIcon(_selectedIcon, null, match.Icon, match.Color);
            _selectedName.Text = match.Display;
            _selectedIcon.Visibility = Visibility.Visible;
        }
        else
        {
            _selectedIcon.Text = "\u2014";
            _selectedIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _selectedName.Text = "None selected";
        }
    }

    /// <summary>
    /// Rebuild the accordion of collapsible category panels. Each panel has
    /// a clickable header (icon + category name + chevron) that toggles
    /// visibility of the action rows below. One category auto-expands on
    /// first render so the picker isn't a wall of closed sections on load.
    /// </summary>
    private void RebuildCategoryAccordion()
    {
        _categoryAccordion.Children.Clear();

        var categories = _items.Select(i => i.Category)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Auto-expand first category on first build so users see items
        // without having to hunt for a disclosure.
        if (!_firstCategoryAutoExpanded && categories.Count > 0)
        {
            _categoryExpanded[categories[0]] = true;
            _firstCategoryAutoExpanded = true;
        }

        foreach (var cat in categories)
        {
            _categoryAccordion.Children.Add(BuildCategorySection(cat));
        }
    }

    private Border BuildCategorySection(string category)
    {
        bool expanded = _categoryExpanded.TryGetValue(category, out var e) && e;

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        if (CategoryStyles.TryGetValue(category, out var style) && style.Icon is { } kind)
        {
            var badge = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x22, style.Color.R, style.Color.G, style.Color.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new MaterialIcon
                {
                    Kind = kind,
                    Width = 14, Height = 14,
                    Foreground = new SolidColorBrush(style.Color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            headerStack.Children.Add(badge);
        }

        var nameText = new TextBlock
        {
            Text = category,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        headerStack.Children.Add(nameText);

        var countText = new TextBlock
        {
            Text = $"  ({_items.Count(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase))})",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        headerStack.Children.Add(countText);

        var chevron = new TextBlock
        {
            Text = expanded ? "\u25BC" : "\u25B6",
            FontSize = 9,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerStack, 0);
        Grid.SetColumn(chevron, 1);
        headerGrid.Children.Add(headerStack);
        headerGrid.Children.Add(chevron);

        var headerRow = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
            Child = headerGrid,
        };
        headerRow.MouseEnter += (_, _) => headerRow.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        headerRow.MouseLeave += (_, _) => headerRow.Background = System.Windows.Media.Brushes.Transparent;

        var itemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(8, 4, 0, 6),
            Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
        };
        foreach (var item in _items.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase)))
        {
            itemsPanel.Children.Add(BuildActionRow(item));
        }

        headerRow.MouseLeftButtonUp += (_, e) =>
        {
            bool current = _categoryExpanded.TryGetValue(category, out var v) && v;
            _categoryExpanded[category] = !current;
            itemsPanel.Visibility = !current ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = !current ? "\u25BC" : "\u25B6";
            e.Handled = true;
        };

        var sectionStack = new StackPanel { Orientation = Orientation.Vertical };
        sectionStack.Children.Add(headerRow);
        sectionStack.Children.Add(itemsPanel);

        var section = new Border
        {
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = sectionStack,
        };
        section.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        section.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return section;
    }

    /// <summary>
    /// Wide row for an action — icon badge + name + optional subtitle.
    /// Click to select. No favorite-star; favorites are removed from the UI.
    /// </summary>
    private Border BuildActionRow(PickerItem item)
    {
        bool isSelected = string.Equals(item.Value, SelectedValue, StringComparison.Ordinal);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Colored icon badge.
        var iconBadge = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x22, item.Color.R, item.Color.G, item.Color.B)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, item.Color.R, item.Color.G, item.Color.B)),
        };
        iconBadge.Child = BuildIconVisual(item.Icon, 14, new SolidColorBrush(item.Color));
        Grid.SetColumn(iconBadge, 0);
        grid.Children.Add(iconBadge);

        // Name (+ tooltip as subtitle).
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Text = item.Display,
            FontSize = 12,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        textStack.Children.Add(nameText);
        if (!string.IsNullOrWhiteSpace(item.Tooltip) && item.Tooltip != item.Display)
        {
            var subText = new TextBlock
            {
                Text = item.Tooltip,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            subText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            textStack.Children.Add(subText);
        }
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var row = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B))
                : System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = isSelected
                ? new SolidColorBrush(ThemeManager.Accent)
                : System.Windows.Media.Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 2),
            Child = grid,
        };
        row.MouseEnter += (_, _) =>
        {
            if (!isSelected)
                row.Background = new SolidColorBrush(Color.FromArgb(0x14, item.Color.R, item.Color.G, item.Color.B));
        };
        row.MouseLeave += (_, _) =>
        {
            if (!isSelected)
                row.Background = System.Windows.Media.Brushes.Transparent;
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            SelectedValue = item.Value;
            UpdateSelectedDisplay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            OnActionChosen?.Invoke(item.Value);
            RebuildAll();
            e.Handled = true;
        };
        return row;
    }

    /// <summary>Render an icon string as either a MaterialIcon (if enum-parseable) or a TextBlock glyph.</summary>
    private static UIElement BuildIconVisual(string icon, double size, Brush foreground)
    {
        if (!string.IsNullOrEmpty(icon) && Enum.TryParse<MaterialIconKind>(icon, out var kind))
        {
            return new MaterialIcon
            {
                Kind = kind,
                Width = size, Height = size,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        return new TextBlock
        {
            Text = string.IsNullOrEmpty(icon) ? "\u2022" : icon,
            FontSize = size,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void RebuildSearchResults()
    {
        _searchResultsPanel.Children.Clear();
        var needle = _searchText;
        if (string.IsNullOrEmpty(needle))
        {
            _searchEmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = _items.Where(i =>
            i.Display.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || i.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in matches)
            _searchResultsPanel.Children.Add(BuildActionRow(item));

        _searchEmptyText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── UI builders ─────────────────────────────────────────────────

    private StackPanel BuildSectionHeader(string title, MaterialIconKind icon)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2, 0, 0, 6),
        };

        var ic = new MaterialIcon
        {
            Kind = icon,
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        ic.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        header.Children.Add(ic);

        var lbl = new TextBlock
        {
            Text = title,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        header.Children.Add(lbl);

        return header;
    }

    /// <summary>
    /// Render the action's icon into the appropriate target.
    /// If the icon string parses to a MaterialIconKind, add a MaterialIcon to the grid (column 0) and leave
    /// the fallback TextBlock empty. Otherwise populate the TextBlock directly.
    /// If grid is null, always write to the TextBlock.
    /// </summary>
    private void ApplyIcon(TextBlock target, Grid? grid, string icon, Color color)
    {
        if (!string.IsNullOrEmpty(icon) && grid != null
            && Enum.TryParse<MaterialIconKind>(icon, ignoreCase: true, out var kind))
        {
            var mi = new MaterialIcon
            {
                Kind = kind,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
                Foreground = new SolidColorBrush(color),
            };
            Grid.SetColumn(mi, 0);
            grid.Children.Add(mi);
            target.Text = "";
            return;
        }

        target.Text = string.IsNullOrEmpty(icon) ? "\u2022" : icon;
        target.Foreground = new SolidColorBrush(color);
    }

    // Helpers (allow subclasses to raise events)
    protected void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    protected void RaiseActionChosen(string value) => OnActionChosen?.Invoke(value);
}
