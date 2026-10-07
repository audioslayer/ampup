using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

// Audio Sessions — live list of apps playing audio, in the Groups-page design language.
// Rows are only rebuilt when the set of sessions (or assignments) changes; the shared
// 75ms _liveTimer updates peak / volume / mute on the existing rows in place.
public partial class MixerView
{
    private bool _audioSessionsExpanded;
    private bool _showHiddenSessions;
    private readonly DispatcherTimer _sessionRefreshTimer;
    private readonly List<SessionRowCtrl> _sessionRows = new();
    private readonly HashSet<string> _sessionKeys = new(); // "name:pid" keys of the rows currently shown
    private bool _sessionListDirty; // force a full rebuild on the next refresh (assignments changed)
    private StackPanel? _sessionListPanel;
    private Border? _sessionListContainer;
    private Border? _sessionEmptyState;
    private StackPanel? _sessionBody;
    private MaterialIcon? _sessionChevron;
    private TextBlock? _sessionCountText;
    private Border? _showHiddenRow;
    private TextBlock? _showHiddenLabel;
    private SolidColorBrush? _peakActiveBrush; // frozen accent brush for active peak fills

    private sealed class SessionRowCtrl
    {
        public SessionRowCtrl(string processName, NAudio.CoreAudioApi.AudioSessionControl session,
            Border peakFill, ScaleTransform peakScale, TextBlock volumeLabel, StyledSlider slider,
            MaterialIcon muteIcon, bool peakActive)
        {
            ProcessName = processName;
            Session = session;
            PeakFill = peakFill;
            PeakScale = peakScale;
            VolumeLabel = volumeLabel;
            Slider = slider;
            MuteIcon = muteIcon;
            PeakActive = peakActive;
        }

        public string ProcessName { get; }
        public NAudio.CoreAudioApi.AudioSessionControl Session { get; }
        public Border PeakFill { get; }
        public ScaleTransform PeakScale { get; }
        public TextBlock VolumeLabel { get; }
        public StyledSlider Slider { get; }
        public MaterialIcon MuteIcon { get; }
        public bool PeakActive { get; set; }
        public int LastVolPct { get; set; } = -1;
        public bool? LastMuted { get; set; }
        public bool Updating { get; set; } // suppress slider → session feedback during tick updates
    }

    // Session icon caches — Process.MainModule throws Win32Exception for protected
    // processes and ExtractAssociatedIcon is expensive; resolve each once per
    // process lifetime instead of on every 2s session-list rebuild.
    private static readonly Dictionary<string, string?> s_sessionExePaths = new();   // "name:pid" → exe path (null = inaccessible, don't retry)
    private static readonly Dictionary<string, BitmapSource?> s_sessionIcons = new(StringComparer.OrdinalIgnoreCase); // exe path → icon (null = extraction failed)

    // ── Build ──────────────────────────────────────────────────────────

    private void BuildAudioSessionsSection()
    {
        // Clickable header: accent section header + count, chevron on the right
        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Show or hide audio sessions" };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.SectionHeader("AUDIO SESSIONS", null, out _sessionCountText));
        _sessionChevron = UiKit.Icon(MaterialIconKind.ChevronDown, 18, "AccentBrush");
        _sessionChevron.Margin = new Thickness(0, 0, 4, 6);
        _sessionChevron.RenderTransformOrigin = new Point(0.5, 0.5);
        _sessionChevron.RenderTransform = new RotateTransform(-90);
        Grid.SetColumn(_sessionChevron, 1);
        header.Children.Add(_sessionChevron);
        header.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleAudioSessions(); };
        AudioSessionsContent.Children.Add(header);

        var intro = new TextBlock
        {
            Text = "Apps playing audio right now — set their level, mute them, or put them on a knob.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -4, 0, 10),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        AudioSessionsContent.Children.Add(intro);

        _sessionBody = new StackPanel { Visibility = Visibility.Collapsed };
        AudioSessionsContent.Children.Add(_sessionBody);
    }

    private void ToggleAudioSessions()
    {
        _audioSessionsExpanded = !_audioSessionsExpanded;
        if (_sessionBody != null)
            _sessionBody.Visibility = _audioSessionsExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_sessionChevron?.RenderTransform is RotateTransform rt)
            rt.Angle = _audioSessionsExpanded ? 0 : -90;

        if (_audioSessionsExpanded)
        {
            if (_sessionListPanel == null)
                BuildSessionListPanel();
            RefreshSessionList(force: true);
            if (!IsOwnerWindowQuiesced)
                _sessionRefreshTimer.Start();
            // Peak bars ride the always-running 75ms _liveTimer, gated on _audioSessionsExpanded
        }
        else
        {
            _sessionRefreshTimer.Stop();
        }
    }

    private void BuildSessionListPanel()
    {
        if (_sessionBody == null) return;

        _sessionListContainer = UiKit.ListContainer(out var rows);
        _sessionListPanel = rows;
        _sessionBody.Children.Add(_sessionListContainer);

        _sessionEmptyState = UiKit.EmptyState(MaterialIconKind.VolumeOff, "Nothing is playing",
            "Apps show up here as soon as they open an audio stream.");
        _sessionEmptyState.Visibility = Visibility.Collapsed;
        _sessionBody.Children.Add(_sessionEmptyState);

        _showHiddenRow = UiKit.LinkRow(MaterialIconKind.EyeOutline, "Show hidden (0)", false, () =>
        {
            _showHiddenSessions = !_showHiddenSessions;
            RefreshSessionList(force: true);
        }, "Hidden apps are also hidden from the tray mixer");
        _showHiddenRow.Margin = new Thickness(0, 6, 0, 0);
        _showHiddenLabel = ((StackPanel)_showHiddenRow.Child).Children.OfType<TextBlock>().First();
        _sessionBody.Children.Add(_showHiddenRow);
    }

    private static string SessionKey(AudioMixer.SessionInfo info)
        => $"{info.ProcessName.ToLowerInvariant()}:{info.Pid}";

    private void RefreshSessionList(bool force = false)
    {
        if (_mixer == null || _sessionListPanel == null) return;

        var hidden = _config?.HiddenTrayApps ?? new();
        var sessions = _mixer.GetAllSessionsInfo()
            .Where(s => _showHiddenSessions || !hidden.Any(h => h.Equals(s.ProcessName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => s.Peak > 0.01f ? 1 : 0)
            .ThenByDescending(s => s.Peak)
            .ThenBy(s => s.ProcessName)
            .ToList();

        // Diff by session key (name:pid) — when the set of sessions hasn't
        // changed, skip the full row rebuild (and the per-row icon work).
        // Volume/peak/mute on surviving rows are kept fresh by the 75ms tick;
        // ordering only re-sorts when sessions appear or vanish.
        if (!force && !_sessionListDirty
            && sessions.Count == _sessionKeys.Count
            && sessions.All(s => _sessionKeys.Contains(SessionKey(s))))
            return;

        _sessionListDirty = false;
        _sessionListPanel.Children.Clear();
        _sessionRows.Clear();
        _sessionKeys.Clear();

        foreach (var info in sessions)
        {
            _sessionKeys.Add(SessionKey(info));
            _sessionListPanel.Children.Add(BuildSessionRow(info));
        }

        bool any = sessions.Count > 0;
        if (_sessionListContainer != null) _sessionListContainer.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (_sessionEmptyState != null) _sessionEmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (_sessionCountText != null) _sessionCountText.Text = sessions.Count.ToString();

        int hiddenCount = hidden.Count;
        if (_showHiddenLabel != null)
            _showHiddenLabel.Text = _showHiddenSessions ? "Hide hidden apps" : $"Show hidden ({hiddenCount})";
        if (_showHiddenRow != null)
        {
            _showHiddenRow.Visibility = hiddenCount > 0 || _showHiddenSessions ? Visibility.Visible : Visibility.Collapsed;
            if (((StackPanel)_showHiddenRow.Child).Children[0] is MaterialIcon ic)
                ic.Kind = _showHiddenSessions ? MaterialIconKind.EyeOffOutline : MaterialIconKind.EyeOutline;
        }
    }

    private void UpdateSessionPeaks()
    {
        if (_sessionRows.Count == 0) return;

        // Hoisted out of the loop; frozen brush is reused across ticks and
        // rebuilt only when the accent color changes.
        var accent = ThemeManager.Accent;
        if (_peakActiveBrush == null || _peakActiveBrush.Color != accent)
        {
            _peakActiveBrush = new SolidColorBrush(accent);
            _peakActiveBrush.Freeze();
        }

        foreach (var row in _sessionRows)
        {
            try
            {
                float peak = row.Session.AudioMeterInformation.MasterPeakValue;
                var sav = row.Session.SimpleAudioVolume;
                int volPct = (int)Math.Round(sav.Volume * 100);
                if (volPct != row.LastVolPct && !row.Slider.IsMouseCaptured)
                {
                    row.LastVolPct = volPct;
                    row.VolumeLabel.Text = $"{volPct}%";
                    row.Updating = true;
                    row.Slider.Value = volPct;
                    row.Updating = false;
                }

                bool muted = sav.Mute;
                if (muted != row.LastMuted)
                {
                    row.LastMuted = muted;
                    ApplyMuteIcon(row.MuteIcon, muted);
                }

                // ScaleTransform is render-only — no layout pass per tick
                row.PeakScale.ScaleX = muted ? 0 : Math.Clamp(peak, 0f, 1f);

                bool active = peak > 0.02f && !muted;
                if (active != row.PeakActive)
                {
                    row.PeakActive = active;
                    if (active)
                        row.PeakFill.Background = _peakActiveBrush;
                    else
                        row.PeakFill.SetResourceReference(Border.BackgroundProperty, "InputBorderBrush");
                }
            }
            catch { }
        }
    }

    private static void ApplyMuteIcon(MaterialIcon icon, bool muted)
    {
        icon.Kind = muted ? MaterialIconKind.VolumeOff : MaterialIconKind.VolumeHigh;
        icon.SetResourceReference(MaterialIcon.ForegroundProperty, muted ? "DangerRedBrush" : "TextDimBrush");
    }

    // ── Row ────────────────────────────────────────────────────────────

    private UIElement BuildSessionRow(AudioMixer.SessionInfo info)
    {
        var accent = ThemeManager.Accent;
        bool active = info.Peak > 0.01f;
        string processName = info.ProcessName;
        var session = _mixer?.GetSessionForProcess(processName);

        // Right side: peak bar · volume slider · value · mute
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Peak bar — fill spans the full track and is driven by a ScaleTransform
        // (render-only) instead of Width, so the 75ms tick never invalidates layout.
        var peakTrack = new Border
        {
            Width = 64, Height = 4, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Live level",
        };
        peakTrack.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        var peakScale = new ScaleTransform(info.Muted ? 0 : Math.Clamp(info.Peak, 0f, 1f), 1.0);
        var peakFill = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2),
            RenderTransform = peakScale,
            RenderTransformOrigin = new Point(0, 0.5), // scale from the left edge
        };
        if (active && !info.Muted)
            peakFill.Background = new SolidColorBrush(accent);
        else
            peakFill.SetResourceReference(Border.BackgroundProperty, "InputBorderBrush");
        peakTrack.Child = peakFill;
        right.Children.Add(peakTrack);

        int volPct = (int)Math.Round(info.Volume * 100);
        var slider = new StyledSlider
        {
            Minimum = 0, Maximum = 100, Value = volPct, Step = 1,
            ShowLabel = false, Width = 130, Height = 24, AccentColor = accent,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "App volume",
            IsEnabled = session != null,
        };
        right.Children.Add(slider);

        var volLabel = new TextBlock
        {
            Text = $"{volPct}%", Width = 38, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        volLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        right.Children.Add(volLabel);

        SessionRowCtrl? ctrl = null;
        var muteBtn = UiKit.IconButton(MaterialIconKind.VolumeHigh, "Mute / unmute", _ =>
        {
            if (session == null) return;
            try
            {
                bool m = !session.SimpleAudioVolume.Mute;
                session.SimpleAudioVolume.Mute = m;
                if (ctrl != null) ctrl.LastMuted = m;
                if (ctrl?.MuteIcon is { } mi) ApplyMuteIcon(mi, m);
            }
            catch { }
        });
        var muteIcon = (MaterialIcon)muteBtn.Child;
        ApplyMuteIcon(muteIcon, info.Muted);
        // IconButton resets the icon brush on hover-out; re-apply the mute colour afterwards.
        muteBtn.MouseLeave += (_, _) =>
        {
            bool m = false;
            try { m = session?.SimpleAudioVolume.Mute ?? info.Muted; } catch { }
            ApplyMuteIcon(muteIcon, m);
        };
        right.Children.Add(muteBtn);

        // "…" menu (hover-only) — assign to knob / add to group / hide
        var more = UiKit.MoreButton(() => SessionMenuItems(processName));
        more.Margin = new Thickness(2, 0, 0, 0);

        var name = string.IsNullOrEmpty(info.DisplayName) ? processName
            : char.ToUpperInvariant(info.DisplayName[0]) + info.DisplayName[1..];
        var row = UiKit.ListRow(BuildSessionIcon(processName, info.Pid, accent), name,
            SessionAssignmentText(processName), right, more);
        row.MouseRightButtonUp += (_, e) => { e.Handled = true; GlassContextMenuHost.Show(more, SessionMenuItems(processName)); };

        if (session != null)
        {
            ctrl = new SessionRowCtrl(processName, session, peakFill, peakScale, volLabel, slider, muteIcon, active)
            {
                LastVolPct = volPct,
                LastMuted = info.Muted,
            };
            slider.ValueChanged += (_, _) =>
            {
                if (ctrl == null || ctrl.Updating) return;
                int pct = (int)Math.Round(slider.Value);
                ctrl.LastVolPct = pct;
                volLabel.Text = $"{pct}%";
                try { session.SimpleAudioVolume.Volume = Math.Clamp(pct / 100f, 0f, 1f); } catch { }
            };
            _sessionRows.Add(ctrl);
        }
        else
        {
            muteBtn.IsEnabled = false;
            muteBtn.Opacity = 0.5;
        }

        if (!active) row.Opacity = 0.85;
        return row;
    }

    private string SessionAssignmentText(string processName)
    {
        if (_config == null) return "Unassigned";
        var parts = new List<string>();
        foreach (var k in _config.Knobs.OrderBy(k => k.Idx))
        {
            string label = !string.IsNullOrWhiteSpace(k.Label) ? k.Label : $"Knob {k.Idx + 1}";
            if (k.Target?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true)
                parts.Add($"On {label}");
            else if (IsAppGroupMember(k, processName))
                parts.Add($"In {label} group");
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : "Unassigned";
    }

    private List<GlassMenuItem> SessionMenuItems(string processName)
    {
        var items = new List<GlassMenuItem>();
        if (_config == null) return items;

        void SaveAndRefresh()
        {
            _onSave?.Invoke(_config);
            _sessionListDirty = true;
            RefreshSessionList(force: true);
        }

        var assign = new List<GlassMenuItem>();
        var group = new List<GlassMenuItem>();
        for (int i = 0; i < 5; i++)
        {
            int knobIdx = i;
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
            string knobLabel = knob != null && !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : $"Knob {knobIdx + 1}";

            bool isDirect = knob?.Target?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true;
            assign.Add(new GlassMenuItem(knobLabel, MaterialIconKind.Knob, () =>
            {
                var k = _config.Knobs.FirstOrDefault(x => x.Idx == knobIdx);
                if (k == null) return;
                // Clicking the current knob removes the direct assignment
                k.Target = isDirect ? "none" : processName.ToLowerInvariant();
                k.Apps.Clear();
                k.DeviceId = "";
                SaveAndRefresh();
            }, IsChecked: isDirect));

            bool isMember = IsAppGroupMember(knob, processName);
            group.Add(new GlassMenuItem(knobLabel, MaterialIconKind.TuneVertical, () =>
            {
                var k = _config.Knobs.FirstOrDefault(x => x.Idx == knobIdx);
                if (k == null) return;
                ToggleAppGroupMembership(k, processName);
                SaveAndRefresh();
            }, IsChecked: isMember));
        }

        items.Add(new GlassMenuItem("Assign to knob", MaterialIconKind.Knob, null, assign));
        items.Add(new GlassMenuItem("Add to app group", MaterialIconKind.TuneVertical, null, group));
        items.Add(GlassMenuItem.Sep);

        bool isHidden = _config.HiddenTrayApps.Any(h => h.Equals(processName, StringComparison.OrdinalIgnoreCase));
        items.Add(new GlassMenuItem(isHidden ? "Unhide" : "Hide", isHidden ? MaterialIconKind.EyeOutline : MaterialIconKind.EyeOffOutline, () =>
        {
            if (isHidden)
                _config.HiddenTrayApps.RemoveAll(h => h.Equals(processName, StringComparison.OrdinalIgnoreCase));
            else if (!_config.HiddenTrayApps.Contains(processName))
                _config.HiddenTrayApps.Add(processName);
            _onSave?.Invoke(_config);
            RefreshSessionList(force: true);
        }));
        return items;
    }

    private FrameworkElement BuildSessionIcon(string processName, int pid, Color accent)
    {
        var tile = UiKit.IconTile(accent, MaterialIconKind.Application, 32);
        var bmpSrc = GetCachedSessionIcon(processName, pid);
        if (bmpSrc != null)
            tile.Child = new Image { Source = bmpSrc, Width = 18, Height = 18 };
        return tile;
    }

    /// <summary>
    /// Resolves the icon for a session's process, with two-level caching:
    /// pid → exe path (so MainModule — which throws for protected processes —
    /// is only attempted once per process, failures included), then
    /// exe path → frozen BitmapSource (one extraction per exe per app lifetime).
    /// </summary>
    private static BitmapSource? GetCachedSessionIcon(string processName, int pid)
    {
        string pidKey = $"{processName.ToLowerInvariant()}:{pid}";
        if (!s_sessionExePaths.TryGetValue(pidKey, out var exePath))
        {
            // Bound the pid cache — pids churn over long uptimes
            if (s_sessionExePaths.Count > 512)
                s_sessionExePaths.Clear();

            try
            {
                using var proc = Process.GetProcessById(pid);
                exePath = proc.MainModule?.FileName;
            }
            catch
            {
                exePath = null; // protected/exited process — cache the failure, don't retry every tick
            }
            s_sessionExePaths[pidKey] = exePath;
        }

        if (string.IsNullOrEmpty(exePath)) return null;

        if (!s_sessionIcons.TryGetValue(exePath, out var icon))
        {
            try
            {
                var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (sysIcon != null)
                {
                    icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    icon.Freeze();
                    sysIcon.Dispose();
                }
            }
            catch { icon = null; }
            s_sessionIcons[exePath] = icon;
        }
        return icon;
    }

    private void RefreshSessionAccent()
    {
        var accent = ThemeManager.Accent;
        foreach (var r in _sessionRows) r.Slider.AccentColor = accent;
    }
}
