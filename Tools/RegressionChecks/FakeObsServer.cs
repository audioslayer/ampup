using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Minimal obs-websocket v5 server for regression checks: Hello / Identify (with
/// optional password), a handful of requests, and server-pushed events. Uses a raw
/// TcpListener so it needs no HTTP URL reservation or admin rights.
/// </summary>
sealed class FakeObsServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _gate = new();
    private WebSocket? _ws;
    private const string Salt = "c2FsdA==";
    private const string Challenge = "Y2hhbGxlbmdl";

    public int Port { get; }
    public string? Password { get; init; }

    /// <summary>Input name → muted state; null = video-only input (no audio).</summary>
    public Dictionary<string, bool?> Inputs { get; } = new();

    /// <summary>Scenes as OBS shows them, top to bottom.</summary>
    public List<string> Scenes { get; } = new();

    public List<(string Input, double? Db, double? Mul)> VolumeSets { get; } = new();
    public int VolumeDelayMs { get; set; }

    public FakeObsServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    public List<(string Input, double? Db, double? Mul)> SnapshotVolumeSets()
    {
        lock (_gate) return VolumeSets.ToList();
    }

    public Task EmitEventAsync(string eventType, JObject data) =>
        SendAsync(new JObject { ["op"] = 5, ["d"] = new JObject { ["eventType"] = eventType, ["eventIntent"] = 0, ["eventData"] = data } });

    /// <summary>Abruptly drops the current client, like OBS quitting.</summary>
    public void DropClient()
    {
        _ws?.Abort();
        _ws = null;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = ServeAsync(client);
            }
        }
        catch { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var header = new StringBuilder();
        var one = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n"))
        {
            if (await stream.ReadAsync(one, _cts.Token) == 0) return;
            header.Append((char)one[0]);
        }
        var key = header.ToString().Split("\r\n")
            .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                       $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _cts.Token);

        var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
        _ws = ws;

        var hello = new JObject { ["obsWebSocketVersion"] = "5.5.0", ["rpcVersion"] = 1 };
        if (Password != null)
            hello["authentication"] = new JObject { ["challenge"] = Challenge, ["salt"] = Salt };
        await SendAsync(new JObject { ["op"] = 0, ["d"] = hello });

        var buffer = new byte[65536];
        try
        {
            while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, _cts.Token);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) return;

                var msg = JObject.Parse(sb.ToString());
                var d = (JObject)msg["d"]!;
                switch (msg["op"]!.Value<int>())
                {
                    case 1:
                        if (Password != null && d["authentication"]?.Value<string>() != Auth(Password))
                        {
                            await ws.CloseOutputAsync((WebSocketCloseStatus)4009, "Authentication failed.", CancellationToken.None);
                            return;
                        }
                        await SendAsync(new JObject { ["op"] = 2, ["d"] = new JObject { ["negotiatedRpcVersion"] = 1 } });
                        break;
                    case 6:
                        await HandleRequestAsync(d);
                        break;
                }
            }
        }
        catch { }
    }

    private async Task HandleRequestAsync(JObject d)
    {
        var type = d["requestType"]!.Value<string>()!;
        var data = d["requestData"] as JObject ?? new JObject();
        bool ok = true;
        JObject? responseData = null;

        switch (type)
        {
            case "GetInputList":
                responseData = new JObject { ["inputs"] = new JArray(Inputs.Keys.Select(n => new JObject { ["inputName"] = n, ["inputKind"] = "x" })) };
                break;
            case "GetInputMute":
            {
                var name = data["inputName"]!.Value<string>()!;
                if (Inputs.TryGetValue(name, out var muted) && muted.HasValue)
                    responseData = new JObject { ["inputMuted"] = muted.Value };
                else
                    ok = false; // OBS: InvalidResourceState for inputs without audio
                break;
            }
            case "GetSceneList":
                // OBS returns the bottom scene first with sceneIndex 0
                responseData = new JObject
                {
                    ["scenes"] = new JArray(Scenes.AsEnumerable().Reverse().Select((n, i) => new JObject { ["sceneName"] = n, ["sceneIndex"] = i }))
                };
                break;
            case "SetInputVolume":
                if (VolumeDelayMs > 0) await Task.Delay(VolumeDelayMs);
                lock (_gate)
                    VolumeSets.Add((data["inputName"]!.Value<string>()!, data["inputVolumeDb"]?.Value<double>(), data["inputVolumeMul"]?.Value<double>()));
                break;
            case "GetRecordStatus":
            case "GetStreamStatus":
                responseData = new JObject { ["outputActive"] = false };
                break;
        }

        var resp = new JObject
        {
            ["requestType"] = type,
            ["requestId"] = d["requestId"],
            ["requestStatus"] = new JObject { ["result"] = ok, ["code"] = ok ? 100 : 604 },
        };
        if (responseData != null) resp["responseData"] = responseData;
        await SendAsync(new JObject { ["op"] = 7, ["d"] = resp });
    }

    private static string Auth(string password)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + Salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + Challenge)));
    }

    private async Task SendAsync(JObject msg)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToString(Formatting.None)), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
        finally { _sendLock.Release(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ws?.Abort();
        _listener.Stop();
    }
}
