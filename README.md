<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>A powerful replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, macro buttons, and a sleek dark UI.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.4.0--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## :sparkles: What's New in v0.4.0-alpha

### :rainbow: 8 New LED Effects
The biggest lighting update yet — 4 new per-knob effects that use all 3 LEDs independently, plus 4 global-spanning effects that treat all 15 LEDs as one continuous light strip.

**Per-Knob 3-LED Effects:**
- :left_right_arrow: **PingPong** — bright dot bounces back and forth across 3 LEDs
- :bar_chart: **Stack** — LEDs build up one by one, then reset
- :ocean: **Wave** — sine wave of brightness travels across 3 LEDs
- :candle: **Candle** — smooth organic flickering (slower/calmer than Fire)

**Global-Spanning Effects (all 15 LEDs as one strip):**
- :red_circle: **Scanner** — Cylon/KITT scanner sweeps back and forth across all 15 LEDs
- :comet: **Meteor Rain** — bright comet with long fading tail across all 15 LEDs
- :purple_heart: **Color Wave** — scrolling color1→color2 gradient across all 15 LEDs
- :orange_circle: **Segments** — rotating barber-pole bands of color1/color2 across all 15 LEDs

### :art: Colorized Effect Tiles
Every effect tile in the picker now has its own unique color identity — Fire is orange, Sparkle is yellow, Pulse is pink, etc. Replaced emoji icons with colorizable unicode symbols so all tiles light up in their signature color across normal, hover, and selected states.

### :control_knobs: Styled Sliders
New custom-drawn `StyledSlider` control matching the Mixer tab's range slider visual style. Used for brightness and all speed sliders — accent-colored track fill, round thumb with white center dot, and value label underneath.

### :art: Per-Effect Header Colors
The effect name and icon above each LED column now matches the selected effect's unique color instead of the global theme accent.

### :level_slider: Live Accent Color Refresh
All custom controls now update instantly when you change the theme accent color — no more restarting the app. Covers effect pickers, curve pickers, range sliders, target pickers, device pickers, and all section headers.

### :desktop_computer: Wider Layout
Default window size bumped to 1600×1000 for better effect tile layout — 4 tiles per row in every column, with room for colors and speed controls below.

---

## :zap: Why Amp Up?

Amp Up is a community-built alternative for the Turn Up USB mixer with a modern dark UI inspired by SteelSeries Sonar and Elgato Wave Link. It extends the hardware with Home Assistant integration, response curves, volume range clamping, and more features coming soon.

---

## :joystick: Features

### :musical_keyboard: Mixer — 5 Channel Strips
- :sound: **Per-app volume control** — assign any knob to a specific app, master volume, mic input, or the active window
- :link: **App groups** — control multiple apps with a single knob
- :chart_with_upwards_trend: **Response curves** — Linear, Logarithmic, or Exponential per channel
- :straight_ruler: **Volume range clamping** — restrict a knob to sweep only a portion of the volume range (e.g. 40–100%)
- :bar_chart: **Live VU meters** — real-time audio level visualization with peak hold
- :bulb: **Monitor brightness** — control physical monitor brightness via DDC/CI

### :video_game: Buttons — 15 Programmable Actions
- :point_up_2: **3 gestures per button** — single press, double press, and hold
- :hammer_and_wrench: **18 action types:**
  - Media controls (play/pause, next, previous)
  - Mute toggles (master, mic, per-app, active window)
  - App launcher / process killer
  - Audio device switching (cycle or direct select)
  - Keyboard macros (any key combo)
  - System power (sleep, lock, shutdown, restart, hibernate, logoff)
  - Profile switching
  - LED brightness cycling

### :rainbow: Lights — Full RGB Control
- :sparkles: **19 LED effects** — solid, blend, fill, gradient, blink, pulse, breathing, fire, comet, sparkle, ping pong, stack, wave, candle, rainbow wave, rainbow cycle, mic status, mute status, audio reactive
- :globe_with_meridians: **4 global-spanning effects** — scanner, meteor rain, color wave, segments (all 15 LEDs as one strip)
- :art: **Dual color support** — set primary and secondary colors per effect
- :fast_forward: **Speed control** — adjustable animation speed per knob
- :high_brightness: **Global brightness** — master brightness slider for all LEDs

### :busts_in_silhouette: Profiles
- :floppy_disk: **Save and load** full configurations (knobs, buttons, lights)
- :arrows_counterclockwise: **Switch profiles** via button press or sidebar profile picker
- :framed_picture: **Colored Fluent icons** — 40+ icons with 10 color presets per profile
- :video_game: **Per-game / per-workflow** setups

### :tv: OSD Overlay
- :window: **Glassmorphism notifications** — sleek dark glass overlay with green glow accent
- :loud_sound: **Volume changes** — shows knob label, percentage, and animated fill bar
- :busts_in_silhouette: **Profile switches** — colored Fluent icon + profile name
- :speaker: **Device switches** — output/input device name with icon
- :round_pushpin: **Configurable position** — 6 screen positions (Fraps-style picker in Settings)
- :control_knobs: **Per-type toggles** — enable/disable OSD for volume, profiles, and devices independently

### :gear: System Integration
- :house: **Home Assistant** — connect to your smart home for automation triggers
- :arrow_up: **Auto-update** — checks for new releases on startup with one-click install
- :rocket: **Start with Windows** — launches silently to system tray
- :package: **System tray app** — glassmorphism context menu with accent glow
- :zap: **Hot-reload config** — changes apply instantly, no restart needed
- :electric_plug: **Auto COM port detection** — scans all serial ports to find the Turn Up device

---

## :inbox_tray: Install

### Installer (Recommended)
1. Download `AmpUp-Setup-0.4.0-alpha.exe` from [Releases](https://github.com/audioslayer/ampup/releases)
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
| :musical_keyboard: **Mixer** | Knob targets, response curves, volume range, app groups |
| :video_game: **Buttons** | Actions for press / double-press / hold per button |
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
| LED Brightness | Control RGB LED brightness with a knob |

---

## :electric_plug: Hardware

Amp Up is designed for the **Turn Up** USB volume mixer (CH343 USB-to-serial). The device has:
- :control_knobs: 5 rotary knobs (10-bit resolution, 0–1023)
- :radio_button: 5 push buttons (supporting press, double-press, and hold)
- :bulb: 15 RGB LEDs (3 per knob)

---

## :hammer_and_wrench: Tech Stack

- **C# / .NET 8** — WPF with WPF-UI (Fluent design + Mica backdrop)
- **NAudio** — WASAPI per-app audio control
- **System.IO.Ports** — serial communication with Turn Up hardware
- **Custom controls** — animated arc knob, 16-segment VU meter, styled sliders
- **Code-behind architecture** — lightweight, no MVVM overhead

---

## :clipboard: Changelog

### v0.4.0-alpha — LED Effects & UI Polish
:rainbow: 8 new LED effects (4 per-knob + 4 global-spanning across all 15 LEDs)
:art: Colorized effect tiles with unique per-effect colors
:control_knobs: Custom StyledSlider control for brightness and speed
:level_slider: Live accent color refresh across all views
:desktop_computer: Wider 1600×1000 default layout
:broom: Removed unused files and dead code for production cleanup

### v0.3.2-alpha — OSD, Profiles & Polish
:tv: Glassmorphism OSD overlay for volume, profiles, and device switching
:busts_in_silhouette: Profile system with colored Fluent icons
:electric_plug: Auto COM port detection
:arrow_up: Auto-update checker
:house: Home Assistant integration
:art: Dynamic accent theming across all views

### v0.2-alpha — Buttons & Lights
:video_game: 18 button action types with 3 gestures per button
:rainbow: 9 LED effects with dual color and speed control
:chart_with_upwards_trend: Response curves and volume range clamping

### v0.1-alpha — Initial Release
:musical_keyboard: 5-channel mixer with per-app volume control
:bar_chart: Live VU meters with peak hold
:floppy_disk: JSON config with auto-save

---

## :world_map: Roadmap

- [x] OSD overlay for volume, profiles, and device switching
- [x] Auto COM port detection
- [x] Auto-update checker
- [x] Global-spanning LED effects (15 LEDs as one strip)
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
