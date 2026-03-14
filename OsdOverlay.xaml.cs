using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp;

public partial class OsdOverlay : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private readonly Storyboard _fadeOut;
    private bool _closing;
    private OsdPosition _position = OsdPosition.BottomRight;

    // Configurable durations (seconds)
    public double VolumeDuration { get; set; } = 2.0;
    public double ProfileDuration { get; set; } = 3.5;
    public double DeviceDuration { get; set; } = 2.5;

    public OsdOverlay()
    {
        InitializeComponent();
        ApplyAccentColors();

        _fadeOut = (Storyboard)FindResource("FadeOut");
        _fadeOut.Completed += (_, _) => Hide();

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            AnimateOut();
        };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(ApplyAccentColors);
    }

    private void ApplyAccentColors()
    {
        var accent = ThemeManager.Accent;

        // Border gradient — visible accent glow on all edges
        var borderBrush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(ThemeManager.WithAlpha(accent, 0x66), 0.0),
                new GradientStop(ThemeManager.WithAlpha(accent, 0x33), 0.5),
                new GradientStop(ThemeManager.WithAlpha(accent, 0x55), 1.0),
            }
        };
        borderBrush.Freeze();
        GlassPanel.BorderBrush = borderBrush;

        // Category label
        CategoryLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));

        // Value text
        OsdValue.Foreground = new SolidColorBrush(accent);

        // Bar track background
        BarTrack.Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x1A));

        // Bar fill gradient (AccentDim -> Accent -> AccentGlow)
        var fillBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(ThemeManager.AccentDim, 0),
            new GradientStop(accent, 0.7),
            new GradientStop(ThemeManager.AccentGlow, 1),
        }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        fillBrush.Freeze();
        BarFill.Background = fillBrush;

        // Bar glow background + effect
        BarGlow.Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66));
        BarGlow.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = accent, BlurRadius = 12, Opacity = 0.4, ShadowDepth = 0
        };

        // Glow layer color
        GlowLayer.Background = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.6, RadiusY = 0.8,
            GradientStops = new GradientStopCollection
            {
                new GradientStop(ThemeManager.WithAlpha(accent, 0x30), 0.3),
                new GradientStop(ThemeManager.WithAlpha(accent, 0x00), 1.0),
            }
        };
    }

    public void SetPosition(OsdPosition position)
    {
        _position = position;
    }

    private void SetTextIcon(string text, double fontSize = 24)
    {
        OsdIconHost.Content = new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void SetSymbolIcon(string symbolName, string colorHex)
    {
        var kind = Enum.TryParse<MaterialIconKind>(symbolName, out var k) ? k : MaterialIconKind.VolumeHigh;
        var icon = new Material.Icons.WPF.MaterialIcon { Kind = kind, Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center };
        try { icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }
        OsdIconHost.Content = icon;
    }

    /// <summary>
    /// Show volume change: label, percentage, animated bar.
    /// </summary>
    public void ShowVolume(string label, int percent, string symbolName = "VolumeHigh")
    {
        _closing = false;
        _dismissTimer.Stop();

        Width = 372;
        ProfileBindingsPanel.Visibility = Visibility.Collapsed;

        CategoryLabel.Text = "VOLUME";
        SetSymbolIcon(symbolName, ThemeManager.AccentHex);
        OsdTitle.Text = label;
        OsdValue.Text = $"{percent}%";
        OsdValue.Visibility = Visibility.Visible;
        BarContainer.Visibility = Visibility.Visible;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(VolumeDuration);

        // Animate bar fill after layout
        Dispatcher.InvokeAsync(() =>
        {
            double maxWidth = BarContainer.ActualWidth > 0 ? BarContainer.ActualWidth : 300;
            double targetWidth = maxWidth * Math.Clamp(percent / 100.0, 0, 1);

            var fillAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BarFill.BeginAnimation(WidthProperty, fillAnim);

            var glowAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BarGlow.BeginAnimation(WidthProperty, glowAnim);

            // Pulse the glow
            var pulse = (Storyboard)FindResource("BarGlowPulse");
            pulse.Begin(this);
        }, DispatcherPriority.Loaded);

        _dismissTimer.Start();
    }

    /// <summary>
    /// Show profile switch: icon + name + knob/button bindings.
    /// </summary>
    public void ShowProfileSwitch(string profileName, ProfileIconConfig iconCfg, AppConfig? config = null)
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = "PROFILE";
        SetSymbolIcon(iconCfg.Symbol, iconCfg.Color);
        OsdTitle.Text = profileName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        if (config != null)
        {
            Width = 620;
            BuildBindingsPanel(config);
            ProfileBindingsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            Width = 372;
            ProfileBindingsPanel.Visibility = Visibility.Collapsed;
        }

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(ProfileDuration);
        _dismissTimer.Start();
    }

    private void BuildBindingsPanel(AppConfig config)
    {
        ProfileBindingsPanel.Children.Clear();

        var accent = ThemeManager.Accent;

        // 5-column horizontal Grid matching physical device layout
        var columnsGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int c = 0; c < 5; c++)
        {
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);

            // Card border
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(i > 0 ? 3 : 0, 0, i < 4 ? 3 : 0, 0),
            };

            var stack = new StackPanel();

            // Number badge — centered circle
            // Mini knob visual with number overlay
            var knobColor = GetTargetColor(knob?.Target ?? "none");
            var miniKnob = new Controls.AnimatedKnobControl
            {
                Width = 32, Height = 32,
                ArcColor = knobColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            // Set a mid-range position so the arc is visible
            miniKnob.Value = 0.6f;

            var knobNumLabel = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI"),
            };
            var knobContainer = new Grid
            {
                Width = 32, Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            knobContainer.Children.Add(miniKnob);
            knobContainer.Children.Add(knobNumLabel);
            stack.Children.Add(knobContainer);

            // Knob target row — icon + label, horizontally centered
            var knobRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            string target = knob?.Target ?? "none";

            var iconText = new TextBlock
            {
                Text = GetTargetIcon(target),
                FontSize = 11,
                Foreground = new SolidColorBrush(GetTargetColor(target)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            knobRow.Children.Add(iconText);

            // Determine knob display label
            string knobLabel;
            if (knob != null && !string.IsNullOrEmpty(knob.Label))
                knobLabel = knob.Label;
            else if (knob != null && target == "apps" && knob.Apps.Count > 0)
                knobLabel = knob.Apps.Count == 1 ? knob.Apps[0] : "App Group";
            else
                knobLabel = FormatTarget(target);

            bool knobDim = knobLabel == "\u2014"; // em dash
            var knobLabelText = new TextBlock
            {
                Text = knobLabel,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(knobDim
                    ? Color.FromRgb(0x44, 0x44, 0x44)
                    : Color.FromRgb(0xE0, 0xE0, 0xE0)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 90,
                FontFamily = new FontFamily("Segoe UI")
            };
            knobRow.Children.Add(knobLabelText);
            stack.Children.Add(knobRow);

            // Divider
            var divider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x4D)), // ~30% alpha
                Margin = new Thickness(0, 6, 0, 6)
            };
            stack.Children.Add(divider);

            // Button action
            string action = btn?.Action ?? "none";
            bool hasAction = !string.IsNullOrEmpty(action) && action != "none";
            var actionLabel = hasAction ? FormatActionForOsd(action, btn) : "\u2014";
            var btnText = new TextBlock
            {
                Text = hasAction ? "\u25B8 " + actionLabel : "\u2014",
                FontSize = 10,
                Foreground = new SolidColorBrush(hasAction
                    ? Color.FromRgb(0x99, 0x99, 0x99)
                    : Color.FromRgb(0x44, 0x44, 0x44)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 90,
                FontFamily = new FontFamily("Segoe UI")
            };
            stack.Children.Add(btnText);

            card.Child = stack;
            System.Windows.Controls.Grid.SetColumn(card, i);
            columnsGrid.Children.Add(card);
        }

        ProfileBindingsPanel.Children.Add(columnsGrid);
    }

    private static string FormatTarget(string target) => target switch
    {
        "master" => "Master Volume",
        "mic" => "Microphone",
        "system" => "System",
        "discord" => "Discord",
        "spotify" => "Spotify",
        "chrome" => "Chrome",
        "any" => "All Apps",
        "active_window" => "Active Window",
        "none" or "" => "—",
        "ha_light" => "HA Light",
        "ha_media" => "HA Media",
        _ when target.StartsWith("govee:") => "Govee",
        _ => char.ToUpper(target[0]) + target[1..]
    };

    private static string GetTargetIcon(string target) => target switch
    {
        "master" => "🔊",
        "system" => "🔔",
        "any" or "active_window" => "🪟",
        "spotify" => "♪",
        "mic" or "input_device" => "🎙",
        "output_device" => "🔈",
        "monitor" => "🖥",
        "discord" => "💬",
        "chrome" => "◆",
        "apps" => "▣",
        _ when target.StartsWith("ha_") => "◈",
        _ when target.StartsWith("govee") => "◈",
        "led_brightness" => "💡",
        "none" or "" => "",
        _ => "♪"
    };

    private static Color GetTargetColor(string target) => target switch
    {
        "master" or "spotify" => Color.FromRgb(0x66, 0xBB, 0x6A),
        "mic" or "input_device" => Color.FromRgb(0xEF, 0x53, 0x50),
        "output_device" or "monitor" => Color.FromRgb(0xAB, 0x47, 0xBC),
        "discord" => Color.FromRgb(0x58, 0x65, 0xF2),
        "chrome" => Color.FromRgb(0x42, 0x85, 0xF4),
        _ when target.StartsWith("ha_") => Color.FromRgb(0x26, 0xC6, 0xDA),
        _ when target.StartsWith("govee") => Color.FromRgb(0xFF, 0x6F, 0x00),
        "system" or "any" or "active_window" => Color.FromRgb(0x42, 0xA5, 0xF5),
        "led_brightness" => Color.FromRgb(0xFF, 0xD5, 0x4F),
        _ => ThemeManager.Accent
    };

    private static string FormatAction(string action) => action switch
    {
        "mute_toggle" => "Mute Toggle",
        "play_pause" or "media_play_pause" => "Play / Pause",
        "next_track" or "media_next" => "Next Track",
        "prev_track" or "media_prev" => "Prev Track",
        "volume_up" => "Volume Up",
        "volume_down" => "Volume Down",
        "switch_profile" => "Switch Profile",
        "cycle_device" or "cycle_output" => "Cycle Device",
        "cycle_input" => "Cycle Input",
        "none" or "" => "—",
        "mute_master" => "Mute Master",
        "mute_mic" => "Mute Mic",
        "mute_program" => "Mute Program",
        "mute_active_window" => "Mute Active Window",
        "mute_app_group" => "Mute App Group",
        "mute_device" => "Mute Device",
        "launch_exe" => "Launch App",
        "close_program" => "Close Program",
        "select_output" => "Select Output",
        "select_input" => "Select Input",
        "macro" => "Macro",
        "cycle_brightness" => "Cycle Brightness",
        "power_sleep" => "Sleep",
        "power_lock" => "Lock",
        "power_off" => "Shut Down",
        "power_restart" => "Restart",
        "power_logoff" => "Log Off",
        "power_hibernate" => "Hibernate",
        _ => string.Join(" ", action.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w))
    };

    /// <summary>
    /// For the OSD, show just the context (app name, profile name) for space-constrained display.
    /// Falls back to the action name if no context available.
    /// </summary>
    private static string FormatActionForOsd(string action, ButtonConfig? btn)
    {
        if (btn == null) return FormatAction(action);

        // For actions with context, show just the context name (shorter for OSD)
        var context = action switch
        {
            "launch_exe" when !string.IsNullOrEmpty(btn.Path)
                => "Launch " + System.IO.Path.GetFileNameWithoutExtension(btn.Path),
            "close_program" when !string.IsNullOrEmpty(btn.Path)
                => "Close " + System.IO.Path.GetFileNameWithoutExtension(btn.Path),
            "mute_program" when !string.IsNullOrEmpty(btn.Path)
                => "Mute " + System.IO.Path.GetFileNameWithoutExtension(btn.Path),
            "switch_profile" when !string.IsNullOrEmpty(btn.ProfileName)
                => btn.ProfileName,
            "switch_profile" when !string.IsNullOrEmpty(btn.Path)
                => btn.Path,
            "macro" when !string.IsNullOrEmpty(btn.MacroKeys)
                => btn.MacroKeys,
            _ => null
        };

        return context ?? FormatAction(action);
    }

    /// <summary>
    /// Show device switch: device name + output/input type.
    /// </summary>
    public void ShowDevice(string deviceName, bool isOutput)
    {
        _closing = false;
        _dismissTimer.Stop();

        Width = 372;
        ProfileBindingsPanel.Visibility = Visibility.Collapsed;

        CategoryLabel.Text = isOutput ? "OUTPUT DEVICE" : "INPUT DEVICE";
        SetSymbolIcon(isOutput ? "VolumeHigh" : "Microphone", ThemeManager.AccentHex);
        OsdTitle.Text = deviceName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(DeviceDuration);
        _dismissTimer.Start();
    }

    private void PositionAndShow()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 20;
        double w = Width;
        // Estimate height: profile bindings are taller than volume/device OSD
        double estimatedHeight = ProfileBindingsPanel.Visibility == Visibility.Visible ? 200 : 120;

        switch (_position)
        {
            case OsdPosition.TopLeft:
                Left = workArea.Left + margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopCenter:
                Left = workArea.Left + (workArea.Width - w) / 2;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopRight:
                Left = workArea.Right - w - margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.BottomLeft:
                Left = workArea.Left + margin;
                Top = workArea.Bottom - estimatedHeight - margin;
                break;
            case OsdPosition.BottomCenter:
                Left = workArea.Left + (workArea.Width - w) / 2;
                Top = workArea.Bottom - estimatedHeight - margin;
                break;
            case OsdPosition.BottomRight:
            default:
                Left = workArea.Right - w - margin;
                Top = workArea.Bottom - estimatedHeight - margin;
                break;
        }

        bool alreadyVisible = IsVisible && !_closing;

        // Stop any in-progress fade-out
        if (_closing)
        {
            _fadeOut.Stop(this);
            _closing = false;
        }

        Show();

        // Only animate fade-in if not already showing
        if (!alreadyVisible)
        {
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        }
        else
        {
            // Ensure fully opaque (in case fade-out was mid-animation)
            RootPanel.Opacity = 1;
            RootPanel.Margin = new Thickness(16);
        }
    }

    private void AnimateOut()
    {
        if (_closing) return;
        _closing = true;
        _fadeOut.Begin(this);
    }
}
