using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

public partial class ButtonsView
{
    private enum N3EncoderEditScope
    {
        Global,
        Space,
        Page,
    }

    private Border? _v2EncoderRotationCard;
    private Border? _v2EncoderRotationBody;
    private Border? _v2EncoderRotationHeader;
    private Border? _v2EncoderRotationAccentIcon;
    private TextBlock? _v2EncoderRotationSummary;
    private MaterialIcon? _v2EncoderRotationChevron;
    private SegmentedControl? _v2EncoderScopePicker;
    private CheckBox? _v2EncoderOverrideCheck;
    private TextBlock? _v2EncoderContextHint;
    private GridPicker? _v2EncoderTargetPicker;
    private TextBox? _v2EncoderCustomTargetBox;
    private TextBox? _v2EncoderLabelBox;
    private StyledSlider? _v2EncoderSensitivitySlider;
    private TextBlock? _v2EncoderSensitivityLabel;
    private RangeSlider? _v2EncoderRangeSlider;
    private TextBlock? _v2EncoderRangeLabel;
    private TextBlock? _v2EncoderEffectiveLabel;
    private N3EncoderEditScope _v2EncoderEditScope = N3EncoderEditScope.Page;
    private bool _v2EncoderRotationExpanded;

    private void BuildV2EncoderRotationEditor()
    {
        if (_v2ActionPanel == null || _v2EncoderRotationCard != null) return;

        var content = new StackPanel();

        content.Children.Add(MakeEncoderEditorLabel("EDIT BINDING FOR"));
        _v2EncoderScopePicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _v2EncoderScopePicker.AddSegment("Global", N3EncoderEditScope.Global);
        _v2EncoderScopePicker.AddSegment("This Space", N3EncoderEditScope.Space);
        _v2EncoderScopePicker.AddSegment("This Page", N3EncoderEditScope.Page);
        _v2EncoderScopePicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _v2EncoderScopePicker.SelectedTag is not N3EncoderEditScope scope) return;
            _v2EncoderEditScope = scope;
            RefreshV2EncoderRotationEditor();
        };
        content.Children.Add(_v2EncoderScopePicker);

        _v2EncoderContextHint = new TextBlock
        {
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        content.Children.Add(_v2EncoderContextHint);

        _v2EncoderOverrideCheck = new CheckBox
        {
            Content = "Override this encoder here",
            FontSize = 11,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        _v2EncoderOverrideCheck.Checked += (_, _) => ToggleV2EncoderOverride(true);
        _v2EncoderOverrideCheck.Unchecked += (_, _) => ToggleV2EncoderOverride(false);
        content.Children.Add(_v2EncoderOverrideCheck);

        content.Children.Add(MakeEncoderEditorLabel("CONTROL TARGET"));
        _v2EncoderTargetPicker = new GridPicker
        {
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Choose what turning this encoder controls",
        };
        PopulateV2EncoderTargetPicker();
        _v2EncoderTargetPicker.SelectionChanged += (_, _) => SaveV2EncoderTarget();
        content.Children.Add(_v2EncoderTargetPicker);

        _v2EncoderCustomTargetBox = MakeEditorTextBox("Process name, for example AppleMusic");
        _v2EncoderCustomTargetBox.Margin = new Thickness(0, 0, 0, 10);
        _v2EncoderCustomTargetBox.Visibility = Visibility.Collapsed;
        _v2EncoderCustomTargetBox.LostFocus += (_, _) => SaveV2EncoderCustomTarget();
        content.Children.Add(_v2EncoderCustomTargetBox);

        content.Children.Add(MakeEncoderEditorLabel("OSD LABEL"));
        _v2EncoderLabelBox = MakeEditorTextBox("Displayed while turning the encoder");
        _v2EncoderLabelBox.Margin = new Thickness(0, 0, 0, 10);
        _v2EncoderLabelBox.LostFocus += (_, _) => SaveV2EncoderLabel();
        content.Children.Add(_v2EncoderLabelBox);

        _v2EncoderSensitivityLabel = MakeEncoderEditorLabel("SENSITIVITY: 3.2% PER CLICK");
        content.Children.Add(_v2EncoderSensitivityLabel);
        _v2EncoderSensitivitySlider = new StyledSlider
        {
            Minimum = 0.1,
            Maximum = 12.8,
            Value = 3.2,
            Step = 0.1,
            ShowLabel = false,
            Height = 28,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _v2EncoderSensitivitySlider.ValueChanged += (_, _) => SaveV2EncoderSensitivity();
        content.Children.Add(_v2EncoderSensitivitySlider);

        _v2EncoderRangeLabel = MakeEncoderEditorLabel("VOLUME RANGE: 0-100%");
        content.Children.Add(_v2EncoderRangeLabel);
        _v2EncoderRangeSlider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            LowerValue = 0,
            UpperValue = 100,
            Height = 28,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _v2EncoderRangeSlider.LowerValueChanged += (_, _) => SaveV2EncoderRange();
        _v2EncoderRangeSlider.UpperValueChanged += (_, _) => SaveV2EncoderRange();
        content.Children.Add(_v2EncoderRangeSlider);

        _v2EncoderEffectiveLabel = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            TextWrapping = TextWrapping.Wrap,
        };
        var effectivePill = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 7),
            Background = new SolidColorBrush(Color.FromArgb(
                0x12, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            Child = _v2EncoderEffectiveLabel,
        };
        content.Children.Add(effectivePill);

        _v2EncoderRotationCard = MakeV2EncoderRotationCard(content);
        _v2EncoderRotationCard.Visibility = Visibility.Collapsed;
        _v2ActionPanel.Children.Add(_v2EncoderRotationCard);
    }

    private void PopulateV2EncoderTargetPicker()
    {
        if (_v2EncoderTargetPicker == null) return;

        var picker = _v2EncoderTargetPicker;
        var muted = Color.FromRgb(0x88, 0x88, 0x88);
        var blue = Color.FromRgb(0x42, 0xA5, 0xF5);
        var green = Color.FromRgb(0x66, 0xBB, 0x6A);
        var red = Color.FromRgb(0xEF, 0x53, 0x50);
        var teal = Color.FromRgb(0x26, 0xC6, 0xDA);
        var purple = Color.FromRgb(0xAB, 0x47, 0xBC);
        var orange = Color.FromRgb(0xFF, 0xA7, 0x26);
        var yellow = Color.FromRgb(0xFF, 0xD5, 0x4F);

        picker.AddCategory("Audio");
        picker.AddItem("None", "none", "-", muted);
        picker.AddItem("Master Volume", "master", "\u266A", green);
        picker.AddItem("Microphone", "mic", "\u25CE", red);
        picker.AddItem("Active Window", "active_window", "\u25A3", purple);
        picker.AddItem("System Sounds", "system", "\u25C6", blue);
        picker.AddItem("Automatic App", "any", "\u25C8", teal);

        picker.AddCategory("Lighting");
        picker.AddItem("Room Brightness", "room_lights", "\u2600", yellow,
            "All configured room lights");
        picker.AddItem("AmpUp LED Brightness", "led_brightness", "\u25C9", orange,
            "Turn Up hardware LEDs");
        picker.AddItem("Monitor Brightness", "monitor", "\u25AD", orange,
            "Primary display");

        picker.AddCategory("Apps");
        picker.AddItem("Discord", "discord", "D", Color.FromRgb(0x58, 0x65, 0xF2));
        picker.AddItem("Spotify", "spotify", "\u266A", Color.FromRgb(0x1D, 0xB9, 0x54));
        picker.AddItem("Chrome", "chrome", "C", Color.FromRgb(0x42, 0x85, 0xF4));
        picker.AddItem("Custom App / Process", "__custom__", "+", teal);

        picker.AddCategory("Stream Controller");
        picker.AddItem("Cycle Spaces", "sc_space_cycle", "\u229E", teal);
        picker.AddItem("Cycle Pages", "sc_page_cycle", "\u25A4", orange);
    }

    private Border MakeV2EncoderRotationCard(UIElement content)
    {
        var accent = ThemeManager.Accent;
        var cardStack = new StackPanel();

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _v2EncoderRotationAccentIcon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
            Background = new SolidColorBrush(Color.FromArgb(0x16, accent.R, accent.G, accent.B)),
            Child = new TextBlock
            {
                Text = "\u21BB",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(_v2EncoderRotationAccentIcon, 0);
        headerGrid.Children.Add(_v2EncoderRotationAccentIcon);

        var titleStack = new StackPanel
        {
            Margin = new Thickness(11, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = "ROTATION",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
        });
        _v2EncoderRotationSummary = new TextBlock
        {
            Text = "Choose what this encoder controls",
            FontSize = 9.5,
            Foreground = FindBrush("TextDimBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        titleStack.Children.Add(_v2EncoderRotationSummary);
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        _v2EncoderRotationChevron = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronDown,
            Width = 20,
            Height = 20,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_v2EncoderRotationChevron, 2);
        headerGrid.Children.Add(_v2EncoderRotationChevron);

        _v2EncoderRotationHeader = new Border
        {
            Padding = new Thickness(13, 11, 13, 11),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = headerGrid,
            ToolTip = "Expand rotation settings",
        };
        _v2EncoderRotationHeader.MouseEnter += (_, _) =>
            _v2EncoderRotationHeader.Background = new SolidColorBrush(
                Color.FromArgb(0x0C, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
        _v2EncoderRotationHeader.MouseLeave += (_, _) =>
            _v2EncoderRotationHeader.Background = Brushes.Transparent;
        _v2EncoderRotationHeader.MouseLeftButtonUp += (_, e) =>
        {
            SetV2EncoderRotationExpanded(!_v2EncoderRotationExpanded);
            e.Handled = true;
        };
        cardStack.Children.Add(_v2EncoderRotationHeader);

        _v2EncoderRotationBody = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(13, 14, 13, 13),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Child = content,
        };
        _v2EncoderRotationBody.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        cardStack.Children.Add(_v2EncoderRotationBody);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            ClipToBounds = true,
            Child = cardStack,
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        SetV2EncoderRotationExpanded(false);
        return card;
    }

    private void SetV2EncoderRotationExpanded(bool expanded)
    {
        _v2EncoderRotationExpanded = expanded;
        if (_v2EncoderRotationChevron != null)
            _v2EncoderRotationChevron.Kind = expanded
                ? MaterialIconKind.ChevronUp
                : MaterialIconKind.ChevronDown;
        if (_v2EncoderRotationHeader != null)
            _v2EncoderRotationHeader.ToolTip = expanded
                ? "Collapse rotation settings"
                : "Expand rotation settings";
        if (_v2EncoderRotationBody == null) return;

        _v2EncoderRotationBody.BeginAnimation(UIElement.OpacityProperty, null);
        if (!expanded)
        {
            _v2EncoderRotationBody.Opacity = 0;
            _v2EncoderRotationBody.Visibility = Visibility.Collapsed;
            return;
        }

        _v2EncoderRotationBody.Visibility = Visibility.Visible;
        _v2EncoderRotationBody.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private TextBlock MakeEncoderEditorLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = FindBrush("TextDimBrush"),
        Margin = new Thickness(0, 0, 0, 4),
    };

    private int SelectedN3EncoderIndex =>
        _v2SelectionKind == V2SelectionKind.EncoderPress
            ? _scSelectedButtonIdx - StreamControllerEncoderPressBase
            : -1;

    private List<N3EncoderContextConfig> GetActiveEditorEncoderContexts()
    {
        if (_config == null) return new List<N3EncoderContextConfig>();
        return ActiveFolder?.EncoderContexts ?? _config.N3.EncoderContexts;
    }

    private KnobConfig? GetExactEditorEncoderBinding(N3EncoderEditScope scope, int encoderIdx)
    {
        if (_config == null || encoderIdx is < 0 or > 2) return null;
        if (scope == N3EncoderEditScope.Global)
            return _config.N3.Knobs.FirstOrDefault(k => k.Idx == encoderIdx);

        int page = scope == N3EncoderEditScope.Space ? -1 : _scCurrentPage;
        return GetActiveEditorEncoderContexts().FirstOrDefault(c => c.Page == page)
            ?.Knobs.FirstOrDefault(k => k.Idx == encoderIdx);
    }

    private KnobConfig? GetEffectiveEditorEncoderBinding(int encoderIdx)
    {
        if (_config == null || encoderIdx is < 0 or > 2) return null;
        var contexts = GetActiveEditorEncoderContexts();
        return contexts.FirstOrDefault(c => c.Page == _scCurrentPage)
                   ?.Knobs.FirstOrDefault(k => k.Idx == encoderIdx)
               ?? contexts.FirstOrDefault(c => c.Page == -1)
                   ?.Knobs.FirstOrDefault(k => k.Idx == encoderIdx)
               ?? _config.N3.Knobs.FirstOrDefault(k => k.Idx == encoderIdx);
    }

    private KnobConfig? GetInheritedEditorEncoderBinding(N3EncoderEditScope scope, int encoderIdx)
    {
        if (_config == null) return null;
        if (scope == N3EncoderEditScope.Page)
        {
            return GetExactEditorEncoderBinding(N3EncoderEditScope.Space, encoderIdx)
                   ?? GetExactEditorEncoderBinding(N3EncoderEditScope.Global, encoderIdx);
        }
        return GetExactEditorEncoderBinding(N3EncoderEditScope.Global, encoderIdx);
    }

    private KnobConfig? GetEditableEditorEncoderBinding()
    {
        int encoderIdx = SelectedN3EncoderIndex;
        if (encoderIdx < 0) return null;
        return GetExactEditorEncoderBinding(_v2EncoderEditScope, encoderIdx)
               ?? (_v2EncoderEditScope == N3EncoderEditScope.Global
                   ? CreateEditorEncoderOverride(N3EncoderEditScope.Global, encoderIdx)
                   : null);
    }

    private KnobConfig CreateEditorEncoderOverride(N3EncoderEditScope scope, int encoderIdx)
    {
        var source = GetInheritedEditorEncoderBinding(scope, encoderIdx)
                     ?? new KnobConfig { Idx = encoderIdx };
        var clone = CloneN3EncoderBinding(source, encoderIdx);

        if (scope == N3EncoderEditScope.Global)
        {
            _config!.N3.Knobs.Add(clone);
            return clone;
        }

        int page = scope == N3EncoderEditScope.Space ? -1 : _scCurrentPage;
        var contexts = GetActiveEditorEncoderContexts();
        var context = contexts.FirstOrDefault(c => c.Page == page);
        if (context == null)
        {
            context = new N3EncoderContextConfig { Page = page };
            contexts.Add(context);
        }
        context.Knobs.Add(clone);
        return clone;
    }

    private static KnobConfig CloneN3EncoderBinding(KnobConfig source, int encoderIdx) => new()
    {
        Idx = encoderIdx,
        Label = source.Label,
        Target = source.Target,
        DeviceId = source.DeviceId,
        MinVolume = source.MinVolume,
        MaxVolume = source.MaxVolume,
        Curve = source.Curve,
        Apps = new List<string>(source.Apps),
        LastRawValue = -1,
        EncoderStep = source.EncoderStep,
    };

    private void ToggleV2EncoderOverride(bool enabled)
    {
        if (_loading || _config == null || _v2EncoderEditScope == N3EncoderEditScope.Global) return;
        int encoderIdx = SelectedN3EncoderIndex;
        if (encoderIdx < 0) return;

        if (enabled)
        {
            if (GetExactEditorEncoderBinding(_v2EncoderEditScope, encoderIdx) == null)
                CreateEditorEncoderOverride(_v2EncoderEditScope, encoderIdx);
        }
        else
        {
            int page = _v2EncoderEditScope == N3EncoderEditScope.Space ? -1 : _scCurrentPage;
            var contexts = GetActiveEditorEncoderContexts();
            var context = contexts.FirstOrDefault(c => c.Page == page);
            context?.Knobs.RemoveAll(k => k.Idx == encoderIdx);
            if (context != null && context.Knobs.Count == 0)
                contexts.Remove(context);
        }

        QueueSave();
        RefreshV2EncoderRotationEditor();
        RefreshV2LeftPanel();
    }

    private void SaveV2EncoderTarget()
    {
        if (_loading || _v2EncoderTargetPicker == null) return;
        var knob = GetEditableEditorEncoderBinding();
        if (knob == null) return;

        string target = _v2EncoderTargetPicker.SelectedTag as string ?? "none";
        bool custom = target == "__custom__";
        if (!custom)
        {
            knob.Target = target;
            knob.DeviceId = "";
            if (string.IsNullOrWhiteSpace(knob.Label) || knob.Label.StartsWith("Encoder ", StringComparison.OrdinalIgnoreCase))
                knob.Label = FormatN3EncoderTarget(target);
        }
        else if (_v2EncoderCustomTargetBox != null && !string.IsNullOrWhiteSpace(_v2EncoderCustomTargetBox.Text))
        {
            knob.Target = _v2EncoderCustomTargetBox.Text.Trim();
        }

        if (_v2EncoderCustomTargetBox != null)
            _v2EncoderCustomTargetBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        QueueSave();
        // Keep the custom textbox open while the user types. Refreshing here
        // would re-select the previous stored target before LostFocus has a
        // chance to persist the new process name.
        if (!custom)
            RefreshV2EncoderRotationEditor();
        RefreshV2LeftPanel();
    }

    private void SaveV2EncoderCustomTarget()
    {
        if (_loading || _v2EncoderCustomTargetBox == null) return;
        var knob = GetEditableEditorEncoderBinding();
        if (knob == null) return;
        var value = _v2EncoderCustomTargetBox.Text.Trim();
        if (string.IsNullOrEmpty(value)) return;
        knob.Target = value;
        if (string.IsNullOrWhiteSpace(knob.Label)) knob.Label = value;
        QueueSave();
        RefreshV2EncoderRotationEditor();
        RefreshV2LeftPanel();
    }

    private void SaveV2EncoderLabel()
    {
        if (_loading || _v2EncoderLabelBox == null) return;
        var knob = GetEditableEditorEncoderBinding();
        if (knob == null) return;
        knob.Label = _v2EncoderLabelBox.Text.Trim();
        QueueSave();
        RefreshV2LeftPanel();
    }

    private void SaveV2EncoderSensitivity()
    {
        if (_loading || _v2EncoderSensitivitySlider == null) return;
        var knob = GetEditableEditorEncoderBinding();
        if (knob == null) return;
        knob.EncoderStep = Math.Clamp((int)Math.Round(_v2EncoderSensitivitySlider.Value * 10), 1, 128);
        if (_v2EncoderSensitivityLabel != null)
            _v2EncoderSensitivityLabel.Text = $"SENSITIVITY: {_v2EncoderSensitivitySlider.Value:F1}% PER CLICK";
        QueueSave();
    }

    private void SaveV2EncoderRange()
    {
        if (_loading || _v2EncoderRangeSlider == null) return;
        var knob = GetEditableEditorEncoderBinding();
        if (knob == null) return;
        knob.MinVolume = (int)Math.Round(_v2EncoderRangeSlider.LowerValue);
        knob.MaxVolume = (int)Math.Round(_v2EncoderRangeSlider.UpperValue);
        if (_v2EncoderRangeLabel != null)
            _v2EncoderRangeLabel.Text = $"{GetN3EncoderRangeName(knob.Target)}: {knob.MinVolume}-{knob.MaxVolume}%";
        QueueSave();
    }

    private void RefreshV2EncoderRotationEditor()
    {
        if (_v2EncoderRotationCard == null || _config == null) return;
        int encoderIdx = SelectedN3EncoderIndex;
        bool visible = encoderIdx is >= 0 and < 3;
        _v2EncoderRotationCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        bool previousLoading = _loading;
        _loading = true;
        try
        {
            if (_v2EncoderScopePicker != null)
                _v2EncoderScopePicker.SelectedIndex = (int)_v2EncoderEditScope;

            string spaceName = string.IsNullOrEmpty(_scActiveFolder) ? "Home" : _scActiveFolder;
            if (_v2EncoderContextHint != null)
            {
                _v2EncoderContextHint.Text = _v2EncoderEditScope switch
                {
                    N3EncoderEditScope.Global => "Fallback used everywhere that has no Space or page override.",
                    N3EncoderEditScope.Space => $"Used on every page in {spaceName}, unless that page has its own override.",
                    _ => $"Used only on {spaceName}, page {_scCurrentPage + 1}.",
                };
            }

            var exact = GetExactEditorEncoderBinding(_v2EncoderEditScope, encoderIdx);
            var shown = exact ?? GetInheritedEditorEncoderBinding(_v2EncoderEditScope, encoderIdx)
                ?? new KnobConfig { Idx = encoderIdx };
            bool editable = _v2EncoderEditScope == N3EncoderEditScope.Global || exact != null;

            if (_v2EncoderOverrideCheck != null)
            {
                _v2EncoderOverrideCheck.Visibility = _v2EncoderEditScope == N3EncoderEditScope.Global
                    ? Visibility.Collapsed : Visibility.Visible;
                _v2EncoderOverrideCheck.IsChecked = exact != null;
            }

            SelectV2EncoderTarget(shown.Target);
            if (_v2EncoderLabelBox != null)
            {
                _v2EncoderLabelBox.Text = shown.Label;
                _v2EncoderLabelBox.IsEnabled = editable;
            }
            if (_v2EncoderTargetPicker != null) _v2EncoderTargetPicker.IsEnabled = editable;
            if (_v2EncoderCustomTargetBox != null) _v2EncoderCustomTargetBox.IsEnabled = editable;
            if (_v2EncoderSensitivitySlider != null)
            {
                int step = shown.EncoderStep > 0 ? shown.EncoderStep : 32;
                _v2EncoderSensitivitySlider.Value = Math.Clamp(step / 10.0, 0.1, 12.8);
                _v2EncoderSensitivitySlider.IsEnabled = editable;
            }
            if (_v2EncoderSensitivityLabel != null)
                _v2EncoderSensitivityLabel.Text = $"SENSITIVITY: {Math.Clamp((shown.EncoderStep > 0 ? shown.EncoderStep : 32) / 10.0, 0.1, 12.8):F1}% PER CLICK";
            if (_v2EncoderRangeSlider != null)
            {
                _v2EncoderRangeSlider.LowerValue = Math.Clamp(shown.MinVolume, 0, 100);
                _v2EncoderRangeSlider.UpperValue = Math.Clamp(shown.MaxVolume, 0, 100);
                _v2EncoderRangeSlider.IsEnabled = editable;
            }
            if (_v2EncoderRangeLabel != null)
                _v2EncoderRangeLabel.Text = $"{GetN3EncoderRangeName(shown.Target)}: {shown.MinVolume}-{shown.MaxVolume}%";

            var effective = GetEffectiveEditorEncoderBinding(encoderIdx);
            string source = GetExactEditorEncoderBinding(N3EncoderEditScope.Page, encoderIdx) != null
                ? "this page"
                : GetExactEditorEncoderBinding(N3EncoderEditScope.Space, encoderIdx) != null
                    ? "this Space"
                    : "global";
            if (_v2EncoderEffectiveLabel != null)
            {
                _v2EncoderEffectiveLabel.Text = $"Effective now: {FormatN3EncoderTarget(effective?.Target)} ({source})";
            }
            if (_v2EncoderRotationSummary != null)
                _v2EncoderRotationSummary.Text = $"{FormatN3EncoderTarget(effective?.Target)}  ·  {source}";
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    private void SelectV2EncoderTarget(string? target)
    {
        if (_v2EncoderTargetPicker == null) return;
        string normalized = string.IsNullOrWhiteSpace(target) ? "none" : target;
        int index = -1;
        for (int i = 0; i < _v2EncoderTargetPicker.ItemCount; i++)
        {
            if (string.Equals(_v2EncoderTargetPicker.GetTagAt(i) as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        bool custom = index < 0;
        if (custom)
        {
            for (int i = 0; i < _v2EncoderTargetPicker.ItemCount; i++)
                if (_v2EncoderTargetPicker.GetTagAt(i) as string == "__custom__") { index = i; break; }
        }
        _v2EncoderTargetPicker.SelectedIndex = Math.Max(0, index);
        if (_v2EncoderCustomTargetBox != null)
        {
            _v2EncoderCustomTargetBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            _v2EncoderCustomTargetBox.Text = custom ? normalized : "";
        }
    }

    private static string FormatN3EncoderTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "none") return "None";
        return target.ToLowerInvariant() switch
        {
            "master" => "Master Volume",
            "mic" => "Microphone",
            "active_window" => "Active Window",
            "system" => "System Sounds",
            "any" => "Automatic App",
            "room_lights" => "Room Brightness",
            "led_brightness" => "AmpUp LED Brightness",
            "monitor" => "Monitor Brightness",
            "discord" => "Discord",
            "spotify" => "Spotify",
            "chrome" => "Chrome",
            "sc_space_cycle" => "Cycle Spaces",
            "sc_page_cycle" => "Cycle Pages",
            _ => target,
        };
    }

    private static string GetN3EncoderRangeName(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "VALUE RANGE";
        string normalized = target.ToLowerInvariant();
        return normalized == "room_lights"
               || normalized == "led_brightness"
               || normalized == "monitor"
               || normalized == "govee"
               || normalized.StartsWith("govee:", StringComparison.Ordinal)
               || normalized.StartsWith("group:", StringComparison.Ordinal)
            ? "BRIGHTNESS RANGE"
            : "VOLUME RANGE";
    }

    private void RefreshV2EncoderRotationAccent()
    {
        var accent = ThemeManager.Accent;
        _v2EncoderTargetPicker?.RefreshAccent();
        if (_v2EncoderRotationAccentIcon != null)
        {
            _v2EncoderRotationAccentIcon.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x66, accent.R, accent.G, accent.B));
            _v2EncoderRotationAccentIcon.Background = new SolidColorBrush(
                Color.FromArgb(0x16, accent.R, accent.G, accent.B));
            if (_v2EncoderRotationAccentIcon.Child is TextBlock icon)
                icon.Foreground = new SolidColorBrush(accent);
        }
        if (_v2EncoderEffectiveLabel != null)
            _v2EncoderEffectiveLabel.Foreground = new SolidColorBrush(accent);
    }

    private void RemoveActiveEditorEncoderPageOverrides(int page)
    {
        var contexts = GetActiveEditorEncoderContexts();
        contexts.RemoveAll(context => context.Page == page);
    }
}
