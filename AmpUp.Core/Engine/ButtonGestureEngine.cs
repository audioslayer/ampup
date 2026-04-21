using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Platform-agnostic gesture state machine for button input.
/// Detects tap, double-press, and hold gestures from raw down/up events
/// and fires events for the host to execute platform-specific actions.
/// </summary>
public class ButtonGestureEngine : IDisposable
{
    private const int HoldThresholdMs = 500;
    private const int DoubleClickWindowMs = 300;

    private readonly object _stateLock = new();
    private readonly Dictionary<int, DateTime> _downAt = new();
    private readonly Dictionary<int, int> _clickCount = new();
    private readonly HashSet<int> _held = new();
    private readonly Dictionary<int, System.Threading.Timer?> _holdTimers = new();
    private readonly Dictionary<int, System.Threading.Timer?> _clickTimers = new();

    // Stash config ref for timer callbacks so timer threads see the latest button bindings.
    private volatile AppConfig? _lastConfig;

    /// <summary>
    /// Optional process-wide override for button resolution. When set, this delegate
    /// takes priority over the default search in AppConfig.Buttons / AppConfig.N3.Buttons.
    /// Used by folder navigation to route N3 key presses to folder-scoped buttons.
    /// Return null from the delegate to fall back to the default resolver.
    /// Static so host code that doesn't hold a direct ButtonGestureEngine reference
    /// (e.g. via ButtonHandler's private field) can still wire this up.
    /// </summary>
    public static Func<int, ButtonConfig?>? ButtonResolverOverride { get; set; }

    /// <summary>
    /// Fires when a gesture resolves to an action.
    /// Parameters: (buttonIdx, gesture ["tap"/"double"/"hold"], actionString, resolvedButtonConfig)
    /// The resolvedButtonConfig has Action/Path/DeviceId etc. already mapped from the correct gesture fields.
    /// </summary>
    public event Action<int, string, string, ButtonConfig>? OnGestureAction;

    /// <summary>Fires with profile name when switch_profile action is detected.</summary>
    public event Action<string>? OnProfileSwitch;

    /// <summary>Fires with (deviceName, isOutput) when the default audio device changes.</summary>
    public event Action<string, bool>? OnDeviceSwitched;

    /// <summary>Fires with new brightness percentage when cycle_brightness is triggered.</summary>
    public event Action<int>? OnBrightnessCycle;

    public void RaiseProfileSwitch(string profileName) => OnProfileSwitch?.Invoke(profileName);

    public void RaiseDeviceSwitched(string deviceName, bool isOutput) => OnDeviceSwitched?.Invoke(deviceName, isOutput);

    public void RaiseBrightnessCycle(int brightness) => OnBrightnessCycle?.Invoke(brightness);

    public void HandleDown(int idx, AppConfig config)
    {
        if (idx < 0) return;

        _lastConfig = config;
        lock (_stateLock)
        {
            _downAt[idx] = DateTime.UtcNow;
            _held.Remove(idx);

            DisposeTimer(_holdTimers, idx);
            _holdTimers[idx] = new System.Threading.Timer(_ => OnHoldFired(idx), null, HoldThresholdMs, Timeout.Infinite);
        }
    }

    public void HandleUp(int idx, AppConfig config)
    {
        if (idx < 0) return;

        _lastConfig = config;

        bool wasHeld;
        lock (_stateLock)
        {
            DisposeTimer(_holdTimers, idx);

            wasHeld = _held.Contains(idx);
            if (wasHeld)
            {
                _held.Remove(idx);
                return;
            }

            _clickCount[idx] = _clickCount.GetValueOrDefault(idx) + 1;

            DisposeTimer(_clickTimers, idx);
            _clickTimers[idx] = new System.Threading.Timer(_ => OnClickWindowExpired(idx), null, DoubleClickWindowMs, Timeout.Infinite);
        }
    }

    private void OnHoldFired(int idx)
    {
        ButtonConfig? holdBtn = null;
        string action = "none";

        lock (_stateLock)
        {
            DisposeTimer(_holdTimers, idx);
            _held.Add(idx);

            DisposeTimer(_clickTimers, idx);
            _clickCount[idx] = 0;

            var btn = ResolveButtonConfig(idx);
            if (btn != null)
            {
                action = btn.HoldAction ?? "none";
                if (!string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
                {
                    holdBtn = new ButtonConfig
                    {
                        Idx = btn.Idx,
                        Action = action,
                        Path = btn.HoldPath ?? "",
                        DeviceId = btn.HoldDeviceId,
                        DeviceIds = btn.HoldDeviceIds,
                        MacroKeys = btn.HoldMacroKeys,
                        ProfileName = btn.HoldProfileName,
                        ProfileNames = btn.HoldProfileNames,
                        PowerAction = btn.HoldPowerAction,
                        LinkedKnobIdx = btn.HoldLinkedKnobIdx,
                        CycleDeviceType = btn.HoldCycleDeviceType,
                        // Fall back to the tap FolderName if the hold-specific one is empty
                        // so upgrades from earlier builds don't lose their binding.
                        FolderName = !string.IsNullOrEmpty(btn.HoldFolderName) ? btn.HoldFolderName : btn.FolderName,
                    };
                }
            }
        }

        if (holdBtn != null)
        {
            OnGestureAction?.Invoke(idx, "hold", action, holdBtn);
        }
    }

    private void OnClickWindowExpired(int idx)
    {
        int clicks;
        ButtonConfig? tapBtn = null;
        ButtonConfig? dblBtn = null;
        string tapAction = "none";
        string dblAction = "none";

        lock (_stateLock)
        {
            DisposeTimer(_clickTimers, idx);

            clicks = _clickCount.GetValueOrDefault(idx);
            _clickCount[idx] = 0;

            var btn = ResolveButtonConfig(idx);
            if (btn != null)
            {
                if (clicks >= 2)
                {
                    dblAction = btn.DoublePressAction ?? "none";
                    if (!string.Equals(dblAction, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        dblBtn = new ButtonConfig
                        {
                            Idx = btn.Idx,
                            Action = dblAction,
                            Path = btn.DoublePressPath ?? "",
                            DeviceId = btn.DoublePressDeviceId,
                            DeviceIds = btn.DoublePressDeviceIds,
                            MacroKeys = btn.DoublePressMacroKeys,
                            ProfileName = btn.DoublePressProfileName,
                            ProfileNames = btn.DoublePressProfileNames,
                            PowerAction = btn.DoublePressPowerAction,
                            LinkedKnobIdx = btn.DoublePressLinkedKnobIdx,
                            CycleDeviceType = btn.DoublePressCycleDeviceType,
                            FolderName = !string.IsNullOrEmpty(btn.DoublePressFolderName) ? btn.DoublePressFolderName : btn.FolderName,
                        };
                    }
                }
                else
                {
                    tapAction = btn.Action ?? "none";
                    if (!string.Equals(tapAction, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        tapBtn = btn;
                    }
                }
            }
        }

        if (dblBtn != null)
        {
            OnGestureAction?.Invoke(idx, "double", dblAction, dblBtn);
        }
        else if (tapBtn != null)
        {
            OnGestureAction?.Invoke(idx, "tap", tapAction, tapBtn);
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            foreach (int idx in _holdTimers.Keys.ToList())
            {
                DisposeTimer(_holdTimers, idx);
            }
            foreach (int idx in _clickTimers.Keys.ToList())
            {
                DisposeTimer(_clickTimers, idx);
            }
        }
    }

    private ButtonConfig? ResolveButtonConfig(int idx)
    {
        var over = ButtonResolverOverride;
        if (over != null)
        {
            var fromOverride = over(idx);
            if (fromOverride != null) return fromOverride;
        }

        return _lastConfig?.Buttons.FirstOrDefault(b => b.Idx == idx)
            ?? _lastConfig?.N3.Buttons.FirstOrDefault(b => b.Idx == idx);
    }

    private static void DisposeTimer(Dictionary<int, System.Threading.Timer?> timers, int idx)
    {
        if (timers.TryGetValue(idx, out var timer))
        {
            timer?.Dispose();
            timers.Remove(idx);
        }
    }
}
