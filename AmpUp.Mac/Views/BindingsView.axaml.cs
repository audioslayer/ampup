using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class BindingsView : UserControl
{
    private AppConfig? _config;
    private Action<string>? _onNavigateToMixer;
    private Action<string>? _onNavigateToButtons;
    private Action<string>? _onSwitchProfile;
    private Action<string>? _onPreviewOsd;

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
        { "cycle_brightness", "Brightness" }, { "quick_wheel", "Quick Wheel" },
        { "power_sleep", "Sleep" }, { "power_lock", "Lock" }, { "power_off", "Off" },
        { "power_restart", "Restart" }, { "power_logoff", "Logoff" }, { "power_hibernate", "Hibernate" },
        { "ha_toggle", "HA Toggle" }, { "ha_scene", "HA Scene" }, { "ha_service", "HA Service" },
    };

    private static readonly Dictionary<string, Color> ActionColors = new()
    {
        { "none",               Color.Parse("#444444") },
        { "media_play_pause",   Color.Parse("#66BB6A") },
        { "media_next",         Color.Parse("#66BB6A") },
        { "media_prev",         Color.Parse("#66BB6A") },
        { "mute_master",        Color.Parse("#EF5350") },
        { "mute_mic",           Color.Parse("#EF5350") },
        { "mute_program",       Color.Parse("#EF5350") },
        { "mute_active_window", Color.Parse("#EF5350") },
        { "mute_app_group",     Color.Parse("#EF5350") },
        { "mute_device",        Color.Parse("#EF5350") },
        { "launch_exe",         Color.Parse("#42A5F5") },
        { "close_program",      Color.Parse("#FF7C43") },
        { "cycle_output",       Color.Parse("#AB47BC") },
        { "cycle_input",        Color.Parse("#AB47BC") },
        { "select_output",      Color.Parse("#AB47BC") },
        { "select_input",       Color.Parse("#AB47BC") },
        { "macro",              Color.Parse("#FFD54F") },
        { "switch_profile",     Color.Parse("#29B6F6") },
        { "cycle_brightness",   Color.Parse("#FFF176") },
        { "quick_wheel",        Color.Parse("#FFD54F") },
        { "power_sleep",        Color.Parse("#7C8CF8") },
        { "power_lock",         Color.Parse("#FFD54F") },
        { "power_off",          Color.Parse("#FF4444") },
        { "power_restart",      Color.Parse("#FF8A3D") },
        { "power_logoff",       Color.Parse("#AB47BC") },
        { "power_hibernate",    Color.Parse("#42A5F5") },
        { "ha_toggle",          Color.Parse("#26C6DA") },
        { "ha_scene",           Color.Parse("#FFA726") },
        { "ha_service",         Color.Parse("#AB47BC") },
    };

    // Theme colors (matching Theme.axaml)
    private static readonly Color Accent = Color.Parse("#00E676");
    private static readonly IBrush CardBgBrush = new SolidColorBrush(Color.Parse("#1C1C1C"));
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush BgDarkBrush = new SolidColorBrush(Color.Parse("#141414"));
    private static readonly IBrush TextPrimaryBrush = new SolidColorBrush(Color.Parse("#E8E8E8"));
    private static readonly IBrush TextSecBrush = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly IBrush TextDimBrush = new SolidColorBrush(Color.Parse("#6A6A6A"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Accent);

    public BindingsView()
    {
        InitializeComponent();
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
        Root.Children.Clear();

        // Page header
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Profile Overview",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "All knob and button assignments across your profiles",
            FontSize = 12,
            Foreground = TextSecBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Root.Children.Add(headerPanel);

        // Render current profile first, then others
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
                profileConfig = ConfigManager.LoadProfile(profileName) ?? new AppConfig();
            }
            Root.Children.Add(BuildProfileSection(profileName, profileConfig));
        }
    }

    private Control BuildProfileSection(string profileName, AppConfig config)
    {
        bool isActive = _config?.ActiveProfile == profileName;
        var iconCfg = _config?.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        Color accentColor;
        try { accentColor = Color.Parse(iconCfg.Color); }
        catch { accentColor = Accent; }
        var accentBrush = new SolidColorBrush(accentColor);

        var section = new Border
        {
            Background = CardBgBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(16, 14, 16, 16)
        };

        var sectionContent = new StackPanel();

        // Profile header row
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var profileHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var capturedName = profileName;
        profileHeader.PointerPressed += (_, _) => _onSwitchProfile?.Invoke(capturedName);

        // Accent left bar
        profileHeader.Children.Add(new Border
        {
            Width = 3, Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = accentBrush,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        profileHeader.Children.Add(new TextBlock
        {
            Text = profileName,
            FontSize = 14,
            FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
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
                    FontWeight = FontWeight.SemiBold,
                    Foreground = accentBrush
                }
            });
        }
        Grid.SetColumn(profileHeader, 0);
        headerGrid.Children.Add(profileHeader);

        // Action buttons row
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var previewBtn = MakeHeaderButton("Preview OSD");
        previewBtn.Margin = new Thickness(6, 0, 0, 0);
        previewBtn.PointerPressed += (_, e) =>
        {
            _onPreviewOsd?.Invoke(capturedName);
            e.Handled = true;
        };
        actionRow.Children.Add(previewBtn);

        Grid.SetColumn(actionRow, 1);
        headerGrid.Children.Add(actionRow);
        sectionContent.Children.Add(headerGrid);

        // Knobs section
        sectionContent.Children.Add(MakeSectionLabel("KNOBS"));
        var knobsGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 6, 0, 14) };
        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            knobsGrid.Children.Add(BuildKnobCard(i, knob, capturedName, light));
        }
        sectionContent.Children.Add(knobsGrid);

        // Buttons section
        sectionContent.Children.Add(MakeSectionLabel("BUTTONS"));
        var buttonsGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i) ?? new ButtonConfig { Idx = i };
            buttonsGrid.Children.Add(BuildButtonCard(i, btn, capturedName));
        }
        sectionContent.Children.Add(buttonsGrid);

        section.Child = sectionContent;
        return section;
    }

    private static Border MakeHeaderButton(string text)
    {
        var normalBg = new SolidColorBrush(Color.Parse("#1E1E1E"));
        var hoverBg = new SolidColorBrush(Color.Parse("#282828"));
        var btn = new Border
        {
            Background = normalBg,
            BorderBrush = new SolidColorBrush(Color.Parse("#363636")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
            }
        };
        btn.PointerEntered += (_, _) => btn.Background = hoverBg;
        btn.PointerExited += (_, _) => btn.Background = normalBg;
        return btn;
    }

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeight.SemiBold,
        Foreground = TextDimBrush,
    };

    private Control BuildKnobCard(int idx, KnobConfig knob, string profileName, LightConfig? light)
    {
        bool isEmpty = string.IsNullOrEmpty(knob.Target) || knob.Target == "none";

        IBrush cardBg;
        if (light != null && (light.R > 10 || light.G > 10 || light.B > 10))
        {
            cardBg = new SolidColorBrush(Color.FromArgb(0x18,
                (byte)Math.Clamp(light.R, 0, 255),
                (byte)Math.Clamp(light.G, 0, 255),
                (byte)Math.Clamp(light.B, 0, 255)));
        }
        else
        {
            cardBg = BgDarkBrush;
        }

        var card = new Border
        {
            Background = cardBg,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Opacity = isEmpty ? 0.45 : 1.0
        };

        var accentBorder = new SolidColorBrush(Color.FromArgb(0x80, Accent.R, Accent.G, Accent.B));
        card.PointerEntered += (_, _) => { if (!isEmpty) card.BorderBrush = accentBorder; };
        card.PointerExited += (_, _) => card.BorderBrush = CardBorderBrush;
        card.PointerPressed += (_, _) => _onNavigateToMixer?.Invoke(profileName);

        var content = new StackPanel();

        // Title row
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"K{idx + 1}",
            FontSize = 9, FontWeight = FontWeight.SemiBold,
            Foreground = TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        if (!string.IsNullOrEmpty(knob.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = knob.Label,
                FontSize = 11, FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        content.Children.Add(titleRow);

        // Target display
        if (knob.Target == "apps" && knob.Apps.Count > 0)
        {
            var chipWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var app in knob.Apps.Take(4))
            {
                chipWrap.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x20, Accent.R, Accent.G, Accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, Accent.R, Accent.G, Accent.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 3, 3),
                    Child = new TextBlock
                    {
                        Text = app, FontSize = 9,
                        Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                    }
                });
            }
            if (knob.Apps.Count > 4)
            {
                chipWrap.Children.Add(new TextBlock
                {
                    Text = $"+{knob.Apps.Count - 4}",
                    FontSize = 9, Foreground = TextDimBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 3),
                });
            }
            content.Children.Add(chipWrap);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = FormatTarget(knob),
                FontSize = 11,
                Foreground = isEmpty ? TextDimBrush : AccentBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        card.Child = content;
        return card;
    }

    private Control BuildButtonCard(int idx, ButtonConfig btn, string profileName)
    {
        bool hasTap = btn.Action != "none" && !string.IsNullOrEmpty(btn.Action);
        bool hasDouble = btn.DoublePressAction != "none" && !string.IsNullOrEmpty(btn.DoublePressAction);
        bool hasHold = btn.HoldAction != "none" && !string.IsNullOrEmpty(btn.HoldAction);
        bool isEmpty = !hasTap && !hasDouble && !hasHold;

        var card = new Border
        {
            Background = BgDarkBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Opacity = isEmpty ? 0.45 : 1.0
        };

        var accentBorder = new SolidColorBrush(Color.FromArgb(0x80, Accent.R, Accent.G, Accent.B));
        card.PointerEntered += (_, _) => { if (!isEmpty) card.BorderBrush = accentBorder; };
        card.PointerExited += (_, _) => card.BorderBrush = CardBorderBrush;
        card.PointerPressed += (_, _) => _onNavigateToButtons?.Invoke(profileName);

        var content = new StackPanel();

        // Title row
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"B{idx + 1}",
            FontSize = 9, FontWeight = FontWeight.SemiBold,
            Foreground = TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        if (!string.IsNullOrEmpty(btn.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = btn.Label,
                FontSize = 11, FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimaryBrush,
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
                Text = "No actions", FontSize = 10, Foreground = TextDimBrush
            });
        }
        else
        {
            if (hasTap)
                content.Children.Add(BuildGestureRow("TAP", btn.Action, btn.Path, btn.MacroKeys, btn.ProfileName, Color.Parse("#66BB6A")));
            if (hasDouble)
                content.Children.Add(BuildGestureRow("DBL", btn.DoublePressAction, btn.DoublePressPath, btn.DoublePressMacroKeys, btn.DoublePressProfileName, Color.Parse("#FFD54F")));
            if (hasHold)
                content.Children.Add(BuildGestureRow("HOLD", btn.HoldAction, btn.HoldPath, btn.HoldMacroKeys, btn.HoldProfileName, Color.Parse("#FF8A3D")));
        }

        card.Child = content;
        return card;
    }

    private static Control BuildGestureRow(string gestureLabel, string action, string path,
        string macroKeys, string profileName, Color gestureColor)
    {
        if (!ActionColors.TryGetValue(action, out var actionColor))
            actionColor = Color.Parse("#CCCCCC");
        if (!ActionDisplayNames.TryGetValue(action, out var displayName))
            displayName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(action.Replace("_", " "));

        var context = action switch
        {
            "launch_exe" or "close_program" or "mute_program" when !string.IsNullOrEmpty(path)
                => Path.GetFileNameWithoutExtension(path),
            "switch_profile" when !string.IsNullOrEmpty(profileName) => profileName,
            "switch_profile" when !string.IsNullOrEmpty(path) => path,
            "macro" when !string.IsNullOrEmpty(macroKeys) => macroKeys,
            _ => null
        };
        if (context != null) displayName = $"{displayName}: {context}";

        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, gestureColor.R, gestureColor.G, gestureColor.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = gestureLabel, FontSize = 8,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(gestureColor)
            }
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var actionText = new TextBlock
        {
            Text = displayName, FontSize = 10,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(actionColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(actionText, 1);
        grid.Children.Add(actionText);

        return grid;
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
                ? string.Join(", ", knob.Apps.Select(a => {
                    var n = Path.GetFileNameWithoutExtension(a);
                    return string.IsNullOrEmpty(n) ? a : char.ToUpperInvariant(n[0]) + n[1..];
                }))
                : "App Group",
            "output_device" => "Output Device",
            "input_device" => "Input Device",
            "monitor" => "Monitor Brightness",
            "led_brightness" => "LED Brightness",
            _ when t.StartsWith("ha_") => FormatHATarget(t),
            _ when t.StartsWith("govee") => "Govee",
            _ => CamelToTitle(t)
        };
    }

    private static string FormatHATarget(string target)
    {
        var parts = target.Split(':', 2);
        var entityId = parts.Length > 1 ? parts[1] : "";
        if (!string.IsNullOrEmpty(entityId))
        {
            var name = entityId.Contains('.') ? entityId.Split('.', 2)[1] : entityId;
            name = name.Replace("_", " ");
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
        }
        return "Home Assistant";
    }

    private static string CamelToTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new System.Text.StringBuilder();
        result.Append(char.ToUpperInvariant(s[0]));
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == '_' || s[i] == '.') result.Append(' ');
            else result.Append(s[i]);
        }
        return result.ToString();
    }
}
