using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AmpUp.Core.Models;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace AmpUp.Core.Services;

/// <summary>
/// Thin wrapper around SpotifyAPI-NET that handles PKCE auth, refresh, and
/// a 2-second now-playing poll loop. The hosting app calls Connect once
/// (opens browser → OAuth → tokens stored in config), then reads the
/// CurrentTrack property and subscribes to OnStateChanged for live updates.
/// </summary>
public sealed class SpotifyIntegration : IDisposable
{
    private const int PollIntervalMs = 2000;
    private const string CallbackPath = "/callback";

    private SpotifyClient? _client;
    private EmbedIOAuthServer? _server;
    private PKCETokenResponse? _tokenResponse;
    private PKCEAuthenticator? _authenticator;
    private string? _codeVerifier;
    private CancellationTokenSource? _pollCts;

    private readonly SpotifyConfig _config;
    private readonly Action<SpotifyConfig> _persist;

    public SpotifyIntegration(SpotifyConfig config, Action<SpotifyConfig> persistConfig)
    {
        _config = config;
        _persist = persistConfig;
    }

    public bool IsConnected => _client != null;
    public SpotifyTrackState? CurrentTrack { get; private set; }
    public event Action? OnStateChanged;

    /// <summary>Fires after a Like key press succeeds: (nowLiked, track title, artists).</summary>
    public event Action<bool, string, string>? LikeChanged;

    private static string TokenCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmpUp", "spotify_tokens.json");

    // Album art cache — one file at a time, overwritten on track change so
    // the display renderer has a stable path to read.
    public static string AlbumArtCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmpUp", "spotify_nowplaying.jpg");

    private static readonly string[] DefaultScopes =
    {
        Scopes.UserReadPlaybackState,
        Scopes.UserReadCurrentlyPlaying,
        Scopes.UserModifyPlaybackState,
        Scopes.UserLibraryRead,
        Scopes.UserLibraryModify,
        Scopes.PlaylistReadPrivate,
        Scopes.PlaylistReadCollaborative,
    };

    /// <summary>True when a refresh token is saved (the user connected once and hasn't been revoked).</summary>
    public bool HasSavedSession => !string.IsNullOrWhiteSpace(_config.RefreshToken);

    /// <summary>True while the background auto-restore loop is still trying to bring the session back.</summary>
    public bool IsRestoring => _restoreCts != null;

    /// <summary>Fires when IsConnected / IsRestoring / HasSavedSession change (not on every track change).</summary>
    public event Action? ConnectionChanged;

    private CancellationTokenSource? _restoreCts;
    private readonly SemaphoreSlim _restoreWake = new(0, 1);

    private enum RestoreResult { Restored, NothingToRestore, Revoked, TransientFailure }

    /// <summary>
    /// Try once to rehydrate from a previously-saved refresh token. Returns true
    /// when an existing session was restored successfully.
    /// </summary>
    public async Task<bool> TryRestoreAsync() => await TryRestoreOnceAsync() == RestoreResult.Restored;

    private async Task<RestoreResult> TryRestoreOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId)) return RestoreResult.NothingToRestore;
        if (string.IsNullOrWhiteSpace(_config.RefreshToken)) return RestoreResult.NothingToRestore;
        try
        {
            var refreshed = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(_config.ClientId, _config.RefreshToken));
            await FinalizeLoginAsync(refreshed);
            return RestoreResult.Restored;
        }
        catch (Exception ex)
        {
            Logger.Log($"Spotify restore failed: {ex.Message}");
            if (IsInvalidGrant(ex))
            {
                // A revoked/expired refresh token can never recover by retrying.
                // Clear it so subsequent launches do not keep attempting it.
                _config.RefreshToken = "";
                _config.ConnectedUser = "";
                _persist(_config);
                return RestoreResult.Revoked;
            }
            // Network not up yet (common right after boot with Start with Windows),
            // DNS failure, Spotify 5xx, etc. — worth retrying.
            return RestoreResult.TransientFailure;
        }
    }

    /// <summary>
    /// Restore the saved session in the background, retrying transient failures with
    /// backoff (5s, 15s, 30s, then every 60s) and immediately when Windows reports the
    /// network is available again. Stops on success, on a revoked token, on a manual
    /// Connect/Disconnect, or on Dispose. Safe to call more than once.
    /// </summary>
    public void StartAutoRestore()
    {
        if (_client != null || !HasSavedSession || string.IsNullOrWhiteSpace(_config.ClientId)) return;
        var cts = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _restoreCts, cts, null) != null) { cts.Dispose(); return; }

        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        ConnectionChanged?.Invoke();
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            int[] delaysSec = { 5, 15, 30 };
            int attempt = 0;
            try
            {
                while (!ct.IsCancellationRequested && _client == null)
                {
                    var result = await TryRestoreOnceAsync();
                    if (result != RestoreResult.TransientFailure) break;

                    int wait = attempt < delaysSec.Length ? delaysSec[attempt] : 60;
                    attempt++;
                    Logger.Log($"Spotify restore: retrying in {wait}s (attempt {attempt + 1})");
                    try { await _restoreWake.WaitAsync(TimeSpan.FromSeconds(wait), ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex) { Logger.Log($"Spotify auto-restore loop error: {ex.Message}"); }
            finally { StopAutoRestore(); }
        }, ct);
    }

    private void StopAutoRestore()
    {
        var cts = Interlocked.Exchange(ref _restoreCts, null);
        if (cts == null) return;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
        ConnectionChanged?.Invoke();
    }

    private void OnNetworkAvailabilityChanged(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;
        // Wake the retry loop now instead of waiting out the backoff.
        try { if (_restoreWake.CurrentCount == 0) _restoreWake.Release(); } catch (SemaphoreFullException) { }
    }

    /// <summary>
    /// Kick off the PKCE OAuth flow. Opens the user's browser, spins up a
    /// loopback HttpListener on the configured callback port, blocks until
    /// the user grants consent (or cancels). Fires OnStateChanged on
    /// success; throws on failure.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId))
            throw new InvalidOperationException("Spotify ClientId is not set. Create an app at developer.spotify.com and paste the Client ID into Settings.");

        // A manual connect supersedes any background restore attempts.
        StopAutoRestore();

        // PKCE: generate verifier + SHA-256 challenge.
        var (verifier, challenge) = PKCEUtil.GenerateCodes();
        _codeVerifier = verifier;

        // Ensure we have a loopback server listening for the redirect.
        await ShutdownServerAsync();
        var redirectUri = new Uri($"http://127.0.0.1:{_config.CallbackPort}{CallbackPath}");
        _server = new EmbedIOAuthServer(redirectUri, _config.CallbackPort);
        var completion = new TaskCompletionSource<bool>();
        _server.AuthorizationCodeReceived += async (_, response) =>
        {
            try
            {
                await _server.Stop();
                var tokens = await new OAuthClient().RequestToken(
                    new PKCETokenRequest(_config.ClientId, response.Code, redirectUri, _codeVerifier!));
                await FinalizeLoginAsync(tokens);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"Spotify token exchange failed: {ex.Message}");
                completion.TrySetException(ex);
            }
        };
        _server.ErrorReceived += async (_, error, _) =>
        {
            await _server.Stop();
            completion.TrySetException(new Exception($"Spotify auth error: {error}"));
        };
        await _server.Start();

        var request = new LoginRequest(redirectUri, _config.ClientId, LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Scope = DefaultScopes,
        };
        BrowserUtil.Open(request.ToUri());

        // Let the user's browser flow complete; 3-minute timeout.
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(3));
        var winner = await Task.WhenAny(completion.Task, timeoutTask);
        if (winner == timeoutTask)
        {
            await ShutdownServerAsync();
            throw new TimeoutException("Spotify login timed out (3 min). Try again.");
        }
        await completion.Task; // rethrow any exception
    }

    private async Task FinalizeLoginAsync(PKCETokenResponse tokens)
    {
        _tokenResponse = tokens;
        _authenticator = new PKCEAuthenticator(_config.ClientId, tokens);
        _authenticator.TokenRefreshed += (_, fresh) =>
        {
            _tokenResponse = fresh;
            _config.RefreshToken = fresh.RefreshToken ?? _config.RefreshToken;
            _persist?.Invoke(_config);
        };

        var cfg = SpotifyClientConfig.CreateDefault().WithAuthenticator(_authenticator);
        _client = new SpotifyClient(cfg);

        // Cache refresh token + display name in user config.
        _config.RefreshToken = tokens.RefreshToken ?? _config.RefreshToken;
        try
        {
            var me = await _client.UserProfile.Current();
            _config.ConnectedUser = me?.DisplayName ?? me?.Id ?? "";
        }
        catch { /* display name is best-effort */ }
        _persist?.Invoke(_config);

        await ShutdownServerAsync();
        StartPolling();
        OnStateChanged?.Invoke();
        ConnectionChanged?.Invoke();
    }

    public void Disconnect()
    {
        StopAutoRestore();
        StopPolling();
        _client = null;
        _authenticator = null;
        _tokenResponse = null;
        _config.RefreshToken = "";
        _config.ConnectedUser = "";
        CurrentTrack = null;
        _persist?.Invoke(_config);
        OnStateChanged?.Invoke();
        ConnectionChanged?.Invoke();
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await PollOnceAsync(ct); }
                catch (APITooManyRequestsException rate)
                {
                    var wait = rate.RetryAfter.TotalMilliseconds > 0
                        ? rate.RetryAfter.TotalMilliseconds
                        : 5000;
                    try { await Task.Delay(TimeSpan.FromMilliseconds(wait), ct); } catch { }
                    continue;
                }
                catch (Exception ex)
                {
                    if (IsInvalidGrant(ex))
                    {
                        Logger.Log("Spotify authorization expired or was revoked; reconnect Spotify in Settings");
                        Disconnect();
                        break;
                    }
                    Logger.Log($"Spotify poll error: {ex.Message}");
                }

                try { await Task.Delay(PollIntervalMs, ct); } catch { }
            }
        }, ct);
    }

    private void StopPolling()
    {
        var cts = Interlocked.Exchange(ref _pollCts, null);
        if (cts == null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private static bool IsInvalidGrant(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (_client == null) return;
        var playback = await _client.Player.GetCurrentPlayback();
        var next = SpotifyTrackState.FromPlayback(playback);

        string? prevTrack = CurrentTrack?.TrackId;
        string? prevAlbum = CurrentTrack?.AlbumUri;
        bool prevPlaying = CurrentTrack?.IsPlaying ?? false;
        bool prevShuffle = CurrentTrack?.Shuffle ?? false;
        string prevRepeat = CurrentTrack?.RepeatState ?? "";
        bool prevLiked = CurrentTrack?.Liked ?? false;

        // Liked-state requires a second call (PlaybackState doesn't include it).
        if (!string.IsNullOrEmpty(next.TrackId) && next.TrackId != prevTrack)
        {
            var liked = await LibraryContainsAsync(TrackUri(next.TrackId));
            if (liked.HasValue) next = next with { Liked = liked.Value };
        }
        else if (CurrentTrack != null)
        {
            next = next with { Liked = CurrentTrack.Liked };
        }

        CurrentTrack = next;

        // Refresh album art cache on album change.
        if (!string.IsNullOrEmpty(next.AlbumArtUrl) && next.AlbumUri != prevAlbum)
            _ = FetchAlbumArtAsync(next.AlbumArtUrl);

        bool changed = next.TrackId != prevTrack
                       || next.IsPlaying != prevPlaying
                       || next.Shuffle != prevShuffle
                       || next.RepeatState != prevRepeat
                       || next.Liked != prevLiked;
        if (changed) OnStateChanged?.Invoke();
    }

    private static readonly HttpClient _http = new();
    private static async Task FetchAlbumArtAsync(string url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(AlbumArtCachePath)!);
            await File.WriteAllBytesAsync(AlbumArtCachePath, bytes);
        }
        catch (Exception ex) { Logger.Log($"Spotify album-art fetch failed: {ex.Message}"); }
    }

    // ── Control actions ────────────────────────────────────────────────

    public async Task PlayPauseAsync()
    {
        if (_client == null) return;
        try
        {
            if (CurrentTrack?.IsPlaying == true)
                await _client.Player.PausePlayback();
            else
                await _client.Player.ResumePlayback();
        }
        catch (Exception ex) { Logger.Log($"Spotify play/pause failed: {ex.Message}"); }
        _ = Task.Run(async () => { await Task.Delay(350); try { await PollOnceAsync(default); } catch { } });
    }

    public async Task NextAsync()
    {
        if (_client == null) return;
        try { await _client.Player.SkipNext(); }
        catch (Exception ex) { Logger.Log($"Spotify next failed: {ex.Message}"); }
        _ = Task.Run(async () => { await Task.Delay(350); try { await PollOnceAsync(default); } catch { } });
    }

    public async Task PreviousAsync()
    {
        if (_client == null) return;
        try { await _client.Player.SkipPrevious(); }
        catch (Exception ex) { Logger.Log($"Spotify prev failed: {ex.Message}"); }
        _ = Task.Run(async () => { await Task.Delay(350); try { await PollOnceAsync(default); } catch { } });
    }

    public async Task ToggleShuffleAsync()
    {
        if (_client == null) return;
        try
        {
            bool newState = !(CurrentTrack?.Shuffle ?? false);
            await _client.Player.SetShuffle(new PlayerShuffleRequest(newState));
        }
        catch (Exception ex) { Logger.Log($"Spotify shuffle toggle failed: {ex.Message}"); }
        _ = Task.Run(async () => { await Task.Delay(350); try { await PollOnceAsync(default); } catch { } });
    }

    public async Task ToggleLikeAsync()
    {
        var track = CurrentTrack;
        if (_client == null || string.IsNullOrEmpty(track?.TrackId)) return;
        bool liked = track.Liked;
        try
        {
            // Re-check first: the cached state can be stale (e.g. liked in the Spotify app).
            var current = await LibraryContainsAsync(TrackUri(track.TrackId));
            if (current.HasValue) liked = current.Value;

            await LibraryWriteAsync(liked ? HttpMethod.Delete : HttpMethod.Put, TrackUri(track.TrackId));

            // The poll only re-checks liked state when the track changes, so update it here
            // (N3 heart key + a second press to unlike both depend on it).
            if (CurrentTrack?.TrackId == track.TrackId)
            {
                CurrentTrack = CurrentTrack with { Liked = !liked };
                OnStateChanged?.Invoke();
            }
            LikeChanged?.Invoke(!liked, track.Title, track.Artists);
        }
        catch (Exception ex) { Logger.Log($"Spotify like toggle failed: {ex.Message}"); }
    }

    // ── /me/library (unified save / remove / contains) ─────────────────
    // Called directly over HTTP: SpotifyAPI.Web 7.4's SaveItems/RemoveItems requests made
    // Spotify answer "Missing required field: uris". Spotify wants full URIs
    // (spotify:track:ID); we send them both as the ?uris= query and in the JSON body.

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string TrackUri(string trackId) =>
        trackId.StartsWith("spotify:", StringComparison.Ordinal) ? trackId : $"spotify:track:{trackId}";

    private string AccessToken =>
        _tokenResponse?.AccessToken ?? throw new InvalidOperationException("Spotify is not connected");

    private async Task LibraryWriteAsync(HttpMethod method, string uri)
    {
        var url = $"https://api.spotify.com/v1/me/library?uris={Uri.EscapeDataString(uri)}";
        using var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent($"{{\"uris\":[\"{uri}\"]}}", System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
    }

    private async Task<bool?> LibraryContainsAsync(string uri)
    {
        try
        {
            var url = $"https://api.spotify.com/v1/me/library/contains?uris={Uri.EscapeDataString(uri)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"Spotify liked check failed: {(int)resp.StatusCode} {body}");
                return null;
            }
            // Response is a JSON array of booleans, e.g. [true]
            var t = body.Trim().TrimStart('[').TrimEnd(']').Split(',')[0].Trim();
            return t.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Log($"Spotify liked check failed: {ex.Message}");
            return null;
        }
    }

    public async Task SetVolumeAsync(int percent)
    {
        if (_client == null) return;
        percent = Math.Clamp(percent, 0, 100);
        try { await _client.Player.SetVolume(new PlayerVolumeRequest(percent)); }
        catch (Exception ex) { Logger.Log($"Spotify volume failed: {ex.Message}"); }
    }

    private async Task ShutdownServerAsync()
    {
        if (_server != null)
        {
            try { await _server.Stop(); } catch { }
            _server.Dispose();
            _server = null;
        }
    }

    public void Dispose()
    {
        StopAutoRestore();
        StopPolling();
        ShutdownServerAsync().GetAwaiter().GetResult();
    }
}

/// <summary>Immutable snapshot of the currently-playing track. Null when
/// there's no active playback.</summary>
public record SpotifyTrackState(
    string TrackId,
    string Title,
    string Artists,
    string AlbumUri,
    string AlbumArtUrl,
    bool IsPlaying,
    bool Shuffle,
    string RepeatState,
    int VolumePercent,
    bool Liked)
{
    public static SpotifyTrackState FromPlayback(CurrentlyPlayingContext? pb)
    {
        if (pb?.Item is not FullTrack track)
            return new SpotifyTrackState("", "", "", "", "", false, false, "off", 0, false);

        string artists = string.Join(", ", track.Artists?.ConvertAll(a => a.Name) ?? new());
        var art = track.Album?.Images;
        string artUrl = (art != null && art.Count > 0) ? art[art.Count - 1].Url : ""; // smallest
        string albumUri = track.Album?.Uri ?? "";
        return new SpotifyTrackState(
            TrackId: track.Id ?? "",
            Title: track.Name ?? "",
            Artists: artists,
            AlbumUri: albumUri,
            AlbumArtUrl: artUrl,
            IsPlaying: pb.IsPlaying,
            Shuffle: pb.ShuffleState,
            RepeatState: pb.RepeatState ?? "off",
            VolumePercent: pb.Device?.VolumePercent ?? 0,
            Liked: false);
    }
}
