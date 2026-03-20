using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AmpUp.Mac;

/// <summary>
/// Manages the macOS menu bar tray icon (NSStatusBarItem via Avalonia TrayIcon API).
/// Shows AmpUp icon in the menu bar with a right-click NativeMenu.
/// Left-click toggles main window visibility.
/// Icon reflects connection status: green = connected, gray = disconnected.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _profileItem;
    private readonly NativeMenuItem _muteItem;
    private readonly NativeMenuItem _showHideItem;

    private MainWindow? _mainWindow;
    private bool _isMuted;
    public bool IsQuitting { get; set; }
    private bool _isConnected;
    private string _activeProfile = "Default";

    // Cached bitmaps for connection status
    private readonly WindowIcon _connectedIcon;
    private readonly WindowIcon _disconnectedIcon;

    public TrayIconManager()
    {
        _connectedIcon = LoadIcon("avares://AmpUp.Mac/Assets/tray-connected.png");
        _disconnectedIcon = LoadIcon("avares://AmpUp.Mac/Assets/tray-disconnected.png");

        _showHideItem = new NativeMenuItem("Show AmpUp");
        _showHideItem.Click += OnShowHideClicked;

        _profileItem = new NativeMenuItem("Profile: Default") { IsEnabled = false };

        _muteItem = new NativeMenuItem("Mute Master");
        _muteItem.Click += OnMuteClicked;

        var quitItem = new NativeMenuItem("Quit AmpUp");
        quitItem.Click += OnQuitClicked;

        var menu = new NativeMenu();
        menu.Add(_showHideItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_profileItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_muteItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "AmpUp",
            Icon = _disconnectedIcon,
            Menu = menu,
            IsVisible = true
        };

        // Left-click: toggle window on macOS (Clicked fires on left-click)
        _trayIcon.Clicked += OnTrayClicked;
    }

    /// <summary>
    /// Attach the main window so the manager can show/hide it.
    /// Call once after the window is created.
    /// </summary>
    public void AttachWindow(MainWindow window)
    {
        _mainWindow = window;

        // Intercept close → hide to tray instead
        _mainWindow.Closing += OnWindowClosing;
        _mainWindow.Activated += (_, _) => UpdateShowHideLabel();
        _mainWindow.Deactivated += (_, _) => UpdateShowHideLabel();
    }

    /// <summary>Update icon and menu to reflect device connection state.</summary>
    public void SetConnectionStatus(bool connected)
    {
        _isConnected = connected;
        _trayIcon.Icon = connected ? _connectedIcon : _disconnectedIcon;
        _trayIcon.ToolTipText = connected ? "AmpUp — Connected" : "AmpUp — Disconnected";
    }

    /// <summary>Update the profile name shown in the tray menu.</summary>
    public void SetActiveProfile(string profileName)
    {
        _activeProfile = profileName;
        _profileItem.Header = $"Profile: {profileName}";
    }

    /// <summary>Sync mute toggle label with current master mute state.</summary>
    public void SetMuteState(bool muted)
    {
        _isMuted = muted;
        _muteItem.Header = muted ? "Unmute Master" : "Mute Master";
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    // ── Event wiring for external actions ──────────────────────────

    /// <summary>Fired when user clicks Mute/Unmute from the tray menu.</summary>
    public event Action<bool>? MuteToggled;

    // ── Private handlers ───────────────────────────────────────────

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        ToggleWindowVisibility();
    }

    private void OnShowHideClicked(object? sender, EventArgs e)
    {
        ToggleWindowVisibility();
    }

    private void OnMuteClicked(object? sender, EventArgs e)
    {
        _isMuted = !_isMuted;
        SetMuteState(_isMuted);
        MuteToggled?.Invoke(_isMuted);
    }

    private void OnQuitClicked(object? sender, EventArgs e)
    {
        // Kill first, cleanup second — ensure we actually exit
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Schedule kill on background thread in case main thread blocks
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(500);
            System.Diagnostics.Process.Start("kill", $"-9 {pid}");
        }) { IsBackground = true }.Start();

        // Try cleanup (may fail, that's OK — kill timer is already ticking)
        try
        {
            IsQuitting = true;
            if (Application.Current is App app)
                app.Cleanup();
            _trayIcon.IsVisible = false;
        }
        catch { }

        // Try immediate exit
        Environment.Exit(0);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (IsQuitting) return; // Let it close — app is terminating

        // Hide to tray instead of closing
        e.Cancel = true;
        _mainWindow?.Hide();
        UpdateShowHideLabel();
    }

    private void ToggleWindowVisibility()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }

        UpdateShowHideLabel();
    }

    private void UpdateShowHideLabel()
    {
        _showHideItem.Header = (_mainWindow?.IsVisible == true) ? "Hide AmpUp" : "Show AmpUp";
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static WindowIcon LoadIcon(string uri)
    {
        var bitmap = new Bitmap(AssetLoader.Open(new Uri(uri)));
        return new WindowIcon(bitmap);
    }
}
