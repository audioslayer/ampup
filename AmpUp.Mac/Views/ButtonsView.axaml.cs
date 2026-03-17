using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class ButtonsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    // Action definitions
    private static readonly (string Display, string Value, string Icon, string Category, Color Color)[] AllActions =
    {
        ("None",              "none",              "—", "Media",       Color.Parse("#444444")),
        ("Play / Pause",      "media_play_pause",  "⏯", "Media",      Color.Parse("#66BB6A")),
        ("Next Track",        "media_next",        "⏭", "Media",      Color.Parse("#66BB6A")),
        ("Prev Track",        "media_prev",        "⏮", "Media",      Color.Parse("#66BB6A")),
        ("Mute Volume",       "mute_master",       "🔇", "Mute",      Color.Parse("#EF5350")),
        ("Mute Mic",          "mute_mic",          "🎤", "Mute",      Color.Parse("#EF5350")),
        ("Mute App",          "mute_program",      "🔇", "Mute",      Color.Parse("#EF5350")),
        ("Mute Active Win",   "mute_active_window","🔇", "Mute",      Color.Parse("#EF5350")),
        ("Mute App Group",    "mute_app_group",    "🔇", "Mute",      Color.Parse("#EF5350")),
        ("Launch App",        "launch_exe",        "🚀", "App",       Color.Parse("#42A5F5")),
        ("Close App",         "close_program",     "✕", "App",        Color.Parse("#FF7C43")),
        ("Cycle Output",      "cycle_output",      "🔊", "Device",    Color.Parse("#AB47BC")),
        ("Cycle Input",       "cycle_input",       "🎙", "Device",    Color.Parse("#AB47BC")),
        ("Keyboard Macro",    "macro",             "⌨", "System",     Color.Parse("#FFD54F")),
        ("Switch Profile",    "switch_profile",    "📋", "System",    Color.Parse("#29B6F6")),
        ("Cycle Brightness",  "cycle_brightness",  "💡", "System",    Color.Parse("#FFF176")),
    };

    private static readonly string[] PathActions = { "mute_program", "launch_exe", "close_program" };

    // Per-column controls
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly TextBlock[] _headerIcons = new TextBlock[5];
    private readonly TextBlock[] _headerActions = new TextBlock[5];

    // Gesture controls: [gesture][button]
    private readonly ComboBox[][] _combos = new ComboBox[3][];
    private readonly TextBox[][] _pathBoxes = new TextBox[3][];
    private readonly StackPanel[][] _pathPanels = new StackPanel[3][];
    private readonly TextBox[][] _macroBoxes = new TextBox[3][];
    private readonly StackPanel[][] _macroPanels = new StackPanel[3][];

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
        }

        BuildColumns();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

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
            UpdateGestureVisibility(0, i, btn.Action);

            // DOUBLE
            SelectCombo(_combos[1][i], btn.DoublePressAction);
            SetText(_pathBoxes[1][i], btn.DoublePressPath);
            SetText(_macroBoxes[1][i], btn.DoublePressMacroKeys);
            UpdateGestureVisibility(1, i, btn.DoublePressAction);

            // HOLD
            SelectCombo(_combos[2][i], btn.HoldAction);
            SetText(_pathBoxes[2][i], btn.HoldPath);
            SetText(_macroBoxes[2][i], btn.HoldMacroKeys);
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

            _headerIcons[i] = new TextBlock
            {
                Text = "—",
                FontSize = 28,
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

                // Path field
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

                // Separator between gestures (not after last)
                if (g < 2) panel.Children.Add(MakeSeparator());
            }
        }
    }

    private void UpdateGestureVisibility(int gesture, int idx, string action)
    {
        _pathPanels[gesture][idx].IsVisible = PathActions.Contains(action);
        _macroPanels[gesture][idx].IsVisible = action == "macro";
    }

    private void UpdateHeaderDisplay(int idx)
    {
        var action = GetComboValue(_combos[0][idx]);
        var def = AllActions.FirstOrDefault(a => a.Value == action);
        _headerIcons[idx].Text = def.Icon ?? "—";
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

            btn.DoublePressAction = GetComboValue(_combos[1][i]);
            btn.DoublePressPath = _pathBoxes[1][i].Text?.Trim() ?? "";
            btn.DoublePressMacroKeys = _macroBoxes[1][i].Text?.Trim() ?? "";

            btn.HoldAction = GetComboValue(_combos[2][i]);
            btn.HoldPath = _pathBoxes[2][i].Text?.Trim() ?? "";
            btn.HoldMacroKeys = _macroBoxes[2][i].Text?.Trim() ?? "";
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
            row.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 14,
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
