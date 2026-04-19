using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AmpUp.Controls;

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
    private Button? _v2FolderBackButton;
    private Grid? _v2KeyGrid;
    private StackPanel? _v2PageDotsPanel;

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

        // ── KEYS section ────────────────────────────────────────────────────
        _v2LeftPanel.Children.Add(BuildV2SectionHeader("KEYS"));

        _v2KeyGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
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
            };
            tile.OnClick += () => OnV2KeyTileClick(localIdx);
            tile.OnRightClick += e => OnV2KeyTileRightClick(tile, localIdx, e);

            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            _v2KeyGrid.Children.Add(tile);
            _v2KeyTiles.Add(tile);
        }
        _v2LeftPanel.Children.Add(_v2KeyGrid);

        // ── Page navigation toolbar (flat) ──────────────────────────────────
        _v2LeftPanel.Children.Add(BuildV2PageToolbar());

        // ── HARDWARE section ────────────────────────────────────────────────
        var hardwareHeader = BuildV2SectionHeader("HARDWARE");
        hardwareHeader.Margin = new Thickness(0, 20, 0, 10);
        _v2LeftPanel.Children.Add(hardwareHeader);

        _v2LeftPanel.Children.Add(BuildV2HardwareGrid());

        // Initial population of visuals.
        RefreshV2LeftPanel();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Builders
    // ────────────────────────────────────────────────────────────────────────

    private Border BuildV2FolderBanner()
    {
        var banner = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(
                0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _v2FolderBannerLabel = new TextBlock
        {
            Text = "\U0001F4C1 Editing folder",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_v2FolderBannerLabel, 0);
        row.Children.Add(_v2FolderBannerLabel);

        _v2FolderBackButton = new Button
        {
            Content = "Back to Root",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _v2FolderBackButton.Click += (_, _) => NavigateToFolderInEditor("");
        Grid.SetColumn(_v2FolderBackButton, 1);
        row.Children.Add(_v2FolderBackButton);

        banner.Child = row;
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

        _v2PageAddButton = MakeV2ToolbarTextButton("+", "Add page",
            () => AddStreamControllerPage());
        _v2PageAddButton.Margin = new Thickness(10, 0, 0, 0);
        toolbar.Children.Add(_v2PageAddButton);

        _v2PageRemoveButton = MakeV2ToolbarTextButton("\u2212", "Remove last page",
            () => RemoveStreamControllerPage());
        _v2PageRemoveButton.Margin = new Thickness(4, 0, 0, 0);
        toolbar.Children.Add(_v2PageRemoveButton);

        return toolbar;
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

    private Grid BuildV2HardwareGrid()
    {
        // Buttons + encoders in a single row of 6 equal-width tiles so the
        // hardware strip reads as one cohesive unit under the keys.
        var grid = new Grid();
        for (int c = 0; c < 6; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _v2ButtonTiles.Clear();
        for (int i = 0; i < 3; i++)
        {
            int captureI = i;
            var tile = new StreamControllerTile
            {
                Kind = StreamControllerTile.TileKind.SideButton,
                Title = $"Btn {i + 1}",
                Subtitle = "None",
                IconKind = "GestureTap",
                Margin = new Thickness(i == 0 ? 0 : 4, 0, 4, 0),
            };
            tile.OnClick += () => SelectStreamControllerItem(
                new StreamControllerSelection(
                    StreamControllerSideButtonBase + captureI,
                    $"Button {captureI + 1}",
                    null));
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
            _v2ButtonTiles.Add(tile);
        }

        _v2EncoderTiles.Clear();
        for (int i = 0; i < 3; i++)
        {
            int captureI = i;
            var tile = new StreamControllerTile
            {
                Kind = StreamControllerTile.TileKind.Encoder,
                Title = $"Knob {i + 1}",
                Subtitle = "None",
                IconKind = i == 0 ? "KnobLeft" : "Knob",
                Margin = new Thickness(4, 0, i == 2 ? 0 : 4, 0),
            };
            tile.OnClick += () => SelectStreamControllerItem(
                new StreamControllerSelection(
                    StreamControllerEncoderPressBase + captureI,
                    $"Encoder Press {captureI + 1}",
                    null));
            Grid.SetColumn(tile, 3 + i);
            grid.Children.Add(tile);
            _v2EncoderTiles.Add(tile);
        }

        return grid;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Click handlers
    // ────────────────────────────────────────────────────────────────────────

    private void OnV2KeyTileClick(int localIdx)
    {
        // In folder context, slot 0 is the reserved (auto) Back key — read-only.
        if (InFolderContext && localIdx == 0) return;

        int folderSlotOffset = InFolderContext ? -1 : 0;
        int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + localIdx + folderSlotOffset;
        int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
        SelectStreamControllerItem(new StreamControllerSelection(
            buttonIdx,
            $"Key {globalIdx + 1}",
            globalIdx));
    }

    private void OnV2KeyTileRightClick(StreamControllerTile tile, int localIdx, MouseButtonEventArgs e)
    {
        if (InFolderContext && localIdx == 0) { e.Handled = true; return; }
        int folderSlotOffset = InFolderContext ? -1 : 0;
        int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + localIdx + folderSlotOffset;
        ShowKeyContextMenu(tile, globalIdx);
        e.Handled = true;
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
    }

    private void RefreshV2FolderBanner()
    {
        if (_v2FolderBanner == null || _v2FolderBannerLabel == null) return;
        if (InFolderContext)
        {
            _v2FolderBanner.Visibility = Visibility.Visible;
            _v2FolderBannerLabel.Text = $"\U0001F4C1 Editing folder: {_scActiveFolder}";
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
        int folderSlotOffset = InFolderContext ? -1 : 0;

        for (int i = 0; i < _v2KeyTiles.Count; i++)
        {
            var tile = _v2KeyTiles[i];

            // In a folder, slot 0 previews the auto Back key (read-only).
            if (InFolderContext && i == 0)
            {
                string backHash = "back|selected=false";
                if (!_v2KeyTileStateHash.TryGetValue(i, out var prevHash) || prevHash != backHash)
                {
                    var backKey = App.BuildBackKeyDisplay();
                    tile.PreviewImage = StreamControllerDisplayRenderer.CreateHardwarePreview(backKey);
                    tile.Title = "Back";
                    tile.Subtitle = "Auto";
                    tile.IsSelected = false;
                    tile.Opacity = 0.6;
                    tile.Cursor = Cursors.Arrow;
                    tile.ToolTip = "Automatic Back key \u2014 returns to root folder on press.";
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
            bool isSelected = _scSelectedButtonIdx == buttonIdx;

            // Compose a hash from fields the tile actually renders — skip
            // CreateHardwarePreview + tile.Refresh() when nothing changed.
            string hash = $"{key.Title}|{key.ImagePath}|{key.PresetIconKind}|{key.TextPosition}|{key.TextSize}|{key.TextColor}|{key.BackgroundColor}|{key.AccentColor}|{key.DisplayType}|{key.ClockFormat}|{key.DynamicStateSource}|{key.DynamicStateActiveIcon}|{key.DynamicStateActiveTitle}|{button?.Action}|{isSelected}";
            if (_v2KeyTileStateHash.TryGetValue(i, out var lastHash) && lastHash == hash)
                continue;

            tile.Opacity = 1.0;
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = null;
            tile.PreviewImage = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            tile.Title = string.IsNullOrWhiteSpace(key.Title) ? $"Key {globalIdx + 1}" : key.Title;
            tile.Subtitle = GetStreamActionDisplay(button?.Action);
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

        for (int i = 0; i < _v2ButtonTiles.Count; i++)
        {
            var tile = _v2ButtonTiles[i];
            int buttonIdx = StreamControllerSideButtonBase + i;
            var btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == buttonIdx);
            tile.Title = $"Btn {i + 1}";
            tile.Subtitle = GetStreamActionDisplay(btn?.Action);
            tile.IsSelected = _scSelectedButtonIdx == buttonIdx;
            tile.Refresh();
        }

        for (int i = 0; i < _v2EncoderTiles.Count; i++)
        {
            var tile = _v2EncoderTiles[i];
            int buttonIdx = StreamControllerEncoderPressBase + i;
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == buttonIdx);
            tile.Title = $"Knob {i + 1}";
            tile.Subtitle = GetStreamActionDisplay(press?.Action);
            tile.IsSelected = _scSelectedButtonIdx == buttonIdx;
            tile.Refresh();
        }
    }
}
