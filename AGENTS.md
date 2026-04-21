# AmpUp — Claude Code Context

AmpUp is a C# .NET 8 WPF app that replaces the "Turn Up" USB volume mixer app.
It reads serial events from the Turn Up hardware and maps them to Windows per-app audio volume control.
Uses WPF-UI (FluentWindow, Mica backdrop) with a glassmorphism dark theme, sidebar navigation, and code-behind pattern (no MVVM).

**macOS port: v0.1.0-alpha released** — see [CLAUDE-MAC.md](CLAUDE-MAC.md) for Mac-specific docs, SSH access, and architecture plan.
**Shared library:** `AmpUp.Core/` contains platform-agnostic code (models, serial, RGB, config, integrations).
**Official Turn Up source:** We have admin access to `JaredWF/TurnUpCustomizer` — protocol fully confirmed, see memory for analysis.

## Future Controller Target Notes

### TreasLin / VSDinside N3

- Exact Amazon listing Tyson referenced/ordered: `https://www.amazon.com/TreasLin-Controller-Customizable-Creators-Compatible/dp/B0FM3NP9ZB`
- Product family: TreasLin / VSDinside **N3** mini stream controller
- Inputs/surface: **6 LCD keys + 3 physical buttons + 3 knobs** (knobs support left/right rotation + press)
- Vendor software: **VSD Craft** (Windows + macOS)
- Public SDK exists: `VSDinside/VSDinside-Plugin-SDK` with a Stream Deck-like WebSocket/plugin model
- Important direct-USB evidence:
  - `4ndv/opendeck-akp03` explicitly supports **TreasLin N3**
  - reported **VID `0x5548` / PID `0x1001`**
  - `bitfocus/companion-surface-mirabox-stream-dock` issue #21 contains reverse-engineering notes for N3-family HID events
- Current conclusion: **direct HID/USB control looks realistic**, and the target end state is **native self-contained AmpUp support** with no long-term dependency on VSD Craft, OpenDeck, or helper software. Current research says the strongest implementation clues live in `mirajazz`: protocol v3, usage page `65440` / usage `1`, init via `CRT ... DIS` + `CRT ... LIG`, ACK-prefixed input packets, 60x60 JPEG images rotated 90°, and `STP`-style display commit behavior. Windows firmware reads are currently unreliable in the known Rust stack, so firmware detection should be treated as optional during first tests
- Recommended evaluation order when hardware arrives:
  1. confirm VID/PID and enumerate interfaces
  2. capture button / knob / knob-press events without relying on VSD Craft long-term
  3. identify required init packets
  4. test LCD image updates separately
  5. decide whether to support via raw HID, VSD plugin path, or both
- Treat this as an **R&D controller target**, not guaranteed production hardware yet

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
  MixerView.xaml / .cs     5 channel strips — knob, VU meter, volume %, target/curve/range controls (sidebar label: "Mixer")
                           App group uses chip/pill tags. Smart Mix (Voice Ducking + App Profiles) built in code-behind.
  ButtonsView.xaml / .cs   5 Turn Up button columns (tap/double/hold gesture rows).
                           Stream Controller V2 designer is the active code path for N3:
                             - Two-column split. LEFT = skeuomorphic chassis with 6 LCD tiles (120x120),
                               page dots + nav arrows below, add/remove-page buttons above, 3 side-buttons
                               and 3 encoder cards at the bottom. Breadcrumb banner on top when inside a
                               Space: [← HOME] › 📐 <space name>.
                             - RIGHT = inline-editable "Play"-style header, preview card + Choose Icon,
                               Material-style underline tab bar (DESIGN / ACTION) with a slim magnifier
                               icon next to ACTION that pops the search box above SELECTED.
                             - DESIGN tab: Display Type pills (Normal/Clock/Dynamic), Title, Text Position,
                               Font Size, TEXT COLOR / ICON COLOR / GLOW COLOR swatch rows (14 presets +
                               rainbow custom picker). ICON + GLOW rows hide when the key uses a bitmap
                               instead of a preset icon.
                             - ACTION tab: QuickActionPicker (accordion categories with Material icons +
                               category badges). Selecting an action with options (Device, Macro, Path,
                               Folder, Toggle, Multi-Action, etc.) renders the option controls INSIDE the
                               picker via OptionsHost — no separate card floating below. Sub-section
                               headers use the FOLDERS style (accent bar + bold label) and legacy inner
                               labels are auto-suppressed so the header never duplicates.
                             - Spaces (formerly "Folders") management below the chassis: "Home" always
                               pinned at the top as the default Space, followed by user-created Spaces.
                               "ACTIVE" pill shows the current Space; Home can't be renamed or deleted.
  LightsView.xaml / .cs    Global lighting card + 5 LED columns — effect picker (hover preview on hardware),
                           Custom/Presets color tabs (20 premium gradient palettes), speed slider.
                           Material-style underline tab bar (PER KNOB / GLOBAL).
                           Per-knob preset tiles use card layout (gradient + label, hover accent border).
                           Effect presets auto-set ideal colors (Fire=orange/red, Ocean=blue/teal, etc.).
  SettingsView.xaml / .cs  Connection, startup, profiles, OSD duration sliders, integrations (HA + Govee side-by-side)
  RoomView.xaml / .cs      Room lighting — Material-style UNDERLINE tab bar (ROOM EFFECT / LAYOUT / DEVICES).
                           Top toggle row: [AMP UP] [MUSIC REACTIVE] [VU FILL] [SCREEN SYNC] [GAME MODE].
                           Sub-row when Music Reactive on: SENSITIVITY slider (1-100%).
                           Sub-row when VU Fill on: MODE pills (Classic / Split / Rainfall / Pulse / Spectrum / Drip).
                           ROOM EFFECT tab: two-column layout. LEFT: category tab bar (underline style:
                             STATIC / ANIMATED / REACTIVE / GLOBAL SPAN) filtering EffectPickerControl.
                             RIGHT: 280px dark card settings panel with PALETTE (gradient editor + 24
                             preset swatches in 2-row wrap layout), SPEED slider, BRIGHTNESS slider,
                             DIRECTION pills (Mirror is default).
                           LAYOUT tab: 420px room canvas, dimensions row, device placement + device tray.
                           DEVICES tab: per-device controls (Govee, Corsair, Turn Up sections).
                             Turn Up section: Sync Screen + Turn Up Mixer checkboxes.
                           Screen Sync / Game Mode activation stops Music Reactive + VU Fill; Game Mode
                           exit restores previous state. BuildToggleTile / BuildStatusTile helpers.
  OsdView.xaml / .cs       OSD settings (moved from Settings) + Quick Wheel config
                           Checkboxes for volume/profile/device OSD, position picker, duration sliders.
                           Quick Wheel: enable toggle, trigger button picker, navigation knob picker.
                           Auto-syncs button HoldAction with Quick Wheel config.
  BindingsView.xaml.cs     Profile Overview — all knob/button assignments across profiles with Preview OSD button
                           (sidebar label: "Overview"). Cards tinted with LED colors. Click navigates to correct profile.
                           Per-profile section now includes a STREAM CONTROLLER block when HardwareMode allows
                           and the user's Active Surface picks SC: 2x3 LCD preview grid (via
                           CreateEditorPreview) + 3 side buttons (SB1-SB3) + 3 encoder-press cards (E1-E3).
                           ShouldShowTurnUpOverview / ShouldShowStreamControllerOverview follow the
                           user's Active Surface dropdown so picking "Stream Controller" hides the
                           Turn Up widget and knob/button rows entirely.

Controls/
  AnimatedKnobControl.cs   WPF FrameworkElement — arc sweep knob, glow, frozen resources
  VuMeterControl.cs        WPF FrameworkElement — 16-segment VU meter, DrawingVisual, peak hold
  CurvePickerControl.cs    3 clickable mini graphs showing Linear/Log/Exp response curves
  EffectPickerControl.cs   Categorized grid of animated preview tiles for LED effects (~60 effects)
                           4 categories: STATIC (8), ANIMATED (17), REACTIVE (7), GLOBAL SPAN (30)
                           SetVisibleCategory() filters the grid for RoomView's category tab bar.
                           Hover preview: fires EffectHovered event to preview on hardware LEDs
                           Each tile renders a live mini-visualizer via EffectPreviewControl
  EffectPreviewControl.cs  Live animated mini-visualizer for effect tiles — 57 unique renders
                           (gradient fills, EQ bars, sine waves, ECG heartbeat, fire flicker, aurora drift).
                           Shared 30 FPS timer, auto-pauses hidden tiles. 82px wide unified tile size.
  PhosphorIcon.cs          Phosphor Duotone icon control — base layer at 0.2 opacity + detail at full.
                           29 icons defined in Icons/PhosphorIcons.xaml ResourceDictionary.
                           Used for sidebar nav (Gear, SlidersHorizontal, Keyboard, Lightbulb, etc.).
  ActionPicker.cs          Categorized action dropdown with inline sub-panel for button actions
  QuickActionPicker.cs     Search-first action picker for the Buttons V2 designer. Accordion of
                           categories (Media / Mute / App Control / Device / System / Power /
                           Integrations / Stream Controller / Advanced), each with a Material
                           icon + accent badge. First category auto-expands; per-category open
                           state persists across rebuilds. Public OptionsHost panel below the
                           SELECTED row that the caller fills with action-specific option
                           controls. ShowSearch() reveals the search box; the trigger lives
                           outside the picker (in the right-pane tab bar).
  StreamControllerTile.cs  Unified tile for the N3 key grid + side buttons + encoders. Live
                           theme-aware AccentColor (reads ThemeManager.Accent at render time,
                           subscribes to OnAccentChanged / OnCardThemeChanged). Selection ring
                           is a diagonal gradient brush + big diffuse DropShadow that wraps
                           the rounded corners. 120x120 fixed for LCD tiles. Inner preview
                           uses explicit RectangleGeometry clip (WPF Border.CornerRadius
                           doesn't auto-clip children even with ClipToBounds=true).
  GlassContextMenu.cs      GlassContextMenuHost.Show(anchor, items) renders a modern glass-style
                           context menu — rounded dark card, accent gradient hairline at top,
                           Material icon per row (red for IsDanger), hover accent tint,
                           cascading submenus, and a check glyph in place of the icon for
                           IsChecked rows. Used for the N3 key right-click menu.
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
  PaletteEditorControl.cs  Gradient stop editor + 24 preset swatches (40x20px with labels, 2-row wrap,
                           hover/active states). Filled-dark-circle Add Stop button positioned right
                           of gradient bar. Drives room effect PALETTE settings and LED color2/color1.
  ScreenEdgeControl.cs     Front-view monitor control with draggable crop lines for pillarbox/letterbox
                           content boundaries (used in Screen Sync device settings)
  GoveeSetupGuide.cs       4-step wizard window for Govee API key setup

AmbienceSync.cs            Govee LAN UDP sync engine — discovery, color mirroring, rate limiting
GoveeCloudApi.cs           Govee Platform REST client — devices, scenes, segments, music mode
AudioAnalyzer.cs           WASAPI loopback capture + FFT — 5 frequency bands for audio-reactive LEDs
StreamControllerDisplayRenderer.cs
                           Renders an N3 display key to a bitmap/JPEG. Two render paths:
                             CreateEditorPreview(key, size=256) — high-quality PNG for in-app UI
                             (skips the 60x60 device JPEG round-trip so vector icons stay crisp).
                             CreateDeviceJpeg(key) / ComposeDeviceBitmap + EncodeDeviceBitmap — 60x60
                             rotated JPEG for the hardware; the compose step is UI-thread-bound (WPF
                             render) but encode+send are thread-safe so the caller can Task.Run them.
                           Honors key.IconColor (tints MaterialIcon Foreground) and key.AccentColor
                           (radial glow behind the icon). Clock keys overwrite title with formatted time.
AmpUp.Core/
  Protocol/SerialReader.cs Reads COM port, parses fe/ff frames, fires OnKnob / OnButton events, ±5 jitter deadzone
                           Endpoint snap: raw >1000 → 1023. Deadzone on raw value before snap.
                           Sends FE 01 FF on connect to request knob positions from hardware
  Engine/VolumePipeline.cs Pure math: raw ADC (0-1023) → normalize → response curve → volume range → 0-1
  Engine/RgbController.cs  RGB effects engine — ~60 effects (per-knob + global spanning), 20 FPS animation
                           Per-channel gamma (SetGamma) — default 1.0 linear. Preview color override for calibration.
                           Smooth fire (Candle-style smoothing), per-LED phase offset on Pulse.
                           Effect hover preview via temporary config override.
  Services/PresetManager.cs LED preset save/load (JSON files next to exe)
  Models/BuiltInPalettes.cs 20 premium gradient palettes (Fire, Ocean, Sunset, Neon, Ice, Forest, Lava,
                           Galaxy, Mint, Storm, Cyberpunk, Sakura, Twilight, Coral Reef, Lavender,
                           Copper, etc.). Each palette has 6-7 color stops for rich gradients.
  Services/AmbienceSync.cs  Govee LAN UDP sync — discovery, color mirroring, per-segment frames
                           (SendSegmentFrame is public for VU Fill per-segment control). Multi-session
                           support per process (compound key name:pid) for Discord volume fix.
  Services/N3Controller.cs Native HID driver for the TreasLin / VSDinside N3 stream controller
                           (VID 0x5548 / PID 0x1001). TryConnect opens the vendor MI_00 interface,
                           runs CRT DIS + CRT LIG init, starts read loop + keepalive. Sleep() sends
                           the real firmware standby command (CRT HAN); Wake() re-inits. SendDisplayImage
                           streams JPEGs wrapped in CRT BAT / CRT STP frames.
AudioMixer.cs              WASAPI per-app volume + GetPeakLevel, response curves, volume range
                           Persistent _renderDevice for session COM objects; dedicated _masterPeakDevice for metering
                           Skips AmpUp's own PID for active_window target
                           Sessions indexed by both process name AND WASAPI DisplayName (for UWP/MS Store apps)
                           FuzzyContains: strips spaces before matching ("Apple Music" → "AppleMusic")
ButtonHandler.cs           Gesture state machine (press/double/hold) → 26 action types (incl. mute_device)
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
ScreenCapture.cs           GDI screen capture with 2D zone grid, gamma-correct averaging, dark pixel filtering
AmpUp.Core/
  Engine/ScreenSpatialMapper.cs  Maps device positions to screen regions for DreamView spatial sync
                           Uses monitor placement + room layout to compute per-device screen sampling regions

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
    "effectSpeed": 50, "reactiveMode": "SpectrumBands",
    "idleEffect": "PositionBlend"
  },
  "ambience": {
    "goveeEnabled": false,
    "goveeApiKey": "",
    "syncRoomToTurnUp": false,
    "musicSensitivity": 50,
    "vuFillMode": "Classic",
    "goveeDevices": [
      { "ip": "192.168.1.50", "name": "Living Room Strip", "sku": "H6056", "syncMode": "global", "useSegmentProtocol": true, "poweredOn": true }
    ],
    "brightnessScale": 75,
    "warmToneShift": false,
    "screenSync": {
      "enabled": false, "monitorIndex": 0, "targetFps": 30, "zoneCount": 8,
      "saturation": 1.2, "sensitivity": 5,
      "syncToTurnUp": false,
      "contentBounds": { "leftPct": 0, "rightPct": 0, "topPct": 0, "bottomPct": 0, "autoDetect": true },
      "deviceMappings": [{ "deviceIp": "192.168.1.50", "side": "Full", "useAutoSpatial": false, "cropMode": "Content" }]
    }
  },
  "roomLayout": {
    "widthFt": 12, "depthFt": 10, "heightFt": 8,
    "direction": "LeftToRight",
    "monitor": { "x": 6, "y": 1, "z": 3.5, "rotation": 0, "widthFt": 2.8, "heightFt": 1.0, "monitorIndex": 0 },
    "devices": []
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
  "cardTheme": "Midnight",
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
| `"sc_space_cycle"` | N3 encoder only — twist cycles Home + each Space (signed delta = direction) |
| `"sc_page_cycle"` | N3 encoder only — twist cycles pages in the active Space |

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

**Stream Controller (N3 only):**
| Action | Behavior |
|-|-|
| `"sc_page_next"` | Advance to the next page within the active Space |
| `"sc_page_prev"` | Back to the previous page |
| `"sc_page_home"` | Jump to page 0 |
| `"sc_go_to_page"` | Jump to a specific page (1-based) stored in `path` |
| `"open_folder"` | Navigate into a named Space from `folderName` (+ auto-generated Back key) |

**Advanced:**
| Action | Behavior |
|-|-|
| `"multi_action"` | Run a sequence of actions from `actionChain` with optional delays |
| `"toggle_action"` | Alternate between two actions each press (A / B / A / B…) |
| `"open_url"` | Open `path` in the default browser |
| `"type_text"` | Type the stored text snippet at the cursor |
| `"screenshot"` | Save a screenshot to the user's Pictures folder |

### Button gesture types
Each button supports 3 gestures (15 total bindings):

| Gesture | Config fields | Detection |
|-|-|-|
| **Single press** | `action` + `path` | Released < 500ms, no 2nd press within 300ms |
| **Double press** | `doublePressAction` + `doublePressPath` | 2nd press within 300ms of first release |
| **Hold** | `holdAction` + `holdPath` | Held 500ms+ (fires while holding) |

### LED effects

**Static (8):**
| Effect | Colors | Description |
|-|-|-|
| `SingleColor` | 1 | Color scaled by knob position (default) |
| `ColorBlend` | 2 | Lerp between color1→color2 based on knob position |
| `PositionFill` | 1 | Smooth progressive fill — each LED fades in across its third (matches Turn Up source) |
| `PositionBlend` | 2 | Smooth fill with color gradient (color1→color2 across LEDs) |
| `PositionBlendMute` | 2 | PositionBlend + dims to Color2 when linked knob's group is muted |
| `CycleFill` | 2 | Progressive fill + color cycles between color1/color2 over time |
| `RainbowFill` | — | Progressive fill + rainbow hues cycling, each LED offset 120° |
| `GradientFill` | 2 | Static gradient color1→color2 across 3 LEDs |

**Animated (17):**
| Effect | Colors | Description |
|-|-|-|
| `Blink` | 2 | Alternate between color1/color2 at configurable speed |
| `Pulse` | 2 | Sine-wave oscillation with per-LED phase offset (ripple effect) |
| `Breathing` | 2 | Smooth brightness fade with squared easing + LED phase offset |
| `Fire` | 2 | Smooth candle-style flicker — LED 0=embers, LED 2=flame tips. Speed-adjustable. |
| `Comet` | 2 | Bright pixel chases across 3 LEDs with fading tail |
| `Sparkle` | 2 | Random LED flashes white, decays back to dim base |
| `PingPong` | 2 | Bright dot bounces back and forth with smooth sub-pixel interpolation |
| `Stack` | 2 | LEDs build up one by one, pause, reset |
| `Wave` | 2 | Sine brightness wave travels across 3 LEDs with 120° phase offset |
| `Candle` | 2 | Smooth organic candle flicker with independent per-LED smoothing |
| `RainbowWave` | — | HSV rainbow across all 15 LEDs, speed-adjustable |
| `RainbowCycle` | — | 3 LEDs per knob each get different hue (120° apart), speed-adjustable |
| `Wheel` | 2 | Single bright dot rotates with dim trailing fade |
| `RainbowWheel` | — | Tightly-spaced rainbow hues (40° apart), rotating |
| `Heartbeat` | 1 | Realistic lub-dub double pulse with pause, speed controls BPM |
| `Plasma` | — | Psychedelic overlapping sine waves, organic flowing color |
| `Drip` | 1 | Liquid droplet forms at LED 0, falls to LED 2, splashes |

**Reactive/Status (7):**
| Effect | Colors | Description |
|-|-|-|
| `MicStatus` | 2 | Color1=unmuted, Color2=muted (mic state) |
| `DeviceMute` | 2 | Color1=unmuted, Color2=muted (master state) |
| `AudioReactive` | 2 | FFT-driven. SpectrumBands uses 3-LED VU fill per band. Modes: BeatPulse, SpectrumBands, ColorShift |
| `ProgramMute` | 2 | Color1=unmuted, Color2=muted (watches single program by name) |
| `AppGroupMute` | 2 | Color1=any unmuted, Color2=all muted (watches linked knob's app group) |
| `DeviceSelect` | per-device | Shows mapped color based on current default audio output device |
| `AudioPositionBlend` | 2 | Music reactive with position blend fallback — crossfades between PositionBlend and AudioReactive based on audio energy. Global mode: configurable idle effect (e.g. Ocean) when silent, crossfades to audio-reactive on music |

**Global Spanning (30):** *(only active in Global Lighting / Room modes, per-knob fallback = solid color1)*
| Effect | Description |
|-|-|
| `Scanner` | KITT/Cylon dot sweeps back and forth with fading tail |
| `MeteorRain` | Bright meteor with fading tail, wraps |
| `ColorWave` | Scrolling gradient of color1→color2 via 3 overlapping sine layers + brightness squaring |
| `Segments` | Rotating barber-pole bands |
| `TheaterChase` | Every 3rd LED lit, pattern shifts |
| `RainbowScanner` | Scanner with rainbow hue |
| `SparkleRain` | Multiple simultaneous sparkles across 15 LEDs |
| `BreathingSync` | Sine-wave brightness ripple across all 15 LEDs |
| `FireWall` | Smooth random + 3 sine layers, HSV warm hues (red→orange→yellow) |
| `DualRacer` | Two dots racing opposite directions, additive blend on overlap |
| `Lightning` | Random dramatic strikes cascading from center |
| `Fillup` | LEDs fill left→right, pause, drain right→left |
| `Ocean` | HSV color-shifting (blue/teal/cyan) rolling waves with whitecap foam + brightness squaring |
| `Collision` | Two pulses race toward center, collide with white flash |
| `DNA` | Double helix — two sine waves in opposite directions |
| `Rainfall` | Drops fall with splash effects on impact |
| `PoliceLights` | Double-flash police pattern, left=color1, right=color2 |
| `Aurora` | Northern lights — slow-drifting green/blue/purple bands |
| `Matrix` | Digital rain with bright heads and fading green trails |
| `Starfield` | Gentle twinkling stars with warm/cool color variation |
| `Equalizer` | 5-band audio VU visualization across 15 LEDs |
| `Waterfall` | Colors cascade downward with speed control |
| `Lava` | 4 independent Gaussian blobs moving via sine motion (lava lamp feel) |
| `VuWave` | Audio-reactive wave rippling, bass-driven ripples |
| `NebulaDrift` | Full-spectrum rainbow Aurora (all 360 hues with multi-layer sines) |
| `AudioPositionBlend` | Global mode: crossfades between `idleEffect` (e.g. Ocean) and AudioReactive on beats |

### Audio-Reactive Modes (ReactiveMode)
| Mode | Behavior |
|-|-|
| `BeatPulse` | Bass (band 1) drives ALL knob brightness simultaneously |
| `SpectrumBands` | Each knob = its own frequency band. 3 LEDs show VU-meter fill per band. |
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
| `Ripple` | Outward ripple from center |
| `ColorBurst` | Burst of color expanding outward |
| `Wipe` | Color wipe across all 15 LEDs |

All transitions run 1 second (20 ticks at 20 FPS) then auto-clear.

---

## AudioAnalyzer (FFT)

`AudioAnalyzer.cs` captures system audio via `WasapiLoopbackCapture` and runs 1024-sample Hann-windowed FFT to extract 5 frequency bands. NormRef=0.005f (WASAPI loopback levels are very low). Smoothing: fast attack (0.5), slow decay (0.88). Lazy start/stop — only runs when at least one light uses AudioReactive. Thread safety via `lock(_lock)`.

| Band | Frequency Range | Musical Content |
|-|-|-|
| 0 | 20–80 Hz | Sub-bass |
| 1 | 80–250 Hz | Bass (kick drum, bass guitar) |
| 2 | 250–2000 Hz | Low-mid (vocals, instruments) |
| 3 | 2000–6000 Hz | High-mid (presence) |
| 4 | 6000–20000 Hz | Treble (cymbals, air) |

---

## Theme

### Accent Colors
```
Accent      = #00E676     AccentGlow  = computed (Lighten 40%)     AccentDim = computed (Darken 35%)
TextPrimary = #E8E8E8     TextSec     = #B0B0B0                   TextDim   = #8A8A8A
DangerRed   = #FF4444     SuccessGrn  = #00DD77                   WarnYellow= #FFB800
```

### Card Themes (12 themes, selectable in Settings → Appearance)

| Theme | BgBase | BgDark | CardBg | CardBorder | InputBg | InputBorder |
|-|-|-|-|-|-|-|
| Midnight (default) | #0F0F0F | #141414 | #1C1C1C | #2A2A2A | #242424 | #363636 |
| Blue Steel | #0A0E14 | #0F1520 | #152230 | #1E3048 | #1A2838 | #2A3E56 |
| Ocean | #081014 | #0C1820 | #10223A | #183050 | #142840 | #1E3E5E |
| Teal | #081210 | #0C1A18 | #102824 | #183834 | #14302C | #1E4842 |
| Ice | #0C1218 | #101A24 | #182838 | #24384E | #1E3040 | #2C4660 |
| Ember | #140A08 | #1C100C | #281810 | #3A2418 | #301E16 | #4A3228 |
| Forest | #081208 | #0C1A0E | #122414 | #1C3420 | #182C1A | #264030 |
| Violet | #100A16 | #18101E | #22182C | #30243C | #2A1E34 | #3E3050 |
| Rose | #140A10 | #1E0E16 | #2C1420 | #3E2030 | #341A28 | #4C2C3E |
| Slate | #0C0E12 | #121418 | #1A1E24 | #282E36 | #22282E | #343C44 |
| Obsidian | #060606 | #0A0A0A | #101010 | #1A1A1A | #151515 | #222222 |
| Mocha | #120C08 | #1A1210 | #261C16 | #382A22 | #2E221A | #443830 |

Themes are applied at runtime via `ThemeManager.SetCardTheme()` which updates all background/card DynamicResource brushes. Config field: `cardTheme` (string, default "Midnight").

Custom scrollbars: slim 8px, transparent track, accent-colored thumb with hover/drag brightness states.

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

### WPF Pitfalls
- **WPF-UI namespace conflicts:** `Wpf.Ui.Controls` has types like `Border` that conflict with `System.Windows.Controls.Border`. Always fully qualify when both namespaces are in scope.
- **AllowsTransparency=true breaks Win11** — creates WS_EX_LAYERED window, mouse hit-testing fails on Canvas/Image. Never use on popup/dialog windows.
- **WPF TextBlock has no LetterSpacing** — not a real WPF property, don't try to set it.
- **WPF emoji render monochrome** — Unicode emoji in TextBlock render as black glyphs, not color. Use colored text/shapes instead.
- **WPF Popup HWND isolation** — Popups live in separate native windows, making MouseLeave/MouseEnter unreliable. GridPicker and ActionPicker avoid this by using borderless Window flyouts with inline sub-panels.
- **OSD ShowKnobOsd must use Dispatcher** — HandleKnob runs on the serial reader thread. ShowKnobOsd touches WPF controls and MUST be wrapped in `Dispatcher.BeginInvoke`.

### Theming
- **Card themes use DynamicResource** — all background/card colors are bound via DynamicResource so `ThemeManager.SetCardTheme()` can swap them at runtime. Do not hardcode grey hex values for backgrounds.
- **Render-path brushes are frozen statics** — AnimatedKnobControl, VuMeterControl, and ChannelGlowControl freeze Pen/Brush as static fields for performance. These cannot be changed after creation. Use `ThemeManager.OnCardThemeChanged` to rebuild if needed.

### Config / Runtime
- **config.json is read from next to the `.exe`**, not the project root.
- **Single instance:** Only one AmpUp can run. Check Task Manager for stale process.
- **Newtonsoft.Json PascalCase** — config props in memory are PascalCase, JSON file is camelCase.
- **Cannot `dotnet build` on Linux** — WPF is Windows-only.
- **build-installer.bat working directory** — Must use `%~dp0` to locate AmpUp.csproj relative to script, not current working directory.

### Hardware
- **Turn Up device doesn't auto-report positions** — Device only sends health pings on connect. Must send `FE 01 FF` (info request) to get initial positions.
- **USB chip:** WCH CH343 (`VID_1A86 PID_55D3`), serial number `5185001494`. Device ID from firmware: `00 1F CC E4`.
- **Turn Up gamma table unused** — Official Turn Up source defines a gamma8 table but never applies it. Raw RGB is sent. Our default gamma is 1.0 (linear) to match.

### Audio
- **WASAPI loopback levels are very low** — NormRef=0.005f in AudioAnalyzer. If LEDs don't react, lower further.
- **WASAPI master peak very low** — `MasterPeakValue` returns ~0.02-0.04. VU meter uses 2.3x uniform boost.
- **Active window reads AmpUp's own PID** — `GetActiveWindowVolume` must skip `Environment.ProcessId`.
- **MS Store apps use helper processes** — Apple Music audio runs through AMPLibraryAgent. AudioMixer indexes sessions by WASAPI DisplayName. FuzzyContains strips spaces for matching.
- **Discord multi-session** — Discord creates multiple WASAPI sessions (voice + system). `RefreshSessions` stores ALL sessions per process with compound key `name:pid`.

### Timing
- **Single-press has ~300ms latency** — intentional for double-press detection.
- **Hold threshold is 500ms** — configurable in `ButtonHandler.cs`.

### Tray / OSD
- **Tray popup DPI on multi-monitor** — Screen.WorkingArea returns physical pixels but WPF Left/Top expect DIPs. Must convert via TransformFromDevice.
- **Monitor friendly names** — `Screen.DeviceName` only gives `\\.\DISPLAY1`. Use `NativeMethods.GetMonitorFriendlyNames()` for real names.
- **OSD shows curve-applied volume** — OSD uses `VolumePipeline.ComputeVolume()`, not raw knob position.

### Govee
- **Govee LAN API** — UDP multicast to 239.255.255.250:4001 for discovery, listen on 4002. Control via unicast to device IP:**4003**. Rate limit: max 10 sends/sec. Segment protocol: enable=`BB 00 01 B1 01 [xor]`, colors=`BB [len] B0 00 [count] [RGB...] [xor]`, auto-disables after ~60s (keepalive every 30s).
- **Govee colorwc implicitly turns on device** — AmbienceSync checks `device.PoweredOn` before sending.
- **Govee Cloud API** — REST at openapi.api.govee.com, header `Govee-API-Key`. Rate limit: ~100 req/min, 10k/day.

### Development
- **Parallel agents can mismatch APIs** — when agents build caller and callee in parallel, method names may not match. Always verify interface alignment or build sequentially.
- **New spanning effects must be in SpanningEffects HashSet** — or they silently fall back to solid color1 in Global mode.
- **AudioPositionBlend global idleEffect required** — `globalLight.idleEffect` must be set (e.g. Ocean) or silent passages show dim solid color.
- **Music Reactive thread safety** — `OnRoomFrame` must snapshot config at start and use `Task.Run` for segment sends.
- **VU Fill must StopRoomPattern before starting** — otherwise competing Govee segment frames fight at 20fps.
- **ScreenCapture gamma** — sRGB pixels must be linearized before averaging, then re-encoded.

### Stream Controller / N3
- **"Folders" is dead user-facing terminology** — renamed to "Spaces" everywhere the user sees, but internal fields / method names (`_currentN3Folder`, `NavigateToN3Folder`, `folder.Buttons`, `open_folder` action, config `Folders` list) stay as-is for backwards compat. When writing UI strings or docs, say Space/Home, not Folder/Root.
- **Border.CornerRadius does not clip children** — even with `ClipToBounds=true`, a Border's children render as rectangles. For rounded-image content like the LCD preview, set an explicit `RectangleGeometry` clip via SizeChanged. See `StreamControllerTile.BuildLcdKeyContent`.
- **WPF render of N3 LCD keys must stay on UI thread** — `StreamControllerDisplayRenderer.ComposeImage` builds a Grid + MaterialIcon + renders via RenderTargetBitmap, all dispatcher-bound. JPEG encode + HID write (EncodeDeviceBitmap + SendDisplayImage) are thread-safe, so the SC pipeline composes on UI, then Task.Run's the encode + send. Keeps folder/page navigation from freezing the UI.
- **N3 sleep must skip the display refresh** — while `_n3AsleepFromIdle` is true, SyncStreamControllerDisplays must NOT fire, since each HID frame visually wakes the LCDs. Also throttle the clock/dynamic refresh to 3s (was firing on every 1s idle-detection tick).
- **Folder-nav feedback loop** — `App.NavigateToN3Folder` BeginInvoke's a callback that bounces back through `SetActiveN3Folder` → `NavigateToFolderInEditor`. The synchronous `_v2FolderSyncing` guard is cleared before the queued callback fires, so the chain used to loop through the dispatcher forever. Guard with an idempotency check (`_scActiveFolder == folderName` → return) at the top of `NavigateToFolderInEditor`.
- **Hardware device probes are deferred post-show** — `InitializeHardwareDeferred` runs at `ApplicationIdle` priority after MainWindow.Show(). Corsair Start, LG monitor HID, N3 TryConnect, Screen Sync all run on their own `Task.Run` so window paints instantly; connection status updates are dispatched back when probes complete.
- **Active Surface vs HardwareMode** — `HardwareMode` is the connection gate (TurnUpOnly / StreamControllerOnly / DualMode / Auto). `TabSelection.PreferredSurface` is the user's UI preference when both devices are connected. Overview + Buttons + Mixer all honor PreferredSurface when HardwareMode is permissive; HardwareMode hard-limits what's shown otherwise.
- **SC knob encoders share the KnobConfig.Target pipeline** — `HandleN3EncoderTwist` calls `ApplyKnobConfig` same as Turn Up. New N3-only targets (`sc_space_cycle`, `sc_page_cycle`) short-circuit BEFORE the volume math and walk the Space / page list using `Math.Sign(e.Delta)` for direction.
- **SC mixer TARGET picker is integration-aware** — both Turn Up and SC now route through `PopulateTargetPickerItems(picker, config, includeN3Nav)`. SC calls `RebuildScTargetPickerItems` on every config load so App Group, HA, Groups, Room Lights, Govee, VoiceMeeter, and Corsair stay in sync with the user's integration toggles.
- **QuickActionPicker options live INSIDE the picker** — action-specific controls (DEVICE, PATH, MACRO, FOLDER, etc.) are parented into `_v2ActionPicker.OptionsHost`, right below SELECTED. `MakeV2SectionCard` auto-suppresses the legacy panel's first TextBlock label so the outer accent-bar header doesn't render the same word twice.
- **CreateEditorPreview vs CreateHardwarePreview** — editor previews (tile grid, big preview card, overview LCD thumbnails) use `CreateEditorPreview(key, size)` for a high-quality PNG at the requested resolution. `CreateHardwarePreview` still round-trips through the 60x60 device JPEG and should only be used when simulating what the device actually renders.
- **N3 page-1+ folder LCD idx collides with side buttons + encoder presses** — LCD keys on page ≥1 compute idx as `100 + page*6 + slot`, which overlaps 106-108 (side buttons) and 109-111 (encoder presses). The gesture engine's async hold/click timers resolve by bare idx alone, so without intervention a page-1 LCD press runs the global side/encoder binding. TWO fixes in App.xaml.cs: (a) runtime — the dispatcher calls `PreresolveLcdButton(idx)` before `HandleN3VirtualButton` to stash the folder ButtonConfig in `_n3ButtonOverride`, and the resolver prefers the stash; side/encoder branches clear the stash first; (b) editor — selection click path stamps `_v2SelectionKind` (LcdKey / SideButton / EncoderPress) from the click source so `IsN3PagedKeySelection` and `GetOwningButtonList` pick the right list instead of guessing by idx range.
- **V2 editor debounced-save gotcha** — `UpdateStreamControllerSelection` used to read action from `_scActionPicker` (legacy picker), which is only synced on Tap gesture. On Double/Hold the debounce would write Tap's action into DoublePressAction/HoldAction, clobbering the user's pick. Fix: prefer `_v2ActionPicker.SelectedValue`, fall back to `_scActionPicker` only when empty.
- **Tap / Double / Hold gesture bar** — shows at top of the ACTION tab for side buttons + encoder presses only (not LCD keys). `_v2Gesture` state selects which ButtonConfig fields get read/written via `GetGestureAction / SetGestureAction` (and parallel helpers for Path / MacroKeys / FolderName). LCD keys stay on Tap so their config flows unchanged.
- **Per-gesture FolderName** — ButtonConfig has `FolderName`, `DoublePressFolderName`, `HoldFolderName`. `ButtonGestureEngine` copies the right one into the cloned ButtonConfig for hold/double presses; empty gesture-specific value falls back to `.FolderName` so existing configs don't lose their binding on upgrade.
- **N3 device JPEG — do NOT fill corners black** — earlier code zeroed four 4x4 corner blocks assuming a rounded-corner UI mask would hide them. The N3 LCD shows the full pixel rectangle with no hardware mask, so those fills appear as visible black boxes on device. Kept disabled.
- **Space Templates live in `Services/SpaceTemplates.cs`** — factory-based; each template's `Build()` returns a fresh `ButtonFolderConfig` that the UI adds to `_config.N3.Folders` with unique-name collision handling (Media → Media (2)). The "+ Add" button in the TEMPLATES section also calls `NavigateToFolderInEditor(folder.Name)` so the user drops straight into the new Space. Ship-time pre-filled templates avoid the config-race problem we had when mutating `config.json` directly while AmpUp was running.
- **FX icon pack uses `fx_` prefix** — `TryResolveCustomPackImagePath` accepts `fx_`, `neon_`, `material_`, `retro_`, `synthwave_`, `cyber_` prefixes and checks both `.png` and `.jpg`. The 18 FX icons under `Icons/fx_*.jpg` were generated by Gemini 3 Pro image-gen and cropped to strip the app-store-mockup white surround — `PresetIconKind="fx_aurora"` etc. wires a DisplayKey to its icon with no absolute path required.

### Govee
- **Segment mode is lost on power-cycle** — H6056 / DreamView G1S Pro and other segment devices reset to default mode when they receive `turn=0`/`turn=1`. AmbienceSync's `_segmentEnabled` HashSet caches the segment-mode-on state and skips the SegmentEnable command when cached, so after a power-cycle the device sits ignoring razer segment frames for up to 25 s (the keep-alive interval). Fix: `SetGoveePower(dev, true)` calls `_ambienceSync?.ClearAllSegmentTracking()` so the next frame re-enables segment mode immediately.
- **Cloud-only devices need separate brightness path** — `AmbienceSync.SetBrightness` and `EnsureDevicesPoweredOn` both skip devices without an IP. The room_lights knob target handles cloud devices inline via `GoveeCloudApi.SetBrightness`, throttled to 1.5 s per device (the cloud API limits ~100 req/min; a knob spins would otherwise burn the daily quota in seconds). See `ApplyRoomLightsBrightness` in App.xaml.cs.

### Corsair
- **Stop()` only pauses `SyncColors`, not `SetStaticColorAllAsync`** — music reactive + VU Fill timers write directly via `SetStaticColorAllAsync` which bypasses the `_paused` gate. Without additional protection, those timers repaint Corsair immediately after the toggle's black frame. `CorsairSync.SetStaticColorAllAsync` now checks `_paused` at entry and returns early; music timer also guards on `_config.Corsair.Enabled` so the pause sticks.
- **Must flip `Corsair.Enabled = false` on off** — the off flow has to both `Stop()` the sync AND set `Corsair.Enabled = false`. Otherwise `OnConfigChanged`'s Start()/Stop() callback (App.xaml.cs:1033) sees Enabled=true on the next config save, calls Start() which clears `_paused`, and the LEDs come back on. Group/room/corsair toggles all mirror this pattern.

### Room toggle
- **HandleRoomToggle must mirror HandleCorsairToggle for Corsair** — saves the prior `Corsair.LightSyncMode` into `_roomToggleSavedCorsairMode` so on-press restores the exact mode. Without this, flipping lights off and on again forces `LightSyncMode = "vu_reactive"` and the user's static-color choice is lost.
- **ResumeRoomEffect falls back to `config.Ambience.RoomEffect`** — Govee devices power on to their last color (often white) and need a room-effect frame push to jump back to Aurora / etc. If `_activePattern` is null (user never opened the Room tab this session), fall back to the persisted `config.Ambience.RoomEffect` so the on-press still starts a pattern. Avoid the 800 ms `Task.Delay` — the 20 FPS frame loop catches any dropped frames during device power-up and you don't sit at white for a full second.

---

## Version History

- **v0.7.0-alpha** — Inline sub-panel pickers, app group chips, Smart Mix redesign, OSD overhaul, DreamView gamma-correct capture, Profile Overview page.
- **v0.7.2-alpha** — Bug fixes: purple LEDs, DeviceSelect persistence, multi-monitor tray, VU meters.
- **v0.7.3-alpha** — Unified tray popup (left+right click), ambient glow on mixer cards, smooth knob lerp, live LED color sync.
- **v0.7.4-alpha** — Discovered `FE 01 FF` info request command. Full Turn Up protocol mapped.
- **v0.7.5-alpha** — Per-segment DreamView, screen zone mapping, green/orange/red VU meters.
- **v0.7.6-alpha** — Fixed profile switching via button discarding unsaved config.
- **v0.7.7-alpha** — DPI fix for flyout popups on mixed-DPI multi-monitor setups.
- **v0.8.0-alpha** — Hardware device widget on Overview, profile editor, AmpUp.Core shared library extracted for cross-platform.
- **v0.8.1-alpha** — Quick Wheel radial OSD, monitor brightness DDC/CI throttle fix, OSD tab.
- **v0.8.2-alpha** — Quick Wheel mode selector (Profile/Output Device), tray audio activity bars, fuzzy process matching.
- **v0.8.3-alpha** — LED Calibration with per-channel gamma sliders, confirmed Turn Up sends raw RGB (gamma default 1.0).
- **v0.8.4-alpha** — DPI-aware tray popup, vertical taskbar detection, hide OSD in fullscreen.
- **v0.8.5-alpha** — Multiple Quick Wheels, Govee PoweredOn persistence, Mac port feature-complete, tray overhaul (EarTrumpet-inspired), framework-dependent builds.
- **v0.1.0-alpha-mac** — First macOS release with per-app volume via Core Audio Process Taps.
- **v0.9.0-alpha** — Major UI overhaul: Lights tab redesign, audio sessions in Mixer, tray popup redesign, per-knob LED preview.
- **v0.9.2-alpha** — Bug fixes: tray context menu, OSD monitor picker refresh, Mac per-app volume and quit fixes.
- **v0.9.3-alpha** — LED effects overhaul (6 new, 5 improved), OSD curve-applied volume, color presets, hardware hover preview.
- **v0.9.4-alpha** — Room tab redesign (AmbienceView renamed), Corsair enhancements, Music Reactive brightness modulation.
- **v0.9.5-alpha** — Groups global, Govee flyout sub-menus, Corsair devices moved to Settings.
- **v0.9.6-alpha** — Profile and Group pickers use flyout sub-menus.
- **v0.9.7-alpha** — Spatial Screen Sync, Room tab two-column redesign, 10 new spanning effects, 20 premium palettes, VU Fill 6 modes, critical Govee bug fixes.
- **v0.9.8-alpha** — Animated effect preview tiles, Phosphor Duotone icons, section cards UI pattern, Mixer/Lights/Room tab redesigns.
- **v0.9.9-alpha** — 12 card themes, 6 new room sweep effects (HSV-generated), label readability overhaul, Screen Sync redesign, HA searchable entity picker.
- **v0.9.10-alpha** — Stream Controller (N3) V2 designer pass, big terminology + UX overhaul:
  - **V2 Buttons tab:** skeuomorphic chassis with merged LCD grid + page dots + side buttons + encoders. Right pane is a Material underline tab bar (DESIGN / ACTION) with a slim magnifier icon next to ACTION that pops a search box above SELECTED. Inline-editable header matching the Mixer channel label pattern.
  - **Spaces (formerly "Folders"):** user-facing rename from "Folder"/"Root" → "Space"/"Home". Breadcrumb nav banner (`← HOME › 📐 Space Name`) replaces the old banner. Home is a pinned row at the top of the SPACES list; current Space shows an ACTIVE accent pill. Legacy internals (NavigateToN3Folder, folder config class) kept intact for compat.
  - **Glass context menu:** new `GlassContextMenuHost.Show()` renders a rounded dark card with accent gradient ribbon, per-row Material icons, hover accent tint, cascading submenus, and a check glyph for the active item. Replaces the default WPF ContextMenu on N3 key right-click.
  - **QuickActionPicker accordion:** replaces the old pill-based filter bar. Categories collapse/expand with a chevron + icon badge (Media / Mute / App Control / Device / System / Power / Integrations / Stream Controller / Advanced). Action-specific options render INSIDE the picker via OptionsHost — no separate card below.
  - **Design surface:** ICON COLOR + GLOW COLOR swatch rows in DESIGN (14 presets + rainbow custom picker). Icon picker now carries its accent forward to the key so the on-device glow matches the hue shown in the picker.
  - **High-quality previews:** `CreateEditorPreview(key, size)` renders directly at the requested pixel size instead of round-tripping through the 60x60 device JPEG. Editor preview uses 360px, tile grid 240px. Vector icons stay crisp at any scale.
  - **Stream Controller tile glow:** replaced flat hard 2px accent border with a shimmery diagonal gradient brush + beefy diffuse DropShadow. Theme-aware (live ThemeManager.Accent via OnAccentChanged). Inner preview clips to rounded corners via explicit RectangleGeometry so the selection ring wraps the corners cleanly.
  - **Overview SC support:** per-profile block now includes a STREAM CONTROLLER section (2x3 LCD preview grid + 3 side buttons + 3 encoders) when the user's Active Surface includes SC. Turn Up widget + knob/button rows hide entirely for SC-only setups.
  - **SC Mixer parity:** N3 encoders now get the full TARGET catalogue the Turn Up mixer has — App Group chip list, HA, Device Groups, Room Lights, Govee, VoiceMeeter, Corsair. Refactored via shared `PopulateTargetPickerItems` + `RebuildAppTogglesFor`.
  - **Knob-twist nav:** two new N3-only targets, `sc_space_cycle` (twist cycles Home + each Space) and `sc_page_cycle` (twist cycles pages in the active Space). Discrete — one detent = one step.
  - **Real N3 sleep command:** `CRT HAN` firmware standby (via N3Controller.Sleep / Wake) instead of brightness=0 dimming. System suspend + user-idle both put the N3 to sleep; wake re-inits and re-syncs displays. Display-refresh tick now skips while asleep so HID writes don't flicker the LCDs back on.
  - **Startup freeze fix:** Corsair iCUE Start, LG monitor HID open, N3 TryConnect + initial display sync, Screen Sync start all moved into `InitializeHardwareDeferred()` which runs at ApplicationIdle priority post-Show() on Task.Run — window paints instantly.
  - **Folder-nav freeze fix:** idempotency guard at the top of `NavigateToFolderInEditor` stops the dispatcher feedback loop that bounced back through `SetActiveN3Folder`.
  - **Sync display task split:** JPEG encode + HID writes (~400ms) now run on a background Task instead of blocking the UI — WPF compose stays on the UI thread via `ComposeDeviceBitmap` + `EncodeDeviceBitmap` split.
- **v1.0.0-beta** — First beta milestone. Pass focused on N3 polish + integration robustness + a designed icon pack.
  - **Space Templates** — new TEMPLATES section in the Buttons tab. One-click adds a pre-built ButtonFolderConfig (Room Effects, Media, Discord, System, Apps, Audio Profiles, Spotify) into the user's config with unique-name collision handling; editor auto-navigates into the new Space. Factory definitions live in `Services/SpaceTemplates.cs` — adding a template is ~10 lines.
  - **FX icon pack** — 18 bespoke neon-styled JPGs generated via Gemini 3 Pro image-gen, cropped to their dark interior, shipped under `Icons/fx_*.jpg`. `TryResolveCustomPackImagePath` gained an `fx_` prefix and now accepts both `.png` and `.jpg`. `PresetIconKind = "fx_aurora"` etc. wires a DisplayKey to the pack.
  - **Tap / Double / Hold gesture editor** — segmented bar at the top of the ACTION tab when editing a side button or encoder press. Switches the action picker and all sub-option cards (Path / Macro / Space / HA / Govee) to read/write the DoublePress* / Hold* ButtonConfig fields. LCD keys are unchanged.
  - **Per-gesture FolderName** — new `DoublePressFolderName` / `HoldFolderName` fields on ButtonConfig + fallback-to-Tap in ButtonGestureEngine let the same button open a different Space on tap vs double vs hold.
  - **N3 idx collision fixes** — page-1+ folder LCDs share idx 106-117 with side buttons + encoder presses. Two pieces: (a) runtime `_n3ButtonOverride` pre-resolution from the dispatcher; (b) editor `_v2SelectionKind` stamped at click time so `IsN3PagedKeySelection` + `GetOwningButtonList` pick the right list without guessing.
  - **Govee fixes**
    - Cloud-only devices (G1S Pro etc.) now follow Room Lights knob brightness via a cloud-API path throttled to 1.5 s/device.
    - Segment devices: `_segmentEnabled` cache is cleared on `SetGoveePower(true)` so the next frame re-enables segment mode immediately instead of sitting at the power-on default for up to 25 s.
    - Group toggles route every device through `SetGoveePower` (LAN + Cloud) so cloud-only devices aren't silently skipped on off.
  - **Corsair toggle fixes** — group toggle + room toggle both flip `Corsair.Enabled = false` on off (in addition to Stop() + static-mode). Otherwise the next `OnConfigChanged` callback re-Start()'s Corsair and the music timer repaints the LEDs. `SetStaticColorAllAsync` now respects `_paused`. Music-reactive timer also guards on `Corsair.Enabled`.
  - **Room toggle polish** — preserves the prior `Corsair.LightSyncMode` across off/on via `_roomToggleSavedCorsairMode`; `ResumeRoomEffect` falls back to `config.Ambience.RoomEffect` when `_activePattern` is null; dropped the 800 ms delay before firing ResumeRoomEffect so devices don't sit at white.
  - **Device JPEG corners** — dropped the 4x4 black corner fills from `EncodeForDevice`; they were masking non-existent rounded-corner clip on the N3 LCD and showed up as visible black boxes on device.
  - **Group toggle race fixes** — infers current group on/off state from `Govee.PoweredOn` + `Corsair.Enabled` + `LightSyncMode` before flipping, so the first press after startup does the right thing regardless of cached `_groupStates` values.

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

### Completed
- [x] **OBS Studio integration** — source gain, mute, scene switching (WebSocket v5)
- [x] **VoiceMeeter integration** — strip/bus gain control and mute
- [x] **Plugin system** — interfaces (ITurnUpPlugin, etc.) + LED presets (12 built-in)
- [x] **Exponential2 response curve** — steeper x^3/10000 for fine control at low volumes
- [x] **Audio device type distinction** — separate Media vs Communications vs Both when cycling/selecting
- [x] **Mac: editable views** — knob target picker, button action picker, light effect picker
- [x] **Mac: proper .app bundle** — drag-to-Applications DMG install
- [x] **Mac: menu bar tray icon** — NSStatusBarItem with quick mixer popup
- [x] **Mac: Govee LAN/Cloud integration** — wired into App + RoomView
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
