# AmpUp — Claude Code Context

AmpUp is a C# .NET 8 WPF app that replaces the "Turn Up" USB volume mixer app.
It reads serial events from the Turn Up hardware and maps them to Windows per-app audio volume control.
Uses WPF-UI (FluentWindow, Mica backdrop) with a glassmorphism dark theme, sidebar navigation, and code-behind pattern (no MVVM).

**macOS port: v0.1.0-alpha released** — see [CLAUDE-MAC.md](CLAUDE-MAC.md) for Mac-specific docs, SSH access, and architecture plan.
**Shared library:** `AmpUp.Core/` contains platform-agnostic code (models, serial, RGB, config, integrations).
**Official Turn Up source:** We have admin access to `JaredWF/TurnUpCustomizer` — protocol fully confirmed, see memory for analysis.

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
| Knob batch | `fe 04 [5x hi+lo] ff` | 13 bytes. All 5 knob values (response to info request) |
| Health/ping | `fe 02 ff` | 3 bytes. Periodic heartbeat (~500ms) |
| Button down | `fe 06 [idx] ff` | 4 bytes. idx=0-4, button pressed |
| Button up | `fe 07 [idx] ff` | 4 bytes. idx=0-4, button released |
| Device ID | `fe 08 [4 bytes] ff` | 7 bytes. Response to info request |
| Info request | `fe 01 ff` | 3 bytes. **PC→Device.** Triggers Device ID + Knob batch response |

### RGB Write Protocol

- **Frame:** 48 bytes — `FE 05 [45 bytes RGB data] FF`
- **Layout:** 5 knobs × 3 LEDs × 3 bytes (R,G,B) = 45 data bytes
- **Byte offset for knob K, LED L:** `K*9 + L*3 + 2` (R), `+3` (G), `+4` (B)
- **Gamma correction:** Default 1.0 (linear, no correction — matches official Turn Up app which defines but never applies gamma). Per-channel R/G/B gamma configurable via Settings → LED Calibration (0.5–4.0).
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
  MixerView.xaml / .cs     5 channel strips — knob, VU meter, volume %, target/curve/range controls (sidebar label: "Knobs")
                           App group uses chip/pill tags. Smart Mix (Voice Ducking + App Profiles) built in code-behind.
  ButtonsView.xaml / .cs   5 button columns — 3 gesture rows (tap/double/hold), categorized action picker with sub-flyouts
  LightsView.xaml / .cs    Global lighting card + 5 LED columns — visual effect picker, colors, speed
  SettingsView.xaml / .cs  Connection, startup, profiles, OSD duration sliders, integrations (HA + Govee side-by-side)
  AmbienceView.xaml / .cs  Govee room lighting — LAN sync + Cloud dashboard (scenes, segments, music mode)
                           DreamView screen sync card with zone preview. Hidden in sidebar when Govee not enabled.
  OsdView.xaml / .cs       OSD settings (moved from Settings) + Quick Wheel config
                           Checkboxes for volume/profile/device OSD, position picker, duration sliders.
                           Quick Wheel: enable toggle, trigger button picker, navigation knob picker.
                           Auto-syncs button HoldAction with Quick Wheel config.
  BindingsView.xaml.cs     Profile Overview — all knob/button assignments across profiles with Preview OSD button
                           (sidebar label: "Overview"). Cards tinted with LED colors. Click navigates to correct profile.

Controls/
  AnimatedKnobControl.cs   WPF FrameworkElement — arc sweep knob, glow, frozen resources
  VuMeterControl.cs        WPF FrameworkElement — 16-segment VU meter, DrawingVisual, peak hold
  CurvePickerControl.cs    3 clickable mini graphs showing Linear/Log/Exp response curves
  EffectPickerControl.cs   Categorized grid of colorful icon tiles for LED effects (30+ effects)
  ActionPicker.cs          Categorized action dropdown with inline sub-panel for button actions
  GridPicker.cs            Categorized target dropdown with inline sub-panel for knob targets
                           Both use borderless Window flyout + inline sub-panel (single HWND, no cross-window issues).
                           Items support optional subtitle text. Sub-panel shows context header + highlights active parent.
  CheckListPicker.cs       Multi-select checklist picker (for cycle device subset selection, filtered by direction)
  ListPicker.cs            Dropdown list picker with optional filter, accent highlighting
  TrayMixerPopup.cs        Unified tray popup — volume mixer + device switcher + app assignment + controls
                           Both left and right tray click open this popup. Includes connection status,
                           output/input device cycling, per-app sliders (live updating), Assign Running Apps,
                           Open/Exit, update-available banner, and per-app audio activity peak bars.
                           DPI-aware positioning (pixel→DIP conversion). Detects taskbar position
                           (left/right/top/bottom) for vertical taskbar support.
  TrayContextMenu.cs       Legacy right-click menu (still exists but both clicks now use TrayMixerPopup)
  ChannelGlowControl.cs    Audio-reactive ambient glow behind mixer channel cards (DrawingVisual)
  SegmentedControl.cs      Pill-style segmented buttons
  RangeSlider.cs           Dual-thumb range slider
  StyledSlider.cs          Single-thumb slider matching RangeSlider style (accent-aware, ShowLabel toggle,
                           Step for decimal snap granularity, LabelFormat for display format)
  GoveeSetupGuide.cs       4-step wizard window for Govee API key setup

AmbienceSync.cs            Govee LAN UDP sync engine — discovery, color mirroring, rate limiting
GoveeCloudApi.cs           Govee Platform REST client — devices, scenes, segments, music mode
AudioAnalyzer.cs           WASAPI loopback capture + FFT — 5 frequency bands for audio-reactive LEDs
SerialReader.cs            Reads COM port, parses fe/ff frames, fires OnKnob / OnButton events, ±3 jitter deadzone
                           Sends FE 01 FF on connect to request knob positions from hardware
AudioMixer.cs              WASAPI per-app volume + GetPeakLevel, response curves, volume range
                           Persistent _renderDevice for session COM objects; dedicated _masterPeakDevice for metering
                           Skips AmpUp's own PID for active_window target
                           Sessions indexed by both process name AND WASAPI DisplayName (for UWP/MS Store apps)
                           FuzzyContains: strips spaces before matching ("Apple Music" → "AppleMusic")
ButtonHandler.cs           Gesture state machine (press/double/hold) → 26 action types (incl. mute_device)
RgbController.cs           RGB effects engine — 30+ effects (per-knob + global spanning), 20 FPS animation
                           Per-channel gamma (SetGamma) — default 1.0 linear. Preview color override for calibration.
DuckingEngine.cs           Auto-ducking: monitors trigger app audio, fades target app volumes with smooth interpolation
AutoProfileSwitcher.cs     Auto-profile switching: monitors foreground window, fires profile switch events with debounce
MonitorBrightness.cs       DDC/CI physical monitor brightness via dxva2.dll. Cached handles + throttled
                           SetThrottled() at 60ms intervals (last-value-wins). 5s startup guard.
RadialWheelOverlay.xaml/.cs  Radial pie-menu OSD overlay for Quick Wheel — glass dark theme, accent glow
                           Always 8 pie segments (profiles/devices + blank slots). Profile segments show
                           icon + color from ProfileIcons. Follows ThemeManager.Accent (not hardcoded green).
                           Mouse hover/click + keyboard + Escape. Shows on OSD-configured monitor.
                           SetProfiles() for profile mode, SetDevices() for output device mode.
                           Public: Show(), Highlight(), GetSelectedIndex(), GetTotalSlots(), Dismiss().
DreamSyncController.cs     Screen sync engine — captures screen zones, sends colors to Govee via LAN UDP
                           Per-segment support via Govee "razer" protocol for capable devices (H6056=6 segments)
                           Falls back to single-color colorwc for devices without segment support
ScreenCapture.cs           GDI screen capture with zone sampling, gamma-correct averaging, dark pixel filtering
NativeMethods.cs           P/Invoke declarations (user32, PowrProf, DisplayConfig for monitor friendly names)
Config.cs                  Loads/saves config.json + profile system (Newtonsoft.Json)
Logger.cs                  Appends to %AppData%\AmpUp\ampup.log
UpdateChecker.cs           GitHub release version check — reads version from assembly at runtime
config.json                User config — knob targets, button actions, RGB effects, profiles

deploy.bat                 Local dev: git pull → build → kill old → launch
build-installer.bat        Local installer build (dotnet publish + Inno Setup)
.github/workflows/release.yml.disabled  GitHub Actions workflow (DISABLED — manual installer flow)
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
  "ambience": {
    "goveeEnabled": false,
    "goveeApiKey": "",
    "goveeDevices": [
      { "ip": "192.168.1.50", "name": "Living Room Strip", "sku": "H6056", "syncMode": "global", "useSegmentProtocol": true, "poweredOn": true }
    ],
    "brightnessScale": 75,
    "warmToneShift": false,
    "screenSync": {
      "enabled": false, "monitorIndex": 0, "targetFps": 30, "zoneCount": 8,
      "saturation": 1.2, "sensitivity": 5,
      "deviceMappings": [{ "deviceIp": "192.168.1.50", "side": "Full" }]
    }
  },
  "osd": {
    "showVolume": true, "showProfileSwitch": true, "showDeviceSwitch": true,
    "volumeDuration": 2.0, "profileDuration": 3.5, "deviceDuration": 2.5,
    "position": "BottomRight", "monitorIndex": 0,
    "hideInFullscreen": false,
    "quickWheels": [
      { "enabled": true, "triggerButton": 0, "mode": "Profile" },
      { "enabled": true, "triggerButton": 1, "mode": "OutputDevice" }
    ]
  },
  "profileTransition": "Cascade",
  "startWithWindows": true,
  "ledBrightness": 100,
  "gammaR": 1.0, "gammaG": 1.0, "gammaB": 1.0,
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
| `"quick_wheel"` | Hold to open radial profile picker (auto-set by OSD Quick Wheel config) |

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
| `ProgramMute` | 2 | Color1=unmuted, Color2=muted (watches single program by name) |
| `AppGroupMute` | 2 | Color1=any unmuted, Color2=all muted (watches linked knob's app group) |
| `DeviceSelect` | per-device | Shows mapped color based on current default audio output device |
| `PositionBlend` | 2 | Blend between color1→color2 based on knob position |
| `PositionBlendMute` | 2 | PositionBlend + dims to Color2 when linked knob's group is muted |

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

- **GridPicker** — Categorized target dropdown with inline sub-panel. Used in MixerView for knob targets. Uses borderless Window flyout with inline sub-panel (single HWND, no cross-window issues). Items support subtitle text (e.g. "Home Assistant" + "Light"). Sub-panel shows context header + highlights active parent item.

- **ActionPicker** — Categorized action dropdown with inline sub-panel. Used in ButtonsView for all 15 gesture action slots. 7 categories: MEDIA (green), MUTE (red), APP CONTROL (blue), DEVICE (purple), SYSTEM (gold), POWER (red), INTEGRATIONS (cyan). Same inline sub-panel pattern as GridPicker.

- **AnimatedKnobControl** — WPF FrameworkElement, `OnRender(DrawingContext)`. Arc sweep 225° start / 270° sweep via `StreamGeometry`. All Pen/Brush frozen as static fields. Renders knob-face.png with colored arc ring. Smooth lerp animation via `SetTarget()` + `Tick()` (0.5 per tick). Arc color syncs with live LED color from RgbController.

- **VuMeterControl** — WPF FrameworkElement, `DrawingVisual` child. 16 segments, standard green (0-9) / orange (10-12) / red (13-15), peak hold 1.5s.

- **ChannelGlowControl** — Audio-reactive radial gradient glow behind mixer channel cards. Tinted with live LED color. Fast attack (0.4), slow decay (0.92). Skips re-render when alpha/color unchanged (perf).

### View Details

- **MixerView (sidebar: "Knobs"):** 5 channel strips with visual curve pickers (CurvePickerControl), app group chip/pill tags (clickable, accent-tinted, wrap layout), hover glow on strip borders. GridPicker with sub-flyouts for output_device/input_device, HA entities (shown as "Home Assistant" + domain subtitle), Govee devices. Smart Mix section built in code-behind: Voice Ducking (ListPicker for trigger app, amount slider, collapsible Advanced for fade timing) + App Profiles (dynamic add/remove rules with app + profile ListPickers).

- **ButtonsView:** 5 column layout — one per button. 3 gesture sections per button with colored headers (TAP=green, DOUBLE=gold, HOLD=orange). ActionPicker with categorized dropdown and sub-flyouts for HA entities and audio device actions.

- **LightsView:** Global Lighting card at top (checkbox + EffectPickerControl + colors + speed + brightness slider on right). 5 per-knob panels below (hidden when global is on). EffectPickerControl replaces dropdown.

- **SettingsView:** Section headers with accent left-border indicators. HA and Govee integrations displayed side-by-side. Profile section has Overview button. LED Calibration section: per-channel R/G/B gamma sliders (StyledSlider, Step=0.1, accent-tinted), 6 test color swatches with live hardware preview, auto-clear on tab switch. (OSD settings moved to OsdView in v0.8.1.)

- **OsdView:** OSD overlay toggles (volume/profile/device) with per-type duration sliders (StyledSlider, Step=0.5, separate value labels), position grid picker, monitor selector, hide-in-fullscreen checkbox. Quick Wheels section: dynamic add/remove rows, each with Mode (Profiles/Output Device) + Trigger Button. Any knob navigates. Auto-syncs button HoldActions.

- **BindingsView (sidebar: "Overview"):** Profile Overview page showing all knob/button assignments across profiles. Knob cards tinted with LED color. Button cards show all gestures with colored TAP/DBL/HOLD badges. Preview OSD button per profile. Click any card to switch to that profile and navigate to the correct tab.

- **AmbienceView:** Govee device cards with on/off, brightness, color scenes, music mode. On/off persists PoweredOn to config (prevents color sync from implicitly turning on devices). Brightness slider + on/off checkbox update live from Govee knob turns. DreamView screen sync card with monitor picker (friendly names via DisplayConfig API), FPS/zone selectors, saturation/sensitivity sliders (ShowLabel=false), live zone preview. Device cards dim when DreamView is active.

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

**GitHub Actions workflow is DISABLED** (`.github/workflows/release.yml.disabled`).
Installers are built manually on Tyson's Windows PC:
1. Run `deploy.bat` to pull + build + test
2. Run `build-installer.bat` to produce the Inno Setup `.exe`
3. Upload installer to GitHub Releases manually

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
- Config: `%AppData%\AmpUp\config.json`

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
- **AllowsTransparency=true breaks Win11** — creates WS_EX_LAYERED window, mouse hit-testing fails on Canvas/Image. Never use on popup/dialog windows.
- **WPF TextBlock has no LetterSpacing** — not a real WPF property, don't try to set it
- **WPF emoji render monochrome** — Unicode emoji in TextBlock render as black glyphs, not color. Use colored text/shapes instead.
- **Parallel agents can mismatch APIs** — when agents build caller and callee in parallel, method names may not match. Always verify interface alignment or build sequentially.
- **Govee LAN API** — UDP multicast to 239.255.255.250:4001 for discovery, listen on 4002. Control via unicast to device IP:**4003**. Rate limit: max 10 sends/sec. Supports on/off, brightness, solid color, and per-segment colors via "razer" command (binary frames with base64 encoding). Segment protocol: enable=`BB 00 01 B1 01 [xor]`, colors=`BB [len] B0 00 [count] [RGB...] [xor]`, auto-disables after ~60s (keepalive every 30s).
- **Govee Cloud API** — REST at openapi.api.govee.com, header `Govee-API-Key`. Supports scenes, segments, music mode. Rate limit: ~100 req/min, 10k/day. API key from Govee app settings. No speed control for dynamic scenes (animation speed is baked into preset).
- **Govee DreamView conflict** — DreamView sends screen colors at 30fps via LAN UDP. Cloud scene commands get immediately overridden. UI dims device cards when DreamView is active.
- **WPF Popup HWND isolation** — Popups live in separate native windows, making MouseLeave/MouseEnter unreliable. GridPicker and ActionPicker avoid this by using borderless Window flyouts with inline sub-panels (no second window needed).
- **Monitor friendly names** — `Screen.DeviceName` only gives `\\.\DISPLAY1`. Use `NativeMethods.GetMonitorFriendlyNames()` (DisplayConfig API) for real names like "DELL U2723QE".
- **ScreenCapture gamma** — sRGB pixels must be linearized before averaging, then re-encoded. Simple RGB averaging produces darker/muddier results.
- **StyledSlider ShowLabel** — Set `ShowLabel = false` when the value is displayed in a separate label to avoid duplicate display under the thumb.
- **Turn Up device doesn't auto-report positions** — Device only sends health pings on connect, NOT a knob batch. Must send `FE 01 FF` (info request) to get initial positions. Discovered via serial probe (all commands 0x00-0x20 tested, only 0x01 responds).
- **USB chip:** WCH CH343 (`VID_1A86 PID_55D3`), serial number `5185001494`. Device ID from firmware: `00 1F CC E4`.
- **WASAPI master peak very low** — Master endpoint `AudioMeterInformation.MasterPeakValue` returns ~0.02-0.04 for normal audio. Per-app sessions return 0.2-0.5. VU meter uses 2.3x uniform boost.
- **Active window reads AmpUp's own PID** — `GetActiveWindowVolume` must skip `Environment.ProcessId` to avoid showing wrong volume when mixer window is focused.
- **Govee device SKU can be empty** — Devices added before LAN scan SKU detection have empty `Sku` field. `GetSegmentCount` falls back to name matching (e.g. "Light Bar" → 6 segments).
- **Govee LAN control port is 4003** — Not 4001 as some docs suggest. Discovery uses multicast 4001, listen on 4002, but all control commands go to unicast device:4003.
- **Govee colorwc implicitly turns on device** — Sending any color command turns on a powered-off Govee device. AmbienceSync checks `device.PoweredOn` before sending. 5-second startup guard on Govee brightness (same as HA/monitor).
- **Turn Up gamma table unused** — Official Turn Up source (JaredWF/TurnUpCustomizer) defines a gamma8 table but never applies it in SetLightColors. Raw RGB is sent. Our default gamma is 1.0 (linear) to match.
- **MS Store apps use helper processes** — Apple Music audio runs through AMPLibraryAgent, not AppleMusic process. AudioMixer indexes sessions by WASAPI DisplayName to catch these. FuzzyContains strips spaces for matching.
- **Tray popup DPI on multi-monitor** — Screen.WorkingArea returns physical pixels but WPF Left/Top expect DIPs. Must convert via TransformFromDevice. Show window off-screen first so PresentationSource is available for DPI calc.
- **Vertical taskbar positioning** — Detect taskbar side by comparing WorkingArea to screen Bounds. If WorkingArea.Right < Bounds.Right → taskbar on right, etc.
- **OSD phantom popups on reconnect** — Device reconnect sends knob batch, each fires HandleKnob → OSD. Suppressed by: 5s startup guard, 2s reconnect guard (`_connectedAt`), and value-change guard (±8 ADC from last OSD display).
- **StyledSlider ShowLabel clips at small heights** — Label draws below thumb at cy+ThumbRadius+4. Use ShowLabel=false with separate TextBlock for sliders under ~35px height.
- **build-installer.bat working directory** — Must use `%~dp0` to locate AmpUp.csproj relative to script, not current working directory. Otherwise version extraction fails silently.
- **Mac: TCC audio permission per binary** — macOS grants Microphone/audio recording permission per executable path. Each new build location needs its own permission grant. Must launch from Terminal (not SSH) the first time for the permission dialog to appear. SSH sessions cannot show TCC prompts.
- **Mac: Avalonia NativeMenu blocks all exit calls** — `Environment.Exit()`, `Process.Kill()`, `_exit()`, and even `kill -9` via `Process.Start` all fail when called from an Avalonia NativeMenu click handler on macOS. The handler runs in a Cocoa context that swallows exit attempts. **Solution:** Use a background polling thread started at app init that watches a `_wantQuit` volatile bool flag. The menu handler sets the flag; the background thread calls `Environment.Exit(0)` from its own thread context. This was a multi-hour debugging session — do NOT try to exit from NativeMenu handlers directly.
- **Mac: Cleanup() can hang on quit** — `_serial?.Dispose()` and other resource disposal can block the main thread during quit, preventing exit code from running. **Solution:** Run all Cleanup dispose calls on a background thread with a 2-second `ManualResetEventSlim` timeout so quit always proceeds.
- **Mac: MainWindow.Closing cancels Shutdown** — Both `MainWindow` and `TrayIconManager` had Closing handlers that unconditionally cancelled close (hide-to-tray). This prevents `lifetime.Shutdown()` from working. **Solution:** `IsQuitting` flag checked by all Closing handlers — when true, don't cancel.
- **Mac: deploy.sh must use dotnet exec** — `dotnet run --project` launches from the project directory where `libAmpUpAudio.dylib` doesn't exist. Must use `dotnet exec` from `bin/Debug/net8.0/osx-arm64/` where the dylib is copied by `build.sh`.
- **Mac: DOTNET_ROOT required for native host** — The `AmpUp.Mac` native executable needs `DOTNET_ROOT=/opt/homebrew/opt/dotnet@8/libexec` set in the environment to find the .NET runtime. Only needed for dev builds; release .app bundles are self-contained.
- **Mac: media keys via AppleScript app control** — CGEvent posting requires Accessibility permission. osascript+System Events also requires Accessibility. **Solution:** Use direct AppleScript app control (`tell application "Spotify" to playpause`) which needs no permissions. Falls back to Music app if Spotify isn't running.
- **Windows: tray context menu dismissed by popup Deactivated** — Right-clicking a session row opened the context menu window, which stole focus from the tray popup, triggering Deactivated→Hide(). **Solution:** `_contextMenuOpen` flag suppresses Hide() during Deactivated while context menu is open.
- **Windows: OSD monitor picker stale on display change** — Monitor combo only populated on load. **Solution:** Refresh on `IsVisibleChanged` (tab switch) and `SystemEvents.DisplaySettingsChanged`.
- **Windows: tray device enumeration blocks UI** — `EnumerateAudioEndPoints` + `AudioSessionManager.Sessions` for non-default devices can take 3-5s with USB/Bluetooth devices. **Solution:** Move PID→device name map building to `Task.Run` with a separate `MMDeviceEnumerator`. Popup opens instantly, device badges fill in async.

---

## Version History

- **v0.3.1-alpha** — Renamed from WolfMixer to AmpUp. Icon design, GitHub release.
- **v0.3.2-alpha** — Audio-Reactive RGB (FFT), Mute App Group, Visual Curve Picker, Global Lighting, 6 new effects (Breathing/Fire/Comet/Sparkle/GradientFill), Profile Switch Transitions, EffectPickerControl, ActionPickerControl, App Group toggles, Power action tiles, custom scrollbars, full UI polish pass.
- **v0.4.0-alpha** — 8 new LED effects, styled sliders, dynamic accent theming, view redesigns, chromeless color picker, file browse button, column-based button layout.
- **v0.4.1-alpha** — Fix: Double Press and Hold gestures now show all context controls (device picker, macro, profile, power, knob) — previously only path textbox. Per-gesture config fields so each gesture has independent settings. New CheckListPicker multi-select for cycle_output/cycle_input device subset selection.
- **v0.5.0-alpha** — Auto-ducking (DuckingEngine: fade other apps when trigger app speaks), auto-profile switching (AutoProfileSwitcher: foreground-window based profile selection), tray quick-mixer popup (left-click tray icon to show per-app volume sliders), profile export/import (save/load profile JSON files via file dialog). New Settings UI sections for all three features.
- **v0.5.1-alpha** — Quick Assign from Tray (right-click tray → Assign Running Apps submenu → pick knob), Auto-Detect & Suggest Layout (amber banner in MixerView when known apps detected and knobs unconfigured), Knob Copy/Paste (right-click channel strip → Copy/Paste/Reset context menu with static clipboard).
- **v0.5.2-alpha** — Bug fix: knob UI updates immediately on hardware turn (push position directly to MixerView via Dispatcher.BeginInvoke, bypassing 50ms poll). Bug fix: potentiometer jitter deadzone in SerialReader (±3 ADC count threshold suppresses noise). New LED effect: DeviceSelect — shows per-device colors based on which Windows output device is currently default (up to 3 device→color mappings per knob, configured in LightsView).
- **v0.5.x (Mar 11 polish)** — Copy/paste context menus for Lights and Buttons views. UI consistency audit + tooltips across all views (standardized headers, labels, ComboBox styles; tooltips on every control explaining what it does). Moved Auto-Ducking and Auto-Profile Switching from Settings to collapsible "Smart Mix" section in Mixer tab. Friendly serial port selector with auto-detect (COM port dropdown, auto-detect button probes for CH343/CH340, connection status indicator, raw port/baud hidden under Advanced). 4 new per-knob LED effects (PositionBlend, Wheel, RainbowWheel, ProgramMute). Mute Device button action. 13 new global-spanning LED effects (TheaterChase, RainbowScanner, SparkleRain, BreathingSync, FireWall, DualRacer, Lightning, Fillup, Ocean, Collision, DNA, Rainfall, PoliceLights).
- **v0.6.0-alpha (Mar 12)** — **Ambience tab**: Govee LAN sync + Cloud dashboard. 4-step API key setup wizard. AppGroupMute LED effect. Process picker UX. Win11 color picker fix. Bug audit (8 fixes). Theme consistency pass.
- **v0.7.0-alpha (Mar 14)** — **Polish & ease of use release.** Inline sub-panel pickers (GridPicker/ActionPicker use single borderless Window with inline right panel — eliminates cross-HWND hover bugs). HA targets show "Home Assistant" with domain subtitle. Govee uses sub-flyout for device selection. App Group chips (pill tags replace checkboxes). Smart Mix redesign (Voice Ducking with ListPicker, App Profiles with dynamic add/remove rules). OSD horizontal 5-column profile layout (number badges, all 3 gestures with colored TAP/DBL/HOLD badges, LED color tints, configurable per-type duration). DreamView improvements (gamma-correct screen capture, dark pixel filtering, real monitor names via DisplayConfig API, device cards dim when active). Profile Overview page (renamed from Bindings, profile-aware navigation, Preview OSD button, LED color card tints). StyledSlider ShowLabel toggle. Wider combo boxes throughout.
- **v0.7.2-alpha (Mar 14)** — **Bug fix release.** Fixed purple LED colors (gamma 2.8→2.0). Fixed DeviceSelect persistence (CollectAndSave was wiping DeviceMappings). Fixed multi-monitor tray menu positioning (Screen.FromPoint). OSD monitor selector with friendly names. Fixed tray mixer popup crash (Track.AppendChild). Fixed VU meters (persistent MMDevice for COM objects). Fixed VU freezing on theme change (Loaded handler restarts timer).
- **v0.7.3-alpha (Mar 14)** — **Unified tray popup.** Left+right click open same popup with mixer, device switcher (click to cycle output/input), app assignment, Open/Exit, update banner. Ambient glow effect on mixer cards (audio-reactive, LED-tinted). Smooth knob lerp animation. Live LED color sync in UI (rainbow/fire effects cycle in mixer). OSD throttled to 10fps. Performance: visibility guards on all UI timers, brush caching, ~60% less background CPU.
- **v0.7.4-alpha (Mar 14)** — **Hardware position request.** Discovered `FE 01 FF` command requests knob positions from device on connect (confirmed via serial probe — device only sends health pings, not auto-batch). Button cards dynamic height. OSD friendly labels. Full Turn Up protocol mapped (0x00-0x20 probed, only 0x01 responds).
- **v0.7.5-alpha (Mar 14)** — **Per-segment DreamView.** Govee devices with known segments (H6056=6) receive individual colors via segment protocol instead of single solid color. Screen zones proportionally mapped to device segments. Fixed DreamView not syncing (auto-create device mappings). Fixed active_window showing wrong volume when AmpUp focused (skip own PID). Standard green/orange/red VU meters (2.3x boost). Channel label input UX (green caret, dark bg on focus, select-all).
- **v0.7.6-alpha (Mar 14)** — **Bug fix: profile switching via button.** Fixed button-triggered profile switch discarding unsaved config (button bindings reverted when switching back). UI edits now persist to profile file immediately.
- **v0.7.7-alpha (Mar 15)** — **DPI fix.** Fixed flyout popup positioning on multi-monitor setups with mixed DPI scaling (#6). All 7 PointToScreen sites corrected for PerMonitorV2.
- **v0.8.0-alpha (Mar 15)** — **Interactive hardware widget + macOS port foundation.** Hardware device visualization on Overview page (live knob positions, LED colors, button states, tooltips). Profile editor (rename, icon, color — real-time save). Duplicate profile + reorder. AmpUp.Core shared library extracted (10-step refactor for cross-platform). OSD startup suppression. Unknown action color fix.
- **v0.8.1-alpha (Mar 16)** — **Quick Wheel OSD + Monitor Brightness fix.** New OSD tab (moved OSD settings from Settings, added Quick Wheel config). RadialWheelOverlay: glass radial pie-menu for profile switching — hold button to open, spin knob or mouse hover to navigate, release/click to select, Escape to dismiss. Three input modes: hardware knob (30 ADC delta threshold), mouse (hover+click), keyboard (Escape). `quick_wheel` button action auto-syncs between OSD tab and Buttons tab. Monitor brightness throttle fix (#7): cached DDC/CI handles + 60ms throttled `SetThrottled()` with last-value-wins pattern. 5-second startup guard on monitor brightness (prevents flicker on boot, same pattern as HA). Quick Wheel added to button action picker under System category.
- **v0.8.2-alpha (Mar 16)** — **Quick Wheel mode selector + tray audio activity.** Quick Wheel supports two modes (Profile / Output Device) selectable in OSD settings. Any knob navigates (removed single-knob restriction). 8-slot flower layout with profile icons/colors. Tray popup shows per-app audio peak level bars. Fuzzy process name matching for MS Store apps (#8). Phantom OSD suppression (reconnect + value-change guards). Buttons tab syncs when Quick Wheel toggled.
- **v0.8.3-alpha (Mar 16)** — **Purple LED fix + LED Calibration.** Confirmed official Turn Up app sends raw RGB (no gamma). Default gamma now 1.0. LED Calibration in Settings: per-channel R/G/B gamma sliders with live hardware preview (test color swatches). WASAPI DisplayName session indexing fixes Apple Music (#8). Installer version extraction fix.
- **v0.8.4-alpha (Mar 16)** — **Multi-monitor tray fix + fullscreen OSD.** DPI-aware tray popup positioning (pixel→DIP conversion). Vertical taskbar detection. Hide OSD in fullscreen option. Quick Wheel follows theme accent color.
- **v0.8.5-alpha (Mar 16)** — **Multiple Quick Wheels + Govee fixes.** Config changed from single QuickWheel to list of QuickWheels — each button can trigger a different wheel mode. OSD layout redesign (vertical flow, separate value labels). Govee PoweredOn state: on/off checkbox persists to config, color sync respects it, 5s startup guard. Govee knob turns update Ambience UI live (on/off checkbox + brightness slider). StyledSlider Step/LabelFormat properties for decimal sliders.
- **v0.1.0-alpha-mac (Mar 15)** — **First macOS release.** Per-app volume control via Core Audio Process Taps (first hardware mixer to do this on Mac). Avalonia UI with dark theme. All views: Mixer, Buttons, Lights, Settings. Serial + LEDs + buttons all working on Apple Silicon.
- **v0.8.5-alpha (Mar 17)** — **Massive update: Mac port complete, tray overhaul, bug fixes from first user (Rapdactyl).**
  - **User-reported bug fixes:** Instant mute LED feedback (OnVolumeNotification callbacks), solid mute LED colors (removed unwanted breathing/pink shift), tray icon resilience (survives monitor config changes via DisplaySettingsChanged + WM_TASKBARCREATED).
  - **Tray mixer overhaul (EarTrumpet-inspired):** 32px rounded app icons, scroll wheel volume on rows, volume % tray icon indicator, per-app device badges, search/filter bar, pin apps to top, System Sounds row, scroll wheel on tray icon for master volume, middle-click mute toggle, Quick Assign panel (app grid + inline knob picker), right-click context menu (assign to knob, move to device, hide/show), themed HoverComboBox device dropdowns, live mute state polling, deduplicated app list (Discord fix), friendly display names (Apple Music fix).
  - **New views:** Live Hardware Preview strip (bottom bar — LED colors, knob positions, VU levels), Audio Dashboard (Activity tab — all audio sessions with levels, knob assignments, quick-assign).
  - **Mac port feature-complete:** All views ported to Avalonia, editable pickers (knob/button/LED), menu bar tray icon, .app bundle + DMG installer, Govee LAN/Cloud + HA + DreamView wiring, auto-update, Hardware Preview strip, Audio Dashboard, real OSD overlay (transparent topmost window), keyboard shortcuts (Cmd+1-6), full polish audit.
  - **Build:** Framework-dependent builds (installer ~55MB → ~5-8MB), .NET 8 Desktop Runtime auto-detection in installer.
  - **Repo:** Mac code merged into master (single branch), versions synced (both 0.8.5-alpha).

- **v0.9.0-alpha (Mar 17)** — **Major UI overhaul + cross-platform polish.**
  - **Lights redesign:** Segmented "Per Knob / Global" tab toggle replaces checkbox. Presets hidden until Global enabled, shown inside Global card.
  - **Audio Sessions in Mixer:** Collapsible section replaces Activity tab. Inline knob assignment pills, hide/show apps, no STATUS column. Session list doesn't rebuild while assign panel is expanded.
  - **Tray popup redesign:** Footer removed. Header has AMP UP (click to open app), Quick Assign button, X close. Device dropdown with chevron arrow — click name to quick-cycle checked devices, click chevron for full list. Checkboxes for quick-swap device subset (persisted to config). Tray follows app theme accent color (ThemeManager.Accent, not Windows system blue). Update banner moved above sessions.
  - **Per-knob LED preview:** Color picker only lights up the knob being edited. Global picker still previews all 5.
  - **PositionFill fix:** Uses single primary color (was incorrectly blending color1→color2 like PositionBlend).
  - **Material icon fallbacks:** Apps without extractable icons show MaterialIconKind.Application instead of plain letter.
  - **Modernized button path inputs:** Rounded borders (CornerRadius=6), Material FolderOpen/FormatListBulleted icons, vertically centered text.
  - **Accent-colored Overview buttons:** Duplicate, Preview OSD, and arrow buttons use accent-tinted styling.
  - **Nav indicators removed:** Dot/bar indicators removed — accent-colored text/icon is sufficient.
  - **Blue window border removed:** BorderThickness=0 on FluentWindow.
  - **OSD ghost suppression:** Threshold bumped from ±8 to ±15 ADC counts.
  - **Button startup guard:** 5s startup + 2s reconnect guard on button events prevents phantom sleep/actions on app open.
  - **Hardware preview footer hidden:** HardwarePreview strip collapsed, row height set to Auto.
  - **Lights view scrollable:** Wrapped in ScrollViewer (was clipped at bottom).
  - **Import removed from nav:** Settings moved to bottom of sidebar. Import button added to Welcome dialog instead.
  - **Deploy.bat fix:** Uses `git fetch origin && git reset --hard origin/master` instead of `git reset --hard HEAD` (fixes conflicts from Mac .sh files).
  - **Build fixes:** AmpUp.Mac excluded from WPF glob in .csproj, all warnings suppressed (VoiceMeeter SupportedOSPlatform, nullable dereference).
  - **CycleDeviceSubset config:** New `Dictionary<string, List<string>>` field for persisting quick-swap device subsets.
  - **Mac: Material Icons throughout** — Added Material.Icons.Avalonia package. All nav icons, button actions, ambience scenes, audio dashboard use MaterialIcon controls matching Windows.
  - **Mac: RGB lights fix** — Knob positions initialized to 1.0 on connect + ApplyColors() immediate render. Config (lights, gamma, brightness) applied on startup and save.
  - **Mac: Quick Wheel working** — Hold-button opens RadialWheelOverlay, knob navigates segments, release confirms selection. Profile switching via wheel.
  - **Mac: Audio device selection** — Native Swift bridge for Core Audio device enumeration. Settings view shows output/input device dropdowns with all available devices.
  - **Mac: App identity** — CFBundleName/CFBundleDisplayName set to "Amp Up". Proper macOS app menu (About, Preferences, Quit). Tray icon shows app logo.
  - **Mac: Native window controls** — Traffic light buttons (close/minimize/zoom) visible. Header bar draggable.
  - **Mac: Swift 6.2 fixes** — CATapDescription API, AudioHardwareCreateProcessTap, VirtualMasterVolume→VirtualMainVolume rename, peakSize let→var.

- **v0.9.2-alpha (Mar 19)** — **Bug fix release + Mac audio/quit fixes.**
  - **Windows bug fixes:** Tray right-click context menu no longer instantly dismissed (_contextMenuOpen flag). OSD monitor picker refreshes on display change (DisplaySettingsChanged + IsVisibleChanged). Tray popup crash fix (IsVisible guard on Hide). Tray popup instant open (device enumeration moved to background thread).
  - **Mac: per-app volume fixed** — libAmpUpAudio.dylib wasn't loading because deploy.sh used `dotnet run` (wrong working dir). Fixed to `dotnet exec` from build output. Also fixed dylib not copied to osx-arm64 RID folder.
  - **Mac: media keys working** — Play/Pause/Next/Prev buttons use direct AppleScript app control (`tell application "Spotify" to playpause`). No Accessibility permission needed.
  - **Mac: quit fully working** — Extensive fix for Avalonia on macOS refusing to exit. NativeMenu handlers block all exit calls (Environment.Exit, Process.Kill, _exit, kill -9). Solution: background "quit watcher" thread polls a volatile bool flag, calls Environment.Exit from its own context. Cleanup runs on background thread with 2s timeout to prevent hanging. All quit paths (tray, Dock, Cmd+Q) route through same mechanism.
  - **Mac: dev workflow** — Desktop shortcuts: "AmpUp Test" (AppleScript app, launches dev build silently), "AmpUp Build" (pulls + builds). Production install at /Applications/AmpUp.app with auto-updater.

---

## Release Workflow

### Windows
1. `deploy.bat` — pull + Debug build + launch (for testing)
2. Tell Howl to bump version → updates `AmpUp.csproj` + `AmpUp.Mac.csproj` + `Info.plist`
3. `build-installer.bat` — Release publish (framework-dependent ~5-8MB) + Inno Setup installer
4. Tell Howl → creates GitHub release + uploads `.exe`

### macOS
1. SSH to Mac: `ssh audio@192.168.189.234`
2. `cd ~/Projects/AmpUp.Mac/AmpUp.Mac && chmod +x *.sh && ./deploy.sh` — pull + build (for testing)
3. Double-click **AmpUp Test** on Mac desktop to launch dev build (AppleScript app, no Terminal)
4. Double-click **AmpUp Build** on Mac desktop to pull + rebuild (alternative to SSH)
5. `./bundle.sh` — full .app bundle + DMG for release
6. Tell Howl → uploads `.dmg` to same GitHub release
7. Production install: `/Applications/AmpUp.app` (auto-updater manages this)

**First-time setup on new Mac binary:** Must launch from Terminal once (not SSH) to grant TCC audio permission. Each build path needs its own permission grant.

### Version bumping
- Both platforms share the same version number
- Files to update: `AmpUp.csproj`, `AmpUp.Mac/AmpUp.Mac.csproj`, `AmpUp.Mac/Info.plist`
- Howl handles all three when asked

### Build types
- **Windows:** Framework-dependent (requires .NET 8 Desktop Runtime — auto-detected by installer)
- **Mac:** Self-contained ARM64 .app bundle in DMG (no runtime needed)

---

## Roadmap

### Completed ✅
- [x] **OBS Studio integration** — source gain, mute, scene switching (WebSocket v5)
- [x] **VoiceMeeter integration** — strip/bus gain control and mute
- [x] **Plugin system** — interfaces (ITurnUpPlugin, etc.) + LED presets (12 built-in)
- [x] **Exponential2 response curve** — steeper x³/10000 for fine control at low volumes
- [x] **Audio device type distinction** — separate Media vs Communications vs Both when cycling/selecting
- [x] **Mac: editable views** — knob target picker, button action picker, light effect picker
- [x] **Mac: proper .app bundle** — drag-to-Applications DMG install
- [x] **Mac: menu bar tray icon** — NSStatusBarItem with quick mixer popup
- [x] **Mac: Govee LAN/Cloud integration** — wired into App + AmbienceView
- [x] **Mac: Home Assistant integration** — wired into button actions
- [x] **Mac: DreamView screen capture** — CGWindowList implementation
- [x] **Mac: auto-update** — GitHub releases download + install flow
- [x] **Mac: Hardware Preview** — live 5-knob status bar
- [x] **Mac: Audio Dashboard** — real-time session view with quick-assign
- [x] **Mac: OSD overlay** — transparent topmost window for volume/profile/device changes
- [x] **Tray mixer overhaul** — EarTrumpet-inspired polish, search, pin, Quick Assign, context menus
- [x] **Framework-dependent builds** — ~5-8MB updates instead of ~55MB
- [x] **Instant mute LED feedback** — volume notification callbacks
- [x] **Solid mute LED colors** — no unwanted breathing animation
- [x] **Tray icon resilience** — survives monitor/taskbar changes

### Remaining
- [ ] **Multi-device support** — multiple Turn Up units simultaneously, each with own profile
- [ ] **Streamlabs integration** — source gain, mute, scene switching
- [ ] **SteelSeries Sonar integration** — volume and mute control
- [ ] **System theme following** — match light/dark mode on both Windows and Mac
- [ ] **GitHub Actions CI** — auto-build Windows .exe + Mac .dmg on release
- [ ] **Razer Chroma integration** — RGB sync
- [ ] **Advanced macro system** — per-key-event macros with delays (from Turn Up source)
- [ ] ~~**Mac: Intel support**~~ — shelved (tiny audience: only 2018-2020 Intel Macs on Sonoma 14.2+)
