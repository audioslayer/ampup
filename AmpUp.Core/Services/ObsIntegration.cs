using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

/// <summary>
/// OBS Studio 28+ integration over obs-websocket v5. Keeps a live list of audio
/// sources and scenes (refreshed from OBS events), tracks source mute and
/// record/stream state, and drives source volume from knobs.
/// </summary>
public class ObsIntegration : IDisposable
{
    private ClientWebSocket? _ws;
    private ObsConfig _config;
    private string _connectionKey = "";
    private string? _lastLoggedError; // retries every 5 s while OBS is closed: log each failure once
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _reconnectTask;
    private volatile bool _disposed;
    private int _requestId;
    private readonly Dictionary<string, TaskCompletionSource<JObject>> _pending = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private TaskCompletionSource<bool>? _identified;

    // Live OBS state
    private IReadOnlyList<string> _audioSources = Array.Empty<string>();
    private IReadOnlyList<string> _scenes = Array.Empty<string>();
    private readonly Dictionary<string, bool> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _faders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Level, long Tick)> _meters = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _sceneItems = Array.Empty<string>();
    private IReadOnlyList<string> _filters = Array.Empty<string>();
    private int _listRefreshQueued;

    // EventSubscription::All (bits 0-11) plus the high-volume InputVolumeMeters (1 << 16)
    private const int EventSubscriptions = 0xFFF | (1 << 16);

    // Knob volume coalescing — latest value wins per source
    private readonly Dictionary<string, float> _pendingVolume = new(StringComparer.Ordinal);
    private readonly HashSet<string> _volumeInFlight = new(StringComparer.Ordinal);

    public bool IsAvailable { get; private set; }

    /// <summary>Why the last connection attempt failed, or null when connected / never tried.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while OBS is actively recording.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>True while OBS is actively streaming.</summary>
    public bool IsStreaming { get; private set; }

    /// <summary>True while a recording is paused.</summary>
    public bool IsRecordingPaused { get; private set; }

    /// <summary>True while the OBS virtual camera is on.</summary>
    public bool IsVirtualCamActive { get; private set; }

    /// <summary>True while the replay buffer is running (needed to save replays).</summary>
    public bool IsReplayBufferActive { get; private set; }

    /// <summary>"On air": streaming or recording.</summary>
    public bool IsLive => IsStreaming || IsRecording;

    /// <summary>The scene currently on program (live).</summary>
    public string CurrentScene { get; private set; } = "";

    /// <summary>Sources in each scene, as "Scene|Source" (for show / hide source actions).</summary>
    public IReadOnlyList<string> SceneItems => _sceneItems;

    /// <summary>Filters on every source and scene, as "Source|Filter" (for filter on / off actions).</summary>
    public IReadOnlyList<string> Filters => _filters;

    /// <summary>OBS inputs that have audio (mic, desktop audio, media, browser sources…), in OBS order.</summary>
    public IReadOnlyList<string> AudioSources => _audioSources;

    /// <summary>Scene names in the order OBS shows them.</summary>
    public IReadOnlyList<string> Scenes => _scenes;

    /// <summary>Connected / disconnected.</summary>
    public event Action? StateChanged;

    /// <summary>Audio source or scene list changed.</summary>
    public event Action? SourcesChanged;

    /// <summary>A source was muted or unmuted (source name, muted).</summary>
    public event Action<string, bool>? MuteChanged;

    /// <summary>Streaming, recording, record pause, virtual camera or replay buffer started / stopped.</summary>
    public event Action? OutputStateChanged;

    /// <summary>A replay was saved (file path).</summary>
    public event Action<string>? ReplaySaved;

    /// <summary>The program scene changed (scene name).</summary>
    public event Action<string>? SceneChanged;

    public ObsIntegration(ObsConfig config)
    {
        _config = config;
        _connectionKey = ConnectionKey(config);
    }

    private static string ConnectionKey(ObsConfig c) => $"{c.Host}|{c.Port}|{c.Password}";

    /// <summary>OBS's own fader scale (libobs log fader): 0 → silent, 0.5 → about -18.7 dB, 1 → 0 dB.
    /// Using it means the knob position matches the slider position in OBS's mixer.</summary>
    public static float NormalizedToDb(float normalized)
    {
        const double offsetDb = 6.0, rangeDb = 96.0;
        if (normalized >= 1f) return 0f;
        if (normalized <= 0f) return -100f;
        double db = -(rangeDb + offsetDb) * Math.Pow((rangeDb + offsetDb) / offsetDb, -normalized) + offsetDb;
        return (float)Math.Max(db, -100.0);
    }

    /// <summary>Inverse of <see cref="NormalizedToDb"/>: OBS dB to fader position 0-1.</summary>
    public static float DbToNormalized(double db)
    {
        const double offsetDb = 6.0, rangeDb = 96.0;
        if (db >= 0) return 1f;
        if (double.IsNaN(db) || db <= -100) return 0f;
        double def = -Math.Log((offsetDb - db) / (rangeDb + offsetDb)) / Math.Log((rangeDb + offsetDb) / offsetDb);
        return (float)Math.Clamp(def, 0.0, 1.0);
    }

    /// <summary>A source's fader position (0-1), as last reported by OBS or set by AmpUp.</summary>
    public bool TryGetVolume(string source, out float normalized)
    {
        lock (_lock)
            return _faders.TryGetValue(source, out normalized);
    }

    /// <summary>
    /// Live post-fader level of a source on OBS's meter scale (-60 dB = 0, 0 dB = 1).
    /// 0 when the source is silent, inactive, or meters stopped arriving.
    /// </summary>
    public float GetMeterLevel(string source)
    {
        lock (_lock)
        {
            if (_meters.TryGetValue(source, out var m) && Environment.TickCount64 - m.Tick < 300)
                return m.Level;
        }
        return 0f;
    }

    public bool TryGetMuted(string source, out bool muted)
    {
        lock (_lock)
            return _muted.TryGetValue(source, out muted);
    }

    public void UpdateConfig(ObsConfig config)
    {
        _config = config;
        var key = ConnectionKey(config);
        bool keyChanged = key != _connectionKey;
        _connectionKey = key;

        if (!config.Enabled)
        {
            if (IsAvailable || _ws != null) _ = DisconnectAsync();
            return;
        }

        // Reconnect when the address/password changed, or when newly enabled and
        // no reconnect loop is already working on it.
        bool reconnecting = _reconnectTask != null && !_reconnectTask.IsCompleted;
        if (keyChanged || (!IsAvailable && !reconnecting))
            _ = ConnectAsync();
    }

    public async Task<bool> ConnectAsync()
    {
        if (!_config.Enabled || _disposed) return false;

        await _connectLock.WaitAsync();
        try
        {
            await DisconnectAsync();

            LastError = null;
            _cts = new CancellationTokenSource();
            var identified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _identified = identified;
            var host = string.IsNullOrWhiteSpace(_config.Host) ? "localhost" : _config.Host.Trim();
            try
            {
                var ws = new ClientWebSocket();
                _ws = ws;
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                connectTimeout.CancelAfter(4000);
                await ws.ConnectAsync(new Uri($"ws://{host}:{_config.Port}"), connectTimeout.Token);

                // Receive loop handles Hello → Identify → Identified
                _receiveTask = ReceiveLoopAsync(ws, _cts.Token);

                var done = await Task.WhenAny(identified.Task, Task.Delay(4000, _cts.Token));
                if (done != identified.Task || !identified.Task.Result)
                {
                    LastError ??= "OBS didn't finish the handshake";
                    LogConnectError($"OBS connect: {LastError}");
                    IsAvailable = false;
                    StartReconnectLoop();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex is OperationCanceledException
                    ? $"No answer from {host}:{_config.Port}"
                    : $"Can't reach OBS at {host}:{_config.Port}";
                LogConnectError($"OBS connect failed: {LastError} ({ex.Message})");
                IsAvailable = false;
                StartReconnectLoop();
                return false;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        bool wasAvailable = IsAvailable;
        IsAvailable = false;
        IsRecording = false;
        IsStreaming = false;
        IsRecordingPaused = false;
        IsVirtualCamActive = false;
        IsReplayBufferActive = false;
        _cts?.Cancel();
        _identified?.TrySetResult(false);

        var ws = _ws;
        _ws = null;
        if (ws != null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
        }

        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
        }

        if (wasAvailable) StateChanged?.Invoke();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // 4009 = AuthenticationFailed in obs-websocket v5
                    if ((int?)result.CloseStatus == 4009)
                        LastError = string.IsNullOrEmpty(_config.Password)
                            ? "OBS needs a password (Tools → WebSocket Server Settings)"
                            : "OBS rejected the password";
                    else if (!string.IsNullOrEmpty(result.CloseStatusDescription))
                        LastError = result.CloseStatusDescription;
                    OnConnectionLost();
                    return;
                }

                try
                {
                    HandleMessage(JObject.Parse(sb.ToString()), ws, ct);
                }
                catch (Exception ex)
                {
                    Logger.Log($"OBS message parse error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                if (ex is not WebSocketException)
                    Logger.Log($"OBS receive loop error: {ex.Message}");
                OnConnectionLost();
            }
        }
    }

    private void LogConnectError(string message)
    {
        if (message == _lastLoggedError) return;
        _lastLoggedError = message;
        Logger.Log(message + " (retrying every 5 s; logged once)");
    }

    private void OnConnectionLost()
    {
        bool wasAvailable = IsAvailable;
        IsAvailable = false;
        _identified?.TrySetResult(false);
        if (wasAvailable)
        {
            Logger.Log("OBS WebSocket: connection lost");
            StateChanged?.Invoke();
        }
        StartReconnectLoop();
    }

    // Runs on the receive loop: must never await a request response here.
    private void HandleMessage(JObject msg, ClientWebSocket ws, CancellationToken ct)
    {
        var op = msg["op"]?.Value<int>() ?? -1;
        var d = msg["d"] as JObject;

        switch (op)
        {
            case 0: // Hello
                _ = SendIdentifyAsync(d, ws, ct);
                break;
            case 2: // Identified
                IsAvailable = true;
                LastError = null;
                _lastLoggedError = null;
                Logger.Log($"OBS WebSocket: connected to {_config.Host}:{_config.Port}");
                _identified?.TrySetResult(true);
                StateChanged?.Invoke();
                _ = Task.Run(async () =>
                {
                    await RefreshListsAsync();
                    await RefreshStatusAsync();
                });
                break;
            case 5: // Event
                HandleEvent(d);
                break;
            case 7: // RequestResponse
            {
                var requestId = d?["requestId"]?.Value<string>();
                if (requestId != null)
                {
                    TaskCompletionSource<JObject>? tcs;
                    lock (_lock)
                        _pending.Remove(requestId, out tcs);
                    tcs?.TrySetResult(d ?? new JObject());
                }
                break;
            }
        }
    }

    private void HandleEvent(JObject? d)
    {
        var type = d?["eventType"]?.Value<string>();
        var data = d?["eventData"];
        switch (type)
        {
            case "InputMuteStateChanged":
            {
                var name = data?["inputName"]?.Value<string>();
                var muted = data?["inputMuted"]?.Value<bool>() ?? false;
                if (name == null) break;
                lock (_lock) _muted[name] = muted;
                MuteChanged?.Invoke(name, muted);
                break;
            }
            case "InputVolumeMeters":
                UpdateMeters(data?["inputs"] as JArray);
                break;
            case "InputVolumeChanged":
            {
                var name = data?["inputName"]?.Value<string>();
                var db = data?["inputVolumeDb"]?.Value<double>();
                if (name != null && db.HasValue)
                    lock (_lock) _faders[name] = DbToNormalized(db.Value);
                break;
            }
            case "RecordStateChanged":
            {
                IsRecording = data?["outputActive"]?.Value<bool>() ?? IsRecording;
                var state = data?["outputState"]?.Value<string>();
                if (state == "OBS_WEBSOCKET_OUTPUT_PAUSED") IsRecordingPaused = true;
                else if (state == "OBS_WEBSOCKET_OUTPUT_RESUMED" || !IsRecording) IsRecordingPaused = false;
                OutputStateChanged?.Invoke();
                break;
            }
            case "StreamStateChanged":
                IsStreaming = data?["outputActive"]?.Value<bool>() ?? IsStreaming;
                OutputStateChanged?.Invoke();
                break;
            case "VirtualcamStateChanged":
                IsVirtualCamActive = data?["outputActive"]?.Value<bool>() ?? IsVirtualCamActive;
                OutputStateChanged?.Invoke();
                break;
            case "ReplayBufferStateChanged":
                IsReplayBufferActive = data?["outputActive"]?.Value<bool>() ?? IsReplayBufferActive;
                OutputStateChanged?.Invoke();
                break;
            case "ReplayBufferSaved":
                ReplaySaved?.Invoke(data?["savedReplayPath"]?.Value<string>() ?? "");
                break;
            case "CurrentProgramSceneChanged":
            {
                var scene = data?["sceneName"]?.Value<string>();
                if (scene != null)
                {
                    CurrentScene = scene;
                    SceneChanged?.Invoke(scene);
                }
                break;
            }
            case "SceneItemCreated":
            case "SceneItemRemoved":
            case "SourceFilterCreated":
            case "SourceFilterRemoved":
            case "SourceFilterNameChanged":
            case "InputCreated":
            case "InputRemoved":
            case "InputNameChanged":
            case "SceneCreated":
            case "SceneRemoved":
            case "SceneNameChanged":
            case "SceneListChanged":
            case "CurrentSceneCollectionChanged":
                QueueListRefresh();
                break;
        }
    }

    private void UpdateMeters(JArray? inputs)
    {
        if (inputs == null) return;
        long now = Environment.TickCount64;
        lock (_lock)
        {
            foreach (var input in inputs)
            {
                var name = input["inputName"]?.Value<string>();
                if (name == null) continue;
                // inputLevelsMul: one [magnitude, peak, inputPeak] triple per channel; peak is post-fader
                double peak = 0;
                if (input["inputLevelsMul"] is JArray channels)
                    foreach (var ch in channels)
                        if (ch is JArray levels && levels.Count > 1)
                            peak = Math.Max(peak, levels[1].Value<double>());
                float level = peak <= 0.001 ? 0f : (float)Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
                _meters[name] = (level, now);
            }
        }
    }

    private void QueueListRefresh()
    {
        if (Interlocked.Exchange(ref _listRefreshQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(400); // collapse bursts (scene collection switch fires dozens)
            Interlocked.Exchange(ref _listRefreshQueued, 0);
            await RefreshListsAsync();
        });
    }

    /// <summary>Re-reads the audio source and scene lists plus each source's mute state.</summary>
    public async Task RefreshListsAsync()
    {
        if (!IsAvailable) return;
        try
        {
            var inputsResp = await SendRequestAsync("GetInputList");
            var names = (inputsResp?["responseData"]?["inputs"] as JArray)?
                .Select(i => i["inputName"]?.Value<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .ToList() ?? new List<string>();

            // Only inputs with audio answer GetInputMute successfully — that's the
            // filter for "audio source" (video-only sources return an error).
            var muteReads = await Task.WhenAll(names.Select(async n =>
            {
                var r = await SendRequestAsync("GetInputMute", new JObject { ["inputName"] = n }, timeoutMs: 2000);
                bool ok = r?["requestStatus"]?["result"]?.Value<bool>() ?? false;
                bool muted = r?["responseData"]?["inputMuted"]?.Value<bool>() ?? false;
                double? db = null;
                if (ok)
                {
                    var v = await SendRequestAsync("GetInputVolume", new JObject { ["inputName"] = n }, timeoutMs: 2000);
                    db = v?["responseData"]?["inputVolumeDb"]?.Value<double>();
                }
                return (Name: n, IsAudio: ok, Muted: muted, Db: db);
            }));

            var scenesResp = await SendRequestAsync("GetSceneList");
            var scenes = (scenesResp?["responseData"]?["scenes"] as JArray)?
                .OrderByDescending(s => s["sceneIndex"]?.Value<int>() ?? 0) // OBS lists top-of-UI last
                .Select(s => s["sceneName"]?.Value<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .ToList() ?? new List<string>();

            if (inputsResp == null) return; // dropped mid-refresh; keep last-known lists

            var current = await SendRequestAsync("GetCurrentProgramScene", timeoutMs: 2000);
            var currentScene = current?["responseData"]?["sceneName"]?.Value<string>()
                ?? current?["responseData"]?["currentProgramSceneName"]?.Value<string>();
            if (currentScene != null) CurrentScene = currentScene;

            // Sources per scene and filters per source, for the show/hide and filter pickers
            var itemLists = await Task.WhenAll(scenes.Select(async sc =>
            {
                var r = await SendRequestAsync("GetSceneItemList", new JObject { ["sceneName"] = sc }, timeoutMs: 2000);
                return ((r?["responseData"]?["sceneItems"] as JArray) ?? new JArray())
                    .Select(i => i["sourceName"]?.Value<string>())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => $"{sc}|{n}")
                    .ToList();
            }));
            var filterLists = await Task.WhenAll(names.Concat(scenes).Distinct().Select(async src =>
            {
                var r = await SendRequestAsync("GetSourceFilterList", new JObject { ["sourceName"] = src }, timeoutMs: 2000);
                return ((r?["responseData"]?["filters"] as JArray) ?? new JArray())
                    .Select(f => f["filterName"]?.Value<string>())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => $"{src}|{n}")
                    .ToList();
            }));
            _sceneItems = itemLists.SelectMany(x => x).ToList();
            _filters = filterLists.SelectMany(x => x).ToList();

            var audio = new List<string>();
            var changedMutes = new List<(string, bool)>();
            lock (_lock)
            {
                foreach (var m in muteReads.Where(m => m.IsAudio))
                {
                    audio.Add(m.Name);
                    if (!_muted.TryGetValue(m.Name, out var prev) || prev != m.Muted)
                        changedMutes.Add((m.Name, m.Muted));
                    _muted[m.Name] = m.Muted;
                    if (m.Db.HasValue) _faders[m.Name] = DbToNormalized(m.Db.Value);
                }
            }
            _audioSources = audio;
            if (scenesResp != null) _scenes = scenes;

            foreach (var (name, muted) in changedMutes)
                MuteChanged?.Invoke(name, muted);
            SourcesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Log($"OBS list refresh failed: {ex.Message}");
        }
    }

    /// <summary>Polls OBS for the current recording/streaming state. OBS events keep these
    /// current while connected; the poll is a backstop.</summary>
    public async Task RefreshStatusAsync()
    {
        if (!IsAvailable) { IsRecording = false; IsStreaming = false; return; }

        try
        {
            var before = (IsRecording, IsStreaming, IsRecordingPaused, IsVirtualCamActive, IsReplayBufferActive);

            var rec = await SendRequestAsync("GetRecordStatus", timeoutMs: 2000);
            if (rec != null)
            {
                IsRecording = rec["responseData"]?["outputActive"]?.Value<bool>() ?? false;
                IsRecordingPaused = rec["responseData"]?["outputPaused"]?.Value<bool>() ?? false;
            }

            var stream = await SendRequestAsync("GetStreamStatus", timeoutMs: 2000);
            if (stream != null)
                IsStreaming = stream["responseData"]?["outputActive"]?.Value<bool>() ?? false;

            var vcam = await SendRequestAsync("GetVirtualCamStatus", timeoutMs: 2000);
            if (vcam != null)
                IsVirtualCamActive = vcam["responseData"]?["outputActive"]?.Value<bool>() ?? false;

            // Fails (no responseData) when the replay buffer isn't enabled in OBS's output settings
            var replay = await SendRequestAsync("GetReplayBufferStatus", timeoutMs: 2000);
            if (replay != null)
                IsReplayBufferActive = replay["responseData"]?["outputActive"]?.Value<bool>() ?? false;

            if (before != (IsRecording, IsStreaming, IsRecordingPaused, IsVirtualCamActive, IsReplayBufferActive))
                OutputStateChanged?.Invoke();
        }
        catch
        {
            // Silent — leave last-known values in place.
        }
    }

    private async Task SendIdentifyAsync(JObject? hello, ClientWebSocket ws, CancellationToken ct)
    {
        var auth = hello?["authentication"];
        var d = new JObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = EventSubscriptions };

        if (auth != null)
        {
            if (string.IsNullOrEmpty(_config.Password))
            {
                LastError = "OBS needs a password (Tools → WebSocket Server Settings)";
            }
            else
            {
                // OBS WebSocket v5 auth: SHA256 challenge-response
                var challenge = auth["challenge"]?.Value<string>() ?? "";
                var salt = auth["salt"]?.Value<string>() ?? "";
                d["authentication"] = ComputeAuth(_config.Password, salt, challenge);
            }
        }

        try
        {
            await SendAsync(ws, new JObject { ["op"] = 1, ["d"] = d }, ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"OBS identify failed: {ex.Message}");
        }
    }

    private static string ComputeAuth(string password, string salt, string challenge)
    {
        // Step 1: SHA256(password + salt) → base64
        using var sha256 = SHA256.Create();
        var secretHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        var secretBase64 = Convert.ToBase64String(secretHash);

        // Step 2: SHA256(base64_secret + challenge) → base64
        var authHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(secretBase64 + challenge));
        return Convert.ToBase64String(authHash);
    }

    // ClientWebSocket allows one outstanding send at a time; knob spins, status
    // polls and button presses all send concurrently, so serialize here.
    private async Task SendAsync(ClientWebSocket ws, JObject msg, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(msg.ToString(Formatting.None));
        await _sendLock.WaitAsync(ct);
        try
        {
            if (ws.State != WebSocketState.Open) return;
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<JObject?> SendRequestAsync(string requestType, JObject? requestData = null, int timeoutMs = 5000)
    {
        var ws = _ws;
        if (ws?.State != WebSocketState.Open || !IsAvailable) return null;

        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
            _pending[id] = tcs;

        var msg = new JObject
        {
            ["op"] = 6,
            ["d"] = new JObject
            {
                ["requestType"] = requestType,
                ["requestId"] = id,
                ["requestData"] = requestData ?? new JObject(),
            }
        };

        try
        {
            await SendAsync(ws, msg, _cts?.Token ?? CancellationToken.None);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed == tcs.Task)
                return await tcs.Task;

            lock (_lock)
                _pending.Remove(id);
            return null;
        }
        catch
        {
            lock (_lock)
                _pending.Remove(id);
            return null;
        }
    }

    // ── Reconnect loop ──────────────────────────────────────────────

    private void StartReconnectLoop()
    {
        if (_disposed || !_config.Enabled) return;
        if (_reconnectTask != null && !_reconnectTask.IsCompleted) return;

        _reconnectTask = Task.Run(async () =>
        {
            while (!_disposed && _config.Enabled && !IsAvailable)
            {
                await Task.Delay(5000);
                if (_disposed || !_config.Enabled || IsAvailable) return;

                try
                {
                    // ConnectAsync would re-enter StartReconnectLoop on failure; this
                    // task is still running so that call is a no-op and we loop here.
                    if (await ConnectAsync())
                    {
                        Logger.Log("OBS WebSocket: reconnected");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"OBS reconnect failed: {ex.Message}");
                }
            }
        });
    }

    // ── Public API methods ──────────────────────────────────────────

    public Task StartRecordingAsync() => SendRequestAsync("StartRecord");
    public Task StopRecordingAsync() => SendRequestAsync("StopRecord");
    public Task ToggleRecordingAsync() => SendRequestAsync("ToggleRecord");
    public Task StartStreamingAsync() => SendRequestAsync("StartStream");
    public Task StopStreamingAsync() => SendRequestAsync("StopStream");
    public Task ToggleStreamingAsync() => SendRequestAsync("ToggleStream");

    public Task SetSceneAsync(string sceneName) =>
        SendRequestAsync("SetCurrentProgramScene", new JObject { ["sceneName"] = sceneName });

    public Task ToggleVirtualCamAsync() => SendRequestAsync("ToggleVirtualCam");
    public Task ToggleReplayBufferAsync() => SendRequestAsync("ToggleReplayBuffer");
    public Task ToggleRecordPauseAsync() => SendRequestAsync("ToggleRecordPause");
    public Task TriggerStudioTransitionAsync() => SendRequestAsync("TriggerStudioModeTransition");

    /// <summary>Adds a chapter marker to the recording (OBS 30.2+, Hybrid MP4 recordings only).</summary>
    public async Task<bool> AddRecordChapterAsync()
    {
        var r = await SendRequestAsync("CreateRecordChapter");
        bool ok = r?["requestStatus"]?["result"]?.Value<bool>() ?? false;
        if (!ok && r != null)
            Logger.Log($"OBS chapter marker failed: {r["requestStatus"]?["comment"]} (needs OBS 30.2+ recording to Hybrid MP4)");
        return ok;
    }

    /// <summary>Saves the replay buffer. When the buffer isn't running there's nothing to save yet,
    /// so this starts it instead and the next press saves.</summary>
    public async Task<bool> SaveReplayAsync()
    {
        if (!IsReplayBufferActive)
        {
            var status = await SendRequestAsync("GetReplayBufferStatus", timeoutMs: 2000);
            IsReplayBufferActive = status?["responseData"]?["outputActive"]?.Value<bool>() ?? false;
        }
        if (!IsReplayBufferActive)
        {
            Logger.Log("OBS replay buffer isn't running, starting it");
            await SendRequestAsync("StartReplayBuffer");
            return false;
        }
        var r = await SendRequestAsync("SaveReplayBuffer");
        return r?["requestStatus"]?["result"]?.Value<bool>() ?? false;
    }

    /// <summary>Shows or hides a source in a scene. <paramref name="sceneName"/> empty = the live scene.</summary>
    public async Task ToggleSceneItemAsync(string sceneName, string sourceName)
    {
        if (string.IsNullOrEmpty(sceneName)) sceneName = CurrentScene;
        if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(sourceName)) return;

        var idResp = await SendRequestAsync("GetSceneItemId", new JObject { ["sceneName"] = sceneName, ["sourceName"] = sourceName });
        var id = idResp?["responseData"]?["sceneItemId"]?.Value<int>();
        if (id == null)
        {
            Logger.Log($"OBS: '{sourceName}' isn't in scene '{sceneName}'");
            return;
        }
        var item = new JObject { ["sceneName"] = sceneName, ["sceneItemId"] = id.Value };
        var en = await SendRequestAsync("GetSceneItemEnabled", item);
        bool enabled = en?["responseData"]?["sceneItemEnabled"]?.Value<bool>() ?? false;
        item["sceneItemEnabled"] = !enabled;
        await SendRequestAsync("SetSceneItemEnabled", item);
    }

    /// <summary>Turns a filter on a source (or scene) on or off.</summary>
    public async Task ToggleFilterAsync(string sourceName, string filterName)
    {
        if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(filterName)) return;
        var key = new JObject { ["sourceName"] = sourceName, ["filterName"] = filterName };
        var f = await SendRequestAsync("GetSourceFilter", key);
        if (f?["responseData"] == null)
        {
            Logger.Log($"OBS: no filter '{filterName}' on '{sourceName}'");
            return;
        }
        bool enabled = f["responseData"]?["filterEnabled"]?.Value<bool>() ?? false;
        key["filterEnabled"] = !enabled;
        await SendRequestAsync("SetSourceFilterEnabled", key);
    }

    public Task ToggleMuteAsync(string sourceName) =>
        SendRequestAsync("ToggleInputMute", new JObject { ["inputName"] = sourceName });

    /// <summary>
    /// Sets a source's volume from a 0-1 knob value on OBS's fader scale. Calls are
    /// coalesced per source so a fast knob spin sends the latest value, not a backlog.
    /// </summary>
    public void SetSourceVolume(string sourceName, float normalized)
    {
        if (!IsAvailable || string.IsNullOrEmpty(sourceName)) return;

        lock (_lock)
        {
            _faders[sourceName] = Math.Clamp(normalized, 0f, 1f);
            _pendingVolume[sourceName] = Math.Clamp(normalized, 0f, 1f);
            if (!_volumeInFlight.Add(sourceName)) return;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                float value;
                lock (_lock)
                {
                    if (!_pendingVolume.Remove(sourceName, out value))
                    {
                        _volumeInFlight.Remove(sourceName);
                        return;
                    }
                }

                var data = new JObject { ["inputName"] = sourceName };
                if (value <= 0f) data["inputVolumeMul"] = 0f;
                else data["inputVolumeDb"] = NormalizedToDb(value);
                await SendRequestAsync("SetInputVolume", data, timeoutMs: 1000);
            }
        });
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (!_config.Enabled) return false;

            if (!IsAvailable)
                await ConnectAsync();

            if (!IsAvailable) return false;

            var resp = await SendRequestAsync("GetVersion", timeoutMs: 3000);
            return resp != null;
        }
        catch (Exception ex)
        {
            Logger.Log($"OBS test connection failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Off the caller's sync context: blocking the UI thread on an awaited close deadlocks.
        try { Task.Run(DisconnectAsync).Wait(1500); } catch { }
    }
}
