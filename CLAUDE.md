# WolfMixer — Claude Code Context

WolfMixer is a C# .NET 8 WinForms system tray app that replaces the "Turn Up" USB volume mixer app.
It reads serial events from the Turn Up hardware and maps them to Windows per-app audio volume control.

---

## Hardware — Turn Up Device

- **USB chip:** CH343 USB-to-serial
- **Port:** COM3 (stable, doesn't change on replug)
- **Baud rate:** 115200

### Serial Frame Protocol (fully reverse engineered)

All frames are wrapped with `0xFE` (start) and `0xFF` (end).

| Event | Frame | Notes |
|---|---|---|
| Knob move | `fe 03 [idx] [hi] [lo] ff` | 6 bytes. idx=0-4, value=hi*256+lo, range 0-1023 |
| Knob batch | `fe 04 [5x hi+lo] ff` | 13 bytes. All 5 knob values on connect |
| Health/ping | `fe 02 ff` | 3 bytes. Periodic heartbeat |
| Button down | `fe 06 [idx] ff` | 4 bytes. idx=0-4, button pressed |
| Button up | `fe 07 [idx] ff` | 4 bytes. idx=0-4, button released |
| Device ID | `fe 08 [4 bytes] ff` | 7 bytes. Sent on connect |

### Knob/Button Layout
- **Knob 0** = leftmost, **Knob 4** = rightmost
- **Button 0** = leftmost, **Button 4** = rightmost

---

## Architecture

Single `.exe`, no installer, no service. Lives in system tray.

```
WolfMixer.csproj        .NET 8 WinForms, NAudio + Newtonsoft.Json + System.IO.Ports
Program.cs              Entry point, single-instance mutex, ApplicationContext
SerialReader.cs         Reads COM3, parses fe/ff frames, fires OnKnob / OnButton events
AudioMixer.cs           WASAPI per-app volume, response curves, volume range, active window
ButtonHandler.cs        Gesture state machine (press/double/hold) → 17 action types
RgbController.cs        RGB effects engine — 9 effects, 20 FPS animation, 3 LEDs per knob
MonitorBrightness.cs    DDC/CI physical monitor brightness via dxva2.dll
Config.cs               Loads/saves config.json + profile system (Newtonsoft.Json)
TrayApp.cs              System tray icon, wires serial→audio→RGB→buttons, mute state polling
ConfigForm.cs           4-tab dark-themed config UI (Knobs, Buttons, Lights, Settings)
Logger.cs               Appends to wolfmixer.log next to exe
config.json             User config — knob targets, button actions, RGB effects, profiles
```

---

## config.json Format

```json
{
  "serial": { "port": "COM3", "baud": 115200 },
  "knobs": [
    { "idx": 0, "label": "Master", "target": "master", "minVolume": 0, "maxVolume": 100, "curve": "Linear" },
    { "idx": 1, "label": "Discord", "target": "discord" },
    { "idx": 2, "label": "Spotify", "target": "spotify" },
    { "idx": 3, "label": "Monitor", "target": "monitor" },
    { "idx": 4, "label": "Active", "target": "active_window" }
  ],
  "buttons": [
    {
      "idx": 0, "action": "media_prev",
      "holdAction": "mute_mic", "holdPath": "",
      "doublePressAction": "none", "doublePressPath": "",
      "macroKeys": "", "deviceId": "", "profileName": "", "powerAction": ""
    }
  ],
  "lights": [
    { "idx": 0, "r": 0, "g": 150, "b": 255, "effect": "SingleColor", "r2": 255, "g2": 0, "b2": 0, "effectSpeed": 50 }
  ],
  "startWithWindows": true,
  "ledBrightness": 100,
  "activeProfile": "Default",
  "profiles": ["Default"]
}
```

### Knob target values
| Target | Behavior |
|---|---|
| `"master"` | Windows master volume (default audio endpoint) |
| `"mic"` | Microphone input level (default capture device) |
| `"system"` | System Sounds session |
| `"any"` | First active audio session not already assigned |
| `"active_window"` | Volume of currently focused window's audio session |
| `"output_device"` | Specific output device volume (by `deviceId`) |
| `"input_device"` | Specific input device volume (by `deviceId`) |
| `"monitor"` | Physical monitor brightness via DDC/CI |
| `"discord"` | Matches process name containing "discord" |
| `"spotify"` | Matches process name containing "spotify" |
| `"chrome"` | Matches process name containing "chrome" |
| `"game.exe"` | Any substring of the process name works |

### Knob options
| Field | Default | Description |
|---|---|---|
| `minVolume` | 0 | Minimum volume % (knob at 0% = this value) |
| `maxVolume` | 100 | Maximum volume % (knob at 100% = this value) |
| `curve` | `"Linear"` | Response curve: `Linear`, `Logarithmic`, `Exponential` |
| `deviceId` | `""` | Audio device ID (for `output_device` / `input_device` targets) |

### Button action values (17 actions)
| Action | Behavior |
|---|---|
| `"none"` | Do nothing |
| `"media_play_pause"` | Send VK_MEDIA_PLAY_PAUSE |
| `"media_next"` | Send VK_MEDIA_NEXT_TRACK |
| `"media_prev"` | Send VK_MEDIA_PREV_TRACK |
| `"mute_master"` | Send VK_VOLUME_MUTE |
| `"mute_mic"` | Toggle default mic endpoint mute |
| `"mute_program"` | Toggle mute on app matching `path` (process name) |
| `"mute_active_window"` | Toggle mute on focused window's audio |
| `"launch_exe"` | Launch app at `path` |
| `"close_program"` | Kill process matching `path` |
| `"cycle_output"` | Cycle through output devices (all or `deviceIds` subset) |
| `"cycle_input"` | Cycle through input devices |
| `"select_output"` | Switch to specific output device by `deviceId` |
| `"select_input"` | Switch to specific input device by `deviceId` |
| `"macro"` | Send keyboard combo from `macroKeys` (e.g. `"ctrl+shift+m"`) |
| `"system_power"` | Execute `powerAction`: sleep, hibernate, lock, shutdown, restart, logoff |
| `"switch_profile"` | Switch to named profile from `profileName` |

### Button gesture types
Each button supports 3 gestures (15 total bindings):

| Gesture | Config fields | Detection |
|---|---|---|
| **Single press** | `action` + `path` | Released < 500ms, no 2nd press within 300ms |
| **Double press** | `doublePressAction` + `doublePressPath` | 2nd press within 300ms of first release |
| **Hold** | `holdAction` + `holdPath` | Held 500ms+ (fires while holding) |

### LED effects (9 types)
| Effect | Colors | Description |
|---|---|---|
| `SingleColor` | 1 | Color scaled by knob position (default) |
| `ColorBlend` | 2 | Lerp between color1→color2 based on knob position |
| `PositionFill` | 1 | LEDs light up left→right as knob increases |
| `Blink` | 2 | Alternate between color1/color2 at configurable speed |
| `Pulse` | 2 | Smooth sine-wave oscillation between color1/color2 |
| `RainbowWave` | — | HSV rainbow across all 5 knobs, animated |
| `RainbowCycle` | — | 3 LEDs per knob each get different hue, animated |
| `MicStatus` | 2 | Color1=unmuted, Color2=muted (mic state) |
| `DeviceMute` | 2 | Color1=unmuted, Color2=muted (master state) |

`effectSpeed` (1-100) controls animated effect timing. Global `ledBrightness` (0-100) scales all LEDs.

---

## RGB Write Protocol

- **Frame:** 48 bytes — `FE 05 [45 bytes RGB data] FF`
- **Layout:** 5 knobs × 3 LEDs × 3 bytes (R,G,B) = 45 data bytes
- **Byte offset for knob K, LED L:** `K*9 + L*3 + 2` (R), `+3` (G), `+4` (B)
- **Gamma correction:** 256-entry lookup table applied before sending
- **Refresh:** 50ms (20 FPS) for animated effects. Device turns LEDs off without periodic frames.
- **Brightness:** Global 0-100% multiplier applied before gamma

---

## Profile System

- Profiles saved as `profile_<name>.json` next to the exe
- `ConfigManager.SaveProfile()` / `LoadProfile()` for persistence
- Switch via button action (`switch_profile`) or Settings tab
- Each profile stores full config (knobs, buttons, lights, settings)

---

## Device Switching (IPolicyConfig COM)

`ButtonHandler.cs` uses undocumented Windows COM interface to change default audio device:
- `IPolicyConfig` GUID: `f8679f50-850a-41cf-9c72-430f290290c8`
- `PolicyConfigClient` CLSID: `870af99c-171d-4f9e-af0d-e63df40c2bc9`
- Sets device for all 3 roles (Console, Multimedia, Communications)

---

## Build & Run

```powershell
dotnet build
dotnet run
# Exe at: bin\Debug\net8.0-windows\WolfMixer.exe
```

**Requirements:** .NET 8 SDK

**Before running:** Kill Turn Up if it's running (it holds COM3).
```powershell
taskkill /f /im "Turn Up.exe"
taskkill /f /im TurnUpService.exe
```

---

## Known Issues / Gotchas

- **config.json is read from next to the `.exe`**, not the project root
- **Single instance:** Only one WolfMixer can run. Check Task Manager for stale process.
- **`any` target** picks first audio session — may not be expected one
- **Knob debounce threshold is 5 raw units** — lower in `AudioMixer.cs` if sluggish
- **Single-press has ~300ms latency** — intentional for double-press detection
- **Hold threshold is 500ms** — configurable in `ButtonHandler.cs`
- **Monitor brightness (DDC/CI)** may not work on all monitors — depends on DDC/CI support
- **Newtonsoft.Json PascalCase** — config props in memory are PascalCase, JSON file is camelCase

---

## ConfigForm UI (4 tabs)

- **Knobs tab:** 5 columns — label, gauge, target, device picker, response curve, volume range
- **Buttons tab:** 5 columns — 3 action rows (press/double/hold), context-sensitive sub-controls
- **Lights tab:** 5 columns — effect type, color1/color2 pickers, speed slider, global brightness
- **Settings tab:** Start with Windows, profiles (save/load/new), serial port/baud config

Dark theme: `BgDark=#1B1B1B`, `CardBg=#262626`, `Accent=#00B4D8`, owner-drawn tab headers.

---

## Active Audio Devices on This PC

- `DENON-AVAMP (NVIDIA High Definition Audio)`
- `OMEN by HP 35 (NVIDIA High Definition Audio)`
- `LG HDR QHD (NVIDIA High Definition Audio)`
- `ASUS VG278HE (NVIDIA High Definition Audio)`
- `Headphones (2- JLab JBuds Lux ANC)`
- `Headset (2- JLab JBuds Lux ANC)`

---

## File Locations

| File | Path |
|---|---|
| Project source | `C:\Users\audio\Desktop\WolfMixer\` |
| Built exe | `C:\Users\audio\Desktop\WolfMixer\bin\Debug\net8.0-windows\WolfMixer.exe` |
| Runtime config | `C:\Users\audio\Desktop\WolfMixer\bin\Debug\net8.0-windows\config.json` |
| Log file | `C:\Users\audio\Desktop\WolfMixer\bin\Debug\net8.0-windows\wolfmixer.log` |
| Turn Up install | `C:\Program Files (x86)\Turn Up\` |
| Turn Up DLLs | `C:\Program Files (x86)\Turn Up\service\` (TurnUpBox.dll, CSCore.dll, etc.) |

---

## Future Ideas

- OBS Studio 28+ integration (obs-websocket) — scene switching, source mute/gain, recording/streaming
- VoiceMeeter strip/bus gain and mute control
- Per-app volume display overlay (small HUD)
- USB PnP auto-detection instead of hardcoded COM3
- Multiple Turn Up device support
- Publish as single-file exe: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`
- Installer (WiX or NSIS) for clean deployment

---

## UI/UX Redesign (March 2026)

Major visual overhaul — premium dark theme inspired by Elgato Wave Link. All logic files untouched; this is purely a visual/UX redesign.

### New Custom Controls

- **AnimatedKnobControl.cs** — Arc-sweep animated knob with glow effects. Properties: `Value` (0-1), `ArcColor`, `PercentText`, `KnobSize` (default 88px). Draws anti-aliased arc from 225° with 270° sweep, soft glow layer at 40% opacity, needle dot at tip, center percentage label.
- **VuMeterControl.cs** — 16-segment vertical VU meter with peak hold. Properties: `Level` (0-1), `BarColor`. Bottom 10 segments use bar color, top 3 yellow (#FFB800), top 1 red (#FF4444). Peak hold for 1.5s with smooth falloff.

### ConfigForm.cs Changes

Complete visual rewrite (all logic/wiring preserved):
- **New color palette** (see below)
- **Device preview strip** at top of Knobs tab — mini hardware visualization showing 5 knobs with LED glow rings and button LEDs
- **AnimatedKnobControl** per channel replacing basic gauges
- **VuMeterControl** per channel showing live audio levels
- **Better tab headers** — owner-drawn 130×36px tabs with Unicode icons (◈ KNOBS, ⊞ BUTTONS, ❋ LIGHTS, ⚙ SETTINGS), accent bottom border on selected
- **Header bar** — 48px bar with 🐺 WOLFMIXER branding, connection status dot, profile dropdown
- **Save button** and improved profile management in Settings tab

### TrayApp.cs Changes

- **Polished 32×32 icon** — 5 equalizer bars with rounded tops, different heights [6,12,20,15,9]px, cyan when connected / gray when disconnected
- **Live volume display in tray menu** — 5 menu items showing per-knob volume with bar visualization, updated every 500ms via timer
- Menu header: "🐺 WolfMixer" label

### Deploy Workflow

Run `deploy.bat` from the project folder on the Windows PC (`C:\Users\audio\Desktop\WolfMixer\`):
1. Pulls latest from GitHub
2. Builds with `dotnet build -c Debug`
3. Kills any running WolfMixer.exe
4. Launches the new build

### Color Palette Reference

```
BgBase      = #0F0F0F   // deepest background
BgDark      = #141414   // main form background
CardBg      = #1C1C1C   // cards / panels
CardBorder  = #2A2A2A   // card borders
InputBg     = #242424   // textboxes, combos
InputBorder = #363636
Accent      = #00B4D8   // primary cyan
AccentGlow  = #00E5FF   // bright cyan for active/glow
AccentDim   = #007A94   // dim cyan for inactive
TextPrimary = #E8E8E8
TextSec     = #9A9A9A
TextDim     = #555555
DangerRed   = #FF4444
SuccessGrn  = #00DD77
WarnYellow  = #FFB800
```
