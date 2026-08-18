using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmpUp.Controls;

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
    private SegmentedControl? _v2EncoderScopePicker;
    private CheckBox? _v2EncoderOverrideCheck;
    private TextBlock? _v2EncoderContextHint;
    private ListPicker? _v2EncoderTargetPicker;
    private TextBox? _v2EncoderCustomTargetBox;
    private TextBox? _v2EncoderLabelBox;
    private StyledSlider? _v2EncoderSensitivitySlider;
    private TextBlock? _v2EncoderSensitivityLabel;
    private RangeSlider? _v2EncoderRangeSlider;
    private TextBlock? _v2EncoderRangeLabel;
    private TextBlock? _v2EncoderEffectiveLabel;
    private N3EncoderEditScope _v2EncoderEditScope = N3EncoderEditScope.Page;

    private static readonly (string Label, string Target)[] N3ContextTargetOptions =
    {
        ("None", "none"),
        ("Master Volume", "master"),
        ("Microphone", "mic"),
        ("Active Window", "active_window"),
        ("System Sounds", "system"),
        ("Automatic App", "any"),
        ("Discord", "discord"),
        ("Spotify", "spotify"),
        ("Chrome", "chrome"),
        ("Cycle Spaces", "sc_space_cycle"),
        ("Cycle Pages", "sc_page_cycle"),
        ("Custom App / Process", "__custom__"),
    };

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

        content.Children.Add(MakeEncoderEditorLabel("TARGET"));
        _v2EncoderTargetPicker = new ListPicker { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var option in N3ContextTargetOptions)
            _v2EncoderTargetPicker.AddItem(option.Label, option.Target);
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
        content.Children.Add(_v2EncoderEffectiveLabel);

        _v2EncoderRotationCard = MakeV2CommonFieldCard("ROTATION", content);
        _v2EncoderRotationCard.Visibility = Visibility.Collapsed;
        _v2ActionPanel.Children.Add(_v2EncoderRotationCard);
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
            _v2EncoderRangeLabel.Text = $"VOLUME RANGE: {knob.MinVolume}-{knob.MaxVolume}%";
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
                _v2EncoderRangeLabel.Text = $"VOLUME RANGE: {shown.MinVolume}-{shown.MaxVolume}%";

            var effective = GetEffectiveEditorEncoderBinding(encoderIdx);
            if (_v2EncoderEffectiveLabel != null)
            {
                string source = GetExactEditorEncoderBinding(N3EncoderEditScope.Page, encoderIdx) != null
                    ? "this page"
                    : GetExactEditorEncoderBinding(N3EncoderEditScope.Space, encoderIdx) != null
                        ? "this Space"
                        : "global";
                _v2EncoderEffectiveLabel.Text = $"Effective now: {FormatN3EncoderTarget(effective?.Target)} ({source})";
            }
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
            "discord" => "Discord",
            "spotify" => "Spotify",
            "chrome" => "Chrome",
            "sc_space_cycle" => "Cycle Spaces",
            "sc_page_cycle" => "Cycle Pages",
            _ => target,
        };
    }

    private void RemoveActiveEditorEncoderPageOverrides(int page)
    {
        var contexts = GetActiveEditorEncoderContexts();
        contexts.RemoveAll(context => context.Page == page);
    }
}
