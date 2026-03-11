# AmpUp — Claude Code Context

AmpUp is a C# .NET 8 WPF app that replaces the "Turn Up" USB volume mixer app.
It reads serial events from the Turn Up hardware and maps them to Windows per-app audio volume control.
Uses WPF-UI (FluentWindow, Mica backdrop) with a glassmorphism dark theme, sidebar navigation, and code-behind pattern (no MVVM).

---

## Hardware — Turn Up Device

- **USB chip:** CH343 USB-to-serial
- **Port:** COM3 (usually — can briefly disappear from `GetPortNames()` after process restart; `FindDevicePort` always tries configured port first as fallback)
- **Baud rate:** 115200
- **5 knobs** (potentiometers, 10-bit 0-1023)
- **5 buttons** (momentary switches)
- **15 RGB LEDs** (3 per knob, individually addressable)

### Serial Frame Protocol (fully reverse engineered)

All frames are wrapped with `0xFE` (start) and `0xFF` (end).

| Event | Frame | Notes |
|-|-|-|
| Knob move | `fe 03 [idx] [hi] [lo] ff` | 6 bytes. idx=0-4, value=hi*256+lo, range 0-1023 |
| Knob batch | `fe 04 [5x hi+lo] ff` | 13 bytes. All 5 knob values on connect |
| Health/ping | `fe 02 ff` | 3 bytes. Periodic heartbeat |
| Button down | `fe 06 [idx] ff` | 4 bytes. idx=0-4, button pressed |
| Button up | `fe 07 [idx] ff` | 4 bytes. idx=0-4, button released |
| Device ID | `fe 08 [4 bytes] ff` | 7 bytes. Sent on connect |

### RGB Write Protocol

- **Frame:** 48 bytes — `FE 05 [45 bytes RGB data] FF`
- **Layout:** 5 knobs × 3 LEDs × 3 bytes (R,G,B) = 45 data bytes
- **Byte offset for knob K, LED L:** `K*9 + L*3 + 2` (R), `+3` (G), `+4` (B)
- **Gamma correction:** 256-entry lookup table applied before sending
- **Refresh:** 50ms (20 FPS) for animated effects. Device turns LEDs off without periodic frames.
- **Brightness:** Global 0-100% multiplier applied before gamma
- **Per-LED control:** `SetColor(knobIdx, ledIdx, r, g, b)` — each of the 3 LEDs per knob is independently addressable

---

## Architecture

Single `.exe` with Inno Setup installer for distribution.

```
AmpUp.csproj              .NET 8 WPF, WPF-UI + NAudio + Newtonsoft.Json + System.IO.Ports
App.xaml / App.xaml.cs     WPF Application entry point, backend orchestration, single-instance mutex
MainWindow.xaml / .cs      FluentWindow with Mica, sidebar nav, connection status pulse, active indicator bars
Theme.xaml                 Glassmorphism color palette, card/text styles, custom green scrollbar, styled controls

Views/
  MixerView.xaml / .cs     5 channel strips — knob, VU meter, volume %, target/curve/range controls
  ButtonsView.xaml / .cs   5 button tiles — 3 gesture rows (tap/double/hold), colorful action tiles
  LightsView.xaml / .cs    Global lighting card + 5 LED columns — visual effect picker, colors, speed
  SettingsView.xaml / .cs  Connection, startup, profiles, integrations (HA + FanControl)
  HomeAssistantView.xaml/.cs  Home Assistant entity control

Controls/
  AnimatedKnobControl.cs   WPF FrameworkElement — arc sweep knob, glow, frozen resources
  VuMeterControl.cs        WPF FrameworkElement — 16-segment VU meter, DrawingVisual, peak hold
  CurvePickerControl.cs    3 clickable mini graphs showing Linear/Log/Exp response curves
  EffectPickerControl.cs   Categorized grid of colorful icon tiles for LED effects (15 effects)
  ActionPickerControl.cs   Categorized grid of colorful icon tiles for button actions (25 actions)
  SegmentedControl.cs      Pill-style segmented buttons
  GridPicker.cs            Categorized grid picker (legacy, replaced by ActionPickerControl)
  ListPicker.cs            Dropdown list picker
  FlyoutPicker.cs          Floating picker popup
  RangeSlider.cs           Dual-thumb range slider

AudioAnalyzer.cs           WASAPI loopback capture + FFT — 5 frequency bands for audio-reactive LEDs
SerialReader.cs            Reads COM3, parses fe/ff frames, fires OnKnob / OnButton events
AudioMixer.cs              WASAPI per-app volume + GetPeakLevel, response curves, volume range
ButtonHandler.cs           Gesture state machine (press/double/hold) → 25 action types
RgbController.cs           RGB effects engine — 15 effects, 20 FPS animation, 3 LEDs per knob
MonitorBrightness.cs       DDC/CI physical monitor brightness via dxva2.dll
NativeMethods.cs           Consolidated P/Invoke declarations (user32, PowrProf)
Config.cs                  Loads/saves config.json + profile system (Newtonsoft.Json)
Logger.cs                  Appends to %AppData%\AmpUp\ampup.log
UpdateChecker.cs           GitHub release version check — reads version from assembly at runtime
config.json                User config — knob targets, button actions, RGB effects, profiles

release.bat                One-command release: bumps version, commits, tags, pushes
deploy.bat                 Local dev: git pull → build → kill old → launch
build-installer.bat        Local installer build (dotnet publish + Inno Setup)
.github/workflows/release.yml  GitHub Actions: auto-builds installer on v* tag push
installer/ampup-setup.iss  Inno Setup script (reads version from auto-generated version.iss)
```

---

## config.json Format

```json
{
  "serial": { "port": "COM3", "baud": 115200 },
  "knobs": [
    { "idx": 0, "label": "Master", "target": "master", "minVolume": 0, "maxVolume": 100, "curve": "Linear" },
    { "idx": 1, "label": "Discord", "target": "discord" },
    { "idx": 2, "label": "Music", "target": "apps", "apps": ["spotify", "foobar2000"] }
  ],
  "buttons": [
    {
      "idx": 0, "action": "media_prev",
      "holdAction": "mute_mic", "holdPath": "",
      "doublePressAction": "none", "doublePressPath": "",
      "macroKeys": "", "deviceId": "", "profileName": "",
      "powerAction": "", "linkedKnobIdx": -1
    }
  ],
  "lights": [
    { "idx": 0, "r": 0, "g": 150, "b": 255, "effect": "SingleColor",
      "r2": 255, "g2": 0, "b2": 0, "effectSpeed": 50,
      "reactiveMode": "SpectrumBands" }
  ],
  "globalLight": {
    "enabled": false, "effect": "RainbowWave",
    "r": 0, "g": 230, "b": 118, "r2": 255, "g2": 255, "b2": 255,
    "effectSpeed": 50, "reactiveMode": "SpectrumBands"
  },
  "profileTransition": "Cascade",
  "startWithWindows": true,
  "ledBrightness": 100,
  "activeProfile": "Default",
  "profiles": ["Default"]
}
```

### Knob target values
| Target | Behavior |
|-|-|
| `"master"` | Windows master volume (default audio endpoint) |
| `"mic"` | Microphone input level (default capture device) |
| `"system"` | System Sounds session |
| `"any"` | First active audio session not already assigned |
| `"active_window"` | Volume of currently focused window's audio session |
| `"apps"` | App group — controls multiple apps in `apps[]` list |
| `"output_device"` | Specific output device volume (by `deviceId`) |
| `"input_device"` | Specific input device volume (by `deviceId`) |
| `"monitor"` | Physical monitor brightness via DDC/CI |
| `"discord"` | Matches process name containing "discord" |
| `"spotify"` | Matches process name containing "spotify" |
| `"chrome"` | Matches process name containing "chrome" |
| `"game.exe"` | Any substring of the process name works |

### Button action values (25 actions)

**Media:**
| Action | Behavior |
|-|-|
| `"none"` | Do nothing |
| `"media_play_pause"` | Send VK_MEDIA_PLAY_PAUSE |
| `"media_next"` | Send VK_MEDIA_NEXT_TRACK |
| `"media_prev"` | Send VK_MEDIA_PREV_TRACK |

**Mute:**
| Action | Behavior |
|-|-|
| `"mute_master"` | Send VK_VOLUME_MUTE |
| `"mute_mic"` | Toggle default mic endpoint mute |
| `"mute_program"` | Toggle mute on app matching `path` (process name) |
| `"mute_active_window"` | Toggle mute on focused window's audio |
| `"mute_app_group"` | Toggle mute on all apps in linked knob's app group (uses `linkedKnobIdx`) |

**App Control:**
| Action | Behavior |
|-|-|
| `"launch_exe"` | Launch app at `path` |
| `"close_program"` | Kill process matching `path` |

**Device:**
| Action | Behavior |
|-|-|
| `"cycle_output"` | Cycle through output devices |
| `"cycle_input"` | Cycle through input devices |
| `"select_output"` | Switch to specific output device by `deviceId` |
| `"select_input"` | Switch to specific input device by `deviceId` |

**System:**
| Action | Behavior |
|-|-|
| `"macro"` | Send keyboard combo from `macroKeys` (e.g. `"ctrl+shift+m"`) |
| `"switch_profile"` | Switch to named profile from `profileName` |
| `"cycle_brightness"` | Cycle LED brightness |

**Power (individual actions — no sub-picker):**
| Action | Behavior |
|-|-|
| `"power_sleep"` | Graceful sleep (SetSuspendState, forceCritical=false) |
| `"power_lock"` | Lock workstation |
| `"power_off"` | Shutdown |
| `"power_restart"` | Restart |
| `"power_logoff"` | Log off |
| `"power_hibernate"` | Hibernate |
| `"system_power"` | Legacy — uses `powerAction` sub-field |

### Button gesture types
Each button supports 3 gestures (15 total bindings):

| Gesture | Config fields | Detection |
|-|-|-|
| **Single press** | `action` + `path` | Released < 500ms, no 2nd press within 300ms |
| **Double press** | `doublePressAction` + `doublePressPath` | 2nd press within 300ms of first release |
| **Hold** | `holdAction` + `holdPath` | Held 500ms+ (fires while holding) |

### LED effects (15 types)

**Static:**
| Effect | Colors | Description |
|-|-|-|
| `SingleColor` | 1 | Color scaled by knob position (default) |
| `ColorBlend` | 2 | Lerp between color1→color2 based on knob position |
| `PositionFill` | 1 | LEDs light up left→right as knob increases |
| `GradientFill` | 2 | Static gradient color1→color2 across 3 LEDs |

**Animated:**
| Effect | Colors | Description |
|-|-|-|
| `Blink` | 2 | Alternate between color1/color2 at configurable speed |
| `Pulse` | 2 | Smooth sine-wave oscillation between color1/color2 |
| `Breathing` | 1 | Smooth brightness fade in/out with squared easing |
| `Fire` | 2 | Random warm flicker — color1=base flame, color2=ember tip. Per-LED. |
| `Comet` | 1 | Bright pixel chases across 3 LEDs with fading tail |
| `Sparkle` | 1 | Random LED flashes white, decays back to dim base |
| `RainbowWave` | — | HSV rainbow across all 5 knobs, animated |
| `RainbowCycle` | — | 3 LEDs per knob each get different hue, animated |

**Reactive/Status:**
| Effect | Colors | Description |
|-|-|-|
| `MicStatus` | 2 | Color1=unmuted, Color2=muted (mic state) |
| `DeviceMute` | 2 | Color1=unmuted, Color2=muted (master state) |
| `AudioReactive` | 2 | FFT-driven. Color1=idle, Color2=peak. Modes: BeatPulse, SpectrumBands, ColorShift |

### Audio-Reactive Modes (ReactiveMode)
| Mode | Behavior |
|-|-|
| `BeatPulse` | Bass (band 1) drives ALL knob brightness simultaneously |
| `SpectrumBands` | Each knob = its own frequency band (sub-bass → treble) |
| `ColorShift` | Hue shifts based on overall audio energy |

### Global Lighting
When `globalLight.enabled = true`, one effect config applies to all 5 knobs. Per-knob settings are hidden in the UI but preserved.

### Profile Switch Transitions (ProfileTransition)
| Transition | Animation |
|-|-|
| `None` | No animation |
| `Flash` | All 5 knobs flash 3 times in accent color |
| `Cascade` | Knobs light up 1→5 in sequence, then fade |
| `RainbowSweep` | Accelerating rainbow wave that fades out |

All transitions run 3 seconds (60 ticks at 20 FPS) then auto-clear.

---

## AudioAnalyzer (FFT)

`AudioAnalyzer.cs` captures system audio via `WasapiLoopbackCapture` and runs FFT to extract 5 frequency bands:

| Band | Frequency Range | Musical Content |
|-|-|-|
| 0 | 20–80 Hz | Sub-bass |
| 1 | 80–250 Hz | Bass (kick drum, bass guitar) |
| 2 | 250–2000 Hz | Low-mid (vocals, instruments) |
| 3 | 2000–6000 Hz | High-mid (presence) |
| 4 | 6000–20000 Hz | Treble (cymbals, air) |

- **FFT size:** 1024 samples, Hann windowed
- **Normalization:** `NormRef = 0.005f` (WASAPI loopback levels are very low)
- **Smoothing:** Fast attack (0.5), slow decay (0.88)
- **Lifecycle:** Lazy start/stop — only runs when at least one light uses AudioReactive
- **Thread safety:** `lock(_lock)` on SmoothedBands updates

---

## UI Architecture

### Custom Controls (`Controls/`)

- **CurvePickerControl** — 3 mini canvas graphs showing actual response curves (Linear/Log/Exp). Click to select. Uses same math as AudioMixer.ApplyCurve. Accent-colored polylines on dark background with grid dots.

- **EffectPickerControl** — 15 LED effects as categorized icon tiles. 3 categories: STATIC, ANIMATED, REACTIVE. Each tile has its own unique color (Fire=orange, Sparkle=yellow, etc.). Dark glass tiles with accent glow on selection.

- **ActionPickerControl** — 25 button actions as categorized icon tiles. 6 categories: MEDIA (green), MUTE (red), APP CONTROL (blue), DEVICE (purple), SYSTEM (gold), POWER (red). Tiles are 82×58px with 22px icons.

- **AnimatedKnobControl** — WPF FrameworkElement, `OnRender(DrawingContext)`. Arc sweep 225° start / 270° sweep via `StreamGeometry`. All Pen/Brush frozen as static fields.

- **VuMeterControl** — WPF FrameworkElement, `DrawingVisual` child. 16 segments, color zones (cyan→yellow→red), peak hold 1.5s.

### View Details

- **MixerView:** 5 channel strips with visual curve pickers (CurvePickerControl), app group toggle lists (inline checkboxes for running apps), hover glow on strip borders.

- **ButtonsView:** Colorful button strip at top — tiles tinted by their action's color. 3 gesture sections per button with colored headers (TAP=green, DOUBLE=gold, HOLD=orange). ActionPickerControl for all 15 action slots.

- **LightsView:** Global Lighting card at top (checkbox + EffectPickerControl + colors + speed + brightness slider on right). 5 per-knob panels below (hidden when global is on). EffectPickerControl replaces dropdown.

- **SettingsView:** Section headers with accent left-border indicators.

### Theme (Theme.xaml)

```
BgBase      = #0F0F0F     BgDark      = #141414
CardBg      = #1C1C1C     CardBorder  = #2A2A2A
InputBg     = #242424     InputBorder = #363636
Accent      = #00E676     AccentGlow  = #69F0AE     AccentDim = #00A854
TextPrimary = #E8E8E8     TextSec     = #9A9A9A     TextDim   = #555555
DangerRed   = #FF4444     SuccessGrn  = #00DD77     WarnYellow= #FFB800
```

Custom scrollbars: slim 8px, transparent track, green thumb (#00E676) with hover/drag brightness states.

---

## Build & Deploy

```powershell
# On Windows PC (C:\Users\audio\Desktop\AmpUp\)
deploy.bat    # git pull → dotnet build → kill old → launch new

# Manual
dotnet build -c Debug
# Exe: bin\Debug\net8.0-windows\AmpUp.exe
```

**Requirements:** .NET 8 SDK, Windows 10/11

**Before running:** Kill Turn Up if it's running (it holds COM3).
```powershell
taskkill /f /im "Turn Up.exe"
taskkill /f /im TurnUpService.exe
```

### Version Management

**Single source of truth:** `AmpUp.csproj` `<Version>` tag (e.g. `0.3.2-alpha`).

| File | How it gets the version |
|-|-|
| `AmpUp.csproj` | `<Version>0.3.2-alpha</Version>` — the source of truth |
| `UpdateChecker.cs` | Reads `AssemblyInformationalVersionAttribute` at runtime (no hardcoded string) |
| `installer/ampup-setup.iss` | `#include "version.iss"` — auto-generated by build/release scripts |
| `installer/version.iss` | Auto-generated, gitignored: `#define MyAppVersion "0.3.2-alpha"` |

### Release Workflow

**One-command release:**
```powershell
release.bat 0.4.0-alpha
```
This does:
1. Bumps `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in `.csproj`
2. Generates `installer/version.iss`
3. Commits `release: v0.4.0-alpha`, tags `v0.4.0-alpha`, pushes

**GitHub Actions** (`.github/workflows/release.yml`) then:
1. Triggers on `v*` tag push
2. Runs `dotnet publish` (self-contained, win-x64)
3. Installs Inno Setup, builds installer
4. Creates GitHub Release with installer `.exe` attached

### Installer
Inno Setup script at `installer/ampup-setup.iss`. Output in `installer/output/`.
Local build: `build-installer.bat` (auto-extracts version from .csproj).

### Auto-Update
`UpdateChecker.cs` checks `audioslayer/ampup` GitHub releases on startup.
- Compares `AssemblyInformationalVersion` against latest release tag
- Semantic version comparison (major.minor.patch, pre-release aware)
- Downloads installer `.exe` to temp, launches it, shuts down app

---

## Development Setup

Two clones of the same repo:

| Location | Purpose |
|-|-|
| `Z:\Projects\ampup\` | Code editing with Claude Code (this machine) |
| `C:\Users\audio\Desktop\AmpUp\` | Build + test + run the live app (same Windows PC) |

**Workflow:** Edit on Z: → `git push` → `deploy.bat` on Desktop → test → `release.bat` when ready.

Both clones use the same GitHub origin (`audioslayer/ampup`). Git identity: Tyson Wolf / audioslayer@gmail.com (set per-repo, not global).

- Log: `%AppData%\AmpUp\ampup.log`
- Config: `C:\Users\audio\Desktop\AmpUp\bin\Debug\net8.0-windows\config.json`

---

## Profile System

- Profiles saved as `profile_<name>.json` next to the exe
- `ConfigManager.SaveProfile()` / `LoadProfile()` for persistence
- Switch via button action (`switch_profile`) or Settings tab
- Each profile stores full config (knobs, buttons, lights, settings)
- Profile switch triggers transition animation (configurable)

---

## Known Issues / Gotchas

- **WPF-UI namespace conflicts:** `Wpf.Ui.Controls` has types like `Border` that conflict with `System.Windows.Controls.Border`. Always fully qualify when both namespaces are in scope.
- **config.json is read from next to the `.exe`**, not the project root
- **Single instance:** Only one AmpUp can run. Check Task Manager for stale process.
- **Single-press has ~300ms latency** — intentional for double-press detection
- **Hold threshold is 500ms** — configurable in `ButtonHandler.cs`
- **WASAPI loopback levels are very low** — NormRef=0.005f in AudioAnalyzer. If LEDs don't react, lower further.
- **Sleep uses graceful suspend** (forceCritical=false) — required for proper GPU/USB wake
- **Newtonsoft.Json PascalCase** — config props in memory are PascalCase, JSON file is camelCase
- **Cannot `dotnet build` on Linux** — WPF is Windows-only

---

## Version History

- **v0.3.1-alpha** — Renamed from WolfMixer to AmpUp. Icon design, GitHub release.
- **v0.3.2-alpha** — Audio-Reactive RGB (FFT), Mute App Group, Visual Curve Picker, Global Lighting, 6 new effects (Breathing/Fire/Comet/Sparkle/GradientFill), Profile Switch Transitions, EffectPickerControl, ActionPickerControl, App Group toggles, Power action tiles, custom scrollbars, full UI polish pass.
