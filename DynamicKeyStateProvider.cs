using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
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
///   spotify_playing   — Spotify has an active (non-paused) audio session on the default output
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
                    return IsProcessSessionActive("spotify");

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

    /// <summary>
    /// True when any WASAPI session on the default render endpoint whose process name
    /// contains <paramref name="processNameFragment"/> reports <c>AudioSessionStateActive</c>
    /// (i.e. producing audio right now — not paused or inactive).
    /// </summary>
    private static bool IsProcessSessionActive(string processNameFragment)
    {
        try
        {
            MMDevice? device;
            lock (_enumLock)
                device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device == null) return false;
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        if (s.State != AudioSessionState.AudioSessionStateActive) continue;
                        var pid = (int)s.GetProcessID;
                        if (pid == 0) continue;
                        var procName = Process.GetProcessById(pid).ProcessName;
                        if (procName.Contains(processNameFragment, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>Labels for the editor UI. Keep order stable.</summary>
    public static readonly (string Source, string Label)[] Sources =
    {
        ("mute_master",     "Master Muted"),
        ("mute_mic",        "Mic Muted"),
        ("obs_recording",   "OBS Recording"),
        ("obs_streaming",   "OBS Streaming"),
        ("spotify_playing", "Spotify Playing"),
        ("discord_mic",     "Discord Mic Muted (coming soon)"),
    };
}
