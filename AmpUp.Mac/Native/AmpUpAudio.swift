import Foundation
import CoreAudio
import AudioToolbox
import Cocoa

// ============================================================================
// AmpUp Native Audio Bridge v2 — Per-App Volume via Core Audio Process Taps
// Uses mutedWhenTapped + aggregate output for real volume control
// Requires macOS 14.2+
// ============================================================================

// MARK: - Tap State

class AppTap {
    let pid: pid_t
    let objectID: AudioObjectID
    var tapID: AudioObjectID = kAudioObjectUnknown
    var aggregateID: AudioObjectID = kAudioObjectUnknown
    var ioProcID: AudioDeviceIOProcID?
    nonisolated(unsafe) var volume: Float = 1.0  // written main thread, read audio thread
    nonisolated(unsafe) var currentVolume: Float = 1.0  // smoothed
    nonisolated(unsafe) var peakLevel: Float = 0.0
    nonisolated(unsafe) var isMuted: Bool = false

    init(pid: pid_t, objectID: AudioObjectID) {
        self.pid = pid
        self.objectID = objectID
    }
}

private var activeTaps: [pid_t: AppTap] = [:]
private let tapLock = NSLock()

// Volume ramp coefficient (~30ms at 48kHz)
private let rampCoeff: Float = 0.005

// MARK: - Output Device Helper

func getDefaultOutputDeviceUID() -> String? {
    var defaultDevice = AudioObjectID(kAudioObjectUnknown)
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var size = UInt32(MemoryLayout<AudioObjectID>.size)
    guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &defaultDevice) == noErr else { return nil }

    var uidAddress = AudioObjectPropertyAddress(
        mSelector: kAudioDevicePropertyDeviceUID,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var uid: Unmanaged<CFString>?
    var uidSize = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
    guard AudioObjectGetPropertyData(defaultDevice, &uidAddress, 0, nil, &uidSize, &uid) == noErr,
          let uidStr = uid?.takeRetainedValue() as String? else { return nil }
    return uidStr
}

// MARK: - Process Discovery

func getAudioProcesses() -> [(pid: pid_t, name: String, objectID: AudioObjectID)] {
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyProcessObjectList,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var size: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(
        AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size
    ) == noErr else { return [] }

    let count = Int(size) / MemoryLayout<AudioObjectID>.size
    var objectIDs = [AudioObjectID](repeating: 0, count: count)
    guard AudioObjectGetPropertyData(
        AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &objectIDs
    ) == noErr else { return [] }

    var results: [(pid_t, String, AudioObjectID)] = []
    for objID in objectIDs {
        var pidAddr = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyPID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var pid: pid_t = 0
        var pidSize = UInt32(MemoryLayout<pid_t>.size)
        guard AudioObjectGetPropertyData(objID, &pidAddr, 0, nil, &pidSize, &pid) == noErr else { continue }

        var nameAddr = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyBundleID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var nameRef: Unmanaged<CFString>?
        var nameSize = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        var name = "Unknown"
        if AudioObjectGetPropertyData(objID, &nameAddr, 0, nil, &nameSize, &nameRef) == noErr,
           let n = nameRef?.takeRetainedValue() as String? {
            name = n
        }

        var runAddr = AudioObjectPropertyAddress(
            mSelector: kAudioProcessPropertyIsRunning,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var isRunning: UInt32 = 0
        var runSize = UInt32(MemoryLayout<UInt32>.size)
        if AudioObjectGetPropertyData(objID, &runAddr, 0, nil, &runSize, &isRunning) == noErr,
           isRunning > 0 {
            results.append((pid, name, objID))
        }
    }
    return results
}

func pidToObjectID(_ pid: pid_t) -> AudioObjectID? {
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyTranslatePIDToProcessObject,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var objectID = AudioObjectID(kAudioObjectUnknown)
    var size = UInt32(MemoryLayout<AudioObjectID>.size)
    var pidVar = pid
    let status = AudioObjectGetPropertyData(
        AudioObjectID(kAudioObjectSystemObject), &address,
        UInt32(MemoryLayout<pid_t>.size), &pidVar,
        &size, &objectID
    )
    return status == noErr ? objectID : nil
}

// MARK: - Tap Management (FineTune-style with volume control)

func createTap(for pid: pid_t) -> Bool {
    tapLock.lock()
    defer { tapLock.unlock() }

    if activeTaps[pid] != nil { return true }

    guard let objectID = pidToObjectID(pid) else {
        NSLog("AmpUpAudio: Failed to get objectID for PID %d", pid)
        return false
    }

    guard let outputUID = getDefaultOutputDeviceUID() else {
        NSLog("AmpUpAudio: Failed to get default output device UID")
        return false
    }

    let tap = AppTap(pid: pid, objectID: objectID)

    // Create tap with mutedWhenTapped — we control the volume via buffer scaling
    let tapDesc = CATapDescription(stereoMixdownOfProcesses: [objectID])
    tapDesc.uuid = UUID()
    tapDesc.name = "AmpUp-\(pid)"
    tapDesc.muteBehavior = CATapMuteBehavior.mutedWhenTapped
    tapDesc.isPrivate = true

    var tapID: AudioObjectID = kAudioObjectUnknown
    let status = AudioHardwareCreateProcessTap(tapDesc, &tapID)
    guard status == noErr else {
        NSLog("AmpUpAudio: Failed to create tap for PID %d: %d", pid, status)
        return false
    }
    tap.tapID = tapID

    // Create aggregate device with output sub-device + tap
    let tapUID = tapDesc.uuid.uuidString
    let aggDesc: [String: Any] = [
        kAudioAggregateDeviceNameKey: "AmpUp-Tap-\(pid)",
        kAudioAggregateDeviceUIDKey: UUID().uuidString,
        kAudioAggregateDeviceMainSubDeviceKey: outputUID,
        kAudioAggregateDeviceClockDeviceKey: outputUID,
        kAudioAggregateDeviceIsPrivateKey: true,
        kAudioAggregateDeviceIsStackedKey: true,
        kAudioAggregateDeviceTapAutoStartKey: true,
        kAudioAggregateDeviceSubDeviceListKey: [
            [
                kAudioSubDeviceUIDKey: outputUID,
                kAudioSubDeviceDriftCompensationKey: false
            ] as [String: Any]
        ],
        kAudioAggregateDeviceTapListKey: [
            [
                kAudioSubTapDriftCompensationKey: true,
                kAudioSubTapUIDKey: tapUID
            ] as [String: Any]
        ]
    ]

    var aggregateID: AudioObjectID = kAudioObjectUnknown
    let aggStatus = AudioHardwareCreateAggregateDevice(aggDesc as CFDictionary, &aggregateID)
    guard aggStatus == noErr else {
        NSLog("AmpUpAudio: Failed to create aggregate for PID %d: %d", pid, aggStatus)
        AudioHardwareDestroyProcessTap(tapID)
        return false
    }
    tap.aggregateID = aggregateID

    // Brief wait for aggregate to be ready
    Thread.sleep(forTimeInterval: 0.05)

    // Create IO proc — scale input buffer by volume, copy to output
    let tapPtr = Unmanaged.passRetained(tap).toOpaque()  // retained so it stays alive
    var ioProcID: AudioDeviceIOProcID?
    let ioStatus = AudioDeviceCreateIOProcID(aggregateID, { (_, _, inInputData, _, outOutputData, _, clientData) -> OSStatus in
        guard let clientData = clientData else { return noErr }
        let t = Unmanaged<AppTap>.fromOpaque(clientData).takeUnretainedValue()

        let inputList = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: inInputData))
        let outputList = UnsafeMutableAudioBufferListPointer(outOutputData)

        let targetVol = t.isMuted ? Float(0) : t.volume
        var peak: Float = 0
        for i in 0..<min(inputList.count, outputList.count) {
            guard let inData = inputList[i].mData,
                  let outData = outputList[i].mData else {
                if let outData = outputList[i].mData {
                    memset(outData, 0, Int(outputList[i].mDataByteSize))
                }
                continue
            }

            let inSamples = inData.assumingMemoryBound(to: Float.self)
            let outSamples = outData.assumingMemoryBound(to: Float.self)
            let sampleCount = Int(inputList[i].mDataByteSize) / MemoryLayout<Float>.size
            for s in 0..<sampleCount {
                // Smooth volume ramp to prevent clicks
                t.currentVolume += (targetVol - t.currentVolume) * rampCoeff
                let sample = inSamples[s] * t.currentVolume
                outSamples[s] = sample

                let absSample = Swift.abs(sample)
                if absSample > peak { peak = absSample }
            }
        }

        // Clear any extra output buffers
        if outputList.count > inputList.count {
            for i in inputList.count..<outputList.count {
                if let outData = outputList[i].mData {
                    memset(outData, 0, Int(outputList[i].mDataByteSize))
                }
            }
        }

        t.peakLevel = peak
        return noErr
    }, tapPtr, &ioProcID)

    guard ioStatus == noErr else {
        NSLog("AmpUpAudio: Failed to create IO proc for PID %d: %d", pid, ioStatus)
        AudioHardwareDestroyAggregateDevice(aggregateID)
        AudioHardwareDestroyProcessTap(tapID)
        Unmanaged<AppTap>.fromOpaque(tapPtr).release()
        return false
    }
    tap.ioProcID = ioProcID

    let startStatus = AudioDeviceStart(aggregateID, ioProcID)
    guard startStatus == noErr else {
        NSLog("AmpUpAudio: Failed to start IO for PID %d: %d", pid, startStatus)
        AudioDeviceDestroyIOProcID(aggregateID, ioProcID!)
        AudioHardwareDestroyAggregateDevice(aggregateID)
        AudioHardwareDestroyProcessTap(tapID)
        Unmanaged<AppTap>.fromOpaque(tapPtr).release()
        return false
    }

    activeTaps[pid] = tap
    NSLog("AmpUpAudio: Created volume-control tap for PID %d", pid)
    return true
}

func destroyTap(for pid: pid_t) {
    tapLock.lock()
    defer { tapLock.unlock() }

    guard let tap = activeTaps.removeValue(forKey: pid) else { return }

    // Teardown order matters: Stop -> DestroyIOProc -> DestroyAggregate -> DestroyTap
    if let procID = tap.ioProcID {
        AudioDeviceStop(tap.aggregateID, procID)
        AudioDeviceDestroyIOProcID(tap.aggregateID, procID)
    }
    if tap.aggregateID != kAudioObjectUnknown {
        AudioHardwareDestroyAggregateDevice(tap.aggregateID)
    }
    if tap.tapID != kAudioObjectUnknown {
        AudioHardwareDestroyProcessTap(tap.tapID)
    }
    // Release the retained reference
    Unmanaged.passUnretained(tap).release()
    NSLog("AmpUpAudio: Destroyed tap for PID %d", pid)
}

// MARK: - Master Volume

func getMasterVolume() -> Float {
    var defaultDevice = AudioObjectID(kAudioObjectUnknown)
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var size = UInt32(MemoryLayout<AudioObjectID>.size)
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &defaultDevice)

    var volume: Float32 = 0
    var volAddress = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    var volSize = UInt32(MemoryLayout<Float32>.size)
    AudioObjectGetPropertyData(defaultDevice, &volAddress, 0, nil, &volSize, &volume)
    return volume
}

func setMasterVolume(_ volume: Float) {
    var defaultDevice = AudioObjectID(kAudioObjectUnknown)
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var size = UInt32(MemoryLayout<AudioObjectID>.size)
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &defaultDevice)

    var vol = max(0, min(1, volume))
    var volAddress = AudioObjectPropertyAddress(
        mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
        mScope: kAudioDevicePropertyScopeOutput,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectSetPropertyData(defaultDevice, &volAddress, 0, nil, UInt32(MemoryLayout<Float32>.size), &vol)
}

// MARK: - C API (for P/Invoke)

@_cdecl("ampup_audio_process_count")
public func ampup_audio_process_count() -> Int32 {
    return Int32(getAudioProcesses().count)
}

@_cdecl("ampup_get_master_volume")
public func ampup_get_master_volume() -> Float {
    return getMasterVolume()
}

@_cdecl("ampup_set_master_volume")
public func ampup_set_master_volume(_ volume: Float) {
    setMasterVolume(volume)
}

@_cdecl("ampup_create_tap")
public func ampup_create_tap(_ pid: Int32) -> Int32 {
    return createTap(for: pid) ? 1 : 0
}

@_cdecl("ampup_destroy_tap")
public func ampup_destroy_tap(_ pid: Int32) {
    destroyTap(for: pid)
}

@_cdecl("ampup_set_process_volume")
public func ampup_set_process_volume(_ pid: Int32, _ volume: Float) {
    tapLock.lock()
    activeTaps[pid]?.volume = max(0, min(2, volume))  // 0.0 to 2.0 (boost allowed)
    tapLock.unlock()
}

@_cdecl("ampup_set_process_mute")
public func ampup_set_process_mute(_ pid: Int32, _ muted: Int32) {
    tapLock.lock()
    activeTaps[pid]?.isMuted = muted != 0
    tapLock.unlock()
}

@_cdecl("ampup_get_process_peak")
public func ampup_get_process_peak(_ pid: Int32) -> Float {
    tapLock.lock()
    let peak = activeTaps[pid]?.peakLevel ?? 0
    tapLock.unlock()
    return peak
}

@_cdecl("ampup_get_process_volume")
public func ampup_get_process_volume(_ pid: Int32) -> Float {
    tapLock.lock()
    let vol = activeTaps[pid]?.volume ?? 1.0
    tapLock.unlock()
    return vol
}

@_cdecl("ampup_cleanup")
public func ampup_cleanup() {
    tapLock.lock()
    let pids = Array(activeTaps.keys)
    tapLock.unlock()
    for pid in pids {
        destroyTap(for: pid)
    }
}

public typealias ProcessCallback = @convention(c) (Int32, UnsafePointer<CChar>, Int32) -> Void

@_cdecl("ampup_enumerate_processes")
public func ampup_enumerate_processes(_ callback: ProcessCallback) {
    let processes = getAudioProcesses()
    for proc in processes {
        proc.name.withCString { namePtr in
            callback(proc.pid, namePtr, 1)
        }
    }
}

// MARK: - Media Key Simulation

@_cdecl("ampup_send_media_key")
public func ampup_send_media_key(_ keyType: Int32) {
    // NX_KEYTYPE_PLAY=16, NX_KEYTYPE_NEXT=17, NX_KEYTYPE_PREVIOUS=18
    func postKey(_ down: Bool) {
        let flags: Int32 = down ? 0xa00 : 0xb00
        let data1 = Int((keyType << 16) | flags)
        guard let event = NSEvent.otherEvent(
            with: .systemDefined, location: .zero,
            modifierFlags: NSEvent.ModifierFlags(rawValue: UInt(down ? 0xa0000 : 0xb0000)),
            timestamp: 0, windowNumber: 0, context: nil,
            subtype: 8, data1: data1, data2: -1
        ) else { return }
        event.cgEvent?.post(tap: .cghidEventTap)
    }
    postKey(true)
    postKey(false)
}

// MARK: - Audio Device Enumeration

public typealias DeviceEnumCallback = @convention(c) (UInt32, UnsafePointer<CChar>?, Bool, Bool) -> Void

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
        var nameAddr = AudioObjectPropertyAddress(
            mSelector: kAudioObjectPropertyName,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var name: CFString = "" as CFString
        var nameSize = UInt32(MemoryLayout<CFString>.size)
        AudioObjectGetPropertyData(deviceID, &nameAddr, 0, nil, &nameSize, &name)
        let nameStr = name as String
        var outputAddr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams,
            mScope: kAudioDevicePropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain
        )
        var outputSize: UInt32 = 0
        AudioObjectGetPropertyDataSize(deviceID, &outputAddr, 0, nil, &outputSize)
        var inputAddr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams,
            mScope: kAudioDevicePropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain
        )
        var inputSize: UInt32 = 0
        AudioObjectGetPropertyDataSize(deviceID, &inputAddr, 0, nil, &inputSize)
        guard outputSize > 0 || inputSize > 0 else { continue }
        nameStr.withCString { ptr in callback(deviceID, ptr, outputSize > 0, inputSize > 0) }
    }
}

@_cdecl("ampup_get_default_output_device")
public func ampup_get_default_output_device() -> UInt32 {
    var deviceID = AudioDeviceID(0)
    var size = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &deviceID)
    return deviceID
}

@_cdecl("ampup_get_default_input_device")
public func ampup_get_default_input_device() -> UInt32 {
    var deviceID = AudioDeviceID(0)
    var size = UInt32(MemoryLayout<AudioDeviceID>.size)
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultInputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &deviceID)
    return deviceID
}

@_cdecl("ampup_set_default_output_device")
public func ampup_set_default_output_device(_ deviceID: UInt32) -> Bool {
    var id = deviceID
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultOutputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    return AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, UInt32(MemoryLayout<AudioDeviceID>.size), &id) == noErr
}

@_cdecl("ampup_set_default_input_device")
public func ampup_set_default_input_device(_ deviceID: UInt32) -> Bool {
    var id = deviceID
    var addr = AudioObjectPropertyAddress(
        mSelector: kAudioHardwarePropertyDefaultInputDevice,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    return AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, UInt32(MemoryLayout<AudioDeviceID>.size), &id) == noErr
}
