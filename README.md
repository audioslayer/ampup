# WolfMixer 🐺

A lightweight C# Windows system tray app that replaces the official "Turn Up" software with proper per-app audio control, full RGB lighting, monitor brightness, and a clean dark-theme config UI.

Built because the official app is Electron bloat and kept breaking.

---

## Features

- **Per-app volume control** — map each knob to a specific process, master volume, mic input, active window, or any audio device
- **Response curves** — Linear, Logarithmic, or Exponential per-knob
- **Volume range** — set min/max so a knob only sweeps e.g. 40–100%
- **9 RGB lighting effects** — SingleColor, ColorBlend, Blink, Pulse, PositionFill, RainbowWave, RainbowCycle, MicStatus, DeviceMute
- **Monitor brightness** — DDC/CI control of physical monitors via Windows API
- **17 button actions** — media keys, mic/app mute, launch/kill apps, keyboard macros, audio device switching, system power actions, profile switching
- **3 gestures per button** — single press, double press, hold (15 total bindings across 5 buttons)
- **Profile system** — save/load/switch full configs per game, scene, or workflow
- **Audio device switching** — cycle or jump directly to any output/input device
- **Active window control** — one knob always controls whatever app is in focus
- **System tray** — zero taskbar footprint, right-click to configure
- **Hot-reload config** — save and apply without restarting
- **Start with Windows** — registry-based autostart

---

## Requirements

- Windows 10/11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK to build)
- Turn Up USB device

---

## Build

```powershell
git clone https://github.com/audioslayer/wolfmixer.git
cd wolfmixer
dotnet build
```

Exe output: `bin\Debug\net8.0-windows\WolfMixer.exe`

**Kill Turn Up before running** — it holds the COM port:
```powershell
taskkill /f /im "Turn Up.exe"
taskkill /f /im TurnUpService.exe
```

Then launch `WolfMixer.exe` — it lives in the system tray.

---

## Configuration

Right-click the tray icon → **Configure**. Four tabs:

| Tab | What you set |
|---|---|
| **Knobs** | Label, target app/device, response curve, volume range |
| **Buttons** | Action per press / double-press / hold, macros, paths |
| **Lights** | RGB effect, colors, speed, global brightness |
| **Settings** | Startup, serial port, profiles |

Hit **Save & Apply** — takes effect immediately, no restart needed.

---

## Knob Targets

| Target | Controls |
|---|---|
| `master` | Windows master volume |
| `mic` | Default microphone input level |
| `monitor` | Physical monitor brightness (DDC/CI) |
| `active_window` | Volume of whatever app is currently focused |
| `any` | First active audio session not already assigned |
| `discord` | App volume by process name substring |
| `spotify` | App volume by process name substring |
| `chrome` | App volume by process name substring |
| `game.exe` | Any process name substring works |
| `output_device` | Specific audio output by device ID |
| `input_device` | Specific audio input by device ID |

---

## Button Actions

| Action | Description |
|---|---|
| `media_play_pause` | Play/pause media |
| `media_next / media_prev` | Track skip |
| `mute_master` | Toggle Windows mute |
| `mute_mic` | Toggle microphone mute |
| `mute_program` | Toggle mute on specific app |
| `mute_active_window` | Toggle mute on focused app |
| `launch_exe` | Launch app/script at path |
| `close_program` | Kill process by name |
| `cycle_output / cycle_input` | Rotate through audio devices |
| `select_output / select_input` | Jump to specific audio device |
| `macro` | Send keyboard combo (e.g. `ctrl+shift+m`) |
| `system_power` | Sleep, hibernate, lock, shutdown, restart, logoff |
| `switch_profile` | Load a saved profile |

Each button supports **single press**, **double press**, and **hold** independently.

---

## RGB Effects

| Effect | Description |
|---|---|
| `SingleColor` | Solid color, brightness tracks knob position |
| `ColorBlend` | Fades between two colors as knob moves |
| `PositionFill` | LEDs fill left→right as knob increases |
| `Blink` | Alternates between two colors |
| `Pulse` | Smooth sine-wave oscillation between two colors |
| `RainbowWave` | Animated rainbow sweeping across all knobs |
| `RainbowCycle` | Each LED gets its own hue, all cycling |
| `MicStatus` | Color 1 = unmuted, Color 2 = muted |
| `DeviceMute` | Color 1 = audio on, Color 2 = master muted |

---

## Architecture

Single `.exe`, no installer, no background service.

```
SerialReader.cs      Reads COM3 @ 115200 baud, parses Turn Up frame protocol
AudioMixer.cs        WASAPI per-app volume via NAudio, session polling, curves
ButtonHandler.cs     Gesture state machine → 17 action types
RgbController.cs     RGB effects engine, 20 FPS animation, gamma correction
MonitorBrightness.cs DDC/CI physical monitor brightness via dxva2.dll
Config.cs            Load/save config.json + profile system
TrayApp.cs           Wires everything together, system tray
ConfigForm.cs        4-tab dark-theme config UI
```

---

## Device Protocol

The Turn Up device communicates over CH343 USB-to-serial at 115200 baud. All frames use `0xFE` / `0xFF` as start/end bytes.

**Read frames (device → PC):**
- Knob move: `FE 03 [idx] [hi] [lo] FF` — 10-bit value (0–1023)
- Button down: `FE 06 [idx] FF`
- Button up: `FE 07 [idx] FF`
- Init batch: `FE 04 [all 5 knob values] FF`

**Write frames (PC → device):**
- RGB update: `FE 05 [45 bytes: 5 knobs × 3 LEDs × RGB] FF`
- Must be sent at ~20 FPS or device turns LEDs off

---

## Known Limitations

- **Monitor brightness** requires DDC/CI support — not all monitors support it
- **Single-press** has ~300ms latency to allow double-press detection
- **`any` target** picks the first active session it finds — may be unpredictable with many apps open
- **COM3** is hardcoded as default — configurable in Settings tab

---

## Roadmap

- [ ] OBS WebSocket integration (scene switching, source mute/gain, recording control)
- [ ] VoiceMeeter strip/bus control
- [ ] USB PnP auto-detection (no hardcoded COM3)
- [ ] Per-app volume HUD overlay
- [ ] Multiple Turn Up device support
- [ ] Single-file publish / installer

---

## License

MIT — do whatever you want with it.
