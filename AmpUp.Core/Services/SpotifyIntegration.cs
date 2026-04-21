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

    /// <summary>
    /// Try to rehydrate from a previously-saved refresh token. Returns true
    /// when an existing session was restored successfully. Call at app
    /// startup before any user-facing Connect button.
    /// </summary>
    public async Task<bool> TryRestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId)) return false;
        if (string.IsNullOrWhiteSpace(_config.RefreshToken)) return false;
        try
        {
            var refreshed = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(_config.ClientId, _config.RefreshToken));
            await FinalizeLoginAsync(refreshed);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Spotify restore failed: {ex.Message}");
            return false;
        }
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
    }

    public void Disconnect()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        _client = null;
        _authenticator = null;
        _tokenResponse = null;
        _config.RefreshToken = "";
        _config.ConnectedUser = "";
        CurrentTrack = null;
        _persist?.Invoke(_config);
        OnStateChanged?.Invoke();
    }

    private void StartPolling()
    {
        _pollCts?.Cancel();
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
                catch (Exception ex) { Logger.Log($"Spotify poll error: {ex.Message}"); }

                try { await Task.Delay(PollIntervalMs, ct); } catch { }
            }
        }, ct);
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
            try
            {
                var liked = await _client.Library.CheckItems(new LibraryCheckItemsRequest(new[] { next.TrackId }));
                if (liked?.Count > 0) next = next with { Liked = liked[0] };
            }
            catch { /* best-effort */ }
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
        if (_client == null || string.IsNullOrEmpty(CurrentTrack?.TrackId)) return;
        try
        {
            bool liked = CurrentTrack?.Liked ?? false;
            if (liked)
                await _client.Library.RemoveItems(new LibraryRemoveItemsRequest(new[] { CurrentTrack!.TrackId }));
            else
                await _client.Library.SaveItems(new LibrarySaveItemsRequest(new[] { CurrentTrack!.TrackId }));
        }
        catch (Exception ex) { Logger.Log($"Spotify like toggle failed: {ex.Message}"); }
        _ = Task.Run(async () => { await Task.Delay(350); try { await PollOnceAsync(default); } catch { } });
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
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
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
