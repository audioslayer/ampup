using NAudio.CoreAudioApi;
using AmpUp.Core.Services;

namespace AmpUp;

/// <summary>
/// Resolves "is this dynamic state active right now?" for Stream Controller
/// LCD keys configured with <c>DisplayKeyType.DynamicState</c>.
///
/// Supported sources:
///   mute_master       — default render endpoint is muted
///   mute_mic          — default communications capture endpoint is muted
///   obs_recording     — OBS is recording
///   obs_streaming     — OBS is streaming
///   spotify_playing   — Spotify is actively playing (NOT implemented — always false for MVP)
///   discord_mic       — Discord mic muted (NOT implemented — always false for MVP)
/// </summary>
internal static class DynamicKeyStateProvider
{
    private static readonly MMDeviceEnumerator _enumerator = new();
    private static readonly object _enumLock = new();

    public static bool IsActive(string source, ObsIntegration? obs, AudioMixer? mixer)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;

        try
        {
            switch (source)
            {
                case "mute_master":
                    return GetDefaultEndpointMute(DataFlow.Render, Role.Multimedia);

                case "mute_mic":
                    return GetDefaultEndpointMute(DataFlow.Capture, Role.Communications);

                case "obs_recording":
                    return obs?.IsRecording == true;

                case "obs_streaming":
                    return obs?.IsStreaming == true;

                case "spotify_playing":
                    // TODO: requires Windows SMTC / Spotify Web API integration — MVP returns false.
                    return false;

                case "discord_mic":
                    // TODO: no reliable public API for Discord mic mute — MVP returns false.
                    return false;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool GetDefaultEndpointMute(DataFlow flow, Role role)
    {
        try
        {
            MMDevice? dev;
            lock (_enumLock)
                dev = _enumerator.GetDefaultAudioEndpoint(flow, role);
            if (dev == null) return false;
            bool muted = dev.AudioEndpointVolume.Mute;
            dev.Dispose();
            return muted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Labels for the editor UI. Keep order stable.</summary>
    public static readonly (string Source, string Label)[] Sources =
    {
        ("mute_master",     "Master Muted"),
        ("mute_mic",        "Mic Muted"),
        ("obs_recording",   "OBS Recording"),
        ("obs_streaming",   "OBS Streaming"),
        ("spotify_playing", "Spotify Playing (coming soon)"),
        ("discord_mic",     "Discord Mic Muted (coming soon)"),
    };
}
