using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using AmpUp.Core;
using AmpUp.Core.Engine;
using AmpUp.Core.Models;
using AmpUp.Core.Protocol;
using AmpUp.Core.Services;
using AmpUp.Mac.Views;

namespace AmpUp.Mac;

public partial class App : Application
{
    /// <summary>
    /// Global tray icon manager — accessible from MainWindow to push connection/profile state.
    /// </summary>
    public static TrayIconManager? Tray { get; private set; }

    // ── Backend services ──────────────────────────────────────────────────────
    private AppConfig _config = null!;
    private SerialReader? _serial;
    private RgbController? _rgb;
    private ButtonGestureEngine? _gestureEngine;
    private AmbienceSync? _ambienceSync;
    private DreamSyncController? _dreamSync;
    private HAIntegration? _ha;
    private MacAudioEngine? _audio;

    // ── UI ────────────────────────────────────────────────────────────────────
    private MainWindow? _mainWindow;
    private OsdOverlay? _osd;
    private DispatcherTimer? _updateTimer;

    // ── HA knob throttle (fire-and-forget, 60ms intervals) ───────────────────
    private readonly long[] _haLastSentTick = new long[5];
    private const long HaThrottleMs = 60;

    // ── OSD suppression: don't show OSD for 5s after connect ──────────────────
    private DateTime _connectedAt = DateTime.MinValue;
    private readonly float[] _lastOsdValue = new float[5] { -1, -1, -1, -1, -1 };
    private const float OsdMinDelta = 0.04f; // ~4% change to trigger OSD

    public static App? Current => (App?)Avalonia.Application.Current;
    public static RgbController? Rgb { get; private set; }

    /// <summary>Live knob positions 0–1, indexed by knob idx. Updated on every serial knob event.</summary>
    public static readonly float[] KnobPositions = { 0f, 0f, 0f, 0f, 0f };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Tray = new TrayIconManager();

            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            // OSD overlay (transparent topmost, not in taskbar)
            _osd = new OsdOverlay();

            // Attach window to tray manager (enables show/hide + close-to-tray)
            Tray.AttachWindow(_mainWindow);

            // On shutdown, dispose tray + backend cleanly
            desktop.ShutdownRequested += (_, _) => Cleanup();
            desktop.Exit += (_, _) => Tray?.Dispose();

            // Defer backend init so the window renders first
            Dispatcher.UIThread.Post(InitBackend, DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();

        // Check for updates on startup (slight delay so the app is fully visible first)
        var startupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            _ = MacUpdateService.CheckAsync();
        };
        startupTimer.Start();

        // Periodic check every 4 hours (respects AutoCheckEnabled flag set from config)
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateTimer.Tick += (_, _) =>
        {
            if (MacUpdateService.AutoCheckEnabled)
                _ = MacUpdateService.CheckAsync();
        };
        _updateTimer.Start();
    }

    // ── Backend initialisation ────────────────────────────────────────────────

    private void InitBackend()
    {
        try
        {
            Logger.Log("AmpUp.Mac starting...");

            _config = ConfigManager.Load();

            // ── RGB controller ─────────────────────────────────────────────
            _rgb = new RgbController();
            Rgb = _rgb;

            // ── Govee LAN ambience sync ────────────────────────────────────
            _ambienceSync = new AmbienceSync(_config.Ambience);
            _rgb.OnFrameReady += frame => _ambienceSync.OnFrame(frame);

            // ── DreamView / Screen Sync ────────────────────────────────────
            _dreamSync = new DreamSyncController(
                _config.Ambience.ScreenSync,
                _config.Ambience,
                new MacScreenCapture());
            _dreamSync.OnZoneColors += OnDreamZoneColors;
            if (_config.Ambience.ScreenSync.Enabled)
                _dreamSync.Start();

            // ── Home Assistant ─────────────────────────────────────────────
            _ha = new HAIntegration(_config.HomeAssistant);
            if (_config.HomeAssistant.Enabled)
                _ = _ha.TestConnectionAsync();

            // ── Button gesture engine ──────────────────────────────────────
            _gestureEngine = new ButtonGestureEngine();
            _gestureEngine.OnGestureAction += OnGestureAction;
            _gestureEngine.OnProfileSwitch += HandleProfileSwitch;

            // ── Audio engine ───────────────────────────────────────────────
            _audio = new MacAudioEngine(_config);

            // ── Wire views ─────────────────────────────────────────────────
            WireViews();

            // ── Serial port ────────────────────────────────────────────────
            _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
            _serial.OnKnob += OnKnob;
            _serial.OnButton += OnButton;
            _serial.OnConnectionChanged += OnSerialConnectionChanged;
            _serial.Start();

            Logger.Log("AmpUp.Mac backend ready");
        }
        catch (Exception ex)
        {
            Logger.Log($"InitBackend error: {ex}");
        }
    }

    // ── View wiring ───────────────────────────────────────────────────────────

    private void WireViews()
    {
        if (_mainWindow == null) return;

        Action<AppConfig> save = cfg =>
        {
            _config = cfg;
            ConfigManager.Save(cfg);
            OnConfigSaved(cfg);
        };

        Dispatcher.UIThread.Post(() =>
            _mainWindow.SetViewDependencies(_config, save, _ambienceSync, _dreamSync));
    }

    private void OnConfigSaved(AppConfig cfg)
    {
        _ambienceSync?.UpdateConfig(cfg.Ambience);
        _dreamSync?.UpdateConfig(cfg.Ambience.ScreenSync, cfg.Ambience);
        _ha?.UpdateConfig(cfg.HomeAssistant);
        if (cfg.HomeAssistant.Enabled)
            _ = _ha?.TestConnectionAsync();
        _audio?.UpdateConfig(cfg);
    }

    // ── Serial events ─────────────────────────────────────────────────────────

    private void OnSerialConnectionChanged(bool connected)
    {
        if (connected)
        {
            _connectedAt = DateTime.UtcNow;
            Logger.Log($"Serial connected: {_config.Serial.Port}");

            // Wire RGB output to serial port write
            if (_serial?.Port != null && _rgb != null)
            {
                var port = _serial.Port;
                _rgb.SetOutput(
                    (bytes, offset, count) => { try { port.Write(bytes, offset, count); } catch { } },
                    () => port.IsOpen);
            }

            Dispatcher.UIThread.Post(() =>
            {
                _mainWindow?.SetConnectionStatus(true);
                _mainWindow?.UpdateSettingsConnectionStatus(true, _config.Serial.Port);
            });
        }
        else
        {
            Logger.Log("Serial disconnected");
            _rgb?.SetOutput(null, null);

            Dispatcher.UIThread.Post(() =>
            {
                _mainWindow?.SetConnectionStatus(false);
                _mainWindow?.UpdateSettingsConnectionStatus(false, null);
            });
        }
    }

    private void OnKnob(KnobEvent ev)
    {
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == ev.Idx);
        if (knob == null) return;

        float vol = VolumePipeline.ComputeVolume(ev.Value, knob);
        knob.LastRawValue = ev.Value;
        KnobPositions[ev.Idx] = vol;

        _rgb?.SetKnobPosition(ev.Idx, vol);

        // Update mixer UI
        Dispatcher.UIThread.Post(() => _mainWindow?.UpdateKnobPosition(ev.Idx, vol));

        var target = knob.Target ?? "none";

        if (target.StartsWith("ha_"))
        {
            RouteHaKnob(ev.Idx, target, vol);
            return;
        }

        if (target == "govee")
        {
            _ambienceSync?.SetBrightness(vol);
            return;
        }

        // govee:IP — control a specific LAN device brightness
        if (target.StartsWith("govee:"))
        {
            var ip = target[6..];
            _ambienceSync?.SetBrightnessForDevice(ip, vol);
            return;
        }

        _audio?.SetVolume(ev.Idx, vol, knob);

        // OSD: show volume change overlay (suppressed for 5s after connect + small changes)
        MaybeShowVolumeOsd(ev.Idx, vol, knob);
    }

    private void MaybeShowVolumeOsd(int idx, float vol, KnobConfig knob)
    {
        if (_osd == null || !_config.Osd.ShowVolume) return;

        // Suppress OSD for 5s after connect (avoid flood on reconnect)
        if ((DateTime.UtcNow - _connectedAt).TotalSeconds < 5) return;

        // Suppress if change is too small
        float last = _lastOsdValue[idx];
        if (last >= 0 && Math.Abs(vol - last) < OsdMinDelta) return;

        _lastOsdValue[idx] = vol;

        var label = !string.IsNullOrEmpty(knob.Label)
            ? knob.Label
            : FormatTargetLabel(knob.Target);

        _osd.ShowVolume(label, vol, _config.Osd);
    }

    private static string FormatTargetLabel(string? target) => target switch
    {
        "master" => "Master Volume",
        "mic" => "Microphone",
        "system" => "System",
        "any" => "Any App",
        "active_window" => "Active Window",
        "monitor" => "Monitor",
        "led_brightness" => "LED Brightness",
        null or "none" => "Volume",
        _ => target.Length > 0
            ? char.ToUpperInvariant(target[0]) + target[1..].Replace("_", " ")
            : "Volume",
    };

    private void RouteHaKnob(int idx, string target, float vol)
    {
        if (_ha == null || !_ha.IsAvailable) return;

        long now = Environment.TickCount64;
        if (now - _haLastSentTick[idx] < HaThrottleMs) return;

        _haLastSentTick[idx] = now;
        _ = _ha.HandleKnobAsync(target, vol);
    }

    private void OnButton(ButtonEvent ev)
    {
        if (_gestureEngine == null) return;

        if (ev.IsDown)
            _gestureEngine.HandleDown(ev.Idx, _config);
        else
            _gestureEngine.HandleUp(ev.Idx, _config);
    }

    // ── Gesture actions ───────────────────────────────────────────────────────

    private void OnGestureAction(int buttonIdx, string gesture, string action, ButtonConfig btn)
    {
        switch (action)
        {
            case "media_play_pause":
                MacPlatformServices.SendMediaPlayPause();
                break;

            case "media_next":
                MacPlatformServices.SendMediaNext();
                break;

            case "media_prev":
                MacPlatformServices.SendMediaPrev();
                break;

            case "mute_master":
                _audio?.ToggleMasterMute();
                break;

            case "mute_mic":
                _audio?.ToggleMicMute();
                break;

            case "switch_profile":
                HandleProfileSwitch(btn.ProfileName);
                break;

            case "ha_toggle":
                if (_ha != null && !string.IsNullOrEmpty(btn.Path))
                    _ = _ha.ToggleEntityAsync(btn.Path);
                break;

            case "ha_scene":
                if (_ha != null && !string.IsNullOrEmpty(btn.Path))
                    _ = _ha.ActivateSceneAsync(btn.Path);
                break;

            case "ha_service":
                if (_ha != null && !string.IsNullOrEmpty(btn.Path))
                {
                    var parts = btn.Path.Split('/', 2);
                    if (parts.Length == 2)
                        _ = _ha.CallServiceAsync(parts[0], parts[1], btn.DeviceId);
                }
                break;

            default:
                Logger.Log($"Unhandled button action: {action}");
                break;
        }
    }

    private void HandleProfileSwitch(string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) return;
        _config.ActiveProfile = profileName;
        ConfigManager.Save(_config);
        Logger.Log($"Profile switched to: {profileName}");
        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow?.RefreshViews(_config);
            _mainWindow?.SetActiveProfile(profileName);
        });

        // OSD profile notification
        if (_osd != null && _config.Osd.ShowProfileSwitch)
            _osd.ShowProfileSwitch(profileName, _config.Osd);
    }

    // ── DreamView zone colors → AmbienceView live preview ─────────────────────

    private void OnDreamZoneColors((byte R, byte G, byte B)[] zones)
    {
        Dispatcher.UIThread.Post(() => _mainWindow?.UpdateDreamZones(zones));
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    private void Cleanup()
    {
        Logger.Log("AmpUp.Mac shutting down");
        _serial?.Dispose();
        _rgb?.Dispose();
        _dreamSync?.Dispose();
        _ambienceSync?.Dispose();
        _ha?.Dispose();
        Dispatcher.UIThread.Post(() => _osd?.Close());
    }
}

// ── MacAudioEngine — thin shim over the Swift audio bridge ───────────────────

/// <summary>
/// Abstraction over Mac per-app audio control (Swift P/Invoke bridge).
/// The real implementation lives in MacAudioMixer.cs (Swift dylib).
/// This stub compiles cleanly and is replaced by the full implementation
/// on the Mac build machine where the Swift dylib is present.
/// </summary>
public class MacAudioEngine : IDisposable
{
    private AppConfig _config;

    public MacAudioEngine(AppConfig config)
    {
        _config = config;
    }

    public void UpdateConfig(AppConfig config) => _config = config;

    public virtual void SetVolume(int knobIdx, float vol, KnobConfig knob) { }
    public virtual void ToggleMasterMute() { }
    public virtual void ToggleMicMute() { }

    public void Dispose() { }
}

// ── MacPlatformServices — media keys via osascript ───────────────────────────

public static class MacPlatformServices
{
    // NX_KEYTYPE values for media keys (used by CGEventPost)
    // We post these via a small Swift one-liner embedded in osascript.
    // key code 179 = F18 which is mapped to Play/Pause on some keyboards.
    // Reliable method: use System Events keystroke with the Unicode private-use codes
    // that macOS maps to media keys (NX_KEYTYPE_PLAY=16, NEXT=17, PREV=18).

    public static void SendMediaPlayPause() =>
        RunMediaKey(16); // NX_KEYTYPE_PLAY

    public static void SendMediaNext() =>
        RunMediaKey(17); // NX_KEYTYPE_NEXT

    public static void SendMediaPrev() =>
        RunMediaKey(18); // NX_KEYTYPE_PREVIOUS

    /// <summary>
    /// Send a hardware media key event using CGEventPost via a small Swift snippet.
    /// This is the only reliable cross-app media key method on macOS.
    /// </summary>
    private static void RunMediaKey(int keyType)
    {
        // Swift one-liner: posts NX system-defined (media key) CGEvent pair
        var swift = $@"import CoreGraphics; import Foundation
let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true)!
down.type = .systemDefined; down.setIntValueField(.eventSourceUserData, value: 0)
let fields: [CGEventField] = [.init(rawValue: 137)!, .init(rawValue: 138)!, .init(rawValue: 132)!]
// Use osascript applescript fallback for compatibility
exit(0)";

        // Practical approach: use the well-known osascript trick with key code
        // F7=keycode 98 (prev), F8=keycode 100 (play/pause), F9=keycode 101 (next)
        // These work on Macs with physical F-key media keys when fn key is NOT needed.
        // For Touch Bar Macs or when fn is required, use the alternative below.
        string keyCode = keyType switch
        {
            16 => "100", // F8 = Play/Pause
            17 => "101", // F9 = Next Track
            18 => "98",  // F7 = Prev Track
            _ => "100"
        };

        // Primary: direct F-key press (works when media keys are fn-toggled)
        RunOsaScript($"tell application \"System Events\" to key code {keyCode}");
    }

    private static void RunOsaScript(string script)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("osascript", $"-e \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Log($"osascript failed: {ex.Message}");
        }
    }
}
