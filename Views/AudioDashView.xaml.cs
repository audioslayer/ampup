using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
namespace AmpUp.Views;

public partial class AudioDashView : UserControl
{
    private AppConfig? _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onSave;

    // Two timers: fast for peak levels, slower for session list + stats
    private readonly DispatcherTimer _peakTimer;
    private readonly DispatcherTimer _sessionTimer;

    // Live row controls — rebuilt on session list refresh
    private readonly List<SessionRowControls> _rowControls = new();

    // Inline assign panel — only one expanded at a time
    private StackPanel? _expandedPanel;
    private string? _expandedProcess;

    private record SessionRowControls(
        string ProcessName,
        AudioSessionControl Session,
        Border PeakFill,
        TextBlock VolumeLabel,
        Border PeakTrack
    );

    public AudioDashView()
    {
        InitializeComponent();

        _peakTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _peakTimer.Tick += OnPeakTick;

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += OnSessionTick;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshAll();
                _peakTimer.Start();
                _sessionTimer.Start();
            }
            else
            {
                _peakTimer.Stop();
                _sessionTimer.Stop();
            }
        };
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onSave)
    {
        _config = config;
        _mixer = mixer;
        _onSave = onSave;
        if (IsVisible)
            RefreshAll();
    }

    // ── Timer callbacks ─────────────────────────────────────────────

    private void OnPeakTick(object? sender, EventArgs e) => UpdatePeakLevels();
    private void OnSessionTick(object? sender, EventArgs e) => RefreshAll();

    // ── Refresh ──────────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (_mixer == null) return;
        RefreshStats();
        RefreshSessionList();
    }

    private void RefreshStats()
    {
        if (_mixer == null) return;
        try
        {
            var (masterVol, masterMuted) = _mixer.GetMasterInfo();
            MasterVolLabel.Text = masterMuted ? "Muted" : $"{(int)Math.Round(masterVol * 100)}%";
            MasterVolLabel.Foreground = masterMuted
                ? (SolidColorBrush)FindResource("DangerRedBrush")
                : (SolidColorBrush)FindResource("AccentBrush");
            MasterMuteLabel.Text = masterMuted ? "MUTED" : "";

            var (output, input) = _mixer.GetDefaultDeviceNames();
            OutputDevLabel.Text = Shorten(output, 28);
            InputDevLabel.Text = Shorten(input, 28);
        }
        catch { }
    }

    private void RefreshSessionList()
    {
        if (_mixer == null) return;

        var sessions = _mixer.GetAllSessionsInfo();

        // Sort: active (peak > 0.01) first, then by peak desc, then by name
        sessions = sessions
            .OrderByDescending(s => s.Peak > 0.01f ? 1 : 0)
            .ThenByDescending(s => s.Peak)
            .ThenBy(s => s.ProcessName)
            .ToList();

        StreamCountLabel.Text = sessions.Count.ToString();
        EmptyState.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Rebuild the session list panel
        SessionListPanel.Children.Clear();
        _rowControls.Clear();

        foreach (var info in sessions)
            SessionListPanel.Children.Add(BuildSessionRow(info));
    }

    private void UpdatePeakLevels()
    {
        foreach (var row in _rowControls)
        {
            try
            {
                float peak = row.Session.AudioMeterInformation.MasterPeakValue;
                float vol = row.Session.SimpleAudioVolume.Volume;

                // Update volume label
                row.VolumeLabel.Text = $"{(int)Math.Round(vol * 100)}%";

                // Update peak bar width
                double trackWidth = row.PeakTrack.ActualWidth;
                if (trackWidth <= 0) trackWidth = 120;
                row.PeakFill.Width = Math.Clamp(peak * trackWidth, 0, trackWidth);

                // Color: accent when active, dim when silent
                bool active = peak > 0.02f;
                row.PeakFill.Background = active
                    ? (SolidColorBrush)FindResource("AccentBrush")
                    : (SolidColorBrush)FindResource("InputBorderBrush");
            }
            catch { }
        }
    }

    // ── Row builder ──────────────────────────────────────────────────

    private UIElement BuildSessionRow(AudioMixer.SessionInfo info)
    {
        var accent = GetAccentColor();
        bool active = info.Peak > 0.01f;

        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 6, 4, 6),
            Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.Background = (SolidColorBrush)FindResource("CardBgBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        // Col 0: App icon
        var iconEl = BuildAppIcon(info.ProcessName, info.Pid, accent);
        Grid.SetColumn(iconEl, 0);
        grid.Children.Add(iconEl);

        // Col 1: App name
        var nameLabel = new TextBlock
        {
            Text = TitleCase(info.DisplayName),
            FontSize = 12,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = active
                ? (SolidColorBrush)FindResource("TextPrimaryBrush")
                : (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 8, 0),
        };
        Grid.SetColumn(nameLabel, 1);
        grid.Children.Add(nameLabel);

        // Col 2: Volume %
        var volLabel = new TextBlock
        {
            Text = $"{(int)Math.Round(info.Volume * 100)}%",
            FontSize = 11,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(volLabel, 2);
        grid.Children.Add(volLabel);

        // Col 3: Peak bar track + fill
        var peakTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = (SolidColorBrush)FindResource("CardBorderBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var peakFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = active
                ? (SolidColorBrush)FindResource("AccentBrush")
                : (SolidColorBrush)FindResource("InputBorderBrush"),
            Width = Math.Clamp(info.Peak * 104, 0, 104),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        peakTrack.Child = peakFill;
        Grid.SetColumn(peakTrack, 3);
        grid.Children.Add(peakTrack);

        // Col 4: Knob assignment badge
        var knobBadge = BuildKnobBadge(info.ProcessName, accent);
        Grid.SetColumn(knobBadge, 4);
        grid.Children.Add(knobBadge);

        // Col 5: Mute status
        var statusBadge = BuildStatusBadge(info.Muted, active);
        Grid.SetColumn(statusBadge, 5);
        grid.Children.Add(statusBadge);

        row.Child = grid;

        // Track row for live peak updates
        // We need the session — find it by process name from mixer sessions
        var session = GetSessionForProcess(info.ProcessName);
        if (session != null)
        {
            _rowControls.Add(new SessionRowControls(
                info.ProcessName,
                session,
                peakFill,
                volLabel,
                peakTrack
            ));
        }

        // Inline assign panel (hidden by default)
        var assignPanel = BuildInlineAssignPanel(info.ProcessName);
        assignPanel.Visibility = Visibility.Collapsed;

        var wrapper = new StackPanel();
        wrapper.Children.Add(row);
        wrapper.Children.Add(assignPanel);

        // Click to toggle inline assign panel
        row.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ToggleInlineAssign(info.ProcessName, assignPanel);
        };

        return wrapper;
    }

    private UIElement BuildAppIcon(string processName, int pid, Color accent)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            var exePath = proc.MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (sysIcon != null)
                {
                    var bmpSrc = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmpSrc.Freeze();
                    sysIcon.Dispose();
                    return new Border
                    {
                        Width = 28, Height = 28,
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new Image
                        {
                            Source = bmpSrc,
                            Width = 18, Height = 18,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        }
                    };
                }
            }
        }
        catch { }

        // Letter fallback
        return BuildLetterIcon(processName.Length > 0 ? char.ToUpperInvariant(processName[0]).ToString() : "?", accent);
    }

    private static Border BuildLetterIcon(string letter, Color color)
    {
        return new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Material.Icons.WPF.MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.Application,
                Width = 16, Height = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, color.R, color.G, color.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
    }

    private UIElement BuildKnobBadge(string processName, Color accent)
    {
        var assignedKnob = FindAssignedKnob(processName);

        if (assignedKnob != null)
        {
            string label = !string.IsNullOrWhiteSpace(assignedKnob.Label)
                ? assignedKnob.Label
                : $"Knob {assignedKnob.Idx + 1}";

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            badge.Child = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
            };
            return badge;
        }

        // Unassigned — dimmed pill
        var unassigned = new Border
        {
            Background = (SolidColorBrush)FindResource("InputBgBrush"),
            BorderBrush = (SolidColorBrush)FindResource("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        unassigned.Child = new TextBlock
        {
            Text = "Unassigned",
            FontSize = 9,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
        };
        return unassigned;
    }

    private static UIElement BuildStatusBadge(bool muted, bool active)
    {
        string text;
        Color color;

        if (muted)
        {
            text = "MUTED";
            color = Color.FromRgb(0xFF, 0x44, 0x44);
        }
        else if (active)
        {
            text = "ACTIVE";
            color = Color.FromRgb(0x00, 0xDD, 0x77);
        }
        else
        {
            text = "SILENT";
            color = Color.FromRgb(0x55, 0x55, 0x55);
        }

        return new TextBlock
        {
            Text = text,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── Inline assign panel ──────────────────────────────────────────

    private void ToggleInlineAssign(string processName, StackPanel assignPanel)
    {
        // Collapse previously expanded panel
        if (_expandedPanel != null && _expandedPanel != assignPanel)
        {
            _expandedPanel.Visibility = Visibility.Collapsed;
        }

        if (assignPanel.Visibility == Visibility.Visible)
        {
            assignPanel.Visibility = Visibility.Collapsed;
            _expandedPanel = null;
            _expandedProcess = null;
        }
        else
        {
            // Refresh pill states before showing
            RebuildAssignPanelContent(assignPanel, processName);
            assignPanel.Visibility = Visibility.Visible;
            _expandedPanel = assignPanel;
            _expandedProcess = processName;
        }
    }

    private StackPanel BuildInlineAssignPanel(string processName)
    {
        var panel = new StackPanel();
        RebuildAssignPanelContent(panel, processName);
        return panel;
    }

    private void RebuildAssignPanelContent(StackPanel panel, string processName)
    {
        panel.Children.Clear();
        if (_config == null) return;

        var accent = GetAccentColor();
        var assignedKnob = FindAssignedKnob(processName);

        // Horizontal row of knob pills
        var pillRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(40, 2, 8, 8),
        };

        // Label
        pillRow.Children.Add(new TextBlock
        {
            Text = "ASSIGN TO",
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        for (int i = 0; i < 5; i++)
        {
            int knobIdx = i;
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
            string knobLabel = knob != null && !string.IsNullOrWhiteSpace(knob.Label)
                ? knob.Label : $"Knob {knobIdx + 1}";

            bool isCurrent = knob?.Target?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true
                || (knob?.Target == "apps" && knob.Apps.Any(a => a.Equals(processName, StringComparison.OrdinalIgnoreCase)));

            var pill = new Border
            {
                Background = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B))
                    : (SolidColorBrush)FindResource("CardBgBrush"),
                BorderBrush = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B))
                    : (SolidColorBrush)FindResource("InputBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            pill.Child = new TextBlock
            {
                Text = knobLabel,
                FontSize = 10,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isCurrent
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            };

            bool captured = isCurrent;
            pill.MouseEnter += (_, _) =>
            {
                pill.Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
                pill.BorderBrush = new SolidColorBrush(accent);
            };
            pill.MouseLeave += (_, _) =>
            {
                pill.Background = captured
                    ? new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B))
                    : (SolidColorBrush)FindResource("CardBgBrush");
                pill.BorderBrush = captured
                    ? new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B))
                    : (SolidColorBrush)FindResource("InputBorderBrush");
            };

            pill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (captured)
                {
                    RemoveKnobAssignment(knobIdx);
                }
                else
                {
                    AssignToKnob(processName, knobIdx);
                }
                RefreshSessionList();
            };

            pillRow.Children.Add(pill);
        }

        // Unassign button if currently assigned
        if (assignedKnob != null)
        {
            var removePill = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
            };
            removePill.Child = new TextBlock
            {
                Text = "Remove",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
            };
            removePill.MouseEnter += (_, _) => removePill.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0x44, 0x44));
            removePill.MouseLeave += (_, _) => removePill.Background = Brushes.Transparent;
            removePill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                RemoveKnobAssignment(assignedKnob.Idx);
                RefreshSessionList();
            };
            pillRow.Children.Add(removePill);
        }

        panel.Children.Add(pillRow);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private KnobConfig? FindAssignedKnob(string processName)
    {
        if (_config == null) return null;
        return _config.Knobs.FirstOrDefault(k =>
            k.Target?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true
            || (k.Target == "apps" && k.Apps.Any(a => a.Equals(processName, StringComparison.OrdinalIgnoreCase))));
    }

    private void AssignToKnob(string processName, int knobIdx)
    {
        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
        if (knob == null) return;
        knob.Target = processName.ToLowerInvariant();
        knob.Apps.Clear();
        _onSave?.Invoke(_config);
    }

    private void RemoveKnobAssignment(int knobIdx)
    {
        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
        if (knob == null) return;
        knob.Target = "none";
        knob.Apps.Clear();
        _onSave?.Invoke(_config);
    }

    private AudioSessionControl? GetSessionForProcess(string processName)
    {
        return _mixer?.GetSessionForProcess(processName);
    }

    private Color GetAccentColor()
    {
        try { return ((SolidColorBrush)FindResource("AccentBrush")).Color; } catch { }
        return Color.FromRgb(0x00, 0xE6, 0x76);
    }

    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static string Shorten(string s, int max)
        => s.Length > max ? s[..max] + "…" : s;
}
