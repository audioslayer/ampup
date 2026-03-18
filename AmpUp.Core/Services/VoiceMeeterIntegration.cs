using System.Runtime.InteropServices;
using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class VoiceMeeterIntegration : IDisposable
{
    private bool _loggedIn;
    private bool _available;
    private bool _disposed;
    private readonly object _lock = new();
    private Task? _reconnectTask;
    private CancellationTokenSource? _cts;

    public bool IsAvailable => _available;
    public bool IsConnected => _loggedIn;

    // ── P/Invoke to VoicemeeterRemote64.dll ─────────────────────────

    private const string DllName = "VoicemeeterRemote64.dll";

    [DllImport(DllName, EntryPoint = "VBVMR_Login", CallingConvention = CallingConvention.StdCall)]
    private static extern int VBVMR_Login();

    [DllImport(DllName, EntryPoint = "VBVMR_Logout", CallingConvention = CallingConvention.StdCall)]
    private static extern int VBVMR_Logout();

    [DllImport(DllName, EntryPoint = "VBVMR_SetParameterFloat", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_SetParameterFloat([MarshalAs(UnmanagedType.LPStr)] string param, float value);

    [DllImport(DllName, EntryPoint = "VBVMR_GetParameterFloat", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_GetParameterFloat([MarshalAs(UnmanagedType.LPStr)] string param, out float value);

    [DllImport(DllName, EntryPoint = "VBVMR_IsParametersDirty", CallingConvention = CallingConvention.StdCall)]
    private static extern int VBVMR_IsParametersDirty();

    [DllImport(DllName, EntryPoint = "VBVMR_GetVoicemeeterType", CallingConvention = CallingConvention.StdCall)]
    private static extern int VBVMR_GetVoicemeeterType(out int type);

    [DllImport(DllName, EntryPoint = "VBVMR_GetParameterStringA", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_GetParameterStringA([MarshalAs(UnmanagedType.LPStr)] string param, [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder value);

    // ── Public API ──────────────────────────────────────────────────

    public VoiceMeeterIntegration()
    {
        // Check if the DLL is loadable
        _available = CheckDllAvailable();
        if (_available)
            Logger.Log("VoiceMeeter: DLL found, integration available");
        else
            Logger.Log("VoiceMeeter: DLL not found, integration unavailable");
    }

    private static bool CheckDllAvailable()
    {
        try
        {
            // Try to find the DLL via registry (VoiceMeeter installs to Program Files)
            var installDir = GetInstallDir();
            if (!string.IsNullOrEmpty(installDir))
            {
                var dllPath = System.IO.Path.Combine(installDir, DllName);
                if (System.IO.File.Exists(dllPath))
                {
                    // Add the install dir to the DLL search path so P/Invoke can find it
                    SetDllDirectory(installDir);
                    return true;
                }
            }

            // Fallback: try loading directly (might be in PATH)
            VBVMR_Login();
            VBVMR_Logout();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch
        {
            // DLL exists but maybe VoiceMeeter isn't running — that's still "available"
            return true;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static string? GetInstallDir()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}");
            return key?.GetValue("UninstallString")?.ToString() is string path
                ? System.IO.Path.GetDirectoryName(path) : null;
        }
        catch { return null; }
    }

    public bool Connect()
    {
        if (!_available) return false;

        lock (_lock)
        {
            if (_loggedIn) return true;

            try
            {
                int result = VBVMR_Login();
                // 0 = OK, 1 = OK (VoiceMeeter not running but will launch)
                if (result == 0 || result == 1)
                {
                    _loggedIn = true;
                    Logger.Log($"VoiceMeeter: Login successful (result={result})");
                    StartReconnectMonitor();
                    return true;
                }

                Logger.Log($"VoiceMeeter: Login failed (result={result})");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"VoiceMeeter: Login exception: {ex.Message}");
                _available = false;
                return false;
            }
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            if (_loggedIn)
            {
                try { VBVMR_Logout(); } catch { }
                _loggedIn = false;
                Logger.Log("VoiceMeeter: Logged out");
            }
        }
    }

    /// <summary>
    /// Set strip gain. Range: -60 to +12 dB.
    /// </summary>
    public void SetStripGain(int stripIdx, float db)
    {
        if (!EnsureConnected()) return;
        db = Math.Clamp(db, -60f, 12f);
        try
        {
            VBVMR_SetParameterFloat($"Strip[{stripIdx}].Gain", db);
        }
        catch (Exception ex)
        {
            Logger.Log($"VoiceMeeter: SetStripGain({stripIdx}, {db}) failed: {ex.Message}");
            HandleConnectionLost();
        }
    }

    /// <summary>
    /// Set bus gain. Range: -60 to +12 dB.
    /// </summary>
    public void SetBusGain(int busIdx, float db)
    {
        if (!EnsureConnected()) return;
        db = Math.Clamp(db, -60f, 12f);
        try
        {
            VBVMR_SetParameterFloat($"Bus[{busIdx}].Gain", db);
        }
        catch (Exception ex)
        {
            Logger.Log($"VoiceMeeter: SetBusGain({busIdx}, {db}) failed: {ex.Message}");
            HandleConnectionLost();
        }
    }

    /// <summary>
    /// Toggle mute on a strip. Returns the new mute state (true=muted).
    /// </summary>
    public bool ToggleStripMute(int stripIdx)
    {
        if (!EnsureConnected()) return false;
        try
        {
            VBVMR_GetParameterFloat($"Strip[{stripIdx}].Mute", out float current);
            float newVal = current < 0.5f ? 1f : 0f;
            VBVMR_SetParameterFloat($"Strip[{stripIdx}].Mute", newVal);
            Logger.Log($"VoiceMeeter: Strip[{stripIdx}] mute → {(newVal > 0.5f ? "ON" : "OFF")}");
            return newVal > 0.5f;
        }
        catch (Exception ex)
        {
            Logger.Log($"VoiceMeeter: ToggleStripMute({stripIdx}) failed: {ex.Message}");
            HandleConnectionLost();
            return false;
        }
    }

    /// <summary>
    /// Toggle mute on a bus. Returns the new mute state (true=muted).
    /// </summary>
    public bool ToggleBusMute(int busIdx)
    {
        if (!EnsureConnected()) return false;
        try
        {
            VBVMR_GetParameterFloat($"Bus[{busIdx}].Mute", out float current);
            float newVal = current < 0.5f ? 1f : 0f;
            VBVMR_SetParameterFloat($"Bus[{busIdx}].Mute", newVal);
            Logger.Log($"VoiceMeeter: Bus[{busIdx}] mute → {(newVal > 0.5f ? "ON" : "OFF")}");
            return newVal > 0.5f;
        }
        catch (Exception ex)
        {
            Logger.Log($"VoiceMeeter: ToggleBusMute({busIdx}) failed: {ex.Message}");
            HandleConnectionLost();
            return false;
        }
    }

    /// <summary>
    /// Get strip labels. Returns list of (index, name) tuples.
    /// VoiceMeeter Basic: 3 strips, Banana: 5 strips, Potato: 8 strips.
    /// </summary>
    public List<(int Index, string Name)> GetStripNames()
    {
        var result = new List<(int, string)>();
        if (!EnsureConnected()) return result;

        int count = GetStripCount();
        for (int i = 0; i < count; i++)
        {
            string name = GetStringParam($"Strip[{i}].Label");
            if (string.IsNullOrWhiteSpace(name))
                name = $"Strip {i + 1}";
            result.Add((i, name));
        }
        return result;
    }

    /// <summary>
    /// Get bus labels. Returns list of (index, name) tuples.
    /// VoiceMeeter Basic: 2 buses, Banana: 5 buses, Potato: 8 buses.
    /// </summary>
    public List<(int Index, string Name)> GetBusNames()
    {
        var result = new List<(int, string)>();
        if (!EnsureConnected()) return result;

        int count = GetBusCount();
        for (int i = 0; i < count; i++)
        {
            string name = GetStringParam($"Bus[{i}].Label");
            if (string.IsNullOrWhiteSpace(name))
                name = $"Bus {i + 1}";
            result.Add((i, name));
        }
        return result;
    }

    /// <summary>
    /// Map a normalized 0.0-1.0 knob value to VoiceMeeter gain range (-60 to +12 dB).
    /// </summary>
    public static float NormalizedToGain(float normalized)
    {
        // 0.0 → -60 dB, 1.0 → +12 dB (72 dB range)
        return -60f + normalized * 72f;
    }

    // ── Internals ───────────────────────────────────────────────────

    private bool EnsureConnected()
    {
        if (_loggedIn) return true;
        if (!_available) return false;
        return Connect();
    }

    private void HandleConnectionLost()
    {
        lock (_lock)
        {
            _loggedIn = false;
        }
    }

    private void StartReconnectMonitor()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _reconnectTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(5000, token);
                if (_disposed) return;

                if (!_loggedIn && _available)
                {
                    try
                    {
                        int result = VBVMR_Login();
                        if (result == 0 || result == 1)
                        {
                            lock (_lock) _loggedIn = true;
                            Logger.Log("VoiceMeeter: Reconnected");
                        }
                    }
                    catch { }
                }
            }
        }, token);
    }

    private int GetVoicemeeterType()
    {
        try
        {
            VBVMR_GetVoicemeeterType(out int type);
            return type; // 1=Basic, 2=Banana, 3=Potato
        }
        catch { return 0; }
    }

    private int GetStripCount()
    {
        return GetVoicemeeterType() switch
        {
            1 => 3,  // Basic
            2 => 5,  // Banana
            3 => 8,  // Potato
            _ => 5   // Default to Banana count
        };
    }

    private int GetBusCount()
    {
        return GetVoicemeeterType() switch
        {
            1 => 2,  // Basic
            2 => 5,  // Banana
            3 => 8,  // Potato
            _ => 3   // Default to 3
        };
    }

    private string GetStringParam(string param)
    {
        try
        {
            var sb = new System.Text.StringBuilder(512);
            int result = VBVMR_GetParameterStringA(param, sb);
            return result == 0 ? sb.ToString() : "";
        }
        catch { return ""; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _cts?.Dispose();
    }
}
