using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AmpUp;

public partial class ImportWizardWindow : Window
{
    public string? ImportedProfileName { get; private set; }

    private int _currentStep = 1;
    private TurnUpConfig? _turnUpConfig;
    private TurnUpProfile? _selectedProfile;

    // Auto-mapped configs (updated when user changes combos in step 2)
    private KnobConfig[] _mappedKnobs = new KnobConfig[5];
    private ButtonConfig[] _mappedButtons = new ButtonConfig[5];
    private LightConfig[] _mappedLights = new LightConfig[5];
    private int _mappedBrightness = 100;
    private ResponseCurve _mappedCurve = ResponseCurve.Linear;

    // Step 2 combo boxes for reading user selections
    private readonly ComboBox[] _knobCombos = new ComboBox[5];
    private readonly ComboBox[] _buttonCombos = new ComboBox[5];

    private int _unsupportedCount;

    // ── Standard mapping options ───────────────────────────────────

    private static readonly (string Value, string Display)[] KnobTargets =
    {
        ("none", "None"),
        ("master", "Master Volume"),
        ("mic", "Microphone"),
        ("system", "System Sounds"),
        ("any", "Any App"),
        ("active_window", "Active Window"),
        ("monitor", "Monitor Brightness"),
        ("output_device", "Output Device"),
        ("input_device", "Input Device"),
        ("led_brightness", "LED Brightness"),
    };

    private static readonly (string Value, string Display)[] ButtonActions =
    {
        ("none", "None"),
        ("media_play_pause", "Play / Pause"),
        ("media_next", "Next Track"),
        ("media_prev", "Previous Track"),
        ("mute_master", "Mute Master"),
        ("mute_mic", "Mute Mic"),
        ("mute_program", "Mute App"),
        ("mute_active_window", "Mute Active Window"),
        ("launch_exe", "Launch App"),
        ("close_program", "Close App"),
        ("cycle_output", "Cycle Output"),
        ("cycle_input", "Cycle Input"),
        ("select_output", "Select Output"),
        ("select_input", "Select Input"),
        ("macro", "Keyboard Macro"),
        ("system_power", "System Power"),
        ("switch_profile", "Switch Profile"),
        ("cycle_brightness", "Cycle Brightness"),
    };

    public ImportWizardWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };

        // Apply accent color to border gradient, title, input elements
        var accent = ThemeManager.Accent;
        var borderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(accent, 0x55),
            ThemeManager.WithAlpha(accent, 0x22),
            new Point(0, 0), new Point(1, 1));
        borderBrush.Freeze();
        RootPanel.BorderBrush = borderBrush;
        TitleLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));
        ProfileNameBox.CaretBrush = new SolidColorBrush(accent);
        var inputBorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x33));
        inputBorderBrush.Freeze();
        ProfileNameBorder.BorderBrush = inputBorderBrush;
        SummaryLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));

        Loaded += (_, _) =>
        {
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        };

        // Style navigation buttons
        BtnCancel.Style = MakeButtonStyle(false);
        BtnBack.Style = MakeButtonStyle(false);
        BtnNext.Style = MakeButtonStyle(true);

        BtnCancel.Click += (_, _) => Close();
        BtnBack.Click += (_, _) => GoBack();
        BtnNext.Click += (_, _) => GoNext();

        // Style the profile list
        ProfileListBox.Background = Brushes.Transparent;
        ProfileListBox.BorderThickness = new Thickness(0);

        BuildStep1();
        ShowStep(1);
    }

    // ── Step Navigation ────────────────────────────────────────────

    private void ShowStep(int step)
    {
        _currentStep = step;

        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        BtnBack.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Update button text
        if (step == 3)
        {
            BtnNext.Content = "Import";
            BtnNext.Style = MakeButtonStyle(true, isImport: true);
        }
        else
        {
            BtnNext.Content = "Next";
            BtnNext.Style = MakeButtonStyle(true);
        }

        // Enable Next only if valid
        BtnNext.IsEnabled = step switch
        {
            1 => _turnUpConfig != null && ProfileListBox.SelectedItem != null,
            _ => true
        };

        // Step text
        StepText.Text = step switch
        {
            1 => "Step 1 of 3 \u2014 Select Profile",
            2 => "Step 2 of 3 \u2014 Review Mappings",
            3 => "Step 3 of 3 \u2014 Complete Import",
            _ => ""
        };

        // Dots
        var accentColor = ThemeManager.Accent;
        var inactiveColor = (Color)ColorConverter.ConvertFromString("#333333");
        Dot1.Fill = new SolidColorBrush(step >= 1 ? accentColor : inactiveColor);
        Dot2.Fill = new SolidColorBrush(step >= 2 ? accentColor : inactiveColor);
        Dot3.Fill = new SolidColorBrush(step >= 3 ? accentColor : inactiveColor);
    }

    private void GoNext()
    {
        switch (_currentStep)
        {
            case 1:
                if (ProfileListBox.SelectedItem == null) return;
                var idx = ProfileListBox.SelectedIndex;
                _selectedProfile = _turnUpConfig!.Profiles[idx];
                AutoMapProfile();
                BuildStep2();
                ShowStep(2);
                break;

            case 2:
                ReadStep2Selections();
                BuildStep3();
                ShowStep(3);
                break;

            case 3:
                DoImport();
                break;
        }
    }

    private void GoBack()
    {
        if (_currentStep > 1)
            ShowStep(_currentStep - 1);
    }

    // ── Step 1: Detection ──────────────────────────────────────────

    private void BuildStep1()
    {
        var configPath = TurnUpImporter.FindConfigPath();

        if (configPath == null)
        {
            DetectStatusText.Text = "Turn Up configuration not found.\n\n" +
                "Expected location:\n" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Turn Up", "TurnUpConfig.json") +
                "\n\nMake sure the Turn Up app has been installed and configured at least once.";
            DetectStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            SelectProfileLabel.Visibility = Visibility.Collapsed;
            BtnNext.IsEnabled = false;
            return;
        }

        _turnUpConfig = TurnUpImporter.LoadConfig(configPath);

        if (_turnUpConfig == null || _turnUpConfig.Profiles.Count == 0)
        {
            DetectStatusText.Text = "Found Turn Up config but couldn't read any profiles.";
            DetectStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            BtnNext.IsEnabled = false;
            return;
        }

        DetectStatusText.Text = $"Found Turn Up configuration with {_turnUpConfig.Profiles.Count} profile(s).";
        DetectStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00DD77"));
        SelectProfileLabel.Visibility = Visibility.Visible;

        ProfileListBox.Items.Clear();
        foreach (var profile in _turnUpConfig.Profiles)
        {
            var item = new ListBoxItem
            {
                Content = profile.ProfileName,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8")),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C")),
                Cursor = Cursors.Hand,
            };
            ProfileListBox.Items.Add(item);
        }

        if (ProfileListBox.Items.Count > 0)
            ProfileListBox.SelectedIndex = 0;

        ProfileListBox.SelectionChanged += (_, _) =>
        {
            BtnNext.IsEnabled = ProfileListBox.SelectedItem != null;
        };
    }

    // ── Auto-map Turn Up → Amp Up ──────────────────────────────────

    private void AutoMapProfile()
    {
        if (_selectedProfile == null || _turnUpConfig == null) return;

        _mappedCurve = TurnUpImporter.MapResponseCurve(_turnUpConfig.Settings.ResponseCurve);
        _mappedBrightness = (int)Math.Round(_turnUpConfig.Settings.Brightness);

        for (int i = 0; i < 5; i++)
        {
            _mappedKnobs[i] = i < _selectedProfile.Knobs.Count
                ? TurnUpImporter.MapKnob(_selectedProfile.Knobs[i], i, _mappedCurve)
                : new KnobConfig { Idx = i };

            _mappedButtons[i] = i < _selectedProfile.Buttons.Count
                ? TurnUpImporter.MapButton(_selectedProfile.Buttons[i], i)
                : new ButtonConfig { Idx = i };

            _mappedLights[i] = i < _selectedProfile.Lights.Count
                ? TurnUpImporter.MapLight(_selectedProfile.Lights[i], i)
                : new LightConfig { Idx = i, R = 0, G = 150, B = 255 };
        }
    }

    // ── Step 2: Mapping Review ─────────────────────────────────────

    private void BuildStep2()
    {
        MappingPanel.Children.Clear();
        _unsupportedCount = 0;

        // ── Knobs section ──
        AddSectionHeader("KNOBS");

        for (int i = 0; i < 5; i++)
        {
            var turnUpKnob = i < (_selectedProfile?.Knobs.Count ?? 0)
                ? _selectedProfile!.Knobs[i] : null;

            var turnUpDesc = turnUpKnob != null
                ? TurnUpImporter.DescribeKnob(turnUpKnob) : "None";

            bool unsupported = turnUpKnob != null && TurnUpImporter.IsUnsupportedKnob(turnUpKnob.EffectType);
            if (unsupported) _unsupportedCount++;

            var combo = CreateKnobCombo(_mappedKnobs[i].Target);
            _knobCombos[i] = combo;

            AddMappingRow($"Knob {i + 1}", turnUpDesc, combo, unsupported);
        }

        // ── Buttons section ──
        AddSectionHeader("BUTTONS");

        for (int i = 0; i < 5; i++)
        {
            var turnUpButton = i < (_selectedProfile?.Buttons.Count ?? 0)
                ? _selectedProfile!.Buttons[i] : null;

            var turnUpDesc = turnUpButton != null
                ? TurnUpImporter.DescribeButton(turnUpButton) : "None";

            bool unsupported = turnUpButton != null && TurnUpImporter.IsUnsupportedButton(turnUpButton.EffectType);
            if (unsupported) _unsupportedCount++;

            var combo = CreateButtonCombo(_mappedButtons[i].Action);
            _buttonCombos[i] = combo;

            AddMappingRow($"Button {i + 1}", turnUpDesc, combo, unsupported);
        }

        // ── Lights summary ──
        AddSectionHeader("LIGHTS");
        var lightsNote = new TextBlock
        {
            Text = $"5 LED configurations will be imported with their colors and effects.\nGlobal brightness: {_mappedBrightness}%",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A9A9A")),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        MappingPanel.Children.Add(lightsNote);
    }

    private void AddSectionHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x66)),
            Margin = new Thickness(0, 12, 0, 8),
        };
        MappingPanel.Children.Add(header);
    }

    private void AddMappingRow(string label, string turnUpDesc, ComboBox combo, bool unsupported)
    {
        var row = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        // Label
        var labelBlock = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        // Turn Up description
        var descBlock = new TextBlock
        {
            Text = turnUpDesc,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                unsupported ? "#FFB800" : "#9A9A9A")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (unsupported)
            descBlock.ToolTip = "This Turn Up feature has no Amp Up equivalent. Mapped to None.";
        Grid.SetColumn(descBlock, 1);
        grid.Children.Add(descBlock);

        // Arrow
        var arrow = new TextBlock
        {
            Text = "\u2192",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8A8A")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        // ComboBox
        combo.Width = 160;
        combo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(combo, 3);
        grid.Children.Add(combo);

        row.Child = grid;
        MappingPanel.Children.Add(row);
    }

    private ComboBox CreateKnobCombo(string preSelected)
    {
        var combo = new ComboBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#363636")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8")),
        };

        bool foundSelection = false;
        foreach (var (value, display) in KnobTargets)
        {
            var item = new ComboBoxItem { Content = display, Tag = value };
            combo.Items.Add(item);
            if (value == preSelected)
            {
                item.IsSelected = true;
                foundSelection = true;
            }
        }

        // If the pre-selected value is a custom process name, add it
        if (!foundSelection && preSelected != "none" && preSelected != "apps")
        {
            var custom = new ComboBoxItem { Content = preSelected, Tag = preSelected, IsSelected = true };
            combo.Items.Add(custom);
        }
        else if (!foundSelection && preSelected == "apps")
        {
            var custom = new ComboBoxItem { Content = "App Group", Tag = "apps", IsSelected = true };
            combo.Items.Add(custom);
        }

        if (combo.SelectedItem == null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;

        return combo;
    }

    private ComboBox CreateButtonCombo(string preSelected)
    {
        var combo = new ComboBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#363636")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8")),
        };

        foreach (var (value, display) in ButtonActions)
        {
            var item = new ComboBoxItem { Content = display, Tag = value };
            combo.Items.Add(item);
            if (value == preSelected)
                item.IsSelected = true;
        }

        if (combo.SelectedItem == null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;

        return combo;
    }

    // ── Read Step 2 Selections ─────────────────────────────────────

    private void ReadStep2Selections()
    {
        for (int i = 0; i < 5; i++)
        {
            if (_knobCombos[i]?.SelectedItem is ComboBoxItem knobItem)
            {
                var newTarget = knobItem.Tag?.ToString() ?? "none";
                // Only update target — preserve label, apps, deviceId from auto-map
                _mappedKnobs[i].Target = newTarget;
            }

            if (_buttonCombos[i]?.SelectedItem is ComboBoxItem btnItem)
            {
                var newAction = btnItem.Tag?.ToString() ?? "none";
                // Only update action — preserve path, macroKeys, etc. from auto-map
                _mappedButtons[i].Action = newAction;
            }
        }
    }

    // ── Step 3: Summary ────────────────────────────────────────────

    private void BuildStep3()
    {
        // Suggest profile name from Turn Up profile
        ProfileNameBox.Text = _selectedProfile?.ProfileName ?? "Imported";

        // Build summary
        var knobSummary = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var target = _mappedKnobs[i].Target;
            var display = FindDisplayName(KnobTargets, target) ?? target;
            knobSummary.Add($"  Knob {i + 1}: {display}" +
                (!string.IsNullOrEmpty(_mappedKnobs[i].Label) ? $" ({_mappedKnobs[i].Label})" : ""));
        }

        var btnSummary = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var action = _mappedButtons[i].Action;
            var display = FindDisplayName(ButtonActions, action) ?? action;
            var extra = action switch
            {
                "launch_exe" when !string.IsNullOrEmpty(_mappedButtons[i].Path) =>
                    $" \u2014 {Path.GetFileName(_mappedButtons[i].Path)}",
                "macro" when !string.IsNullOrEmpty(_mappedButtons[i].MacroKeys) =>
                    $" \u2014 {_mappedButtons[i].MacroKeys}",
                "system_power" when !string.IsNullOrEmpty(_mappedButtons[i].PowerAction) =>
                    $" \u2014 {_mappedButtons[i].PowerAction}",
                "switch_profile" when !string.IsNullOrEmpty(_mappedButtons[i].ProfileName) =>
                    $" \u2014 {_mappedButtons[i].ProfileName}",
                _ => ""
            };
            btnSummary.Add($"  Button {i + 1}: {display}{extra}");
        }

        SummaryText.Text =
            "Knobs:\n" + string.Join("\n", knobSummary) + "\n\n" +
            "Buttons:\n" + string.Join("\n", btnSummary) + "\n\n" +
            $"Lights: 5 LED configs imported\n" +
            $"Brightness: {_mappedBrightness}%\n" +
            $"Response curve: {_mappedCurve}";

        if (_unsupportedCount > 0)
        {
            WarningsText.Visibility = Visibility.Visible;
            WarningsText.Text = $"\u26a0 {_unsupportedCount} Turn Up action(s) had no Amp Up equivalent and were set to None.";
        }
        else
        {
            WarningsText.Visibility = Visibility.Collapsed;
        }
    }

    private static string? FindDisplayName((string Value, string Display)[] options, string value)
    {
        foreach (var (v, d) in options)
            if (v == value) return d;
        return null;
    }

    // ── Import ─────────────────────────────────────────────────────

    private void DoImport()
    {
        var profileName = ProfileNameBox.Text.Trim();
        if (string.IsNullOrEmpty(profileName))
        {
            GlassDialog.ShowWarning("Please enter a profile name.", owner: this);
            return;
        }

        // Build the config
        var config = new AppConfig
        {
            ActiveProfile = profileName,
            Profiles = new List<string> { profileName },
            LedBrightness = _mappedBrightness,
            Knobs = _mappedKnobs.ToList(),
            Buttons = _mappedButtons.ToList(),
            Lights = _mappedLights.ToList(),
        };

        // Save as a profile
        ConfigManager.SaveProfile(config, profileName);
        ImportedProfileName = profileName;

        Logger.Log($"Imported Turn Up profile \"{_selectedProfile?.ProfileName}\" as \"{profileName}\"");

        GlassDialog.ShowInfo(
            $"Profile \"{profileName}\" imported successfully!\n\n" +
            "Switch to it in the Profiles dropdown, or fine-tune settings in the Mixer and Buttons tabs.",
            "IMPORT COMPLETE", owner: this);

        Close();
    }

    // ── Button Styling ─────────────────────────────────────────────

    private static Style MakeButtonStyle(bool isPrimary, bool isImport = false)
    {
        var hoverAccent = $"#{ThemeManager.AccentGlow.R:X2}{ThemeManager.AccentGlow.G:X2}{ThemeManager.AccentGlow.B:X2}";
        var bg = isPrimary ? ThemeManager.AccentHex : "#1C1C1C";
        var fg = isPrimary ? "#0F0F0F" : "#E8E8E8";
        var hoverBg = isPrimary ? hoverAccent : "#2A2A2A";
        var borderColor = isPrimary ? ThemeManager.AccentHex : "#2A2A2A";

        if (isImport) { bg = ThemeManager.AccentHex; hoverBg = hoverAccent; }

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, isPrimary ? FontWeights.SemiBold : FontWeights.Regular));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 8, 20, 8)));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);

        template.VisualTree = border;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverBg)), "Bd"));
        template.Triggers.Add(hoverTrigger);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
