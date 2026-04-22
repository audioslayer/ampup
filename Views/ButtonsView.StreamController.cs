using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class ButtonsView
{
    private const int StreamControllerDisplayKeyBase = 100;
    private const int StreamControllerSideButtonBase = 106;
    private const int StreamControllerEncoderPressBase = 109;
    private const int StreamControllerKeysPerPage = 6;

    // Key grid
    private readonly Border[] _scDisplayCards = new Border[6];
    private readonly Image[] _scDisplayImages = new Image[6];
    private readonly TextBlock[] _scDisplayCaptions = new TextBlock[6];

    // Quick controls
    private readonly Border[] _scSideCards = new Border[3];
    private readonly Border[] _scPressCards = new Border[3];
    private readonly TextBlock[] _scSideLabels = new TextBlock[3];
    private readonly TextBlock[] _scPressLabels = new TextBlock[3];

    // Right sidebar — editor
    private TextBlock? _scEditorTitle;
    private Image? _scEditorPreview;
    private TextBox? _scTitleBox;

    // Display type (Normal / Clock / Dynamic / Solid / Spotify) + related editors
    private SegmentedControl? _scDisplayTypePicker;
    private StackPanel? _scClockPanel;
    private TextBox? _scClockFormatBox;
    private StackPanel? _scDynamicPanel;
    private TextBlock? _scDynamicSourceLabel;
    private TextBox? _scDynamicActiveIconBox;
    private Button? _scDynamicChooseIconButton;
    private TextBox? _scDynamicActiveTitleBox;
    private SegmentedControl? _scDynamicDimWhenPicker;
    private StyledSlider? _scDynamicDimSlider;
    private TextBlock? _scDynamicDimLabel;
    private WrapPanel? _scDynamicGlowSwatchPanel;
    // Panels that host the "Normal" editors — grouped so they can hide together.
    private readonly List<FrameworkElement> _scNormalOnlyRows = new();
    private SegmentedControl? _scTextPositionPicker;
    private StyledSlider? _scTextSizeSlider;
    private TextBlock? _scTextSizeLabel;
    private Border? _scTextColorSwatch;
    private WrapPanel? _scTextColorSwatchPanel;
    private WrapPanel? _scIconColorSwatchPanel;
    private WrapPanel? _scGlowColorSwatchPanel;
    private TextBox? _scIconBox;
    private Button? _scChooseIconButton;
    private Button? _scClearIconButton;
    private StackPanel? _scDisplayDesignPanel;
    private ActionPicker? _scActionPicker;
    private TextBox? _scPathBox;
    private StackPanel? _scPathPanel;
    private TextBlock? _scPathLabel;
    private Button? _scBrowsePathButton;
    private Button? _scPickPathButton;
    private Border? _scAppChip;
    private TextBox? _scMacroBox;
    private StackPanel? _scMacroPanel;
    private TextBox? _scTextSnippetBox;
    private StackPanel? _scTextSnippetPanel;
    private Border? _scScreenshotInfoPanel;
    private ListPicker? _scDevicePicker;
    private StackPanel? _scGoveePanel;
    private ListPicker? _scGoveePicker;
    private StackPanel? _scRoomEffectPanel;
    private ListPicker? _scRoomEffectPicker;
    private StackPanel? _scDevicePanel;
    private ListPicker? _scKnobPicker;
    private StackPanel? _scKnobPanel;

    // Toggle (A/B) editor
    private StackPanel? _scTogglePanel;
    private ActionPicker? _scToggleActionAPicker;
    private ActionPicker? _scToggleActionBPicker;
    private TextBox? _scTogglePathABox;
    private TextBox? _scTogglePathBBox;
    private StackPanel? _scTogglePathAPanel;
    private StackPanel? _scTogglePathBPanel;
    private TextBlock? _scTogglePathALabel;
    private TextBlock? _scTogglePathBLabel;

    // Multi-Action sequence editor
    private StackPanel? _scMultiActionPanel;
    private StackPanel? _scMultiActionList;
    private Button? _scMultiActionAddButton;
    private TextBlock? _scMultiActionEmptyHint;

    // Actions disallowed inside a multi-action step (no nesting)
    private static readonly HashSet<string> MultiActionExcluded = new()
    {
        "multi_action", "toggle_action", "open_folder"
    };

    // Which actions require a path-ish text field when used as a multi-action step
    private static readonly HashSet<string> MultiActionStepPathActions = new()
    {
        "launch_exe", "close_program", "mute_program", "open_url", "sc_go_to_page",
        "ha_service", "govee_color", "obs_scene", "obs_mute", "vm_mute_strip", "vm_mute_bus"
    };

    // Sidebar tabs
    private StackPanel? _scDisplayTabContent;
    private StackPanel? _scActionTabContent;
    private Border? _scDisplayTabBtn;
    private Border? _scActionTabBtn;
    private bool _scShowingDisplayTab = true;

    // Paging
    private int _scCurrentPage;
    private int _scPageCount = 1;
    private readonly List<Ellipse> _scPageDots = new();
    private TextBlock? _scPageLabel;
    private StackPanel? _scPageDotsPanel;
    private Button? _scPageLeft;
    private Button? _scPageRight;
    private Button? _scAddPageButton;

    private int _scSelectedButtonIdx = StreamControllerDisplayKeyBase;

    private sealed record StreamControllerSelection(int ButtonIdx, string Label, int? DisplayIdx);

    private int PagedDisplayKeyBase => StreamControllerDisplayKeyBase + (_scCurrentPage * StreamControllerKeysPerPage);

    // ── Folder editing state ────────────────────────────────────────────
    // "" means we're editing the root grid. Otherwise the name is the key
    // into _config.N3.Folders. The editor UI routes all key/button reads
    // through GetActiveN3DisplayKeys / GetActiveN3ButtonList so existing
    // editing flows "just work" on folder contents.
    private string _scActiveFolder = "";
    private Border? _scFolderBanner;
    private TextBlock? _scFolderBannerLabel;
    private Button? _scFolderBackToRootButton;
    private ListPicker? _scFolderPicker;
    private StackPanel? _scFolderPanel;
    private Button? _scNewFolderButton;

    private bool InFolderContext => !string.IsNullOrEmpty(_scActiveFolder);

    // Re-entrance guard — NavigateToFolderInEditor calls App.NavigateToN3Folder
    // which calls SetActiveN3Folder which re-enters NavigateToFolderInEditor.
    private bool _v2FolderSyncing;

    private ButtonFolderConfig? ActiveFolder =>
        _config?.N3.Folders.FirstOrDefault(f => f.Name == _scActiveFolder);

    private List<StreamControllerDisplayKeyConfig> GetActiveN3DisplayKeys()
    {
        if (_config == null) return new List<StreamControllerDisplayKeyConfig>();
        var folder = ActiveFolder;
        return folder?.DisplayKeys ?? _config.N3.DisplayKeys;
    }

    private List<ButtonConfig> GetActiveN3ButtonList()
    {
        if (_config == null) return new List<ButtonConfig>();
        var folder = ActiveFolder;
        return folder?.Buttons ?? _config.N3.Buttons;
    }

    private int GetActiveN3PageCount()
    {
        if (_config == null) return 1;
        var folder = ActiveFolder;
        return Math.Max(1, folder?.PageCount ?? _config.N3.PageCount);
    }

    private void SetActiveN3PageCount(int count)
    {
        if (_config == null) return;
        var folder = ActiveFolder;
        if (folder != null) folder.PageCount = Math.Max(1, count);
        else _config.N3.PageCount = Math.Max(1, count);
    }

    private void InitializeDeviceSelector()
    {
        DeviceSelector.AccentColor = ThemeManager.Accent;
        DeviceSelector.ClearSegments();
        DeviceSelector.AddSegment("Turn Up", DeviceSurface.TurnUp);
        DeviceSelector.AddSegment("Stream Controller", DeviceSurface.StreamController);
        DeviceSelector.AddSegment("Both", DeviceSurface.Both);
        DeviceSelector.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            if (DeviceSelector.SelectedTag is DeviceSurface surface)
            {
                _config.TabSelection.Buttons = surface;
                UpdateDeviceSurfaceVisibility(surface);
                QueueSave();
            }
        };
    }

    private System.Windows.Threading.DispatcherTimer? _scPreviewRefreshTimer;

    private void BuildStreamControllerDesigner()
    {
        StreamControllerRoot.Children.Clear();

        // Tick the UI key grid once per 15s so Clock / DynamicState previews stay fresh.
        if (_scPreviewRefreshTimer == null)
        {
            _scPreviewRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15),
            };
            _scPreviewRefreshTimer.Tick += (_, _) => RefreshLiveDisplayPreviews();
            _scPreviewRefreshTimer.Start();
        }

        // Build the _sc* widgets (TextBox, SegmentedControl, handlers, etc.)
        // then compose the Stream Deck-inspired V2 canvas. The widget factory
        // assembles a throwaway panel tree so each widget has a parent during
        // construction; V2 detaches them and re-hosts them under _v2Root.
        BuildStreamControllerWidgetFactory();
        BuildStreamControllerDesignerV2();
        StreamControllerRoot.Children.Clear();
        if (_v2Root != null)
            StreamControllerRoot.Children.Add(_v2Root);
    }

    /// <summary>
    /// Creates every <c>_sc*</c> widget (TextBoxes, SegmentedControls, pickers,
    /// sliders) with their event handlers wired up, and assembles them into a
    /// temporary panel tree so they have valid parents during construction.
    /// The V2 designer then detaches each widget and hosts it in the new
    /// layout — so this tree itself is discarded but the widgets live on.
    /// </summary>
    private void BuildStreamControllerWidgetFactory()
    {

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        // ── LEFT: Center Stage ──────────────────────────────────────────
        var left = new StackPanel();

        // Folder breadcrumb banner — only visible while editing a folder
        _scFolderBanner = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
        };
        var bannerRow = new Grid();
        bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _scFolderBannerLabel = new TextBlock
        {
            Text = "Editing Space",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_scFolderBannerLabel, 0);
        bannerRow.Children.Add(_scFolderBannerLabel);
        _scFolderBackToRootButton = new Button
        {
            Content = "← Back to Home",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _scFolderBackToRootButton.Click += (_, _) => NavigateToFolderInEditor("");
        Grid.SetColumn(_scFolderBackToRootButton, 1);
        bannerRow.Children.Add(_scFolderBackToRootButton);
        _scFolderBanner.Child = bannerRow;
        left.Children.Add(_scFolderBanner);

        // Key grid with nav arrows
        var gridArea = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        gridArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gridArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _scPageLeft = MakeNavArrow("\u276E", () => NavigateStreamControllerPage(-1));
        _scPageRight = MakeNavArrow("\u276F", () => NavigateStreamControllerPage(1));
        Grid.SetColumn(_scPageLeft, 0);
        Grid.SetColumn(_scPageRight, 2);
        gridArea.Children.Add(_scPageLeft);
        gridArea.Children.Add(_scPageRight);

        var keyGrid = new UniformGrid
        {
            Columns = 3,
            Rows = 2,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(keyGrid, 1);

        for (int i = 0; i < 6; i++)
        {
            int localIdx = i;
            var card = new Border
            {
                Background = FindBrush("BgDarkBrush"),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(4),
                Padding = new Thickness(8),
                Cursor = Cursors.Hand
            };
            card.MouseEnter += (_, _) =>
            {
                if (InFolderContext && localIdx == 0) return;
                int folderSlotOffset = InFolderContext ? -1 : 0;
                int targetButtonIdx = PagedDisplayKeyBase + localIdx + folderSlotOffset;
                if (_scSelectedButtonIdx != targetButtonIdx)
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            };
            card.MouseLeave += (_, _) =>
            {
                if (InFolderContext && localIdx == 0) return;
                int folderSlotOffset = InFolderContext ? -1 : 0;
                int targetButtonIdx = PagedDisplayKeyBase + localIdx + folderSlotOffset;
                if (_scSelectedButtonIdx != targetButtonIdx)
                    card.BorderBrush = FindBrush("CardBorderBrush");
            };

            var stack = new StackPanel();
            var imageBorder = new Border
            {
                Height = 120,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            imageBorder.Child = previewImage;
            var caption = new TextBlock
            {
                Text = $"Key {i + 1}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            stack.Children.Add(imageBorder);
            stack.Children.Add(caption);
            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) =>
            {
                // In a folder, slot 0 is the reserved Back key — clicking it
                // doesn't select anything (it's a read-only visual on hardware).
                if (InFolderContext && localIdx == 0) return;

                int folderSlotOffset = InFolderContext ? -1 : 0;
                int targetLocalIdx = localIdx + folderSlotOffset;
                int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + targetLocalIdx;
                int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
                SelectStreamControllerItem(new StreamControllerSelection(
                    buttonIdx,
                    $"Key {globalIdx + 1}",
                    globalIdx));
            };
            card.MouseRightButtonUp += (_, e) =>
            {
                if (InFolderContext && localIdx == 0) { e.Handled = true; return; }
                int folderSlotOffset = InFolderContext ? -1 : 0;
                int targetLocalIdx = localIdx + folderSlotOffset;
                ShowKeyContextMenu(card, _scCurrentPage * StreamControllerKeysPerPage + targetLocalIdx);
                e.Handled = true;
            };

            _scDisplayCards[i] = card;
            _scDisplayImages[i] = previewImage;
            _scDisplayCaptions[i] = caption;
            keyGrid.Children.Add(card);
        }

        gridArea.Children.Add(keyGrid);
        left.Children.Add(gridArea);

        // Page indicator row
        var pageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 16)
        };
        _scPageDotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        pageRow.Children.Add(_scPageDotsPanel);
        _scPageLabel = new TextBlock
        {
            Text = "Page 1 of 1",
            FontSize = 11,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        pageRow.Children.Add(_scPageLabel);
        _scAddPageButton = MakeSmallButton("+", () => AddStreamControllerPage());
        _scAddPageButton.ToolTip = "Add page";
        pageRow.Children.Add(_scAddPageButton);
        var removePageButton = MakeSmallButton("−", () => RemoveStreamControllerPage());
        removePageButton.ToolTip = "Remove last page";
        removePageButton.Margin = new Thickness(4, 0, 0, 0);
        pageRow.Children.Add(removePageButton);
        left.Children.Add(pageRow);

        // Quick controls — mirrors physical hardware layout:
        // Bottom buttons (under LCD grid) on the left, knobs on the right
        left.Children.Add(MakeStreamHeader("HARDWARE CONTROLS", "Physical buttons and encoder knobs"));

        var hwLayout = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        hwLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        hwLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        // Left side: 3 bottom buttons in a row
        var btnColumn = new StackPanel();
        btnColumn.Children.Add(new TextBlock
        {
            Text = "BUTTONS",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var buttonRow = new UniformGrid { Columns = 3 };
        for (int i = 0; i < 3; i++)
        {
            int buttonIdx = StreamControllerSideButtonBase + i;
            int captureI = i;
            var card = MakeSmallControlCard($"Btn {i + 1}", out var label);
            card.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, $"Button {captureI + 1}", null));
            _scSideCards[i] = card;
            _scSideLabels[i] = label;
            buttonRow.Children.Add(card);
        }
        btnColumn.Children.Add(buttonRow);
        Grid.SetColumn(btnColumn, 0);
        hwLayout.Children.Add(btnColumn);

        // Right side: knobs — 1 large on top, 2 small below
        var knobColumn = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        knobColumn.Children.Add(new TextBlock
        {
            Text = "ENCODERS",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // Large knob (encoder 1)
        {
            int buttonIdx = StreamControllerEncoderPressBase + 0;
            var card = MakeSmallControlCard("Knob 1", out var label);
            card.MinHeight = 52;
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, "Encoder Press 1", null));
            _scPressCards[0] = card;
            _scPressLabels[0] = label;
            knobColumn.Children.Add(card);
        }

        // Two small knobs side by side
        var smallKnobRow = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 1; i < 3; i++)
        {
            int buttonIdx = StreamControllerEncoderPressBase + i;
            int captureI = i;
            var card = MakeSmallControlCard($"Knob {i + 1}", out var label);
            card.Margin = new Thickness(i == 1 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, $"Encoder Press {captureI + 1}", null));
            _scPressCards[i] = card;
            _scPressLabels[i] = label;
            smallKnobRow.Children.Add(card);
        }
        knobColumn.Children.Add(smallKnobRow);
        Grid.SetColumn(knobColumn, 1);
        hwLayout.Children.Add(knobColumn);

        left.Children.Add(hwLayout);

        // ── RIGHT: Sidebar Editor ───────────────────────────────────────
        var rightScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 0, 0, 0)
        };
        var right = new StackPanel();

        _scEditorTitle = new TextBlock
        {
            Text = "Key 1",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        right.Children.Add(_scEditorTitle);

        // Tab bar
        var tabBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _scDisplayTabBtn = MakeTabButton("Display", true);
        _scActionTabBtn = MakeTabButton("Action", false);
        _scDisplayTabBtn.MouseLeftButtonUp += (_, _) => SwitchStreamControllerEditorTab(showDisplay: true);
        _scActionTabBtn.MouseLeftButtonUp += (_, _) => SwitchStreamControllerEditorTab(showDisplay: false);
        Grid.SetColumn(_scDisplayTabBtn, 0);
        Grid.SetColumn(_scActionTabBtn, 1);
        tabBar.Children.Add(_scDisplayTabBtn);
        tabBar.Children.Add(_scActionTabBtn);
        right.Children.Add(tabBar);

        // ── Display tab content ─────────────────────────────────────────
        _scDisplayTabContent = new StackPanel();
        _scDisplayDesignPanel = _scDisplayTabContent;

        var previewCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 14)
        };
        _scEditorPreview = new Image
        {
            Height = 160,
            Stretch = Stretch.Uniform
        };
        previewCard.Child = _scEditorPreview;
        _scDisplayTabContent.Children.Add(previewCard);

        _scTitleBox = MakeEditorTextBox("Display title");
        _scTitleBox.TextChanged += (_, _) => { if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); } };
        _scIconBox = MakeEditorTextBox("No icon selected");
        _scIconBox.IsReadOnly = true;
        _scChooseIconButton = MakeEditorButton("Choose Icon", (_, _) => ChooseStreamControllerIcon());
        _scClearIconButton = MakeEditorButton("Clear", (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key == null) return;
            key.ImagePath = "";
            key.PresetIconKind = "";
            LoadStreamControllerSelection();
            QueueSave();
        });

        // ── Display Type (Normal / Clock / Dynamic) ────────────────────
        var displayTypeLabel = MakeEditorLabel("DISPLAY TYPE");
        _scDisplayTabContent.Children.Add(displayTypeLabel);
        _scDisplayTypePicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scDisplayTypePicker.AddSegment("Normal", DisplayKeyType.Normal);
        _scDisplayTypePicker.AddSegment("Clock", DisplayKeyType.Clock);
        _scDisplayTypePicker.AddSegment("Dynamic", DisplayKeyType.DynamicState);
        _scDisplayTypePicker.AddSegment("Solid", DisplayKeyType.Solid);
        _scDisplayTypePicker.AddSegment("Spotify", DisplayKeyType.SpotifyNowPlaying);
        _scDisplayTypePicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key != null && _scDisplayTypePicker.SelectedTag is DisplayKeyType dt)
            {
                key.DisplayType = dt;
                if (dt == DisplayKeyType.Solid && !string.IsNullOrWhiteSpace(key.AccentColor))
                    key.BackgroundColor = key.AccentColor;
            }
            UpdateDisplayTypeVisibility();
            BuildGlowColorSwatches();
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scDisplayTabContent.Children.Add(_scDisplayTypePicker);

        // Clock-type editors
        _scClockPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _scClockPanel.Children.Add(MakeEditorLabel("CLOCK FORMAT"));
        _scClockFormatBox = MakeEditorTextBox("HH:mm");
        _scClockFormatBox.TextChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key != null) key.ClockFormat = _scClockFormatBox.Text;
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scClockPanel.Children.Add(_scClockFormatBox);
        var clockChipsRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        foreach (var (chipLabel, chipFmt) in new[] { ("HH:mm", "HH:mm"), ("h:mm tt", "h:mm tt"), ("h:mm:ss tt", "h:mm:ss tt") })
        {
            var chip = MakeClockFormatChip(chipLabel, chipFmt);
            clockChipsRow.Children.Add(chip);
        }
        _scClockPanel.Children.Add(clockChipsRow);
        _scDisplayTabContent.Children.Add(_scClockPanel);

        // Dynamic-type editors
        _scDynamicPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _scDynamicPanel.Children.Add(MakeEditorLabel("STATE SOURCE"));
        _scDynamicSourceLabel = new TextBlock
        {
            Text = "No source — pick an action",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        _scDynamicSourceLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        _scDynamicSourceLabel.SetResourceReference(TextBlock.BackgroundProperty, "InputBgBrush");
        _scDynamicPanel.Children.Add(_scDynamicSourceLabel);

        _scDynamicPanel.Children.Add(MakeEditorLabel("ACTIVE ICON"));
        _scDynamicActiveIconBox = MakeEditorTextBox("No active icon");
        _scDynamicActiveIconBox.IsReadOnly = true;
        _scDynamicPanel.Children.Add(_scDynamicActiveIconBox);
        _scDynamicChooseIconButton = MakeEditorButton("Choose Active Icon", (_, _) => ChooseStreamControllerDynamicActiveIcon());
        _scDynamicChooseIconButton.Margin = new Thickness(0, 6, 0, 10);
        _scDynamicPanel.Children.Add(_scDynamicChooseIconButton);

        _scDynamicPanel.Children.Add(MakeEditorLabel("ACTIVE TITLE"));
        _scDynamicActiveTitleBox = MakeEditorTextBox("Active label");
        _scDynamicActiveTitleBox.TextChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key != null) key.DynamicStateActiveTitle = _scDynamicActiveTitleBox.Text;
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scDynamicPanel.Children.Add(_scDynamicActiveTitleBox);

        // Dim-state controls — when does the key render dimmed?
        _scDynamicPanel.Children.Add(MakeEditorLabel("DIM WHEN"));
        _scDynamicDimWhenPicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _scDynamicDimWhenPicker.AddSegment("Off / Inactive", false);
        _scDynamicDimWhenPicker.AddSegment("On / Active", true);
        _scDynamicDimWhenPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            var k = GetSelectedDisplayKeyConfig();
            if (k != null && _scDynamicDimWhenPicker.SelectedTag is bool b)
                k.DynamicStateDimWhenActive = b;
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scDynamicPanel.Children.Add(_scDynamicDimWhenPicker);

        _scDynamicDimLabel = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            Text = "Dim level: 50%",
        };
        _scDynamicDimLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        _scDynamicPanel.Children.Add(_scDynamicDimLabel);
        _scDynamicDimSlider = new StyledSlider
        {
            Minimum = 0, Maximum = 100,
            Width = 220,
            Step = 5,
            ShowLabel = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _scDynamicDimSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            int pct = (int)Math.Round(_scDynamicDimSlider.Value);
            if (_scDynamicDimLabel != null) _scDynamicDimLabel.Text = $"Dim level: {pct}%";
            var k = GetSelectedDisplayKeyConfig();
            if (k != null) k.DynamicStateInactiveBrightness = pct;
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scDynamicPanel.Children.Add(_scDynamicDimSlider);

        _scDynamicPanel.Children.Add(MakeEditorLabel("GLOW COLOR (ACTIVE)"));
        _scDynamicGlowSwatchPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _scDynamicPanel.Children.Add(_scDynamicGlowSwatchPanel);

        _scDisplayTabContent.Children.Add(_scDynamicPanel);

        var titleLabel = MakeEditorLabel("TITLE");
        _scDisplayTabContent.Children.Add(titleLabel);
        _scDisplayTabContent.Children.Add(_scTitleBox);
        _scNormalOnlyRows.Add(titleLabel);
        _scNormalOnlyRows.Add(_scTitleBox);
        // Text position picker
        var textPositionLabel = MakeEditorLabel("TEXT POSITION");
        _scDisplayTabContent.Children.Add(textPositionLabel);
        _scNormalOnlyRows.Add(textPositionLabel);
        _scTextPositionPicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scTextPositionPicker.AddSegment("Top", DisplayTextPosition.Top);
        _scTextPositionPicker.AddSegment("Middle", DisplayTextPosition.Middle);
        _scTextPositionPicker.AddSegment("Bottom", DisplayTextPosition.Bottom);
        _scTextPositionPicker.AddSegment("Hidden", DisplayTextPosition.Hidden);
        _scTextPositionPicker.SelectionChanged += (_, _) => { if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); } };
        _scDisplayTabContent.Children.Add(_scTextPositionPicker);
        _scNormalOnlyRows.Add(_scTextPositionPicker);

        // Text size slider
        _scTextSizeLabel = new TextBlock
        {
            Text = "Font Size: 14",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        _scDisplayTabContent.Children.Add(_scTextSizeLabel);
        _scNormalOnlyRows.Add(_scTextSizeLabel);
        _scTextSizeSlider = new StyledSlider
        {
            Minimum = 6,
            Maximum = 28,
            Value = 14,
            Step = 1,
            ShowLabel = false,
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scTextSizeSlider.ValueChanged += (_, _) =>
        {
            if (_scTextSizeLabel != null)
                _scTextSizeLabel.Text = $"Font Size: {(int)Math.Round(_scTextSizeSlider.Value)}";
            if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); }
        };
        _scDisplayTabContent.Children.Add(_scTextSizeSlider);
        _scNormalOnlyRows.Add(_scTextSizeSlider);

        // Text color palette
        var textColorLabel = MakeEditorLabel("TEXT COLOR");
        _scDisplayTabContent.Children.Add(textColorLabel);
        _scNormalOnlyRows.Add(textColorLabel);
        _scTextColorSwatch = new Border(); // placeholder for tracking
        var textColorWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _scTextColorSwatchPanel = textColorWrap;
        _scDisplayTabContent.Children.Add(textColorWrap);
        _scNormalOnlyRows.Add(textColorWrap);

        var iconLabel = MakeEditorLabel("ICON");
        _scDisplayTabContent.Children.Add(iconLabel);
        _scDisplayTabContent.Children.Add(_scIconBox);
        _scNormalOnlyRows.Add(iconLabel);
        _scNormalOnlyRows.Add(_scIconBox);
        var iconButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        iconButtonRow.Children.Add(_scChooseIconButton);
        _scClearIconButton.Margin = new Thickness(8, 0, 0, 0);
        iconButtonRow.Children.Add(_scClearIconButton);
        _scDisplayTabContent.Children.Add(iconButtonRow);
        _scNormalOnlyRows.Add(iconButtonRow);

        right.Children.Add(_scDisplayTabContent);

        // ── Action tab content ──────────────────────────────────────────
        _scActionTabContent = new StackPanel { Visibility = Visibility.Collapsed };

        _scActionTabContent.Children.Add(MakeEditorLabel("ACTION"));
        _scActionPicker = MakeActionCombo();
        _scActionPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            // Keep DynamicState source in sync with the bound action.
            var key = GetSelectedDisplayKeyConfig();
            if (key != null)
            {
                var derived = DynamicKeyStateProvider.DeriveSourceFromAction(_scActionPicker?.SelectedValue);
                if (key.DynamicStateSource != derived)
                    key.DynamicStateSource = derived;
                if (_scDynamicSourceLabel != null)
                {
                    string lbl = DynamicKeyStateProvider.GetSourceLabel(derived);
                    _scDynamicSourceLabel.Text = string.IsNullOrEmpty(lbl)
                        ? "No source — pick an action with a trackable state"
                        : $"Auto: {lbl}";
                }
            }
            UpdateStreamControllerActionVisibility();
            QueueSave();
        };
        _scActionTabContent.Children.Add(_scActionPicker);

        (_scPathPanel, _scPathBox, _scPathLabel, _scBrowsePathButton, _scPickPathButton, _scAppChip) = MakeStreamPathRow();
        (_scMacroPanel, _scMacroBox) = MakeStreamMacroRow();
        (_scTextSnippetPanel, _scTextSnippetBox) = MakeStreamTextSnippetRow();
        _scScreenshotInfoPanel = MakeStreamScreenshotInfo();
        (_scDevicePanel, _scDevicePicker) = MakeStreamDeviceRow();
        (_scGoveePanel, _scGoveePicker) = MakeStreamGoveeRow();
        (_scRoomEffectPanel, _scRoomEffectPicker) = MakeStreamRoomEffectRow();
        (_scKnobPanel, _scKnobPicker) = MakeStreamKnobRow();
        _scTogglePanel = MakeStreamToggleRow();
        _scMultiActionPanel = MakeStreamMultiActionPanel();
        (_scFolderPanel, _scFolderPicker, _scNewFolderButton) = MakeStreamFolderRow();

        _scActionTabContent.Children.Add(_scPathPanel);
        _scActionTabContent.Children.Add(_scMacroPanel);
        _scActionTabContent.Children.Add(_scTextSnippetPanel);
        _scActionTabContent.Children.Add(_scScreenshotInfoPanel);
        _scActionTabContent.Children.Add(_scDevicePanel);
        _scActionTabContent.Children.Add(_scGoveePanel);
        _scActionTabContent.Children.Add(_scRoomEffectPanel);
        _scActionTabContent.Children.Add(_scKnobPanel);
        _scActionTabContent.Children.Add(_scTogglePanel);
        _scActionTabContent.Children.Add(_scMultiActionPanel);
        _scActionTabContent.Children.Add(_scFolderPanel);

        right.Children.Add(_scActionTabContent);

        rightScroll.Content = right;

        Grid.SetColumn(left, 0);
        Grid.SetColumn(rightScroll, 1);
        root.Children.Add(left);
        root.Children.Add(rightScroll);

        StreamControllerRoot.Children.Add(root);
    }

    // ── Navigation helpers ──────────────────────────────────────────────

    private Button MakeNavArrow(string glyph, Action onClick)
    {
        var btn = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 20,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 36,
            Height = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0)
        };
        btn.MouseEnter += (_, _) => { if (btn.Content is TextBlock t) t.Foreground = new SolidColorBrush(ThemeManager.Accent); };
        btn.MouseLeave += (_, _) => { if (btn.Content is TextBlock t) t.Foreground = FindBrush("TextDimBrush"); };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button MakeSmallButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 28,
            MinHeight = 26,
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Border MakeTabButton(string text, bool active)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = active ? new SolidColorBrush(ThemeManager.Accent) : Brushes.Transparent,
            Background = Brushes.Transparent
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = active ? new SolidColorBrush(ThemeManager.Accent) : FindBrush("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        return border;
    }

    private void SwitchStreamControllerEditorTab(bool showDisplay)
    {
        _scShowingDisplayTab = showDisplay;
        if (_scDisplayTabContent != null)
            _scDisplayTabContent.Visibility = showDisplay ? Visibility.Visible : Visibility.Collapsed;
        if (_scActionTabContent != null)
            _scActionTabContent.Visibility = showDisplay ? Visibility.Collapsed : Visibility.Visible;

        // Update tab visuals
        if (_scDisplayTabBtn != null)
        {
            _scDisplayTabBtn.BorderBrush = showDisplay ? new SolidColorBrush(ThemeManager.Accent) : Brushes.Transparent;
            if (_scDisplayTabBtn.Child is TextBlock dt)
                dt.Foreground = showDisplay ? new SolidColorBrush(ThemeManager.Accent) : FindBrush("TextDimBrush");
        }
        if (_scActionTabBtn != null)
        {
            _scActionTabBtn.BorderBrush = !showDisplay ? new SolidColorBrush(ThemeManager.Accent) : Brushes.Transparent;
            if (_scActionTabBtn.Child is TextBlock at)
                at.Foreground = !showDisplay ? new SolidColorBrush(ThemeManager.Accent) : FindBrush("TextDimBrush");
        }
    }

    // ── Paging ───────────────────────────────────────────────────────────

    private void NavigateStreamControllerPage(int delta)
    {
        if (_config == null) return;
        int newPage = Math.Clamp(_scCurrentPage + delta, 0, _scPageCount - 1);
        if (newPage == _scCurrentPage) return;
        _scCurrentPage = newPage;
        _config.N3.CurrentPage = newPage;

        // Auto-select first key on the new page
        _scSelectedButtonIdx = PagedDisplayKeyBase;
        RefreshStreamControllerPageUI();
        LoadStreamControllerConfig();
        QueueSave();
    }

    public void SetStreamControllerPage(int page)
    {
        if (_config == null) return;
        int newPage = Math.Clamp(page, 0, _scPageCount - 1);
        if (newPage == _scCurrentPage) return;
        _scCurrentPage = newPage;
        _config.N3.CurrentPage = newPage;
        _scSelectedButtonIdx = PagedDisplayKeyBase;
        RefreshStreamControllerPageUI();
        LoadStreamControllerConfig();
        QueueSave();

        // Trigger hardware display re-sync via save
        _onSave?.Invoke(_config);
    }

    private void AddStreamControllerPage()
    {
        if (_config == null) return;
        _scPageCount++;
        SetActiveN3PageCount(_scPageCount);
        EnsureStreamControllerPageConfigs(_scPageCount - 1);
        NavigateStreamControllerPage(_scPageCount - 1 - _scCurrentPage);
    }

    private void RemoveStreamControllerPage()
    {
        if (_config == null || _scPageCount <= 1) return;
        int removedPage = _scPageCount - 1;

        // Confirm — count any keys or buttons on the last page that have
        // user content so the warning can actually name the stakes.
        var keys = GetActiveN3DisplayKeys();
        var btns = GetActiveN3ButtonList();
        int startIdx = removedPage * StreamControllerKeysPerPage;
        int btnStart = StreamControllerDisplayKeyBase + startIdx;
        int filledSlots = 0;
        foreach (var k in keys)
        {
            if (k.Idx < startIdx || k.Idx >= startIdx + StreamControllerKeysPerPage) continue;
            if (!string.IsNullOrEmpty(k.Title) || !string.IsNullOrEmpty(k.Subtitle)
                || !string.IsNullOrEmpty(k.ImagePath) || !string.IsNullOrEmpty(k.PresetIconKind)
                || k.DisplayType != DisplayKeyType.Normal)
                filledSlots++;
        }
        foreach (var b in btns)
        {
            if (b.Idx < btnStart || b.Idx >= btnStart + StreamControllerKeysPerPage) continue;
            if (!string.IsNullOrEmpty(b.Action) && b.Action != "none")
                filledSlots++;
        }

        string spaceLabel = string.IsNullOrEmpty(_scActiveFolder) ? "Home" : _scActiveFolder;
        string message = filledSlots > 0
            ? $"Remove page {_scPageCount} from \"{spaceLabel}\"?\n\nThis will delete {filledSlots} configured key binding{(filledSlots == 1 ? "" : "s")} on that page. This can't be undone."
            : $"Remove page {_scPageCount} from \"{spaceLabel}\"?";

        bool ok = GlassDialog.Confirm(message, "Remove page", dangerYes: filledSlots > 0, owner: Window.GetWindow(this));
        if (!ok) return;

        // Remove configs for the last page — folder-aware.
        keys.RemoveAll(k => k.Idx >= startIdx && k.Idx < startIdx + StreamControllerKeysPerPage);
        btns.RemoveAll(b => b.Idx >= btnStart && b.Idx < btnStart + StreamControllerKeysPerPage);

        _scPageCount--;
        SetActiveN3PageCount(_scPageCount);
        if (_scCurrentPage >= _scPageCount)
            _scCurrentPage = _scPageCount - 1;
        _config.N3.CurrentPage = _scCurrentPage;
        _scSelectedButtonIdx = PagedDisplayKeyBase;
        RefreshStreamControllerPageUI();
        LoadStreamControllerConfig();
        QueueSave();
    }

    private void EnsureStreamControllerPageConfigs(int page)
    {
        if (_config == null) return;
        var keys = GetActiveN3DisplayKeys();
        var btns = GetActiveN3ButtonList();
        for (int i = 0; i < StreamControllerKeysPerPage; i++)
        {
            int globalIdx = page * StreamControllerKeysPerPage + i;
            if (!keys.Any(k => k.Idx == globalIdx))
                keys.Add(new StreamControllerDisplayKeyConfig { Idx = globalIdx });
            int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
            if (!btns.Any(b => b.Idx == buttonIdx))
                btns.Add(new ButtonConfig { Idx = buttonIdx });
        }
    }

    private void RefreshStreamControllerPageUI()
    {
        // Rebuild dots
        _scPageDotsPanel?.Children.Clear();
        _scPageDots.Clear();
        for (int i = 0; i < _scPageCount; i++)
        {
            int targetPage = i;
            var dot = new Ellipse
            {
                Width = i == _scCurrentPage ? 10 : 8,
                Height = i == _scCurrentPage ? 10 : 8,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = i == _scCurrentPage
                    ? new SolidColorBrush(ThemeManager.Accent)
                    : FindBrush("TextDimBrush"),
                Cursor = Cursors.Hand,
                ToolTip = $"Page {i + 1}"
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                if (targetPage != _scCurrentPage)
                    NavigateStreamControllerPage(targetPage - _scCurrentPage);
            };
            _scPageDots.Add(dot);
            _scPageDotsPanel?.Children.Add(dot);
        }

        if (_scPageLabel != null)
            _scPageLabel.Text = $"Page {_scCurrentPage + 1} of {_scPageCount}";

        // Dim arrows at bounds
        if (_scPageLeft?.Content is TextBlock lt)
            lt.Foreground = _scCurrentPage > 0 ? FindBrush("TextSecBrush") : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        if (_scPageRight?.Content is TextBlock rt)
            rt.Foreground = _scCurrentPage < _scPageCount - 1 ? FindBrush("TextSecBrush") : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    }

    // ── Existing helpers (preserved) ────────────────────────────────────

    private Border MakeSmallControlCard(string title, out TextBlock actionLabel)
    {
        var border = new Border
        {
            Background = FindBrush("BgDarkBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        actionLabel = new TextBlock
        {
            Text = "None",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = FindBrush("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(actionLabel);
        border.Child = stack;
        return border;
    }

    private static TextBox MakeEditorTextBox(string placeholder)
    {
        return new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 36,
            Background = (Brush)Application.Current.FindResource("InputBgBrush"),
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(ThemeManager.Accent),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            Text = "",
            Tag = placeholder
        };
    }

    private static Button MakeEditorButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            Padding = new Thickness(12, 5, 12, 5)
        };
        button.Click += onClick;
        return button;
    }

    /// <summary>
    /// Which button list owns the currently-selected item. Uses the
    /// click-time-stamped selection kind to disambiguate folder page 1+
    /// LCD keys (idx 106-117) from physical side buttons / encoder
    /// presses (same idx range). Side/encoder items always live in the
    /// root N3.Buttons list regardless of folder context.
    /// </summary>
    private List<ButtonConfig> GetOwningButtonList()
    {
        if (_config == null) return new List<ButtonConfig>();
        if (_v2SelectionKind == V2SelectionKind.LcdKey && InFolderContext)
            return GetActiveN3ButtonList();
        return _config.N3.Buttons;
    }

    private StackPanel MakeStreamHeader(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeManager.Accent)
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = FindBrush("TextDimBrush")
        });
        return stack;
    }

    private TextBlock MakeEditorLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private (StackPanel panel, TextBox box, TextBlock label, Button browse, Button pick, Border appChip) MakeStreamPathRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var label = MakeEditorLabel("PROCESS NAME");
        panel.Children.Add(label);

        var box = MakeEditorTextBox("Path or process");
        panel.Children.Add(box);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal };
        var browse = MakeEditorButton("Browse", (_, _) => BrowsePathForSelectedStreamAction());
        var pick = MakeEditorButton("Pick Running App", (_, _) => PickRunningAppForSelectedStreamAction());
        pick.Margin = new Thickness(8, 0, 0, 0);
        buttonRow.Children.Add(browse);
        buttonRow.Children.Add(pick);
        panel.Children.Add(buttonRow);

        var appChip = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Selected app",
                Foreground = FindBrush("TextPrimaryBrush")
            }
        };
        panel.Children.Add(appChip);

        box.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        return (panel, box, label, browse, pick, appChip);
    }

    private (StackPanel panel, TextBox box) MakeStreamMacroRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("MACRO"));
        var box = MakeEditorTextBox("ctrl+shift+m");
        box.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        panel.Children.Add(box);
        return (panel, box);
    }

    private (StackPanel panel, TextBox box) MakeStreamTextSnippetRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("TEXT TO TYPE"));
        var box = MakeEditorTextBox("Text to type (use line breaks for Enter)");
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.MinHeight = 60;
        box.VerticalContentAlignment = VerticalAlignment.Top;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.TextChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            var button = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            if (button == null) return;
            button.TextSnippet = box.Text;
            QueueSave();
        };
        panel.Children.Add(box);
        return (panel, box);
    }

    private Border MakeStreamScreenshotInfo()
    {
        var card = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Captures the primary screen to clipboard on press.",
                FontSize = 12,
                Foreground = FindBrush("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        return card;
    }

    private (StackPanel panel, ListPicker picker) MakeStreamDeviceRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("DEVICE"));
        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        panel.Children.Add(picker);
        return (panel, picker);
    }

    /// <summary>
    /// Govee device picker for govee_toggle / govee_white_toggle actions.
    /// Selection saves the device IP into <c>ButtonConfig.Path</c>.
    /// </summary>
    private (StackPanel panel, ListPicker picker) MakeStreamGoveeRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("GOVEE DEVICE"));
        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            // Ignore programmatic ClearItems — only react to real user picks.
            if (picker.SelectedIndex < 0 || picker.SelectedTag is not string ip
                || string.IsNullOrEmpty(ip)) return;
            var list = GetOwningButtonList();
            var btn = list.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            if (btn == null) return;
            // For govee_color the path is "ip|hex" — preserve any existing hex suffix.
            if (btn.Action == "govee_color" && !string.IsNullOrEmpty(btn.Path) && btn.Path.Contains('|'))
            {
                var hex = btn.Path.Split('|', 2)[1];
                btn.Path = $"{ip}|{hex}";
            }
            else
            {
                btn.Path = ip;
            }
            QueueSave();
        };
        panel.Children.Add(picker);
        return (panel, picker);
    }

    /// <summary>Re-populate the Govee picker from the current config.</summary>
    private void RefreshGoveePickerItems()
    {
        if (_scGoveePicker == null || _config == null) return;
        _scGoveePicker.ClearItems();
        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
            bool hasCloud = !hasLan && !string.IsNullOrWhiteSpace(dev.DeviceId) && !string.IsNullOrWhiteSpace(dev.Sku);
            if (!hasLan && !hasCloud) continue;

            if (hasLan)
            {
                var label = string.IsNullOrWhiteSpace(dev.Name) ? dev.Ip : $"{dev.Name} ({dev.Ip})";
                _scGoveePicker.AddItem(label, dev.Ip);
            }
            else
            {
                // Cloud-only (e.g. H604C) — tag stored as cloud:<deviceId>.
                var friendly = !string.IsNullOrWhiteSpace(dev.Name) ? dev.Name : dev.DeviceId;
                _scGoveePicker.AddItem($"{friendly} (API)", $"cloud:{dev.DeviceId}");
            }
        }
    }

    /// <summary>
    /// Room effect picker for the "room_effect" action. Selection saves
    /// the LightEffect enum name into <c>ButtonConfig.Path</c>.
    /// </summary>
    private (StackPanel panel, ListPicker picker) MakeStreamRoomEffectRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("ROOM EFFECT"));
        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            if (picker.SelectedIndex < 0 || picker.SelectedTag is not string effect
                || string.IsNullOrEmpty(effect)) return;
            var list = GetOwningButtonList();
            var btn = list.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            if (btn == null) return;
            btn.Path = effect;
            QueueSave();
        };
        panel.Children.Add(picker);
        return (panel, picker);
    }

    /// <summary>Re-populate the Room Effect picker with every LightEffect.</summary>
    private void RefreshRoomEffectPickerItems()
    {
        if (_scRoomEffectPicker == null) return;
        _scRoomEffectPicker.ClearItems();
        foreach (var val in Enum.GetValues<LightEffect>())
            _scRoomEffectPicker.AddItem(val.ToString(), val.ToString());
    }

    private (StackPanel panel, ListPicker picker) MakeStreamKnobRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("LINKED TURN UP KNOB"));
        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        panel.Children.Add(picker);
        return (panel, picker);
    }

    // ── Folder picker row (for open_folder action) ─────────────────────
    private (StackPanel panel, ListPicker picker, Button newButton) MakeStreamFolderRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("SPACE"));

        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            // Programmatic ClearItems fires SelectionChanged with
            // SelectedIndex = -1 while the picker is being repopulated —
            // early-return so the reload doesn't wipe the user's folder.
            // Empty SelectedTag IS a valid choice (== Home sentinel).
            if (picker.SelectedIndex < 0 || picker.SelectedTag is not string folderName) return;
            // Side buttons / encoder presses live on the root list even when the
            // editor is navigated inside a folder — GetActiveN3ButtonList returns
            // the folder's list there and would miss them. Select the right list
            // based on the selected button's idx range.
            var list = GetOwningButtonList();
            var btn = list.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            if (btn == null) return;
            // Route action + folder name through gesture-aware setters so
            // Double/Hold folder picks don't clobber Tap's binding.
            SetGestureAction(btn, "open_folder");
            SetGestureFolderName(btn, folderName);
            QueueSave();
        };
        panel.Children.Add(picker);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var newBtn = MakeEditorButton("+ New Folder", (_, _) => CreateNewFolderForCurrentKey());
        var editBtn = MakeEditorButton("Edit Folder", (_, _) =>
        {
            if (picker.SelectedTag is string name && !string.IsNullOrEmpty(name))
                NavigateToFolderInEditor(name);
        });
        editBtn.Margin = new Thickness(8, 0, 0, 0);
        buttonRow.Children.Add(newBtn);
        buttonRow.Children.Add(editBtn);
        panel.Children.Add(buttonRow);

        return (panel, picker, newBtn);
    }

    private void RefreshFolderPickerItems()
    {
        if (_scFolderPicker == null || _config == null) return;
        _scFolderPicker.ClearItems();
        // Home sentinel — empty-string tag. Users removing the auto Back
        // key still need a way to return Home, so Home is always first in
        // the picker.
        _scFolderPicker.AddItem("\uD83C\uDFE0  Home", "");
        foreach (var folder in _config.N3.Folders)
        {
            if (!string.IsNullOrEmpty(folder.Name))
                _scFolderPicker.AddItem(folder.Name, folder.Name);
        }
    }

    private void CreateNewFolderForCurrentKey()
    {
        if (_config == null) return;

        string? name = GlassDialog.Prompt(
            "Enter a name for the new folder:",
            "New Folder",
            Window.GetWindow(this));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        // Dedup — append (2), (3), ... if the name already exists.
        if (_config.N3.Folders.Any(f => f.Name == name))
        {
            int counter = 2;
            string candidate;
            do
            {
                candidate = $"{name} ({counter++})";
            } while (_config.N3.Folders.Any(f => f.Name == candidate));
            name = candidate;
        }

        var folder = new ButtonFolderConfig { Name = name, PageCount = 1 };
        // Pre-seed first page with empty display key + button slots so the UI
        // is immediately functional when we navigate in.
        for (int i = 0; i < StreamControllerKeysPerPage; i++)
        {
            folder.DisplayKeys.Add(new StreamControllerDisplayKeyConfig { Idx = i });
            folder.Buttons.Add(new ButtonConfig { Idx = StreamControllerDisplayKeyBase + i });
        }
        _config.N3.Folders.Add(folder);

        // Assign the new folder to the currently-selected button.
        var btn = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
        if (btn != null)
        {
            btn.Action = "open_folder";
            btn.FolderName = name;
        }

        RefreshFolderPickerItems();
        if (_scFolderPicker != null)
        {
            for (int i = 0; i < _scFolderPicker.ItemCount; i++)
            {
                if (_scFolderPicker.GetTagAt(i) as string == name)
                {
                    _scFolderPicker.SelectedIndex = i;
                    break;
                }
            }
        }

        QueueSave();
        // Refresh the editor so the action picker shows open_folder + folder chip.
        LoadStreamControllerSelection();
    }

    /// <summary>
    /// Glass-style flattened menu items for the Spaces (folder) affordance
    /// on a key right-click. When the key already opens a Space, adds
    /// "Edit Space Contents" and "Unlink from Space" rows after the parent.
    /// </summary>
    private IEnumerable<GlassMenuItem> BuildFolderGlassMenuItems(int globalIdx)
    {
        if (_config == null) yield break;

        int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
        var btn = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == buttonIdx);
        bool isFolderKey = btn != null && btn.Action == "open_folder" && !string.IsNullOrEmpty(btn.FolderName);

        // Build the submenu list.
        var sub = new List<GlassMenuItem>
        {
            new("+ New Space…", Material.Icons.MaterialIconKind.Plus, () =>
            {
                _scSelectedButtonIdx = buttonIdx;
                CreateNewFolderForCurrentKey();
            }),
        };
        if (_config.N3.Folders.Count > 0)
        {
            sub.Add(GlassMenuItem.Sep);
            foreach (var folder in _config.N3.Folders)
            {
                if (string.IsNullOrEmpty(folder.Name)) continue;
                var folderName = folder.Name;
                bool isCurrent = isFolderKey && btn!.FolderName == folderName;
                sub.Add(new(folderName,
                    isCurrent ? Material.Icons.MaterialIconKind.Check : (Material.Icons.MaterialIconKind?)Material.Icons.MaterialIconKind.ViewDashboardOutline,
                    () =>
                    {
                        var target = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == buttonIdx);
                        if (target == null) return;
                        target.Action = "open_folder";
                        target.FolderName = folderName;
                        QueueSave();
                        LoadStreamControllerConfig();
                    },
                    IsChecked: isCurrent));
            }
        }

        yield return new(
            isFolderKey ? "Change Space" : "Open as Space",
            Material.Icons.MaterialIconKind.ViewDashboardOutline,
            null,
            Submenu: sub);

        if (isFolderKey)
        {
            yield return new("Edit Space Contents",
                Material.Icons.MaterialIconKind.Pencil,
                () => NavigateToFolderInEditor(btn!.FolderName));
            yield return new("Unlink from Space",
                Material.Icons.MaterialIconKind.LinkVariantOff,
                () =>
                {
                    var target = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == buttonIdx);
                    if (target == null) return;
                    target.Action = "none";
                    target.FolderName = "";
                    QueueSave();
                    LoadStreamControllerConfig();
                });
        }
    }

    /// <summary>
    /// Programmatic navigation for the editor — changes which folder's contents
    /// are being edited, updates the banner, resets page to 0, and reloads the UI.
    /// Matches what App.NavigateToN3Folder does for the hardware, plus UI refresh.
    /// Mirrors the change to the physical device so what the user edits is what
    /// the N3 shows.
    /// </summary>
    public void NavigateToFolderInEditor(string folderName)
    {
        folderName ??= "";
        if (_config != null && folderName.Length > 0
            && _config.N3.Folders.All(f => f.Name != folderName))
        {
            folderName = ""; // fallback if we were handed a bad name
        }

        // Idempotency guard — prevents the dispatcher feedback loop where
        // App.NavigateToN3Folder BeginInvoke's a callback that calls
        // SetActiveN3Folder → us again. The _v2FolderSyncing flag is
        // synchronous and has already been cleared by the time that
        // queued callback fires, so we need a "nothing to do" check here.
        if (_scActiveFolder == folderName) return;

        _scActiveFolder = folderName;
        _scCurrentPage = 0;
        if (_config != null) _config.N3.CurrentPage = 0;
        _scSelectedButtonIdx = StreamControllerDisplayKeyBase;
        UpdateFolderBanner();

        // Ensure the active folder has slot configs for the first page so the
        // editor save flow has ButtonConfig/DisplayKeyConfig objects to mutate.
        if (_config != null && InFolderContext)
            EnsureStreamControllerPageConfigs(0);

        // Mirror the editor navigation to the physical device so the N3 LCDs
        // render whichever folder the user is editing. Guarded by _v2Syncing
        // flag because App.NavigateToN3Folder calls SetActiveN3Folder which
        // re-enters this method — we'd loop forever without the guard.
        if (!_v2FolderSyncing)
        {
            _v2FolderSyncing = true;
            try { (Application.Current as App)?.NavigateToN3Folder(folderName); }
            finally { _v2FolderSyncing = false; }
        }

        LoadStreamControllerConfig();
    }

    /// <summary>Public entry point used by App when the physical device opens a folder.</summary>
    public void SetActiveN3Folder(string folderName)
    {
        // Keep editor in sync without saving (App already navigated hardware).
        NavigateToFolderInEditor(folderName);
    }

    private void UpdateFolderBanner()
    {
        if (_scFolderBanner == null || _scFolderBannerLabel == null) return;
        if (InFolderContext)
        {
            _scFolderBanner.Visibility = Visibility.Visible;
            _scFolderBannerLabel.Text = $"Editing folder: {_scActiveFolder}";
        }
        else
        {
            _scFolderBanner.Visibility = Visibility.Collapsed;
        }
    }

    // Actions that cannot be chosen for Toggle A/B (prevent recursion / unsupported)
    private static readonly HashSet<string> ToggleSubActionBlocklist = new()
    {
        "toggle_action", "multi_action", "open_folder",
    };

    // Actions that require a path textbox inside Toggle A/B (same set as main picker)
    private static bool ToggleSubActionNeedsPath(string action) =>
        PathActions.Contains(action) || action is "ha_service" or "govee_color" or "obs_scene" or "obs_mute" or "vm_mute_strip" or "vm_mute_bus";

    private ActionPicker MakeFilteredActionCombo(HashSet<string> blocklist)
    {
        var picker = new ActionPicker
        {
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        foreach (var (category, values) in ActionCategories)
        {
            bool anyAdded = false;
            foreach (var value in values)
            {
                if (blocklist.Contains(value)) continue;
                if (!ActionLookup.TryGetValue(value, out var action)) continue;
                if (!anyAdded) { picker.AddCategory(category); anyAdded = true; }
                var icon = ActionIcons.GetValueOrDefault(value, "—");
                var color = ActionColors.GetValueOrDefault(value, Color.FromRgb(0x88, 0x88, 0x88));
                var tooltip = ActionTooltips.GetValueOrDefault(value, action.Display);
                picker.AddItem(action.Display, value, icon, color, tooltip);
            }
        }

        picker.BuildPopup();
        picker.Select("none");
        return picker;
    }

    private (StackPanel panel, TextBlock label, TextBox box) MakeStreamToggleInnerPath(string initialLabel)
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        var label = MakeEditorLabel(initialLabel);
        panel.Children.Add(label);
        var box = MakeEditorTextBox("Path or process");
        panel.Children.Add(box);
        return (panel, label, box);
    }

    private StackPanel MakeStreamToggleRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };

        // ── Action A ────────────────────────────────────────────────
        var headerA = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "A",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeManager.Accent),
            },
        };
        panel.Children.Add(headerA);

        panel.Children.Add(MakeEditorLabel("ACTION A"));
        _scToggleActionAPicker = MakeFilteredActionCombo(ToggleSubActionBlocklist);
        _scToggleActionAPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateStreamControllerToggleVisibility();
            SaveStreamControllerToggleToConfig();
        };
        panel.Children.Add(_scToggleActionAPicker);

        (_scTogglePathAPanel, _scTogglePathALabel, _scTogglePathABox) = MakeStreamToggleInnerPath("PATH A");
        _scTogglePathABox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            SaveStreamControllerToggleToConfig();
        };
        panel.Children.Add(_scTogglePathAPanel);

        // ── Action B ────────────────────────────────────────────────
        var headerB = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 12, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "B",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeManager.Accent),
            },
        };
        panel.Children.Add(headerB);

        panel.Children.Add(MakeEditorLabel("ACTION B"));
        _scToggleActionBPicker = MakeFilteredActionCombo(ToggleSubActionBlocklist);
        _scToggleActionBPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateStreamControllerToggleVisibility();
            SaveStreamControllerToggleToConfig();
        };
        panel.Children.Add(_scToggleActionBPicker);

        (_scTogglePathBPanel, _scTogglePathBLabel, _scTogglePathBBox) = MakeStreamToggleInnerPath("PATH B");
        _scTogglePathBBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            SaveStreamControllerToggleToConfig();
        };
        panel.Children.Add(_scTogglePathBPanel);

        return panel;
    }

    private void UpdateStreamControllerToggleVisibility()
    {
        if (_scToggleActionAPicker == null || _scToggleActionBPicker == null
            || _scTogglePathAPanel == null || _scTogglePathBPanel == null
            || _scTogglePathALabel == null || _scTogglePathBLabel == null)
            return;

        var aAction = GetComboActionValue(_scToggleActionAPicker);
        var bAction = GetComboActionValue(_scToggleActionBPicker);
        _scTogglePathAPanel.Visibility = ToggleSubActionNeedsPath(aAction) ? Visibility.Visible : Visibility.Collapsed;
        _scTogglePathBPanel.Visibility = ToggleSubActionNeedsPath(bAction) ? Visibility.Visible : Visibility.Collapsed;
        _scTogglePathALabel.Text = GetTogglePathLabelText(aAction);
        _scTogglePathBLabel.Text = GetTogglePathLabelText(bAction);
    }

    private static string GetTogglePathLabelText(string action) => action switch
    {
        "mute_program" or "close_program" => "PROCESS NAME",
        "launch_exe" => "APP PATH",
        "sc_go_to_page" => "PAGE NUMBER",
        "open_url" => "URL",
        "ha_service" => "SERVICE CALL",
        "govee_color" => "HEX COLOR",
        "obs_scene" => "SCENE NAME",
        "obs_mute" => "SOURCE NAME",
        "vm_mute_strip" => "STRIP INDEX",
        "vm_mute_bus" => "BUS INDEX",
        _ => "PATH",
    };

    private void SaveStreamControllerToggleToConfig()
    {
        if (_config == null || _scToggleActionAPicker == null || _scToggleActionBPicker == null
            || _scTogglePathABox == null || _scTogglePathBBox == null)
            return;

        var button = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
        if (button == null) return;

        button.ToggleActionA = GetComboActionValue(_scToggleActionAPicker);
        button.ToggleActionB = GetComboActionValue(_scToggleActionBPicker);
        button.TogglePathA = ToggleSubActionNeedsPath(button.ToggleActionA) ? GetTextBoxValue(_scTogglePathABox) : "";
        button.TogglePathB = ToggleSubActionNeedsPath(button.ToggleActionB) ? GetTextBoxValue(_scTogglePathBBox) : "";
        QueueSave();
    }

    // ── Config load/save ────────────────────────────────────────────────

    private void LoadStreamControllerConfig()
    {
        if (_config == null || _scActionPicker == null || _scDevicePicker == null || _scKnobPicker == null) return;

        // Ensure the folder we think we're editing still exists.
        if (InFolderContext && _config.N3.Folders.All(f => f.Name != _scActiveFolder))
            _scActiveFolder = "";
        UpdateFolderBanner();

        int activePageCount = GetActiveN3PageCount();
        int currentPage = Math.Clamp(_config.N3.CurrentPage, 0, Math.Max(0, activePageCount - 1));
        _scCurrentPage = currentPage;
        _config.N3.CurrentPage = currentPage;
        _scPageCount = activePageCount;

        var activeKeys = GetActiveN3DisplayKeys();

        PopulateActionPicker(
            _scActionPicker,
            _config.HomeAssistant.Enabled,
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => IsHaAction(b.Action) || IsHaAction(b.DoublePressAction) || IsHaAction(b.HoldAction)),
            _config.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0,
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => IsGoveeAction(b.Action) || IsGoveeAction(b.DoublePressAction) || IsGoveeAction(b.HoldAction)),
            _config.Obs.Enabled,
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => IsObsAction(b.Action) || IsObsAction(b.DoublePressAction) || IsObsAction(b.HoldAction)),
            _config.VoiceMeeter.Enabled,
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => IsVmAction(b.Action) || IsVmAction(b.DoublePressAction) || IsVmAction(b.HoldAction)),
            _config.Groups.Count > 0,
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => b.Action == "group_toggle" || b.DoublePressAction == "group_toggle" || b.HoldAction == "group_toggle"),
            showScPageActions: true);

        PopulateDevicePicker(_scDevicePicker);
        PopulateKnobPicker(_scKnobPicker, _config);
        RefreshFolderPickerItems();

        for (int i = 0; i < 6; i++)
        {
            // When BackKeyEnabled, slot 0 on page 0 previews the auto Back
            // key and slots 1-5 map to folder keys 0-4. When disabled, all
            // 6 slots map straight to folder keys 0-5.
            if (IsBackKeyShown && i == 0)
            {
                var backKey = App.BuildBackKeyDisplay();
                _scDisplayImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(backKey);
                _scDisplayCaptions[i].Text = "Back (auto)";
                _scDisplayCards[i].Opacity = 0.6;
                _scDisplayCards[i].Cursor = Cursors.Arrow;
                _scDisplayCards[i].ToolTip = "Automatic Back key — returns to Home on press.";
                continue;
            }
            _scDisplayCards[i].Opacity = 1.0;
            _scDisplayCards[i].Cursor = Cursors.Hand;
            _scDisplayCards[i].ToolTip = null;

            int folderSlotOffset = IsBackKeyShown ? -1 : 0;
            int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + i + folderSlotOffset;
            var key = activeKeys.FirstOrDefault(k => k.Idx == globalIdx) ?? new StreamControllerDisplayKeyConfig { Idx = globalIdx };
            _scDisplayImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            _scDisplayCaptions[i].Text = string.IsNullOrWhiteSpace(key.Title) ? $"Key {globalIdx + 1}" : key.Title;
        }

        // Side buttons + encoder presses stay root-scoped — they aren't paginated
        // into folders (the physical side buttons are always global quick controls).
        for (int i = 0; i < 3; i++)
        {
            var side = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerSideButtonBase + i);
            _scSideLabels[i].Text = GetStreamActionDisplay(side);
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerEncoderPressBase + i);
            _scPressLabels[i].Text = GetStreamActionDisplay(press);
        }

        RefreshStreamControllerPageUI();
        LoadStreamControllerSelection();
    }

    private void LoadStreamControllerSelection()
    {
        if (_config == null || _scActionPicker == null || _scPathBox == null || _scMacroBox == null || _scDevicePicker == null || _scKnobPicker == null
            || _scEditorTitle == null || _scEditorPreview == null || _scTitleBox == null || _scIconBox == null)
            return;

        // Resolve the button config. Folder page 1+ LCD keys overlap the
        // root side/encoder idx range (106-111), so a naive "idx < 106 =
        // folder / else = root" split loads the wrong config for those
        // keys. Prefer the folder list first when we're inside a folder,
        // then fall back to root — that way an LCD click on idx 106 inside
        // "Room Effects" loads the folder's Fire key, while a Home-level
        // side-button click still loads the root binding.
        var buttonList = InFolderContext ? GetActiveN3ButtonList() : _config.N3.Buttons;
        var button = buttonList.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx)
                     ?? _config.N3.Buttons.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx)
                     ?? new ButtonConfig { Idx = _scSelectedButtonIdx };
        var selection = DescribeSelection(_scSelectedButtonIdx);
        _scEditorTitle.Text = selection.Label;

        // Gesture-aware loads for physical buttons (side/encoder) — reads
        // the DoublePress*/Hold* fields when those gestures are selected.
        // For LCD keys, _v2Gesture is always Tap so these resolve to
        // .Action/.Path/.MacroKeys just like before.
        string gAction = GetGestureAction(button);
        string gPath = GetGesturePath(button);
        string gMacro = GetGestureMacroKeys(button);

        SelectCombo(_scActionPicker, gAction);
        SetTextBoxValue(_scPathBox, ExtractPathBoxValue(gAction, gPath));
        SetTextBoxValue(_scMacroBox, gMacro);
        if (_scTextSnippetBox != null) _scTextSnippetBox.Text = button.TextSnippet ?? "";
        SelectDevicePicker(_scDevicePicker, button.DeviceId);
        SelectKnobPicker(_scKnobPicker, button.LinkedKnobIdx);
        // Sub-tag selections use the gesture-resolved action/path so Double
        // and Hold modes show their own integration sub-menus (HA entity,
        // Govee device, group name, etc.) not the Tap binding's.
        SelectHaSubTag(_scActionPicker, gAction, gPath);
        SelectDeviceSubTag(_scActionPicker, gAction, button.DeviceId);
        SelectProfileSubTag(_scActionPicker, gAction, button.ProfileName);
        SelectGroupSubTag(_scActionPicker, gAction, gPath);
        SelectGoveeSubTag(_scActionPicker, gAction, gPath);

        // Govee picker selection happens AFTER UpdateStreamControllerActionVisibility
        // below — that method calls RefreshGoveePickerItems which clears + refills
        // the picker and would otherwise wipe a selection we set here.
        string? pendingGoveeIp = (gAction is "govee_toggle" or "govee_white_toggle" or "govee_color"
                                   && !string.IsNullOrEmpty(gPath))
            ? (gPath.Contains('|') ? gPath.Split('|')[0] : gPath)
            : null;

        string? pendingRoomEffect = (gAction == "room_effect" && !string.IsNullOrEmpty(gPath))
            ? gPath : null;

        // Load Toggle (A/B) sub-actions
        if (_scToggleActionAPicker != null) SelectCombo(_scToggleActionAPicker, button.ToggleActionA);
        if (_scToggleActionBPicker != null) SelectCombo(_scToggleActionBPicker, button.ToggleActionB);
        if (_scTogglePathABox != null) SetTextBoxValue(_scTogglePathABox, button.TogglePathA);
        if (_scTogglePathBBox != null) SetTextBoxValue(_scTogglePathBBox, button.TogglePathB);

        // Folder picker selection happens after UpdateStreamControllerActionVisibility
        // below, once RefreshFolderPickerItems has populated it.
        string? pendingFolderName = gAction == "open_folder" ? GetGestureFolderName(button) : null;

        if (selection.DisplayIdx.HasValue)
        {
            var activeKeys = GetActiveN3DisplayKeys();
            var activeButtons = GetActiveN3ButtonList();
            var key = activeKeys.FirstOrDefault(k => k.Idx == selection.DisplayIdx.Value) ?? new StreamControllerDisplayKeyConfig { Idx = selection.DisplayIdx.Value };
            if (key.DisplayType == DisplayKeyType.DynamicState && string.IsNullOrWhiteSpace(key.DynamicStateSource))
            {
                var boundButton = activeButtons.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + selection.DisplayIdx.Value);
                string derived = DynamicKeyStateProvider.DeriveSourceFromAction(boundButton?.Action);
                if (!string.IsNullOrWhiteSpace(derived))
                    key.DynamicStateSource = derived;
            }
            _scTitleBox.Text = key.Title;
            _scIconBox.Text = !string.IsNullOrWhiteSpace(key.ImagePath)
                ? System.IO.Path.GetFileName(key.ImagePath)
                : !string.IsNullOrWhiteSpace(key.PresetIconKind)
                    ? key.PresetIconKind
                    : "No icon selected";
            if (_scTextPositionPicker != null)
            {
                _scTextPositionPicker.SelectedIndex = key.TextPosition switch
                {
                    DisplayTextPosition.Top => 0,
                    DisplayTextPosition.Middle => 1,
                    DisplayTextPosition.Bottom => 2,
                    DisplayTextPosition.Hidden => 3,
                    _ => 2
                };
            }
            if (_scTextSizeSlider != null) _scTextSizeSlider.Value = Math.Clamp(key.TextSize, 6, 28);
            if (_scTextSizeLabel != null) _scTextSizeLabel.Text = $"Font Size: {Math.Clamp(key.TextSize, 6, 28)}";
            BuildTextColorSwatches();

            // Display Type + Clock / Dynamic fields
            if (_scDisplayTypePicker != null)
            {
                _scDisplayTypePicker.SelectedIndex = key.DisplayType switch
                {
                    DisplayKeyType.Clock => 1,
                    DisplayKeyType.DynamicState => 2,
                    DisplayKeyType.Solid => 3,
                    DisplayKeyType.SpotifyNowPlaying => 4,
                    _ => 0
                };
            }
            if (_scClockFormatBox != null)
                _scClockFormatBox.Text = string.IsNullOrWhiteSpace(key.ClockFormat) ? "HH:mm" : key.ClockFormat;
            if (_scDynamicSourceLabel != null)
            {
                string src = key.DynamicStateSource ?? "";
                string label = DynamicKeyStateProvider.GetSourceLabel(src);
                _scDynamicSourceLabel.Text = string.IsNullOrEmpty(label)
                    ? "No source — pick an action with a trackable state"
                    : $"Auto: {label}";
            }
            if (_scDynamicActiveIconBox != null)
                _scDynamicActiveIconBox.Text = string.IsNullOrWhiteSpace(key.DynamicStateActiveIcon) ? "No active icon" : key.DynamicStateActiveIcon;
            if (_scDynamicActiveTitleBox != null)
                _scDynamicActiveTitleBox.Text = key.DynamicStateActiveTitle;
            if (_scDynamicDimWhenPicker != null)
                _scDynamicDimWhenPicker.SelectedIndex = key.DynamicStateDimWhenActive ? 1 : 0;
            if (_scDynamicDimSlider != null)
            {
                int pct = Math.Clamp(key.DynamicStateInactiveBrightness, 0, 100);
                _scDynamicDimSlider.Value = pct;
                if (_scDynamicDimLabel != null) _scDynamicDimLabel.Text = $"Dim level: {pct}%";
            }
            BuildDynamicGlowSwatches();
            UpdateDisplayTypeVisibility();

            _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            _scDisplayDesignPanel!.Visibility = Visibility.Visible;

            // Show Display tab for LCD keys, show tab bar
            if (_scDisplayTabBtn != null) _scDisplayTabBtn.Visibility = Visibility.Visible;
            if (_scShowingDisplayTab)
                SwitchStreamControllerEditorTab(showDisplay: true);
        }
        else
        {
            _scTitleBox.Text = "";
            _scIconBox.Text = "";
            _scEditorPreview.Source = null;
            _scDisplayDesignPanel!.Visibility = Visibility.Collapsed;

            // Non-LCD controls: force Action tab, hide Display tab
            if (_scDisplayTabBtn != null) _scDisplayTabBtn.Visibility = Visibility.Collapsed;
            SwitchStreamControllerEditorTab(showDisplay: false);
        }

        UpdateStreamControllerActionVisibility();

        RefreshStreamControllerSelectionVisuals();

        // V2 panels (owned by parallel-agent files) mirror the selection + visibility state.
        RefreshV2LeftPanel();
        RefreshV2RightPanel();
        RefreshV2ActionFieldsVisibility();

        // Pending selections must be applied AFTER every refresh call that
        // might Clear+AddItem the pickers. Both UpdateStreamControllerActionVisibility
        // and RefreshV2ActionFieldsVisibility call RefreshFolderPickerItems /
        // RefreshGoveePickerItems, so applying earlier loses the selection.
        if (_scFolderPicker != null && pendingFolderName != null)
        {
            int foundIdx = -1;
            for (int i = 0; i < _scFolderPicker.ItemCount; i++)
            {
                if (_scFolderPicker.GetTagAt(i) as string == pendingFolderName)
                {
                    foundIdx = i;
                    break;
                }
            }
            _scFolderPicker.SelectedIndex = foundIdx;
        }

        if (_scGoveePicker != null && pendingGoveeIp != null)
        {
            int foundIdx = -1;
            for (int i = 0; i < _scGoveePicker.ItemCount; i++)
            {
                if (string.Equals(_scGoveePicker.GetTagAt(i) as string, pendingGoveeIp, StringComparison.OrdinalIgnoreCase))
                {
                    foundIdx = i;
                    break;
                }
            }
            _scGoveePicker.SelectedIndex = foundIdx;
        }

        if (_scRoomEffectPicker != null && pendingRoomEffect != null)
        {
            int foundIdx = -1;
            for (int i = 0; i < _scRoomEffectPicker.ItemCount; i++)
            {
                if (string.Equals(_scRoomEffectPicker.GetTagAt(i) as string, pendingRoomEffect, StringComparison.OrdinalIgnoreCase))
                {
                    foundIdx = i;
                    break;
                }
            }
            _scRoomEffectPicker.SelectedIndex = foundIdx;
        }
    }

    private void UpdateStreamControllerActionVisibility()
    {
        if (_scActionPicker == null || _scPathPanel == null || _scPathLabel == null || _scBrowsePathButton == null || _scPickPathButton == null
            || _scMacroPanel == null || _scDevicePanel == null || _scKnobPanel == null || _scAppChip == null || _scPathBox == null)
            return;

        var action = GetComboActionValue(_scActionPicker);
        bool needsPath = PathActions.Contains(action) || action is "ha_service" or "govee_color" or "obs_scene" or "obs_mute" or "vm_mute_strip" or "vm_mute_bus";
        _scPathPanel.Visibility = needsPath ? Visibility.Visible : Visibility.Collapsed;
        _scMacroPanel.Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        if (_scTextSnippetPanel != null)
            _scTextSnippetPanel.Visibility = action == "type_text" ? Visibility.Visible : Visibility.Collapsed;
        if (_scScreenshotInfoPanel != null)
            _scScreenshotInfoPanel.Visibility = action == "screenshot" ? Visibility.Visible : Visibility.Collapsed;
        _scDevicePanel.Visibility = action is "select_output" or "select_input" or "mute_device" ? Visibility.Visible : Visibility.Collapsed;
        if (_scGoveePanel != null)
        {
            bool showGovee = action is "govee_toggle" or "govee_white_toggle" or "govee_color";
            _scGoveePanel.Visibility = showGovee ? Visibility.Visible : Visibility.Collapsed;
            if (showGovee) RefreshGoveePickerItems();
        }
        if (_scRoomEffectPanel != null)
        {
            bool showRoom = action == "room_effect";
            _scRoomEffectPanel.Visibility = showRoom ? Visibility.Visible : Visibility.Collapsed;
            if (showRoom) RefreshRoomEffectPickerItems();
        }
        _scKnobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
        if (_scTogglePanel != null)
        {
            _scTogglePanel.Visibility = action == "toggle_action" ? Visibility.Visible : Visibility.Collapsed;
            if (action == "toggle_action")
                UpdateStreamControllerToggleVisibility();
        }
        if (_scFolderPanel != null)
        {
            _scFolderPanel.Visibility = action == "open_folder" ? Visibility.Visible : Visibility.Collapsed;
            if (action == "open_folder")
                RefreshFolderPickerItems();
        }
        if (_scMultiActionPanel != null)
        {
            _scMultiActionPanel.Visibility = action == "multi_action" ? Visibility.Visible : Visibility.Collapsed;
            if (action == "multi_action")
                RebuildMultiActionList();
        }

        if (_scPathPanel.Visibility == Visibility.Visible)
        {
            if (action == "sc_go_to_page")
            {
                _scPathLabel.Text = "PAGE NUMBER";
                _scPathBox.Tag = "Page number (1-based)";
                _scBrowsePathButton.Visibility = Visibility.Collapsed;
                _scPickPathButton.Visibility = Visibility.Collapsed;
            }
            else if (action == "open_url")
            {
                _scPathLabel.Text = "URL";
                _scPathBox.Tag = "https://example.com";
                _scPathBox.ToolTip = "URL to open in the default browser";
                _scBrowsePathButton.Visibility = Visibility.Collapsed;
                _scPickPathButton.Visibility = Visibility.Collapsed;
                // open_url doesn't use the app chip — ensure the text input is visible
                if (_scPathBox.Parent is System.Windows.Controls.Border inputBorder)
                    inputBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ApplyPathLabelAndButtons(_scPathLabel, _scPathBox, _scBrowsePathButton, _scPickPathButton, action, _scAppChip);
            }
        }

        _scAppChip.Visibility = action is "close_program" or "mute_program" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStreamControllerSelection()
    {
        if (_config == null || _scActionPicker == null || _scPathBox == null || _scMacroBox == null || _scDevicePicker == null || _scKnobPicker == null)
            return;

        // Prefer folder list first — page 1+ LCD keys (idx 106-117) live
        // there and would otherwise resolve to the root side/encoder list
        // by idx range.
        var buttonList = GetOwningButtonList();
        var button = buttonList.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
        if (button == null) return;

        // Route Action / Path / MacroKeys to the gesture-specific fields
        // for side buttons + encoder presses. For LCD keys _v2Gesture is
        // always Tap so these still write to .Action/.Path/.MacroKeys.
        //
        // Prefer the V2 picker for action — the legacy _scActionPicker is
        // only synced on Tap, so on Double/Hold it still holds the Tap
        // binding and would overwrite the user's gesture-specific pick.
        string uiAction = _v2ActionPicker?.SelectedValue ?? "";
        if (string.IsNullOrEmpty(uiAction)) uiAction = GetComboActionValue(_scActionPicker);
        string uiPath = GetActionPath(_scActionPicker, _scPathBox);
        SetGestureAction(button, uiAction);
        SetGesturePath(button, uiPath);
        // V2 designer uses dedicated sub-pickers instead of the legacy
        // ActionPicker sub-flyout. GetActionPath reads the legacy SubTag
        // which is empty when picked via V2 — fall back to the V2
        // picker's value so the debounced save doesn't wipe the user's
        // choice.
        if (uiAction is "govee_toggle" or "govee_white_toggle" or "govee_color"
            && _scGoveePicker != null
            && _scGoveePicker.SelectedTag is string goveeIp && !string.IsNullOrEmpty(goveeIp))
        {
            string current = GetGesturePath(button);
            if (uiAction == "govee_color")
            {
                var existingHex = current.Contains('|') ? current.Split('|', 2)[1] : "";
                SetGesturePath(button, string.IsNullOrEmpty(existingHex) ? goveeIp : $"{goveeIp}|{existingHex}");
            }
            else
            {
                SetGesturePath(button, goveeIp);
            }
        }
        else if (uiAction == "room_effect"
                 && _scRoomEffectPicker != null
                 && _scRoomEffectPicker.SelectedTag is string roomEffect && !string.IsNullOrEmpty(roomEffect))
        {
            SetGesturePath(button, roomEffect);
        }
        SetGestureMacroKeys(button, GetTextBoxValue(_scMacroBox));
        // Checks against uiAction (the gesture-selected action) so options
        // that feed sub-pickers save correctly whether the user is on
        // Tap / Double / Hold.
        button.DeviceId = GetDeviceIdForAction(uiAction, _scActionPicker, _scDevicePicker);
        button.ProfileName = uiAction == "switch_profile" ? (_scActionPicker.SelectedSubTag ?? "") : "";
        button.LinkedKnobIdx = int.TryParse(_scKnobPicker.SelectedTag as string, out var linked) ? linked : -1;
        // Preserve folder linkage for open_folder — routed through the
        // gesture-aware setter so Tap / Double / Hold each remember their
        // own Space target.
        if (uiAction == "open_folder" && _scFolderPicker != null)
        {
            var folderName = _scFolderPicker.SelectedTag as string ?? GetGestureFolderName(button);
            SetGestureFolderName(button, folderName);
        }

        var display = GetSelectedDisplayKeyConfig();
        if (display != null && _scTitleBox != null)
        {
            display.Title = _scTitleBox.Text.Trim();
            if (_scTextPositionPicker?.SelectedTag is DisplayTextPosition textPos)
                display.TextPosition = textPos;
            if (_scTextSizeSlider != null)
                display.TextSize = (int)Math.Round(_scTextSizeSlider.Value);
            }

        LoadStreamControllerConfig();
    }

    private void SelectStreamControllerItem(StreamControllerSelection selection)
    {
        _scSelectedButtonIdx = selection.ButtonIdx;

        // Stamp selection kind at click time — idx alone is ambiguous
        // inside folders on page 1+ where LCD keys collide with side /
        // encoder idx (106-117). DisplayIdx.HasValue is the authoritative
        // bit set by LCD tile clicks.
        if (selection.DisplayIdx.HasValue)
            _v2SelectionKind = V2SelectionKind.LcdKey;
        else if (selection.ButtonIdx >= StreamControllerEncoderPressBase
                 && selection.ButtonIdx < StreamControllerEncoderPressBase + 3)
            _v2SelectionKind = V2SelectionKind.EncoderPress;
        else if (selection.ButtonIdx >= StreamControllerSideButtonBase
                 && selection.ButtonIdx < StreamControllerSideButtonBase + 3)
            _v2SelectionKind = V2SelectionKind.SideButton;
        else
            _v2SelectionKind = V2SelectionKind.LcdKey;

        SyncV2GestureBarForSelection();
        LoadStreamControllerSelection();
    }

    private StreamControllerSelection DescribeSelection(int buttonIdx)
    {
        if (buttonIdx >= StreamControllerDisplayKeyBase && buttonIdx < StreamControllerDisplayKeyBase + (_scPageCount * StreamControllerKeysPerPage))
        {
            int globalIdx = buttonIdx - StreamControllerDisplayKeyBase;
            return new StreamControllerSelection(buttonIdx, $"Key {globalIdx + 1}", globalIdx);
        }
        if (buttonIdx >= StreamControllerSideButtonBase && buttonIdx < StreamControllerSideButtonBase + 3)
            return new StreamControllerSelection(buttonIdx, $"Button {buttonIdx - StreamControllerSideButtonBase + 1}", null);
        return new StreamControllerSelection(buttonIdx, $"Encoder Press {buttonIdx - StreamControllerEncoderPressBase + 1}", null);
    }

    private StreamControllerDisplayKeyConfig? GetSelectedDisplayKeyConfig()
    {
        if (_config == null) return null;
        if (_scSelectedButtonIdx < StreamControllerDisplayKeyBase)
            return null;
        // Side buttons / encoder presses don't have LCD display keys.
        if (_scSelectedButtonIdx >= StreamControllerSideButtonBase)
            return null;
        int globalIdx = _scSelectedButtonIdx - StreamControllerDisplayKeyBase;
        if (globalIdx >= _scPageCount * StreamControllerKeysPerPage)
            return null;
        return GetActiveN3DisplayKeys().FirstOrDefault(k => k.Idx == globalIdx);
    }

    /// <summary>
    /// Re-renders the 6 key tiles + editor preview so Clock and DynamicState keys
    /// pick up time/state changes without the user needing to interact.
    /// </summary>
    private void RefreshLiveDisplayPreviews()
    {
        if (_config == null) return;

        var activeKeys = GetActiveN3DisplayKeys();

        bool anyLive = false;
        foreach (var k in activeKeys)
        {
            if (k.DisplayType == DisplayKeyType.Clock || k.DisplayType == DisplayKeyType.DynamicState)
            {
                anyLive = true;
                break;
            }
        }
        if (!anyLive) return;

        int folderSlotOffset = InFolderContext ? -1 : 0;
        for (int i = 0; i < 6; i++)
        {
            // Slot 0 is the static Back tile inside folders — skip the live redraw.
            if (InFolderContext && i == 0) continue;
            int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + i + folderSlotOffset;
            var key = activeKeys.FirstOrDefault(k => k.Idx == globalIdx);
            if (key == null) continue;
            if (key.DisplayType == DisplayKeyType.Normal) continue;
            _scDisplayImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
        }

        var selected = GetSelectedDisplayKeyConfig();
        if (selected != null && _scEditorPreview != null
            && (selected.DisplayType == DisplayKeyType.Clock || selected.DisplayType == DisplayKeyType.DynamicState))
        {
            _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(selected);
        }
    }

    private void UpdateEditorPreviewOnly()
    {
        var display = GetSelectedDisplayKeyConfig();
        if (display == null || _scEditorPreview == null || _scTitleBox == null) return;

        display.Title = _scTitleBox.Text.Trim();
        if (_scTextPositionPicker?.SelectedTag is DisplayTextPosition pos)
            display.TextPosition = pos;
        if (_scTextSizeSlider != null)
            display.TextSize = (int)Math.Round(_scTextSizeSlider.Value);

        bool spotifySpan = StreamControllerDisplayRenderer.IsSpotifyAlbumArtSpanned(display);
        // In root the folder key Idx == local slot. In a folder, local slot = Idx + 1
        // (slot 0 is the Back key).
        int folderSlotOffset = InFolderContext ? 1 : 0;
        int localIdx = display.Idx - (_scCurrentPage * StreamControllerKeysPerPage) + folderSlotOffset;
        StreamControllerDisplayKeyConfig? spotifySpanMaster = null;
        if (!InFolderContext && localIdx >= 0 && localIdx < StreamControllerKeysPerPage)
        {
            spotifySpanMaster = GetActiveN3DisplayKeys()
                .Where(StreamControllerDisplayRenderer.IsSpotifyAlbumArtSpanned)
                .OrderBy(k => k.Idx)
                .FirstOrDefault(k => StreamControllerDisplayRenderer.CoversSpotifyAlbumArtSlot(k, localIdx));
        }

        _scEditorPreview.Source = spotifySpan
            ? StreamControllerDisplayRenderer.CreateSpotifyAlbumArtCompositePreview(display, 120)
            : spotifySpanMaster != null && localIdx >= 0
                ? StreamControllerDisplayRenderer.CreateSpotifyAlbumArtTilePreview(spotifySpanMaster, display, localIdx, 360)
                : StreamControllerDisplayRenderer.CreateEditorPreview(display, 360);

        if (localIdx >= 0 && localIdx < 6)
        {
            _scDisplayImages[localIdx].Source = spotifySpan
                ? StreamControllerDisplayRenderer.CreateSpotifyAlbumArtTilePreview(display, display, localIdx, 240)
                : spotifySpanMaster != null
                    ? StreamControllerDisplayRenderer.CreateSpotifyAlbumArtTilePreview(spotifySpanMaster, display, localIdx, 240)
                    : StreamControllerDisplayRenderer.CreateEditorPreview(display, 240);
            _scDisplayCaptions[localIdx].Text = string.IsNullOrWhiteSpace(display.Title) ? $"Key {display.Idx + 1}" : display.Title;
        }

        // Also refresh the V2 left-panel tile (for live updates when
        // editing title/icon/color — the per-tile cache checks IconColor).
        RefreshV2LeftPanel();
    }

    private void RefreshStreamControllerSelectionVisuals()
    {
        int folderSlotOffset = InFolderContext ? 1 : 0;
        for (int i = 0; i < 6; i++)
        {
            int targetButtonIdx = PagedDisplayKeyBase + (i - folderSlotOffset);
            // In a folder, slot 0 is the Back key and can never be "selected".
            bool isBackSlot = InFolderContext && i == 0;
            bool active = !isBackSlot && _scSelectedButtonIdx == targetButtonIdx;
            ApplySelectionState(_scDisplayCards[i], active);
        }
        for (int i = 0; i < 3; i++)
        {
            ApplySelectionState(_scSideCards[i], _scSelectedButtonIdx == StreamControllerSideButtonBase + i);
            ApplySelectionState(_scPressCards[i], _scSelectedButtonIdx == StreamControllerEncoderPressBase + i);
        }
    }

    private void ApplySelectionState(Border border, bool active)
    {
        border.BorderBrush = active
            ? new SolidColorBrush(ThemeManager.Accent)
            : FindBrush("CardBorderBrush");
        border.Background = active
            ? new SolidColorBrush(Color.FromArgb(0x15, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B))
            : FindBrush("BgDarkBrush");
    }

    private void UpdateDeviceSurfaceVisibility(DeviceSurface surface)
    {
        TurnUpButtonsPanel.Visibility = surface is DeviceSurface.TurnUp or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
        StreamControllerPanel.Visibility = surface is DeviceSurface.StreamController or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetStreamActionDisplay(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || action == "none") return "None";
        return Actions.FirstOrDefault(a => a.Value == action).Display ?? action;
    }

    /// <summary>
    /// Action subtitle with the action-specific target appended when the
    /// user has picked one — e.g. "Open Space › Lights" instead of bare
    /// "Open Space". Falls back to the action name for cases that don't
    /// carry a user-visible target.
    /// </summary>
    private string GetStreamActionDisplay(ButtonConfig? btn)
    {
        if (btn == null) return "None";
        string baseLabel = GetStreamActionDisplay(btn.Action);
        switch (btn.Action)
        {
            case "open_folder":
                var folder = string.IsNullOrWhiteSpace(btn.FolderName) ? "Home" : btn.FolderName;
                return $"{baseLabel} › {folder}";
            case "switch_profile":
                if (!string.IsNullOrWhiteSpace(btn.ProfileName))
                    return $"{baseLabel} › {btn.ProfileName}";
                break;
            case "room_effect":
                if (!string.IsNullOrWhiteSpace(btn.Path))
                    return $"{baseLabel} › {btn.Path}";
                break;
            case "launch_exe":
            case "close_program":
            case "mute_program":
                if (!string.IsNullOrWhiteSpace(btn.Path))
                {
                    var tail = System.IO.Path.GetFileNameWithoutExtension(btn.Path);
                    if (string.IsNullOrWhiteSpace(tail)) tail = btn.Path;
                    return $"{baseLabel} › {tail}";
                }
                break;
        }
        return baseLabel;
    }

    /// <summary>
    /// Curated list of font families the FONT picker offers for LCD key
    /// titles. All are shipped with Windows 10/11 out of the box. Segoe
    /// UI stays the default because it's what the rest of the app uses.
    /// </summary>
    private static readonly string[] StreamControllerFontPresets =
    {
        "Segoe UI",
        "Segoe UI Semibold",
        "Arial",
        "Arial Black",
        "Verdana",
        "Tahoma",
        "Calibri",
        "Cambria",
        "Consolas",
        "Courier New",
        "Georgia",
        "Times New Roman",
        "Trebuchet MS",
        "Impact",
        "Comic Sans MS",
    };

    private static readonly (string Name, string Hex)[] TextColorPresets =
    {
        ("White",   "#FFFFFF"),
        ("Light",   "#E0E0E0"),
        ("Grey",    "#9E9E9E"),
        ("Green",   "#00E676"),
        ("Cyan",    "#00B4D8"),
        ("Blue",    "#448AFF"),
        ("Purple",  "#B388FF"),
        ("Pink",    "#FF4081"),
        ("Red",     "#FF5252"),
        ("Orange",  "#FF6E40"),
        ("Gold",    "#FFD740"),
        ("Lime",    "#C6FF00"),
    };

    /// <summary>
    /// Dynamic-state "active" glow — writes to
    /// <see cref="StreamControllerDisplayKeyConfig.DynamicStateGlowColor"/>.
    /// An "Off" swatch clears the glow.
    /// </summary>
    private void BuildDynamicGlowSwatches()
    {
        if (_scDynamicGlowSwatchPanel == null) return;
        _scDynamicGlowSwatchPanel.Children.Clear();

        var display = GetSelectedDisplayKeyConfig();
        string currentHex = display?.DynamicStateGlowColor ?? "";

        // "Off" swatch — clears the glow.
        var off = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            BorderThickness = new Thickness(2),
            BorderBrush = string.IsNullOrEmpty(currentHex)
                ? new SolidColorBrush(Colors.White)
                : Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "No glow",
        };
        off.Child = new TextBlock
        {
            Text = "✕",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        off.MouseLeftButtonDown += (_, _) =>
        {
            if (display == null) return;
            display.DynamicStateGlowColor = "";
            BuildDynamicGlowSwatches();
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        _scDynamicGlowSwatchPanel.Children.Add(off);

        foreach (var (name, hex) in TextColorPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            string captured = hex;
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                if (display == null) return;
                display.DynamicStateGlowColor = captured;
                BuildDynamicGlowSwatches();
                UpdateEditorPreviewOnly();
                QueueSave();
            };
            _scDynamicGlowSwatchPanel.Children.Add(swatch);
        }
    }

    /// <summary>
    /// Build the BACKGROUND / GLOW colour swatch row — writes to
    /// <c>StreamControllerDisplayKeyConfig.AccentColor</c>, which the
    /// renderer uses for the radial glow behind the icon.
    /// </summary>
    private void BuildGlowColorSwatches()
    {
        if (_scGlowColorSwatchPanel == null) return;
        _scGlowColorSwatchPanel.Children.Clear();

        var display = GetSelectedDisplayKeyConfig();
        bool isSolid = display?.DisplayType == DisplayKeyType.Solid;
        string currentHex = isSolid
            ? (display?.BackgroundColor ?? display?.AccentColor ?? "#00E676")
            : (display?.AccentColor ?? "#00E676");

        foreach (var (name, hex) in TextColorPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            string capturedHex = hex;
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                if (display == null) return;
                if (isSolid)
                    display.BackgroundColor = capturedHex;
                else
                    display.AccentColor = capturedHex;
                BuildGlowColorSwatches();
                UpdateEditorPreviewOnly();
                QueueSave();
            };
            _scGlowColorSwatchPanel.Children.Add(swatch);
        }

        string activeHex = isSolid
            ? (display?.BackgroundColor ?? "")
            : (display?.AccentColor ?? "");
        bool isCustom = !string.IsNullOrWhiteSpace(activeHex)
            && !TextColorPresets.Any(p => p.Hex.Equals(activeHex, StringComparison.OrdinalIgnoreCase));
        var customSwatch = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Red, 0.0), new(Colors.Yellow, 0.17), new(Colors.Lime, 0.33),
                    new(Colors.Cyan, 0.5), new(Colors.Blue, 0.67), new(Colors.Magenta, 0.83), new(Colors.Red, 1.0),
                }, new Point(0, 0), new Point(1, 1)),
            BorderThickness = new Thickness(2),
            BorderBrush = isCustom ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Custom color",
            Child = new TextBlock
            {
                Text = "+", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        customSwatch.MouseLeftButtonDown += (_, _) =>
        {
            if (display == null) return;
            Color initial;
            try
            {
                initial = (Color)ColorConverter.ConvertFromString(
                    isSolid ? (display.BackgroundColor ?? display.AccentColor ?? "#00E676")
                            : (display.AccentColor ?? "#00E676"));
            }
            catch { initial = Colors.Lime; }
            var dialog = new ColorPickerDialog(initial) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c =>
            {
                if (isSolid)
                    display.BackgroundColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                else
                    display.AccentColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                UpdateEditorPreviewOnly();
            };
            dialog.ShowDialog();
            if (isSolid)
                display.BackgroundColor = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
            else
                display.AccentColor = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
            BuildGlowColorSwatches();
            QueueSave();
        };
        _scGlowColorSwatchPanel.Children.Add(customSwatch);
    }

    /// <summary>
    /// Build the ICON COLOR swatch row — mirrors the TEXT COLOR palette
    /// but writes to <c>StreamControllerDisplayKeyConfig.IconColor</c>
    /// so preset vector icons tint live. Inactive for user bitmap icons.
    /// </summary>
    private void BuildIconColorSwatches()
    {
        if (_scIconColorSwatchPanel == null) return;
        _scIconColorSwatchPanel.Children.Clear();

        var display = GetSelectedDisplayKeyConfig();
        string currentHex = display?.IconColor ?? "#F7F7F7";

        foreach (var (name, hex) in TextColorPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            string capturedHex = hex;
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                if (display == null) return;
                display.IconColor = capturedHex;
                BuildIconColorSwatches();
                UpdateEditorPreviewOnly();
                QueueSave();
            };
            _scIconColorSwatchPanel.Children.Add(swatch);
        }

        bool isCustom = display?.IconColor != null
            && !TextColorPresets.Any(p => p.Hex.Equals(display.IconColor, StringComparison.OrdinalIgnoreCase));
        var customSwatch = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Red, 0.0), new(Colors.Yellow, 0.17), new(Colors.Lime, 0.33),
                    new(Colors.Cyan, 0.5), new(Colors.Blue, 0.67), new(Colors.Magenta, 0.83), new(Colors.Red, 1.0),
                }, new Point(0, 0), new Point(1, 1)),
            BorderThickness = new Thickness(2),
            BorderBrush = isCustom ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Custom color",
            Child = new TextBlock
            {
                Text = "+", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        customSwatch.MouseLeftButtonDown += (_, _) =>
        {
            if (display == null) return;
            Color initial;
            try { initial = (Color)ColorConverter.ConvertFromString(display.IconColor ?? "#F7F7F7"); }
            catch { initial = Colors.White; }
            var dialog = new ColorPickerDialog(initial) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c =>
            {
                display.IconColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                UpdateEditorPreviewOnly();
            };
            dialog.ShowDialog();
            display.IconColor = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
            BuildIconColorSwatches();
            QueueSave();
        };
        _scIconColorSwatchPanel.Children.Add(customSwatch);
    }

    private void BuildTextColorSwatches()
    {
        if (_scTextColorSwatchPanel == null) return;
        _scTextColorSwatchPanel.Children.Clear();

        var display = GetSelectedDisplayKeyConfig();
        string currentHex = display?.TextColor ?? "#FFFFFF";

        foreach (var (name, hex) in TextColorPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            string capturedHex = hex;
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                if (display == null) return;
                display.TextColor = capturedHex;
                BuildTextColorSwatches();
                UpdateEditorPreviewOnly();
                QueueSave();
            };
            _scTextColorSwatchPanel.Children.Add(swatch);
        }

        // Custom color picker (rainbow swatch with +)
        bool isCustom = display?.TextColor != null
            && !TextColorPresets.Any(p => p.Hex.Equals(display.TextColor, StringComparison.OrdinalIgnoreCase));
        var customSwatch = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Red, 0.0), new(Colors.Yellow, 0.17), new(Colors.Lime, 0.33),
                    new(Colors.Cyan, 0.5), new(Colors.Blue, 0.67), new(Colors.Magenta, 0.83), new(Colors.Red, 1.0),
                }, new Point(0, 0), new Point(1, 1)),
            BorderThickness = new Thickness(2),
            BorderBrush = isCustom ? new SolidColorBrush(Colors.White) : Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Custom color",
            Child = new TextBlock
            {
                Text = "+", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        customSwatch.MouseLeftButtonDown += (_, _) =>
        {
            if (display == null) return;
            Color initial;
            try { initial = (Color)ColorConverter.ConvertFromString(display.TextColor ?? "#FFFFFF"); }
            catch { initial = Colors.White; }
            var dialog = new ColorPickerDialog(initial) { Owner = Window.GetWindow(this) };
            dialog.ColorChanged += c =>
            {
                display.TextColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                UpdateEditorPreviewOnly();
            };
            dialog.ShowDialog();
            display.TextColor = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
            BuildTextColorSwatches();
            QueueSave();
        };
        _scTextColorSwatchPanel.Children.Add(customSwatch);
    }

    // ── Context menu ─────────────────────────────────────────────────────

    private void ShowGlassMenu(FrameworkElement anchor, List<GlassMenuItem> items)
        => GlassContextMenuHost.Show(anchor, items);

    private void ShowKeyContextMenu(FrameworkElement anchor, int globalIdx)
    {
        var items = new List<GlassMenuItem>
        {
            new("Clear Key",         Material.Icons.MaterialIconKind.Eraser,       () => ClearDisplayKey(globalIdx)),
            new("Delete Key Content",Material.Icons.MaterialIconKind.TrashCanOutline, () => ClearDisplayKey(globalIdx), IsDanger: true),
            GlassMenuItem.Sep,
        };

        // Space (folder) submenu items — reuse existing builder which adds
        // a MenuItem subtree; we'll flatten it into GlassMenuItems.
        items.AddRange(BuildFolderGlassMenuItems(globalIdx));

        items.Add(GlassMenuItem.Sep);
        items.Add(new("Copy", Material.Icons.MaterialIconKind.ContentCopy, () => CopyDisplayKeyToClipboard(globalIdx)));
        items.Add(new("Paste", Material.Icons.MaterialIconKind.ContentPaste,
            () => PasteDisplayKeyFromClipboard(globalIdx),
            IsEnabled: _scClipboardKey != null));

        ShowGlassMenu(anchor, items);
    }

    private void CopyDisplayKeyToClipboard(int globalIdx)
    {
        if (_config == null) return;
        var srcKey = GetActiveN3DisplayKeys().FirstOrDefault(k => k.Idx == globalIdx);
        _scClipboardKey = srcKey == null ? null : new StreamControllerDisplayKeyConfig
        {
            ImagePath = srcKey.ImagePath, PresetIconKind = srcKey.PresetIconKind,
            Title = srcKey.Title,
            BackgroundColor = srcKey.BackgroundColor, AccentColor = srcKey.AccentColor,
            TextPosition = srcKey.TextPosition, TextSize = srcKey.TextSize, TextColor = srcKey.TextColor,
        };
        var srcBtn = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
        _scClipboardButton = srcBtn == null ? null : new ButtonConfig
        {
            Action = srcBtn.Action, Path = srcBtn.Path, MacroKeys = srcBtn.MacroKeys,
            DeviceId = srcBtn.DeviceId, ProfileName = srcBtn.ProfileName, LinkedKnobIdx = srcBtn.LinkedKnobIdx,
            FolderName = srcBtn.FolderName,
        };
    }

    private void PasteDisplayKeyFromClipboard(int globalIdx)
    {
        if (_config == null || _scClipboardKey == null) return;
        var target = GetActiveN3DisplayKeys().FirstOrDefault(k => k.Idx == globalIdx);
        if (target != null)
        {
            target.ImagePath = _scClipboardKey.ImagePath;
            target.PresetIconKind = _scClipboardKey.PresetIconKind;
            target.Title = _scClipboardKey.Title;
            target.BackgroundColor = _scClipboardKey.BackgroundColor;
            target.AccentColor = _scClipboardKey.AccentColor;
            target.TextPosition = _scClipboardKey.TextPosition;
            target.TextSize = _scClipboardKey.TextSize;
            target.TextColor = _scClipboardKey.TextColor;
        }
        if (_scClipboardButton != null)
        {
            var btnTarget = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
            if (btnTarget != null)
            {
                btnTarget.Action = _scClipboardButton.Action;
                btnTarget.Path = _scClipboardButton.Path;
                btnTarget.MacroKeys = _scClipboardButton.MacroKeys;
                btnTarget.DeviceId = _scClipboardButton.DeviceId;
                btnTarget.ProfileName = _scClipboardButton.ProfileName;
                btnTarget.LinkedKnobIdx = _scClipboardButton.LinkedKnobIdx;
                btnTarget.FolderName = _scClipboardButton.FolderName;
            }
        }
        LoadStreamControllerConfig();
        QueueSave();
    }

    private StreamControllerDisplayKeyConfig? _scClipboardKey;
    private ButtonConfig? _scClipboardButton;

    private void ClearDisplayKey(int globalIdx)
    {
        if (_config == null) return;
        var key = GetActiveN3DisplayKeys().FirstOrDefault(k => k.Idx == globalIdx);
        if (key != null)
        {
            key.ImagePath = "";
            key.PresetIconKind = "";
            key.Title = "";
            key.BackgroundColor = "#1C1C1C";
            key.AccentColor = "#00E676";
            key.TextPosition = DisplayTextPosition.Bottom;
            key.TextSize = 14;
            key.TextColor = "#FFFFFF";
        }
        var btn = GetActiveN3ButtonList().FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
        if (btn != null)
        {
            btn.Action = "none";
            btn.Path = "";
            btn.MacroKeys = "";
            btn.DeviceId = "";
            btn.ProfileName = "";
            btn.FolderName = "";
            btn.LinkedKnobIdx = -1;
        }
        LoadStreamControllerConfig();
        QueueSave();
    }

    // ── Display type helpers ────────────────────────────────────────────

    private Border MakeClockFormatChip(string label, string format)
    {
        var chip = new Border
        {
            Background = FindBrush("InputBgBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush")
            }
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (_loading || _config == null) return;
            if (_scClockFormatBox == null) return;
            _scClockFormatBox.Text = format;
            var key = GetSelectedDisplayKeyConfig();
            if (key != null) key.ClockFormat = format;
            UpdateEditorPreviewOnly();
            QueueSave();
        };
        return chip;
    }

    private void UpdateDisplayTypeVisibility()
    {
        var dt = _scDisplayTypePicker?.SelectedTag is DisplayKeyType t ? t : DisplayKeyType.Normal;
        if (_scClockPanel != null)
            _scClockPanel.Visibility = dt == DisplayKeyType.Clock ? Visibility.Visible : Visibility.Collapsed;
        if (_scDynamicPanel != null)
            _scDynamicPanel.Visibility = dt == DisplayKeyType.DynamicState ? Visibility.Visible : Visibility.Collapsed;

        bool showNormal = dt == DisplayKeyType.Normal || dt == DisplayKeyType.SpotifyNowPlaying;
        foreach (var row in _scNormalOnlyRows)
            row.Visibility = showNormal ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseStreamControllerDynamicActiveIcon()
    {
        if (_config == null) return;
        var display = GetSelectedDisplayKeyConfig();
        if (display == null) return;

        var dialog = new StreamControllerIconPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            // Dynamic active icon is a MaterialIconKind name only — ignore downloaded images.
            if (!string.IsNullOrWhiteSpace(dialog.SelectedIconKind))
            {
                display.DynamicStateActiveIcon = dialog.SelectedIconKind!;
                if (_scDynamicActiveIconBox != null)
                    _scDynamicActiveIconBox.Text = dialog.SelectedIconKind!;
                UpdateEditorPreviewOnly();
                QueueSave();
            }
        }
    }

    // ── Image / Icon pickers ────────────────────────────────────────────

    private void ChooseStreamControllerIcon()
    {
        if (_config == null) return;
        var display = GetSelectedDisplayKeyConfig();
        if (display == null) return;

        var dialog = new StreamControllerIconPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            if (!string.IsNullOrWhiteSpace(dialog.SelectedDownloadedImagePath))
            {
                display.ImagePath = dialog.SelectedDownloadedImagePath!;
                display.PresetIconKind = "";
                LoadStreamControllerSelection();
                QueueSave();
            }
            else if (!string.IsNullOrWhiteSpace(dialog.SelectedIconKind))
            {
                display.PresetIconKind = dialog.SelectedIconKind!;
                display.ImagePath = "";
                // Carry the picker's per-icon accent forward as the glow
                // colour so the key preview matches what the user saw in
                // the picker instead of defaulting to green.
                if (dialog.SelectedAccent is Color acc)
                    display.AccentColor = $"#{acc.R:X2}{acc.G:X2}{acc.B:X2}";
                LoadStreamControllerSelection();
                QueueSave();
            }
        }
    }

    private void BrowsePathForSelectedStreamAction()
    {
        if (_scPathBox == null || _scActionPicker == null) return;
        var action = GetComboActionValue(_scActionPicker);
        if (action == "launch_exe")
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Programs|*.exe;*.bat;*.cmd|All files|*.*",
                Title = "Choose application"
            };
            if (dialog.ShowDialog() == true)
            {
                SetTextBoxValue(_scPathBox, dialog.FileName);
                QueueSave();
            }
        }
    }

    private void PickRunningAppForSelectedStreamAction()
    {
        if (_config == null || _onSave == null || _scPathBox == null) return;
        var dialog = new AmpUp.Controls.AppPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SetTextBoxValue(_scPathBox, dialog.SelectedPath);
            QueueSave();
        }
    }

        // ── Multi-Action sequence editor ────────────────────

    private StackPanel MakeStreamMultiActionPanel()
    {
        var panel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0)
        };

        panel.Children.Add(MakeEditorLabel("SEQUENCE"));

        _scMultiActionEmptyHint = new TextBlock
        {
            Text = "No steps yet. Add one below.",
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 2, 0, 8)
        };
        panel.Children.Add(_scMultiActionEmptyHint);

        _scMultiActionList = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(_scMultiActionList);

        _scMultiActionAddButton = new Button
        {
            Content = "+  Add Step",
            MinHeight = 34,
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = FindBrush("InputBgBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 2, 0, 0)
        };
        _scMultiActionAddButton.Click += (_, _) => AddMultiActionStep();
        panel.Children.Add(_scMultiActionAddButton);

        return panel;
    }

    private ButtonConfig? GetSelectedStreamButton()
    {
        if (_config == null) return null;
        return _config.N3.Buttons.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
    }

    private void AddMultiActionStep()
    {
        var button = GetSelectedStreamButton();
        if (button == null) return;
        button.ActionSequence.Add(new MultiActionStep { Action = "none", DelayMs = 0, Path = "" });
        RebuildMultiActionList();
        QueueSave();
    }

    private void RemoveMultiActionStep(int idx)
    {
        var button = GetSelectedStreamButton();
        if (button == null) return;
        if (idx < 0 || idx >= button.ActionSequence.Count) return;
        button.ActionSequence.RemoveAt(idx);
        RebuildMultiActionList();
        QueueSave();
    }

    private void MoveMultiActionStep(int idx, int delta)
    {
        var button = GetSelectedStreamButton();
        if (button == null) return;
        int newIdx = idx + delta;
        if (idx < 0 || idx >= button.ActionSequence.Count) return;
        if (newIdx < 0 || newIdx >= button.ActionSequence.Count) return;
        var step = button.ActionSequence[idx];
        button.ActionSequence.RemoveAt(idx);
        button.ActionSequence.Insert(newIdx, step);
        RebuildMultiActionList();
        QueueSave();
    }

    private void RebuildMultiActionList()
    {
        if (_scMultiActionList == null) return;
        _scMultiActionList.Children.Clear();

        var button = GetSelectedStreamButton();
        var steps = button?.ActionSequence ?? new List<MultiActionStep>();

        if (_scMultiActionEmptyHint != null)
            _scMultiActionEmptyHint.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < steps.Count; i++)
        {
            int capturedIdx = i;
            _scMultiActionList.Children.Add(BuildMultiActionRow(steps[i], capturedIdx, steps.Count));
        }
    }

    private Border BuildMultiActionRow(MultiActionStep step, int idx, int total)
    {
        var row = new Border
        {
            Background = FindBrush("BgDarkBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var content = new StackPanel();

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stepLabel = new TextBlock
        {
            Text = $"STEP {idx + 1}",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(stepLabel, 0);
        headerGrid.Children.Add(stepLabel);

        var upBtn = MakeMultiActionIconButton("▲", "Move up");
        upBtn.IsEnabled = idx > 0;
        upBtn.Opacity = idx > 0 ? 1.0 : 0.35;
        upBtn.Click += (_, _) => MoveMultiActionStep(idx, -1);
        Grid.SetColumn(upBtn, 1);
        headerGrid.Children.Add(upBtn);

        var downBtn = MakeMultiActionIconButton("▼", "Move down");
        downBtn.IsEnabled = idx < total - 1;
        downBtn.Opacity = idx < total - 1 ? 1.0 : 0.35;
        downBtn.Click += (_, _) => MoveMultiActionStep(idx, 1);
        Grid.SetColumn(downBtn, 2);
        headerGrid.Children.Add(downBtn);

        var removeBtn = MakeMultiActionIconButton("✕", "Remove step");
        removeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
        removeBtn.Click += (_, _) => RemoveMultiActionStep(idx);
        Grid.SetColumn(removeBtn, 3);
        headerGrid.Children.Add(removeBtn);

        content.Children.Add(headerGrid);

        var delayActionGrid = new Grid();
        delayActionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        delayActionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var delayStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        delayStack.Children.Add(new TextBlock
        {
            Text = "DELAY (ms)",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var delayBox = new TextBox
        {
            Text = step.DelayMs.ToString(),
            Width = 68,
            MinHeight = 34,
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CaretBrush = new SolidColorBrush(ThemeManager.Accent),
            ToolTip = "Delay in milliseconds before running this step (0 = immediate)"
        };
        delayBox.PreviewTextInput += (_, e) =>
        {
            foreach (var ch in e.Text)
            {
                if (!char.IsDigit(ch)) { e.Handled = true; return; }
            }
        };
        delayBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            var btn = GetSelectedStreamButton();
            if (btn == null || idx >= btn.ActionSequence.Count) return;
            if (int.TryParse(delayBox.Text, out var ms) && ms >= 0)
                btn.ActionSequence[idx].DelayMs = ms;
            else
                btn.ActionSequence[idx].DelayMs = 0;
            QueueSave();
        };
        delayStack.Children.Add(delayBox);
        Grid.SetColumn(delayStack, 0);
        delayActionGrid.Children.Add(delayStack);

        var actionStack = new StackPanel { Orientation = Orientation.Vertical };
        actionStack.Children.Add(new TextBlock
        {
            Text = "ACTION",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var actionPicker = MakeFilteredActionCombo(MultiActionExcluded);
        actionPicker.Margin = new Thickness(0);
        actionPicker.Select(string.IsNullOrWhiteSpace(step.Action) ? "none" : step.Action);

        var pathPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var pathLabel = new TextBlock
        {
            Text = "PATH",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        pathPanel.Children.Add(pathLabel);
        var pathBox = new TextBox
        {
            Text = step.Path ?? "",
            MinHeight = 32,
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(ThemeManager.Accent)
        };
        pathBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            var btn = GetSelectedStreamButton();
            if (btn == null || idx >= btn.ActionSequence.Count) return;
            btn.ActionSequence[idx].Path = pathBox.Text;
            QueueSave();
        };
        pathPanel.Children.Add(pathBox);

        var macroPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        macroPanel.Children.Add(new TextBlock
        {
            Text = "MACRO",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var macroBox = new TextBox
        {
            Text = step.MacroKeys ?? "",
            MinHeight = 32,
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(ThemeManager.Accent),
            ToolTip = "Keyboard shortcut, e.g. ctrl+shift+m"
        };
        macroBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            var btn = GetSelectedStreamButton();
            if (btn == null || idx >= btn.ActionSequence.Count) return;
            btn.ActionSequence[idx].MacroKeys = macroBox.Text;
            QueueSave();
        };
        macroPanel.Children.Add(macroBox);

        void RefreshOptionalRows()
        {
            var currentAction = actionPicker.SelectedValue;
            pathPanel.Visibility = MultiActionStepPathActions.Contains(currentAction) ? Visibility.Visible : Visibility.Collapsed;
            pathLabel.Text = currentAction switch
            {
                "launch_exe" => "APP PATH",
                "close_program" or "mute_program" => "PROCESS NAME",
                "open_url" => "URL",
                "sc_go_to_page" => "PAGE NUMBER",
                "ha_service" => "ENTITY ID",
                "govee_color" => "DEVICE IP / NAME",
                "obs_scene" => "SCENE NAME",
                "obs_mute" => "SOURCE NAME",
                "vm_mute_strip" or "vm_mute_bus" => "INDEX",
                _ => "PATH"
            };
            macroPanel.Visibility = currentAction == "macro" ? Visibility.Visible : Visibility.Collapsed;
        }

        actionPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var btn = GetSelectedStreamButton();
            if (btn == null || idx >= btn.ActionSequence.Count) return;
            btn.ActionSequence[idx].Action = actionPicker.SelectedValue;
            RefreshOptionalRows();
            QueueSave();
        };

        actionStack.Children.Add(actionPicker);
        actionStack.Children.Add(pathPanel);
        actionStack.Children.Add(macroPanel);

        Grid.SetColumn(actionStack, 1);
        delayActionGrid.Children.Add(actionStack);

        content.Children.Add(delayActionGrid);
        row.Child = content;

        RefreshOptionalRows();

        return row;
    }

    private Button MakeMultiActionIconButton(string glyph, string tooltip)
    {
        return new Button
        {
            Content = glyph,
            ToolTip = tooltip,
            Width = 26,
            Height = 26,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 11,
            Background = FindBrush("InputBgBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = FindBrush("TextSecBrush"),
            Cursor = Cursors.Hand
        };
    }
}
