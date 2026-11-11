using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

/// <summary>
/// Local Discord RPC client for desktop Discord. It connects to the
/// discord-ipc-{n} named pipe, authenticates with Discord RPC, then flips
/// self mute/deafen through GET_VOICE_SETTINGS + SET_VOICE_SETTINGS.
/// </summary>
public sealed class DiscordRpcIntegration : IDisposable
{
    // Discord limits rpc.voice.read/rpc.voice.write to approved partners, but
    // users added as testers on AmpUp's developer app can authorize them.
    // Everyone else gets invalid_scope from AUTHORIZE — IsTesterAccessError
    // detects that so the UI can explain how to request tester access.
    public static bool VoiceControlAvailable => true;
    public const string VoiceControlUnavailableReason =
        "Discord voice controls are unavailable because Discord restricts the required RPC voice scopes to approved partners.";

    // Where users ask to be added as a tester: friend request to aud10sayer on Discord.
    public const string CommunityInviteUrl = "https://discord.gg/TDkfWSjqZ";
    public const string TesterAccessMessage =
        "Discord voice controls are in tester preview. Discord only lets approved testers connect until AmpUp is fully approved.\n\n" +
        "To get access, send a friend request to aud10sayer on Discord asking for Amp Up tester access (see the Setup guide). Once you're added as a tester, click Connect again.";

    /// <summary>
    /// True when Discord rejected authorization because this account isn't
    /// an approved tester (invalid_scope / OAuth2 scope errors).
    /// </summary>
    public static bool IsTesterAccessError(Exception ex)
    {
        var message = ex.Message ?? "";
        return message.Contains("invalid_scope", StringComparison.OrdinalIgnoreCase)
            || message.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OAuth2", StringComparison.OrdinalIgnoreCase);
    }

    private const int HandshakeOpcode = 0;
    private const int FrameOpcode = 1;
    private const int CloseOpcode = 2;
    private const int PipeCount = 10;
    private static readonly string[] VoiceScopes = { "rpc", "rpc.voice.read", "rpc.voice.write" };

    private readonly DiscordRpcConfig _config;
    private readonly Action<DiscordRpcConfig> _persist;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new();
    private NamedPipeClientStream? _pipe;
    private string? _pipeName;
    private bool _authenticated;
    private int _unsupportedActionLogged;

    public DiscordRpcIntegration(DiscordRpcConfig config, Action<DiscordRpcConfig> persistConfig)
    {
        _config = config;
        _persist = persistConfig;
    }

    public bool IsReady => _authenticated && _pipe?.IsConnected == true;
    public bool? LastKnownMute { get; private set; }
    public bool? LastKnownDeafen { get; private set; }
    public bool? LastKnownPushToTalk { get; private set; }
    public bool? LastKnownAutomaticGainControl { get; private set; }
    public bool? LastKnownEchoCancellation { get; private set; }
    public bool? LastKnownNoiseSuppression { get; private set; }
    /// <summary>Discord input volume, 0..100 (Discord's own scale).</summary>
    public float? LastKnownInputVolume { get; private set; }
    /// <summary>Discord output volume, 0..200 (100 = normal).</summary>
    public float? LastKnownOutputVolume { get; private set; }

    /// <summary>Raised (on a background thread) when cached voice settings change.</summary>
    public event Action? VoiceSettingsChanged;

    // Knob mapping: input 0..100% -> Discord 0..100; output 0..100% -> Discord 0..200
    // (knob at 50% = Discord's normal 100%).
    public const float InputVolumeMax = 100f;
    public const float OutputVolumeMax = 200f;

    private const int StatePollIntervalMs = 2000;
    private const int ResumeRetryMs = 30000;
    private float _pendingInputVolume = -1f;
    private float _pendingOutputVolume = -1f;
    private int _volumeSending;
    private int _volumeSkipLogged;
    private CancellationTokenSource? _pollCts;
    private long _lastResumeAttemptTick;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (!VoiceControlAvailable)
            throw new NotSupportedException(VoiceControlUnavailableReason);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disconnect()
    {
        ClosePipe();
        _config.AccessToken = "";
        _config.RefreshToken = "";
        _config.TokenExpiresAtUtc = DateTime.MinValue;
        _config.ConnectedUser = "";
        _persist(_config);
    }

    public async Task ToggleMuteAsync(CancellationToken ct = default)
        => await ToggleVoiceFlagAsync("mute", ct);

    public async Task ToggleDeafenAsync(CancellationToken ct = default)
        => await ToggleVoiceFlagAsync("deaf", ct);

    public async Task SetMuteAsync(bool muted, CancellationToken ct = default)
        => await SetVoiceFlagAsync("mute", muted, ct);

    public async Task SetDeafenAsync(bool deafened, CancellationToken ct = default)
        => await SetVoiceFlagAsync("deaf", deafened, ct);

    public async Task ToggleNoiseSuppressionAsync(CancellationToken ct = default)
        => await ToggleVoiceFlagAsync("noise_suppression", ct);

    public async Task ToggleAutomaticGainControlAsync(CancellationToken ct = default)
        => await ToggleVoiceFlagAsync("automatic_gain_control", ct);

    public async Task ToggleEchoCancellationAsync(CancellationToken ct = default)
        => await ToggleVoiceFlagAsync("echo_cancellation", ct);

    /// <summary>Switch Discord input mode between Voice Activity and Push to Talk.</summary>
    public async Task TogglePushToTalkAsync(CancellationToken ct = default)
    {
        if (!VoiceControlAvailable)
        {
            ReportUnsupportedAction();
            return;
        }

        if (!_config.Enabled)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
            var settings = await SendCommandAsync("GET_VOICE_SETTINGS", new JObject(), ct);
            UpdateCachedFlags(settings);
            var mode = settings["mode"] as JObject;
            bool isPtt = string.Equals(mode?.Value<string>("type"), "PUSH_TO_TALK", StringComparison.OrdinalIgnoreCase);
            var args = new JObject
            {
                ["mode"] = new JObject { ["type"] = isPtt ? "VOICE_ACTIVITY" : "PUSH_TO_TALK" },
            };
            var updated = await SendCommandAsync("SET_VOICE_SETTINGS", args, ct);
            UpdateCachedFlags(updated);
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC toggle push to talk failed: {ex.Message}");
            ClosePipe();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Knob position (0..1) to Discord input volume (0..100). Latest value wins.</summary>
    public void SetInputVolume(float normalized)
    {
        Interlocked.Exchange(ref _pendingInputVolume, Math.Clamp(normalized, 0f, 1f) * InputVolumeMax);
        KickVolumeSender();
    }

    /// <summary>Knob position (0..1) to Discord output volume (0..200, 0.5 = 100). Latest value wins.</summary>
    public void SetOutputVolume(float normalized)
    {
        Interlocked.Exchange(ref _pendingOutputVolume, Math.Clamp(normalized, 0f, 1f) * OutputVolumeMax);
        KickVolumeSender();
    }

    /// <summary>Last known Discord input volume as a knob position (0..1).</summary>
    public bool TryGetInputVolume(out float normalized)
    {
        var v = LastKnownInputVolume;
        normalized = v.HasValue ? Math.Clamp(v.Value / InputVolumeMax, 0f, 1f) : 0f;
        return v.HasValue;
    }

    /// <summary>Last known Discord output volume as a knob position (0..1).</summary>
    public bool TryGetOutputVolume(out float normalized)
    {
        var v = LastKnownOutputVolume;
        normalized = v.HasValue ? Math.Clamp(v.Value / OutputVolumeMax, 0f, 1f) : 0f;
        return v.HasValue;
    }

    /// <summary>
    /// Keep the cached voice settings fresh by polling GET_VOICE_SETTINGS every 2s
    /// while connected and <paramref name="shouldPoll"/> says a Discord action or
    /// target is in use. Never opens Discord's Authorize prompt: if not connected it
    /// only resumes with a still-valid stored token.
    /// </summary>
    public void StartStatePolling(Func<bool> shouldPoll)
    {
        _pollCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _ = Task.Run(() => StatePollLoopAsync(shouldPoll, cts.Token));
    }

    public async Task LeaveVoiceChannelAsync(CancellationToken ct = default)
    {
        if (!VoiceControlAvailable)
        {
            ReportUnsupportedAction();
            return;
        }

        if (!_config.Enabled)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
            await SendCommandAsync("SELECT_VOICE_CHANNEL",
                new JObject { ["channel_id"] = JValue.CreateNull() }, ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC leave voice failed: {ex.Message}");
            ClosePipe();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool?> GetMuteAsync(CancellationToken ct = default)
        => await GetVoiceFlagAsync("mute", ct);

    public async Task<bool?> GetDeafenAsync(CancellationToken ct = default)
        => await GetVoiceFlagAsync("deaf", ct);

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            ClosePipe();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void KickVolumeSender()
    {
        if (Interlocked.CompareExchange(ref _volumeSending, 1, 0) != 0)
            return;
        _ = Task.Run(() => VolumeSendLoopAsync());
    }

    private async Task VolumeSendLoopAsync()
    {
        try
        {
            while (true)
            {
                float input = Interlocked.Exchange(ref _pendingInputVolume, -1f);
                float output = Interlocked.Exchange(ref _pendingOutputVolume, -1f);
                if (input < 0f && output < 0f)
                    break;

                if (!VoiceControlAvailable || !_config.Enabled)
                    continue;

                // A knob turn must never pop Discord's Authorize dialog; that only
                // happens from Connect or a button press.
                if (!IsReady && string.IsNullOrWhiteSpace(_config.AccessToken)
                    && string.IsNullOrWhiteSpace(_config.RefreshToken))
                {
                    if (Interlocked.Exchange(ref _volumeSkipLogged, 1) == 0)
                        Logger.Log("Discord RPC volume ignored: Discord is not connected (Settings > Integrations > Discord).");
                    continue;
                }

                var args = new JObject();
                if (input >= 0f) args["input"] = new JObject { ["volume"] = Math.Round(input, 1) };
                if (output >= 0f) args["output"] = new JObject { ["volume"] = Math.Round(output, 1) };

                await _gate.WaitAsync();
                try
                {
                    await EnsureAuthenticatedAsync(CancellationToken.None);
                    var updated = await SendCommandAsync("SET_VOICE_SETTINGS", args, CancellationToken.None);
                    UpdateCachedFlags(updated);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Discord RPC set volume failed: {ex.Message}");
                    ClosePipe();
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC volume sender failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _volumeSending, 0);
            if (Volatile.Read(ref _pendingInputVolume) >= 0f || Volatile.Read(ref _pendingOutputVolume) >= 0f)
                KickVolumeSender();
        }
    }

    private async Task StatePollLoopAsync(Func<bool> shouldPoll, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StatePollIntervalMs, ct);
                if (!VoiceControlAvailable || !_config.Enabled || !shouldPoll())
                    continue;

                if (!await _gate.WaitAsync(0, ct))
                    continue; // a command is running; it refreshes the cache itself
                try
                {
                    if (!IsReady && !await TryResumeAsync(ct))
                        continue;
                    var settings = await SendCommandAsync("GET_VOICE_SETTINGS", new JObject(), ct);
                    UpdateCachedFlags(settings);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Discord RPC state poll failed: {ex.Message}");
                    ClosePipe();
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"Discord RPC state poll loop error: {ex.Message}");
            }
        }
    }

    /// <summary>Reconnect with the stored token only (no refresh, no Authorize prompt). Caller holds _gate.</summary>
    private async Task<bool> TryResumeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.AccessToken)
            || _config.TokenExpiresAtUtc <= DateTime.UtcNow.AddMinutes(5))
            return false;

        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastResumeAttemptTick) < ResumeRetryMs)
            return false;
        Interlocked.Exchange(ref _lastResumeAttemptTick, now);

        var clientId = ResolveClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            return false;

        try
        {
            await ConnectAsync(clientId, ct);
            await AuthenticateAsync(_config.AccessToken, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Log($"Discord RPC resume failed: {ex.Message}");
            ClosePipe();
            return false;
        }
    }

    private async Task ToggleVoiceFlagAsync(string flag, CancellationToken ct)
    {
        if (!VoiceControlAvailable)
        {
            ReportUnsupportedAction();
            return;
        }

        if (!_config.Enabled)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
            var settings = await SendCommandAsync("GET_VOICE_SETTINGS", new JObject(), ct);
            bool current = settings.Value<bool?>(flag) ?? false;
            UpdateCachedFlags(settings);
            var args = new JObject { [flag] = !current };
            var updated = await SendCommandAsync("SET_VOICE_SETTINGS", args, ct);
            UpdateCachedFlags(updated);
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC toggle {flag} failed: {ex.Message}");
            ClosePipe();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetVoiceFlagAsync(string flag, bool value, CancellationToken ct)
    {
        if (!VoiceControlAvailable)
        {
            ReportUnsupportedAction();
            return;
        }

        if (!_config.Enabled)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
            var updated = await SendCommandAsync("SET_VOICE_SETTINGS",
                new JObject { [flag] = value }, ct);
            UpdateCachedFlags(updated);
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC set {flag} failed: {ex.Message}");
            ClosePipe();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool?> GetVoiceFlagAsync(string flag, CancellationToken ct)
    {
        if (!VoiceControlAvailable)
        {
            ReportUnsupportedAction();
            return null;
        }

        if (!_config.Enabled)
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);
            var settings = await SendCommandAsync("GET_VOICE_SETTINGS", new JObject(), ct);
            UpdateCachedFlags(settings);
            return settings.Value<bool?>(flag);
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord RPC read {flag} failed: {ex.Message}");
            ClosePipe();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (IsReady)
            return;

        var clientId = ResolveClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Discord RPC Client ID is not configured.");

        await ConnectAsync(clientId, ct);

        if (!string.IsNullOrWhiteSpace(_config.AccessToken)
            && _config.TokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            try
            {
                await AuthenticateAsync(_config.AccessToken, ct);
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"Discord RPC stored token failed: {ex.Message}");
                ClosePipe();
                await ConnectAsync(clientId, ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(_config.RefreshToken))
        {
            try
            {
                var refreshed = await RefreshAccessTokenAsync(ct);
                await AuthenticateAsync(refreshed.AccessToken, ct);
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"Discord RPC refresh token failed: {ex.Message}");
                _config.AccessToken = "";
                _config.RefreshToken = "";
                _config.TokenExpiresAtUtc = DateTime.MinValue;
                _config.ConnectedUser = "";
                _persist(_config);
                ClosePipe();
                await ConnectAsync(clientId, ct);
            }
        }

        var auth = await AuthorizeAsync(clientId, ct);
        var token = await ExchangeCodeAsync(auth.Code, auth.CodeVerifier, ct);
        await AuthenticateAsync(token.AccessToken, ct);
    }

    private async Task ConnectAsync(string clientId, CancellationToken ct)
    {
        ClosePipe();

        for (int i = 0; i < PipeCount; i++)
        {
            var pipeName = $"discord-ipc-{i}";
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(350, ct);
                _pipe = pipe;
                _pipeName = pipeName;

                var handshake = new JObject
                {
                    ["v"] = 1,
                    ["client_id"] = clientId,
                };
                await WritePacketAsync(HandshakeOpcode, handshake, ct);
                var ready = await ReadPayloadAsync(ct);
                if (ready.Value<string>("evt") != "READY")
                    throw new InvalidOperationException($"Unexpected Discord RPC handshake response: {ready}");

                return;
            }
            catch
            {
                pipe.Dispose();
                if (_pipe == pipe) _pipe = null;
            }
        }

        throw new InvalidOperationException("Discord desktop RPC pipe was not found. Start Discord and try again.");
    }

    private async Task<AuthCodeResponse> AuthorizeAsync(string clientId, CancellationToken ct)
    {
        var codeVerifier = CreatePkceVerifier();
        var args = new JObject
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["scopes"] = new JArray(VoiceScopes),
            ["code_challenge"] = CreatePkceChallenge(codeVerifier),
            ["code_challenge_method"] = "S256",
        };
        var data = await SendCommandAsync("AUTHORIZE", args, ct);
        var code = data.Value<string>("code");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Discord did not return an authorization code.");
        return new AuthCodeResponse(code, codeVerifier);
    }

    private async Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        var clientId = ResolveClientId();
        var clientSecret = ResolveClientSecret();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = ResolveRedirectUri(),
            ["code_verifier"] = codeVerifier,
        };

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_id"] = clientId;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }

        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Discord token exchange failed: {(int)response.StatusCode} {json}");

        var payload = JObject.Parse(json);
        var accessToken = payload.Value<string>("access_token") ?? "";
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Discord token exchange returned no access token.");

        _config.AccessToken = accessToken;
        _config.RefreshToken = payload.Value<string>("refresh_token") ?? _config.RefreshToken;
        int expiresIn = payload.Value<int?>("expires_in") ?? 604800;
        _config.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
        _persist(_config);

        return new TokenResponse(accessToken);
    }

    private async Task<TokenResponse> RefreshAccessTokenAsync(CancellationToken ct)
    {
        var clientId = ResolveClientId();
        var clientSecret = ResolveClientSecret();
        var refreshToken = _config.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("No Discord refresh token is available.");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_id"] = clientId;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }

        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Discord token refresh failed: {(int)response.StatusCode} {json}");

        var payload = JObject.Parse(json);
        var accessToken = payload.Value<string>("access_token") ?? "";
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Discord token refresh returned no access token.");

        _config.AccessToken = accessToken;
        _config.RefreshToken = payload.Value<string>("refresh_token") ?? _config.RefreshToken;
        int expiresIn = payload.Value<int?>("expires_in") ?? 604800;
        _config.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
        _persist(_config);

        return new TokenResponse(accessToken);
    }

    private async Task AuthenticateAsync(string accessToken, CancellationToken ct)
    {
        var data = await SendCommandAsync("AUTHENTICATE", new JObject { ["access_token"] = accessToken }, ct);
        var user = data["user"] as JObject;
        if (user != null)
        {
            _config.ConnectedUser = user.Value<string>("username") ?? user.Value<string>("id") ?? "";
            _persist(_config);
        }
        _authenticated = true;
    }

    private async Task<JObject> SendCommandAsync(string command, JObject args, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var payload = new JObject
        {
            ["cmd"] = command,
            ["args"] = args,
            ["nonce"] = nonce,
        };

        await WritePacketAsync(FrameOpcode, payload, ct);

        while (true)
        {
            var response = await ReadPayloadAsync(ct);
            if (response.Value<string>("nonce") != nonce)
                continue;

            if (response.Value<string>("evt") == "ERROR")
            {
                var err = response["data"] as JObject;
                var message = err?.Value<string>("message") ?? response.ToString(Formatting.None);
                throw new InvalidOperationException(message);
            }

            return response["data"] as JObject ?? new JObject();
        }
    }

    private async Task WritePacketAsync(int opcode, JObject payload, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Discord RPC pipe is not connected.");
        var json = payload.ToString(Formatting.None);
        var body = Encoding.UTF8.GetBytes(json);
        var header = new byte[8];
        BitConverter.GetBytes(opcode).CopyTo(header, 0);
        BitConverter.GetBytes(body.Length).CopyTo(header, 4);
        await pipe.WriteAsync(header, ct);
        await pipe.WriteAsync(body, ct);
        await pipe.FlushAsync(ct);
    }

    private async Task<JObject> ReadPayloadAsync(CancellationToken ct)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Discord RPC pipe is not connected.");
        var header = await ReadExactAsync(pipe, 8, ct);
        int opcode = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);
        if (opcode == CloseOpcode)
            throw new InvalidOperationException("Discord closed the RPC connection.");
        if (length <= 0 || length > 1024 * 1024)
            throw new InvalidOperationException($"Invalid Discord RPC frame length: {length}");

        var body = await ReadExactAsync(pipe, length, ct);
        return JObject.Parse(Encoding.UTF8.GetString(body));
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
            if (read <= 0)
                throw new EndOfStreamException("Discord RPC pipe ended unexpectedly.");
            offset += read;
        }
        return buffer;
    }

    private void UpdateCachedFlags(JObject settings)
    {
        bool changed = false;
        bool? ReadBool(string key, bool? current)
        {
            if (settings[key] is JValue { Type: JTokenType.Boolean } token)
            {
                bool value = token.Value<bool>();
                if (current != value) changed = true;
                return value;
            }
            return current;
        }
        float? ReadVolume(string key, float? current)
        {
            if (settings[key] is JObject obj && obj["volume"] is JValue token
                && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer))
            {
                float value = token.Value<float>();
                if (!current.HasValue || Math.Abs(current.Value - value) > 0.05f) changed = true;
                return value;
            }
            return current;
        }

        LastKnownMute = ReadBool("mute", LastKnownMute);
        LastKnownDeafen = ReadBool("deaf", LastKnownDeafen);
        LastKnownAutomaticGainControl = ReadBool("automatic_gain_control", LastKnownAutomaticGainControl);
        LastKnownEchoCancellation = ReadBool("echo_cancellation", LastKnownEchoCancellation);
        LastKnownNoiseSuppression = ReadBool("noise_suppression", LastKnownNoiseSuppression);
        LastKnownInputVolume = ReadVolume("input", LastKnownInputVolume);
        LastKnownOutputVolume = ReadVolume("output", LastKnownOutputVolume);

        if (settings["mode"] is JObject mode && mode.Value<string>("type") is string modeType)
        {
            bool ptt = string.Equals(modeType, "PUSH_TO_TALK", StringComparison.OrdinalIgnoreCase);
            if (LastKnownPushToTalk != ptt) changed = true;
            LastKnownPushToTalk = ptt;
        }

        if (changed)
        {
            try { VoiceSettingsChanged?.Invoke(); }
            catch (Exception ex) { Logger.Log($"Discord RPC state handler failed: {ex.Message}"); }
        }
    }

    private void ReportUnsupportedAction()
    {
        if (Interlocked.Exchange(ref _unsupportedActionLogged, 1) == 0)
            Logger.Log($"Discord RPC action ignored: {VoiceControlUnavailableReason}");
    }

    private string ResolveClientId()
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("AMPUP_DISCORD_CLIENT_ID"),
            _config.ClientId);

    private string ResolveClientSecret()
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("AMPUP_DISCORD_CLIENT_SECRET"),
            _config.ClientSecret);

    private string ResolveRedirectUri()
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("AMPUP_DISCORD_REDIRECT_URI"),
            _config.RedirectUri,
            "http://127.0.0.1");

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return "";
    }

    private static string CreatePkceVerifier()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreatePkceChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private void ClosePipe()
    {
        _authenticated = false;
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _pipeName = null;
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        ClosePipe();
        _gate.Dispose();
        _http.Dispose();
    }

    private sealed record AuthCodeResponse(string Code, string CodeVerifier);
    private sealed record TokenResponse(string AccessToken);
}
