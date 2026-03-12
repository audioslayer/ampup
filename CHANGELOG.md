# Changelog

All notable changes to Amp Up are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.5.2-alpha] — 2026-03-11

### Added
- **DeviceSelect LED effect** — shows per-device colors based on the currently active Windows output device; configure up to 3 device→color mappings per knob in LightsView
- **ProgramMute LED effect** — color1=unmuted, color2=muted for any target process
- **Multi-color palette presets** — 12 preset palettes (Sunset, Ocean, Neon, Galaxy, Vaporwave, Aurora, etc.) with 5 gradient colors mapped across all 15 LEDs
- **3 new profile switch transitions** — Ripple (center-out wave), ColorBurst (white flash + triadic sparks), Wipe (left-to-right gradient sweep); all transitions now derive multi-color patterns from the profile's icon color
- **First-run Welcome Dialog** — 5-step setup guide with numbered color icons and app logo; shows again on version updates via `LastWelcomeVersion` tracking
- **Global exception handler** — catches unhandled exceptions and shows a friendly error dialog with log file path instead of crashing silently

### Changed
- **Complete UI overhaul** — every control rethemed for dark glassmorphism:
  - **Custom ActionPicker** replaces all ComboBox dropdowns in Buttons view — dark popup with colored icons per action, proper hover states
  - **Redesigned GridPicker** replaces pill-style target picker in Mixer — full-width text rows with colored category headers (♪ Audio, ⬡ Devices, ◈ Integrations, ◉ Apps)
  - **Fully themed ComboBox** template in Theme.xaml — dark dropdown popup, rounded items, accent hover (used in Settings and remaining dropdowns)
  - **Ring-style color swatches** — outer ring border with inner color circle, hover glow matches swatch color
  - **Tray right-click menu** completely rebuilt as WPF popup (replaces WinForms ContextMenuStrip) — inline app assignment with expandable knob list, no more submenu cascades
  - **Smart Mix redesign** — Auto-Ducking uses human-readable flow ("When this app is active... lower these apps"), Auto-Profile rules shown as mini cards with accent arrow
  - **OSD glow fix** — separate RadialGradientBrush glow layer eliminates square artifacts on rounded corners; gradient border for subtle edge highlighting
  - **Card panel styling** — gradient border (white highlight fade) replaces DropShadowEffect; corner radius bumped to 10px
- **Effect tile icons all colored** — replaced emoji icons with Unicode symbols that respect Foreground color; dimmed when unselected, bright when selected; global tiles larger (70px)
- **Brightness slider** moved into global settings panel (under Speed) instead of floating in a separate column
- **Auto-suggest layout** moved to opt-in toggle in new Settings → Preferences section (off by default)
- **Profile transitions** use the profile's icon color as the base hue for multi-color derivations (complementary, analogous, triadic)

### Fixed
- **Knob UI updates immediately on hardware turn** — position pushed directly to MixerView via `Dispatcher.BeginInvoke`, bypassing the 50ms poll cycle that caused visible lag
- **Potentiometer jitter suppressed** — ±3 ADC count deadzone in SerialReader prevents noise from triggering spurious volume changes
- **COM port dropdown clipping** — height increased to 38px to prevent text cutoff
- **NAudio COM object caching** — eliminates ~12 COM allocations/sec at idle; cached enumerator, mic, and master device with refresh on exception
- **Thread safety** — added locks for AudioMixer, RgbController, and Config save operations
- **Log rotation** — auto-delete if >1MB at startup; startup separator with version

---

## [0.5.1-alpha] — 2026-03-11

### Added
- **Quick Assign from Tray** — right-click the system tray icon → Assign Running Apps submenu → pick a knob to instantly bind any currently running audio app
- **Auto-Suggest Layout** — amber banner in MixerView when known apps (Discord, Spotify, Chrome, etc.) are detected running but their knobs are unconfigured; suggests a layout with one click
- **Knob Copy / Paste / Reset** — right-click any channel strip for a context menu to copy settings, paste to another strip, or reset to defaults

---

## [0.5.0-alpha] — 2026-03-11

### Added
- **Auto-Ducking (DuckingEngine)** — monitors a trigger app's audio output and fades target app volumes when it's active; configurable trigger, target apps, duck amount, attack/release speed, and enable/disable toggle
- **Auto-Profile Switching (AutoProfileSwitcher)** — monitors the foreground window and automatically switches to a mapped profile; configurable app→profile mappings with debounce
- **Tray Quick Mixer** — left-click the system tray icon to open a compact glassmorphic per-app volume mixer popup; shows all active audio sessions with sliders
- **Profile Export / Import** — save any profile as a standalone `.json` file via file dialog; load it back on any machine

### Changed
- Auto-Ducking and Auto-Profile Switching live in a collapsible **Smart Mix** section in the Mixer tab (moved from Settings in v0.5.x polish pass)

---

## [0.4.1-alpha] — 2026-03-11

### Fixed
- **Double Press and Hold gestures now show all context controls** — previously only showed a path textbox; now all gesture rows expose device picker, macro field, profile picker, power action, and linked knob as appropriate

### Added
- **Per-gesture independent config** — each of the 3 gestures (tap / double / hold) has its own separate config fields; changing one gesture's action no longer stomps another gesture's settings
- **CheckListPicker for cycle device actions** — `cycle_output` and `cycle_input` button actions now show a multi-select checklist to restrict cycling to a chosen subset of devices

---

## [0.4.0-alpha] — 2026-03-10

### Added
- **8 new LED effects:**
  - Per-knob: PingPong (dot bounces across 3 LEDs), Stack (LEDs build up then reset), Wave (sine brightness wave), Candle (organic slow flicker)
  - Global-spanning: Scanner (Cylon sweep), MeteorRain (comet with long tail), ColorWave (scrolling gradient), Segments (rotating barber-pole bands)
- **StyledSlider control** — custom-drawn slider matching the range slider style; used for brightness and all speed sliders
- **Per-effect header colors** — effect name and icon above each LED column now matches the selected effect's unique color
- **Live accent color refresh** — all custom controls (effect pickers, curve pickers, sliders, pickers) update instantly when the theme accent color changes; no restart needed
- **Colorized effect tiles** — every tile in the effect picker has a unique color identity; replaced emoji icons with colorizable unicode symbols

### Changed
- Default window size bumped to 1600×1000 for better effect tile layout (4 tiles per row)

---

## [0.3.2-alpha] — 2026-03-10

### Added
- **Audio-Reactive RGB (AudioAnalyzer)** — WASAPI loopback capture with 1024-sample FFT and Hann windowing; 5 frequency bands (sub-bass → treble) with fast attack / slow decay smoothing; lazy start/stop when any light uses AudioReactive
- **AudioReactive LED effect** — three reactive modes: BeatPulse (bass drives all knobs), SpectrumBands (each knob = its own frequency band), ColorShift (hue shifts with energy)
- **Mute App Group button action** — toggle mute on all apps in a linked knob's app group (`linkedKnobIdx` field)
- **Visual Curve Picker (CurvePickerControl)** — 3 clickable mini canvas graphs showing actual Linear/Log/Exp response curves; replaces the dropdown in MixerView
- **Global Lighting** — one effect config applied to all 5 knobs simultaneously; per-knob settings preserved but hidden in UI
- **6 new LED effects:** Breathing, Fire, Comet, Sparkle, GradientFill, plus AudioReactive
- **Profile Switch Transitions** — None, Flash, Cascade, RainbowSweep animations on profile change (configurable)
- **EffectPickerControl** — categorized grid of colorful icon tiles replacing the effect dropdown (STATIC / ANIMATED / REACTIVE categories)
- **ActionPickerControl** — categorized grid of colorful icon tiles for all 26 button actions (MEDIA / MUTE / APP CONTROL / DEVICE / SYSTEM / POWER)
- **App Group toggles in MixerView** — inline checklist of running apps when knob target is `apps`; click to add/remove from the group
- **Power action tiles** — power actions (sleep, lock, shutdown, restart, hibernate, logoff) rendered as dedicated tiles in ActionPickerControl
- **Custom scrollbars** — slim 8px green scrollbars throughout the app
- **UI polish pass** — spacing, typography, and color consistency across all views

---

## [0.3.1-alpha] — 2026-03-09

### Added
- Renamed project from **WolfMixer** to **AmpUp**
- New app icon design
- GitHub release pipeline (Inno Setup installer, GitHub Actions on `v*` tag push)
- Auto-update checker (reads `AssemblyInformationalVersion` at runtime, compares against latest GitHub release tag, downloads and launches installer)

---

[0.5.2-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.5.2-alpha
[0.5.1-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.5.1-alpha
[0.5.0-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.5.0-alpha
[0.4.1-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.4.1-alpha
[0.4.0-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.4.0-alpha
[0.3.2-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.3.2-alpha
[0.3.1-alpha]: https://github.com/audioslayer/ampup/releases/tag/v0.3.1-alpha
