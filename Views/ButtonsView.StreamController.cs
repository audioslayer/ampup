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
    private TextBox? _scSubtitleBox;
    private SegmentedControl? _scTextPositionPicker;
    private StyledSlider? _scTextSizeSlider;
    private TextBlock? _scTextSizeLabel;
    private TextBox? _scTextColorBox;
    private TextBox? _scImagePathBox;
    private Button? _scBrowseImageButton;
    private Button? _scClearImageButton;
    private TextBox? _scPresetIconBox;
    private Button? _scChoosePresetButton;
    private Button? _scClearPresetButton;
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
    private ListPicker? _scDevicePicker;
    private StackPanel? _scDevicePanel;
    private ListPicker? _scKnobPicker;
    private StackPanel? _scKnobPanel;

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

    private void BuildStreamControllerDesigner()
    {
        StreamControllerRoot.Children.Clear();

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        // ── LEFT: Center Stage ──────────────────────────────────────────
        var left = new StackPanel();

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
                if (_scSelectedButtonIdx != PagedDisplayKeyBase + localIdx)
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            };
            card.MouseLeave += (_, _) =>
            {
                if (_scSelectedButtonIdx != PagedDisplayKeyBase + localIdx)
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
                SelectStreamControllerItem(new StreamControllerSelection(
                    PagedDisplayKeyBase + localIdx,
                    $"Key {_scCurrentPage * StreamControllerKeysPerPage + localIdx + 1}",
                    _scCurrentPage * StreamControllerKeysPerPage + localIdx));
            };
            card.MouseRightButtonUp += (_, e) =>
            {
                ShowKeyContextMenu(card, _scCurrentPage * StreamControllerKeysPerPage + localIdx);
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
        _scSubtitleBox = MakeEditorTextBox("Display subtitle");
        _scSubtitleBox.TextChanged += (_, _) => { if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); } };
        _scImagePathBox = MakeEditorTextBox("No image selected");
        _scImagePathBox.IsReadOnly = true;
        _scBrowseImageButton = MakeEditorButton("Browse Image", (_, _) => BrowseStreamControllerImage());
        _scClearImageButton = MakeEditorButton("Clear Image", (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key == null) return;
            key.ImagePath = "";
            LoadStreamControllerSelection();
            QueueSave();
        });
        _scPresetIconBox = MakeEditorTextBox("No preset icon selected");
        _scPresetIconBox.IsReadOnly = true;
        _scChoosePresetButton = MakeEditorButton("Choose Preset", (_, _) => ChooseStreamControllerPresetIcon());
        _scClearPresetButton = MakeEditorButton("Clear Preset", (_, _) =>
        {
            if (_loading || _config == null) return;
            var key = GetSelectedDisplayKeyConfig();
            if (key == null) return;
            key.PresetIconKind = "";
            LoadStreamControllerSelection();
            QueueSave();
        });

        _scDisplayTabContent.Children.Add(MakeEditorLabel("TITLE"));
        _scDisplayTabContent.Children.Add(_scTitleBox);
        _scDisplayTabContent.Children.Add(MakeEditorLabel("SUBTITLE"));
        _scDisplayTabContent.Children.Add(_scSubtitleBox);

        // Text position picker
        _scDisplayTabContent.Children.Add(MakeEditorLabel("TEXT POSITION"));
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

        // Text color
        _scDisplayTabContent.Children.Add(MakeEditorLabel("TEXT COLOR"));
        _scTextColorBox = MakeEditorTextBox("#FFFFFF");
        _scTextColorBox.MaxLength = 7;
        _scTextColorBox.TextChanged += (_, _) => { if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); } };
        _scDisplayTabContent.Children.Add(_scTextColorBox);

        _scDisplayTabContent.Children.Add(MakeEditorLabel("IMAGE"));
        _scDisplayTabContent.Children.Add(_scImagePathBox);
        var imageButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        imageButtonRow.Children.Add(_scBrowseImageButton);
        _scClearImageButton.Margin = new Thickness(8, 0, 0, 0);
        imageButtonRow.Children.Add(_scClearImageButton);
        _scDisplayTabContent.Children.Add(imageButtonRow);
        _scDisplayTabContent.Children.Add(MakeEditorLabel("PRESET ICON"));
        _scDisplayTabContent.Children.Add(_scPresetIconBox);
        var presetButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        presetButtonRow.Children.Add(_scChoosePresetButton);
        _scClearPresetButton.Margin = new Thickness(8, 0, 0, 0);
        presetButtonRow.Children.Add(_scClearPresetButton);
        _scDisplayTabContent.Children.Add(presetButtonRow);

        right.Children.Add(_scDisplayTabContent);

        // ── Action tab content ──────────────────────────────────────────
        _scActionTabContent = new StackPanel { Visibility = Visibility.Collapsed };

        _scActionTabContent.Children.Add(MakeEditorLabel("ACTION"));
        _scActionPicker = MakeActionCombo();
        _scActionPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateStreamControllerActionVisibility();
            QueueSave();
        };
        _scActionTabContent.Children.Add(_scActionPicker);

        (_scPathPanel, _scPathBox, _scPathLabel, _scBrowsePathButton, _scPickPathButton, _scAppChip) = MakeStreamPathRow();
        (_scMacroPanel, _scMacroBox) = MakeStreamMacroRow();
        (_scDevicePanel, _scDevicePicker) = MakeStreamDeviceRow();
        (_scKnobPanel, _scKnobPicker) = MakeStreamKnobRow();

        _scActionTabContent.Children.Add(_scPathPanel);
        _scActionTabContent.Children.Add(_scMacroPanel);
        _scActionTabContent.Children.Add(_scDevicePanel);
        _scActionTabContent.Children.Add(_scKnobPanel);

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
        _config.N3.PageCount = _scPageCount;
        EnsureStreamControllerPageConfigs(_scPageCount - 1);
        NavigateStreamControllerPage(_scPageCount - 1 - _scCurrentPage);
    }

    private void RemoveStreamControllerPage()
    {
        if (_config == null || _scPageCount <= 1) return;
        int removedPage = _scPageCount - 1;

        // Remove configs for the last page
        int startIdx = removedPage * StreamControllerKeysPerPage;
        _config.N3.DisplayKeys.RemoveAll(k => k.Idx >= startIdx && k.Idx < startIdx + StreamControllerKeysPerPage);
        int btnStart = StreamControllerDisplayKeyBase + startIdx;
        _config.N3.Buttons.RemoveAll(b => b.Idx >= btnStart && b.Idx < btnStart + StreamControllerKeysPerPage);

        _scPageCount--;
        _config.N3.PageCount = _scPageCount;
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
        for (int i = 0; i < StreamControllerKeysPerPage; i++)
        {
            int globalIdx = page * StreamControllerKeysPerPage + i;
            if (!_config.N3.DisplayKeys.Any(k => k.Idx == globalIdx))
                _config.N3.DisplayKeys.Add(new StreamControllerDisplayKeyConfig { Idx = globalIdx });
            int buttonIdx = StreamControllerDisplayKeyBase + globalIdx;
            if (!_config.N3.Buttons.Any(b => b.Idx == buttonIdx))
                _config.N3.Buttons.Add(new ButtonConfig { Idx = buttonIdx });
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

    private (StackPanel panel, ListPicker picker) MakeStreamDeviceRow()
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(MakeEditorLabel("DEVICE"));
        var picker = new ListPicker();
        picker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        panel.Children.Add(picker);
        return (panel, picker);
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

    // ── Config load/save ────────────────────────────────────────────────

    private void LoadStreamControllerConfig()
    {
        if (_config == null || _scActionPicker == null || _scDevicePicker == null || _scKnobPicker == null) return;

        _scCurrentPage = Math.Clamp(_config.N3.CurrentPage, 0, Math.Max(0, _config.N3.PageCount - 1));
        _scPageCount = Math.Max(1, _config.N3.PageCount);

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

        for (int i = 0; i < 6; i++)
        {
            int globalIdx = _scCurrentPage * StreamControllerKeysPerPage + i;
            var key = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == globalIdx) ?? new StreamControllerDisplayKeyConfig { Idx = globalIdx };
            _scDisplayImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            _scDisplayCaptions[i].Text = string.IsNullOrWhiteSpace(key.Title) ? $"Key {globalIdx + 1}" : key.Title;
        }

        for (int i = 0; i < 3; i++)
        {
            var side = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerSideButtonBase + i);
            _scSideLabels[i].Text = GetStreamActionDisplay(side?.Action);
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerEncoderPressBase + i);
            _scPressLabels[i].Text = GetStreamActionDisplay(press?.Action);
        }

        RefreshStreamControllerPageUI();
        LoadStreamControllerSelection();
    }

    private void LoadStreamControllerSelection()
    {
        if (_config == null || _scActionPicker == null || _scPathBox == null || _scMacroBox == null || _scDevicePicker == null || _scKnobPicker == null
            || _scEditorTitle == null || _scEditorPreview == null || _scTitleBox == null || _scSubtitleBox == null || _scImagePathBox == null || _scPresetIconBox == null)
            return;

        var button = _config.N3.Buttons.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx) ?? new ButtonConfig { Idx = _scSelectedButtonIdx };
        var selection = DescribeSelection(_scSelectedButtonIdx);
        _scEditorTitle.Text = selection.Label;

        SelectCombo(_scActionPicker, button.Action);
        SetTextBoxValue(_scPathBox, ExtractPathBoxValue(button.Action, button.Path));
        SetTextBoxValue(_scMacroBox, button.MacroKeys);
        SelectDevicePicker(_scDevicePicker, button.DeviceId);
        SelectKnobPicker(_scKnobPicker, button.LinkedKnobIdx);
        SelectHaSubTag(_scActionPicker, button.Action, button.Path);
        SelectDeviceSubTag(_scActionPicker, button.Action, button.DeviceId);
        SelectProfileSubTag(_scActionPicker, button.Action, button.ProfileName);
        SelectGroupSubTag(_scActionPicker, button.Action, button.Path);
        SelectGoveeSubTag(_scActionPicker, button.Action, button.Path);

        if (selection.DisplayIdx.HasValue)
        {
            var key = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == selection.DisplayIdx.Value) ?? new StreamControllerDisplayKeyConfig { Idx = selection.DisplayIdx.Value };
            _scTitleBox.Text = key.Title;
            _scSubtitleBox.Text = key.Subtitle;
            _scImagePathBox.Text = string.IsNullOrWhiteSpace(key.ImagePath) ? "No image selected" : key.ImagePath;
            _scPresetIconBox.Text = string.IsNullOrWhiteSpace(key.PresetIconKind) ? "No preset icon selected" : key.PresetIconKind;
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
            if (_scTextColorBox != null) _scTextColorBox.Text = string.IsNullOrWhiteSpace(key.TextColor) ? "#FFFFFF" : key.TextColor;
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
            _scSubtitleBox.Text = "";
            _scImagePathBox.Text = "";
            _scPresetIconBox.Text = "";
            _scEditorPreview.Source = null;
            _scDisplayDesignPanel!.Visibility = Visibility.Collapsed;

            // Non-LCD controls: force Action tab, hide Display tab
            if (_scDisplayTabBtn != null) _scDisplayTabBtn.Visibility = Visibility.Collapsed;
            SwitchStreamControllerEditorTab(showDisplay: false);
        }

        UpdateStreamControllerActionVisibility();
        RefreshStreamControllerSelectionVisuals();
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
        _scDevicePanel.Visibility = action is "select_output" or "select_input" or "mute_device" ? Visibility.Visible : Visibility.Collapsed;
        _scKnobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;

        if (_scPathPanel.Visibility == Visibility.Visible)
        {
            if (action == "sc_go_to_page")
            {
                _scPathLabel.Text = "PAGE NUMBER";
                _scPathBox.Tag = "Page number (1-based)";
                _scBrowsePathButton.Visibility = Visibility.Collapsed;
                _scPickPathButton.Visibility = Visibility.Collapsed;
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

        var button = _config.N3.Buttons.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
        if (button == null) return;

        button.Action = GetComboActionValue(_scActionPicker);
        button.Path = GetActionPath(_scActionPicker, _scPathBox);
        button.MacroKeys = GetTextBoxValue(_scMacroBox);
        button.DeviceId = GetDeviceIdForAction(button.Action, _scActionPicker, _scDevicePicker);
        button.ProfileName = button.Action == "switch_profile" ? (_scActionPicker.SelectedSubTag ?? "") : "";
        button.LinkedKnobIdx = int.TryParse(_scKnobPicker.SelectedTag as string, out var linked) ? linked : -1;

        var display = GetSelectedDisplayKeyConfig();
        if (display != null && _scTitleBox != null && _scSubtitleBox != null)
        {
            display.Title = _scTitleBox.Text.Trim();
            display.Subtitle = _scSubtitleBox.Text.Trim();
            if (_scTextPositionPicker?.SelectedTag is DisplayTextPosition textPos)
                display.TextPosition = textPos;
            if (_scTextSizeSlider != null)
                display.TextSize = (int)Math.Round(_scTextSizeSlider.Value);
            if (_scTextColorBox != null && !string.IsNullOrWhiteSpace(_scTextColorBox.Text))
                display.TextColor = _scTextColorBox.Text.Trim();
        }

        LoadStreamControllerConfig();
    }

    private void SelectStreamControllerItem(StreamControllerSelection selection)
    {
        _scSelectedButtonIdx = selection.ButtonIdx;
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
            return new StreamControllerSelection(buttonIdx, $"Side Button {buttonIdx - StreamControllerSideButtonBase + 1}", null);
        return new StreamControllerSelection(buttonIdx, $"Encoder Press {buttonIdx - StreamControllerEncoderPressBase + 1}", null);
    }

    private StreamControllerDisplayKeyConfig? GetSelectedDisplayKeyConfig()
    {
        if (_config == null) return null;
        if (_scSelectedButtonIdx < StreamControllerDisplayKeyBase)
            return null;
        int globalIdx = _scSelectedButtonIdx - StreamControllerDisplayKeyBase;
        if (globalIdx >= _scPageCount * StreamControllerKeysPerPage)
            return null;
        return _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == globalIdx);
    }

    private void UpdateEditorPreviewOnly()
    {
        var display = GetSelectedDisplayKeyConfig();
        if (display == null || _scEditorPreview == null || _scTitleBox == null || _scSubtitleBox == null) return;

        display.Title = _scTitleBox.Text.Trim();
        display.Subtitle = _scSubtitleBox.Text.Trim();
        if (_scTextPositionPicker?.SelectedTag is DisplayTextPosition pos)
            display.TextPosition = pos;
        if (_scTextSizeSlider != null)
            display.TextSize = (int)Math.Round(_scTextSizeSlider.Value);
        if (_scTextColorBox != null && !string.IsNullOrWhiteSpace(_scTextColorBox.Text))
            display.TextColor = _scTextColorBox.Text.Trim();
        _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(display);

        int localIdx = display.Idx - (_scCurrentPage * StreamControllerKeysPerPage);
        if (localIdx >= 0 && localIdx < 6)
        {
            _scDisplayImages[localIdx].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(display);
            _scDisplayCaptions[localIdx].Text = string.IsNullOrWhiteSpace(display.Title) ? $"Key {display.Idx + 1}" : display.Title;
        }
    }

    private void RefreshStreamControllerSelectionVisuals()
    {
        for (int i = 0; i < 6; i++)
        {
            bool active = _scSelectedButtonIdx == PagedDisplayKeyBase + i;
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

    // ── Context menu ─────────────────────────────────────────────────────

    private void ShowKeyContextMenu(Border card, int globalIdx)
    {
        var menu = new ContextMenu();
        var clearItem = new MenuItem { Header = "Clear Key" };
        clearItem.Click += (_, _) => ClearDisplayKey(globalIdx);
        menu.Items.Add(clearItem);

        var deleteItem = new MenuItem { Header = "Delete Key Content" };
        deleteItem.Click += (_, _) => ClearDisplayKey(globalIdx);
        menu.Items.Add(deleteItem);

        menu.Items.Add(new Separator());

        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += (_, _) =>
        {
            if (_config == null) return;
            var srcKey = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == globalIdx);
            _scClipboardKey = srcKey == null ? null : new StreamControllerDisplayKeyConfig
            {
                ImagePath = srcKey.ImagePath, PresetIconKind = srcKey.PresetIconKind,
                Title = srcKey.Title, Subtitle = srcKey.Subtitle,
                BackgroundColor = srcKey.BackgroundColor, AccentColor = srcKey.AccentColor,
                TextPosition = srcKey.TextPosition, TextSize = srcKey.TextSize, TextColor = srcKey.TextColor,
            };
            var srcBtn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
            _scClipboardButton = srcBtn == null ? null : new ButtonConfig
            {
                Action = srcBtn.Action, Path = srcBtn.Path, MacroKeys = srcBtn.MacroKeys,
                DeviceId = srcBtn.DeviceId, ProfileName = srcBtn.ProfileName, LinkedKnobIdx = srcBtn.LinkedKnobIdx,
            };
        };
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = "Paste", IsEnabled = _scClipboardKey != null };
        pasteItem.Click += (_, _) =>
        {
            if (_config == null || _scClipboardKey == null) return;
            var target = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == globalIdx);
            if (target != null)
            {
                target.ImagePath = _scClipboardKey.ImagePath;
                target.PresetIconKind = _scClipboardKey.PresetIconKind;
                target.Title = _scClipboardKey.Title;
                target.Subtitle = _scClipboardKey.Subtitle;
                target.BackgroundColor = _scClipboardKey.BackgroundColor;
                target.AccentColor = _scClipboardKey.AccentColor;
                target.TextPosition = _scClipboardKey.TextPosition;
                target.TextSize = _scClipboardKey.TextSize;
                target.TextColor = _scClipboardKey.TextColor;
            }
            if (_scClipboardButton != null)
            {
                var btnTarget = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
                if (btnTarget != null)
                {
                    btnTarget.Action = _scClipboardButton.Action;
                    btnTarget.Path = _scClipboardButton.Path;
                    btnTarget.MacroKeys = _scClipboardButton.MacroKeys;
                    btnTarget.DeviceId = _scClipboardButton.DeviceId;
                    btnTarget.ProfileName = _scClipboardButton.ProfileName;
                    btnTarget.LinkedKnobIdx = _scClipboardButton.LinkedKnobIdx;
                }
            }
            LoadStreamControllerConfig();
            QueueSave();
        };
        menu.Items.Add(pasteItem);

        card.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private StreamControllerDisplayKeyConfig? _scClipboardKey;
    private ButtonConfig? _scClipboardButton;

    private void ClearDisplayKey(int globalIdx)
    {
        if (_config == null) return;
        var key = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == globalIdx);
        if (key != null)
        {
            key.ImagePath = "";
            key.PresetIconKind = "";
            key.Title = "";
            key.Subtitle = "";
            key.BackgroundColor = "#1C1C1C";
            key.AccentColor = "#00E676";
            key.TextPosition = DisplayTextPosition.Bottom;
            key.TextSize = 14;
            key.TextColor = "#FFFFFF";
        }
        var btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerDisplayKeyBase + globalIdx);
        if (btn != null)
        {
            btn.Action = "none";
            btn.Path = "";
            btn.MacroKeys = "";
            btn.DeviceId = "";
            btn.ProfileName = "";
            btn.LinkedKnobIdx = -1;
        }
        LoadStreamControllerConfig();
        QueueSave();
    }

    // ── Image / Icon pickers ────────────────────────────────────────────

    private void BrowseStreamControllerImage()
    {
        if (_config == null) return;
        var display = GetSelectedDisplayKeyConfig();
        if (display == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Title = "Choose Stream Controller image"
        };

        if (dialog.ShowDialog() == true)
        {
            display.ImagePath = dialog.FileName;
            display.PresetIconKind = "";
            LoadStreamControllerSelection();
            QueueSave();
        }
    }

    private void ChooseStreamControllerPresetIcon()
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
}
