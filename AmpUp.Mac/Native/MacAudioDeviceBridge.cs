using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AmpUp.Core;

namespace AmpUp.Mac.Native;

/// <summary>
/// P/Invoke bridge to the Swift audio device enumeration functions in libAmpUpAudio.dylib.
/// Falls back gracefully when the dylib is not available (e.g. on Windows dev machine).
/// </summary>
public static class MacAudioDeviceBridge
{
    private const string LibName = "libAmpUpAudio";

    // ── Delegates matching Swift @convention(c) callbacks ───────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeviceEnumCallback(uint deviceId, IntPtr name, bool isOutput, bool isInput);

    // ── P/Invoke declarations ──────────────────────────────────────────────

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ampup_enumerate_audio_devices(DeviceEnumCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ampup_get_default_output_device();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ampup_get_default_input_device();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ampup_set_default_output_device(uint deviceId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ampup_set_default_input_device(uint deviceId);

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a macOS audio device with its ID, name, and direction capabilities.
    /// </summary>
    public class AudioDeviceInfo
    {
        public uint DeviceId { get; set; }
        public string Name { get; set; } = "";
        public bool IsOutput { get; set; }
        public bool IsInput { get; set; }
    }

    /// <summary>
    /// Enumerate all audio devices on the system.
    /// Returns empty list if the native library is unavailable.
    /// </summary>
    public static List<AudioDeviceInfo> GetAllDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            ampup_enumerate_audio_devices((id, namePtr, isOutput, isInput) =>
            {
                string name = namePtr != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(namePtr) ?? $"Device {id}"
                    : $"Device {id}";

                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = id,
                    Name = name,
                    IsOutput = isOutput,
                    IsInput = isInput,
                });
            });
        }
        catch (DllNotFoundException)
        {
            Logger.Log("MacAudioDeviceBridge: libAmpUpAudio not available (expected on non-Mac)");
        }
        catch (Exception ex)
        {
            Logger.Log($"MacAudioDeviceBridge: GetAllDevices error: {ex.Message}");
        }

        return devices;
    }

    /// <summary>Get all output devices.</summary>
    public static List<AudioDeviceInfo> GetOutputDevices()
        => GetAllDevices().FindAll(d => d.IsOutput);

    /// <summary>Get all input devices.</summary>
    public static List<AudioDeviceInfo> GetInputDevices()
        => GetAllDevices().FindAll(d => d.IsInput);

    /// <summary>Get the default output device ID. Returns 0 if unavailable.</summary>
    public static uint GetDefaultOutputDeviceId()
    {
        try { return ampup_get_default_output_device(); }
        catch { return 0; }
    }

    /// <summary>Get the default input device ID. Returns 0 if unavailable.</summary>
    public static uint GetDefaultInputDeviceId()
    {
        try { return ampup_get_default_input_device(); }
        catch { return 0; }
    }

    /// <summary>Set the default output device. Returns true on success.</summary>
    public static bool SetDefaultOutputDevice(uint deviceId)
    {
        try { return ampup_set_default_output_device(deviceId); }
        catch (Exception ex)
        {
            Logger.Log($"MacAudioDeviceBridge: SetDefaultOutputDevice error: {ex.Message}");
            return false;
        }
    }

    /// <summary>Set the default input device. Returns true on success.</summary>
    public static bool SetDefaultInputDevice(uint deviceId)
    {
        try { return ampup_set_default_input_device(deviceId); }
        catch (Exception ex)
        {
            Logger.Log($"MacAudioDeviceBridge: SetDefaultInputDevice error: {ex.Message}");
            return false;
        }
    }
}
