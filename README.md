<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>A powerful replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, macro buttons, and a sleek dark UI.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.5.2--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## :sparkles: What's New in v0.5.x

### :speaker: Smart Mix — Auto-Ducking & Auto-Profiles
Two new intelligent mixing features live in a collapsible **Smart Mix** section in the Mixer tab:

- **Auto-Ducking** — automatically fades other apps when a trigger app (e.g. Discord) is speaking. Fully configurable trigger app, target apps, duck amount, and fade speed.
- **Auto-Profile Switching** — automatically switches profiles based on the foreground window. Map any app to any profile — no button press needed.

### :package: Tray Quick Mixer
**Left-click** the system tray icon to open a compact glassmorphic per-app volume mixer popup — adjust any running app's volume without opening the main window.

### :arrows_counterclockwise: Profile Export / Import
Save profiles as standalone `.json` files and load them back — great for sharing configs or keeping backups.

### :mag: Quick Assign from Tray
**Right-click** the system tray icon → **Assign Running Apps** → pick a knob to instantly bind any currently running audio app.

### :bulb: Auto-Suggest Layout
When known apps are detected running but their knobs are unconfigured, an amber banner appears in the Mixer view suggesting a layout with one click.

### :clipboard: Copy / Paste Everywhere
Right-click context menus on channel strips, LED columns, and button tiles for Copy / Paste / Reset — works across all three views.

### :electric_plug: Friendly Serial Port Selector
COM port dropdown with an **Auto-Detect** button that probes all ports for the CH343/CH340 chip. Shows connection status inline. Raw port/baud fields tucked under Advanced for power users.

### :video_game: Per-Gesture Independent Config
Each button gesture (tap / double / hold) now has its own independent set of config fields — device picker, macro, profile, power action — so all 3 gestures can use any action type without sharing settings.

### :bulb: New LED Effects
- **DeviceSelect** — shows per-device colors based on the currently active Windows output device (up to 3 device→color mappings per knob)
- **ProgramMute** — color1=unmuted, color2=muted for any target process
- **PositionBlend**, **Wheel**, **RainbowWheel** — new per-knob animated effects
- **13 new global-spanning effects** — TheaterChase, RainbowScanner, SparkleRain, BreathingSync, FireWall, DualRacer, Lightning, Fillup, Ocean, Collision, DNA, Rainfall, PoliceLights

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
- :sparkles: **30+ LED effects** — per-knob effects (SingleColor, ColorBlend, PositionFill, GradientFill, PositionBlend, Wheel, RainbowWheel, Blink, Pulse, Breathing, Fire, Comet, Sparkle, PingPong, Stack, Wave, Candle, RainbowWave, RainbowCycle, MicStatus, DeviceMute, ProgramMute, DeviceSelect, AudioReactive)
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

### :gear: System Integration
- :house: **Home Assistant** — connect to your smart home for automation triggers
- :arrow_up: **Auto-update** — checks for new releases on startup with one-click install
- :rocket: **Start with Windows** — launches silently to system tray
- :zap: **Hot-reload config** — changes apply instantly, no restart needed
- :electric_plug: **Auto COM port detection** — friendly dropdown with auto-detect for the Turn Up device

---

## :inbox_tray: Install

### Installer (Recommended)
1. Download `AmpUp-Setup-0.5.2-alpha.exe` from [Releases](https://github.com/audioslayer/ampup/releases)
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
- [ ] OBS WebSocket integration (scene switching, source control)
- [ ] VoiceMeeter strip/bus control
- [ ] Multi-device support
- [ ] FanControl integration

---

## :page_facing_up: License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a>
</p>
