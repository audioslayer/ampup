using NAudio.CoreAudioApi;

namespace AmpUp;

public class DuckingConfig
{
    public bool Enabled { get; set; } = false;
    public List<DuckingRule> Rules { get; set; } = new();
}

public class DuckingRule
{
    public string TriggerApp { get; set; } = "";           // process name that triggers ducking (e.g. "discord")
    public List<string> TargetApps { get; set; } = new();  // apps to duck (empty = duck all non-trigger)
    public int DuckPercent { get; set; } = 50;             // reduce volume by this % (0-100)
    public int FadeInMs { get; set; } = 500;               // ms to fade volume back up after trigger stops
    public int FadeOutMs { get; set; } = 200;              // ms to fade volume down when trigger starts
    public float ActivationThreshold { get; set; } = 0.01f;// audio level to consider trigger 'active'
}

public class DuckingEngine : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _lock = new();

    // Per-rule state
    private readonly Dictionary<int, RuleState> _ruleStates = new();

    private bool _disposed;

    private class SessionInfo
    {
        public string ProcessName { get; set; } = "";
        public float OriginalVolume { get; set; }
        public float CurrentDuckedVolume { get; set; }
        public bool IsDucked { get; set; }
    }

    private class RuleState
    {
        public bool TriggerActive { get; set; }
        // processName (lowercase) -> session info for currently tracked targets
        public Dictionary<string, SessionInfo> TrackedSessions { get; set; } = new();
        // Timestamps for fade tracking
        public DateTime FadeStartTime { get; set; } = DateTime.MinValue;
        public FadeDirection FadeDir { get; set; } = FadeDirection.None;
        public int FadeMs { get; set; }
        // Volume at fade start (per-session stored in SessionInfo)
        public bool FadeComplete { get; set; } = true;
    }

    private enum FadeDirection { None, FadeOut, FadeIn }

    public void Poll(DuckingConfig config)
    {
        if (!config.Enabled || config.Rules.Count == 0) return;

        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                // Enumerate sessions once per Poll call
                var sessions = EnumerateSessions();

                for (int i = 0; i < config.Rules.Count; i++)
                {
                    var rule = config.Rules[i];
                    if (string.IsNullOrWhiteSpace(rule.TriggerApp)) continue;

                    if (!_ruleStates.TryGetValue(i, out var state))
                    {
                        state = new RuleState();
                        _ruleStates[i] = state;
                    }

                    PollRule(rule, state, sessions);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DuckingEngine.Poll error: {ex.Message}");
            }
        }
    }

    private void PollRule(DuckingRule rule, RuleState state, Dictionary<string, (AudioSessionControl Session, float Peak)> sessions)
    {
        string trigger = rule.TriggerApp.ToLowerInvariant();

        // Check if trigger app is active and has audio above threshold
        bool triggerNowActive = false;
        foreach (var kv in sessions)
        {
            if (kv.Key.Contains(trigger))
            {
                if (kv.Value.Peak >= rule.ActivationThreshold)
                {
                    triggerNowActive = true;
                    break;
                }
            }
        }

        bool wasActive = state.TriggerActive;

        if (triggerNowActive && !wasActive)
        {
            // Trigger just became active — start fade-out on targets
            state.TriggerActive = true;
            CaptureTargetVolumes(rule, state, sessions);
            BeginFade(state, FadeDirection.FadeOut, rule.FadeOutMs);
            Logger.Log($"DuckingEngine: trigger '{rule.TriggerApp}' active — ducking {state.TrackedSessions.Count} session(s) by {rule.DuckPercent}%");
        }
        else if (!triggerNowActive && wasActive)
        {
            // Trigger just stopped — start fade-in (restore)
            state.TriggerActive = false;
            BeginFade(state, FadeDirection.FadeIn, rule.FadeInMs);
            Logger.Log($"DuckingEngine: trigger '{rule.TriggerApp}' stopped — restoring volumes");
        }

        // Advance any in-progress fade
        if (!state.FadeComplete)
        {
            ApplyFadeStep(rule, state);
        }
    }

    private void CaptureTargetVolumes(DuckingRule rule, RuleState state, Dictionary<string, (AudioSessionControl Session, float Peak)> sessions)
    {
        string trigger = rule.TriggerApp.ToLowerInvariant();

        // Remove stale sessions that are no longer present
        var toRemove = state.TrackedSessions.Keys
            .Where(k => !sessions.ContainsKey(k))
            .ToList();
        foreach (var k in toRemove)
            state.TrackedSessions.Remove(k);

        bool duckAll = rule.TargetApps.Count == 0;

        foreach (var kv in sessions)
        {
            string procName = kv.Key;

            // Never duck the trigger app itself
            if (procName.Contains(trigger)) continue;

            // If TargetApps specified, only duck matching apps
            if (!duckAll)
            {
                bool matched = rule.TargetApps.Any(t => procName.Contains(t.ToLowerInvariant()));
                if (!matched) continue;
            }

            // Skip system sounds (PID 0 sessions — already filtered in EnumerateSessions)

            float currentVol;
            try { currentVol = kv.Value.Session.SimpleAudioVolume.Volume; }
            catch { continue; }

            if (!state.TrackedSessions.TryGetValue(procName, out var info))
            {
                info = new SessionInfo { ProcessName = procName };
                state.TrackedSessions[procName] = info;
            }

            // Only re-capture original if not currently ducked
            if (!info.IsDucked)
                info.OriginalVolume = currentVol;

            info.CurrentDuckedVolume = currentVol; // starting point for fade
        }
    }

    private static void BeginFade(RuleState state, FadeDirection dir, int durationMs)
    {
        state.FadeDir = dir;
        state.FadeMs = Math.Max(1, durationMs);
        state.FadeStartTime = DateTime.UtcNow;
        state.FadeComplete = false;

        // Snapshot current volume as fade-start for each session
        foreach (var info in state.TrackedSessions.Values)
        {
            // CurrentDuckedVolume holds the current real volume at fade start
            // (already set in CaptureTargetVolumes or previous fade step)
        }
    }

    private void ApplyFadeStep(DuckingRule rule, RuleState state)
    {
        double elapsed = (DateTime.UtcNow - state.FadeStartTime).TotalMilliseconds;
        float t = Math.Clamp((float)(elapsed / state.FadeMs), 0f, 1f);

        // Enumerate fresh sessions to get current AudioSessionControl references
        Dictionary<string, (AudioSessionControl Session, float Peak)> liveSessions;
        try { liveSessions = EnumerateSessions(); }
        catch { return; }

        bool allDone = true;
        string trigger = rule.TriggerApp.ToLowerInvariant();

        foreach (var info in state.TrackedSessions.Values)
        {
            if (!liveSessions.TryGetValue(info.ProcessName, out var live))
            {
                // Session disappeared — mark as not ducked so it's not restored
                info.IsDucked = false;
                continue;
            }

            float targetVol;
            float startVol;

            if (state.FadeDir == FadeDirection.FadeOut)
            {
                // Fade from original down to ducked level
                float duckedTarget = info.OriginalVolume * (1f - rule.DuckPercent / 100f);
                startVol = info.OriginalVolume;
                targetVol = duckedTarget;
                info.IsDucked = true;
            }
            else
            {
                // Fade from current (ducked) back up to original
                startVol = info.CurrentDuckedVolume;
                targetVol = info.OriginalVolume;
            }

            float newVol = Lerp(startVol, targetVol, t);

            try
            {
                live.Session.SimpleAudioVolume.Volume = Math.Clamp(newVol, 0f, 1f);
                info.CurrentDuckedVolume = newVol;
            }
            catch { }

            if (t < 1f) allDone = false;
        }

        if (t >= 1f || allDone)
        {
            state.FadeComplete = true;

            if (state.FadeDir == FadeDirection.FadeIn)
            {
                // Fully restored — clear tracking so fresh capture next time
                foreach (var info in state.TrackedSessions.Values)
                    info.IsDucked = false;
            }
        }
    }

    /// <summary>
    /// Enumerate all non-system audio sessions on the default render endpoint.
    /// Returns processName (lowercase) -> (session, peak level).
    /// </summary>
    private Dictionary<string, (AudioSessionControl Session, float Peak)> EnumerateSessions()
    {
        var result = new Dictionary<string, (AudioSessionControl, float)>();

        var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var dev = device;
        var mgr = dev.AudioSessionManager;
        var sessions = mgr.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                var pid = (int)s.GetProcessID;
                if (pid == 0) continue; // skip system sounds

                var proc = System.Diagnostics.Process.GetProcessById(pid);
                var name = proc.ProcessName.ToLowerInvariant();

                float peak = 0f;
                try { peak = s.AudioMeterInformation.MasterPeakValue; } catch { }

                if (!result.ContainsKey(name))
                    result[name] = (s, peak);
            }
            catch { }
        }

        return result;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
        _enumerator.Dispose();
    }
}
