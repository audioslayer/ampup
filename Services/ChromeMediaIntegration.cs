using Windows.Media.Control;

namespace AmpUp.Services;

/// <summary>
/// Controls Chrome's media session (YouTube etc.) through Windows'
/// GlobalSystemMediaTransportControls, picking the session by app id so it
/// never sends a global media key that Spotify or another player could take.
/// </summary>
public static class ChromeMediaIntegration
{
    public static Task<bool> PlayPauseAsync() => SendAsync("play/pause", s => s.TryTogglePlayPauseAsync().AsTask());
    public static Task<bool> NextAsync() => SendAsync("next", s => s.TrySkipNextAsync().AsTask());
    public static Task<bool> PrevAsync() => SendAsync("previous", s => s.TrySkipPreviousAsync().AsTask());

    private static async Task<bool> SendAsync(
        string actionName,
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var chromeSessions = manager.GetSessions()
                .Where(s => IsChrome(s.SourceAppUserModelId))
                .ToList();

            // Prefer the tab that is playing, so pause hits the video you hear.
            var session = chromeSessions.FirstOrDefault(s =>
                    s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? chromeSessions.FirstOrDefault();

            if (session == null)
            {
                Logger.Log($"Chrome media: cannot {actionName}; no Chrome media session found.");
                return false;
            }

            bool ok = await command(session);
            if (!ok) Logger.Log($"Chrome media: {actionName} was rejected by the session.");
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Log($"Chrome media: {actionName} failed: {ex.Message}");
            return false;
        }
    }

    // Classic installs report "chrome.exe"; Chrome PWAs and packaged builds
    // report an AUMID containing "Chrome".
    private static bool IsChrome(string? appId) =>
        !string.IsNullOrEmpty(appId)
        && appId.Contains("chrome", StringComparison.OrdinalIgnoreCase);
}
