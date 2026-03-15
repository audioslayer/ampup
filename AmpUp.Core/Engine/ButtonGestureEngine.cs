using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Platform-agnostic gesture state machine for button input.
/// Detects tap, double-press, and hold gestures from raw down/up events
/// and fires events for the host to execute platform-specific actions.
/// </summary>
public class ButtonGestureEngine : IDisposable
{
    // ── Gesture state machine constants ───────────────────────────────

    private const int HoldThresholdMs = 500;
    private const int DoubleClickWindowMs = 300;

    // ── Per-button gesture state ─────────────────────────────────────

    private readonly DateTime[] _downAt = new DateTime[5];
    private readonly int[] _clickCount = new int[5];
    private readonly bool[] _held = new bool[5];
    private readonly System.Threading.Timer?[] _holdTimers = new System.Threading.Timer?[5];
    private readonly System.Threading.Timer?[] _clickTimers = new System.Threading.Timer?[5];

    // Stash config ref for timer callbacks — volatile so timer threads see the latest value
    private volatile AppConfig? _lastConfig;

    // ── Events ────────────────────────────────────────────────────────

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

    // ── Event invocation helpers (for use by action executor) ────────

    /// <summary>Raise OnProfileSwitch from action execution layer.</summary>
    public void RaiseProfileSwitch(string profileName) => OnProfileSwitch?.Invoke(profileName);

    /// <summary>Raise OnDeviceSwitched from action execution layer.</summary>
    public void RaiseDeviceSwitched(string deviceName, bool isOutput) => OnDeviceSwitched?.Invoke(deviceName, isOutput);

    /// <summary>Raise OnBrightnessCycle from action execution layer.</summary>
    public void RaiseBrightnessCycle(int brightness) => OnBrightnessCycle?.Invoke(brightness);

    // ── Gesture state machine ─────────────────────────────────────────

    public void HandleDown(int idx, AppConfig config)
    {
        if (idx < 0 || idx > 4) return;
        _lastConfig = config;
        _downAt[idx] = DateTime.UtcNow;
        _held[idx] = false;

        // Start hold timer — fires if button stays down for 500ms
        _holdTimers[idx]?.Dispose();
        _holdTimers[idx] = new System.Threading.Timer(_ => OnHoldFired(idx), null, HoldThresholdMs, Timeout.Infinite);
    }

    public void HandleUp(int idx, AppConfig config)
    {
        if (idx < 0 || idx > 4) return;
        _lastConfig = config;

        // Cancel hold timer
        _holdTimers[idx]?.Dispose();
        _holdTimers[idx] = null;

        // If hold already fired, consume the release
        if (_held[idx])
        {
            _held[idx] = false;
            return;
        }

        // Short press — count it for single/double detection
        _clickCount[idx]++;

        // Restart the double-click window timer
        _clickTimers[idx]?.Dispose();
        _clickTimers[idx] = new System.Threading.Timer(_ => OnClickWindowExpired(idx), null, DoubleClickWindowMs, Timeout.Infinite);
    }

    private void OnHoldFired(int idx)
    {
        _holdTimers[idx]?.Dispose();
        _holdTimers[idx] = null;
        _held[idx] = true;

        // Cancel any pending click detection (hold wins)
        _clickTimers[idx]?.Dispose();
        _clickTimers[idx] = null;
        _clickCount[idx] = 0;

        var btn = _lastConfig?.Buttons.FirstOrDefault(b => b.Idx == idx);
        if (btn == null) return;

        var action = btn.HoldAction ?? "none";
        var path = btn.HoldPath ?? "";
        if (!string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"Button {idx} hold → action: {action}");
            var holdBtn = new ButtonConfig
            {
                Idx = btn.Idx,
                Action = action,
                Path = path,
                DeviceId = btn.HoldDeviceId,
                DeviceIds = btn.HoldDeviceIds,
                MacroKeys = btn.HoldMacroKeys,
                ProfileName = btn.HoldProfileName,
                PowerAction = btn.HoldPowerAction,
                LinkedKnobIdx = btn.HoldLinkedKnobIdx
            };
            OnGestureAction?.Invoke(idx, "hold", action, holdBtn);
        }
    }

    private void OnClickWindowExpired(int idx)
    {
        _clickTimers[idx]?.Dispose();
        _clickTimers[idx] = null;

        int clicks = _clickCount[idx];
        _clickCount[idx] = 0;

        var btn = _lastConfig?.Buttons.FirstOrDefault(b => b.Idx == idx);
        if (btn == null) return;

        if (clicks >= 2)
        {
            var action = btn.DoublePressAction ?? "none";
            var path = btn.DoublePressPath ?? "";
            if (!string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Button {idx} double-press → action: {action}");
                var dblBtn = new ButtonConfig
                {
                    Idx = btn.Idx,
                    Action = action,
                    Path = path,
                    DeviceId = btn.DoublePressDeviceId,
                    DeviceIds = btn.DoublePressDeviceIds,
                    MacroKeys = btn.DoublePressMacroKeys,
                    ProfileName = btn.DoublePressProfileName,
                    PowerAction = btn.DoublePressPowerAction,
                    LinkedKnobIdx = btn.DoublePressLinkedKnobIdx
                };
                OnGestureAction?.Invoke(idx, "double", action, dblBtn);
            }
        }
        else
        {
            // Single press
            if (!string.Equals(btn.Action, "none", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Button {idx} press → action: {btn.Action}");
                OnGestureAction?.Invoke(idx, "tap", btn.Action, btn);
            }
        }
    }

    // ── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        for (int i = 0; i < 5; i++)
        {
            _holdTimers[i]?.Dispose();
            _holdTimers[i] = null;
            _clickTimers[i]?.Dispose();
            _clickTimers[i] = null;
        }
    }
}
