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
    private TextBlock? _v2FolderSectionArrow;
    private StackPanel? _v2FolderSectionContent;
    private bool _v2FoldersExpanded;

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

    private Border BuildV2FoldersSection()
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

        // Clickable header — accent bar + label + chevron, matches AUDIO SESSIONS.
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
        var headerLabel = new TextBlock
        {
            Text = "SPACES",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerStack.Children.Add(headerLabel);
        _v2FolderSectionArrow = new TextBlock
        {
            Text = "\u25B6",
            FontSize = 9,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        headerStack.Children.Add(_v2FolderSectionArrow);
        headerRow.Child = headerStack;
        headerRow.MouseLeftButtonDown += (_, _) => ToggleV2FoldersExpanded();
        stack.Children.Add(headerRow);

        _v2FolderSectionContent = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0),
        };
        stack.Children.Add(_v2FolderSectionContent);

        section.Child = stack;
        return section;
    }

    private void ToggleV2FoldersExpanded()
    {
        _v2FoldersExpanded = !_v2FoldersExpanded;
        if (_v2FolderSectionContent != null)
            _v2FolderSectionContent.Visibility = _v2FoldersExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_v2FolderSectionArrow != null)
            _v2FolderSectionArrow.Text = _v2FoldersExpanded ? "\u25BC" : "\u25B6";

        if (_v2FoldersExpanded) RefreshV2FoldersList();
    }

    /// <summary>Rebuild the folder list inside the FOLDERS card — called on expand and after create/rename/delete.</summary>
    private void RefreshV2FoldersList()
    {
        if (_v2FolderSectionContent == null || _config == null) return;
        _v2FolderSectionContent.Children.Clear();

        // "+ New Space" action row at the top.
        var newBtn = MakeEditorButton("+ New Space", (_, _) =>
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
        });
        newBtn.Margin = new Thickness(0, 0, 0, 8);
        _v2FolderSectionContent.Children.Add(newBtn);

        // Home always appears at the top — it's the default Space and is
        // always present. Rename/Delete are hidden since it's permanent.
        _v2FolderSectionContent.Children.Add(BuildV2HomeRow());

        if (_config.N3.Folders.Count == 0)
        {
            _v2FolderSectionContent.Children.Add(new TextBlock
            {
                Text = "No extra Spaces yet. Create one above or right-click any key → Open as Space.",
                FontSize = 11,
                Foreground = FindBrush("TextDimBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 0),
            });
            return;
        }

        foreach (var folder in _config.N3.Folders)
        {
            _v2FolderSectionContent.Children.Add(BuildV2FolderRow(folder));
        }
    }

    /// <summary>
    /// Row for the default "Home" Space — always present, can't be
    /// renamed or deleted. Reads the root _config.N3 DisplayKeys/Buttons
    /// for its count so it mirrors what's really there.
    /// </summary>
    private Border BuildV2HomeRow()
    {
        bool isActive = string.IsNullOrEmpty(_scActiveFolder);

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
        int keyCount = assignedSlots.Count;
        int pageCount = Math.Max(1, _config.N3.PageCount);

        return BuildV2SpaceRow(
            folder: null,
            isActive: isActive,
            iconKind: MaterialIconKind.Home,
            pageCount: pageCount,
            keyCount: keyCount,
            onActivate: () => NavigateToFolderInEditor(""));
    }

    private Border BuildV2FolderRow(ButtonFolderConfig folder)
    {
        bool isActive = string.Equals(_scActiveFolder, folder.Name, StringComparison.Ordinal);

        // Dedup assigned-slot counting — a slot counts if it has any title /
        // subtitle / icon / action / non-default display type.
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

        return BuildV2SpaceRow(
            folder: folder,
            isActive: isActive,
            iconKind: MaterialIconKind.ViewDashboardOutline,
            pageCount: folder.PageCount,
            keyCount: assignedSlots.Count,
            onActivate: () => NavigateToFolderInEditor(folder.Name));
    }

    /// <summary>
    /// Shared builder for Home + user-Space rows. The whole card is
    /// clickable to activate; the name is an inline-editable TextBox
    /// (matching the preview-header rename pattern), and delete is a
    /// small icon button. Home passes folder=null and skips both.
    /// </summary>
    private Border BuildV2SpaceRow(
        ButtonFolderConfig? folder,
        bool isActive,
        MaterialIconKind iconKind,
        int pageCount,
        int keyCount,
        Action onActivate)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            Cursor = isActive ? Cursors.Arrow : Cursors.Hand,
        };
        row.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        if (isActive)
        {
            row.BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x88, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
        }
        else
        {
            row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new MaterialIcon
        {
            Kind = iconKind,
            Width = 14, Height = 14,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        // Home: non-editable bold label. User Space: inline-editable TextBox
        // that mirrors the preview-header rename pattern.
        if (folder == null)
        {
            nameRow.Children.Add(new TextBlock
            {
                Text = "Home",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            var nameBox = new TextBox
            {
                Text = folder.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                CaretBrush = FindBrush("AccentBrush"),
                SelectionBrush = FindBrush("AccentDimBrush"),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MaxLength = 30,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.IBeam,
                ToolTip = "Click to rename",
            };
            nameBox.GotFocus += (_, _) =>
            {
                nameBox.Background = FindBrush("InputBgBrush");
                nameBox.BorderThickness = new Thickness(0, 0, 0, 1);
                nameBox.BorderBrush = FindBrush("AccentBrush");
                nameBox.SelectAll();
            };
            nameBox.LostFocus += (_, _) =>
            {
                nameBox.Background = System.Windows.Media.Brushes.Transparent;
                nameBox.BorderThickness = new Thickness(0);
                CommitV2SpaceRename(folder, nameBox.Text);
            };
            nameBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    nameBox.Text = folder.Name;
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            nameRow.Children.Add(nameBox);
        }

        if (isActive)
        {
            nameRow.Children.Add(new TextBlock
            {
                Text = "  ACTIVE",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeManager.Accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 1, 0, 0),
            });
        }
        labelStack.Children.Add(nameRow);

        labelStack.Children.Add(new TextBlock
        {
            Text = $"{pageCount} page{(pageCount == 1 ? "" : "s")} · {keyCount} key{(keyCount == 1 ? "" : "s")} assigned",
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(22, 2, 0, 0),
        });
        Grid.SetColumn(labelStack, 0);
        grid.Children.Add(labelStack);

        // Delete — user Spaces only. Small icon button; no label chrome.
        if (folder != null)
        {
            var delIcon = new MaterialIcon
            {
                Kind = MaterialIconKind.TrashCanOutline,
                Width = 16, Height = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
            };
            var delBtn = new Button
            {
                Content = delIcon,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"Delete {folder.Name}",
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            };
            delBtn.MouseEnter += (_, _) => delIcon.Foreground =
                new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            delBtn.MouseLeave += (_, _) => delIcon.Foreground =
                new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            delBtn.Click += (_, e) =>
            {
                e.Handled = true;
                DeleteV2Space(folder);
            };
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);
        }

        row.Child = grid;

        // Click anywhere on the row (except the name editor or delete icon)
        // activates the Space. Routed-event OriginalSource lets us skip
        // clicks that originated in the name TextBox or the delete button.
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled || isActive) return;
            if (IsMouseEventFromEditableChild(e.OriginalSource as DependencyObject)) return;
            onActivate();
        };

        return row;
    }

    private static bool IsMouseEventFromEditableChild(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TextBox || source is Button) return true;
            source = VisualTreeHelper.GetParent(source) ?? (source is FrameworkElement fe ? fe.Parent : null);
        }
        return false;
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

    // Device-body control refs — kept so RefreshV2ButtonHardware can update labels.
    private readonly List<Border> _v2HwButtonCaps = new();
    private readonly List<TextBlock> _v2HwButtonSubs = new();
    private readonly List<Border> _v2HwEncoderRings = new();
    private readonly List<TextBlock> _v2HwEncoderSubs = new();

    private Border BuildV2HardwareDeviceBody()
    {
        // Outer chassis — rounded dark body with subtle bevel. Hosts every
        // interactive part of the N3 so the whole device reads as one unit:
        // screens on top, page toolbar underneath, then physical controls.
        var chassis = new Border
        {
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(28, 24, 28, 24),
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x14, 0x17, 0x1C),
                Color.FromRgb(0x0A, 0x0C, 0x10),
                new Point(0.5, 0), new Point(0.5, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.6,
            },
        };

        var body = new StackPanel();

        // Add / remove page row — sits above the grid so the primary
        // affordance for growing a Space is in the user's sight line.
        body.Children.Add(BuildV2PageAddRemoveRow());

        // Screens (2x3 LCD grid). _v2KeyGrid was assembled in FillV2LeftPanel.
        if (_v2KeyGrid != null)
        {
            _v2KeyGrid.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(_v2KeyGrid);
        }

        // Page toolbar — tucked under the screens inside the chassis.
        var pageBar = BuildV2PageToolbar();
        pageBar.Margin = new Thickness(0, 0, 0, 16);
        body.Children.Add(pageBar);

        // Horizontal divider between the screen section and the physical controls.
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 18),
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
                new Point(0, 0), new Point(0.5, 0))
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
                },
            },
        };
        body.Children.Add(divider);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Left bank: 3 push-buttons ────────────────────────────────────────
        var buttonBank = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _v2ButtonTiles.Clear();
        _v2HwButtonCaps.Clear();
        _v2HwButtonSubs.Clear();
        for (int i = 0; i < 3; i++)
        {
            int captureI = i;
            int buttonIdx = StreamControllerSideButtonBase + i;
            var col = BuildV2PushButton(i + 1, buttonIdx, out var cap, out var sub);
            _v2HwButtonCaps.Add(cap);
            _v2HwButtonSubs.Add(sub);
            col.Margin = new Thickness(i == 0 ? 0 : 18, 0, 0, 0);
            buttonBank.Children.Add(col);
        }
        Grid.SetColumn(buttonBank, 0);
        grid.Children.Add(buttonBank);

        // ── Vertical divider between buttons and encoders ────────────────────
        var vDivider = new Border
        {
            Width = 1,
            Margin = new Thickness(28, 6, 28, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        Grid.SetColumn(vDivider, 1);
        grid.Children.Add(vDivider);

        // ── Right bank: 3 encoders ───────────────────────────────────────────
        var encoderBank = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _v2EncoderTiles.Clear();
        _v2HwEncoderRings.Clear();
        _v2HwEncoderSubs.Clear();
        for (int i = 0; i < 3; i++)
        {
            int captureI = i;
            int encoderIdx = StreamControllerEncoderPressBase + i;
            var col = BuildV2Encoder(i + 1, encoderIdx, out var ring, out var sub);
            _v2HwEncoderRings.Add(ring);
            _v2HwEncoderSubs.Add(sub);
            col.Margin = new Thickness(i == 0 ? 0 : 18, 0, 0, 0);
            encoderBank.Children.Add(col);
        }
        Grid.SetColumn(encoderBank, 2);
        grid.Children.Add(encoderBank);

        body.Children.Add(grid);
        chassis.Child = body;
        return chassis;
    }

    private StackPanel BuildV2PushButton(int oneBasedLabel, int buttonIdx, out Border capOut, out TextBlock subOut)
    {
        var col = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };

        // Socket (recessed well for the cap to sit in) — uses an inset
        // darker radial gradient to simulate depth.
        var socket = new Border
        {
            Width = 62, Height = 62,
            CornerRadius = new CornerRadius(16),
            Cursor = Cursors.Hand,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Background = new RadialGradientBrush(
                Color.FromRgb(0x0A, 0x0B, 0x0F),
                Color.FromRgb(0x16, 0x18, 0x1E))
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.6, RadiusY = 0.6,
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
        };

        // Cap — plastic button surface. Uses a composite of a base linear
        // gradient + a translucent radial highlight overlay so the cap
        // reads as a slightly domed plastic key.
        var cap = new Border
        {
            Width = 48, Height = 48,
            CornerRadius = new CornerRadius(12),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Background = new LinearGradientBrush(
                Color.FromRgb(0x25, 0x28, 0x30),
                Color.FromRgb(0x11, 0x13, 0x18),
                new Point(0.5, 0), new Point(0.5, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.7,
            },
        };

        var capContents = new Grid();

        // Specular highlight — subtle light blob near top of cap.
        var specular = new Border
        {
            Width = 38, Height = 16,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                new Point(0.5, 0), new Point(0.5, 1)),
        };
        capContents.Children.Add(specular);

        var number = new TextBlock
        {
            Text = oneBasedLabel.ToString(),
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE2)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 3,
                ShadowDepth = 1,
                Opacity = 0.55,
            },
        };
        capContents.Children.Add(number);

        cap.Child = capContents;
        socket.Child = cap;

        socket.MouseEnter += (_, _) =>
        {
            cap.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
            cap.BorderThickness = new Thickness(1.5);
        };
        socket.MouseLeave += (_, _) =>
        {
            bool stillSelected = _scSelectedButtonIdx == buttonIdx;
            if (!stillSelected)
            {
                cap.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                cap.BorderThickness = new Thickness(1);
            }
        };
        socket.MouseLeftButtonUp += (_, _) =>
            SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, $"Button {oneBasedLabel}", null));

        col.Children.Add(socket);

        col.Children.Add(new TextBlock
        {
            Text = $"Button {oneBasedLabel}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        var sub = new TextBlock
        {
            Text = "None",
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 90,
        };
        col.Children.Add(sub);

        capOut = cap;
        subOut = sub;
        return col;
    }

    private StackPanel BuildV2Encoder(int oneBasedLabel, int encoderIdx, out Border ringOut, out TextBlock subOut)
    {
        var col = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };

        // Round root — one Grid so we can stack concentric ellipses cleanly.
        var stack = new Grid
        {
            Width = 68, Height = 68,
            Cursor = Cursors.Hand,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.75,
            },
        };

        // Recessed socket (outermost) — radial gradient to fake depth.
        var socket = new Ellipse
        {
            Fill = new RadialGradientBrush(
                Color.FromRgb(0x08, 0x09, 0x0C),
                Color.FromRgb(0x16, 0x18, 0x1E))
            {
                GradientOrigin = new Point(0.5, 0.4),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.55, RadiusY = 0.55,
            },
            Stroke = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
            StrokeThickness = 1,
        };
        stack.Children.Add(socket);

        // Metallic outer rim.
        var outerRim = new Ellipse
        {
            Width = 60, Height = 60,
            Fill = new LinearGradientBrush(
                Color.FromRgb(0x3A, 0x3C, 0x42),
                Color.FromRgb(0x0E, 0x0F, 0x13),
                new Point(0.5, 0), new Point(0.5, 1)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(outerRim);

        // Inner dark groove — simulates the space between rim and knob cap.
        var groove = new Ellipse
        {
            Width = 52, Height = 52,
            Fill = new SolidColorBrush(Color.FromRgb(0x05, 0x06, 0x08)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(groove);

        // Knob cap — spherical look via radial gradient.
        var cap = new Ellipse
        {
            Width = 48, Height = 48,
            Fill = new RadialGradientBrush(
                Color.FromRgb(0x38, 0x3B, 0x42),
                Color.FromRgb(0x0D, 0x0E, 0x12))
            {
                GradientOrigin = new Point(0.35, 0.25),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.75, RadiusY = 0.75,
            },
            Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(cap);

        // Specular highlight — small bright crescent in upper-left for glossy plastic.
        var specular = new Ellipse
        {
            Width = 22, Height = 10,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                new Point(0.5, 0), new Point(0.5, 1)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 13, 0, 0),
            IsHitTestVisible = false,
            RenderTransform = new RotateTransform(-10),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        stack.Children.Add(specular);

        // Indicator tick at top — accent-colored, glowing.
        var tick = new Border
        {
            Width = 4, Height = 11,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ThemeManager.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 13, 0, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ThemeManager.Accent,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.9,
            },
        };
        stack.Children.Add(tick);

        // Hover/selection tracking wraps the whole stack, ring border is
        // the outerRim Stroke (updated later).
        stack.MouseEnter += (_, _) =>
        {
            outerRim.Stroke = new SolidColorBrush(ThemeManager.Accent);
            outerRim.StrokeThickness = 1.5;
        };
        stack.MouseLeave += (_, _) =>
        {
            bool stillSelected = _scSelectedButtonIdx == encoderIdx;
            if (!stillSelected)
            {
                outerRim.Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                outerRim.StrokeThickness = 1;
            }
        };
        stack.MouseLeftButtonUp += (_, _) =>
            SelectStreamControllerItem(new StreamControllerSelection(encoderIdx, $"Encoder Press {oneBasedLabel}", null));

        // Wrap stack in a Border so downstream code can update the selection
        // indicator via a Border reference.
        var ringProxy = new Border
        {
            Child = stack,
            Tag = outerRim, // ref used by RefreshV2HardwareTiles
            Background = Brushes.Transparent,
        };

        col.Children.Add(ringProxy);

        col.Children.Add(new TextBlock
        {
            Text = $"Knob {oneBasedLabel}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        var sub = new TextBlock
        {
            Text = "None",
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 90,
        };
        col.Children.Add(sub);

        ringOut = ringProxy;
        subOut = sub;
        return col;
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
        if (_v2FoldersExpanded) RefreshV2FoldersList();
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
                    tile.PreviewImage = StreamControllerDisplayRenderer.CreateEditorPreview(backKey, 240);
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
            var visualKey = spotifySpanMaster ?? key;
            if (visualKey.DisplayType == DisplayKeyType.SpotifyNowPlaying)
            {
                var spotifyInfo = StreamControllerDisplayRenderer.SpotifyNowPlayingTitleProvider?.Invoke() ?? ("", "");
                spotifyStateHash = $"{StreamControllerDisplayRenderer.SpotifyNowPlayingImagePath}|{spotifyInfo.Title}|{spotifyInfo.Subtitle}|{visualKey.SpotifyAlbumArtLayout}|{i}";
            }

            // Compose a hash from fields the tile actually renders — skip
            // CreateHardwarePreview + tile.Refresh() when nothing changed.
            string hash = $"{key.Title}|{key.ImagePath}|{key.PresetIconKind}|{key.TextPosition}|{key.TextSize}|{key.TextColor}|{key.IconColor}|{key.FontFamily}|{key.Brightness}|{key.BackgroundColor}|{key.AccentColor}|{key.DisplayType}|{key.ClockFormat}|{key.DynamicStateSource}|{key.DynamicStateActiveIcon}|{key.DynamicStateActiveTitle}|{key.DynamicStateInactiveBrightness}|{key.DynamicStateDimWhenActive}|{key.DynamicStateGlowColor}|{key.SpotifyAlbumArtLayout}|{spotifyStateHash}|{dynamicActive}|{button?.Action}|{isSelected}";
            if (_v2KeyTileStateHash.TryGetValue(i, out var lastHash) && lastHash == hash)
                continue;

            tile.Opacity = 1.0;
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = null;
            if (spotifySpanMaster != null)
            {
                tile.PreviewImage = StreamControllerDisplayRenderer.CreateSpotifyAlbumArtTilePreview(spotifySpanMaster, i, 240);
                tile.PreviewAnimation = null;
                tile.PreviewAnimationSignature = $"spotify-span|{spotifySpanMaster.Idx}|{spotifyStateHash}|240";
            }
            else
            {
                tile.PreviewImage = StreamControllerDisplayRenderer.CreateEditorPreview(key, 240);
                tile.PreviewAnimation = StreamControllerDisplayRenderer.CreateEditorPreviewAnimation(key, 240);
                tile.PreviewAnimationSignature = $"{key.Idx}|{key.ImagePath}|{key.PresetIconKind}|{key.SpotifyAlbumArtLayout}|{spotifyStateHash}|240";
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

    private void RefreshV2HardwareTiles()
    {
        if (_config == null) return;

        var accent = new SolidColorBrush(ThemeManager.Accent);
        var idleBorder = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var idleRingBorder = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

        for (int i = 0; i < _v2HwButtonCaps.Count; i++)
        {
            int buttonIdx = StreamControllerSideButtonBase + i;
            var btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == buttonIdx);
            bool selected = _scSelectedButtonIdx == buttonIdx;

            if (i < _v2HwButtonSubs.Count)
                _v2HwButtonSubs[i].Text = GetStreamActionDisplay(btn);
            var cap = _v2HwButtonCaps[i];
            cap.BorderBrush = selected ? accent : idleBorder;
            cap.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        for (int i = 0; i < _v2HwEncoderRings.Count; i++)
        {
            int buttonIdx = StreamControllerEncoderPressBase + i;
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == buttonIdx);
            bool selected = _scSelectedButtonIdx == buttonIdx;

            if (i < _v2HwEncoderSubs.Count)
                _v2HwEncoderSubs[i].Text = GetStreamActionDisplay(press);
            // Tag holds the outerRim Ellipse — drive its Stroke for selection
            // instead of the wrapping Border (which is transparent chrome).
            if (_v2HwEncoderRings[i].Tag is Ellipse rim)
            {
                rim.Stroke = selected ? accent : idleRingBorder;
                rim.StrokeThickness = selected ? 2 : 1;
            }
        }
    }
}
