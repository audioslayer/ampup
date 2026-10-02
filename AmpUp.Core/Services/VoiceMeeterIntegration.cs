using System.Runtime.InteropServices;
using AmpUp.Core;
using AmpUp.Core.Models;
using Microsoft.Win32;

namespace AmpUp.Core.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class VoiceMeeterIntegration : IDisposable
{
    private bool _loggedIn;
    private volatile bool _available;
    private volatile bool _connected;
    private bool _enabled;
    private volatile bool _disposed;
    private string? _dllPath;
    private string _installDirectory = "";
    private readonly object _lock = new();
    private readonly Func<bool> _discoverDll;
    private readonly Func<int> _login;
    private readonly Func<int> _logout;
    private readonly Func<int> _refreshParameters;
    private Task? _reconnectTask;
    private CancellationTokenSource? _cts;

    public bool IsAvailable => _available;
    public bool IsConnected => _connected;
    public event Action? StateChanged;
    public string? DllPath => _dllPath;
    public bool RequiresRestart
    {
        get
        {
            lock (_lock)
                return _available && !string.IsNullOrWhiteSpace(_installDirectory)
                    && !string.Equals(_dllPath, System.IO.Path.Combine(_installDirectory, DllName),
                        StringComparison.OrdinalIgnoreCase);
        }
    }
    public event Action<bool, int, bool>? MuteStateChanged;

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

    public VoiceMeeterIntegration(string? installDirectory = null)
        : this(installDirectory, null, VBVMR_Login, VBVMR_Logout, VBVMR_IsParametersDirty)
    {
    }

    // Keep lifecycle checks testable without loading the native API or opening audio.
    internal VoiceMeeterIntegration(Func<bool>? discoverDll, Func<int> login,
        Func<int> logout, Func<int> refreshParameters)
        : this(null, discoverDll, login, logout, refreshParameters)
    {
    }

    private VoiceMeeterIntegration(string? installDirectory, Func<bool>? discoverDll, Func<int> login,
        Func<int> logout, Func<int> refreshParameters)
    {
        _installDirectory = installDirectory?.Trim() ?? "";
        _discoverDll = discoverDll ?? (() => CheckDllAvailable(out _dllPath, _installDirectory));
        _login = login;
        _logout = logout;
        _refreshParameters = refreshParameters;
        _available = _discoverDll();
        if (_available)
            Logger.Log($"VoiceMeeter: DLL found at '{_dllPath}', integration available");
        else
            Logger.Log("VoiceMeeter: DLL not found in the registry, standard install folders, or PATH; integration unavailable");
    }

    public void SetInstallDirectory(string? installDirectory)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var directory = installDirectory?.Trim() ?? "";
            if (string.Equals(_installDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
            _installDirectory = directory;
            // An already loaded native API remains in use until process restart.
            // If discovery had failed, the chosen folder can be tried immediately.
            if (!_available && _enabled) RefreshConnection();
            StateChanged?.Invoke();
        }
    }

    private static bool CheckDllAvailable(out string? dllPath, string? installDirectory = null)
    {
        dllPath = null;

        foreach (var installDir in GetInstallDirs(installDirectory))
        {
            var candidate = System.IO.Path.Combine(installDir, DllName);
            if (!System.IO.File.Exists(candidate))
                continue;

            try
            {
                // Load without registering a Remote API client. Windows keeps
                // the module available to the subsequent P/Invoke bindings.
                NativeLibrary.Load(candidate);
                dllPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException
                                       or BadImageFormatException
                                       or EntryPointNotFoundException)
            {
                Logger.Log($"VoiceMeeter: Could not load '{candidate}': {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(installDirectory)) return false;

        try
        {
            // Fallback: try loading directly (might be in PATH)
            NativeLibrary.Load(DllName);
            dllPath = DllName;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or BadImageFormatException
                                   or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GetInstallDirs(string? installDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(installDirectory))
            return new[] { installDirectory };

        const string uninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}";

        var candidates = new List<string>();

        // Match VB-Audio's official SDK lookup: check the native view, then force
        // the 32-bit registry view. The latter is where 64-bit AmpUp normally finds
        // VoiceMeeter/Potato's installer entry.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(uninstallKey);
                var installLocation = key?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrWhiteSpace(installLocation))
                    candidates.Add(Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"')));
                var installDir = GetDirectoryFromUninstallString(key?.GetValue("UninstallString")?.ToString());
                if (!string.IsNullOrWhiteSpace(installDir))
                    candidates.Add(installDir);
            }
            catch
            {
                // Registry access can be restricted; standard folders below remain valid fallbacks.
            }
        }

        AddStandardInstallDir(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddStandardInstallDir(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddStandardInstallDir(candidates, Environment.GetEnvironmentVariable("ProgramW6432"));

        // A running instance also identifies portable/custom installs that have no
        // installer entry. Potato's executable is commonly named voicemeeter8.
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.ProcessName.StartsWith("voicemeeter", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var directory = System.IO.Path.GetDirectoryName(process.MainModule?.FileName);
                    if (!string.IsNullOrWhiteSpace(directory)) candidates.Add(directory);
                }
                catch { } // Other users' processes can deny access to MainModule.
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddStandardInstallDir(List<string> candidates, string? programFilesDir)
    {
        if (!string.IsNullOrWhiteSpace(programFilesDir))
            candidates.Add(System.IO.Path.Combine(programFilesDir, "VB", "Voicemeeter"));
    }

    private static string? GetDirectoryFromUninstallString(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;

        var command = Environment.ExpandEnvironmentVariables(uninstallString.Trim());
        string executablePath;

        if (command.StartsWith('"'))
        {
            int closingQuote = command.IndexOf('"', 1);
            executablePath = closingQuote > 1 ? command[1..closingQuote] : command.Trim('"');
        }
        else
        {
            int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            executablePath = exeEnd >= 0 ? command[..(exeEnd + 4)] : command;
        }

        return System.IO.Path.GetDirectoryName(executablePath);
    }

    public bool Connect()
    {
        lock (_lock)
        {
            if (_disposed) return false;
            _enabled = true;
            // Start monitoring even if the DLL or VoiceMeeter is not ready yet.
            StartReconnectMonitor();
            return RefreshConnection();
        }
    }

    private bool RefreshConnection()
    {
        // Caller holds _lock; login, logout and parameter refresh cannot overlap.
        if (!_enabled || _disposed) return false;
        if (!_available)
        {
            _available = _discoverDll();
            if (!_available) return false;
            Logger.Log($"VoiceMeeter: DLL found at '{_dllPath}'");
            StateChanged?.Invoke();
        }

        try
        {
            if (!_loggedIn)
            {
                int result = _login();
                // Both 0 and 1 register the client. 1 does not launch VoiceMeeter.
                if (result is not (0 or 1))
                {
                    SetConnected(false);
                    return false;
                }
                _loggedIn = true;
                Logger.Log($"VoiceMeeter: Login successful (result={result})");
            }

            // The same login survives VoiceMeeter closing and restarting.
            // A nonnegative refresh result means the engine is responding.
            SetConnected(_refreshParameters() >= 0);
        }
        catch (Exception ex)
        {
            Logger.Log($"VoiceMeeter: Connection check failed: {ex.Message}");
            SetConnected(false);
        }
        return _connected;
    }

    private void SetConnected(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected;
        Logger.Log(connected ? "VoiceMeeter: Connected" : "VoiceMeeter: Waiting for engine");
        StateChanged?.Invoke();
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _enabled = false;
            _cts?.Cancel();
            if (_loggedIn)
            {
                try { _logout(); } catch { }
                _loggedIn = false;
                Logger.Log("VoiceMeeter: Logged out");
            }
            SetConnected(false);
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
        return ToggleMute($"Strip[{stripIdx}].Mute", $"strip {stripIdx}",
            muted => MuteStateChanged?.Invoke(true, stripIdx, muted));
    }

    /// <summary>
    /// Toggle mute on a bus. Returns the new mute state (true=muted).
    /// </summary>
    public bool ToggleBusMute(int busIdx)
    {
        return ToggleMute($"Bus[{busIdx}].Mute", $"bus {busIdx}",
            muted => MuteStateChanged?.Invoke(false, busIdx, muted));
    }

    /// <summary>Read the current mute state for a VoiceMeeter strip.</summary>
    public bool TryGetStripMuted(int stripIdx, out bool muted)
        => TryGetMute($"Strip[{stripIdx}].Mute", out muted);

    /// <summary>Read the current mute state for a VoiceMeeter bus.</summary>
    public bool TryGetBusMuted(int busIdx, out bool muted)
        => TryGetMute($"Bus[{busIdx}].Mute", out muted);

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

    private bool ToggleMute(string parameter, string description, Action<bool> onChanged)
    {
        if (!EnsureConnected()) return false;

        lock (_lock)
        {
            try
            {
                if (!TryRefreshParameters()) return false;

                int getResult = VBVMR_GetParameterFloat(parameter, out float current);
                if (getResult != 0)
                {
                    Logger.Log($"VoiceMeeter: Reading {description} mute failed (result={getResult})");
                    if (getResult < 0) HandleConnectionLost();
                    return false;
                }

                float newValue = current < 0.5f ? 1f : 0f;
                int setResult = VBVMR_SetParameterFloat(parameter, newValue);
                if (setResult != 0)
                {
                    Logger.Log($"VoiceMeeter: Toggling {description} mute failed (result={setResult})");
                    if (setResult < 0) HandleConnectionLost();
                    return false;
                }

                bool muted = newValue > 0.5f;
                Logger.Log($"VoiceMeeter: {description} mute -> {(muted ? "on" : "off")}");
                onChanged(muted);
                return muted;
            }
            catch (Exception ex)
            {
                Logger.Log($"VoiceMeeter: Toggling {description} mute failed: {ex.Message}");
                HandleConnectionLost();
                return false;
            }
        }
    }

    private bool TryGetMute(string parameter, out bool muted)
    {
        muted = false;
        if (!EnsureConnected()) return false;

        lock (_lock)
        {
            try
            {
                if (!TryRefreshParameters()) return false;

                int result = VBVMR_GetParameterFloat(parameter, out float value);
                if (result != 0)
                {
                    if (result < 0) HandleConnectionLost();
                    return false;
                }

                muted = value >= 0.5f;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"VoiceMeeter: Reading '{parameter}' failed: {ex.Message}");
                HandleConnectionLost();
                return false;
            }
        }
    }

    private bool TryRefreshParameters()
    {
        int result = _refreshParameters();
        if (result >= 0) return true;

        Logger.Log($"VoiceMeeter: Parameter refresh failed (result={result})");
        HandleConnectionLost();
        return false;
    }

    private bool EnsureConnected()
    {
        lock (_lock)
        {
            if (!_enabled || _disposed) return false;
            return _connected || RefreshConnection();
        }
    }

    private void HandleConnectionLost()
    {
        lock (_lock) SetConnected(false);
    }

    private void StartReconnectMonitor()
    {
        if (_cts is { IsCancellationRequested: false }) return;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _reconnectTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(5000, token);
                    lock (_lock)
                    {
                        if (token.IsCancellationRequested || _disposed || !_enabled) return;
                        RefreshConnection();
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
