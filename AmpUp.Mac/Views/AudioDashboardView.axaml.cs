using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core;
using AmpUp.Core.Engine;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

/// <summary>
/// Real-time audio session dashboard.
/// Shows all running audio apps with peak level, volume %, knob assignment, and quick-assign.
/// </summary>
public partial class AudioDashboardView : UserControl
{
    // ── Config / save ─────────────────────────────────────────────────────────
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;

    // ── Session state ─────────────────────────────────────────────────────────
    private List<AudioSessionInfo> _sessions = new();

    // ── Timers ────────────────────────────────────────────────────────────────
    private DispatcherTimer? _levelTimer;   // 500ms — update peak levels
    private DispatcherTimer? _scanTimer;    // 5s    — rescan session list

    // ── Quick-assign state ────────────────────────────────────────────────────
    private bool _quickAssignActive;
    private AudioSessionInfo? _pendingAssign;

    // ── Theme colors ──────────────────────────────────────────────────────────
    private static readonly Color Accent       = Color.Parse("#00E676");
    private static readonly Color AccentDim    = Color.Parse("#00A854");
    private static readonly IBrush CardBg      = new SolidColorBrush(Color.Parse("#1C1C1C"));
    private static readonly IBrush CardBorder  = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#00E676"));
    private static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#E8E8E8"));
    private static readonly IBrush TextSec     = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly IBrush TextDim     = new SolidColorBrush(Color.Parse("#6A6A6A"));
    private static readonly IBrush BgDark      = new SolidColorBrush(Color.Parse("#141414"));
    private static readonly IBrush DangerRed   = new SolidColorBrush(Color.Parse("#FF4444"));

    public AudioDashboardView()
    {
        InitializeComponent();
        BtnRefresh.Click += (_, _) => ScanSessions();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── External wiring ───────────────────────────────────────────────────────

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _config = config;
        _onSave = onSave;
        ScanSessions();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _levelTimer.Tick += (_, _) => UpdatePeakLevels();
        _levelTimer.Start();

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _scanTimer.Tick += (_, _) => ScanSessions();
        _scanTimer.Start();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _levelTimer?.Stop();
        _scanTimer?.Stop();
    }

    // ── Session scanning ──────────────────────────────────────────────────────

    private void ScanSessions()
    {
        var sessions = GetAudioSessions();

        // Sort: playing (peak > 1%) first by peak desc, then alphabetically
        _sessions = sessions
            .OrderByDescending(s => s.Peak > 0.01f ? 1 : 0)
            .ThenByDescending(s => s.Peak)
            .ThenBy(s => s.AppName)
            .ToList();

        RebuildUI();
    }

    private void UpdatePeakLevels()
    {
        bool changed = false;
        foreach (var s in _sessions)
        {
            float newPeak = GetProcessPeak(s.Pid);
            if (Math.Abs(newPeak - s.Peak) > 0.005f)
            {
                s.Peak = newPeak;
                changed = true;
            }
        }

        if (changed)
        {
            // Re-sort — playing apps bubble up
            _sessions = _sessions
                .OrderByDescending(s => s.Peak > 0.01f ? 1 : 0)
                .ThenByDescending(s => s.Peak)
                .ThenBy(s => s.AppName)
                .ToList();
            RebuildUI();
        }
        else
        {
            // Just update level bars in-place without full rebuild
            UpdateLevelBarsInPlace();
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void RebuildUI()
    {
        SessionListPanel.Children.Clear();
        SessionCountLabel.Text = $"{_sessions.Count} session{(_sessions.Count == 1 ? "" : "s")}";

        bool hasUnassigned = _sessions.Any(s => !s.IsAssigned);
        AssignHintLabel.IsVisible = hasUnassigned;

        foreach (var session in _sessions)
        {
            SessionListPanel.Children.Add(MakeSessionRow(session));
        }

        if (_sessions.Count == 0)
        {
            SessionListPanel.Children.Add(MakeEmptyState());
        }
    }

    private Control MakeSessionRow(AudioSessionInfo session)
    {
        bool isAssigned = session.IsAssigned;
        bool isPlaying  = session.Peak > 0.01f;

        // Card border color: accent for assigned, subtle for unassigned
        var borderColor = isAssigned
            ? new SolidColorBrush(Color.FromArgb(80, Accent.R, Accent.G, Accent.B))
            : CardBorder;

        var card = new Border
        {
            Background   = CardBg,
            BorderBrush  = borderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding      = new Thickness(12, 10),
            Cursor       = isAssigned ? Cursor.Default : new Cursor(StandardCursorType.Hand),
            Tag          = session,
        };

        // Hover tint for unassigned rows (quick-assign affordance)
        if (!isAssigned)
        {
            card.PointerEntered += (_, _) =>
                card.Background = new SolidColorBrush(Color.Parse("#242424"));
            card.PointerExited += (_, _) =>
                card.Background = CardBg;
            card.PointerPressed += OnSessionCardPressed;
        }

        // ── Row layout: icon | name + bars | volume | assignment badge ────────
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*,60,80"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // ── App icon (letter avatar) ─────────────────────────────────────────
        row.Children.Add(MakeAppIcon(session, isPlaying));

        // ── Center column: app name + peak bar ──────────────────────────────
        var centerCol = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };
        Grid.SetColumn(centerCol, 1);

        // App name
        centerCol.Children.Add(new TextBlock
        {
            Text       = session.AppName,
            Foreground = isAssigned ? AccentBrush : TextPrimary,
            FontSize   = 13,
            FontWeight = isAssigned ? FontWeight.SemiBold : FontWeight.Normal,
        });

        // Peak bar
        var barTrack = new Border
        {
            Background    = new SolidColorBrush(Color.Parse("#2A2A2A")),
            CornerRadius  = new CornerRadius(2),
            Height        = 4,
            Margin        = new Thickness(0, 0, 16, 0),
        };
        var barFill = new Border
        {
            Background    = MakePeakBrush(session.Peak, isAssigned),
            CornerRadius  = new CornerRadius(2),
            Height        = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag           = "peakbar",
        };
        // Width is set dynamically; wrap in a relative container
        var barContainer = new Grid { Tag = $"barcontainer:{session.Pid}" };
        barContainer.Children.Add(barTrack);
        barContainer.Children.Add(barFill);

        // Store fill ref on session for in-place updates
        session.PeakBarFill = barFill;
        session.PeakBarContainer = barContainer;

        // Bind bar width on size change
        barContainer.SizeChanged += (_, e) =>
        {
            UpdateBarWidth(barFill, session.Peak, e.NewSize.Width);
        };

        centerCol.Children.Add(barContainer);
        row.Children.Add(centerCol);

        // ── Volume % ─────────────────────────────────────────────────────────
        var volLabel = new TextBlock
        {
            Text              = FormatVolume(session.Volume),
            Foreground        = isPlaying ? TextPrimary : TextDim,
            FontSize          = 13,
            FontFamily        = new FontFamily("Consolas,Menlo,monospace"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag               = $"vollabel:{session.Pid}",
        };
        Grid.SetColumn(volLabel, 2);
        session.VolumeLabel = volLabel;
        row.Children.Add(volLabel);

        // ── Assignment badge ─────────────────────────────────────────────────
        var badge = MakeAssignmentBadge(session);
        Grid.SetColumn(badge, 3);
        row.Children.Add(badge);

        card.Child = row;
        return card;
    }

    private Control MakeAppIcon(AudioSessionInfo session, bool isPlaying)
    {
        char letter = string.IsNullOrEmpty(session.AppName) ? '?' : char.ToUpper(session.AppName[0]);

        // Color based on first letter — deterministic hue
        var hue = (letter - 'A') * (360.0 / 26.0);
        var iconColor = ColorFromHsv(hue, 0.6, 0.75);

        var icon = new Border
        {
            Width        = 32,
            Height       = 32,
            CornerRadius = new CornerRadius(8),
            Background   = new SolidColorBrush(Color.FromArgb(60, iconColor.R, iconColor.G, iconColor.B)),
            BorderBrush  = new SolidColorBrush(Color.FromArgb(100, iconColor.R, iconColor.G, iconColor.B)),
            BorderThickness = new Thickness(1),
            Margin       = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        icon.Child = new TextBlock
        {
            Text                = letter.ToString(),
            Foreground          = new SolidColorBrush(iconColor),
            FontSize            = 14,
            FontWeight          = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        // Playing indicator: green pulse dot
        if (isPlaying)
        {
            var dot = new Border
            {
                Width        = 8,
                Height       = 8,
                CornerRadius = new CornerRadius(4),
                Background   = AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Margin       = new Thickness(0, 0, -2, -2),
            };

            var overlay = new Grid();
            overlay.Children.Add(icon);
            overlay.Children.Add(dot);

            var wrapper = new Grid
            {
                Width  = 40,
                Height = 40,
                VerticalAlignment = VerticalAlignment.Center,
            };
            wrapper.Children.Add(overlay);
            Grid.SetColumn(wrapper, 0);
            return wrapper;
        }

        var iconWrapper = new Grid
        {
            Width  = 40,
            Height = 40,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconWrapper.Children.Add(icon);
        Grid.SetColumn(iconWrapper, 0);
        return iconWrapper;
    }

    private Control MakeAssignmentBadge(AudioSessionInfo session)
    {
        if (session.IsAssigned)
        {
            // Green badge showing which knob
            var badge = new Border
            {
                Background    = new SolidColorBrush(Color.FromArgb(40, Accent.R, Accent.G, Accent.B)),
                BorderBrush   = new SolidColorBrush(Color.FromArgb(120, Accent.R, Accent.G, Accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius  = new CornerRadius(12),
                Padding       = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            badge.Child = new TextBlock
            {
                Text       = $"Knob {session.AssignedKnobIdx + 1}",
                Foreground = AccentBrush,
                FontSize   = 11,
                FontWeight = FontWeight.SemiBold,
            };
            return badge;
        }
        else
        {
            // Dim "Assign" affordance
            var badge = new Border
            {
                Background    = new SolidColorBrush(Color.Parse("#1A1A1A")),
                BorderBrush   = new SolidColorBrush(Color.Parse("#333333")),
                BorderThickness = new Thickness(1),
                CornerRadius  = new CornerRadius(12),
                Padding       = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Cursor        = new Cursor(StandardCursorType.Hand),
            };
            badge.Child = new TextBlock
            {
                Text       = "+ Assign",
                Foreground = TextDim,
                FontSize   = 11,
            };
            return badge;
        }
    }

    private Control MakeEmptyState()
    {
        return new Border
        {
            Padding = new Thickness(24, 48),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text                = "🔇",
                        FontSize            = 36,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text                = "No audio sessions",
                        Foreground          = TextSec,
                        FontSize            = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text                = "Play audio in any app to see it here.",
                        Foreground          = TextDim,
                        FontSize            = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                }
            }
        };
    }

    // ── In-place level updates (avoids full rebuild) ───────────────────────────

    private void UpdateLevelBarsInPlace()
    {
        foreach (var session in _sessions)
        {
            if (session.PeakBarFill != null && session.PeakBarContainer != null)
            {
                UpdateBarWidth(session.PeakBarFill, session.Peak, session.PeakBarContainer.Bounds.Width);
                session.PeakBarFill.Background = MakePeakBrush(session.Peak, session.IsAssigned);
            }

            if (session.VolumeLabel != null)
            {
                session.VolumeLabel.Text = FormatVolume(session.Volume);
                session.VolumeLabel.Foreground = session.Peak > 0.01f ? TextPrimary : TextDim;
            }
        }
    }

    private static void UpdateBarWidth(Border bar, float peak, double containerWidth)
    {
        if (containerWidth > 0)
            bar.Width = Math.Max(0, peak * containerWidth);
    }

    private static IBrush MakePeakBrush(float peak, bool isAssigned)
    {
        if (peak > 0.8f) return new SolidColorBrush(Color.Parse("#FF4444")); // clipping
        if (peak > 0.5f) return new SolidColorBrush(Color.Parse("#FFB800")); // hot
        if (isAssigned)  return new SolidColorBrush(Color.Parse("#00E676")); // accent green
        return new SolidColorBrush(Color.Parse("#4CAF50")); // normal green
    }

    // ── Quick-assign ──────────────────────────────────────────────────────────

    private void OnSessionCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border card || card.Tag is not AudioSessionInfo session) return;
        if (_config == null || _onSave == null) return;
        if (session.IsAssigned) return;

        // Show knob picker popup
        ShowKnobPicker(card, session);
    }

    private void ShowKnobPicker(Control anchor, AudioSessionInfo session)
    {
        if (_config == null || _onSave == null) return;

        var popup = new Border
        {
            Background    = new SolidColorBrush(Color.Parse("#242424")),
            BorderBrush   = new SolidColorBrush(Color.Parse("#363636")),
            BorderThickness = new Thickness(1),
            CornerRadius  = new CornerRadius(8),
            Padding       = new Thickness(4),
            BoxShadow     = new BoxShadows(new BoxShadow
            {
                Blur      = 16,
                Color     = Color.FromArgb(80, 0, 0, 0),
            }),
            ZIndex        = 100,
        };

        var panel = new StackPanel { Spacing = 2 };

        var header = new TextBlock
        {
            Text       = $"Assign \"{session.AppName}\" to:",
            Foreground = TextSec,
            FontSize   = 11,
            Margin     = new Thickness(8, 6, 8, 4),
        };
        panel.Children.Add(header);

        for (int i = 0; i < 5; i++)
        {
            var knobIdx = i;
            var knob    = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
            string label = knob?.Label ?? $"Knob {i + 1}";

            var item = new Border
            {
                CornerRadius  = new CornerRadius(6),
                Padding       = new Thickness(8, 6),
                Cursor        = new Cursor(StandardCursorType.Hand),
                Background    = Avalonia.Media.Brushes.Transparent,
            };

            item.Child = new TextBlock
            {
                Text       = label,
                Foreground = TextPrimary,
                FontSize   = 13,
            };

            item.PointerEntered += (_, _) =>
                item.Background = new SolidColorBrush(Color.Parse("#333333"));
            item.PointerExited += (_, _) =>
                item.Background = Avalonia.Media.Brushes.Transparent;

            item.PointerPressed += (_, _) =>
            {
                AssignAppToKnob(session, knobIdx);
                // Close popup
                if (SessionListPanel.Parent is Panel outerPanel)
                    outerPanel.Children.Remove(popup);
                else if (popup.Parent is Panel p)
                    p.Children.Remove(popup);
            };

            panel.Children.Add(item);
        }

        popup.Child = panel;

        // Place popup over the view by adding to the scroll content's parent
        // Use an Overlay layer attached to the window
        if (TopLevel.GetTopLevel(this) is Window win)
        {
            var overlayLayer = OverlayLayer.GetOverlayLayer(this);
            if (overlayLayer != null)
            {
                var pt = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height + 4), win) ?? new Point(100, 100);

                Canvas.SetLeft(popup, pt.X);
                Canvas.SetTop(popup, pt.Y);

                overlayLayer.Children.Add(popup);

                // Dismiss on click outside
                void Dismiss(object? s, PointerPressedEventArgs e2)
                {
                    if (!popup.Bounds.Contains(e2.GetPosition(win)))
                    {
                        overlayLayer.Children.Remove(popup);
                        win.PointerPressed -= Dismiss;
                    }
                }
                win.PointerPressed += Dismiss;
                return;
            }
        }

        // Fallback: just assign to knob 0 silently (shouldn't happen)
        AssignAppToKnob(session, 0);
    }

    private void AssignAppToKnob(AudioSessionInfo session, int knobIdx)
    {
        if (_config == null || _onSave == null) return;

        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
        if (knob == null) return;

        // If knob is already an app group, add to it; otherwise set target to apps
        if (knob.Target == "apps")
        {
            if (!knob.Apps.Contains(session.ProcessName))
                knob.Apps.Add(session.ProcessName);
        }
        else
        {
            knob.Target = "apps";
            knob.Apps = new List<string> { session.ProcessName };
        }

        _onSave(_config);
        ScanSessions(); // refresh to show new assignment
    }

    // ── Audio data (platform shim) ────────────────────────────────────────────

    /// <summary>
    /// Returns current audio sessions. On Mac, this would call into MacAudioEngine / Swift bridge.
    /// On non-Mac (CI/dev), returns simulated data from running processes.
    /// </summary>
    private List<AudioSessionInfo> GetAudioSessions()
    {
        if (_config == null) return new List<AudioSessionInfo>();

        // Try to get sessions from MacAudioEngine if available
        // For now, enumerate running processes and simulate session info
        var sessions = new List<AudioSessionInfo>();

        try
        {
            // Known audio-producing process names (heuristic for when Swift bridge isn't available)
            var audioApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spotify", "music", "vlc", "chrome", "chromium", "firefox", "safari",
                "discord", "zoom", "slack", "teams", "webex", "skype", "facetime",
                "quicktimeplayer", "itunes", "applemusic", "audacity", "logic", "garageband",
                "facetimedaemon", "avconferenced", "ammusiclibraryagent",
            };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName;
                    string display = FormatProcessName(name);

                    // Only include likely audio apps
                    bool isKnown = audioApps.Any(a => name.Contains(a, StringComparison.OrdinalIgnoreCase));
                    if (!isKnown) continue;

                    int knobIdx = FindAssignedKnob(name);
                    float peak  = GetProcessPeak(proc.Id);
                    float vol   = GetProcessVolume(proc.Id, knobIdx);

                    sessions.Add(new AudioSessionInfo
                    {
                        Pid              = proc.Id,
                        ProcessName      = name,
                        AppName          = display,
                        Peak             = peak,
                        Volume           = vol,
                        AssignedKnobIdx  = knobIdx,
                    });
                }
                catch { /* process may have exited */ }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioDashboard: GetAudioSessions error: {ex.Message}");
        }

        return sessions;
    }

    private float GetProcessPeak(int pid)
    {
        // On Mac with Swift dylib: call ampup_get_process_peak(pid)
        // Stub: return 0 (no audio data without dylib)
        return 0f;
    }

    private float GetProcessVolume(int pid, int assignedKnobIdx)
    {
        if (_config == null || assignedKnobIdx < 0) return 1f;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == assignedKnobIdx);
        if (knob == null) return 1f;
        return VolumePipeline.ComputeVolume(knob.LastRawValue, knob);
    }

    private int FindAssignedKnob(string processName)
    {
        if (_config == null) return -1;

        foreach (var knob in _config.Knobs)
        {
            if (knob.Target == "apps" && knob.Apps.Any(a =>
                    FuzzyContains(processName, a)))
                return knob.Idx;

            if (knob.Target != null && !knob.Target.StartsWith("ha_") &&
                knob.Target != "master" && knob.Target != "mic" &&
                knob.Target != "system" && knob.Target != "any" &&
                knob.Target != "active_window" && knob.Target != "output_device" &&
                knob.Target != "input_device" && knob.Target != "monitor" &&
                knob.Target != "led_brightness" && knob.Target != "govee")
            {
                if (FuzzyContains(processName, knob.Target))
                    return knob.Idx;
            }
        }

        return -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool FuzzyContains(string processName, string target)
    {
        return processName.Replace(" ", "").Contains(
            target.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProcessName(string name)
    {
        // Common mappings
        return name.ToLowerInvariant() switch
        {
            "music"              => "Apple Music",
            "ammusiclibraryagent"=> "Apple Music",
            "spotify"            => "Spotify",
            "vlc"                => "VLC",
            "discord"            => "Discord",
            "chrome"             => "Google Chrome",
            "firefox"            => "Firefox",
            "safari"             => "Safari",
            "zoom.us"            => "Zoom",
            "slack"              => "Slack",
            "microsoft teams"    => "Teams",
            "facetime"           => "FaceTime",
            "quicktimeplayer"    => "QuickTime Player",
            _                    => ToTitleCase(name),
        };
    }

    private static string ToTitleCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Insert spaces before capitals (e.g. "QuickTimePlayer" → "Quick Time Player")
        var result = System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z])([A-Z])", " $1");
        return char.ToUpper(result[0]) + result[1..];
    }

    private static string FormatVolume(float vol)
    {
        return $"{(int)Math.Round(vol * 100)}%";
    }

    private static Color ColorFromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;

        double r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}

// ── Data model ────────────────────────────────────────────────────────────────

/// <summary>Represents one audio session / running app.</summary>
internal class AudioSessionInfo
{
    public int    Pid             { get; set; }
    public string ProcessName     { get; set; } = "";
    public string AppName         { get; set; } = "";
    public float  Peak            { get; set; }   // 0.0 – 1.0
    public float  Volume          { get; set; }   // 0.0 – 1.0
    public int    AssignedKnobIdx { get; set; } = -1;

    public bool IsAssigned => AssignedKnobIdx >= 0;

    // Live UI element references (for in-place updates)
    public Border?    PeakBarFill      { get; set; }
    public Grid?      PeakBarContainer { get; set; }
    public TextBlock? VolumeLabel      { get; set; }
}
