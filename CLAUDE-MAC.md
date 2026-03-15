# AmpUp macOS Port — Development Guide

## Status: Phase 0 — Core Extraction (in progress)

Extracting `AmpUp.Core` shared library from the Windows WPF app so both platforms can share backend code.

---

## Mac Development Machine

- **SSH:** `ssh audio@192.168.189.234` (key auth, no password)
- **macOS:** 26.2 (Tahoe) on Apple Silicon (ARM64, T8112)
- **Homebrew:** 5.1.0 (`/opt/homebrew/bin/brew`)
- **.NET 8 SDK:** 8.0.125 (`/opt/homebrew/opt/dotnet@8/bin/dotnet`)
- **DOTNET_ROOT:** `/opt/homebrew/opt/dotnet@8/libexec`
- **Shell:** zsh, PATH in `~/.zshrc`

## Turn Up Hardware on Mac

- **USB chip:** WCH CH343 — recognized natively by macOS, no driver needed
- **Serial port:** `/dev/tty.usbmodem51850014941`
- **Baud:** 115200
- **Protocol:** Identical to Windows (FE/FF framed, same commands)
- **Confirmed working:** Serial read/write tested successfully via .NET System.IO.Ports

## Architecture Plan

```
AmpUp.Core/          Shared .NET 8 class library (no platform deps)
├── Models/           Config data classes, enums
├── Protocol/         SerialReader, frame parsing
├── Engine/           RgbController, ButtonGestureEngine, VolumePipeline
├── Services/         GoveeCloudApi, AmbienceSync, HAIntegration, UpdateChecker
└── Interfaces/       IScreenCapture (platform abstractions)

AmpUp (Windows)       WPF app — references AmpUp.Core
AmpUp.Mac (future)    Avalonia app — references AmpUp.Core
```

## Per-App Audio Strategy (macOS)

**API:** Core Audio Process Tap API (macOS 14.2+, Apple-sanctioned)
- No kernel extensions, no virtual audio device, no driver install
- `AudioHardwareCreateProcessTap` intercepts per-process audio
- Scale audio buffer for volume control

**Implementation approach:**
1. Native Swift `.dylib` wrapping Process Tap API
2. C# P/Invoke into the dylib
3. Expose: `create_tap(pid)`, `set_volume(tapId, float)`, `list_audio_processes()`

**Reference implementations:**
- [FineTune](https://github.com/ronitsingh10/FineTune) — MIT, Swift, per-app volume via Process Taps
- SoundSource 6 (Rogue Amoeba) uses same API under the hood

**Min macOS version:** 14.2 (Sonoma)

## UI Framework

**Avalonia UI** — closest to WPF, C# code sharing, good macOS support
- Similar XAML syntax (`.axaml`)
- Shares all AmpUp.Core backend code
- Dark theme will need porting from WPF Theme.xaml

## Key Differences from Windows

| Feature | Windows | macOS |
|---------|---------|-------|
| Serial port | COM3 | /dev/tty.usbmodem* |
| Audio API | WASAPI (NAudio) | CoreAudio Process Tap |
| UI | WPF + WPF-UI | Avalonia |
| Tray | NotifyIcon | NSStatusBarItem |
| Media keys | SendInput VK_MEDIA_* | CGEvent |
| Monitor brightness | DDC/CI (dxva2.dll) | IOKit DDC/CI |
| Power mgmt | PowrProf.dll | IOKit |
| Screen capture | GDI | CGWindowListCreateImage |
| Config location | %AppData%\AmpUp | ~/.config/AmpUp |

## Phase Plan

| Phase | Description | Status |
|-------|-------------|--------|
| 0 | Extract AmpUp.Core shared library | **In Progress** |
| 1 | Mac serial + LEDs (proof of life) | Pending |
| 2 | Native Swift audio bridge (Process Tap) | Pending |
| 3 | Master volume + media keys + tray | Pending |
| 4 | Per-app volume via Process Taps | Pending |
| 5 | Port Avalonia UI views | Pending |
| 6 | Govee/HA/DreamView | Pending |
