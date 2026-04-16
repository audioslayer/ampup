using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class ButtonsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    // Action definitions — trimmed to actions currently handled by the Mac backend
    private static readonly (string Display, string Value, MaterialIconKind Icon, string Category, Color Color)[] AllActions =
    {
        ("None",           "none",             MaterialIconKind.MinusCircleOutline, "Media",  Color.Parse("#444444")),
        ("Play / Pause",   "media_play_pause", MaterialIconKind.PlayPause,          "Media",  Color.Parse("#66BB6A")),
        ("Next Track",     "media_next",       MaterialIconKind.SkipNext,           "Media",  Color.Parse("#66BB6A")),
        ("Prev Track",     "media_prev",       MaterialIconKind.SkipPrevious,       "Media",  Color.Parse("#66BB6A")),
        ("Switch Profile", "switch_profile",   MaterialIconKind.SwapHorizontal,     "System", Color.Parse("#29B6F6")),
        ("Quick Wheel",    "quick_wheel",      MaterialIconKind.FerrisWheel,        "System", Color.Parse("#80DEEA")),
    };

    private static readonly string[] PathActions = Array.Empty<string>();
    private static readonly string[] MacroActions = Array.Empty<string>();
    private static readonly string[] ProfileActions = { "switch_profile" };
    private static readonly string[] KnobActions = Array.Empty<string>();

    // Per-column controls
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly MaterialIcon[] _headerIcons = new MaterialIcon[5];
    private readonly TextBlock[] _headerActions = new TextBlock[5];

    // Gesture controls: [gesture][button]
    private readonly ComboBox[][] _combos = new ComboBox[3][];
    private readonly TextBox[][] _pathBoxes = new TextBox[3][];
    private readonly StackPanel[][] _pathPanels = new StackPanel[3][];
    private readonly TextBox[][] _macroBoxes = new TextBox[3][];
    private readonly StackPanel[][] _macroPanels = new StackPanel[3][];
    private readonly ComboBox[][] _profilePickers = new ComboBox[3][];
    private readonly StackPanel[][] _profilePanels = new StackPanel[3][];
    private readonly ComboBox[][] _knobPickers = new ComboBox[3][];
    private readonly StackPanel[][] _knobPanels = new StackPanel[3][];

    private static readonly string[] GestureNames = { "TAP", "DOUBLE", "HOLD" };
    private static readonly Color[] GestureColors =
    {
        Color.Parse("#66BB6A"), // TAP = green
        Color.Parse("#FFD54F"), // DOUBLE = gold
        Color.Parse("#FF8A3D"), // HOLD = orange
    };

    public ButtonsView()
    {
        InitializeComponent();

        for (int g = 0; g < 3; g++)
        {
            _combos[g] = new ComboBox[5];
            _pathBoxes[g] = new TextBox[5];
            _pathPanels[g] = new StackPanel[5];
            _macroBoxes[g] = new TextBox[5];
            _macroPanels[g] = new StackPanel[5];
            _profilePickers[g] = new ComboBox[5];
            _profilePanels[g] = new StackPanel[5];
            _knobPickers[g] = new ComboBox[5];
            _knobPanels[g] = new StackPanel[5];
        }

        BuildColumns();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // Rebuild profile pickers with current profiles list
        for (int g = 0; g < 3; g++)
        {
            for (int i = 0; i < 5; i++)
            {
                RebuildProfilePicker(_profilePickers[g][i], config);
                RebuildKnobPicker(_knobPickers[g][i], config);
            }
        }

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // Header
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            var label = !string.IsNullOrWhiteSpace(btn.Label) ? btn.Label
                : (knob != null && !string.IsNullOrWhiteSpace(knob.Label)) ? knob.Label
                : $"Button {i + 1}";
            _headers[i].Text = label;

            // TAP
            SelectCombo(_combos[0][i], btn.Action);
            SetText(_pathBoxes[0][i], btn.Path);
            SetText(_macroBoxes[0][i], btn.MacroKeys);
            SelectComboByValue(_profilePickers[0][i], btn.ProfileName);
            SelectKnobByIdx(_knobPickers[0][i], btn.LinkedKnobIdx);
            UpdateGestureVisibility(0, i, btn.Action);

            // DOUBLE
            SelectCombo(_combos[1][i], btn.DoublePressAction);
            SetText(_pathBoxes[1][i], btn.DoublePressPath);
            SetText(_macroBoxes[1][i], btn.DoublePressMacroKeys);
            SelectComboByValue(_profilePickers[1][i], btn.DoublePressProfileName);
            SelectKnobByIdx(_knobPickers[1][i], btn.DoublePressLinkedKnobIdx);
            UpdateGestureVisibility(1, i, btn.DoublePressAction);

            // HOLD
            SelectCombo(_combos[2][i], btn.HoldAction);
            SetText(_pathBoxes[2][i], btn.HoldPath);
            SetText(_macroBoxes[2][i], btn.HoldMacroKeys);
            SelectComboByValue(_profilePickers[2][i], btn.HoldProfileName);
            SelectKnobByIdx(_knobPickers[2][i], btn.HoldLinkedKnobIdx);
            UpdateGestureVisibility(2, i, btn.HoldAction);

            UpdateHeaderDisplay(i);
        }

        _loading = false;
    }

    private void BuildColumns()
    {
        var panels = new[]
        {
            this.FindControl<StackPanel>("Btn0Panel")!,
            this.FindControl<StackPanel>("Btn1Panel")!,
            this.FindControl<StackPanel>("Btn2Panel")!,
            this.FindControl<StackPanel>("Btn3Panel")!,
            this.FindControl<StackPanel>("Btn4Panel")!,
        };

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var panel = panels[i];

            // Header
            var headerStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };

            _headers[i] = new TextBlock
            {
                Text = $"Button {i + 1}",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            headerStack.Children.Add(_headers[i]);

            _headerIcons[i] = new MaterialIcon
            {
                Kind = MaterialIconKind.MinusCircleOutline,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
            };
            headerStack.Children.Add(_headerIcons[i]);

            _headerActions[i] = new TextBlock
            {
                Text = "None",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
            };
            headerStack.Children.Add(_headerActions[i]);
            panel.Children.Add(headerStack);

            panel.Children.Add(MakeSeparator());

            // 3 gesture sections
            for (int g = 0; g < 3; g++)
            {
                int gesture = g;
                panel.Children.Add(MakeGestureHeader(GestureNames[g], GestureColors[g]));

                // Action combo
                var combo = MakeActionCombo();
                combo.SelectionChanged += (_, _) =>
                {
                    if (_loading) return;
                    var action = GetComboValue(combo);
                    UpdateGestureVisibility(gesture, idx, action);
                    if (gesture == 0) UpdateHeaderDisplay(idx);
                    Save();
                };
                _combos[g][i] = combo;
                panel.Children.Add(combo);

                // Path field (for mute_program, launch_exe, close_program)
                var pathPanel = new StackPanel { IsVisible = false, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
                pathPanel.Children.Add(MakeLabel("PROCESS / PATH"));
                var pathBox = new TextBox { FontSize = 11 };
                pathBox.PropertyChanged += (_, e) =>
                {
                    if (e.Property == TextBox.TextProperty && !_loading) Save();
                };
                _pathBoxes[g][i] = pathBox;
                pathPanel.Children.Add(pathBox);
                _pathPanels[g][i] = pathPanel;
                panel.Children.Add(pathPanel);

                // Macro field
                var macroPanel = new StackPanel { IsVisible = false, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
                macroPanel.Children.Add(MakeLabel("MACRO KEYS"));
                var macroBox = new TextBox { FontSize = 11, Watermark = "ctrl+shift+m" };
                macroBox.PropertyChanged += (_, e) =>
                {
                    if (e.Property == TextBox.TextProperty && !_loading) Save();
                };
                _macroBoxes[g][i] = macroBox;
                macroPanel.Children.Add(macroBox);
                _macroPanels[g][i] = macroPanel;
                panel.Children.Add(macroPanel);

                // Profile picker (for switch_profile)
                var profilePanel = new StackPanel { IsVisible = false, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
                profilePanel.Children.Add(MakeLabel("PROFILE"));
                var profilePicker = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontSize = 11,
                };
                profilePicker.SelectionChanged += (_, _) =>
                {
                    if (!_loading) Save();
                };
                _profilePickers[g][i] = profilePicker;
                profilePanel.Children.Add(profilePicker);
                _profilePanels[g][i] = profilePanel;
                panel.Children.Add(profilePanel);

                // Knob picker (for mute_app_group)
                var knobPanel = new StackPanel { IsVisible = false, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
                knobPanel.Children.Add(MakeLabel("LINKED KNOB"));
                var knobPicker = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontSize = 11,
                };
                knobPicker.SelectionChanged += (_, _) =>
                {
                    if (!_loading) Save();
                };
                _knobPickers[g][i] = knobPicker;
                knobPanel.Children.Add(knobPicker);
                _knobPanels[g][i] = knobPanel;
                panel.Children.Add(knobPanel);

                // Separator between gestures (not after last)
                if (g < 2) panel.Children.Add(MakeSeparator());
            }
        }
    }

    private void UpdateGestureVisibility(int gesture, int idx, string action)
    {
        _pathPanels[gesture][idx].IsVisible = PathActions.Contains(action);
        _macroPanels[gesture][idx].IsVisible = MacroActions.Contains(action);
        _profilePanels[gesture][idx].IsVisible = ProfileActions.Contains(action);
        _knobPanels[gesture][idx].IsVisible = KnobActions.Contains(action);
    }

    private void UpdateHeaderDisplay(int idx)
    {
        var action = GetComboValue(_combos[0][idx]);
        var def = AllActions.FirstOrDefault(a => a.Value == action);
        _headerIcons[idx].Kind = def.Icon;
        _headerActions[idx].Text = def.Display ?? "None";

        if (action == "none")
        {
            _headerIcons[idx].Foreground = FindBrush("TextDimBrush");
            _headerActions[idx].Foreground = FindBrush("TextDimBrush");
        }
        else
        {
            _headerIcons[idx].Foreground = new SolidColorBrush(def.Color);
            _headerActions[idx].Foreground = new SolidColorBrush(def.Color);
        }
    }

    private static void RebuildProfilePicker(ComboBox picker, AppConfig config)
    {
        var current = picker.SelectedItem as ComboBoxItem;
        var currentValue = current?.Tag as string ?? "";

        picker.Items.Clear();
        foreach (var name in config.Profiles)
        {
            picker.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        // Re-select previous value if still valid
        if (!string.IsNullOrEmpty(currentValue))
            SelectComboByValue(picker, currentValue);
        else if (picker.Items.Count > 0)
            picker.SelectedIndex = 0;
    }

    private static void RebuildKnobPicker(ComboBox picker, AppConfig config)
    {
        var current = picker.SelectedItem as ComboBoxItem;
        int currentIdx = current?.Tag is int t ? t : -1;

        picker.Items.Clear();
        picker.Items.Add(new ComboBoxItem { Content = "None", Tag = -1 });
        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            var label = knob != null && !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : $"Knob {i + 1}";
            picker.Items.Add(new ComboBoxItem { Content = label, Tag = i });
        }

        SelectKnobByIdx(picker, currentIdx);
    }

    private static void SelectComboByValue(ComboBox picker, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (picker.Items.Count > 0) picker.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < picker.Items.Count; i++)
        {
            if (picker.Items[i] is ComboBoxItem item && item.Tag as string == value)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        if (picker.Items.Count > 0) picker.SelectedIndex = 0;
    }

    private static void SelectKnobByIdx(ComboBox picker, int idx)
    {
        for (int i = 0; i < picker.Items.Count; i++)
        {
            if (picker.Items[i] is ComboBoxItem item && item.Tag is int t && t == idx)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        picker.SelectedIndex = 0; // None
    }

    private void Save()
    {
        if (_config == null || _onSave == null) return;

        for (int i = 0; i < 5; i++)
        {
            var btn = _config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            btn.Action = GetComboValue(_combos[0][i]);
            btn.Path = _pathBoxes[0][i].Text?.Trim() ?? "";
            btn.MacroKeys = _macroBoxes[0][i].Text?.Trim() ?? "";
            btn.ProfileName = GetProfilePickerValue(_profilePickers[0][i]);
            btn.LinkedKnobIdx = GetKnobPickerIdx(_knobPickers[0][i]);

            btn.DoublePressAction = GetComboValue(_combos[1][i]);
            btn.DoublePressPath = _pathBoxes[1][i].Text?.Trim() ?? "";
            btn.DoublePressMacroKeys = _macroBoxes[1][i].Text?.Trim() ?? "";
            btn.DoublePressProfileName = GetProfilePickerValue(_profilePickers[1][i]);
            btn.DoublePressLinkedKnobIdx = GetKnobPickerIdx(_knobPickers[1][i]);

            btn.HoldAction = GetComboValue(_combos[2][i]);
            btn.HoldPath = _pathBoxes[2][i].Text?.Trim() ?? "";
            btn.HoldMacroKeys = _macroBoxes[2][i].Text?.Trim() ?? "";
            btn.HoldProfileName = GetProfilePickerValue(_profilePickers[2][i]);
            btn.HoldLinkedKnobIdx = GetKnobPickerIdx(_knobPickers[2][i]);
        }

        _onSave(_config);
    }

    // ── UI Helpers ──────────────────────────────────────────────────

    private ComboBox MakeActionCombo()
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11,
        };

        var items = new List<ComboBoxItem>();
        string? lastCategory = null;

        foreach (var (display, value, icon, category, color) in AllActions)
        {
            if (category != lastCategory)
            {
                if (lastCategory != null)
                {
                    // Category separator
                    items.Add(new ComboBoxItem
                    {
                        Content = new Border { Height = 1, Background = FindBrush("CardBorderBrush"), Margin = new Thickness(0, 4) },
                        IsEnabled = false,
                    });
                }
                lastCategory = category;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new MaterialIcon
            {
                Kind = icon,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color),
            });
            row.Children.Add(new TextBlock
            {
                Text = display,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = FindBrush("TextPrimaryBrush"),
            });

            items.Add(new ComboBoxItem { Content = row, Tag = value });
        }

        combo.ItemsSource = items;
        combo.SelectedIndex = 0; // None
        return combo;
    }

    private static void SelectCombo(ComboBox combo, string actionValue)
    {
        if (combo.ItemsSource is not List<ComboBoxItem> items) return;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Tag as string == actionValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboValue(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "none";
        return "none";
    }

    private static string GetProfilePickerValue(ComboBox picker)
    {
        if (picker.SelectedItem is ComboBoxItem item && item.Tag is string s)
            return s;
        return "";
    }

    private static int GetKnobPickerIdx(ComboBox picker)
    {
        if (picker.SelectedItem is ComboBoxItem item && item.Tag is int t)
            return t;
        return -1;
    }

    private Grid MakeGestureHeader(string title, Color color)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(3, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var bar = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(color),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        return grid;
    }

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = FindBrush("CardBorderBrush"),
        Margin = new Thickness(0, 8),
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#555555")),
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static void SetText(TextBox box, string? value)
    {
        box.Text = value ?? "";
    }

    private IBrush FindBrush(string key)
    {
        if (this.TryFindResource(key, this.ActualThemeVariant, out var res) && res is IBrush brush)
            return brush;
        return Brushes.White;
    }
}
