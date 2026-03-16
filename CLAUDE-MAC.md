# AmpUp macOS Port — Development Guide

## Status: v0.1.0-alpha RELEASED

First macOS alpha released. Per-app volume control working. All views built.
GitHub release: `v0.1.0-alpha-mac` on `mac` branch.

---

## Mac Development Machine

- **SSH:** `ssh audio@192.168.189.234` (key auth, no password needed)
- **macOS:** 26.2 (Tahoe) on Apple Silicon (ARM64, T8112)
- **Homebrew:** 5.1.0 (`/opt/homebrew/bin/brew`)
- **.NET 8 SDK:** 8.0.125 (`/opt/homebrew/opt/dotnet@8/bin/dotnet`)
- **DOTNET_ROOT:** `/opt/homebrew/opt/dotnet@8/libexec`
- **Swift:** 6.2.3 (Xcode CLI tools installed)
- **Shell:** zsh, PATH in `~/.zshrc`
- **Mac project location:** `~/Projects/AmpUp.Mac/`
- **Core clone:** `~/Projects/ampup-core/` (cloned from GitHub, pull for updates)

## Development Workflow

- Edit code from Windows (this machine), build/run via SSH to Mac
- Can't build Mac app on Windows — Swift compiler + CoreAudio only on macOS
- `./build.sh` — compiles Swift bridge + dotnet build
- `./run.sh` — build + launch app
- Must run from Terminal for audio capture TCC permission prompt
- Config location: `~/Library/Application Support/AmpUp/config.json`
- Log location: `~/Library/Application Support/AmpUp/ampup.log`

## Turn Up Hardware on Mac

- **USB chip:** WCH CH343 — recognized natively by macOS, no driver needed
- **Serial port:** `/dev/tty.usbmodem51850014941`
- **Baud:** 115200
- **Protocol:** Identical to Windows (FE/FF framed, same commands)
- **Confirmed working:** All knobs, buttons, LEDs, serial protocol

## Architecture

```
AmpUp.Core/              Shared .NET 8 class library (from GitHub master)
├── Models/               Config data classes, enums
├── Protocol/             SerialReader, frame parsing
├── Engine/               RgbController, ButtonGestureEngine, VolumePipeline, AutoProfileSwitcher
├── Services/             GoveeCloudApi, AmbienceSync, HAIntegration, DreamSyncController, UpdateChecker
└── Interfaces/           IScreenCapture

AmpUp.Mac/                macOS Avalonia app
├── Native/
│   └── AmpUpAudio.swift  Native Swift .dylib — Core Audio Process Tap API
├── MacAudioBridge.cs     C# P/Invoke wrapper for Swift dylib
├── MacAudioMixer.cs      Knob → per-app volume (process matching, tap management)
├── MacPlatformServices.cs Media keys, master mute, foreground app detection
├── Views/
│   ├── MixerView.axaml   5 channel strips with volume bars, app pills, curve display
│   ├── ButtonsView.axaml 5 button columns with TAP/DBL/HOLD gesture display
│   ├── LightsView.axaml  LED effect display with live color previews
│   └── SettingsView.axaml Connection, profiles, brightness, about
├── MainWindow.axaml      Sidebar nav + content area
├── App.axaml.cs          App orchestrator (serial, audio, RGB wiring)
├── build.sh              Compile Swift + dotnet build
└── run.sh                Build + launch
```

## Per-App Audio — WORKING

**API:** Core Audio Process Tap API (macOS 14.2+)
- `CATapDescription(stereoMixdownOfProcesses:)` with `.mutedWhenTapped`
- Aggregate device with output sub-device (stacked)
- Volume control via Float32 buffer scaling in IO callback
- 30ms exponential ramp to prevent clicks
- Peak level metering for VU meters

**Native Swift bridge** (`libAmpUpAudio.dylib`) C API:
- `ampup_enumerate_processes(callback)` — list apps producing audio
- `ampup_create_tap(pid)` / `ampup_destroy_tap(pid)` — manage per-app taps
- `ampup_set_process_volume(pid, volume)` — 0.0 to 2.0
- `ampup_get_process_peak(pid)` — peak level for VU meters
- `ampup_get/set_master_volume()` — system volume
- `ampup_set_process_mute(pid, muted)` — per-app mute

**TCC Permission:** Requires "System Audio Recording" consent on first launch. Must run from Terminal for prompt to appear (SSH doesn't trigger GUI dialog).

**Tested:** YouTube (Chrome) + Spotify controlled independently via separate knobs.

## Git Branch Strategy

- `master` — Windows WPF app + AmpUp.Core shared library
- `mac` — Mac release tags (v0.1.0-alpha-mac)
- Mac source lives on the Mac at `~/Projects/AmpUp.Mac/` (not in main repo yet)
- Core changes on master: `git pull` in `~/Projects/ampup-core/` on Mac

## Key Differences from Windows

| Feature | Windows | macOS |
|---------|---------|-------|
| Serial port | COM3 | /dev/tty.usbmodem* |
| Audio API | WASAPI (NAudio) | CoreAudio Process Tap |
| UI | WPF + WPF-UI | Avalonia |
| Tray | NotifyIcon | Not yet (planned) |
| Media keys | SendInput VK_MEDIA_* | osascript / CGEvent |
| Monitor brightness | DDC/CI (dxva2.dll) | Not yet |
| Power mgmt | PowrProf.dll | osascript / pmset |
| Screen capture | GDI | Not yet |
| Config location | %AppData%\AmpUp | ~/Library/Application Support/AmpUp |
| Installer | Inno Setup .exe | .dmg (drag to folder) |

## Completed Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 0 | Extract AmpUp.Core shared library | **Done** |
| 1 | Mac serial + LEDs (proof of life) | **Done** |
| 2 | Native Swift audio bridge (Process Tap) | **Done** |
| 3 | Wire audio bridge into C# Mac app | **Done** |
| 4 | Port Avalonia UI views | **Done** (MixerView, ButtonsView, LightsView, SettingsView) |
| 5 | Master volume + media keys | **Done** |
| 6 | Govee/HA/DreamView | Pending |

## Remaining Work

- Menu bar tray icon (NSStatusBarItem)
- Editable views (knob target picker, button action picker, light effect picker)
- Proper .app bundle (currently runs as bare executable)
- Govee LAN/Cloud integration (code in Core, needs wiring)
- Home Assistant integration (code in Core, needs wiring)
- DreamView screen capture (needs macOS CGWindowList implementation)
- Auto-update (currently just checks + opens browser)
- Intel Mac support (x86_64 build)
