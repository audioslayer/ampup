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
    private int _listRefreshQueued;

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
                    Logger.Log($"OBS connect: {LastError}");
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
                Logger.Log($"OBS connect failed: {ex.Message}");
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
            case "RecordStateChanged":
                IsRecording = data?["outputActive"]?.Value<bool>() ?? IsRecording;
                break;
            case "StreamStateChanged":
                IsStreaming = data?["outputActive"]?.Value<bool>() ?? IsStreaming;
                break;
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
                return (Name: n, IsAudio: ok, Muted: muted);
            }));

            var scenesResp = await SendRequestAsync("GetSceneList");
            var scenes = (scenesResp?["responseData"]?["scenes"] as JArray)?
                .OrderByDescending(s => s["sceneIndex"]?.Value<int>() ?? 0) // OBS lists top-of-UI last
                .Select(s => s["sceneName"]?.Value<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .ToList() ?? new List<string>();

            if (inputsResp == null) return; // dropped mid-refresh; keep last-known lists

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
            var rec = await SendRequestAsync("GetRecordStatus", timeoutMs: 2000);
            if (rec != null)
                IsRecording = rec["responseData"]?["outputActive"]?.Value<bool>() ?? false;

            var stream = await SendRequestAsync("GetStreamStatus", timeoutMs: 2000);
            if (stream != null)
                IsStreaming = stream["responseData"]?["outputActive"]?.Value<bool>() ?? false;
        }
        catch
        {
            // Silent — leave last-known values in place.
        }
    }

    private async Task SendIdentifyAsync(JObject? hello, ClientWebSocket ws, CancellationToken ct)
    {
        var auth = hello?["authentication"];
        var d = new JObject { ["rpcVersion"] = 1 };

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
