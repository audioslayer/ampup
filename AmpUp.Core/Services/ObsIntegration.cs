using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

public class ObsIntegration : IDisposable
{
    private ClientWebSocket? _ws;
    private ObsConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _reconnectTask;
    private bool _disposed;
    private int _requestId;
    private readonly Dictionary<string, TaskCompletionSource<JObject>> _pending = new();
    private readonly object _lock = new();

    public bool IsAvailable { get; private set; }

    /// <summary>True while OBS is actively recording. Updated by <see cref="RefreshStatusAsync"/>.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>True while OBS is actively streaming. Updated by <see cref="RefreshStatusAsync"/>.</summary>
    public bool IsStreaming { get; private set; }

    public ObsIntegration(ObsConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Polls OBS for the current recording/streaming state and updates <see cref="IsRecording"/> / <see cref="IsStreaming"/>.
    /// Call periodically (e.g. every few seconds) to keep dynamic-state UI in sync.
    /// </summary>
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

    public void UpdateConfig(ObsConfig config)
    {
        var wasEnabled = _config.Enabled;
        _config = config;

        if (!config.Enabled)
        {
            DisconnectAsync().ConfigureAwait(false);
            return;
        }

        // Reconnect if settings changed or newly enabled
        if (!wasEnabled || !IsAvailable)
            _ = ConnectAsync();
    }

    public async Task<bool> ConnectAsync()
    {
        if (!_config.Enabled) return false;

        await DisconnectAsync();

        _cts = new CancellationTokenSource();
        try
        {
            _ws = new ClientWebSocket();
            var uri = new Uri($"ws://{_config.Host}:{_config.Port}");
            await _ws.ConnectAsync(uri, _cts.Token);

            // Start receive loop
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            // Wait for Hello message and authenticate
            // The receive loop handles Hello → auth handshake
            // Give it a moment to complete
            await Task.Delay(2000, _cts.Token);

            return IsAvailable;
        }
        catch (Exception ex)
        {
            Logger.Log($"OBS connect failed: {ex.Message}");
            IsAvailable = false;
            StartReconnectLoop();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        IsAvailable = false;
        IsRecording = false;
        IsStreaming = false;
        _cts?.Cancel();

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
            _ws.Dispose();
            _ws = null;
        }

        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    IsAvailable = false;
                    StartReconnectLoop();
                    return;
                }

                var text = sb.ToString();
                try
                {
                    var msg = JObject.Parse(text);
                    await HandleMessageAsync(msg, ct);
                }
                catch (Exception ex)
                {
                    Logger.Log($"OBS message parse error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException)
        {
            IsAvailable = false;
            StartReconnectLoop();
        }
        catch (Exception ex)
        {
            Logger.Log($"OBS receive loop error: {ex.Message}");
            IsAvailable = false;
            StartReconnectLoop();
        }
    }

    private async Task HandleMessageAsync(JObject msg, CancellationToken ct)
    {
        var op = msg["op"]?.Value<int>() ?? -1;

        switch (op)
        {
            case 0: // Hello
                await HandleHelloAsync(msg, ct);
                break;
            case 2: // Identified (auth success)
                IsAvailable = true;
                Logger.Log("OBS WebSocket: authenticated and connected");
                break;
            case 7: // RequestResponse
            {
                var requestId = msg["d"]?["requestId"]?.Value<string>();
                if (requestId != null)
                {
                    TaskCompletionSource<JObject>? tcs;
                    lock (_lock)
                        _pending.Remove(requestId, out tcs);
                    tcs?.TrySetResult(msg["d"] as JObject ?? new JObject());
                }
                break;
            }
        }
    }

    private async Task HandleHelloAsync(JObject msg, CancellationToken ct)
    {
        var d = msg["d"];
        var auth = d?["authentication"];

        JObject identifyData;

        if (auth != null && !string.IsNullOrEmpty(_config.Password))
        {
            // OBS WebSocket v5 auth: SHA256 challenge-response
            var challenge = auth["challenge"]?.Value<string>() ?? "";
            var salt = auth["salt"]?.Value<string>() ?? "";

            var secret = ComputeAuth(_config.Password, salt, challenge);

            identifyData = new JObject
            {
                ["op"] = 1,
                ["d"] = new JObject
                {
                    ["rpcVersion"] = 1,
                    ["authentication"] = secret,
                }
            };
        }
        else
        {
            // No auth required
            identifyData = new JObject
            {
                ["op"] = 1,
                ["d"] = new JObject
                {
                    ["rpcVersion"] = 1,
                }
            };
        }

        await SendAsync(identifyData, ct);
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

    private async Task SendAsync(JObject msg, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = msg.ToString(Formatting.None);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<JObject?> SendRequestAsync(string requestType, JObject? requestData = null, int timeoutMs = 5000)
    {
        if (_ws?.State != WebSocketState.Open || !IsAvailable) return null;

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
            await SendAsync(msg, _cts?.Token ?? CancellationToken.None);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed == tcs.Task)
                return await tcs.Task;

            // Timeout
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
                if (_disposed || !_config.Enabled) return;

                try
                {
                    await ConnectAsync();
                    if (IsAvailable)
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

    public async Task StartRecordingAsync()
    {
        await SendRequestAsync("StartRecord");
    }

    public async Task StopRecordingAsync()
    {
        await SendRequestAsync("StopRecord");
    }

    public async Task ToggleRecordingAsync()
    {
        await SendRequestAsync("ToggleRecord");
    }

    public async Task StartStreamingAsync()
    {
        await SendRequestAsync("StartStream");
    }

    public async Task StopStreamingAsync()
    {
        await SendRequestAsync("StopStream");
    }

    public async Task ToggleStreamingAsync()
    {
        await SendRequestAsync("ToggleStream");
    }

    public async Task SetSceneAsync(string sceneName)
    {
        await SendRequestAsync("SetCurrentProgramScene", new JObject
        {
            ["sceneName"] = sceneName,
        });
    }

    public async Task<List<string>> GetScenesAsync()
    {
        var result = new List<string>();
        var resp = await SendRequestAsync("GetSceneList");
        if (resp == null) return result;

        var scenes = resp["responseData"]?["scenes"] as JArray;
        if (scenes != null)
        {
            foreach (var scene in scenes)
            {
                var name = scene["sceneName"]?.Value<string>();
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }
        }
        return result;
    }

    public async Task ToggleMuteAsync(string sourceName)
    {
        await SendRequestAsync("ToggleInputMute", new JObject
        {
            ["inputName"] = sourceName,
        });
    }

    public async Task SetSourceVolumeAsync(string sourceName, float vol)
    {
        await SendRequestAsync("SetInputVolume", new JObject
        {
            ["inputName"] = sourceName,
            ["inputVolumeMul"] = Math.Clamp(vol, 0f, 1f),
        });
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (!_config.Enabled) return false;

            // Try a fresh connection if not already connected
            if (!IsAvailable)
                await ConnectAsync();

            if (!IsAvailable) return false;

            // Send a simple request to verify
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
        DisconnectAsync().GetAwaiter().GetResult();
    }
}
