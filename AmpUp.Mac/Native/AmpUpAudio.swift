// AmpUpAudio.swift — Native Swift bridge for Core Audio Process Tap API
// Compiled to libAmpUpAudio.dylib, called via P/Invoke from MacAudioBridge.cs
//
// Requires macOS 14.2+ for CATapDescription(stereoMixdownOfProcesses:)
// Build: swiftc -target arm64-apple-macos14.2 -emit-library -o libAmpUpAudio.dylib AmpUpAudio.swift \
//        -framework CoreAudio -framework AudioToolbox -framework Foundation

import Foundation
import CoreAudio
import AudioToolbox

// MARK: - Process enumeration

public typealias ProcessEnumCallback = @convention(c) (pid_t, UnsafePointer<CChar>?) -> Void

@_cdecl("ampup_enumerate_processes")
public func ampup_enumerate_processes(_ callback: ProcessEnumCallback) {
    var propertyAddress = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyProcessObjectList,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )

    var dataSize: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject),
                                         &propertyAddress, 0, nil, &dataSize) == noErr else { return }

    let count = Int(dataSize) / MemoryLayout<AudioObjectID>.size
    var pids = [AudioObjectID](repeating: 0, count: count)
    guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                     &propertyAddress, 0, nil, &dataSize, &pids) == noErr else { return }

    for pid in pids {
        var nameAddr = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyBundleID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var bundleID: CFString = "" as CFString
        var bundleSize = UInt32(MemoryLayout<CFString>.size)
        AudioObjectGetPropertyData(pid, &nameAddr, 0, nil, &bundleSize, &bundleID)
        let bundleStr = bundleID as String
        bundleStr.withCString { ptr in
            callback(pid_t(pid), ptr)
        }
    }
}

// MARK: - Tap management

// Opaque tap state stored per PID
private struct TapState {
    var tapObjectID: AudioObjectID
    var aggregateDeviceID: AudioObjectID
    var volume: Float32
    var muted: Bool
    var peak: Float32
}

private var taps: [pid_t: TapState] = [:]
private let tapLock = NSLock()

@_cdecl("ampup_create_tap")
public func ampup_create_tap(_ pid: pid_t) -> Bool {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard taps[pid] == nil else { return true }

    // CATapDescription requires macOS 14.2+
    guard #available(macOS 14.2, *) else { return false }

    let tapDesc = CATapDescription(stereoMixdownOfProcesses: [UInt32(pid)])
    tapDesc.muteBehavior = .unmuted
    tapDesc.name = "AmpUp-\(pid)"

    var tapID: AUAudioObjectID = 0
    let tapErr = AudioHardwareCreateProcessTap(tapDesc, &tapID)
    if tapErr != noErr {
        NSLog("AmpUp: AudioHardwareCreateProcessTap failed for pid \(pid) with error \(tapErr)")
        return false
    }
    NSLog("AmpUp: Tap created for pid \(pid), tapID=\(tapID)")

    // Build aggregate device with the tap as sub-device
    let aggDesc: [String: Any] = [
        kAudioAggregateDeviceNameKey: "AmpUpAgg-\(pid)",
        kAudioAggregateDeviceUIDKey: "com.wolfden.ampup.agg.\(pid)",
        kAudioAggregateDeviceSubDeviceListKey: [[
            kAudioSubDeviceUIDKey: "AmpUpTap-\(pid)"
        ] as [String: Any]],
        kAudioAggregateDeviceIsPrivateKey: true,
        kAudioAggregateDeviceTapListKey: [[
            kAudioSubTapDriftCompensationKey: false,
            kAudioSubTapUIDKey: tapDesc.uuid.uuidString
        ] as [String: Any]]
    ]

    var aggDeviceID: AudioDeviceID = 0
    let aggErr = AudioHardwareCreateAggregateDevice(aggDesc as CFDictionary, &aggDeviceID)
    if aggErr != noErr {
        NSLog("AmpUp: AudioHardwareCreateAggregateDevice failed for pid \(pid) with error \(aggErr)")
        AudioHardwareDestroyProcessTap(tapID)
        return false
    }
    NSLog("AmpUp: Aggregate device created for pid \(pid), aggDeviceID=\(aggDeviceID)")

    taps[pid] = TapState(
        tapObjectID: tapID,
        aggregateDeviceID: aggDeviceID,
        volume: 1.0,
        muted: false,
        peak: 0.0
    )
    return true
}

@_cdecl("ampup_destroy_tap")
public func ampup_destroy_tap(_ pid: pid_t) {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard let state = taps[pid] else { return }
    AudioHardwareDestroyAggregateDevice(state.aggregateDeviceID)
    AudioHardwareDestroyProcessTap(state.tapObjectID)
    taps.removeValue(forKey: pid)
}

// MARK: - Volume control

@_cdecl("ampup_set_process_volume")
public func ampup_set_process_volume(_ pid: pid_t, _ volume: Float32) {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard var state = taps[pid] else { return }
    state.volume = max(0.0, min(2.0, volume))
    taps[pid] = state

    // Apply volume to aggregate device output
    var vol = state.muted ? Float32(0.0) : state.volume
    var volAddr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    var volSize = UInt32(MemoryLayout<Float32>.size)
    AudioObjectSetPropertyData(state.aggregateDeviceID, &volAddr, 0, nil, volSize, &vol)
}

@_cdecl("ampup_get_process_peak")
public func ampup_get_process_peak(_ pid: pid_t) -> Float32 {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard let state = taps[pid] else { return 0.0 }

    var peak = Float32(0.0)
    var peakAddr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    var peakSize = UInt32(MemoryLayout<Float32>.size)
    AudioObjectGetPropertyData(state.aggregateDeviceID, &peakAddr, 0, nil, &peakSize, &peak)
    return peak
}

@_cdecl("ampup_set_process_mute")
public func ampup_set_process_mute(_ pid: pid_t, _ muted: Bool) {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard var state = taps[pid] else { return }
    state.muted = muted
    taps[pid] = state

    var vol = muted ? Float32(0.0) : state.volume
    var volAddr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    var volSize = UInt32(MemoryLayout<Float32>.size)
    AudioObjectSetPropertyData(state.aggregateDeviceID, &volAddr, 0, nil, volSize, &vol)
}

// MARK: - Master volume

@_cdecl("ampup_get_master_volume")
public func ampup_get_master_volume() -> Float32 {
    var defaultDevice = AudioDeviceID(0)
    var defaultDeviceSize = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                               &addr, 0, nil, &defaultDeviceSize, &defaultDevice)

    var vol = Float32(0.0)
    var volSize = UInt32(MemoryLayout<Float32>.size)
    var volAddr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(defaultDevice, &volAddr, 0, nil, &volSize, &vol)
    return vol
}

@_cdecl("ampup_set_master_volume")
public func ampup_set_master_volume(_ volume: Float32) {
    var defaultDevice = AudioDeviceID(0)
    var defaultDeviceSize = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                               &addr, 0, nil, &defaultDeviceSize, &defaultDevice)

    var vol = max(0.0, min(1.0, volume))
    var volSize = UInt32(MemoryLayout<Float32>.size)
    var volAddr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectSetPropertyData(defaultDevice, &volAddr, 0, nil, volSize, &vol)
}

// MARK: - Audio device enumeration

/// Callback for each audio device: (deviceID, name C-string, isOutput, isInput)
public typealias DeviceEnumCallback = @convention(c) (UInt32, UnsafePointer<CChar>?, Bool, Bool) -> Void

/// Enumerate all audio devices (output and input). Calls callback for each device.
@_cdecl("ampup_enumerate_audio_devices")
public func ampup_enumerate_audio_devices(_ callback: DeviceEnumCallback) {
    var propertyAddress = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDevices,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )

    var dataSize: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject),
                                         &propertyAddress, 0, nil, &dataSize) == noErr else { return }

    let count = Int(dataSize) / MemoryLayout<AudioDeviceID>.size
    var deviceIDs = [AudioDeviceID](repeating: 0, count: count)
    guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                     &propertyAddress, 0, nil, &dataSize, &deviceIDs) == noErr else { return }

    for deviceID in deviceIDs {
        // Get device name
        var nameAddr = AudioObjectPropertyAddress(
            mSelector: kAudioObjectPropertyName,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var name: CFString = "" as CFString
        var nameSize = UInt32(MemoryLayout<CFString>.size)
        AudioObjectGetPropertyData(deviceID, &nameAddr, 0, nil, &nameSize, &name)
        let nameStr = name as String

        // Check if device has output streams
        var outputAddr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams,
            mScope: kAudioDevicePropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain
        )
        var outputSize: UInt32 = 0
        AudioObjectGetPropertyDataSize(deviceID, &outputAddr, 0, nil, &outputSize)
        let hasOutput = outputSize > 0

        // Check if device has input streams
        var inputAddr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams,
            mScope: kAudioDevicePropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain
        )
        var inputSize: UInt32 = 0
        AudioObjectGetPropertyDataSize(deviceID, &inputAddr, 0, nil, &inputSize)
        let hasInput = inputSize > 0

        // Skip devices with no streams (virtual/aggregate noise)
        guard hasOutput || hasInput else { continue }

        nameStr.withCString { ptr in
            callback(deviceID, ptr, hasOutput, hasInput)
        }
    }
}

/// Get the default output device ID.
@_cdecl("ampup_get_default_output_device")
public func ampup_get_default_output_device() -> UInt32 {
    var deviceID = AudioDeviceID(0)
    var size = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                               &addr, 0, nil, &size, &deviceID)
    return deviceID
}

/// Get the default input device ID.
@_cdecl("ampup_get_default_input_device")
public func ampup_get_default_input_device() -> UInt32 {
    var deviceID = AudioDeviceID(0)
    var size = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultInputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                               &addr, 0, nil, &size, &deviceID)
    return deviceID
}

/// Set the default output device by device ID.
@_cdecl("ampup_set_default_output_device")
public func ampup_set_default_output_device(_ deviceID: UInt32) -> Bool {
    var id = deviceID
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    let size = UInt32(MemoryLayout<AudioDeviceID>.size)
    return AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                      &addr, 0, nil, size, &id) == noErr
}

/// Set the default input device by device ID.
@_cdecl("ampup_set_default_input_device")
public func ampup_set_default_input_device(_ deviceID: UInt32) -> Bool {
    var id = deviceID
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultInputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    let size = UInt32(MemoryLayout<AudioDeviceID>.size)
    return AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                      &addr, 0, nil, size, &id) == noErr
}
