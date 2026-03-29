<p align="center">
  <img src="Assets/icon/ampup-256.png" width="120" alt="Amp Up Logo" />
</p>

<h1 align="center">Amp Up</h1>

<p align="center">
  <strong>The ultimate replacement app for the Turn Up USB volume mixer.</strong><br/>
  Per-app audio control, RGB lighting, smart automation, and a gorgeous dark UI.<br/>
  <em>Now on Windows and macOS.</em>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.9.6--alpha-blue" alt="Version" />
  <img src="https://img.shields.io/badge/Windows%2010%2F11-0078D6?logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/macOS%2014.2+-000000?logo=apple&logoColor=white" alt="macOS" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## 🎯 What is Amp Up?

Amp Up is a community-built, open-source replacement for the official Turn Up software. It takes the 5-knob USB mixer hardware and supercharges it with features the original app never had — per-app audio routing, 30+ LED effects, Home Assistant integration, Govee room lighting sync, macro buttons, response curves, and a sleek modern UI inspired by SteelSeries Sonar and Elgato Wave Link.

**Works on Windows and macOS.** Same hardware, same features, both platforms.

---

## 📥 Install

### 🪟 Windows
1. Download **`AmpUp-Setup-0.9.6-alpha.exe`** from [Releases](https://github.com/audioslayer/ampup/releases)
2. Run the installer (auto-installs .NET 8 if needed — one-time)
3. Amp Up appears in your system tray ✨

### 🍎 macOS
1. Download **`AmpUp-0.9.6-alpha.dmg`** from [Releases](https://github.com/audioslayer/ampup/releases)
2. Open the DMG → drag to Applications
3. Launch and grant audio permission on first run 🎤

> **Note:** Kill the official Turn Up app first — it holds the COM/serial port.

---

## ✨ Features

### 🎛️ Mixer — 5 Channel Strips
- **Per-app volume control** — assign any knob to Spotify, Discord, Chrome, or any running app
- **Master volume, mic input, monitor brightness** — all assignable to knobs
- **App groups** — control multiple apps with a single knob
- **Response curves** — Linear, Logarithmic, Exponential, or Exponential² per channel
- **Volume range clamping** — restrict a knob to sweep only part of the range (e.g. 40–100%)
- **Live VU meters** — real-time 16-segment audio level visualization
- **Fuzzy matching** — "Apple Music" matches the `AppleMusic` process automatically

### 🎮 Buttons — 15 Programmable Actions
- **3 gestures per button** — tap, double-press, and hold (each independent)
- **26 action types** including:
  - 🎵 Media controls (play/pause, next, previous)
  - 🔇 Mute toggles (master, mic, per-app, active window, app group, output device)
  - 🚀 App launcher / process killer
  - 🔊 Audio device switching (cycle, direct select, device subsets)
  - ⌨️ Keyboard macros (any key combo)
  - 💤 System power (sleep, lock, shutdown, restart, hibernate)
  - 👤 Profile switching
  - 💡 LED brightness cycling

### 🌈 Lights — Full RGB Control
- **30+ per-knob effects** — SingleColor, ColorBlend, Breathing, Fire, Comet, Sparkle, PingPong, Wave, RainbowCycle, AudioReactive, MicStatus, DeviceMute, and more
- **17 global-spanning effects** — all 15 LEDs as one continuous strip (Scanner, MeteorRain, FireWall, DNA, PoliceLights, Lightning, Ocean...)
- **12 built-in presets** — Cyberpunk, Stealth, Party, Aurora, etc.
- **Dual color support** — primary + secondary colors per effect
- **Speed & brightness control** — per-knob speed slider + global brightness
- **LED calibration** — per-channel R/G/B gamma sliders with live hardware preview
- **Copy/paste** — right-click any LED column to clone settings

### 🎡 Quick Wheel — Radial Switcher
- **Hold a button** → radial pie-menu appears on screen
- **Spin any knob** or hover with mouse to navigate
- **Release to confirm** — switches profile or output device instantly
- **Two modes:** Profile (8 slots) or Output Device

### 📊 Smart Mix — Automation
- **Auto-Ducking** — automatically lower music when Discord/Zoom activates
- **Auto-Profile Switching** — switch profiles when specific apps are focused
- **Auto-Suggest Layout** — detects running apps and suggests knob assignments

### 🏠 Smart Home & Room Lighting
- **Home Assistant** — control lights, media players, fans, covers via button presses or knob turns
- **Govee LAN sync** — mirror knob RGB colors to Govee room lights at 20 FPS
- **Govee Cloud dashboard** — control on/off, brightness, color, scenes
- **Corsair iCUE sync** — drive all iCUE RGB devices with room effects, fan/pump speed control
- **Room tab** — unified Global / Govee / Corsair tabs with pill-style navigation
- **Mini card toggles** — Amp Up Sync, Music Reactive, Screen Sync as dynamic per-tab cards
- **Music Reactive** — system audio modulates room effect brightness (Global) or drives bass/mid/treble colors (Govee LAN / Corsair)
- **DreamView screen sync** — capture screen zones and map colors to Govee + Corsair in real-time
- **Device Groups** — group Govee, Corsair, HA, and audio devices together for unified toggle/brightness control via knob or button

### 🎬 Streaming & Production
- **OBS Studio** — WebSocket v5: scene switching, source gain/mute, streaming/recording toggle
- **VoiceMeeter** — strip/bus gain control and mute toggle

### 📺 OSD Overlay
- **Glassmorphism notifications** — volume changes, profile switches, device changes
- **Quick Wheel** — full-screen radial overlay
- **6 screen positions** — configurable per your setup
- **Per-type toggles & durations** — show/hide each OSD type independently

### 🔊 System Tray — Quick Mixer
- **Left-click** → compact per-app volume popup with live audio peak bars
- **Scroll wheel on tray icon** → adjust master volume without opening anything
- **Middle-click** → instant master mute toggle
- **Search bar** — filter apps by name when the list gets long
- **📌 Pin to top** — keep your most-used apps at the top
- **Right-click any app** → assign to knob, move to device, hide from mixer
- **⚡ Quick Assign** — visual app grid with inline knob picker
- **Device dropdowns** — switch output/input devices from a proper list
- **Tray icon** — shows current volume % (or "M" when muted)

### 🎛️ Hardware Preview
- **Always-visible status bar** at the bottom of the main window
- Live LED colors, knob positions, audio levels for all 5 channels
- Connection status at a glance

### 📊 Audio Dashboard
- **Activity tab** — see every app producing audio right now
- Peak levels, volume %, knob assignments, mute status
- Quick-assign unassigned apps to knobs with one click

### 👤 Profiles
- **Save & load** full configurations (knobs, buttons, lights)
- **Switch via button press**, sidebar picker, Quick Wheel, or tray
- **Export / Import** — share profiles as standalone JSON files
- **40+ icons** with 10 color presets per profile
- **Per-game / per-workflow** setups with auto-switching

### ⚙️ Quality of Life
- **Auto-update** — checks GitHub for new releases, one-click install
- **Start with Windows** — launches silently to system tray
- **Hot-reload config** — changes apply instantly, no restart needed
- **Auto COM port detection** — friendly dropdown with auto-detect
- **Framework-dependent builds** — ~5-8MB updates (not 55MB)
- **Turn Up config import** — migrate your existing setup in one click

---

## 🔌 Hardware

Amp Up is designed for the **Turn Up** USB volume mixer:

| Component | Spec |
|-|-|
| 🎛️ Knobs | 5 rotary encoders (10-bit, 0–1023) |
| 🔘 Buttons | 5 push buttons (tap, double, hold) |
| 💡 LEDs | 15 RGB LEDs (3 per knob) |
| 🔗 USB | CH343 USB-to-serial (115200 baud) |

---

## 🛠️ Tech Stack

| | Windows | macOS |
|-|-|-|
| **UI** | WPF + WPF-UI (Fluent/Mica) | Avalonia 11.2 |
| **Audio** | NAudio (WASAPI) | CoreAudio Process Tap API |
| **Framework** | .NET 8 | .NET 8 + Swift bridge |
| **Serial** | System.IO.Ports | System.IO.Ports |
| **Installer** | Inno Setup | DMG (drag to Applications) |

**Architecture:** `AmpUp.Core` shared library (models, protocol, engine, services) + platform-specific UI.

---

## 🏗️ Build from Source

```bash
# Windows
git clone https://github.com/audioslayer/ampup.git
cd ampup
dotnet build

# macOS (Apple Silicon, requires .NET 8 SDK + Swift 5.9+)
cd AmpUp.Mac
./build.sh    # dev build
./bundle.sh   # full .app + .dmg
```

---

## 🗺️ Roadmap

- [x] Per-app audio control (Windows + macOS)
- [x] 30+ LED effects + 17 global-spanning effects
- [x] OBS Studio + VoiceMeeter integration
- [x] Home Assistant + Govee smart home control
- [x] DreamView screen-to-light sync
- [x] Quick Wheel radial switcher
- [x] Auto-Ducking + Auto-Profile Switching
- [x] EarTrumpet-inspired tray mixer
- [x] macOS Avalonia port (feature-complete)
- [x] Framework-dependent builds (~5MB updates)
- [x] Corsair iCUE RGB sync + fan/pump control
- [x] Device Groups (Govee + Corsair + HA + Audio)
- [x] Unified Room tab with per-device tabs
- [ ] Multi-device support (multiple Turn Up units)
- [ ] Mobile companion app
- [ ] GitHub Actions CI (auto-build both platforms)
- [ ] Razer Chroma RGB sync
- [ ] Advanced macro system

---

## 📋 Changelog Highlights

| Version | Highlights |
|-|-|
| **v0.9.6** 🔥 | **Room tab redesign** — unified room card, pill tabs, dynamic per-tab toggle cards (Amp Up / Music / Screen Sync), Corsair color pickers + presets, Govee flyout sub-menus, music reactive keeps effect playing, Groups global across profiles, device Groups brightness control, Settings footer with Buy Me a Coffee, phantom OSD fix, LED toggle mini cards, AmbienceView → RoomView rename |
| **v0.9.3** | LED effects overhaul (6 new, 5 improved), OSD curve-applied volume, color presets, hardware hover preview |
| **v0.9.0** | Major UI overhaul, audio sessions in mixer, tray redesign, per-knob LED preview |
| **v0.8.5** | Mac port complete, tray overhaul, user bug fixes, framework-dependent builds |
| **v0.8.1** | Quick Wheel radial switcher, monitor brightness fix |
| **v0.8.0** | Hardware widget, profile editor, AmpUp.Core extraction |
| **v0.7.0** | Inline pickers, Smart Mix, DreamView, Profile Overview |
| **v0.6.0** | Ambience tab (Govee LAN + Cloud), setup wizard |
| **v0.5.0** | Auto-Ducking, auto-profile switching, tray quick mixer |
| **v0.4.0** | Audio-reactive RGB, global lighting, dynamic theming |
| **v0.3.1** | Renamed WolfMixer → AmpUp, auto-updater, GitHub releases |

---

## 📄 License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a> 🐺<br/>
  <a href="https://www.buymeacoffee.com/audioslayer">☕ Buy me a coffee</a>
</p>
