using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Material.Icons;
using Material.Icons.WPF;
using NAudio.CoreAudioApi;
using Wpf.Ui.Appearance;

namespace AmpUp.Controls;

public class TrayMixerPopup : Window
{
    private StackPanel _sessionList = null!;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<SessionRow> _rows = new();
    private MMDevice? _masterDevice;
    private Slider? _masterSlider;
    private TextBlock? _masterVolLabel;
    private Button? _masterMuteBtn;
    private readonly System.Windows.Threading.DispatcherTimer _pollTimer;
    private bool _updatingFromPoll; // prevent slider.ValueChanged feedback loop

    // Context menu callbacks
    private Action? _onOpen;
    private Action? _onExit;
    private AudioMixer? _mixer;
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private Action? _onRefresh;

    // Connection status
    private TextBlock _statusDot = null!;
    private TextBlock _statusText = null!;

    // Quick Assign panel
    private Border _quickAssignPanel = null!;
    private bool _quickAssignVisible;
    private string? _expandedAppName; // which app cell is expanded for knob selection

    // Update indicator
    private Border? _updateBanner;

    private record SessionRow(
        string ProcessName,
        AudioSessionControl Session,
        Slider VolumeSlider,
        TextBlock VolLabel,
        Button MuteBtn,
        Border? PeakBar = null
    );

    public TrayMixerPopup()
    {
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 340;
        SizeToContent = SizeToContent.Height;

        Deactivated += (_, _) => Hide();

        _pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _pollTimer.Tick += PollVolumes;

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        // Outer border — drop shadow + rounded glass
        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(242, 0x0F, 0x0F, 0x0F)), // #F20F0F0F
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.7,
                Direction = 270
            },
            Margin = new Thickness(8, 8, 8, 8) // room for shadow
        };

        var root = new DockPanel();
        outer.Child = root;

        // Status dot/text (hidden, updated via UpdateStatus)
        _statusDot = new TextBlock
        {
            Text = "○",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 7, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        _statusText = new TextBlock
        {
            Text = "Disconnected",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9, VerticalAlignment = VerticalAlignment.Center
        };

        // Device switcher at top (with rounded top corners)
        var deviceSection = BuildDeviceSwitcher();
        var deviceBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Child = deviceSection,
        };
        DockPanel.SetDock(deviceBorder, Dock.Top);
        root.Children.Add(deviceBorder);

        // Divider
        root.Children.Add(MakeDivider());

        // Scrollable session list (master + apps)
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 500,
            Style = BuildScrollViewerStyle()
        };
        DockPanel.SetDock(scroll, Dock.Top);

        _sessionList = new StackPanel { Orientation = Orientation.Vertical };
        scroll.Content = _sessionList;

        var wrapper = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            Padding = new Thickness(0, 4, 0, 4),
            Child = scroll
        };
        DockPanel.SetDock(wrapper, Dock.Top);
        root.Children.Add(wrapper);

        // Quick Assign panel (hidden by default, slides in above footer)
        _quickAssignPanel = BuildQuickAssignPanel();
        _quickAssignPanel.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_quickAssignPanel, Dock.Top);
        root.Children.Add(_quickAssignPanel);

        // Footer — Quick Assign button, Open, Exit
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Top);
        root.Children.Add(footer);

        return outer;
    }

    public void SetCallbacks(Action onOpen, Action onExit, AudioMixer mixer, AppConfig config,
        Action<AppConfig> onSave, Action onRefresh)
    {
        _onOpen = onOpen;
        _onExit = onExit;
        _mixer = mixer;
        _config = config;
        _onSave = onSave;
        _onRefresh = onRefresh;
    }

    public void UpdateStatus(bool connected, string? port)
    {
        if (_statusDot == null || _statusText == null) return;
        if (connected)
        {
            _statusDot.Text = "●";
            _statusDot.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0x77));
            _statusText.Text = "Connected";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        }
        else
        {
            _statusDot.Text = "○";
            _statusDot.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            _statusText.Text = "Disconnected";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        }
    }

    public void ShowUpdateAvailable()
    {
        if (_updateBanner != null)
            _updateBanner.Visibility = Visibility.Visible;
    }

    public void ShowPopup()
    {
        try
        {
            RefreshSessions();
            // Show first (off-screen) so PresentationSource is available for DPI conversion,
            // then position correctly and activate
            Left = -10000;
            Top = -10000;
            Show();
            PositionNearTray();
            Activate();
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.ShowPopup error: {ex.Message}");
        }
    }

    private new void Hide()
    {
        _pollTimer.Stop();
        // Close quick assign panel when popup hides
        if (_quickAssignVisible)
        {
            _quickAssignVisible = false;
            _expandedAppName = null;
            _quickAssignPanel.Visibility = Visibility.Collapsed;
        }
        base.Hide();
    }

    private void RefreshSessions()
    {
        _rows.Clear();
        _sessionList.Children.Clear();

        try
        {
            // Master volume row (always first)
            // Store in field so it lives as long as the popup; disposed in OnClosed
            _masterDevice?.Dispose();
            _masterDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            float masterVol = _masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool masterMuted = _masterDevice.AudioEndpointVolume.Mute;

            _sessionList.Children.Add(BuildMasterRow(_masterDevice, masterVol, masterMuted));

            // Divider after master
            _sessionList.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(10, 2, 10, 2)
            });

            // Per-app sessions
            var sessionMgr = _masterDevice.AudioSessionManager;
            var sessions = sessionMgr.Sessions;

            var hiddenApps = _config?.HiddenTrayApps ?? new();

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    var pid = (int)s.GetProcessID;
                    if (pid == 0) continue; // skip System Sounds

                    var proc = Process.GetProcessById(pid);
                    var name = proc.ProcessName;

                    // Skip hidden apps
                    if (hiddenApps.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var row = BuildSessionRow(name, s);
                    if (row != null)
                        _sessionList.Children.Add(row);
                }
                catch { }
            }

            if (_sessionList.Children.Count <= 2) // only master + divider
            {
                var empty = new TextBlock
                {
                    Text = "No active audio apps",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 8)
                };
                _sessionList.Children.Add(empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.RefreshSessions error: {ex.Message}");
            _sessionList.Children.Add(new TextBlock
            {
                Text = "Audio unavailable",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 8)
            });
        }
    }

    private void PollVolumes(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        _updatingFromPoll = true;
        try
        {
            // Update master slider
            if (_masterDevice != null && _masterSlider != null && _masterVolLabel != null)
            {
                try
                {
                    float vol = _masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                    int pct = (int)Math.Round(vol * 100);
                    _masterSlider.Value = pct;
                    _masterVolLabel.Text = $"{pct}%";
                }
                catch { }
            }

            // Update master mute state
            if (_masterDevice != null && _masterMuteBtn != null)
            {
                try
                {
                    bool muted = _masterDevice.AudioEndpointVolume.Mute;
                    UpdateMuteButton(_masterMuteBtn, muted);
                }
                catch { }
            }

            // Update per-app sliders + peak activity bars + mute state
            foreach (var row in _rows)
            {
                try
                {
                    float vol = row.Session.SimpleAudioVolume.Volume;
                    int pct = (int)Math.Round(vol * 100);
                    row.VolumeSlider.Value = pct;
                    row.VolLabel.Text = $"{pct}%";

                    // Update mute state from actual session
                    bool muted = row.Session.SimpleAudioVolume.Mute;
                    UpdateMuteButton(row.MuteBtn, muted);

                    // Update peak activity bar
                    if (row.PeakBar != null)
                    {
                        float peak = row.Session.AudioMeterInformation.MasterPeakValue;
                        double maxWidth = row.VolumeSlider.ActualWidth > 0 ? row.VolumeSlider.ActualWidth : 140;
                        row.PeakBar.Width = peak * maxWidth;
                    }
                }
                catch { }
            }
        }
        finally
        {
            _updatingFromPoll = false;
        }
    }

    private static Border MakeDivider()
    {
        var d = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
        };
        DockPanel.SetDock(d, Dock.Top);
        return d;
    }

    // ── Quick Assign Panel ────────────────────────────────────────────

    private Border BuildQuickAssignPanel()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8, 10, 10),
            Tag = "quickassign" // marker for refresh
        };

        panel.Child = BuildQuickAssignContent();
        return panel;
    }

    private FrameworkElement BuildQuickAssignContent()
    {
        var accent = GetAccentColor();
        var root = new StackPanel();

        // Panel header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock
        {
            Text = "QUICK ASSIGN",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(header);

        if (_mixer == null || _config == null)
        {
            root.Children.Add(new TextBlock
            {
                Text = "Not available",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return root;
        }

        List<string> apps;
        try { apps = _mixer.GetRunningAudioApps(); }
        catch { apps = new List<string>(); }

        var hiddenApps = _config.HiddenTrayApps ?? new();
        var visibleApps = apps.Where(a => !hiddenApps.Any(h => h.Equals(a, StringComparison.OrdinalIgnoreCase))).ToList();
        var hiddenRunning = apps.Where(a => hiddenApps.Any(h => h.Equals(a, StringComparison.OrdinalIgnoreCase))).ToList();

        if (visibleApps.Count == 0 && hiddenRunning.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No audio apps running",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 4)
            });
            return root;
        }

        // App grid — 2 columns
        if (visibleApps.Count > 0)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // gap
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int totalRows = (int)Math.Ceiling(visibleApps.Count / 2.0);
            for (int r = 0; r < totalRows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < visibleApps.Count; i++)
            {
                var appName = visibleApps[i];
                int col = (i % 2 == 0) ? 0 : 2;
                int row = i / 2;

                var cell = BuildAppAssignCell(appName, accent);
                Grid.SetColumn(cell, col);
                Grid.SetRow(cell, row);
                grid.Children.Add(cell);
            }
            root.Children.Add(grid);
        }

        // Hidden apps section
        if (hiddenRunning.Count > 0)
        {
            var hiddenToggleRow = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 4, 4, 4),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var hiddenSection = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

            var chevron = new TextBlock
            {
                Text = "›",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            var hiddenDock = new DockPanel();
            DockPanel.SetDock(chevron, Dock.Right);
            hiddenDock.Children.Add(chevron);
            hiddenDock.Children.Add(new TextBlock
            {
                Text = $"Hidden apps ({hiddenRunning.Count})",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
            });
            hiddenToggleRow.Child = hiddenDock;
            hiddenToggleRow.MouseEnter += (_, _) => hiddenToggleRow.Background = new SolidColorBrush(Color.FromArgb(15, accent.R, accent.G, accent.B));
            hiddenToggleRow.MouseLeave += (_, _) => hiddenToggleRow.Background = Brushes.Transparent;
            hiddenToggleRow.MouseLeftButtonDown += (_, _) =>
            {
                bool show = hiddenSection.Visibility != Visibility.Visible;
                hiddenSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "⌄" : "›";
                Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            };
            root.Children.Add(hiddenToggleRow);

            // Hidden apps in a simple list with "Show" buttons
            foreach (var appName in hiddenRunning)
            {
                var appCapture = appName;
                var hiddenRow = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(6, 5, 6, 5),
                    Margin = new Thickness(0, 1, 0, 1),
                    CornerRadius = new CornerRadius(4),
                    Cursor = Cursors.Hand,
                };
                var dock = new DockPanel();
                var showBtn = new TextBlock
                {
                    Text = "Show",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                    FontSize = 9, VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                };
                showBtn.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    var hidden = _config.HiddenTrayApps;
                    hidden.RemoveAll(h => h.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
                    _onSave?.Invoke(_config);
                    RefreshSessions();
                    RefreshQuickAssignPanel();
                };
                DockPanel.SetDock(showBtn, Dock.Right);
                dock.Children.Add(showBtn);
                dock.Children.Add(new TextBlock
                {
                    Text = TitleCase(appCapture),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    FontSize = 10, FontStyle = FontStyles.Italic,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                hiddenRow.Child = dock;
                hiddenRow.MouseEnter += (_, _) => hiddenRow.Background = new SolidColorBrush(Color.FromArgb(15, accent.R, accent.G, accent.B));
                hiddenRow.MouseLeave += (_, _) => hiddenRow.Background = Brushes.Transparent;
                hiddenSection.Children.Add(hiddenRow);
            }
            root.Children.Add(hiddenSection);
        }

        return root;
    }

    private Border BuildAppAssignCell(string appName, Color accent)
    {
        var appCapture = appName;
        bool isExpanded = _expandedAppName?.Equals(appName, StringComparison.OrdinalIgnoreCase) == true;

        // Find current knob assignment
        var assignedKnob = _config!.Knobs.FirstOrDefault(kn =>
            kn.Target?.Equals(appCapture, StringComparison.OrdinalIgnoreCase) == true);
        bool isAssigned = assignedKnob != null;

        var appColor = GetAppColor(appName);

        // Card container
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Tag = appCapture,
        };

        if (isExpanded)
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B));
        }
        else if (isAssigned)
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, appColor.R, appColor.G, appColor.B));
        }
        else
        {
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }

        var cellContent = new StackPanel { Margin = new Thickness(7, 6, 7, 6) };

        // Top row: icon + name
        var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        // App icon
        UIElement icon = BuildLetterIcon(
            appName.Length > 0 ? char.ToUpperInvariant(appName[0]).ToString() : "?",
            appColor, size: 24);
        DockPanel.SetDock(icon, Dock.Left);
        topRow.Children.Add(icon);

        var nameText = new TextBlock
        {
            Text = TitleCase(appName),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        topRow.Children.Add(nameText);
        cellContent.Children.Add(topRow);

        // Assignment badge or "Unassigned" label
        var badgeContainer = new StackPanel { Orientation = Orientation.Horizontal };
        if (isAssigned)
        {
            var knobLabel = _config.Knobs.FirstOrDefault(kn => kn.Idx == assignedKnob!.Idx);
            string label = !string.IsNullOrWhiteSpace(knobLabel?.Label) ? knobLabel!.Label : $"K{assignedKnob!.Idx + 1}";
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xDD, accent.R, accent.G, accent.B)),
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                }
            };
            // Glow effect on assigned badge
            badge.Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 6,
                ShadowDepth = 0,
                Opacity = 0.3,
            };
            badgeContainer.Children.Add(badge);
        }
        else
        {
            badgeContainer.Children.Add(new TextBlock
            {
                Text = "Unassigned",
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontSize = 8.5,
                FontStyle = FontStyles.Italic,
            });
        }
        cellContent.Children.Add(badgeContainer);

        // Knob picker row (shown when expanded)
        var knobPicker = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed,
        };

        for (int k = 0; k < 5; k++)
        {
            int knobIdx = k;
            var knobCfg = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
            bool isCurrent = knobCfg?.Target?.Equals(appCapture, StringComparison.OrdinalIgnoreCase) == true;
            string knobLbl = !string.IsNullOrWhiteSpace(knobCfg?.Label) ? knobCfg!.Label : $"K{knobIdx + 1}";
            // Truncate label to 3 chars for pill
            string pill = knobLbl.Length > 3 ? knobLbl[..3] : knobLbl;

            var pillBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = isCurrent ? $"Unassign {knobLbl}" : $"Assign to {knobLbl}",
            };

            if (isCurrent)
            {
                pillBorder.Background = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B));
                pillBorder.BorderBrush = new SolidColorBrush(accent);
                pillBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                pillBorder.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                pillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
                pillBorder.BorderThickness = new Thickness(1);
            }

            pillBorder.Child = new TextBlock
            {
                Text = pill,
                Foreground = isCurrent
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 8,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            pillBorder.MouseEnter += (_, _) =>
            {
                if (!isCurrent)
                {
                    pillBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
                    pillBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));
                    if (pillBorder.Child is TextBlock pt)
                        pt.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B));
                }
            };
            pillBorder.MouseLeave += (_, _) =>
            {
                if (!isCurrent)
                {
                    pillBorder.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                    pillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
                    if (pillBorder.Child is TextBlock pt)
                        pt.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
            };

            pillBorder.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                AssignAppToKnob(appCapture, knobIdx, isCurrent);
            };

            knobPicker.Children.Add(pillBorder);
        }
        cellContent.Children.Add(knobPicker);
        card.Child = cellContent;

        // Card hover effects
        card.MouseEnter += (_, _) =>
        {
            if (!isExpanded)
                card.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        };
        card.MouseLeave += (_, _) =>
        {
            if (!isExpanded)
                card.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        };

        // Click card to toggle expanded state
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            _expandedAppName = isExpanded ? null : appCapture;
            RefreshQuickAssignPanel();
            Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
                System.Windows.Threading.DispatcherPriority.Loaded);
        };

        return card;
    }

    private void AssignAppToKnob(string appName, int knobIdx, bool isCurrent)
    {
        if (_config == null) return;
        var cfg = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
        if (cfg == null) return;

        if (isCurrent)
        {
            // Unassign — clear the target
            cfg.Target = "none";
            cfg.Label = $"Knob {knobIdx + 1}";
        }
        else
        {
            cfg.Target = appName;
            cfg.Label = appName;
        }

        _onSave?.Invoke(_config);
        _onRefresh?.Invoke();

        // Collapse after assignment
        _expandedAppName = null;
        RefreshQuickAssignPanel();
        Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshQuickAssignPanel()
    {
        _quickAssignPanel.Child = BuildQuickAssignContent();
    }

    private void ToggleQuickAssignPanel()
    {
        _quickAssignVisible = !_quickAssignVisible;
        _expandedAppName = null;
        if (_quickAssignVisible)
        {
            RefreshQuickAssignPanel();
            _quickAssignPanel.Visibility = Visibility.Visible;
        }
        else
        {
            _quickAssignPanel.Visibility = Visibility.Collapsed;
        }
        Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── Footer ───────────────────────────────────────────────────────

    private UIElement BuildFooter()
    {
        var accent = GetAccentColor();
        var footer = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        };

        // Divider at top of footer
        footer.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        });

        var items = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };

        // Update banner (hidden by default)
        _updateBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xB8, 0x00)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };
        _updateBanner.Child = new TextBlock
        {
            Text = "Update available — click to download",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00)),
            FontSize = 9.5, FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _updateBanner.MouseLeftButtonDown += (_, _) =>
        {
            Hide();
            try { Process.Start(new ProcessStartInfo("https://github.com/audioslayer/ampup/releases/latest") { UseShellExecute = true }); } catch { }
        };
        _updateBanner.MouseEnter += (_, _) => _updateBanner.Background = new SolidColorBrush(Color.FromArgb(50, 0xFF, 0xB8, 0x00));
        _updateBanner.MouseLeave += (_, _) => _updateBanner.Background = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xB8, 0x00));
        items.Children.Add(_updateBanner);

        // Divider before bottom bar
        items.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Margin = new Thickness(0, 2, 0, 4),
        });

        // Bottom bar: icon + AMP UP + status dot | ⚡ Quick Assign | Open Amp Up | Exit
        var bottomRow = new DockPanel { Margin = new Thickness(2, 2, 2, 2) };

        // Exit on far right
        var exitBtn = BuildFooterItem("Exit",
            new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)), false,
            () => { Hide(); _onExit?.Invoke(); });
        DockPanel.SetDock(exitBtn, Dock.Right);
        bottomRow.Children.Add(exitBtn);

        // Open Amp Up next to exit
        var openBtn = BuildFooterItem("Open Amp Up",
            new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)), false,
            () => { Hide(); _onOpen?.Invoke(); });
        DockPanel.SetDock(openBtn, Dock.Right);
        bottomRow.Children.Add(openBtn);

        // ⚡ Quick Assign button
        var qaBtn = BuildQuickAssignButton(accent);
        DockPanel.SetDock(qaBtn, Dock.Right);
        bottomRow.Children.Add(qaBtn);

        // Brand + status on the left
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon/ampup-16.png", UriKind.Absolute);
            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            brandRow.Children.Add(new System.Windows.Controls.Image
            {
                Source = bitmapImage, Width = 12, Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
        }
        catch { }
        brandRow.Children.Add(new TextBlock
        {
            Text = "AMP UP",
            Foreground = new SolidColorBrush(accent),
            FontSize = 9, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        brandRow.Children.Add(_statusDot);
        brandRow.Children.Add(_statusText);
        bottomRow.Children.Add(brandRow);

        items.Children.Add(bottomRow);

        footer.Children.Add(items);
        return footer;
    }

    private Border BuildQuickAssignButton(Color accent)
    {
        var btn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 2, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var dock = new DockPanel();
        dock.Children.Add(new TextBlock
        {
            Text = "⚡ Quick Assign",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        btn.Child = dock;

        btn.MouseEnter += (_, _) =>
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(0x35, accent.R, accent.G, accent.B));
            btn.BorderBrush = new SolidColorBrush(accent);
        };
        btn.MouseLeave += (_, _) =>
        {
            bool active = _quickAssignVisible;
            btn.Background = new SolidColorBrush(Color.FromArgb(active ? 0x35 : 0x20, accent.R, accent.G, accent.B));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(active ? 0xFF : 0x50, accent.R, accent.G, accent.B));
        };

        btn.MouseLeftButtonDown += (_, _) => ToggleQuickAssignPanel();
        return btn;
    }

    private Border BuildFooterItem(string text, Brush foreground, bool bold, Action? onClick)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand,
        };

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 10,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Child = dock;

        var accent = GetAccentColor();
        row.MouseEnter += (_, _) =>
            row.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
        row.MouseLeave += (_, _) =>
            row.Background = Brushes.Transparent;

        if (onClick != null)
            row.MouseLeftButtonDown += (_, _) => onClick();

        return row;
    }

    // ── Session Rows with Right-Click Context Menu ────────────────────

    private UIElement BuildDeviceSwitcher()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(6, 6, 6, 4)
        };

        // Output device row
        panel.Children.Add(BuildDeviceRow("OUTPUT", DataFlow.Render));

        // Input device row
        panel.Children.Add(BuildDeviceRow("INPUT", DataFlow.Capture));

        return panel;
    }

    private UIElement BuildDeviceRow(string label, DataFlow flow)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 5, 10, 5),
        };

        var dock = new DockPanel { LastChildFill = true };

        // Label (OUTPUT / INPUT)
        var typeLabel = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Width = 48,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(typeLabel, Dock.Left);
        dock.Children.Add(typeLabel);

        // Build device list + find current default
        var devices = new List<MMDevice>();
        string currentId = "";
        try
        {
            var role = flow == DataFlow.Capture ? Role.Communications : Role.Multimedia;
            var enumerated = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            foreach (var d in enumerated) devices.Add(d);
            using var def = _enumerator.GetDefaultAudioEndpoint(flow, role);
            currentId = def.ID;
        }
        catch { }

        // ComboBox styled to match the dark theme
        var accent = GetAccentColor();
        var combo = new ComboBox
        {
            FontSize = 11,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = Application.Current.TryFindResource("HoverComboBox") as Style,
        };

        int selectedIdx = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            var name = d.FriendlyName.Length > 36 ? d.FriendlyName[..34] + "…" : d.FriendlyName;
            combo.Items.Add(name);
            if (d.ID == currentId) selectedIdx = i;
        }

        if (combo.Items.Count == 0)
        {
            combo.Items.Add("No devices found");
            combo.IsEnabled = false;
        }

        combo.SelectedIndex = selectedIdx;

        // Switch device on selection change
        combo.SelectionChanged += (_, _) =>
        {
            int idx = combo.SelectedIndex;
            if (idx < 0 || idx >= devices.Count) return;
            try
            {
                ButtonHandler.SetDefaultAudioDevice(devices[idx].ID);
            }
            catch (Exception ex)
            {
                Logger.Log($"TrayMixerPopup device switch error: {ex.Message}");
            }
        };

        dock.Children.Add(combo);
        row.Child = dock;
        return row;
    }

    private UIElement BuildMasterRow(MMDevice device, float vol, bool muted)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(6, 4, 6, 2),
            CornerRadius = new CornerRadius(6)
        };

        var panel = new DockPanel { LastChildFill = true };

        // Icon letter
        var icon = BuildLetterIcon("M", Color.FromRgb(0x00, 0xE6, 0x76));
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);

        // Mute button
        var muteBtn = BuildMuteButton(muted);
        DockPanel.SetDock(muteBtn, Dock.Right);

        // Vol% label
        var volLabel = new TextBlock
        {
            Text = $"{(int)Math.Round(vol * 100)}%",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            Width = 34,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0)
        };
        DockPanel.SetDock(volLabel, Dock.Right);

        panel.Children.Add(muteBtn);
        panel.Children.Add(volLabel);

        // Center: label + slider
        var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
        var label = new TextBlock
        {
            Text = "MASTER",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };

        var slider = BuildVolumeSlider(vol * 100);
        _masterSlider = slider;
        _masterVolLabel = volLabel;
        _masterMuteBtn = muteBtn;
        slider.ValueChanged += (_, e) =>
        {
            if (_updatingFromPoll) return;
            try
            {
                float newVol = (float)(e.NewValue / 100.0);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = newVol;
                volLabel.Text = $"{(int)Math.Round(e.NewValue)}%";
            }
            catch { }
        };

        muteBtn.Click += (_, _) =>
        {
            try
            {
                device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
                bool m = device.AudioEndpointVolume.Mute;
                UpdateMuteButton(muteBtn, m);
            }
            catch { }
        };

        center.Children.Add(label);
        center.Children.Add(slider);
        panel.Children.Add(center);
        row.Child = panel;
        return row;
    }

    private UIElement? BuildSessionRow(string processName, AudioSessionControl session)
    {
        try
        {
            float vol = session.SimpleAudioVolume.Volume;
            bool muted = session.SimpleAudioVolume.Mute;

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(6)
            };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            // Right-click context menu
            row.MouseRightButtonDown += (_, e) =>
            {
                e.Handled = true;
                ShowSessionContextMenu(processName, row);
            };

            var panel = new DockPanel { LastChildFill = true };

            // Try to get app icon from process executable, fall back to letter icon
            var iconColor = GetAppColor(processName);
            UIElement icon;
            try
            {
                var pid = (int)session.GetProcessID;
                var proc = Process.GetProcessById(pid);
                var exePath = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (sysIcon != null)
                    {
                        var bmpSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            sysIcon.Handle, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        bmpSource.Freeze();
                        icon = new Border
                        {
                            Width = 28, Height = 28,
                            CornerRadius = new CornerRadius(6),
                            Background = new SolidColorBrush(Color.FromArgb(30, iconColor.R, iconColor.G, iconColor.B)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new System.Windows.Controls.Image
                            {
                                Source = bmpSource, Width = 18, Height = 18,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                            }
                        };
                        sysIcon.Dispose();
                    }
                    else
                    {
                        icon = BuildLetterIcon(processName.Length > 0 ? char.ToUpperInvariant(processName[0]).ToString() : "?", iconColor);
                    }
                }
                else
                {
                    icon = BuildLetterIcon(processName.Length > 0 ? char.ToUpperInvariant(processName[0]).ToString() : "?", iconColor);
                }
            }
            catch
            {
                var firstChar = processName.Length > 0 ? char.ToUpperInvariant(processName[0]).ToString() : "?";
                icon = BuildLetterIcon(firstChar, iconColor);
            }
            DockPanel.SetDock(icon, Dock.Left);
            panel.Children.Add(icon);

            // Mute button
            var muteBtn = BuildMuteButton(muted);
            DockPanel.SetDock(muteBtn, Dock.Right);

            // Vol% label
            var volLabel = new TextBlock
            {
                Text = $"{(int)Math.Round(vol * 100)}%",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 11,
                Width = 34,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            DockPanel.SetDock(volLabel, Dock.Right);

            panel.Children.Add(muteBtn);
            panel.Children.Add(volLabel);

            // Center: name + slider
            var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
            var nameLabel = new TextBlock
            {
                Text = TitleCase(processName),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };

            var slider = BuildVolumeSlider(vol * 100);

            // Audio activity bar — thin bar under slider showing peak level
            var peakBar = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                Margin = new Thickness(0, 2, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, iconColor.R, iconColor.G, iconColor.B)),
            };

            _rows.Add(new SessionRow(processName, session, slider, volLabel, muteBtn, peakBar));
            slider.ValueChanged += (_, e) =>
            {
                if (_updatingFromPoll) return;
                try
                {
                    session.SimpleAudioVolume.Volume = (float)(e.NewValue / 100.0);
                    volLabel.Text = $"{(int)Math.Round(e.NewValue)}%";
                }
                catch { }
            };

            muteBtn.Click += (_, _) =>
            {
                try
                {
                    session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                    UpdateMuteButton(muteBtn, session.SimpleAudioVolume.Mute);
                }
                catch { }
            };

            center.Children.Add(nameLabel);
            center.Children.Add(slider);
            center.Children.Add(peakBar);
            panel.Children.Add(center);
            row.Child = panel;
            return row;
        }
        catch
        {
            return null;
        }
    }

    private void ShowSessionContextMenu(string processName, Border rowBorder)
    {
        if (_config == null) return;
        var accent = GetAccentColor();

        var menuWin = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
        };

        var isHidden = _config.HiddenTrayApps.Any(h => h.Equals(processName, StringComparison.OrdinalIgnoreCase));
        var displayName = TitleCase(processName);

        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Opacity = 0.7 },
            Padding = new Thickness(4, 4, 4, 4),
            MinWidth = 200,
        };

        var menuStack = new StackPanel();
        outer.Child = menuStack;
        menuWin.Content = outer;

        // Helper: build a menu item
        UIElement MakeItem(string text, Brush fg, Action action, bool isSub = false)
        {
            var item = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(isSub ? 18 : 10, 6, 10, 6),
                Cursor = Cursors.Hand,
            };
            item.Child = new TextBlock
            {
                Text = text,
                Foreground = fg,
                FontSize = 10.5,
                FontFamily = new FontFamily("Segoe UI"),
            };
            item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B));
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonDown += (_, _) =>
            {
                menuWin.Close();
                action();
            };
            return item;
        }

        UIElement MakeSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(8, 2, 8, 2),
            };
        }

        // "Assign to Knob" header (non-clickable label)
        menuStack.Children.Add(new Border
        {
            Padding = new Thickness(10, 5, 10, 3),
            Child = new TextBlock
            {
                Text = "ASSIGN TO KNOB",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
            }
        });

        // Knob items
        for (int k = 0; k < 5; k++)
        {
            int knobIdx = k;
            var knobCfg = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
            bool isCurrent = knobCfg?.Target?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true;
            string kLabel = !string.IsNullOrWhiteSpace(knobCfg?.Label) ? knobCfg!.Label : $"Knob {knobIdx + 1}";
            string prefix = isCurrent ? "✓  " : "   ";
            var fg = isCurrent
                ? new SolidColorBrush(accent)
                : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

            menuStack.Children.Add(MakeItem($"{prefix}{kLabel}", fg, () =>
            {
                AssignAppToKnob(processName, knobIdx, isCurrent);
                RefreshSessions();
            }, isSub: true));
        }

        menuStack.Children.Add(MakeSeparator());

        // Move to device submenu header
        var deviceMenuStack = new StackPanel();
        var deviceSection = new Border
        {
            Padding = new Thickness(10, 5, 10, 3),
            Child = new TextBlock
            {
                Text = "MOVE TO DEVICE",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
            }
        };
        menuStack.Children.Add(deviceSection);

        try
        {
            var renderDevices = new List<MMDevice>();
            var enumerated = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in enumerated) renderDevices.Add(d);

            foreach (var dev in renderDevices)
            {
                var devCapture = dev;
                var devName = dev.FriendlyName.Length > 30 ? dev.FriendlyName[..28] + "…" : dev.FriendlyName;
                menuStack.Children.Add(MakeItem($"   {devName}",
                    new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    () =>
                    {
                        try { ButtonHandler.SetDefaultAudioDevice(devCapture.ID); }
                        catch { }
                    }, isSub: true));
            }
            if (renderDevices.Count == 0)
            {
                menuStack.Children.Add(new Border
                {
                    Padding = new Thickness(18, 4, 10, 4),
                    Child = new TextBlock
                    {
                        Text = "No devices found",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                        FontSize = 10,
                    }
                });
            }
        }
        catch { }

        menuStack.Children.Add(MakeSeparator());

        // Hide/Show toggle
        if (isHidden)
        {
            menuStack.Children.Add(MakeItem($"Show {displayName} in mixer",
                new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                () =>
                {
                    _config.HiddenTrayApps.RemoveAll(h => h.Equals(processName, StringComparison.OrdinalIgnoreCase));
                    _onSave?.Invoke(_config);
                    RefreshSessions();
                    if (_quickAssignVisible) RefreshQuickAssignPanel();
                }));
        }
        else
        {
            menuStack.Children.Add(MakeItem($"Hide {displayName} from mixer",
                new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                () =>
                {
                    if (!_config.HiddenTrayApps.Any(h => h.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                        _config.HiddenTrayApps.Add(processName);
                    _onSave?.Invoke(_config);
                    RefreshSessions();
                    if (_quickAssignVisible) RefreshQuickAssignPanel();
                }));
        }

        menuStack.Children.Add(MakeSeparator());

        // Open app
        menuStack.Children.Add(MakeItem($"Open {displayName}",
            new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            () =>
            {
                try
                {
                    var proc = Process.GetProcessesByName(processName).FirstOrDefault();
                    if (proc != null)
                    {
                        NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                    }
                }
                catch { }
            }));

        menuWin.Deactivated += (_, _) => menuWin.Close();

        // Position near cursor
        var cursorPos = System.Windows.Forms.Cursor.Position;
        menuWin.Left = cursorPos.X + 4;
        menuWin.Top = cursorPos.Y - 20;
        menuWin.Show();

        // Clamp to screen
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        menuWin.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double mw = menuWin.DesiredSize.Width > 0 ? menuWin.DesiredSize.Width : 200;
        double mh = menuWin.DesiredSize.Height > 0 ? menuWin.DesiredSize.Height : 300;
        if (menuWin.Left + mw > screen.WorkingArea.Right)
            menuWin.Left = screen.WorkingArea.Right - mw - 4;
        if (menuWin.Top + mh > screen.WorkingArea.Bottom)
            menuWin.Top = screen.WorkingArea.Bottom - mh - 4;

        menuWin.Activate();
    }

    // ── Shared helpers ────────────────────────────────────────────────

    private static Border BuildLetterIcon(string letter, Color bg, int size = 28)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4.0),
            Background = new SolidColorBrush(Color.FromArgb(60, bg.R, bg.G, bg.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, bg.R, bg.G, bg.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0),
            Child = new TextBlock
            {
                Text = letter,
                Foreground = new SolidColorBrush(bg),
                FontSize = size * 0.43,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Button BuildMuteButton(bool muted)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            ToolTip = muted ? "Unmute" : "Mute"
        };
        UpdateMuteButton(btn, muted);
        return btn;
    }

    private static void UpdateMuteButton(Button btn, bool muted)
    {
        btn.Content = new MaterialIcon
        {
            Kind = muted ? MaterialIconKind.VolumeOff : MaterialIconKind.VolumeHigh,
            Width = 16, Height = 16,
            Foreground = new SolidColorBrush(muted
                ? Color.FromRgb(0xFF, 0x44, 0x44)
                : Color.FromRgb(0x9A, 0x9A, 0x9A)),
        };
        btn.ToolTip = muted ? "Unmute" : "Mute";
    }

    private Slider BuildVolumeSlider(double value)
    {
        var accentColor = GetAccentColor();

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(value, 0, 100),
            Height = 16,
            Margin = new Thickness(0, 3, 0, 0),
            Style = BuildSliderStyle(accentColor)
        };
        return slider;
    }

    private static Color GetAccentColor()
    {
        try
        {
            var accent = ApplicationAccentColorManager.SystemAccent;
            return Color.FromRgb(accent.R, accent.G, accent.B);
        }
        catch
        {
            return Color.FromRgb(0x00, 0xE6, 0x76); // fallback green
        }
    }

    private static Style BuildSliderStyle(Color accent)
    {
        // Use XAML string to build the slider template — FrameworkElementFactory
        // can't set Track.Thumb/DecreaseRepeatButton/IncreaseRepeatButton as properties.
        var accentHex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        var xaml = $@"
<ControlTemplate TargetType='Slider'
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Grid>
        <Border Height='4' CornerRadius='2' Background='#363636' VerticalAlignment='Center' />
        <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton>
                <RepeatButton Opacity='0' IsHitTestVisible='False' />
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton Opacity='0' IsHitTestVisible='False' />
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb Width='12' Height='12' Cursor='Hand'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Border Background='White' CornerRadius='6' Width='12' Height='12' />
                        </ControlTemplate>
                    </Thumb.Template>
                </Thumb>
            </Track.Thumb>
        </Track>
    </Grid>
</ControlTemplate>";

        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        var style = new Style(typeof(Slider));
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    private static Style BuildScrollViewerStyle()
    {
        // Return null to use default; custom scroll styling is complex in code-behind
        // The slim scrollbar from Theme.xaml will apply if merged in App.xaml
        return new Style(typeof(ScrollViewer));
    }

    private void PositionNearTray()
    {
        // Find which monitor the cursor is on (taskbar/tray lives there)
        var cursorPos = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        var wa = screen.WorkingArea; // physical pixel coords

        // Convert pixel coords to WPF DIPs (required for mixed-DPI multi-monitor setups)
        // PresentationSource may be null before first Show(), so fall back to primary DPI
        double dpiScale;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            dpiScale = source.CompositionTarget.TransformFromDevice.M11;
        else
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            dpiScale = 96.0 / g.DpiX;
        }
        double waLeft = wa.Left * dpiScale;
        double waTop = wa.Top * dpiScale;
        double waRight = wa.Right * dpiScale;
        double waBottom = wa.Bottom * dpiScale;

        // Measure popup height
        Measure(new Size(Width, double.PositiveInfinity));
        double height = DesiredSize.Height > 0 ? DesiredSize.Height : 400;

        // Detect taskbar position by comparing work area to full screen bounds
        var bounds = screen.Bounds;
        double bLeft = bounds.Left * dpiScale;
        double bTop = bounds.Top * dpiScale;
        double bRight = bounds.Right * dpiScale;
        double bBottom = bounds.Bottom * dpiScale;

        double x, y;
        if (waRight < bRight - 10)
        {
            // Taskbar on the right — position flush against the left edge of the taskbar
            x = waRight - Width - 12;
            y = waBottom - height - 12;
        }
        else if (waLeft > bLeft + 10)
        {
            // Taskbar on the left — position at left edge of work area
            x = waLeft + 12;
            y = waBottom - height - 12;
        }
        else if (waTop > bTop + 10)
        {
            // Taskbar on top — position at top of work area
            x = waRight - Width - 12;
            y = waTop + 12;
        }
        else
        {
            // Taskbar on bottom (default) — bottom-right corner
            x = waRight - Width - 12;
            y = waBottom - height - 12;
        }

        // Clamp to work area
        x = Math.Max(waLeft + 4, Math.Min(x, waRight - Width - 4));
        y = Math.Max(waTop + 4, Math.Min(y, waBottom - height - 4));

        Left = x;
        Top = y;
    }

    private void RepositionOnScreen()
    {
        // Re-measure and reposition after content changes
        UpdateLayout();
        PositionNearTray();
    }

    private static Color GetAppColor(string processName)
    {
        var lower = processName.ToLowerInvariant();
        return lower switch
        {
            var n when n.Contains("spotify") => Color.FromRgb(0x1D, 0xB9, 0x54),
            var n when n.Contains("discord") => Color.FromRgb(0x58, 0x65, 0xF2),
            var n when n.Contains("chrome") => Color.FromRgb(0xFF, 0xA0, 0x00),
            var n when n.Contains("firefox") => Color.FromRgb(0xFF, 0x67, 0x11),
            var n when n.Contains("vlc") => Color.FromRgb(0xFF, 0x87, 0x00),
            var n when n.Contains("steam") => Color.FromRgb(0x17, 0x1A, 0x21),
            var n when n.Contains("foobar") => Color.FromRgb(0x00, 0x9A, 0xFF),
            var n when n.Contains("msedge") || n.Contains("edge") => Color.FromRgb(0x00, 0x78, 0xD4),
            var n when n.Contains("teams") => Color.FromRgb(0x46, 0x4E, 0xB8),
            var n when n.Contains("zoom") => Color.FromRgb(0x22, 0x8B, 0xFF),
            _ => Color.FromRgb(0x00, 0xE6, 0x76) // default accent green
        };
    }

    private static string TitleCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _masterDevice?.Dispose(); } catch { }
        try { _enumerator.Dispose(); } catch { }
    }
}
