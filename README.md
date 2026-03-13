<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>A powerful replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, macro buttons, and a sleek dark UI.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.6.0--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## :sparkles: What's New in v0.6.0

### :bulb: Ambience Tab — Govee Room Lighting Sync
Brand-new sidebar tab for syncing your room lights with AmpUp:

- **Govee LAN sync** — mirror knob RGB colors to your Govee lights in real-time at 20 FPS
- **Govee Cloud dashboard** — on/off, brightness, color control, scenes, segment colors, and music mode from within AmpUp
- **4-step API key setup wizard** — guided onboarding to connect your Govee account
- **Brightness scaling and warm tone shift** — fine-tune how knob colors translate to room light output

### :new: AppGroupMute LED Effect
New reactive per-knob effect: shows **Color1** when the knob's app group is unmuted, switches to **Color2** when all apps in the group are muted. Instant feedback at a glance.

### :mag: Process Picker for Buttons
`mute_program` and `close_program` button actions now include a **▾ picker** showing all currently running processes — no more typing process names. Improved labels and tooltips throughout the button config UI.

### :bug: Win11 Color Picker Fix
`AllowsTransparency` on the color picker window was breaking hit-testing on Windows 11. Fixed — the color picker now responds correctly to all mouse events.

### :shield: Bug Audit — 8 Stability Fixes
- Config write race condition (concurrent saves could corrupt config.json)
- Serial port dispose on reconnect (COM port not fully released on disconnect)
- AudioAnalyzer thread safety (FFT band reads were unsynchronized)
- Tray mixer popup memory leak (event handlers not detached on close)
- Mute poll reentrancy (overlapping timer ticks caused double-toggle)
- Plus 3 additional stability and edge-case fixes

### :art: Theme Consistency
- **Accent color reserved for primary controls** — green accent now used only for the most important interactive elements
- **All sliders theme-aware** — sliders update to match the current accent color dynamically

---

## :zap: Why Amp Up?

Amp Up is a community-built alternative for the Turn Up USB mixer with a modern dark UI inspired by SteelSeries Sonar and Elgato Wave Link. It extends the hardware with smart mixing automation, Home Assistant integration, response curves, volume range clamping, and more.

---

## :joystick: Features

### :musical_keyboard: Mixer — 5 Channel Strips
- :sound: **Per-app volume control** — assign any knob to a specific app, master volume, mic input, or the active window
- :link: **App groups** — control multiple apps with a single knob
- :chart_with_upwards_trend: **Response curves** — Linear, Logarithmic, or Exponential per channel
- :straight_ruler: **Volume range clamping** — restrict a knob to sweep only a portion of the volume range (e.g. 40–100%)
- :bar_chart: **Live VU meters** — real-time audio level visualization with peak hold
- :bulb: **Monitor brightness** — control physical monitor brightness via DDC/CI
- :mute: **Smart Mix** — Auto-Ducking and Auto-Profile Switching in a collapsible Mixer tab section

### :video_game: Buttons — 15 Programmable Actions
- :point_up_2: **3 gestures per button** — single press, double press, and hold (each with independent settings)
- :hammer_and_wrench: **26 action types:**
  - Media controls (play/pause, next, previous)
  - Mute toggles (master, mic, per-app, active window, app group, output device)
  - App launcher / process killer
  - Audio device switching (cycle with optional device subset, or direct select)
  - Keyboard macros (any key combo)
  - System power (sleep, lock, shutdown, restart, hibernate, logoff)
  - Profile switching
  - LED brightness cycling

### :rainbow: Lights — Full RGB Control
- :sparkles: **30+ LED effects** — per-knob effects (SingleColor, ColorBlend, PositionFill, GradientFill, PositionBlend, PositionBlendMute, Wheel, RainbowWheel, Blink, Pulse, Breathing, Fire, Comet, Sparkle, PingPong, Stack, Wave, Candle, RainbowWave, RainbowCycle, MicStatus, DeviceMute, ProgramMute, AppGroupMute, DeviceSelect, AudioReactive)
- :globe_with_meridians: **17 global-spanning effects** — Scanner, MeteorRain, ColorWave, Segments, TheaterChase, RainbowScanner, SparkleRain, BreathingSync, FireWall, DualRacer, Lightning, Fillup, Ocean, Collision, DNA, Rainfall, PoliceLights (all 15 LEDs as one continuous strip)
- :art: **Dual color support** — primary and secondary colors per effect
- :fast_forward: **Speed control** — adjustable animation speed per knob
- :high_brightness: **Global brightness** — master brightness slider for all LEDs
- :clipboard: **Copy / Paste** — right-click any LED column to copy settings to another

### :busts_in_silhouette: Profiles
- :floppy_disk: **Save and load** full configurations (knobs, buttons, lights)
- :arrows_counterclockwise: **Switch profiles** via button press or sidebar profile picker
- :outbox_tray: **Export / Import** — save profiles as standalone JSON files
- :framed_picture: **Colored Fluent icons** — 40+ icons with 10 color presets per profile
- :video_game: **Per-game / per-workflow** setups with Auto-Profile Switching

### :tv: OSD Overlay
- :window: **Glassmorphism notifications** — sleek dark glass overlay with green glow accent
- :loud_sound: **Volume changes** — shows knob label, percentage, and animated fill bar
- :busts_in_silhouette: **Profile switches** — colored Fluent icon + profile name
- :speaker: **Device switches** — output/input device name with icon
- :round_pushpin: **Configurable position** — 6 screen positions (Fraps-style picker in Settings)
- :control_knobs: **Per-type toggles** — enable/disable OSD for volume, profiles, and devices independently

### :package: Tray Integration
- :speaker: **Quick Mixer** — left-click tray icon for compact per-app volume popup
- :mag: **Quick Assign** — right-click tray → Assign Running Apps → pick a knob instantly

### :bulb: Ambience — Room Lighting
- :globe_with_meridians: **Govee LAN sync** — mirror knob RGB colors to Govee lights in real-time at 20 FPS
- :cloud: **Govee Cloud dashboard** — control on/off, brightness, color, scenes, segment colors, and music mode
- :key: **API key setup wizard** — 4-step guided onboarding to connect your Govee account
- :high_brightness: **Brightness scaling and warm tone shift** — fine-tune how knob colors translate to room light output

### :gear: System Integration
- :house: **Home Assistant** — connect to your smart home for automation triggers
- :arrow_up: **Auto-update** — checks for new releases on startup with one-click install
- :rocket: **Start with Windows** — launches silently to system tray
- :zap: **Hot-reload config** — changes apply instantly, no restart needed
- :electric_plug: **Auto COM port detection** — friendly dropdown with auto-detect for the Turn Up device

---

## :inbox_tray: Install

### Installer (Recommended)
1. Download `AmpUp-Setup-0.6.0-alpha.exe` from [Releases](https://github.com/audioslayer/ampup/releases)
2. Run the installer
3. Amp Up appears in your system tray

### Build from Source
```powershell
git clone https://github.com/audioslayer/ampup.git
cd ampup
dotnet build
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Output: `bin\Debug\net8.0-windows\AmpUp.exe`

> **Note:** Kill the official Turn Up app first — it holds the COM port.
> ```powershell
> taskkill /f /im "Turn Up.exe"
> taskkill /f /im TurnUpService.exe
> ```

---

## :wrench: Configuration

Open the app from the system tray to access four configuration tabs:

| Tab | What You Configure |
|---|---|
| :musical_keyboard: **Mixer** | Knob targets, response curves, volume range, app groups, Smart Mix |
| :video_game: **Buttons** | Actions for tap / double-press / hold per button |
| :rainbow: **Lights** | LED effects, colors, speed, global brightness |
| :bulb: **Ambience** | Govee LAN sync, Govee Cloud dashboard, API key setup |
| :gear: **Settings** | Serial port, startup, profiles, Home Assistant |

All changes save automatically and apply in real-time.

---

## :knobs: Knob Targets

| Target | Controls |
|---|---|
| Master | Windows master volume |
| Mic | Default microphone input level |
| Monitor | Physical monitor brightness (DDC/CI) |
| Active Window | Volume of the currently focused app |
| App Group | Multiple apps on one knob |
| Any | First active audio session not already assigned |
| Process name | Any substring match (e.g. `discord`, `spotify`, `chrome`) |
| Output Device | Specific audio output by device ID |
| Input Device | Specific audio input by device ID |

---

## :electric_plug: Hardware

Amp Up is designed for the **Turn Up** USB volume mixer (CH343 USB-to-serial). The device has:
- :control_knobs: 5 rotary knobs (10-bit resolution, 0–1023)
- :radio_button: 5 push buttons (supporting tap, double-press, and hold)
- :bulb: 15 RGB LEDs (3 per knob)

---

## :hammer_and_wrench: Tech Stack

- **C# / .NET 8** — WPF with WPF-UI (Fluent design + Mica backdrop)
- **NAudio** — WASAPI per-app audio control and FFT audio analysis
- **System.IO.Ports** — serial communication with Turn Up hardware
- **Custom controls** — animated arc knob, 16-segment VU meter, styled sliders
- **Code-behind architecture** — lightweight, no MVVM overhead

---

## :world_map: Roadmap

- [x] OSD overlay for volume, profiles, and device switching
- [x] Auto COM port detection
- [x] Auto-update checker
- [x] Global-spanning LED effects (15 LEDs as one strip)
- [x] Auto-Ducking (Smart Mix)
- [x] Auto-Profile Switching
- [x] Tray Quick Mixer
- [x] Profile Export / Import
- [x] Govee room lighting sync (Ambience tab — LAN + Cloud)
- [ ] OBS WebSocket integration (scene switching, source control)
- [ ] VoiceMeeter strip/bus control
- [ ] Multi-device support
- [ ] FanControl integration
- [ ] OpenRGB integration (sync AmpUp LEDs with other RGB hardware)
- [ ] Mobile companion app (view VU meters, swap profiles from phone)
- [ ] Velopack delta updates (faster, smaller incremental app updates)

---

## :page_facing_up: License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a>
</p>
