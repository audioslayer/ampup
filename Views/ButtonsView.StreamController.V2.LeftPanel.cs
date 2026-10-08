using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// V2 Left Panel — unified device canvas for the Stream Controller Buttons tab.
///
/// Renders the 6 LCD keys, page navigation, folder banner, and the 3 side
/// buttons + 3 encoders as one cohesive surface built from
/// <see cref="StreamControllerTile"/> cards. Visually matches the Room tab's
/// sleek section aesthetic: slim accent vertical bar + uppercase SemiBold text
/// for each section header.
///
/// Hosts populate the tile lists declared on <c>ButtonsView.StreamController.V2.cs</c>:
///   <c>_v2KeyTiles</c>, <c>_v2ButtonTiles</c>, <c>_v2EncoderTiles</c>
/// so the refresh method can update visuals without rebuilding the tree.
/// </summary>
public partial class ButtonsView
{
    // ── Navigation / container refs (owned by this file) ────────────────────
    private Border? _v2FolderBanner;
    private TextBlock? _v2FolderBannerLabel;
    private Grid? _v2KeyGrid;
    private StackPanel? _v2PageDotsPanel;

    // ── Folders management panel (collapsible, styled like Audio Sessions) ──
    private TextBlock? _v2FolderSectionCount;
    private StackPanel? _v2SpaceListHost;
    private TextBlock? _v2SpaceHint;

    // Cache of tile-level state so we can skip the expensive
    // CreateHardwarePreview + tile.Refresh() rebuild when nothing a tile
    // actually renders has changed. Keyed by the tile's slot index (0-5).
    private readonly Dictionary<int, string> _v2KeyTileStateHash = new();
    private TextBlock? _v2PageLabel;
    private Button? _v2PagePrevButton;
    private Button? _v2PageNextButton;
    private Button? _v2PageAddButton;
    private Button? _v2PageRemoveButton;
    private readonly List<Ellipse> _v2PageDots = new();

    partial void FillV2LeftPanel()
    {
        if (_v2LeftPanel == null) return;

        _v2LeftPanel.Children.Clear();

        // ── Folder banner (hidden unless _scActiveFolder is non-empty) ──────
        _v2FolderBanner = BuildV2FolderBanner();
        _v2LeftPanel.Children.Add(_v2FolderBanner);

        // ── Build the 6 LCD tiles (hosted inside the chassis) ──────────────
        _v2KeyGrid = new Grid();
        for (int c = 0; c < 3; c++)
            _v2KeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 2; r++)
            _v2KeyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _v2KeyTiles.Clear();
        for (int i = 0; i < StreamControllerKeysPerPage; i++)
        {
            int localIdx = i;
            var tile = new StreamControllerTile
            {
                Kind = StreamControllerTile.TileKind.LcdKey,
                Title = $"Key {i + 1}",
                Margin = new Thickness(i % 3 == 0 ? 0 : 6, i >= 3 ? 6 : 0, i % 3 == 2 ? 0 : 6, 0),
                AllowDragDrop = true,
                DragPayload = localIdx,
            };
            tile.OnClick += () => OnV2KeyTileClick(localIdx);
            tile.OnRightClick += e => OnV2KeyTileRightClick(tile, localIdx, e);
            tile.OnTileDropped += source => OnV2KeyTileDrop(source, tile);

            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            _v2KeyGrid.Children.Add(tile);
            _v2KeyTiles.Add(tile);
        }

        // ── HARDWARE device chassis — holds LCDs, page toolbar, buttons, encoders ──
        _v2LeftPanel.Children.Add(BuildV2HardwareDeviceBody());

        // ── FOLDERS (collapsible) — styled like the Mixer's Audio Sessions card ──
        _v2LeftPanel.Children.Add(BuildV2FoldersSection());

        // ── TEMPLATES (collapsible) — pre-built Space layouts the user can add ──
        _v2LeftPanel.Children.Add(BuildV2TemplatesSection());

        // Initial population of visuals.
        RefreshV2LeftPanel();
    }

    /// <summary>
    /// SPACES card in the same list style as TEMPLATES: icon-tile rows with name and
    /// "pages · keys" detail. The Space being edited shows ACTIVE; the others show Open.
    /// Each row's hover "…" menu renames / deletes; "+ New Space" sits at the bottom.
    /// </summary>
    private Border BuildV2FoldersSection()
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
        var header = UiKit.SectionHeader("SPACES", null, out var count);
        _v2FolderSectionCount = count;
        stack.Children.Add(header);

        _v2SpaceListHost = new StackPanel();
        stack.Children.Add(_v2SpaceListHost);

        _v2SpaceHint = UiKit.MutedText("No extra Spaces yet. Create one, add a template below, or right-click any key and choose Open as Space.");
        _v2SpaceHint.TextWrapping = TextWrapping.Wrap;
        _v2SpaceHint.Margin = new Thickness(2, 8, 2, 0);
        stack.Children.Add(_v2SpaceHint);

        section.Child = stack;
        RefreshV2FoldersList();
        return section;
    }

    /// <summary>
    /// Collapsible section header in the shared UiKit language: accent bar, bold accent
    /// label, muted count, then a chevron. The whole row toggles.
    /// </summary>
    private static Border BuildV2CollapsibleHeader(string title, Action toggle,
        out MaterialIcon chevron, out TextBlock countText)
    {
        var header = UiKit.SectionHeader(title, null, out countText);
        header.Margin = new Thickness(0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(header);
        chevron = UiKit.Icon(MaterialIconKind.ChevronDown, 18);
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        var row = new Border
        {
            Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
            Child = grid,
        };
        row.MouseLeftButtonDown += (_, _) => toggle();
        return row;
    }

    // Tile colours for user Spaces, cycled in list order. Home uses the accent.
    private static readonly Color[] V2SpacePalette =
    {
        Color.FromRgb(0x42, 0xA5, 0xF5), Color.FromRgb(0xAB, 0x47, 0xBC), Color.FromRgb(0xFF, 0x70, 0x43),
        Color.FromRgb(0x26, 0xC6, 0xDA), Color.FromRgb(0xEC, 0x40, 0x7A), Color.FromRgb(0xFF, 0xCA, 0x28),
        Color.FromRgb(0x66, 0xBB, 0x6A), Color.FromRgb(0x7E, 0x57, 0xC2),
    };

    /// <summary>Rebuild the Spaces list — called on load, navigation and after create/rename/delete.</summary>
    private void RefreshV2FoldersList()
    {
        if (_v2SpaceListHost == null || _config == null) return;
        if (_v2FolderSectionCount != null)
            _v2FolderSectionCount.Text = (1 + _config.N3.Folders.Count).ToString();

        _v2SpaceListHost.Children.Clear();
        var list = UiKit.ListContainer(out var rows);
        _v2SpaceListHost.Children.Add(list);

        rows.Children.Add(BuildV2SpaceListRow(null, MaterialIconKind.Home, ThemeManager.Accent,
            "Home", V2SpaceDetail(Math.Max(1, _config.N3.PageCount), CountV2HomeKeys())));
        for (int i = 0; i < _config.N3.Folders.Count; i++)
        {
            var folder = _config.N3.Folders[i];
            rows.Children.Add(BuildV2SpaceListRow(folder, MaterialIconKind.ViewDashboardOutline,
                V2SpacePalette[i % V2SpacePalette.Length], folder.Name,
                V2SpaceDetail(Math.Max(1, folder.PageCount), CountV2FolderKeys(folder))));
        }
        rows.Children.Add(UiKit.LinkRow(MaterialIconKind.Plus, "New Space", accent: true, CreateV2Space,
            "Create an empty Space with its own pages of keys"));

        if (_v2SpaceHint != null)
            _v2SpaceHint.Visibility = _config.N3.Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One Templates-style row. folder == null is Home (can't be renamed or deleted).</summary>
    private Border BuildV2SpaceListRow(ButtonFolderConfig? folder, MaterialIconKind icon, Color color, string name, string detail)
    {
        string folderName = folder?.Name ?? "";
        bool isActive = string.Equals(_scActiveFolder ?? "", folderName, StringComparison.Ordinal);

        var tile = UiKit.IconTile(color, icon, 30);
        FrameworkElement right;
        if (isActive)
        {
            var badge = UiKit.StatusBadge(out var setBadge);
            setBadge("ACTIVE", true);
            badge.Margin = new Thickness(0, 0, 6, 0);
            right = badge;
        }
        else
        {
            right = UiKit.LinkRow(MaterialIconKind.ArrowRight, "Open", accent: true,
                () => NavigateToFolderInEditor(folderName), $"Edit {name}");
        }

        FrameworkElement? more = folder == null ? null : UiKit.MoreButton(() => new[]
        {
            new GlassMenuItem("Rename", MaterialIconKind.PencilOutline, () => RenameV2Space(folder)),
            GlassMenuItem.Sep,
            new GlassMenuItem("Delete", MaterialIconKind.TrashCanOutline, () => DeleteV2Space(folder), IsDanger: true),
        }, $"{name} options");

        var row = UiKit.ListRow(tile, name, detail, right, more);
        if (isActive)
            row.Background = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x14));
        else
        {
            row.Cursor = Cursors.Hand;
            row.ToolTip = $"Edit {name}";
            row.MouseLeftButtonUp += (_, e) => { if (!e.Handled) NavigateToFolderInEditor(folderName); };
        }
        return row;
    }

    private void RenameV2Space(ButtonFolderConfig folder)
    {
        var name = GlassDialog.Prompt("New name for this Space:", "Rename Space", Window.GetWindow(this), folder.Name);
        if (name != null) CommitV2SpaceRename(folder, name);
    }

    private void CreateV2Space()
    {
        if (_config == null) return;
        string? name = GlassDialog.Prompt("Enter a name for the new Space:", "New Space", Window.GetWindow(this));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (_config.N3.Folders.Any(f => f.Name == name))
        {
            int counter = 2;
            string candidate;
            do { candidate = $"{name} ({counter++})"; }
            while (_config.N3.Folders.Any(f => f.Name == candidate));
            name = candidate;
        }
        var folder = new ButtonFolderConfig { Name = name, PageCount = 1 };
        for (int i = 0; i < StreamControllerKeysPerPage; i++)
        {
            folder.DisplayKeys.Add(new StreamControllerDisplayKeyConfig { Idx = i });
            folder.Buttons.Add(new ButtonConfig { Idx = StreamControllerDisplayKeyBase + i });
        }
        _config.N3.Folders.Add(folder);
        QueueSave();
        RefreshV2FoldersList();
    }

    private static string V2SpaceDetail(int pageCount, int keyCount) =>
        $"{pageCount} page{(pageCount == 1 ? "" : "s")} · {keyCount} key{(keyCount == 1 ? "" : "s")} assigned";

    /// <summary>
    /// Keys in use on Home — reads the root _config.N3 DisplayKeys/Buttons. A slot counts if it
    /// has any title / subtitle / icon / action / non-default display type.
    /// </summary>
    private int CountV2HomeKeys()
    {
        var assignedSlots = new HashSet<int>();
        foreach (var k in _config!.N3.DisplayKeys)
        {
            if (!string.IsNullOrEmpty(k.Title)
                || !string.IsNullOrEmpty(k.Subtitle)
                || !string.IsNullOrEmpty(k.ImagePath)
                || !string.IsNullOrEmpty(k.PresetIconKind)
                || k.DisplayType != DisplayKeyType.Normal)
                assignedSlots.Add(k.Idx);
        }
        foreach (var b in _config.N3.Buttons)
        {
            if (b.Idx < StreamControllerDisplayKeyBase) continue;
            if (b.Idx >= StreamControllerSideButtonBase) continue;
            if (!string.IsNullOrEmpty(b.Action) && b.Action != "none")
                assignedSlots.Add(b.Idx - StreamControllerDisplayKeyBase);
        }
        return assignedSlots.Count;
    }

    private static int CountV2FolderKeys(ButtonFolderConfig folder)
    {
        var assignedSlots = new HashSet<int>();
        foreach (var k in folder.DisplayKeys)
        {
            if (!string.IsNullOrEmpty(k.Title)
                || !string.IsNullOrEmpty(k.Subtitle)
                || !string.IsNullOrEmpty(k.ImagePath)
                || !string.IsNullOrEmpty(k.PresetIconKind)
                || k.DisplayType != DisplayKeyType.Normal)
                assignedSlots.Add(k.Idx);
        }
        foreach (var b in folder.Buttons)
        {
            if (!string.IsNullOrEmpty(b.Action) && b.Action != "none")
                assignedSlots.Add(b.Idx - StreamControllerDisplayKeyBase);
        }
        return assignedSlots.Count;
    }

    private void CommitV2SpaceRename(ButtonFolderConfig folder, string? proposed)
    {
        if (_config == null) return;
        var newName = (proposed ?? "").Trim();
        if (string.IsNullOrEmpty(newName) || newName == folder.Name)
        {
            RefreshV2FoldersList();
            return;
        }
        if (_config.N3.Folders.Any(f => f.Name == newName && f != folder))
        {
            RefreshV2FoldersList();
            return;
        }

        var oldName = folder.Name;
        folder.Name = newName;
        foreach (var btn in _config.N3.Buttons)
            if (btn.Action == "open_folder" && btn.FolderName == oldName)
                btn.FolderName = newName;
        foreach (var f in _config.N3.Folders)
            foreach (var btn in f.Buttons)
                if (btn.Action == "open_folder" && btn.FolderName == oldName)
                    btn.FolderName = newName;
        if (_scActiveFolder == oldName) _scActiveFolder = newName;

        QueueSave();
        RefreshV2FoldersList();
        RefreshV2LeftPanel();
    }

    private void DeleteV2Space(ButtonFolderConfig folder)
    {
        if (_config == null) return;
        if (!GlassDialog.Confirm($"Delete Space \"{folder.Name}\" and all its keys?", "Delete Space", dangerYes: true, owner: Window.GetWindow(this)))
            return;

        _config.N3.Folders.Remove(folder);

        foreach (var btn in _config.N3.Buttons)
            if (btn.Action == "open_folder" && btn.FolderName == folder.Name)
            {
                btn.Action = "none";
                btn.FolderName = "";
            }
        foreach (var f in _config.N3.Folders)
            foreach (var btn in f.Buttons)
                if (btn.Action == "open_folder" && btn.FolderName == folder.Name)
                {
                    btn.Action = "none";
                    btn.FolderName = "";
                }
        if (_scActiveFolder == folder.Name)
            NavigateToFolderInEditor("");

        QueueSave();
        RefreshV2FoldersList();
        RefreshV2LeftPanel();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Builders
    // ────────────────────────────────────────────────────────────────────────

    private Border BuildV2FolderBanner()
    {
        // Breadcrumb-style banner — matches the app's Material underline
        // tab pattern. Reads as: [← ROOT]  ›  📂 <folder name>
        // with a hairline underline below. No card chrome, no coloured
        // fill — just typography + a subtle accent line, same feel as
        // the DESIGN / ACTION tab bar in the right pane.
        var banner = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        banner.SetResourceReference(Border.BorderBrushProperty, "InputBgBrush");

        var stack = new StackPanel();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        // Left breadcrumb crumb: "← ROOT" — clickable, hover brightens.
        var rootChevron = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronLeft,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rootChevron.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");

        var rootLabel = new TextBlock
        {
            Text = "HOME",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        rootLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var rootContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rootContent.Children.Add(rootChevron);
        rootContent.Children.Add(rootLabel);

        var rootBtn = new Border
        {
            Padding = new Thickness(8, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
            Child = rootContent,
            ToolTip = "Back to Home",
        };
        rootBtn.MouseEnter += (_, _) =>
        {
            rootBtn.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            rootLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            rootChevron.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        };
        rootBtn.MouseLeave += (_, _) =>
        {
            rootBtn.Background = System.Windows.Media.Brushes.Transparent;
            rootLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            rootChevron.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        };
        rootBtn.MouseLeftButtonUp += (_, e) =>
        {
            NavigateToFolderInEditor("");
            e.Handled = true;
        };
        row.Children.Add(rootBtn);

        // Separator chevron.
        var sep = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronRight,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };
        sep.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        row.Children.Add(sep);

        // Current Space: accent dashboard icon + accent name (bold).
        var folderIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.ViewDashboardOutline,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(4, 0, 0, 0),
        };
        row.Children.Add(folderIcon);

        _v2FolderBannerLabel = new TextBlock
        {
            Text = "",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(_v2FolderBannerLabel);

        stack.Children.Add(row);
        banner.Child = stack;
        return banner;
    }

    /// <summary>Slim accent bar + uppercase label (matches Room tab style).</summary>
    private StackPanel BuildV2SectionHeader(string title)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        row.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 10, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private StackPanel BuildV2PageToolbar()
    {
        // Bottom toolbar now owns only navigation (prev / dots / label /
        // next). The add/remove buttons moved to BuildV2PageAddRemoveRow
        // which sits above the key grid.
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 4),
        };

        _v2PagePrevButton = MakeV2ToolbarGlyphButton("\u276E", "Previous page",
            () => NavigateStreamControllerPage(-1));
        toolbar.Children.Add(_v2PagePrevButton);

        _v2PageDotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        toolbar.Children.Add(_v2PageDotsPanel);

        _v2PageLabel = new TextBlock
        {
            Text = "Page 1 of 1",
            FontSize = 11,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        toolbar.Children.Add(_v2PageLabel);

        _v2PageNextButton = MakeV2ToolbarGlyphButton("\u276F", "Next page",
            () => NavigateStreamControllerPage(1));
        toolbar.Children.Add(_v2PageNextButton);

        return toolbar;
    }

    /// <summary>
    /// Compact "PAGES  + -" row that sits above the key grid so the
    /// add/remove affordances are easy to reach without scrolling past
    /// the nav controls below.
    /// </summary>
    private StackPanel BuildV2PageAddRemoveRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        row.Children.Add(new TextBlock
        {
            Text = "PAGES",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        _v2PageAddButton = MakeV2ToolbarTextButton("+", "Add page",
            () => AddStreamControllerPage());
        row.Children.Add(_v2PageAddButton);

        _v2PageRemoveButton = MakeV2ToolbarTextButton("\u2212", "Remove last page",
            () => RemoveStreamControllerPage());
        _v2PageRemoveButton.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(_v2PageRemoveButton);

        return row;
    }

    private Button MakeV2ToolbarGlyphButton(string glyph, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                Foreground = FindBrush("TextSecBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };
        btn.MouseEnter += (_, _) =>
        {
            if (btn.Content is TextBlock t) t.Foreground = new SolidColorBrush(ThemeManager.Accent);
        };
        btn.MouseLeave += (_, _) =>
        {
            if (btn.Content is TextBlock t) t.Foreground = FindBrush("TextSecBrush");
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button MakeV2ToolbarTextButton(string text, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Click handlers
    // ────────────────────────────────────────────────────────────────────────

    private void OnV2KeyTileClick(int localIdx)
    {
        // In folder context, slot 0 is the reserved (auto) Back key — read-only.
        if (IsBackKeyShown && localIdx == 0) return;

        int folderSlotOffset = IsBackKeyShown ? -1 : 0;
        int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + localIdx + folderSlotOffset;
        int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
        // Display name follows physical slot (1-6) — matches the tile label
        // and what the user sees on the device.
        SelectStreamControllerItem(new StreamControllerSelection(
            buttonIdx,
            $"Key {localIdx + 1}",
            globalIdx));
    }

    private void OnV2KeyTileRightClick(StreamControllerTile tile, int localIdx, MouseButtonEventArgs e)
    {
        if (IsBackKeyShown && localIdx == 0)
        {
            ShowBackKeyContextMenu(tile);
            e.Handled = true;
            return;
        }
        int folderSlotOffset = IsBackKeyShown ? -1 : 0;
        int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + localIdx + folderSlotOffset;
        ShowKeyContextMenu(tile, globalIdx);
        e.Handled = true;
    }

    /// <summary>
    /// Right-click menu for the auto Back key. Only offers "Remove Back
    /// Key" — positioning Back at other slots isn't supported; the key
    /// always renders at slot 0 of page 0 when enabled.
    /// </summary>
    private void ShowBackKeyContextMenu(StreamControllerTile tile)
    {
        var folder = ActiveFolder;
        if (folder == null) return;

        var items = new List<GlassMenuItem>
        {
            new("Remove Back Key",
                Material.Icons.MaterialIconKind.TrashCanOutline,
                () =>
                {
                    folder.BackKeyEnabled = false;
                    _v2KeyTileStateHash.Clear();
                    RefreshV2LeftPanel();
                    QueueSave();
                    (Application.Current as App)?.NavigateToN3Folder(_scActiveFolder);
                },
                IsDanger: true),
        };
        GlassContextMenuHost.Show(tile, items);
    }

    /// <summary>Accessors for the page layout — respect BackKeyEnabled for the active folder.</summary>
    private bool IsBackKeyShown
        => InFolderContext && _scCurrentPage == 0 && (ActiveFolder?.BackKeyEnabled ?? true);

    /// <summary>
    /// Drag-and-drop handler: swap the display-key + button config between
    /// the source tile and the target tile. Blocked for the folder Back
    /// slot (index 0 inside a Space) since it's virtual/read-only. Both
    /// tiles must be on the current page since the `localIdx` payload is
    /// relative to what's on screen.
    /// </summary>
    private void OnV2KeyTileDrop(StreamControllerTile source, StreamControllerTile target)
    {
        if (_config == null) return;
        if (source.DragPayload is not int srcLocalIdx) return;
        if (target.DragPayload is not int dstLocalIdx) return;
        if (srcLocalIdx == dstLocalIdx) return;

        // Block the auto Back key on either end.
        if (IsBackKeyShown && (srcLocalIdx == 0 || dstLocalIdx == 0)) return;

        int folderSlotOffset = IsBackKeyShown ? -1 : 0;
        int srcGlobal = _scCurrentPage * StreamControllerKeysPerPage + srcLocalIdx + folderSlotOffset;
        int dstGlobal = _scCurrentPage * StreamControllerKeysPerPage + dstLocalIdx + folderSlotOffset;

        SwapV2DisplayKey(srcGlobal, dstGlobal);

        // Keep the user's focus on whichever tile they dropped on —
        // usually they want to edit the newly-arrived content next.
        _scSelectedButtonIdx = StreamControllerDisplayKeyBase + dstGlobal;
        LoadStreamControllerConfig();
        RefreshV2LeftPanel();
        RefreshV2RightPanel();
        QueueSave();
    }

    /// <summary>
    /// Swap display-key + button configs between two slots in the active
    /// Space. Works by rewriting the Idx fields on the underlying records
    /// so references from anywhere else stay consistent and the re-sync
    /// to the device re-draws both slots.
    /// </summary>
    private void SwapV2DisplayKey(int srcGlobalIdx, int dstGlobalIdx)
    {
        if (_config == null) return;
        var keys = GetActiveN3DisplayKeys();
        var btns = GetActiveN3ButtonList();

        var srcKey = keys.FirstOrDefault(k => k.Idx == srcGlobalIdx);
        var dstKey = keys.FirstOrDefault(k => k.Idx == dstGlobalIdx);
        var srcBtn = btns.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + srcGlobalIdx);
        var dstBtn = btns.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + dstGlobalIdx);

        // Ensure both slots have entries so the swap is symmetric.
        if (srcKey == null) { srcKey = new StreamControllerDisplayKeyConfig { Idx = srcGlobalIdx }; keys.Add(srcKey); }
        if (dstKey == null) { dstKey = new StreamControllerDisplayKeyConfig { Idx = dstGlobalIdx }; keys.Add(dstKey); }
        if (srcBtn == null) { srcBtn = new ButtonConfig { Idx = StreamControllerDisplayKeyBase + srcGlobalIdx }; btns.Add(srcBtn); }
        if (dstBtn == null) { dstBtn = new ButtonConfig { Idx = StreamControllerDisplayKeyBase + dstGlobalIdx }; btns.Add(dstBtn); }

        srcKey.Idx = dstGlobalIdx;
        dstKey.Idx = srcGlobalIdx;
        srcBtn.Idx = StreamControllerDisplayKeyBase + dstGlobalIdx;
        dstBtn.Idx = StreamControllerDisplayKeyBase + srcGlobalIdx;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Refresh — re-reads config and repaints all tiles.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads config and updates every tile's Title / Subtitle / PreviewImage /
    /// IsSelected, then calls <see cref="StreamControllerTile.Refresh"/> on each.
    /// Also refreshes the folder banner + paging toolbar.
    /// </summary>
    public void RefreshV2LeftPanel()
    {
        if (_v2LeftPanel == null) return;

        RefreshV2FolderBanner();
        RefreshV2KeyTiles();
        RefreshV2PageToolbar();
        RefreshV2HardwareTiles();
        // Rebuild the Spaces list so the ACTIVE pill / accent border
        // follow _scActiveFolder as the user navigates between Spaces.
        RefreshV2FoldersList();
        RefreshV2MiniMap();
    }

    private void RefreshV2FolderBanner()
    {
        if (_v2FolderBanner == null || _v2FolderBannerLabel == null) return;
        if (InFolderContext)
        {
            _v2FolderBanner.Visibility = Visibility.Visible;
            _v2FolderBannerLabel.Text = _scActiveFolder ?? "";
        }
        else
        {
            _v2FolderBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshV2KeyTiles()
    {
        if (_v2KeyTiles.Count == 0) return;

        var activeKeys = GetActiveN3DisplayKeys();
        var activeButtons = GetActiveN3ButtonList();
        int folderSlotOffset = IsBackKeyShown ? -1 : 0;
        var spotifySpanMasters = new Dictionary<int, StreamControllerDisplayKeyConfig>();
        if (!IsBackKeyShown)
        {
            foreach (var candidate in activeKeys
                         .Where(StreamControllerDisplayRenderer.IsSpotifyAlbumArtSpanned)
                         .OrderBy(k => k.Idx))
            {
                foreach (int coveredSlot in StreamControllerDisplayRenderer.GetSpotifyAlbumArtCoveredSlots(candidate))
                {
                    if (!spotifySpanMasters.ContainsKey(coveredSlot))
                        spotifySpanMasters[coveredSlot] = candidate;
                }
            }
        }

        for (int i = 0; i < _v2KeyTiles.Count; i++)
        {
            var tile = _v2KeyTiles[i];

            // When Back is enabled in the active Space, slot 0 on page 0
            // previews the auto Back key (right-click to remove it).
            if (IsBackKeyShown && i == 0)
            {
                string backHash = "back|selected=false";
                if (!_v2KeyTileStateHash.TryGetValue(i, out var prevHash) || prevHash != backHash)
                {
                    var backKey = App.BuildBackKeyDisplay();
                    tile.PreviewImage = StreamControllerDisplayRenderer.CreateEditorPreview(backKey, 160);
                    tile.Title = "Back";
                    tile.Subtitle = "Auto";
                    tile.IsSelected = false;
                    tile.Opacity = 0.85;
                    tile.Cursor = Cursors.Hand;
                    tile.ToolTip = "Automatic Back key \u2014 right-click to remove.";
                    tile.Refresh();
                    _v2KeyTileStateHash[i] = backHash;
                }
                continue;
            }

            int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + i + folderSlotOffset;
            int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;

            var key = activeKeys.FirstOrDefault(k => k.Idx == globalIdx)
                      ?? new StreamControllerDisplayKeyConfig { Idx = globalIdx };
            var button = activeButtons.FirstOrDefault(b => b.Idx == buttonIdx);
            spotifySpanMasters.TryGetValue(i, out var spotifySpanMaster);
            bool isSelected = _scSelectedButtonIdx == buttonIdx;
            if (key.DisplayType == DisplayKeyType.DynamicState
                && string.IsNullOrWhiteSpace(key.DynamicStateSource))
            {
                string derived = DynamicKeyStateProvider.DeriveSourceFromAction(button?.Action);
                if (!string.IsNullOrWhiteSpace(derived))
                    key.DynamicStateSource = derived;
            }
            bool dynamicActive = false;
            if (key.DisplayType == DisplayKeyType.DynamicState
                && !string.IsNullOrWhiteSpace(key.DynamicStateSource))
            {
                try
                {
                    dynamicActive = StreamControllerDisplayRenderer.DynamicStateResolver?.Invoke(key.DynamicStateSource) ?? false;
                }
                catch
                {
                    dynamicActive = false;
                }
            }

            string spotifyStateHash = "";
            string hardwareStateHash = "";
            var visualKey = spotifySpanMaster ?? key;
            bool drawSpotifySpanTitle = spotifySpanMaster == null
                || StreamControllerDisplayRenderer.ShouldDrawSpotifySpanTitle(
                    spotifySpanMaster, activeKeys, _scCurrentPage * StreamControllerKeysPerPage);
            if (visualKey.DisplayType == DisplayKeyType.SpotifyNowPlaying)
            {
                var spotifyInfo = StreamControllerDisplayRenderer.SpotifyNowPlayingTitleProvider?.Invoke() ?? ("", "");
                spotifyStateHash = $"{visualKey.Idx}|{StreamControllerDisplayRenderer.SpotifyNowPlayingImagePath}|{spotifyInfo.Title}|{spotifyInfo.Subtitle}|{visualKey.SpotifyAlbumArtLayout}|{visualKey.BackgroundColor}|{visualKey.Brightness}|{drawSpotifySpanTitle}|{i}";
            }
            if (visualKey.DisplayType == DisplayKeyType.HardwareMonitor)
            {
                var metricInfo = StreamControllerDisplayRenderer.HardwareMetricProvider?.Invoke(visualKey.HardwareMetricSource, visualKey.HardwareGaugeMax);
                hardwareStateHash = $"{visualKey.HardwareMetricSource}|{visualKey.HardwareMetricLabel}|{visualKey.HardwareMetricLabelSize}|{visualKey.HardwareMetricLabelColor}|{visualKey.HardwareMetricLayout}|{visualKey.HardwareGaugeMax}|{visualKey.HardwareGaugeColorByValue}|{metricInfo?.Label}|{metricInfo?.ValueText}|{metricInfo?.IsAvailable}|{(int)((metricInfo?.GaugeFraction ?? 0f) * 100)}";
            }

            // Compose a hash from fields the tile actually renders — skip
            // CreateHardwarePreview + tile.Refresh() when nothing changed.
            string hash = $"{key.Title}|{key.ImagePath}|{key.PresetIconKind}|{key.TextPosition}|{key.TextSize}|{key.TextColor}|{key.IconColor}|{key.FontFamily}|{key.Brightness}|{key.BackgroundColor}|{key.AccentColor}|{key.DisplayType}|{key.ClockFormat}|{key.DynamicStateSource}|{key.DynamicStateActiveIcon}|{key.DynamicStateActiveTitle}|{key.DynamicStateInactiveBrightness}|{key.DynamicStateDimWhenActive}|{key.DynamicStateGlowColor}|{key.SpotifyAlbumArtLayout}|{key.HardwareMetricSource}|{key.HardwareMetricLabel}|{key.HardwareMetricLabelSize}|{key.HardwareMetricLabelColor}|{key.HardwareMetricLayout}|{spotifyStateHash}|{hardwareStateHash}|{dynamicActive}|{button?.Action}|{isSelected}";
            if (_v2KeyTileStateHash.TryGetValue(i, out var lastHash) && lastHash == hash)
                continue;

            tile.Opacity = 1.0;
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = null;
            if (spotifySpanMaster != null)
            {
                tile.PreviewImage = StreamControllerDisplayRenderer.CreateSpotifyAlbumArtTilePreview(
                    spotifySpanMaster, key, i, 160, drawSpotifySpanTitle);
                tile.PreviewAnimation = null;
                tile.PreviewAnimationSignature = $"spotify-span|{spotifySpanMaster.Idx}|{spotifyStateHash}|160";
            }
            else
            {
                tile.PreviewImage = StreamControllerDisplayRenderer.CreateEditorPreview(key, 160);
                tile.PreviewAnimation = StreamControllerDisplayRenderer.CreateEditorPreviewAnimation(key, 160);
                tile.PreviewAnimationSignature = StreamControllerDisplayRenderer
                    .CreateEditorPreviewAnimationSignature(key, 160);
            }
            // Label by physical slot (1-6, top-left → bot-right) so the user's
            // "top-right = Key 3" mental model holds regardless of whether the
            // Back key is shown. globalIdx+1 leaked the storage index into the
            // UI and made unbound keys land on the wrong physical button.
            tile.Title = string.IsNullOrWhiteSpace(key.Title) ? $"Key {i + 1}" : key.Title;
            tile.Subtitle = GetStreamActionDisplay(button);
            tile.IsSelected = isSelected;
            tile.Refresh();
            _v2KeyTileStateHash[i] = hash;
        }
    }

    private void RefreshV2PageToolbar()
    {
        if (_v2PageDotsPanel == null) return;

        _v2PageDotsPanel.Children.Clear();
        _v2PageDots.Clear();
        for (int i = 0; i < _scPageCount; i++)
        {
            int targetPage = i;
            var dot = new Ellipse
            {
                Width = i == _scCurrentPage ? 9 : 7,
                Height = i == _scCurrentPage ? 9 : 7,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = i == _scCurrentPage
                    ? new SolidColorBrush(ThemeManager.Accent)
                    : FindBrush("TextDimBrush"),
                Cursor = Cursors.Hand,
                ToolTip = $"Page {i + 1}",
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                if (targetPage != _scCurrentPage)
                    NavigateStreamControllerPage(targetPage - _scCurrentPage);
            };
            _v2PageDots.Add(dot);
            _v2PageDotsPanel.Children.Add(dot);
        }

        if (_v2PageLabel != null)
            _v2PageLabel.Text = $"Page {_scCurrentPage + 1} of {_scPageCount}";

        var dimBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        if (_v2PagePrevButton?.Content is TextBlock lt)
            lt.Foreground = _scCurrentPage > 0 ? FindBrush("TextSecBrush") : dimBrush;
        if (_v2PageNextButton?.Content is TextBlock rt)
            rt.Foreground = _scCurrentPage < _scPageCount - 1 ? FindBrush("TextSecBrush") : dimBrush;

        if (_v2PageRemoveButton != null)
            _v2PageRemoveButton.IsEnabled = _scPageCount > 1;
    }
}
