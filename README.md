<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>A powerful replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, macro buttons, and a sleek dark UI.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.8.5--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/platform-Windows%20|%20macOS-0078D6" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## :sparkles: What's New in v0.8.1 — Quick Wheel & Quality of Life

### :ferris_wheel: Quick Wheel — Radial Profile & Device Switcher
- **Hold a button** to open a radial pie-segment overlay centered on screen
- **Turn any knob** to spin through options — volume is suppressed while the wheel is open
- **Release to confirm** — switches to the highlighted profile or output device
- **Two modes:** Profile (8 slots with profile icons/colors) or Output Device (active audio outputs)
- Configurable trigger button and mode in OSD settings

### :speaker: Tray Popup Audio Activity
- **Live peak level bars** under each app's volume slider — see which apps are actively playing audio (like EarTrumpet)
- Bar color matches the app's icon color (Spotify=green, Discord=blue, etc.)

### :musical_note: Fuzzy Process Name Matching
- **MS Store app support** — "Apple Music" now matches "AppleMusic" process name
- Spaces are stripped before matching, so app names typed with spaces still work
- Applied to volume control, mute, app groups, and peak metering

### :sun_with_face: Monitor Brightness Fix
- **Throttled DDC/CI calls** — prevents overwhelming monitors with rapid-fire brightness commands
- **Startup guard** — skips brightness restore during the first 5 seconds to avoid flicker on launch

### :wrench: Quality of Life
- Quick Wheel shows on the OSD-configured monitor (not where the mouse is)
- Toggling Quick Wheel in OSD settings immediately syncs the Buttons tab
- Updated tray popup description text

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
- :window: **Glassmorphism notifications** — sleek dark glass overlay with accent glow border
- :loud_sound: **Volume changes** — shows knob label, percentage, and animated fill bar
- :busts_in_silhouette: **Profile switches** — horizontal 5-column layout mirroring the physical device (knob targets + button actions)
- :speaker: **Device switches** — output/input device name with icon
- :ferris_wheel: **Quick Wheel** — hold a button to open a radial pie-segment overlay, turn any knob to navigate, release to switch profiles or output devices
- :round_pushpin: **Configurable position** — 6 screen positions (Fraps-style picker in Settings)
- :stopwatch: **Per-type duration** — set how long each OSD type stays visible (1–8 seconds)
- :control_knobs: **Per-type toggles** — enable/disable OSD for volume, profiles, and devices independently

### :package: Tray Integration
- :speaker: **Quick Mixer** — left-click tray icon for compact per-app volume popup with live audio activity bars
- :mag: **Quick Assign** — right-click tray → Assign Running Apps → pick a knob instantly

### :bulb: Ambience — Room Lighting
- :globe_with_meridians: **Govee LAN sync** — mirror knob RGB colors to Govee lights in real-time at 20 FPS
- :cloud: **Govee Cloud dashboard** — control on/off, brightness, color, scenes, segment colors, and music mode
- :movie_camera: **DreamView — Screen Sync** — capture screen zones in real-time and map colors to Govee lights with gamma-correct color processing
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

### Windows (Recommended)
1. Download `AmpUp-Setup-0.8.5-alpha.exe` from [Releases](https://github.com/audioslayer/ampup/releases)
2. Run the installer (auto-installs .NET 8 Desktop Runtime if needed)
3. Amp Up appears in your system tray

### macOS
1. Download `AmpUp-0.8.5-alpha.dmg` from [Releases](https://github.com/audioslayer/ampup/releases)
2. Open the DMG, drag Amp Up to Applications
3. Launch from Applications (grant audio permission on first run)

### Build from Source
```bash
# Windows
git clone https://github.com/audioslayer/ampup.git
cd ampup && dotnet build

# macOS (Apple Silicon, requires .NET 8 SDK + Swift)
cd AmpUp.Mac && ./build.sh
```

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
- [x] DreamView screen sync (gamma-correct zone capture to Govee lights)
- [x] Quick Wheel — radial profile/device switcher (hold button + turn knob)
- [x] OBS WebSocket v5 integration (scene switching, source control)
- [x] VoiceMeeter strip/bus control
- [x] macOS port (Avalonia UI, Core Audio Process Taps, all views)
- [x] EarTrumpet-inspired tray mixer (search, pin, scroll wheel, Quick Assign)
- [ ] Multi-device support
- [ ] Mobile companion app (view VU meters, swap profiles from phone)
- [ ] Velopack delta updates (faster, smaller incremental app updates)

---

## :page_facing_up: License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a>
</p>
