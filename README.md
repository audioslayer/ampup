<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>A powerful replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, macro buttons, and a sleek dark UI.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.7.0--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## :sparkles: What's New in v0.7.0 — Polish & Ease of Use

### :dart: Smarter Menus
All dropdown pickers (knob targets, button actions) now use **inline sub-panels** instead of separate popup windows. No more menus closing unexpectedly when your mouse moves between them. Sub-panels show a context header and highlight the active parent item.

### :house: Home Assistant & Govee Integration
- **Home Assistant** targets display as "Home Assistant" with a clean domain subtitle (Light, Media Player, Fan, Cover)
- **Govee** now uses a sub-flyout for device selection — pick from your configured devices instead of seeing one entry per light
- Scene selection **auto-checks the power toggle**; turning off clears the active scene

### :jigsaw: App Group Chips
Replaced the old checkbox list with **clickable pill-shaped tags**. Selected apps show as accent-colored chips, available apps as subtle dark pills. Click to toggle. In-group apps sort first.

### :control_knobs: Smart Mix Redesign
Complete UX overhaul of the Smart Mix section:
- **Voice Ducking** — pick your trigger app from a dropdown instead of typing a process name. Target apps field removed (ducks everything by default). Fade timing hidden behind a collapsible "Advanced" toggle
- **App Profiles** — dynamic add/remove rules replace the old 5 fixed empty slots. Each rule uses app and profile dropdowns populated from running apps

### :tv: OSD Overhaul
- **Horizontal 5-column layout** for profile switches — mirrors the physical Turn Up device (knob targets on top, button actions below)
- **Configurable display duration** per notification type (volume, profile, device) with themed sliders in Settings
- Stronger accent border glow for consistent visual impact

### :movie_camera: DreamView Improvements
- **Gamma-correct color capture** — colors are linearized before averaging, producing vibrant and accurate results instead of washed-out muddy tones
- **Dark pixel filtering** — taskbars and dark UI elements no longer drag zone colors toward black
- **Real monitor names** — dropdown shows "LG ULTRAGEAR+" instead of "DISPLAY1" (via Windows DisplayConfig API)
- **Conflict prevention** — device scene/color controls dim when DreamView is active, with a clear info banner

### :wrench: Quality of Life
- `StyledSlider` gains a `ShowLabel` toggle — hide the thumb label when the value is already shown elsewhere
- Wider combo boxes throughout DreamView so text doesn't clip
- Zone preview swatches enlarged with subtle borders

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
- :round_pushpin: **Configurable position** — 6 screen positions (Fraps-style picker in Settings)
- :stopwatch: **Per-type duration** — set how long each OSD type stays visible (1–8 seconds)
- :control_knobs: **Per-type toggles** — enable/disable OSD for volume, profiles, and devices independently

### :package: Tray Integration
- :speaker: **Quick Mixer** — left-click tray icon for compact per-app volume popup
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

### Installer (Recommended)
1. Download `AmpUp-Setup-0.7.0-alpha.exe` from [Releases](https://github.com/audioslayer/ampup/releases)
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
- [x] DreamView screen sync (gamma-correct zone capture to Govee lights)
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
