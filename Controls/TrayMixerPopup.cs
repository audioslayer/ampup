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

    // Search / filter bar
    private TextBox? _searchBox;
    private Border? _searchClearBtn;
    private string _searchText = "";

    private record SessionRow(
        string ProcessName,
        AudioSessionControl Session,
        Slider VolumeSlider,
        TextBlock VolLabel,
        Button MuteBtn,
        Border? PeakBar = null,
        Border? RowBorder = null,
        bool IsSystemSounds = false
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
        var accent = GetAccentColor();
        // Outer border — clean 1px accent-tinted border, no heavy drop shadow
        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(242, 0x0F, 0x0F, 0x0F)), // #F20F0F0F
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 2,
                Opacity = 0.5,
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

        // Header bar — AmpUp brand on left, connection status on right
        var headerBar = BuildHeaderBar();
        DockPanel.SetDock(headerBar, Dock.Top);
        root.Children.Add(headerBar);

        // Device switcher below header
        var deviceSection = BuildDeviceSwitcher();
        var deviceBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            Child = deviceSection,
        };
        DockPanel.SetDock(deviceBorder, Dock.Top);
        root.Children.Add(deviceBorder);

        // Divider
        root.Children.Add(MakeDivider());

        // Update banner (shown when update available)
        _updateBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xB8, 0x00)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(6, 4, 6, 4),
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
        DockPanel.SetDock(_updateBanner, Dock.Top);
        root.Children.Add(_updateBanner);

        // Quick Assign panel (hidden by default, right below header)
        _quickAssignPanel = BuildQuickAssignPanel();
        _quickAssignPanel.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_quickAssignPanel, Dock.Top);
        root.Children.Add(_quickAssignPanel);

        // Search / filter bar
        var searchBar = BuildSearchBar();
        DockPanel.SetDock(searchBar, Dock.Top);
        root.Children.Add(searchBar);

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
            // Clear search filter on each open so the full list is visible
            if (_searchBox != null && !string.IsNullOrEmpty(_searchBox.Text))
                _searchBox.Text = "";
            _searchText = "";

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

            // Subtle separator after master
            _sessionList.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Margin = new Thickness(6, 1, 6, 1)
            });

            // Per-app sessions
            var sessionMgr = _masterDevice.AudioSessionManager;
            var sessions = sessionMgr.Sessions;

            var hiddenApps = _config?.HiddenTrayApps ?? new();
            var pinnedApps = _config?.PinnedTrayApps ?? new();

            // Collect all app sessions (deduplicated by display name)
            var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AudioSessionControl? systemSoundsSession = null; // pid==0, show at bottom

            // Gather as (displayName, session) pairs for sorting
            var appEntries = new List<(string DisplayName, AudioSessionControl Session)>();

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    var pid = (int)s.GetProcessID;
                    if (pid == 0)
                    {
                        // System Sounds — keep first occurrence
                        systemSoundsSession ??= s;
                        continue;
                    }

                    var proc = Process.GetProcessById(pid);
                    var name = proc.ProcessName;

                    // Prefer WASAPI DisplayName for UWP/packaged apps where audio runs
                    // in a helper process (e.g. AMPLibraryAgent → "Apple Music")
                    string displayName = name;
                    try
                    {
                        var dn = s.DisplayName;
                        if (!string.IsNullOrWhiteSpace(dn) &&
                            !dn.Equals(name, StringComparison.OrdinalIgnoreCase))
                            displayName = dn;
                    }
                    catch { }

                    // Skip hidden apps (check both process name and display name)
                    if (hiddenApps.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                            h.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Deduplicate by display name (not process name) — catches helper processes
                    if (!seenApps.Add(displayName))
                        continue;

                    appEntries.Add((displayName, s));
                }
                catch { }
            }

            // Sort: pinned apps first (in pin order), then rest alphabetically
            appEntries.Sort((a, b) =>
            {
                int pinA = pinnedApps.FindIndex(p => p.Equals(a.DisplayName, StringComparison.OrdinalIgnoreCase));
                int pinB = pinnedApps.FindIndex(p => p.Equals(b.DisplayName, StringComparison.OrdinalIgnoreCase));
                bool isPinnedA = pinA >= 0;
                bool isPinnedB = pinB >= 0;
                if (isPinnedA && isPinnedB) return pinA.CompareTo(pinB);
                if (isPinnedA) return -1;
                if (isPinnedB) return 1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            bool firstApp = true;
            foreach (var (displayName, s) in appEntries)
            {
                bool isPinned = pinnedApps.Any(p => p.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                var row = BuildSessionRow(displayName, s, isPinned);
                if (row != null)
                {
                    if (!firstApp)
                    {
                        _sessionList.Children.Add(new Border
                        {
                            Height = 1,
                            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                            Margin = new Thickness(6, 0, 6, 0)
                        });
                    }
                    firstApp = false;
                    _sessionList.Children.Add(row);
                }
            }

            // System Sounds row at the bottom
            if (systemSoundsSession != null)
            {
                if (!firstApp)
                {
                    _sessionList.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        Margin = new Thickness(6, 2, 6, 2)
                    });
                }
                var sysRow = BuildSystemSoundsRow(systemSoundsSession);
                if (sysRow != null)
                    _sessionList.Children.Add(sysRow);
            }

            if (_sessionList.Children.Count <= 2) // only master + divider
            {
                var emptyPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 14, 0, 10),
                };
                emptyPanel.Children.Add(new MaterialIcon
                {
                    Kind = MaterialIconKind.VolumeOff,
                    Width = 20, Height = 20,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 5),
                });
                emptyPanel.Children.Add(new TextBlock
                {
                    Text = "No active audio apps",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                _sessionList.Children.Add(emptyPanel);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.RefreshSessions error: {ex.Message}");
            _sessionList.Children.Add(new TextBlock
            {
                Text = "Audio unavailable",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 10)
            });
        }

        // Re-apply current search filter
        ApplySearchFilter();
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

    // ── Search Bar ───────────────────────────────────────────────────

    private UIElement BuildSearchBar()
    {
        var accent = GetAccentColor();

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            Padding = new Thickness(8, 5, 8, 5),
        };

        var dock = new DockPanel { LastChildFill = true };

        // Clear (×) button — shown only when text is present
        _searchClearBtn = new Border
        {
            Width = 18, Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "×",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        _searchClearBtn.MouseLeftButtonDown += (_, _) =>
        {
            _searchBox!.Text = "";
            _searchBox.Focus();
        };
        DockPanel.SetDock(_searchClearBtn, Dock.Right);
        dock.Children.Add(_searchClearBtn);

        // Search textbox
        _searchBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            FontSize = 10,
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
        };

        // Rounded corners via ControlTemplate
        var sbFactory = new FrameworkElementFactory(typeof(Border));
        sbFactory.Name = "bd";
        sbFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        sbFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        sbFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        sbFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        var scrollViewerFactory = new FrameworkElementFactory(typeof(ScrollViewer));
        scrollViewerFactory.Name = "PART_ContentHost";
        scrollViewerFactory.SetValue(ScrollViewer.MarginProperty, new Thickness(2, 0, 2, 0));
        sbFactory.AppendChild(scrollViewerFactory);
        var sbTemplate = new ControlTemplate(typeof(TextBox)) { VisualTree = sbFactory };
        // Focus trigger — accent border
        var focusTrigger = new Trigger { Property = TextBox.IsFocusedProperty, Value = true };
        focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)), "bd"));
        sbTemplate.Triggers.Add(focusTrigger);
        _searchBox.Template = sbTemplate;

        // Placeholder text via TextChanged
        _searchBox.TextChanged += (_, _) =>
        {
            _searchText = _searchBox.Text;
            if (_searchClearBtn != null)
                _searchClearBtn.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Collapsed : Visibility.Visible;
            ApplySearchFilter();
        };

        dock.Children.Add(_searchBox);
        container.Child = dock;

        // Overlay placeholder hint
        var grid = new Grid();
        grid.Children.Add(container);

        var placeholder = new TextBlock
        {
            Text = "Search apps...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            FontSize = 10,
            FontFamily = new FontFamily("Segoe UI"),
            IsHitTestVisible = false,
            Margin = new Thickness(17, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // Hide placeholder when textbox has text
        _searchBox.TextChanged += (_, _) =>
            placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(placeholder);

        return grid;
    }

    private void ApplySearchFilter()
    {
        string q = _searchText.Trim();
        foreach (var row in _rows)
        {
            if (row.RowBorder == null) continue;
            bool visible = string.IsNullOrEmpty(q)
                || row.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase);
            row.RowBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── Quick Assign Panel ────────────────────────────────────────────

    private Border BuildQuickAssignPanel()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 10, 10, 10),
            Tag = "quickassign" // marker for refresh
        };

        panel.Child = BuildQuickAssignContent();
        return panel;
    }

    private FrameworkElement BuildQuickAssignContent()
    {
        var accent = GetAccentColor();
        var root = new StackPanel();

        // Panel header with bottom separator
        var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var headerLabel = new TextBlock
        {
            Text = "QUICK ASSIGN",
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        headerStack.Children.Add(headerLabel);
        headerStack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        });
        root.Children.Add(headerStack);

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
                    Foreground = new SolidColorBrush(accent),
                    FontSize = 9, VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                };
                showBtn.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    var hidden = _config.HiddenTrayApps;
                    hidden?.RemoveAll(h => h.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
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

        var items = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };

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
            new SolidColorBrush(accent), false,
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
            Padding = new Thickness(8, 6, 8, 6),
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
            btn.Background = new SolidColorBrush(Color.FromArgb(active ? (byte)0x35 : (byte)0x20, accent.R, accent.G, accent.B));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(active ? (byte)0xFF : (byte)0x50, accent.R, accent.G, accent.B));
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

    private UIElement BuildHeaderBar()
    {
        var accent = GetAccentColor();
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Padding = new Thickness(12, 7, 8, 7),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        var dock = new DockPanel { LastChildFill = false };

        // ── Right side: Quick Assign + Close ──
        // Close (X) button
        var closeBtn = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Child = new MaterialIcon
            {
                Kind = MaterialIconKind.Close, Width = 14, Height = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        closeBtn.MouseEnter += (_, _) =>
        {
            closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x44, 0x44));
            ((MaterialIcon)closeBtn.Child).Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        };
        closeBtn.MouseLeave += (_, _) =>
        {
            closeBtn.Background = Brushes.Transparent;
            ((MaterialIcon)closeBtn.Child).Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        };
        closeBtn.MouseLeftButtonDown += (_, _) => { Hide(); _onExit?.Invoke(); };
        DockPanel.SetDock(closeBtn, Dock.Right);
        dock.Children.Add(closeBtn);

        // Quick Assign button
        var qaBtn = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
            Padding = new Thickness(6, 3, 6, 3),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        var qaRow = new StackPanel { Orientation = Orientation.Horizontal };
        qaRow.Children.Add(new MaterialIcon
        {
            Kind = MaterialIconKind.LightningBolt, Width = 12, Height = 12,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        qaRow.Children.Add(new TextBlock
        {
            Text = "Assign", FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
        });
        qaBtn.Child = qaRow;
        qaBtn.MouseEnter += (_, _) => qaBtn.Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
        qaBtn.MouseLeave += (_, _) =>
        {
            bool active = _quickAssignVisible;
            qaBtn.Background = new SolidColorBrush(Color.FromArgb(active ? (byte)0x30 : (byte)0x18, accent.R, accent.G, accent.B));
        };
        qaBtn.MouseLeftButtonDown += (_, _) => ToggleQuickAssignPanel();
        DockPanel.SetDock(qaBtn, Dock.Right);
        dock.Children.Add(qaBtn);

        // Connection status
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_statusText);
        DockPanel.SetDock(statusRow, Dock.Right);
        dock.Children.Add(statusRow);

        // ── Left side: icon + "AMP UP" (clickable → open app) ──
        var brandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon/ampup-16.png", UriKind.Absolute);
            var bmp = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            brandRow.Children.Add(new System.Windows.Controls.Image
            {
                Source = bmp, Width = 14, Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
        }
        catch { }
        brandRow.Children.Add(new TextBlock
        {
            Text = "AMP UP",
            Foreground = new SolidColorBrush(accent),
            FontSize = 10, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
        });
        brandRow.MouseLeftButtonDown += (_, _) => { Hide(); _onOpen?.Invoke(); };
        DockPanel.SetDock(brandRow, Dock.Left);
        dock.Children.Add(brandRow);

        header.Child = dock;
        return header;
    }

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
        var accent = GetAccentColor();

        // Build device list + find current default
        var devices = new List<MMDevice>();
        string currentId = "";
        string currentName = "No device";
        int currentIdx = 0;
        try
        {
            var role = flow == DataFlow.Capture ? Role.Communications : Role.Multimedia;
            var enumerated = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            foreach (var d in enumerated) devices.Add(d);
            using var def = _enumerator.GetDefaultAudioEndpoint(flow, role);
            currentId = def.ID;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].ID == currentId)
                {
                    currentIdx = i;
                    currentName = devices[i].FriendlyName;
                    break;
                }
            }
        }
        catch { }

        if (currentName.Length > 40) currentName = currentName[..38] + "...";

        var wrapper = new StackPanel();

        // ── Main row (click to cycle through checked devices) ──
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand,
        };

        var dock = new DockPanel { LastChildFill = true };

        var typeLabel = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9, FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Width = 48, VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(typeLabel, Dock.Left);
        dock.Children.Add(typeLabel);

        // Dropdown arrow on right (clickable to expand/collapse)
        var arrow = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronDown,
            Width = 14, Height = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var arrowBtn = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            Child = arrow,
        };
        arrowBtn.MouseEnter += (_, _) => { arrowBtn.Background = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B)); arrow.Foreground = new SolidColorBrush(accent); };
        arrowBtn.MouseLeave += (_, _) => { arrowBtn.Background = Brushes.Transparent; arrow.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)); };
        DockPanel.SetDock(arrowBtn, Dock.Right);
        dock.Children.Add(arrowBtn);

        // Device icon
        var devIcon = new MaterialIcon
        {
            Kind = flow == DataFlow.Render ? MaterialIconKind.Speaker : MaterialIconKind.Microphone,
            Width = 16, Height = 16,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        DockPanel.SetDock(devIcon, Dock.Right);
        dock.Children.Add(devIcon);

        var nameText = new TextBlock
        {
            Text = currentName,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontSize = 11, FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        dock.Children.Add(nameText);
        row.Child = dock;

        // ── Dropdown panel (hidden by default) ──
        var dropdown = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(48, 0, 0, 4),
        };

        // Get quick-swap set from config
        var configKey = flow == DataFlow.Render ? "output" : "input";
        var quickSwapIds = new HashSet<string>();
        if (_config?.CycleDeviceSubset != null && _config.CycleDeviceSubset.TryGetValue(configKey, out var subset))
            foreach (var id in subset) quickSwapIds.Add(id);

        // If no subset configured, default all checked
        if (quickSwapIds.Count == 0)
            foreach (var d in devices) quickSwapIds.Add(d.ID);

        for (int i = 0; i < devices.Count; i++)
        {
            int idx = i;
            var dev = devices[i];
            bool isDefault = dev.ID == currentId;
            bool inSubset = quickSwapIds.Contains(dev.ID);

            var devRow = new Border
            {
                Background = isDefault
                    ? new SolidColorBrush(Color.FromArgb(0x15, accent.R, accent.G, accent.B))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand,
            };

            var devDock = new DockPanel { LastChildFill = true };

            // Checkbox for quick-swap inclusion
            var cb = new CheckBox
            {
                IsChecked = inSubset,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Include in quick-swap cycle",
            };
            cb.Checked += (_, _) =>
            {
                quickSwapIds.Add(dev.ID);
                SaveQuickSwapSubset(configKey, quickSwapIds);
            };
            cb.Unchecked += (_, _) =>
            {
                quickSwapIds.Remove(dev.ID);
                if (quickSwapIds.Count == 0) { cb.IsChecked = true; return; } // must have at least 1
                SaveQuickSwapSubset(configKey, quickSwapIds);
            };
            DockPanel.SetDock(cb, Dock.Left);
            devDock.Children.Add(cb);

            // Active indicator
            if (isDefault)
            {
                var activeDot = new Border
                {
                    Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                DockPanel.SetDock(activeDot, Dock.Right);
                devDock.Children.Add(activeDot);
            }

            var devName = new TextBlock
            {
                Text = dev.FriendlyName,
                FontSize = 10.5,
                Foreground = isDefault
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            devDock.Children.Add(devName);
            devRow.Child = devDock;

            devRow.MouseEnter += (_, _) => devRow.Background = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B));
            devRow.MouseLeave += (_, _) => devRow.Background = isDefault
                ? new SolidColorBrush(Color.FromArgb(0x15, accent.R, accent.G, accent.B))
                : Brushes.Transparent;

            // Click to switch to this device
            devRow.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    ButtonHandler.SetDefaultAudioDevice(dev.ID);
                    nameText.Text = dev.FriendlyName;
                    if (nameText.Text.Length > 40) nameText.Text = nameText.Text[..38] + "...";
                    currentIdx = idx;
                    dropdown.Visibility = Visibility.Collapsed;
                    arrow.Kind = MaterialIconKind.ChevronDown;
                    Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
                catch (Exception ex) { Logger.Log($"Device switch error: {ex.Message}"); }
            };

            dropdown.Children.Add(devRow);
        }

        wrapper.Children.Add(row);
        wrapper.Children.Add(dropdown);

        // Hover on main row
        row.MouseEnter += (_, _) =>
        {
            row.Background = new SolidColorBrush(Color.FromArgb(20, accent.R, accent.G, accent.B));
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
        };

        // Arrow click: toggle dropdown
        arrowBtn.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (dropdown.Visibility == Visibility.Visible)
            {
                dropdown.Visibility = Visibility.Collapsed;
                arrow.Kind = MaterialIconKind.ChevronDown;
            }
            else
            {
                dropdown.Visibility = Visibility.Visible;
                arrow.Kind = MaterialIconKind.ChevronUp;
            }
            Dispatcher.BeginInvoke(new Action(RepositionOnScreen),
                System.Windows.Threading.DispatcherPriority.Loaded);
        };

        // Click row text: quick-cycle through checked devices
        row.MouseLeftButtonDown += (_, _) =>
        {
            // Build cycle list from quick-swap subset
            var cycleDevices = new List<int>();
            for (int i = 0; i < devices.Count; i++)
                if (quickSwapIds.Contains(devices[i].ID)) cycleDevices.Add(i);

            if (cycleDevices.Count < 2) return;

            // Find current position in cycle list and advance
            int cyclePos = cycleDevices.IndexOf(currentIdx);
            int nextCyclePos = (cyclePos + 1) % cycleDevices.Count;
            int nextIdx = cycleDevices[nextCyclePos];

            try
            {
                ButtonHandler.SetDefaultAudioDevice(devices[nextIdx].ID);
                nameText.Text = devices[nextIdx].FriendlyName;
                if (nameText.Text.Length > 40) nameText.Text = nameText.Text[..38] + "...";
                currentIdx = nextIdx;
            }
            catch (Exception ex) { Logger.Log($"Device quick-swap error: {ex.Message}"); }
        };

        return wrapper;
    }

    private UIElement BuildMasterRow(MMDevice device, float vol, bool muted)
    {
        var accent = GetAccentColor();
        // Master row: accent-tinted bg + border to distinguish clearly from app rows
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(6, 4, 6, 2),
            CornerRadius = new CornerRadius(6),
        };

        var panel = new DockPanel { LastChildFill = true };

        // Icon — MaterialIcon VolumeHigh with accent-tinted background
        var icon = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new MaterialIcon
            {
                Kind = MaterialIconKind.VolumeHigh,
                Foreground = new SolidColorBrush(accent),
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);

        // Mute button
        var muteBtn = BuildMuteButton(muted);
        DockPanel.SetDock(muteBtn, Dock.Right);

        // Vol% label — accent color
        var volLabel = new TextBlock
        {
            Text = $"{(int)Math.Round(vol * 100)}%",
            Foreground = new SolidColorBrush(accent),
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
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
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

        // Scroll wheel: ±2% per tick
        row.MouseWheel += (_, e) =>
        {
            try
            {
                int delta = e.Delta > 0 ? 2 : -2;
                float cur = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                float next = Math.Clamp(cur + delta / 100f, 0f, 1f);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = next;
                int pct = (int)Math.Round(next * 100);
                slider.Value = pct;
                volLabel.Text = $"{pct}%";
            }
            catch { }
            e.Handled = true;
        };

        center.Children.Add(label);
        center.Children.Add(slider);
        panel.Children.Add(center);
        row.Child = panel;
        return row;
    }

    private UIElement? BuildSessionRow(string processName, AudioSessionControl session, bool isPinned = false)
    {
        try
        {
            float vol = session.SimpleAudioVolume.Volume;
            bool muted = session.SimpleAudioVolume.Mute;
            var accent = GetAccentColor();

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(6, 0, 6, 0),
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
                        // 32px icon with dark #1A1A1A background so transparent icons look clean
                        icon = new Border
                        {
                            Width = 32, Height = 32,
                            CornerRadius = new CornerRadius(6),
                            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new System.Windows.Controls.Image
                            {
                                Source = bmpSource, Width = 20, Height = 20,
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

            // Vol% label — accent color
            var volLabel = new TextBlock
            {
                Text = $"{(int)Math.Round(vol * 100)}%",
                Foreground = new SolidColorBrush(accent),
                FontSize = 11,
                Width = 34,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            DockPanel.SetDock(volLabel, Dock.Right);

            panel.Children.Add(muteBtn);
            panel.Children.Add(volLabel);

            // Center: name row + slider + peak bar
            var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };

            // Name row: app name + pin indicator + optional device badge
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            var nameLabel = new TextBlock
            {
                Text = TitleCase(processName),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 155,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameRow.Children.Add(nameLabel);

            // Pin indicator — small accent dot for pinned apps
            if (isPinned)
            {
                nameRow.Children.Add(new Border
                {
                    Width = 6, Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Pinned to top",
                });
            }

            // Per-app device badge — show if session uses a non-default device
            var deviceBadge = BuildDeviceBadge(session);
            if (deviceBadge != null)
                nameRow.Children.Add(deviceBadge);

            var slider = BuildVolumeSlider(vol * 100);

            // Audio activity bar — 3px tall, rounded, full width of row
            var peakBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                Margin = new Thickness(0, 2, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, iconColor.R, iconColor.G, iconColor.B)),
            };

            _rows.Add(new SessionRow(processName, session, slider, volLabel, muteBtn, peakBar, row));
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

            // Scroll wheel: ±2% per tick
            row.MouseWheel += (_, e) =>
            {
                try
                {
                    int delta = e.Delta > 0 ? 2 : -2;
                    float cur = session.SimpleAudioVolume.Volume;
                    float next = Math.Clamp(cur + delta / 100f, 0f, 1f);
                    session.SimpleAudioVolume.Volume = next;
                    int pct = (int)Math.Round(next * 100);
                    slider.Value = pct;
                    volLabel.Text = $"{pct}%";
                }
                catch { }
                e.Handled = true;
            };

            center.Children.Add(nameRow);
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

    /// <summary>
    /// Builds a mixer row for System Sounds (pid==0 WASAPI session). Shown at the bottom.
    /// </summary>
    private UIElement? BuildSystemSoundsRow(AudioSessionControl session)
    {
        try
        {
            float vol = session.SimpleAudioVolume.Volume;
            bool muted = session.SimpleAudioVolume.Mute;
            var accent = GetAccentColor();
            var iconColor = Color.FromRgb(0x00, 0xCC, 0xFF); // cyan for system sounds

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(6)
            };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var panel = new DockPanel { LastChildFill = true };

            // Speaker icon using letter "S"
            var icon = BuildLetterIcon("S", iconColor);
            DockPanel.SetDock(icon, Dock.Left);
            panel.Children.Add(icon);

            var muteBtn = BuildMuteButton(muted);
            DockPanel.SetDock(muteBtn, Dock.Right);

            var volLabel = new TextBlock
            {
                Text = $"{(int)Math.Round(vol * 100)}%",
                Foreground = new SolidColorBrush(accent),
                FontSize = 11,
                Width = 34,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            DockPanel.SetDock(volLabel, Dock.Right);

            panel.Children.Add(muteBtn);
            panel.Children.Add(volLabel);

            var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
            var nameLabel = new TextBlock
            {
                Text = "System Sounds",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            center.Children.Add(nameLabel);

            var slider = BuildVolumeSlider(vol * 100);

            var peakBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                Margin = new Thickness(0, 2, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, iconColor.R, iconColor.G, iconColor.B)),
            };

            _rows.Add(new SessionRow("System Sounds", session, slider, volLabel, muteBtn, peakBar, row, IsSystemSounds: true));

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

            row.MouseWheel += (_, e) =>
            {
                try
                {
                    int delta = e.Delta > 0 ? 2 : -2;
                    float cur = session.SimpleAudioVolume.Volume;
                    float next = Math.Clamp(cur + delta / 100f, 0f, 1f);
                    session.SimpleAudioVolume.Volume = next;
                    int pct = (int)Math.Round(next * 100);
                    slider.Value = pct;
                    volLabel.Text = $"{pct}%";
                }
                catch { }
                e.Handled = true;
            };

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

    /// <summary>
    /// Returns a small pill badge showing the app's output device name if it differs from the
    /// default render device. Returns null if same device or can't be determined.
    /// </summary>
    private Border? BuildDeviceBadge(AudioSessionControl session)
    {
        try
        {
            // Try to get the session's audio device via its AudioSessionManager parent
            // We compare against the default render device friendly name
            string? defaultName = null;
            string? sessionDeviceName = null;
            try
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultName = def.FriendlyName;
            }
            catch { }

            // Sessions on non-default devices are rare but possible; NAudio doesn't expose
            // per-session device directly. We enumerate render devices and check if the session
            // appears on a non-default one.
            try
            {
                var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var ep in endpoints)
                {
                    if (ep.FriendlyName == defaultName) { ep.Dispose(); continue; }
                    var mgr = ep.AudioSessionManager;
                    var sessions = mgr.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.GetProcessID == session.GetProcessID)
                        {
                            sessionDeviceName = ep.FriendlyName;
                            break;
                        }
                    }
                    ep.Dispose();
                    if (sessionDeviceName != null) break;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(sessionDeviceName)) return null;

            // Truncate to ~10 chars
            var display = sessionDeviceName.Length > 10 ? sessionDeviceName[..10] + "…" : sessionDeviceName;

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = display,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };
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
        var isPinned = _config.PinnedTrayApps.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase));
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

        // Pin / Unpin
        if (isPinned)
        {
            menuStack.Children.Add(MakeItem($"Unpin {displayName}",
                new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                () =>
                {
                    _config.PinnedTrayApps.RemoveAll(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase));
                    _onSave?.Invoke(_config);
                    RefreshSessions();
                }));
        }
        else
        {
            menuStack.Children.Add(MakeItem($"📌 Pin {displayName} to top",
                new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
                () =>
                {
                    if (!_config.PinnedTrayApps.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                        _config.PinnedTrayApps.Add(processName);
                    _onSave?.Invoke(_config);
                    RefreshSessions();
                }));
        }

        menuStack.Children.Add(MakeSeparator());

        // Hide/Show toggle
        if (isHidden)
        {
            menuStack.Children.Add(MakeItem($"Show {displayName} in mixer",
                new SolidColorBrush(accent),
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
            Background = new SolidColorBrush(Color.FromArgb(40, bg.R, bg.G, bg.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, bg.R, bg.G, bg.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new MaterialIcon
            {
                Kind = MaterialIconKind.Application,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, bg.R, bg.G, bg.B)),
                Width = size * 0.55,
                Height = size * 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Button BuildMuteButton(bool muted)
    {
        // Build a style with hover/pressed triggers so the button has visible feedback
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(0)));

        // Template to avoid WPF default aero hover chrome
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.Name = "bd";
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenterFactory);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = factory };

        var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(25, 0xEE, 0xEE, 0xEE)), "bd"));
        template.Triggers.Add(hoverTrigger);

        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40, 0xEE, 0xEE, 0xEE)), "bd"));
        template.Triggers.Add(pressedTrigger);

        style.Setters.Add(new Setter(Button.TemplateProperty, template));

        var btn = new Button
        {
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = muted ? "Unmute" : "Mute",
            Style = style,
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

    private void SaveQuickSwapSubset(string key, HashSet<string> ids)
    {
        if (_config == null) return;
        _config.CycleDeviceSubset[key] = new List<string>(ids);
        _onSave?.Invoke(_config);
    }

    private static Color GetAccentColor()
    {
        return ThemeManager.Accent;
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
        <Border Height='4' CornerRadius='2' Background='#2A2A2A' VerticalAlignment='Center' />
        <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton>
                <RepeatButton IsHitTestVisible='False' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Height='4' CornerRadius='2' Background='{accentHex}' VerticalAlignment='Center' Opacity='0.85' />
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton Opacity='0' IsHitTestVisible='False' Focusable='False' />
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb Width='12' Height='12' Cursor='Hand'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Border Background='White' CornerRadius='6' Width='12' Height='12'>
                                <Border.Effect>
                                    <DropShadowEffect Color='{accentHex}' BlurRadius='6' ShadowDepth='0' Opacity='0.5' />
                                </Border.Effect>
                            </Border>
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
            var n when n.Contains("steam") => Color.FromRgb(0x4C, 0x6B, 0x9A), // Steam blue (legible on dark bg)
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
