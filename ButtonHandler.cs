using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using AmpUp.Core.Engine;
using AmpUp.Core.Models;
using AmpUp.Core.Services;

namespace AmpUp;

public class ButtonHandler : IDisposable
{
    // P/Invoke declarations consolidated in NativeMethods.cs

    // ── Virtual key constants ─────────────────────────────────────────

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_CONTROL = 0xA2;
    private const byte VK_SHIFT = 0xA0;
    private const byte VK_MENU = 0xA4;   // Alt
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_TAB = 0x09;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_SPACE = 0x20;
    private const byte VK_UP = 0x26;
    private const byte VK_DOWN = 0x28;
    private const byte VK_LEFT = 0x25;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_DELETE = 0x2E;
    private const byte VK_BACK = 0x08;
    private const byte VK_INSERT = 0x2D;
    private const byte VK_HOME = 0x24;
    private const byte VK_END = 0x23;
    private const byte VK_PRIOR = 0x21; // Page Up
    private const byte VK_NEXT = 0x22;  // Page Down
    private const byte VK_SNAPSHOT = 0x2C; // Print Screen
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // ── Fields ────────────────────────────────────────────────────────

    private HAIntegration? _ha;
    private ObsIntegration? _obs;
    private VoiceMeeterIntegration? _vm;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ButtonGestureEngine _gestureEngine = new();

    // Stash config ref for action execution (needed by MuteAppGroup etc.)
    private volatile AppConfig? _lastConfig;

    // Cycle state: tracks current index per button for cycle_output / cycle_input
    private readonly Dictionary<int, int> _cycleIndex = new();

    // ── Events (forwarded from gesture engine) ──────────────────────

    public void SetHAIntegration(HAIntegration? ha) => _ha = ha;
    public void SetObsIntegration(ObsIntegration? obs) => _obs = obs;
    public void SetVoiceMeeterIntegration(VoiceMeeterIntegration? vm) => _vm = vm;

    /// <summary>Fires with button index when quick_wheel is triggered.</summary>
    public event Action<int>? OnQuickWheelOpen;
    /// <summary>Fires with button index when the quick_wheel button is released.</summary>
    public event Action<int>? OnQuickWheelClose;

    // Track which button opened the wheel (for release detection)
    private int _quickWheelActiveButton = -1;

    public event Action<string>? OnProfileSwitch
    {
        add => _gestureEngine.OnProfileSwitch += value;
        remove => _gestureEngine.OnProfileSwitch -= value;
    }
    /// <summary>Fires with (deviceName, isOutput) when the default audio device changes.</summary>
    public event Action<string, bool>? OnDeviceSwitched
    {
        add => _gestureEngine.OnDeviceSwitched += value;
        remove => _gestureEngine.OnDeviceSwitched -= value;
    }
    /// <summary>Fires with new brightness percentage when cycle_brightness is triggered.</summary>
    public event Action<int>? OnBrightnessCycle
    {
        add => _gestureEngine.OnBrightnessCycle += value;
        remove => _gestureEngine.OnBrightnessCycle -= value;
    }

    // Brightness cycle presets (matches Turn Up behavior)
    private static readonly int[] BrightnessPresets = { 100, 75, 50, 25, 0 };
    private int _brightnessPresetIndex = 0;

    // ── IPolicyConfig COM interface for changing default audio device ──

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // We only need SetDefaultEndpoint; pad the vtable with placeholder slots.
        // IPolicyConfig has many methods before SetDefaultEndpoint.
        // The exact vtable layout varies by Windows version. The commonly used
        // approach puts SetDefaultEndpoint at slot index 0 in a minimal interface.
        // We use the "IPolicyConfigVista" compatible layout:
        int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
        int ResetDeviceFormat(string pszDeviceName);
        int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        int GetShareMode(string pszDeviceName, IntPtr pMode);
        int SetShareMode(string pszDeviceName, IntPtr mode);
        int GetPropertyValue(string pszDeviceName, int bFx, IntPtr pKey, IntPtr pValue);
        int SetPropertyValue(string pszDeviceName, int bFx, IntPtr pKey, IntPtr pValue);
        int SetDefaultEndpoint(string pszDeviceName, int role);
        int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    private static readonly Guid CLSID_PolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    // ── Constructor ─────────────────────────────────────────────────

    public ButtonHandler()
    {
        _gestureEngine.OnGestureAction += HandleGestureAction;
    }

    // ── Gesture forwarding (delegates to core engine) ───────────────

    public void HandleDown(int idx, AppConfig config)
    {
        _lastConfig = config;
        _gestureEngine.HandleDown(idx, config);
    }

    public void HandleUp(int idx, AppConfig config)
    {
        _lastConfig = config;
        _gestureEngine.HandleUp(idx, config);

        // If this button was holding the wheel open, fire close event
        if (_quickWheelActiveButton == idx)
        {
            _quickWheelActiveButton = -1;
            OnQuickWheelClose?.Invoke(idx);
        }
    }

    private void HandleGestureAction(int idx, string gesture, string action, ButtonConfig btn)
    {
        // Auto-trigger Quick Wheel on hold if this is a configured trigger button
        if (gesture == "hold" && _lastConfig?.Osd?.QuickWheels != null)
        {
            foreach (var qw in _lastConfig.Osd.QuickWheels)
            {
                if (qw.Enabled && idx == qw.TriggerButton)
                {
                    _quickWheelActiveButton = idx;
                    OnQuickWheelOpen?.Invoke(idx);
                    return; // override the button's normal hold action
                }
            }
        }

        ExecuteAction(action, btn.Path ?? "", btn);
    }

    // ── Action dispatcher ─────────────────────────────────────────────

    public void ExecuteAction(string action, string path, ButtonConfig? btn = null)
    {
        try
        {
            switch (action.ToLowerInvariant())
            {
                case "media_play_pause":
                    PressKey(VK_MEDIA_PLAY_PAUSE); break;
                case "media_next":
                    PressKey(VK_MEDIA_NEXT_TRACK); break;
                case "media_prev":
                    PressKey(VK_MEDIA_PREV_TRACK); break;
                case "mute_master":
                    PressKey(VK_VOLUME_MUTE); break;
                case "mute_mic":
                    ToggleMicMute(); break;
                case "launch_exe":
                    LaunchExe(path); break;
                case "mute_program":
                    MuteProgram(path); break;
                case "mute_active_window":
                    MuteActiveWindow(); break;
                case "mute_app_group":
                    MuteAppGroup(btn); break;
                case "mute_device":
                    MuteDevice(btn?.DeviceId ?? ""); break;
                case "cycle_output":
                    CycleOutputDevice(btn); break;
                case "cycle_input":
                    CycleInputDevice(btn); break;
                case "select_output":
                    SelectDevice(btn?.DeviceId ?? "", DataFlow.Render); break;
                case "select_input":
                    SelectDevice(btn?.DeviceId ?? "", DataFlow.Capture); break;
                case "close_program":
                    CloseProgram(path); break;
                case "macro":
                    ExecuteMacro(btn?.MacroKeys ?? ""); break;
                case "switch_profile":
                    SwitchProfile(btn?.ProfileName ?? ""); break;
                case "system_power":
                    ExecuteSystemPower(btn?.PowerAction ?? ""); break;
                // Individual power actions (no sub-picker needed)
                case "power_sleep":
                    ExecuteSystemPower("sleep"); break;
                case "power_lock":
                    ExecuteSystemPower("lock"); break;
                case "power_off":
                    ExecuteSystemPower("shutdown"); break;
                case "power_restart":
                    ExecuteSystemPower("restart"); break;
                case "power_logoff":
                    ExecuteSystemPower("logoff"); break;
                case "power_hibernate":
                    ExecuteSystemPower("hibernate"); break;
                case "cycle_brightness":
                    CycleBrightness(); break;
                case "ha_toggle":
                    if (_ha != null && !string.IsNullOrEmpty(path))
                        _ = _ha.ToggleEntityAsync(path);
                    break;
                case "ha_scene":
                    if (_ha != null && !string.IsNullOrEmpty(path))
                        _ = _ha.ActivateSceneAsync(path);
                    break;
                case "ha_service":
                    if (_ha != null && !string.IsNullOrEmpty(path))
                    {
                        // path format: "domain.service:entity_id" or just "entity_id" for toggle
                        var parts = path.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            var domSvc = parts[0].Split('.', 2);
                            if (domSvc.Length == 2)
                                _ = _ha.CallServiceAsync(domSvc[0], domSvc[1], parts[1]);
                        }
                        else
                        {
                            _ = _ha.ToggleEntityAsync(path);
                        }
                    }
                    break;
                case "govee_toggle":
                    // path = device IP
                    if (!string.IsNullOrEmpty(path))
                        _ = AmbienceSync.SendToggleAsync(path);
                    break;
                case "quick_wheel":
                    // Fired by gesture engine (hold gesture) — open the radial wheel
                    if (btn != null)
                    {
                        _quickWheelActiveButton = btn.Idx;
                        OnQuickWheelOpen?.Invoke(btn.Idx);
                    }
                    break;
                case "obs_record":
                    if (_obs != null && _obs.IsAvailable)
                        _ = _obs.ToggleRecordingAsync();
                    break;
                case "obs_stream":
                    if (_obs != null && _obs.IsAvailable)
                        _ = _obs.ToggleStreamingAsync();
                    break;
                case "obs_scene":
                    if (_obs != null && _obs.IsAvailable && !string.IsNullOrEmpty(path))
                        _ = _obs.SetSceneAsync(path);
                    break;
                case "obs_mute":
                    if (_obs != null && _obs.IsAvailable && !string.IsNullOrEmpty(path))
                        _ = _obs.ToggleMuteAsync(path);
                    break;
                case "vm_mute_strip":
                    if (_vm != null && _vm.IsAvailable && int.TryParse(path, out int vmStripIdx))
                        _vm.ToggleStripMute(vmStripIdx);
                    break;
                case "vm_mute_bus":
                    if (_vm != null && _vm.IsAvailable && int.TryParse(path, out int vmBusIdx))
                        _vm.ToggleBusMute(vmBusIdx);
                    break;
                case "govee_color":
                    // path = "ip|hexcolor" e.g. "192.168.1.50|FF0080"
                    if (!string.IsNullOrEmpty(path))
                    {
                        var govParts = path.Split('|', 2);
                        if (govParts.Length == 2 && !string.IsNullOrEmpty(govParts[0]) && !string.IsNullOrEmpty(govParts[1]))
                        {
                            var ip = govParts[0];
                            var hex = govParts[1].TrimStart('#');
                            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                            {
                                byte r = (byte)((rgb >> 16) & 0xFF);
                                byte g = (byte)((rgb >> 8) & 0xFF);
                                byte b = (byte)(rgb & 0xFF);
                                _ = AmbienceSync.SendColorAsync(ip, r, g, b);
                            }
                            else
                            {
                                Logger.Log($"govee_color: invalid hex '{govParts[1]}'");
                            }
                        }
                        else if (govParts.Length == 1 && !string.IsNullOrEmpty(govParts[0]))
                        {
                            Logger.Log("govee_color: missing hex color in path (expected ip|hexcolor)");
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ExecuteAction error ({action}): {ex.Message}");
        }
    }

    // ── Simple key press ──────────────────────────────────────────────

    private static void PressKey(byte vk)
    {
        NativeMethods.keybd_event(vk, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Launch executable ─────────────────────────────────────────────

    private static void LaunchExe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Logger.Log("launch_exe: no path configured");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            Logger.Log($"Launched: {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"launch_exe error: {ex.Message}");
        }
    }

    // ── Mic mute toggle ───────────────────────────────────────────────

    private void ToggleMicMute()
    {
        try
        {
            using var mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute;
            Logger.Log($"Mic mute: {mic.AudioEndpointVolume.Mute}");
        }
        catch (Exception ex)
        {
            Logger.Log($"ToggleMicMute error: {ex.Message}");
        }
    }

    // ── Mute program by process name ──────────────────────────────────

    private void MuteProgram(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            Logger.Log("mute_program: no process name configured");
            return;
        }
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                try
                {
                    var pid = (int)session.GetProcessID;
                    if (pid == 0) continue;
                    var proc = Process.GetProcessById(pid);
                    if (proc.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase)
                        || proc.ProcessName.Replace(" ", "").Contains(processName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                        Logger.Log($"mute_program: {proc.ProcessName} mute={session.SimpleAudioVolume.Mute}");
                        return;
                    }
                }
                catch
                {
                    // Process may have exited
                }
            }
            Logger.Log($"mute_program: no audio session found for '{processName}'");
        }
        catch (Exception ex)
        {
            Logger.Log($"mute_program error: {ex.Message}");
        }
    }

    // ── Mute active (foreground) window ───────────────────────────────

    private void MuteActiveWindow()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                Logger.Log("mute_active_window: no foreground window");
                return;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                Logger.Log("mute_active_window: could not get PID");
                return;
            }

            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == pid)
                {
                    session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                    Logger.Log($"mute_active_window: PID {pid} mute={session.SimpleAudioVolume.Mute}");
                    return;
                }
            }

            // Some apps spawn child processes for audio — try matching by process name
            try
            {
                var fgProc = Process.GetProcessById((int)pid);
                var fgName = fgProc.ProcessName;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        var sPid = (int)session.GetProcessID;
                        if (sPid == 0) continue;
                        var sProc = Process.GetProcessById(sPid);
                        if (sProc.ProcessName.Contains(fgName, StringComparison.OrdinalIgnoreCase))
                        {
                            session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                            Logger.Log($"mute_active_window (name match): {sProc.ProcessName} mute={session.SimpleAudioVolume.Mute}");
                            return;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            Logger.Log($"mute_active_window: no audio session for PID {pid}");
        }
        catch (Exception ex)
        {
            Logger.Log($"mute_active_window error: {ex.Message}");
        }
    }

    // ── Mute all apps in a knob's app group ───────────────────────────

    private void MuteAppGroup(ButtonConfig? btn)
    {
        if (btn == null || _lastConfig == null)
        {
            Logger.Log("mute_app_group: no config");
            return;
        }

        int knobIdx = btn.LinkedKnobIdx;
        if (knobIdx < 0 || knobIdx > 4)
        {
            Logger.Log($"mute_app_group: invalid LinkedKnobIdx {knobIdx}");
            return;
        }

        var knob = _lastConfig.Knobs.FirstOrDefault(k => k.Idx == knobIdx);
        if (knob == null || knob.Apps == null || knob.Apps.Count == 0)
        {
            Logger.Log($"mute_app_group: knob {knobIdx} has no app group");
            return;
        }

        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            var matchingSessions = new List<NAudio.CoreAudioApi.AudioSessionControl>();
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                try
                {
                    var pid = (int)session.GetProcessID;
                    if (pid == 0) continue;
                    var proc = Process.GetProcessById(pid);
                    var procName = proc.ProcessName.ToLowerInvariant();

                    foreach (var appName in knob.Apps)
                    {
                        var appLower = appName.ToLowerInvariant();
                        if (procName.Contains(appLower)
                            || procName.Replace(" ", "").Contains(appLower.Replace(" ", "")))
                        {
                            matchingSessions.Add(session);
                            break;
                        }
                    }
                }
                catch { }
            }

            if (matchingSessions.Count == 0)
            {
                Logger.Log($"mute_app_group: no active sessions found for knob {knobIdx} apps");
                return;
            }

            // Toggle: if ANY session is unmuted → mute all. If ALL muted → unmute all.
            bool anyUnmuted = matchingSessions.Any(s => !s.SimpleAudioVolume.Mute);
            bool newMuteState = anyUnmuted;

            foreach (var session in matchingSessions)
            {
                try { session.SimpleAudioVolume.Mute = newMuteState; } catch { }
            }

            var appNames = string.Join(", ", knob.Apps);
            Logger.Log($"mute_app_group: knob {knobIdx} [{appNames}] → mute={newMuteState} ({matchingSessions.Count} sessions)");
        }
        catch (Exception ex)
        {
            Logger.Log($"mute_app_group error: {ex.Message}");
        }
    }

    // ── Mute specific audio device by DeviceId ───────────────────────

    private void MuteDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Logger.Log("mute_device: no DeviceId configured");
            return;
        }

        try
        {
            // Try Render (output) devices first, then Capture (input)
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                try
                {
                    var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var device = devices[i];
                        if (device.ID == deviceId)
                        {
                            bool newMute = !device.AudioEndpointVolume.Mute;
                            device.AudioEndpointVolume.Mute = newMute;
                            Logger.Log($"mute_device: {device.FriendlyName} mute={newMute}");
                            return;
                        }
                    }
                }
                catch { }
            }
            Logger.Log($"mute_device: no active device found with ID '{deviceId}'");
        }
        catch (Exception ex)
        {
            Logger.Log($"mute_device error: {ex.Message}");
        }
    }

    // ── Cycle output devices ──────────────────────────────────────────

    private void CycleOutputDevice(ButtonConfig? btn)
    {
        try
        {
            CycleDevice(DataFlow.Render, btn?.DeviceIds, btn?.Idx ?? -1, btn?.CycleDeviceType ?? CycleDeviceType.Both);
        }
        catch (Exception ex)
        {
            Logger.Log($"cycle_output error: {ex.Message}");
        }
    }

    // ── Cycle input devices ───────────────────────────────────────────

    private void CycleInputDevice(ButtonConfig? btn)
    {
        try
        {
            CycleDevice(DataFlow.Capture, btn?.DeviceIds, btn?.Idx ?? -1, btn?.CycleDeviceType ?? CycleDeviceType.Both);
        }
        catch (Exception ex)
        {
            Logger.Log($"cycle_input error: {ex.Message}");
        }
    }

    private void CycleDevice(DataFlow flow, List<string>? allowedIds, int buttonIdx, CycleDeviceType deviceType = CycleDeviceType.Both)
    {
        var role = flow == DataFlow.Render ? Role.Multimedia : Role.Communications;
        using var currentDevice = _enumerator.GetDefaultAudioEndpoint(flow, role);
        var currentId = currentDevice.ID;

        List<string> deviceIds;
        if (allowedIds != null && allowedIds.Count > 0)
        {
            // Use only the specified subset, but verify they exist
            var allDevices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            var activeIds = new HashSet<string>();
            for (int i = 0; i < allDevices.Count; i++)
            {
                using var d = allDevices[i];
                activeIds.Add(d.ID);
            }
            deviceIds = allowedIds.Where(id => activeIds.Contains(id)).ToList();
        }
        else
        {
            var allDevices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            deviceIds = new List<string>();
            for (int i = 0; i < allDevices.Count; i++)
            {
                using var d = allDevices[i];
                deviceIds.Add(d.ID);
            }
        }

        if (deviceIds.Count < 2)
        {
            Logger.Log($"cycle_{(flow == DataFlow.Render ? "output" : "input")}: not enough devices to cycle ({deviceIds.Count})");
            return;
        }

        // Find current position and advance
        int currentIdx = deviceIds.IndexOf(currentId);
        int nextIdx = (currentIdx + 1) % deviceIds.Count;
        var nextId = deviceIds[nextIdx];

        SetDefaultAudioDevice(nextId, deviceType);

        // Log friendly name and fire event
        try
        {
            var allDevs = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            for (int i = 0; i < allDevs.Count; i++)
            {
                using var d = allDevs[i];
                if (d.ID == nextId)
                {
                    var name = d.FriendlyName;
                    Logger.Log($"cycle_{(flow == DataFlow.Render ? "output" : "input")}: switched to {name}");
                    _gestureEngine.RaiseDeviceSwitched(name, flow == DataFlow.Render);
                    break;
                }
            }
        }
        catch { }
    }

    // ── Select specific device ────────────────────────────────────────

    private void SelectDevice(string deviceId, DataFlow flow)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Logger.Log($"select_{(flow == DataFlow.Render ? "output" : "input")}: no DeviceId configured");
            return;
        }
        try
        {
            SetDefaultAudioDevice(deviceId);
            // Resolve friendly name for notification
            try
            {
                var allDevs = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
                for (int i = 0; i < allDevs.Count; i++)
                {
                    using var d = allDevs[i];
                    if (d.ID == deviceId)
                    {
                        _gestureEngine.RaiseDeviceSwitched(d.FriendlyName, flow == DataFlow.Render);
                        break;
                    }
                }
            }
            catch { }
            Logger.Log($"select_{(flow == DataFlow.Render ? "output" : "input")}: set device {deviceId}");
        }
        catch (Exception ex)
        {
            Logger.Log($"select_{(flow == DataFlow.Render ? "output" : "input")} error: {ex.Message}");
        }
    }

    // ── Set default audio device via IPolicyConfig COM ─────────────────

    internal static void SetDefaultAudioDevice(string deviceId, CycleDeviceType deviceType = CycleDeviceType.Both)
    {
        var policyConfigType = Type.GetTypeFromCLSID(CLSID_PolicyConfigClient);
        if (policyConfigType == null)
            throw new InvalidOperationException("Could not get PolicyConfigClient type from CLSID");

        var policyConfig = (IPolicyConfig)Activator.CreateInstance(policyConfigType)!;

        switch (deviceType)
        {
            case CycleDeviceType.Media:
                policyConfig.SetDefaultEndpoint(deviceId, 0); // Console
                policyConfig.SetDefaultEndpoint(deviceId, 1); // Multimedia
                break;
            case CycleDeviceType.Communications:
                policyConfig.SetDefaultEndpoint(deviceId, 2); // Communications
                break;
            default: // Both
                policyConfig.SetDefaultEndpoint(deviceId, 0);
                policyConfig.SetDefaultEndpoint(deviceId, 1);
                policyConfig.SetDefaultEndpoint(deviceId, 2);
                break;
        }
    }

    // ── Close / kill program ──────────────────────────────────────────

    private static void CloseProgram(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            Logger.Log("close_program: no process name configured");
            return;
        }
        try
        {
            // Strip .exe if user included it
            var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;

            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0)
            {
                // Fallback: substring match across all processes
                procs = Process.GetProcesses()
                    .Where(p =>
                    {
                        try { return p.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    })
                    .ToArray();
            }

            if (procs.Length > 0)
            {
                procs[0].Kill();
                Logger.Log($"close_program: killed {procs[0].ProcessName} (PID {procs[0].Id})");
            }
            else
            {
                Logger.Log($"close_program: no process found matching '{processName}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"close_program error: {ex.Message}");
        }
    }

    // ── Macro (keyboard combo) ────────────────────────────────────────

    private static void ExecuteMacro(string macroKeys)
    {
        if (string.IsNullOrWhiteSpace(macroKeys))
        {
            Logger.Log("macro: no MacroKeys configured");
            return;
        }

        try
        {
            var parts = macroKeys.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var modifiers = new List<byte>();
            var keys = new List<byte>();

            foreach (var part in parts)
            {
                switch (part)
                {
                    case "ctrl" or "control":
                        modifiers.Add(VK_CONTROL); break;
                    case "shift":
                        modifiers.Add(VK_SHIFT); break;
                    case "alt":
                        modifiers.Add(VK_MENU); break;
                    case "win" or "windows" or "super":
                        modifiers.Add(VK_LWIN); break;
                    default:
                        var vk = ResolveKeyCode(part);
                        if (vk != 0) keys.Add(vk);
                        else Logger.Log($"macro: unknown key '{part}'");
                        break;
                }
            }

            // Press modifiers down
            foreach (var mod in modifiers)
                NativeMethods.keybd_event(mod, 0, 0, UIntPtr.Zero);

            // Press and release each key
            foreach (var key in keys)
            {
                NativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            // Release modifiers in reverse order
            for (int i = modifiers.Count - 1; i >= 0; i--)
                NativeMethods.keybd_event(modifiers[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            Logger.Log($"macro: executed '{macroKeys}'");
        }
        catch (Exception ex)
        {
            Logger.Log($"macro error: {ex.Message}");
        }
    }

    private static byte ResolveKeyCode(string keyName)
    {
        return keyName switch
        {
            "enter" or "return" => VK_RETURN,
            "tab" => VK_TAB,
            "escape" or "esc" => VK_ESCAPE,
            "space" or "spacebar" => VK_SPACE,
            "up" => VK_UP,
            "down" => VK_DOWN,
            "left" => VK_LEFT,
            "right" => VK_RIGHT,
            "delete" or "del" => VK_DELETE,
            "backspace" => VK_BACK,
            "insert" or "ins" => VK_INSERT,
            "home" => VK_HOME,
            "end" => VK_END,
            "pageup" or "pgup" => VK_PRIOR,
            "pagedown" or "pgdn" => VK_NEXT,
            "printscreen" or "prtsc" => VK_SNAPSHOT,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33, "4" => 0x34,
            "5" => 0x35, "6" => 0x36, "7" => 0x37, "8" => 0x38, "9" => 0x39,
            _ => ResolveKeyCodeFallback(keyName)
        };
    }

    private static byte ResolveKeyCodeFallback(string keyName)
    {
        // Single character — use VkKeyScan to resolve to virtual key code
        if (keyName.Length == 1)
        {
            short result = NativeMethods.VkKeyScan(keyName[0]);
            byte vk = (byte)(result & 0xFF);
            if (vk != 0xFF)
                return vk;
            // VkKeyScan failed; fall back to uppercase ASCII for letter keys
            if (char.IsAsciiLetter(keyName[0]))
                return (byte)char.ToUpperInvariant(keyName[0]);
        }

        return 0;
    }

    // ── Switch profile ────────────────────────────────────────────────

    private void SwitchProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            Logger.Log("switch_profile: no ProfileName configured");
            return;
        }
        Logger.Log($"switch_profile: requesting switch to '{profileName}'");
        _gestureEngine.RaiseProfileSwitch(profileName);
    }

    // ── LED brightness cycle ──────────────────────────────────────────

    private void CycleBrightness()
    {
        _brightnessPresetIndex = (_brightnessPresetIndex + 1) % BrightnessPresets.Length;
        var pct = BrightnessPresets[_brightnessPresetIndex];
        Logger.Log($"cycle_brightness: {pct}%");
        _gestureEngine.RaiseBrightnessCycle(pct);
    }

    // ── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        _gestureEngine.OnGestureAction -= HandleGestureAction;
        _gestureEngine.Dispose();
        _enumerator.Dispose();
    }

    // ── System power actions ──────────────────────────────────────────

    private static void ExecuteSystemPower(string powerAction)
    {
        if (string.IsNullOrWhiteSpace(powerAction))
        {
            Logger.Log("system_power: no PowerAction configured");
            return;
        }

        try
        {
            switch (powerAction.ToLowerInvariant())
            {
                case "sleep":
                    Logger.Log("system_power: sleep");
                    // forceCritical=false allows apps to prepare for sleep properly
                    // (GPU, USB devices, etc. get clean suspend notifications)
                    NativeMethods.SetSuspendState(false, false, false);
                    break;
                case "hibernate":
                    Logger.Log("system_power: hibernate");
                    NativeMethods.SetSuspendState(true, false, false);
                    break;
                case "lock":
                    Logger.Log("system_power: lock");
                    NativeMethods.LockWorkStation();
                    break;
                case "shutdown":
                    Logger.Log("system_power: shutdown");
                    Process.Start("shutdown", "/s /t 0");
                    break;
                case "restart":
                    Logger.Log("system_power: restart");
                    Process.Start("shutdown", "/r /t 0");
                    break;
                case "logoff":
                    Logger.Log("system_power: logoff");
                    NativeMethods.ExitWindowsEx(0, 0);
                    break;
                default:
                    Logger.Log($"system_power: unknown power action '{powerAction}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"system_power error: {ex.Message}");
        }
    }
}
