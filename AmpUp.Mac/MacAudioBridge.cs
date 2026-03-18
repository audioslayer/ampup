using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AmpUp.Mac;

/// <summary>
/// Audio process info returned by the native bridge.
/// </summary>
public record AudioProcess(int Pid, string Name);

/// <summary>
/// P/Invoke wrapper for libAmpUpAudio.dylib — the native Swift audio bridge
/// that provides per-app volume control on macOS via audio taps.
/// </summary>
public sealed class MacAudioBridge : IDisposable
{
    // ───────────────────────────────────────────────────────────────
    // Native imports
    // ───────────────────────────────────────────────────────────────

    private const string LibName = "libAmpUpAudio";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ampup_audio_process_count();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnumerateCallback(int pid, IntPtr name, int isRunning);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_enumerate_processes(EnumerateCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern float ampup_get_master_volume();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_set_master_volume(float volume);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ampup_create_tap(int pid);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_destroy_tap(int pid);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_set_process_volume(int pid, float volume);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_set_process_mute(int pid, int muted);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern float ampup_get_process_peak(int pid);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern float ampup_get_process_volume(int pid);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_cleanup();

    // ───────────────────────────────────────────────────────────────
    // High-level C# API
    // ───────────────────────────────────────────────────────────────

    private bool _disposed;

    /// <summary>
    /// Enumerates all running audio processes via the native callback and
    /// returns them as a managed list.
    /// </summary>
    public List<AudioProcess> GetRunningAudioApps()
    {
        var results = new List<AudioProcess>();

        // The delegate must be kept alive for the duration of the native call.
        EnumerateCallback cb = (int pid, IntPtr namePtr, int isRunning) =>
        {
            if (isRunning == 0)
                return;

            string name = Marshal.PtrToStringUTF8(namePtr) ?? $"pid-{pid}";
            results.Add(new AudioProcess(pid, name));
        };

        ampup_enumerate_processes(cb);

        // Prevent the delegate from being collected before the native call returns.
        GC.KeepAlive(cb);

        return results;
    }

    /// <summary>
    /// Set per-app volume. Range: 0.0 (silence) to 2.0 (boost).
    /// </summary>
    public void SetAppVolume(int pid, float volume)
    {
        float clamped = Math.Clamp(volume, 0f, 2f);
        ampup_set_process_volume(pid, clamped);
    }

    /// <summary>
    /// Get the current peak level for a process (0.0 – 1.0).
    /// Requires an active tap on the pid.
    /// </summary>
    public float GetAppPeak(int pid)
    {
        return ampup_get_process_peak(pid);
    }

    /// <summary>
    /// Create an audio tap on a process so we can control its volume and read peaks.
    /// Returns true on success.
    /// </summary>
    public bool CreateTap(int pid)
    {
        return ampup_create_tap(pid) == 1;
    }

    /// <summary>
    /// Destroy the audio tap for a process, releasing native resources.
    /// </summary>
    public void DestroyTap(int pid)
    {
        ampup_destroy_tap(pid);
    }

    /// <summary>
    /// System master output volume (0.0 – 1.0).
    /// </summary>
    public float MasterVolume
    {
        get => ampup_get_master_volume();
        set => ampup_set_master_volume(Math.Clamp(value, 0f, 1f));
    }

    /// <summary>
    /// Release all native audio taps and resources.
    /// </summary>
    public void Cleanup()
    {
        ampup_cleanup();
    }

    // ───────────────────────────────────────────────────────────────
    // IDisposable
    // ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            Cleanup();
            _disposed = true;
        }
    }
}
