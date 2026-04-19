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
            };
            tile.OnClick += () => OnV2KeyTileClick(localIdx);
            tile.OnRightClick += e => OnV2KeyTileRightClick(tile, localIdx, e);

            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            _v2KeyGrid.Children.Add(tile);
            _v2KeyTiles.Add(tile);
        }

        // ── HARDWARE device chassis — holds LCDs, page toolbar, buttons, encoders ──
        _v2LeftPanel.Children.Add(BuildV2HardwareDeviceBody());

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

        var accent = new SolidColorBrush(ThemeManager.Accent);
        var idleBorder = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var idleRingBorder = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

        for (int i = 0; i < _v2HwButtonCaps.Count; i++)
        {
            int buttonIdx = StreamControllerSideButtonBase + i;
            var btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == buttonIdx);
            bool selected = _scSelectedButtonIdx == buttonIdx;

            if (i < _v2HwButtonSubs.Count)
                _v2HwButtonSubs[i].Text = GetStreamActionDisplay(btn?.Action);
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
                _v2HwEncoderSubs[i].Text = GetStreamActionDisplay(press?.Action);
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
