using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class ButtonsView
{
    private const int StreamControllerDisplayKeyBase = 100;
    private const int StreamControllerSideButtonBase = 106;
    private const int StreamControllerEncoderPressBase = 109;

    private readonly Border[] _scDisplayCards = new Border[6];
    private readonly Image[] _scDisplayImages = new Image[6];
    private readonly TextBlock[] _scDisplayCaptions = new TextBlock[6];
    private readonly Border[] _scSideCards = new Border[3];
    private readonly Border[] _scPressCards = new Border[3];
    private readonly TextBlock[] _scSideLabels = new TextBlock[3];
    private readonly TextBlock[] _scPressLabels = new TextBlock[3];

    private TextBlock? _scEditorTitle;
    private Image? _scEditorPreview;
    private TextBox? _scTitleBox;
    private TextBox? _scSubtitleBox;
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
    private Border? _scTurnUpHint;

    private int _scSelectedButtonIdx = StreamControllerDisplayKeyBase;

    private sealed record StreamControllerSelection(int ButtonIdx, string Label, int? DisplayIdx);

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

        var left = new StackPanel();
        var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };

        left.Children.Add(MakeStreamHeader("LCD KEYS", "6 visual keys with image + action binding"));
        var keyGrid = new UniformGrid
        {
            Columns = 3,
            Rows = 2,
            Margin = new Thickness(0, 8, 0, 16)
        };

        for (int i = 0; i < 6; i++)
        {
            int displayIdx = i;
            var card = new Border
            {
                Background = FindBrush("BgDarkBrush"),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(i % 3 == 0 ? 0 : 6, i < 3 ? 0 : 6, i % 3 == 2 ? 0 : 6, 0),
                Padding = new Thickness(10),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();
            var imageBorder = new Border
            {
                Height = 110,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            imageBorder.Child = previewImage;
            var caption = new TextBlock
            {
                Text = $"Key {i + 1}",
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(imageBorder);
            stack.Children.Add(caption);
            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(
                StreamControllerDisplayKeyBase + displayIdx,
                $"Display Key {displayIdx + 1}",
                displayIdx));

            _scDisplayCards[i] = card;
            _scDisplayImages[i] = previewImage;
            _scDisplayCaptions[i] = caption;
            keyGrid.Children.Add(card);
        }

        left.Children.Add(keyGrid);
        left.Children.Add(MakeStreamHeader("QUICK CONTROLS", "3 side buttons and 3 encoder presses"));

        var sideWrap = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 10) };
        for (int i = 0; i < 3; i++)
        {
            int buttonIdx = StreamControllerSideButtonBase + i;
            var card = MakeSmallControlCard($"Side {i + 1}", out var label);
            card.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0);
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, $"Side Button {i + 1}", null));
            _scSideCards[i] = card;
            _scSideLabels[i] = label;
            sideWrap.Children.Add(card);
        }
        left.Children.Add(sideWrap);

        var pressWrap = new UniformGrid { Columns = 3 };
        for (int i = 0; i < 3; i++)
        {
            int buttonIdx = StreamControllerEncoderPressBase + i;
            var card = MakeSmallControlCard($"Knob {i + 1}", out var label);
            card.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0);
            card.MouseLeftButtonUp += (_, _) => SelectStreamControllerItem(new StreamControllerSelection(buttonIdx, $"Encoder Press {i + 1}", null));
            _scPressCards[i] = card;
            _scPressLabels[i] = label;
            pressWrap.Children.Add(card);
        }
        left.Children.Add(pressWrap);

        _scEditorTitle = new TextBlock
        {
            Text = "Display Key 1",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        right.Children.Add(_scEditorTitle);

        var previewCard = new Border
        {
            Background = FindBrush("BgDarkBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        _scEditorPreview = new Image
        {
            Height = 180,
            Stretch = Stretch.Uniform
        };
        previewCard.Child = _scEditorPreview;
        right.Children.Add(previewCard);

        _scDisplayDesignPanel = new StackPanel();
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

        _scDisplayDesignPanel.Children.Add(MakeEditorLabel("DISPLAY TITLE"));
        _scDisplayDesignPanel.Children.Add(_scTitleBox);
        _scDisplayDesignPanel.Children.Add(MakeEditorLabel("DISPLAY SUBTITLE"));
        _scDisplayDesignPanel.Children.Add(_scSubtitleBox);
        _scDisplayDesignPanel.Children.Add(MakeEditorLabel("IMAGE"));
        _scDisplayDesignPanel.Children.Add(_scImagePathBox);
        var imageButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
        imageButtonRow.Children.Add(_scBrowseImageButton);
        _scClearImageButton.Margin = new Thickness(8, 0, 0, 0);
        imageButtonRow.Children.Add(_scClearImageButton);
        _scDisplayDesignPanel.Children.Add(imageButtonRow);
        _scDisplayDesignPanel.Children.Add(MakeEditorLabel("PRESET ICON"));
        _scDisplayDesignPanel.Children.Add(_scPresetIconBox);
        var presetButtonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
        presetButtonRow.Children.Add(_scChoosePresetButton);
        _scClearPresetButton.Margin = new Thickness(8, 0, 0, 0);
        presetButtonRow.Children.Add(_scClearPresetButton);
        _scDisplayDesignPanel.Children.Add(presetButtonRow);
        right.Children.Add(_scDisplayDesignPanel);

        right.Children.Add(MakeStreamHeader("ACTION", "Choose what this control does when pressed"));
        _scActionPicker = MakeActionCombo();
        _scActionPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateStreamControllerActionVisibility();
            QueueSave();
        };
        right.Children.Add(_scActionPicker);

        (_scPathPanel, _scPathBox, _scPathLabel, _scBrowsePathButton, _scPickPathButton, _scAppChip) = MakeStreamPathRow();
        (_scMacroPanel, _scMacroBox) = MakeStreamMacroRow();
        (_scDevicePanel, _scDevicePicker) = MakeStreamDeviceRow();
        (_scKnobPanel, _scKnobPicker) = MakeStreamKnobRow();

        right.Children.Add(_scPathPanel);
        right.Children.Add(_scMacroPanel);
        right.Children.Add(_scDevicePanel);
        right.Children.Add(_scKnobPanel);

        _scTurnUpHint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 14, 0, 0),
            Child = new TextBlock
            {
                Text = "Encoder turns still mirror the first three Amp Up knobs. This editor is for LCD keys, side buttons, and encoder presses.",
                Foreground = FindBrush("TextSecBrush"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        right.Children.Add(_scTurnUpHint);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);

        StreamControllerRoot.Children.Add(root);
    }

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
            MinHeight = 34,
            Text = "",
            Tag = placeholder
        };
    }

    private static Button MakeEditorButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            Padding = new Thickness(14, 6, 14, 6)
        };
        button.Click += onClick;
        return button;
    }

    private StackPanel MakeStreamHeader(string title, string subtitle)
    {
        var stack = new StackPanel();
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

    private void LoadStreamControllerConfig()
    {
        if (_config == null || _scActionPicker == null || _scDevicePicker == null || _scKnobPicker == null) return;

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
            _config.Buttons.Concat(_config.N3.Buttons).Any(b => b.Action == "group_toggle" || b.DoublePressAction == "group_toggle" || b.HoldAction == "group_toggle"));

        PopulateDevicePicker(_scDevicePicker);
        PopulateKnobPicker(_scKnobPicker, _config);

        for (int i = 0; i < 6; i++)
        {
            var key = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i) ?? new StreamControllerDisplayKeyConfig { Idx = i };
            _scDisplayImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            _scDisplayCaptions[i].Text = string.IsNullOrWhiteSpace(key.Title) ? $"Key {i + 1}" : key.Title;
        }

        for (int i = 0; i < 3; i++)
        {
            var side = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerSideButtonBase + i);
            _scSideLabels[i].Text = GetStreamActionDisplay(side?.Action);
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == StreamControllerEncoderPressBase + i);
            _scPressLabels[i].Text = GetStreamActionDisplay(press?.Action);
        }

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
            _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);
            _scDisplayDesignPanel!.Visibility = Visibility.Visible;
        }
        else
        {
            _scTitleBox.Text = "";
            _scSubtitleBox.Text = "";
            _scImagePathBox.Text = "Display designer is only available for LCD keys";
            _scPresetIconBox.Text = "";
            _scEditorPreview.Source = null;
            _scDisplayDesignPanel!.Visibility = Visibility.Collapsed;
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
        _scPathPanel.Visibility = PathActions.Contains(action) || action is "ha_service" or "govee_color" or "obs_scene" or "obs_mute" or "vm_mute_strip" or "vm_mute_bus"
            ? Visibility.Visible
            : Visibility.Collapsed;
        _scMacroPanel.Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _scDevicePanel.Visibility = action is "select_output" or "select_input" or "mute_device" ? Visibility.Visible : Visibility.Collapsed;
        _scKnobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;

        if (_scPathPanel.Visibility == Visibility.Visible)
            ApplyPathLabelAndButtons(_scPathLabel, _scPathBox, _scBrowsePathButton, _scPickPathButton, action, _scAppChip);

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
        if (buttonIdx >= StreamControllerDisplayKeyBase && buttonIdx < StreamControllerDisplayKeyBase + 6)
        {
            int displayIdx = buttonIdx - StreamControllerDisplayKeyBase;
            return new StreamControllerSelection(buttonIdx, $"Display Key {displayIdx + 1}", displayIdx);
        }
        if (buttonIdx >= StreamControllerSideButtonBase && buttonIdx < StreamControllerSideButtonBase + 3)
            return new StreamControllerSelection(buttonIdx, $"Side Button {buttonIdx - StreamControllerSideButtonBase + 1}", null);
        return new StreamControllerSelection(buttonIdx, $"Encoder Press {buttonIdx - StreamControllerEncoderPressBase + 1}", null);
    }

    private StreamControllerDisplayKeyConfig? GetSelectedDisplayKeyConfig()
    {
        if (_config == null) return null;
        if (_scSelectedButtonIdx < StreamControllerDisplayKeyBase || _scSelectedButtonIdx >= StreamControllerDisplayKeyBase + 6)
            return null;
        int displayIdx = _scSelectedButtonIdx - StreamControllerDisplayKeyBase;
        return _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == displayIdx);
    }

    private void UpdateEditorPreviewOnly()
    {
        var display = GetSelectedDisplayKeyConfig();
        if (display == null || _scEditorPreview == null || _scTitleBox == null || _scSubtitleBox == null) return;

        display.Title = _scTitleBox.Text.Trim();
        display.Subtitle = _scSubtitleBox.Text.Trim();
        _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(display);
        int idx = display.Idx;
        _scDisplayImages[idx].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(display);
        _scDisplayCaptions[idx].Text = string.IsNullOrWhiteSpace(display.Title) ? $"Key {idx + 1}" : display.Title;
    }

    private void RefreshStreamControllerSelectionVisuals()
    {
        for (int i = 0; i < 6; i++)
        {
            bool active = _scSelectedButtonIdx == StreamControllerDisplayKeyBase + i;
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
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedIconKind))
        {
            display.PresetIconKind = dialog.SelectedIconKind!;
            display.ImagePath = "";
            LoadStreamControllerSelection();
            QueueSave();
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
