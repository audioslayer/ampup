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

        // Border gradient
        var borderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(accent, 0x55),
            ThemeManager.WithAlpha(accent, 0x22),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
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
                new GradientStop(ThemeManager.WithAlpha(accent, 0x22), 0.4),
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

        CategoryLabel.Text = "VOLUME";
        SetSymbolIcon(symbolName, ThemeManager.AccentHex);
        OsdTitle.Text = label;
        OsdValue.Text = $"{percent}%";
        OsdValue.Visibility = Visibility.Visible;
        BarContainer.Visibility = Visibility.Visible;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2);

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
            BuildBindingsPanel(config);
            ProfileBindingsPanel.Visibility = Visibility.Visible;
            KnobsSectionLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x66));
            ButtonsSectionLabel.Foreground = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x66));
        }
        else
        {
            ProfileBindingsPanel.Visibility = Visibility.Collapsed;
        }

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(3.5);
        _dismissTimer.Start();
    }

    private void BuildBindingsPanel(AppConfig config)
    {
        KnobsPanel.Children.Clear();
        ButtonsPanel.Children.Clear();

        // Knob rows
        foreach (var knob in config.Knobs.OrderBy(k => k.Idx))
        {
            var row = BuildKnobRow(knob);
            KnobsPanel.Children.Add(row);
        }

        // Button rows — only non-none tap actions
        bool anyButtons = false;
        foreach (var btn in config.Buttons.OrderBy(b => b.Idx))
        {
            if (string.IsNullOrEmpty(btn.Action) || btn.Action == "none")
                continue;

            var row = BuildButtonRow(btn);
            ButtonsPanel.Children.Add(row);
            anyButtons = true;
        }

        ButtonsSectionLabel.Visibility = anyButtons ? Visibility.Visible : Visibility.Collapsed;
        ButtonsPanel.Visibility = anyButtons ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildKnobRow(KnobConfig knob)
    {
        var grid = new System.Windows.Controls.Grid { Height = 28, Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Number circle
        var circle = new Border
        {
            Width = 16, Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        circle.Child = new TextBlock
        {
            Text = (knob.Idx + 1).ToString(),
            FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(circle, 0);
        grid.Children.Add(circle);

        // Target icon
        var iconColor = GetTargetColor(knob.Target);
        var iconText = new TextBlock
        {
            Text = GetTargetIcon(knob.Target),
            FontSize = 13,
            Foreground = new SolidColorBrush(iconColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(iconText, 1);
        grid.Children.Add(iconText);

        // Label
        string label = !string.IsNullOrEmpty(knob.Label) ? knob.Label : FormatTarget(knob.Target);
        bool isDim = label == "—";
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(isDim
                ? Color.FromRgb(0x44, 0x44, 0x44)
                : Color.FromRgb(0xC8, 0xC8, 0xC8)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        System.Windows.Controls.Grid.SetColumn(labelText, 2);
        grid.Children.Add(labelText);

        return grid;
    }

    private UIElement BuildButtonRow(ButtonConfig btn)
    {
        var grid = new System.Windows.Controls.Grid { Height = 28, Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Number circle
        var circle = new Border
        {
            Width = 16, Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        circle.Child = new TextBlock
        {
            Text = (btn.Idx + 1).ToString(),
            FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(circle, 0);
        grid.Children.Add(circle);

        // Action icon (bullet)
        var iconText = new TextBlock
        {
            Text = "▸",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(iconText, 1);
        grid.Children.Add(iconText);

        // Action label
        var labelText = new TextBlock
        {
            Text = FormatAction(btn.Action),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        System.Windows.Controls.Grid.SetColumn(labelText, 2);
        grid.Children.Add(labelText);

        return grid;
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
        "master" or "system" or "any" or "active_window" or "spotify" => "♪",
        "mic" or "input_device" => "◎",
        "output_device" or "monitor" => "▶",
        "discord" => "◉",
        "chrome" => "◆",
        _ when target.StartsWith("ha_") => "◈",
        _ when target.StartsWith("govee") => "◈",
        "led_brightness" => "◉",
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
    /// Show device switch: device name + output/input type.
    /// </summary>
    public void ShowDevice(string deviceName, bool isOutput)
    {
        _closing = false;
        _dismissTimer.Stop();

        CategoryLabel.Text = isOutput ? "OUTPUT DEVICE" : "INPUT DEVICE";
        SetSymbolIcon(isOutput ? "VolumeHigh" : "Microphone", ThemeManager.AccentHex);
        OsdTitle.Text = deviceName;
        OsdValue.Visibility = Visibility.Collapsed;
        BarContainer.Visibility = Visibility.Collapsed;

        PositionAndShow();
        _dismissTimer.Interval = TimeSpan.FromSeconds(2.5);
        _dismissTimer.Start();
    }

    private void PositionAndShow()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 20;

        switch (_position)
        {
            case OsdPosition.TopLeft:
                Left = workArea.Left + margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopCenter:
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.TopRight:
                Left = workArea.Right - Width - margin;
                Top = workArea.Top + margin;
                break;
            case OsdPosition.BottomLeft:
                Left = workArea.Left + margin;
                Top = workArea.Bottom - 120;
                break;
            case OsdPosition.BottomCenter:
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Bottom - 120;
                break;
            case OsdPosition.BottomRight:
            default:
                Left = workArea.Right - Width - margin;
                Top = workArea.Bottom - 120;
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
