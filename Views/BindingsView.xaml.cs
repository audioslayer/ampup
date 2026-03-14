using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Views;

public class BindingsView : UserControl
{
    private AppConfig? _config;
    private Action<string>? _onNavigateToMixer;   // profile name
    private Action<string>? _onNavigateToButtons;  // profile name
    private Action<string>? _onSwitchProfile;
    private Action<string>? _onPreviewOsd;         // profile name → show OSD

    // Action icons duplicated from ButtonsView (internal access)
    private static readonly Dictionary<string, string> ActionIcons = new()
    {
        { "none", "—" }, { "media_play_pause", "⏯" }, { "media_next", "⏭" },
        { "media_prev", "⏮" }, { "mute_master", "🔇" }, { "mute_mic", "🎤" },
        { "mute_program", "🔇" }, { "mute_active_window", "🔇" }, { "launch_exe", "🚀" },
        { "close_program", "✕" }, { "cycle_output", "🔊" }, { "cycle_input", "🎙" },
        { "select_output", "🔊" }, { "select_input", "🎙" }, { "macro", "⌨" },
        { "switch_profile", "📋" }, { "cycle_brightness", "💡" }, { "mute_app_group", "🔇" },
        { "mute_device", "🔇" },
        { "power_sleep", "😴" }, { "power_lock", "🔒" }, { "power_off", "⏻" },
        { "power_restart", "🔄" }, { "power_logoff", "🚪" }, { "power_hibernate", "❄" },
        { "ha_toggle", "⚡" }, { "ha_scene", "🎬" }, { "ha_service", "⚙" },
    };

    private static readonly Dictionary<string, Color> ActionColors = new()
    {
        { "none",               Color.FromRgb(0x44, 0x44, 0x44) },
        { "media_play_pause",   Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_next",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_prev",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "mute_master",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_mic",           Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_program",       Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_active_window", Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_app_group",     Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_device",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "launch_exe",         Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "close_program",      Color.FromRgb(0xFF, 0x7C, 0x43) },
        { "cycle_output",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "cycle_input",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_output",      Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_input",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "macro",              Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "switch_profile",     Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "cycle_brightness",   Color.FromRgb(0xFF, 0xF1, 0x76) },
        { "power_sleep",        Color.FromRgb(0x7C, 0x8C, 0xF8) },
        { "power_lock",         Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "power_off",          Color.FromRgb(0xFF, 0x44, 0x44) },
        { "power_restart",      Color.FromRgb(0xFF, 0x8A, 0x3D) },
        { "power_logoff",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "power_hibernate",    Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "ha_toggle",          Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "ha_scene",           Color.FromRgb(0xFF, 0xA7, 0x26) },
        { "ha_service",         Color.FromRgb(0xAB, 0x47, 0xBC) },
    };

    private static readonly Dictionary<string, string> ActionDisplayNames = new()
    {
        { "none", "None" }, { "media_play_pause", "Play/Pause" }, { "media_next", "Next" },
        { "media_prev", "Prev" }, { "mute_master", "Mute Vol" }, { "mute_mic", "Mute Mic" },
        { "mute_program", "Mute App" }, { "mute_active_window", "Mute Window" },
        { "mute_app_group", "Mute Group" }, { "mute_device", "Mute Device" },
        { "launch_exe", "Launch" }, { "close_program", "Close App" },
        { "cycle_output", "Cycle Out" }, { "cycle_input", "Cycle In" },
        { "select_output", "Set Output" }, { "select_input", "Set Input" },
        { "macro", "Macro" }, { "switch_profile", "Profile" },
        { "cycle_brightness", "Brightness" },
        { "power_sleep", "Sleep" }, { "power_lock", "Lock" }, { "power_off", "Off" },
        { "power_restart", "Restart" }, { "power_logoff", "Logoff" }, { "power_hibernate", "Hibernate" },
        { "ha_toggle", "HA Toggle" }, { "ha_scene", "HA Scene" }, { "ha_service", "HA Service" },
    };

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _root;

    public BindingsView()
    {
        _root = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _root
        };

        Content = _scroll;
    }

    public void SetNavigationCallbacks(
        Action<string> onMixer, Action<string> onButtons,
        Action<string>? onSwitchProfile = null, Action<string>? onPreviewOsd = null)
    {
        _onNavigateToMixer = onMixer;
        _onNavigateToButtons = onButtons;
        _onSwitchProfile = onSwitchProfile;
        _onPreviewOsd = onPreviewOsd;
    }

    public void LoadConfig(AppConfig config)
    {
        _config = config;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_config == null) return;
        _root.Children.Clear();

        // Page header
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Bindings Overview",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "All knob and button configurations across profiles",
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        _root.Children.Add(headerPanel);

        // Render current profile first, then other profiles
        var profiles = new List<string> { _config.ActiveProfile };
        foreach (var p in _config.Profiles)
        {
            if (p != _config.ActiveProfile) profiles.Add(p);
        }

        foreach (var profileName in profiles)
        {
            AppConfig profileConfig;
            if (profileName == _config.ActiveProfile)
            {
                profileConfig = _config;
            }
            else
            {
                var loaded = ConfigManager.LoadProfile(profileName);
                if (loaded == null)
                {
                    // Profile file doesn't exist yet — use defaults
                    loaded = new AppConfig();
                }
                profileConfig = loaded;
            }

            _root.Children.Add(BuildProfileSection(profileName, profileConfig));
        }
    }

    private UIElement BuildProfileSection(string profileName, AppConfig config)
    {
        bool isActive = _config?.ActiveProfile == profileName;

        var iconCfg = _config?.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        Color accentColor;
        try { accentColor = (Color)ColorConverter.ConvertFromString(iconCfg.Color); }
        catch { accentColor = ThemeManager.Accent; }

        var accentBrush = new SolidColorBrush(accentColor);

        var section = new Border
        {
            Background = (SolidColorBrush)FindResource("CardBgBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(16, 14, 16, 16)
        };

        var sectionContent = new StackPanel();

        // Profile header row — clickable to switch + navigate
        var profileHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
            Cursor = Cursors.Hand
        };
        var capturedName = profileName;
        profileHeader.MouseLeftButtonDown += (_, _) =>
        {
            _onSwitchProfile?.Invoke(capturedName);
        };

        // Accent left bar
        profileHeader.Children.Add(new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = accentBrush,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        profileHeader.Children.Add(new TextBlock
        {
            Text = profileName,
            FontSize = 14,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = accentBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (isActive)
        {
            profileHeader.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "ACTIVE",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = accentBrush
                }
            });
        }

        // "Preview OSD" button — right side of header
        var previewBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Preview OSD",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            }
        };
        previewBtn.MouseEnter += (_, _) => previewBtn.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
        previewBtn.MouseLeave += (_, _) => previewBtn.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        previewBtn.MouseLeftButtonDown += (_, e) =>
        {
            _onPreviewOsd?.Invoke(capturedName);
            e.Handled = true;
        };

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(profileHeader, 0);
        profileHeader.Margin = new Thickness(0);
        headerGrid.Children.Add(profileHeader);
        Grid.SetColumn(previewBtn, 1);
        headerGrid.Children.Add(previewBtn);
        sectionContent.Children.Add(headerGrid);

        // Knobs subsection label
        sectionContent.Children.Add(MakeSectionLabel("KNOBS"));

        // Knobs row
        var knobsRow = new UniformGrid
        {
            Columns = 5,
            Margin = new Thickness(0, 6, 0, 14)
        };

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
            knobsRow.Children.Add(BuildKnobCard(i, knob, capturedName));
        }
        sectionContent.Children.Add(knobsRow);

        // Buttons subsection label
        sectionContent.Children.Add(MakeSectionLabel("BUTTONS"));

        // Buttons row
        var buttonsRow = new UniformGrid
        {
            Columns = 5,
            Margin = new Thickness(0, 6, 0, 0)
        };

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i) ?? new ButtonConfig { Idx = i };
            buttonsRow.Children.Add(BuildButtonCard(i, btn, capturedName));
        }
        sectionContent.Children.Add(buttonsRow);

        section.Child = sectionContent;
        return section;
    }

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = (SolidColorBrush)Application.Current.MainWindow!.FindResource("TextDimBrush"),
        Margin = new Thickness(0, 0, 0, 0)
    };

    private UIElement BuildKnobCard(int idx, KnobConfig knob, string profileName)
    {
        bool isEmpty = string.IsNullOrEmpty(knob.Target) || knob.Target == "none";

        var card = new Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = isEmpty ? 0.45 : 1.0
        };

        card.MouseEnter += (_, _) =>
        {
            if (!isEmpty)
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x80));
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
        };
        card.MouseLeftButtonDown += (_, _) => _onNavigateToMixer?.Invoke(profileName);

        var content = new StackPanel();

        // Knob number + label
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"K{idx + 1}",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        if (!string.IsNullOrEmpty(knob.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = knob.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 90
            });
        }
        content.Children.Add(titleRow);

        // Target
        var targetDisplay = FormatTarget(knob);
        content.Children.Add(new TextBlock
        {
            Text = targetDisplay,
            FontSize = 11,
            Foreground = isEmpty
                ? (SolidColorBrush)FindResource("TextDimBrush")
                : (SolidColorBrush)FindResource("AccentBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Range (only if not default 0-100)
        if (knob.MinVolume != 0 || knob.MaxVolume != 100)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Range: {knob.MinVolume}–{knob.MaxVolume}%",
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("TextSecBrush")
            });
        }

        // Curve (only if not Linear)
        if (knob.Curve != ResponseCurve.Linear)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Curve: {knob.Curve}",
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("TextSecBrush")
            });
        }

        card.Child = content;
        return card;
    }

    private UIElement BuildButtonCard(int idx, ButtonConfig btn, string profileName)
    {
        bool hasTap = btn.Action != "none" && !string.IsNullOrEmpty(btn.Action);
        bool hasDouble = btn.DoublePressAction != "none" && !string.IsNullOrEmpty(btn.DoublePressAction);
        bool hasHold = btn.HoldAction != "none" && !string.IsNullOrEmpty(btn.HoldAction);
        bool isEmpty = !hasTap && !hasDouble && !hasHold;

        var card = new Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = isEmpty ? 0.45 : 1.0
        };

        card.MouseEnter += (_, _) =>
        {
            if (!isEmpty)
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x80));
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
        };
        card.MouseLeftButtonDown += (_, _) => _onNavigateToButtons?.Invoke(profileName);

        var content = new StackPanel();

        // Button number + label
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"B{idx + 1}",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        if (!string.IsNullOrEmpty(btn.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = btn.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 90
            });
        }
        content.Children.Add(titleRow);

        if (isEmpty)
        {
            content.Children.Add(new TextBlock
            {
                Text = "No actions",
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("TextDimBrush")
            });
        }
        else
        {
            if (hasTap)
                content.Children.Add(BuildGestureRow("TAP", btn.Action, btn.Path, btn.MacroKeys, btn.ProfileName, Color.FromRgb(0x66, 0xBB, 0x6A)));

            if (hasDouble)
                content.Children.Add(BuildGestureRow("DBL", btn.DoublePressAction, btn.DoublePressPath, btn.DoublePressMacroKeys, btn.DoublePressProfileName, Color.FromRgb(0xFF, 0xD5, 0x4F)));

            if (hasHold)
                content.Children.Add(BuildGestureRow("HOLD", btn.HoldAction, btn.HoldPath, btn.HoldMacroKeys, btn.HoldProfileName, Color.FromRgb(0xFF, 0x8A, 0x3D)));
        }

        card.Child = content;
        return card;
    }

    private static UIElement BuildGestureRow(string gestureLabel, string action, string path, string macroKeys, string profileName, Color gestureColor)
    {
        ActionIcons.TryGetValue(action, out var icon);
        ActionColors.TryGetValue(action, out var actionColor);
        ActionDisplayNames.TryGetValue(action, out var displayName);

        displayName ??= action;
        icon ??= "•";

        var row = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        var topRow = new WrapPanel();

        // Gesture type badge
        topRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, gestureColor.R, gestureColor.G, gestureColor.B)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = gestureLabel,
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(gestureColor)
            }
        });

        // Action icon + name
        if (!string.IsNullOrEmpty(icon) && icon != "—")
        {
            topRow.Children.Add(new TextBlock
            {
                Text = icon + " ",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        topRow.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(actionColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(topRow);

        // Context detail (path / macro / profile)
        var detail = GetActionDetail(action, path, macroKeys, profileName);
        if (!string.IsNullOrEmpty(detail))
        {
            row.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 9,
                Foreground = (SolidColorBrush)Application.Current.MainWindow!.FindResource("TextSecBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
                MaxWidth = 130
            });
        }

        return row;
    }

    private static string GetActionDetail(string action, string path, string macroKeys, string profileName)
    {
        return action switch
        {
            "macro" when !string.IsNullOrEmpty(macroKeys) => macroKeys,
            "switch_profile" when !string.IsNullOrEmpty(profileName) => profileName,
            "mute_program" or "launch_exe" or "close_program" or "mute_active_window"
                when !string.IsNullOrEmpty(path) => System.IO.Path.GetFileName(path),
            "ha_toggle" or "ha_scene" or "ha_service"
                when !string.IsNullOrEmpty(path) => path.Length > 20 ? path[..20] + "…" : path,
            _ => ""
        };
    }

    private static string FormatTarget(KnobConfig knob)
    {
        var t = knob.Target ?? "none";
        return t switch
        {
            "none" or "" => "—",
            "master" => "Master Volume",
            "mic" => "Microphone",
            "system" => "System Sounds",
            "any" => "Any Active",
            "active_window" => "Active Window",
            "apps" => knob.Apps.Count > 0
                ? string.Join(", ", knob.Apps.Select(FormatAppName))
                : "App Group",
            "output_device" => "Output Device",
            "input_device" => "Input Device",
            "monitor" => "Monitor Brightness",
            "led_brightness" => "LED Brightness",
            _ when t.StartsWith("ha_") => FormatHATarget(t),
            _ => CamelToTitle(t)
        };
    }

    private static string FormatHATarget(string target)
    {
        return target switch
        {
            "ha_light" => "HA: Light",
            "ha_media" => "HA: Media",
            "ha_fan" => "HA: Fan",
            "ha_cover" => "HA: Cover",
            _ => target.Replace("ha_", "HA: ")
        };
    }

    private static string FormatAppName(string app)
    {
        if (string.IsNullOrEmpty(app)) return app;
        var name = System.IO.Path.GetFileNameWithoutExtension(app);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string CamelToTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new System.Text.StringBuilder();
        result.Append(char.ToUpperInvariant(s[0]));
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == '_' || s[i] == '.')
            {
                result.Append(' ');
            }
            else
            {
                result.Append(s[i]);
            }
        }
        return result.ToString();
    }
}
