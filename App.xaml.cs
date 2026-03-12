using System.Drawing;
using System.Windows;
using Microsoft.Win32;
using AmpUp.Controls;
using Forms = System.Windows.Forms;

namespace AmpUp;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppConfig _config = null!;
    private SerialReader _serial = null!;
    private AudioMixer _mixer = null!;
    private ButtonHandler _buttons = null!;
    private RgbController _rgb = null!;
    private AudioAnalyzer? _audioAnalyzer;
    private MainWindow _mainWindow = null!;
    private System.Threading.Timer? _mutePollingTimer;
    private System.Threading.Timer? _autoSwitchTimer;
    private DateTime _connectedAt = DateTime.MinValue;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isConnected;
    private OsdOverlay? _osdOverlay;
    private HAIntegration? _ha;
    private DuckingEngine? _duckingEngine;
    private AutoProfileSwitcher? _autoSwitcher;
    private TrayMixerPopup? _trayMixerPopup;

    /// <summary>
    /// Last hardware knob positions (0-1), updated on every knob event.
    /// Used by MixerView to display position for non-audio targets.
    /// </summary>
    public static readonly float[] KnobPositions = { 1f, 1f, 1f, 1f, 1f };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash handlers — wire up before anything else
        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Log($"CRASH (UI): {ex.Exception}");
            ShowCrashDialog(ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Logger.Log($"CRASH (AppDomain): {ex.ExceptionObject}");
            if (ex.ExceptionObject is Exception exception) ShowCrashDialog(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Logger.Log($"CRASH (Task): {ex.Exception}");
            ex.SetObserved();
        };

        // Single instance check
        _mutex = new Mutex(true, "AmpUp_SingleInstance", out bool isNew);
        if (!isNew)
        {
            GlassDialog.ShowInfo("Amp Up is already running. Check the system tray.");
            Shutdown();
            return;
        }

        Logger.Log("Amp Up starting (WPF)...");

        // Load config and create backend
        _config = ConfigManager.Load();

        // Apply user's accent color
        ThemeManager.SetAccentColor(_config.AccentColor);

        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();
        _audioAnalyzer = new AudioAnalyzer();
        _rgb.SetAudioAnalyzer(_audioAnalyzer);

        _buttons.OnProfileSwitch += HandleProfileSwitch;
        _buttons.OnDeviceSwitched += HandleDeviceSwitched;
        _buttons.OnBrightnessCycle += HandleBrightnessCycle;

        // Start Home Assistant integration
        _ha = new HAIntegration(_config.HomeAssistant);
        _buttons.SetHAIntegration(_ha);
        if (_config.HomeAssistant.Enabled)
            _ = _ha.TestConnectionAsync(); // sets IsAvailable for knob routing

        // Start audio mixer
        _mixer.Start();

        // Ducking engine
        _duckingEngine = new DuckingEngine();

        // Auto-profile switcher
        _autoSwitcher = new AutoProfileSwitcher(_config.AutoSwitch);
        _autoSwitcher.OnProfileSwitchRequested += profileName =>
            Dispatcher.Invoke(() => SwitchToProfile(profileName));
        _autoSwitchTimer = new System.Threading.Timer(_ => _autoSwitcher?.Poll(), null, 2000, 1500);

        // Start serial reader
        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += HandleKnob;
        _serial.OnButton += HandleButton;
        _serial.OnConnectionChanged += HandleConnection;
        _serial.Start();

        // Apply RGB config
        ApplyRgbConfig();
        UpdateAudioAnalyzer();

        // Poll mute states every 500ms for LED status effects
        _mutePollingTimer = new System.Threading.Timer(_ => PollMuteStates(), null, 1000, 500);

        // Apply startup setting
        ApplyStartupSetting();

        // Create tray icon
        SetupTrayIcon();

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RebuildTrayColors);

        // Create main window
        _mainWindow = new MainWindow();
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.Initialize(_config, _mixer, OnConfigChanged);

        // Start minimized to tray if launched with --minimized (Windows startup)
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--minimized"))
            _mainWindow.Show();

        // Sync connection status — serial may have connected before window was created
        if (_isConnected)
            _mainWindow.SetConnectionStatus(true, _serial.Port?.PortName);

        // Welcome dialog — show on first run OR when version changes (update)
        var currentVersion = UpdateChecker.CurrentVersion;
        bool isFirstRun = !_config.HasCompletedSetup;
        bool isUpdate = _config.HasCompletedSetup && _config.LastWelcomeVersion != currentVersion;

        if ((isFirstRun || isUpdate) && !args.Contains("--minimized"))
        {
            var welcome = new WelcomeDialog(() =>
            {
                ShowMainWindow();
                _mainWindow?.NavigateToSettings();
            });
            welcome.Closed += (_, _) =>
            {
                _config.HasCompletedSetup = true;
                _config.LastWelcomeVersion = currentVersion;
                ConfigManager.Save(_config);
            };
            welcome.Show();
        }
    }

    private Forms.ToolStripLabel? _trayStatusLabel;

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(false),
            Text = "Amp Up",
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ShowTrayMixer();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.BackColor = Color.FromArgb(0x11, 0x11, 0x11);
        menu.ForeColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
        menu.Padding = new Forms.Padding(0, 6, 0, 6);
        menu.ShowImageMargin = false;
        menu.Renderer = new GlassMenuRenderer();

        // Header: app name
        var header = new Forms.ToolStripLabel
        {
            Text = "  AMP UP",
            Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B),
            Padding = new Forms.Padding(10, 8, 10, 0),
        };
        menu.Items.Add(header);

        // Version label
        var version = new Forms.ToolStripLabel
        {
            Text = $"  v{UpdateChecker.CurrentVersion}",
            Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
            Padding = new Forms.Padding(10, 0, 10, 4),
        };
        menu.Items.Add(version);

        // Connection status
        _trayStatusLabel = new Forms.ToolStripLabel
        {
            Text = "  ○  Disconnected",
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0x9A, 0x9A, 0x9A),
            Padding = new Forms.Padding(10, 2, 10, 6),
        };
        menu.Items.Add(_trayStatusLabel);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Show
        var showItem = new Forms.ToolStripMenuItem("  Open Amp Up")
        {
            Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold),
            Padding = new Forms.Padding(6, 6, 6, 6),
        };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Assign App submenu placeholder — rebuilt on Opening
        var assignItem = new Forms.ToolStripMenuItem("  Assign Running Apps →")
        {
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular),
            Padding = new Forms.Padding(6, 6, 6, 6),
        };
        menu.Items.Add(assignItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Exit
        var exitItem = new Forms.ToolStripMenuItem("  Exit")
        {
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0xFF, 0x44, 0x44),
            Padding = new Forms.Padding(6, 6, 6, 6),
        };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;

        menu.Opening += (_, _) => RebuildAssignSubmenu(assignItem);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void ShowTrayMixer()
    {
        Dispatcher.Invoke(() =>
        {
            _trayMixerPopup ??= new TrayMixerPopup();
            _trayMixerPopup.ShowPopup();
        });
    }

    private void RebuildTrayColors()
    {
        if (_trayIcon?.ContextMenuStrip == null) return;
        // Update tray icon header color
        var items = _trayIcon.ContextMenuStrip.Items;
        if (items.Count > 0 && items[0] is Forms.ToolStripLabel header)
            header.ForeColor = System.Drawing.Color.FromArgb(ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B);
    }

    private void RebuildAssignSubmenu(Forms.ToolStripMenuItem assignItem)
    {
        assignItem.DropDownItems.Clear();

        var runningApps = _mixer?.GetRunningAudioApps() ?? new List<string>();
        if (runningApps.Count == 0)
        {
            var noneItem = new Forms.ToolStripMenuItem("  (no audio apps running)")
            {
                Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
                ForeColor = Color.FromArgb(0x55, 0x55, 0x55),
                Enabled = false,
            };
            assignItem.DropDownItems.Add(noneItem);
            return;
        }

        foreach (var appName in runningApps)
        {
            var appCapture = appName;
            var appItem = new Forms.ToolStripMenuItem($"  {appName}")
            {
                Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular),
                Padding = new Forms.Padding(4, 4, 4, 4),
            };

            // Sub-items: one per knob
            for (int k = 0; k < 5; k++)
            {
                int knobIdx = k;
                var knob = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
                string knobLabel = knob != null && !string.IsNullOrWhiteSpace(knob.Label)
                    ? knob.Label
                    : $"Knob {knobIdx + 1}";

                var knobItem = new Forms.ToolStripMenuItem($"  {knobLabel}")
                {
                    Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular),
                    Padding = new Forms.Padding(4, 4, 4, 4),
                };
                knobItem.Click += (_, _) =>
                {
                    var cfg = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
                    if (cfg != null)
                    {
                        cfg.Target = appCapture;
                        cfg.Label = appCapture;
                        ConfigManager.Save(_config);
                        _mainWindow?.Dispatcher.Invoke(() =>
                        {
                            _mainWindow.RefreshViews();
                        });
                    }
                };
                appItem.DropDownItems.Add(knobItem);
            }

            assignItem.DropDownItems.Add(appItem);
        }
    }

    private void ShowCrashDialog(Exception ex)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                GlassDialog.ShowWarning(
                    $"Amp Up encountered an error and needs to close.\n\nA crash log has been saved to:\n{Logger.LogPath}\n\nPlease include it when reporting the issue on GitHub.\n\n{ex.Message}",
                    title: "Amp Up Crashed");
                ExitApp();
            });
        }
        catch { }
    }

    private void ExitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _autoSwitchTimer?.Dispose();
        _duckingEngine?.Dispose();
        Dispatcher.Invoke(() => Shutdown());
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Close to tray instead of exiting
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    /// <summary>
    /// Creates a 32x32 tray icon from the embedded logo PNG.
    /// Connected = full color, disconnected = grayscale.
    /// </summary>
    private static Icon CreateTrayIcon(bool connected)
    {
        // Load logo from embedded WPF resource
        var uri = new Uri("pack://application:,,,/Assets/icon/ampup-32.png", UriKind.Absolute);
        var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;

        Bitmap original;
        if (stream != null)
        {
            original = new Bitmap(stream);
            stream.Dispose();
        }
        else
        {
            // Fallback: solid green square
            original = new Bitmap(32, 32);
            using var g = Graphics.FromImage(original);
            g.Clear(Color.FromArgb(ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
        }

        // Resize to 32x32
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.DrawImage(original, 0, 0, 32, 32);
        }
        original.Dispose();

        // If disconnected, convert to grayscale
        if (!connected)
        {
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var px = bmp.GetPixel(x, y);
                int gray = (int)(px.R * 0.3 + px.G * 0.59 + px.B * 0.11);
                bmp.SetPixel(x, y, Color.FromArgb(px.A, gray, gray, gray));
            }
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var result = (Icon)icon.Clone();
        NativeMethods.DestroyIcon(hIcon);
        bmp.Dispose();
        return result;
    }

    /// <summary>
    /// Glassmorphism renderer for the tray context menu — matches OSD overlay style.
    /// </summary>
    private class GlassMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly Color BgColor = Color.FromArgb(0xEE, 0x11, 0x11, 0x11);
        private static Color BorderColor => Color.FromArgb(0x44, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B);
        private static Color HoverBg => Color.FromArgb(0x18, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B);
        private static Color SepColor => Color.FromArgb(0x30, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B);

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(6, 1, e.Item.Width - 12, e.Item.Height - 2);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (e.Item is Forms.ToolStripMenuItem && e.Item.Selected)
            {
                using var brush = new SolidBrush(HoverBg);
                using var path = RoundedRect(rect, 6);
                e.Graphics.FillPath(brush, path);

                // Subtle accent border on hover
                using var pen = new Pen(Color.FromArgb(0x22, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            var bounds = e.AffectedBounds;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Fill background
            using var bgPath = RoundedRect(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), 12);
            using var bgBrush = new SolidBrush(BgColor);
            e.Graphics.FillPath(bgBrush, bgPath);

            // Green glow border (gradient from top-left to bottom-right like the OSD)
            using var borderPen = new Pen(BorderColor, 1.2f);
            e.Graphics.DrawPath(borderPen, bgPath);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            // Suppress default border — we draw our own rounded one in OnRenderToolStripBackground
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.ContentRectangle.Height / 2;
            using var pen = new Pen(SepColor);
            e.Graphics.DrawLine(pen, 16, y, e.Item.Width - 16, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        UpdateAudioAnalyzer();
        ApplyStartupSetting();
        if (_ha != null)
        {
            _ha.UpdateConfig(_config.HomeAssistant);
            if (_config.HomeAssistant.Enabled)
                _ = _ha.TestConnectionAsync();
        }
        _autoSwitcher?.UpdateConfig(_config.AutoSwitch);
    }

    private void HandleKnob(KnobEvent e)
    {
        // Track hardware position for UI display
        if (e.Idx >= 0 && e.Idx < 5)
            KnobPositions[e.Idx] = e.Value / 1023f;

        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
            {
                // Route to Home Assistant
                if (_ha != null && _ha.IsAvailable)
                {
                    float vol = e.Value / 1023f;
                    _ = _ha.HandleKnobAsync(knob.Target, vol);
                }
            }
            else if (knob.Target.Equals("monitor", StringComparison.OrdinalIgnoreCase))
            {
                float vol = e.Value / 1023f;
                MonitorBrightness.SetAll(vol);
            }
            else if (knob.Target.Equals("led_brightness", StringComparison.OrdinalIgnoreCase))
            {
                int pct = (int)Math.Round(e.Value / 1023.0 * 100);
                _config.LedBrightness = pct;
                _rgb.SetBrightness(pct);
            }
            else
            {
                _mixer.SetVolume(knob, e.Value);
            }

            // Show OSD overlay when volume OSD is enabled
            if (_config.Osd.ShowVolume)
            {
                float pct = e.Value / 1023f;
                // Apply min/max range
                int displayPct = (int)Math.Round(knob.MinVolume + pct * (knob.MaxVolume - knob.MinVolume));
                string label = !string.IsNullOrEmpty(knob.Label) ? knob.Label : knob.Target;
                string symbol = knob.Target switch
                {
                    "master" => "Speaker224",
                    "mic" => "Mic24",
                    "monitor" => "Desktop24",
                    "led_brightness" => "Color24",
                    "spotify" => "MusicNote124",
                    "discord" => "Headphones24",
                    _ when knob.Target.StartsWith("ha_") => "Home24",
                    _ => "Speaker124"
                };
                Dispatcher.Invoke(() =>
                {
                    EnsureOsd();
                    _osdOverlay!.ShowVolume(label, displayPct, symbol);
                });
            }
        }
        _rgb.SetKnobPosition(e.Idx, e.Value / 1023f);

        // Push position directly to MixerView so the knob arc updates immediately
        // (don't wait for the 50ms LiveTimer_Tick poll)
        float pos = e.Value / 1023f;
        Dispatcher.BeginInvoke(() => _mainWindow?.UpdateKnobPosition(e.Idx, pos));
    }

    private void HandleButton(ButtonEvent e)
    {
        // Ignore button events in the first second after connection
        if ((DateTime.UtcNow - _connectedAt).TotalMilliseconds < 1000)
            return;

        if (e.IsDown)
            _buttons.HandleDown(e.Idx, _config);
        else
            _buttons.HandleUp(e.Idx, _config);
    }

    private void HandleConnection(bool connected)
    {
        if (connected)
        {
            _connectedAt = DateTime.UtcNow;

            // Initialize RGB knob positions — default to 1.0 (full brightness LEDs)
            // The batch frame from the device will update these to actual hardware positions,
            // and the live timer will track real audio volumes once running.
            for (int i = 0; i < 5; i++)
                _rgb.SetKnobPosition(i, 1f);

            _rgb.SetBrightness(_config.LedBrightness);
            _rgb.SetPort(_serial.Port);
            _rgb.ApplyColors(_config.Lights);
            UpdateAudioAnalyzer();
        }

        _isConnected = connected;
        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(connected);
            _trayIcon.Text = connected ? "Amp Up — Connected" : "Amp Up — Disconnected";
            oldIcon?.Dispose();
        }

        if (_trayStatusLabel != null)
        {
            _trayStatusLabel.Text = connected ? "  ●  Connected" : "  ○  Disconnected";
            _trayStatusLabel.ForeColor = connected ? Color.FromArgb(0x00, 0xDD, 0x77) : Color.FromArgb(0x9A, 0x9A, 0x9A);
        }

        _mainWindow?.SetConnectionStatus(connected, connected ? _serial.Port?.PortName : null);
    }

    /// <summary>
    /// Switch to a named profile. Used by button gestures and AutoProfileSwitcher.
    /// </summary>
    private void SwitchToProfile(string profileName)
    {
        HandleProfileSwitch(profileName);
    }

    private void HandleProfileSwitch(string profileName)
    {
        var profile = ConfigManager.LoadProfile(profileName);
        if (profile == null)
        {
            Logger.Log($"Profile '{profileName}' not found");
            return;
        }

        // Preserve global settings that shouldn't change per-profile
        var osd = _config.Osd;
        var serial = _config.Serial;
        var startWithWindows = _config.StartWithWindows;
        var ha = _config.HomeAssistant;
        var profiles = _config.Profiles;
        var profileIcons = _config.ProfileIcons;
        var ducking = _config.Ducking;
        var autoSwitch = _config.AutoSwitch;

        _config = profile;
        _config.ActiveProfile = profileName;
        _config.Osd = osd;
        _config.Serial = serial;
        _config.StartWithWindows = startWithWindows;
        _config.HomeAssistant = ha;
        _config.Profiles = profiles;
        _config.ProfileIcons = profileIcons;
        _config.Ducking = ducking;
        _config.AutoSwitch = autoSwitch;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        UpdateAudioAnalyzer();

        // Use the profile's icon color for the transition
        var transIconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        System.Windows.Media.Color profileColor;
        try { profileColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(transIconCfg.Color); }
        catch { profileColor = System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76); }
        _rgb.PlayTransition(_config.ProfileTransition, profileColor.R, profileColor.G, profileColor.B);
        Logger.Log($"Switched to profile: {profileName}");

        // Show OSD for profile switch
        if (_config.Osd.ShowProfileSwitch)
        {
            Dispatcher.Invoke(() =>
            {
                EnsureOsd();
                var iconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
                _osdOverlay!.ShowProfileSwitch(profileName, iconCfg);
            });
        }
    }

    private void HandleBrightnessCycle(int pct)
    {
        _config.LedBrightness = pct;
        _rgb.SetBrightness(pct);
        ConfigManager.Save(_config);

        if (_config.Osd.ShowVolume)
        {
            Dispatcher.Invoke(() =>
            {
                EnsureOsd();
                _osdOverlay!.ShowVolume("LED Brightness", pct, "Color24");
            });
        }
    }

    private void HandleDeviceSwitched(string deviceName, bool isOutput)
    {
        if (!_config.Osd.ShowDeviceSwitch) return;
        Dispatcher.Invoke(() =>
        {
            EnsureOsd();
            _osdOverlay!.ShowDevice(deviceName, isOutput);
        });
    }

    private void EnsureOsd()
    {
        _osdOverlay ??= new OsdOverlay();
        _osdOverlay.SetPosition(_config.Osd.Position);
    }

    private string _lastDefaultOutputDeviceId = "";

    // Cached enumerator for mute polling (created once, lives for the app lifetime)
    private NAudio.CoreAudioApi.MMDeviceEnumerator? _pollEnumerator;
    // Cached devices for mute polling — refreshed only when the default device changes
    private NAudio.CoreAudioApi.MMDevice? _cachedMic;
    private NAudio.CoreAudioApi.MMDevice? _cachedMaster;

    private void PollMuteStates()
    {
        try
        {
            _duckingEngine?.Poll(_config.Ducking);
        }
        catch { }

        try
        {
            _pollEnumerator ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();

            try
            {
                // Lazily cache the default mic; re-fetch only on failure
                _cachedMic ??= _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                _rgb.SetMicMuted(_cachedMic.AudioEndpointVolume.Mute);
            }
            catch
            {
                // Device may have changed — clear cache so it's re-fetched next tick
                _cachedMic?.Dispose();
                _cachedMic = null;
            }

            try
            {
                _cachedMaster ??= _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _rgb.SetMasterMuted(_cachedMaster.AudioEndpointVolume.Mute);

                // Notify RgbController when the default output device changes (for DeviceSelect effect)
                string currentId = _cachedMaster.ID;
                if (currentId != _lastDefaultOutputDeviceId)
                {
                    _lastDefaultOutputDeviceId = currentId;
                    _rgb.SetDefaultOutputDevice(currentId);
                    // Default device changed — clear master cache so next poll fetches the new default
                    _cachedMaster.Dispose();
                    _cachedMaster = null;
                }
            }
            catch
            {
                _cachedMaster?.Dispose();
                _cachedMaster = null;
            }

            // Poll program mute states for ProgramMute LED effect
            PollProgramMuteStates();
        }
        catch { }
    }

    private void PollProgramMuteStates()
    {
        try
        {
            // Determine which lights need program mute polling
            var lightsToCheck = new List<LightConfig>();
            foreach (var l in _config.Lights)
            {
                if (l.Effect == LightEffect.ProgramMute && !string.IsNullOrWhiteSpace(l.ProgramName))
                    lightsToCheck.Add(l);
            }
            if (_config.GlobalLight.Enabled && _config.GlobalLight.Effect == LightEffect.ProgramMute)
            {
                for (int i = 0; i < 5; i++)
                    lightsToCheck.Add(new LightConfig { Idx = i, ProgramName = (_config.Lights.FirstOrDefault(l => l.Idx == i)?.ProgramName) ?? "" });
            }

            if (lightsToCheck.Count == 0) return;

            if (_pollEnumerator == null) return;
            // Reuse the cached master device if available; otherwise get a fresh one
            NAudio.CoreAudioApi.MMDevice? device = null;
            bool ownDevice = false;
            if (_cachedMaster != null)
            {
                device = _cachedMaster;
            }
            else
            {
                try
                {
                    device = _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                    ownDevice = true;
                }
                catch { return; }
            }

            try
            {
                var sessions = device.AudioSessionManager.Sessions;

                foreach (var light in lightsToCheck)
                {
                    if (string.IsNullOrWhiteSpace(light.ProgramName)) continue;
                    bool muted = true; // default: muted/not-found
                    try
                    {
                        for (int s = 0; s < sessions.Count; s++)
                        {
                            var session = sessions[s];
                            try
                            {
                                uint pid = session.GetProcessID;
                                if (pid == 0) continue;
                                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                if (proc.ProcessName.Contains(light.ProgramName, StringComparison.OrdinalIgnoreCase))
                                {
                                    muted = session.SimpleAudioVolume.Mute;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    _rgb.SetProgramMuted(light.Idx, muted);
                }
            }
            finally
            {
                if (ownDevice) device?.Dispose();
            }
        }
        catch { }
    }

    private void ApplyRgbConfig()
    {
        _rgb.SetBrightness(_config.LedBrightness);
        _rgb.UpdateConfig(_config.Lights);
        _rgb.UpdateGlobalConfig(_config.GlobalLight);
    }

    /// <summary>
    /// Start or stop the AudioAnalyzer based on whether any light uses AudioReactive.
    /// </summary>
    private void UpdateAudioAnalyzer()
    {
        bool needsAudio = _config.Lights.Any(l => l.Effect == LightEffect.AudioReactive)
            || (_config.GlobalLight.Enabled && _config.GlobalLight.Effect == LightEffect.AudioReactive);
        if (needsAudio)
            _audioAnalyzer?.Start();
        else
            _audioAnalyzer?.Stop();
    }

    private void ApplyStartupSetting()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "AmpUp";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null) return;

            if (_config.StartWithWindows)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue(valueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to update startup setting: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutePollingTimer?.Dispose();
        _autoSwitchTimer?.Dispose();
        _duckingEngine?.Dispose();
        _osdOverlay?.Close();
        _serial?.Dispose();
        _mixer?.Dispose();
        _audioAnalyzer?.Dispose();
        _rgb?.Dispose();
        _ha?.Dispose();
        _cachedMic?.Dispose();
        _cachedMaster?.Dispose();
        _pollEnumerator?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
