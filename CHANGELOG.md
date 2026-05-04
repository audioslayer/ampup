# Changelog

All notable changes to Amp Up are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.0.0-beta] — 2026-05-03

This is the big one: Amp Up graduates into a 1.0 beta for Windows with a polished Turn Up mixer experience, native beta support for the TreasLin / VSDinside N3 stream controller, and a serious pass on performance, reliability, lighting, release packaging, and everyday feel.

### 🚀 Added
- **TreasLin / VSDinside N3 stream controller support** — native HID support for the N3 surface, including 6 LCD keys, 3 tap buttons, and 3 rotary buttons with press support.
- **N3 visual designer** — LCD key previews, title/icon/glow styling, pages, Spaces, side buttons, encoder actions, page navigation, and device sleep/wake controls.
- **Stream-controller actions** — page next/previous/home, jump to page, open Space/folder, page cycle, Space cycle, and encoder-friendly action routing.
- **Room lighting control center** — Room Effect, Layout, and Devices tabs with Govee, Corsair iCUE, Turn Up, Music Reactive, VU Fill, Screen Sync, and Game Mode workflows.
- **Corsair iCUE room sync** — room effects can drive Corsair devices alongside Govee and Turn Up lighting.
- **DreamView-style screen sync** — spatial screen color capture with device placement and per-device sync behavior.
- **Unified tray mixer popup** — app volume sliders, live activity bars, output/input switching, app assignment, update banner, and DPI-aware taskbar positioning.
- **Quick Wheel and OSD polish** — profile/device radial switching, configurable OSD monitor/position/durations, and cleaner profile/device notifications.
- **Release automation** — GitHub release workflow, installer packaging, updater prerelease handling, and safer beta distribution defaults.

### ✨ Changed
- **Hardware Mode now defaults to Turn Up** for new users, while still exposing Stream Controller and Both modes when users connect an N3.
- **Active Surface controls the UI more clearly** so users with only a Turn Up mixer see Turn Up-first pages, and N3 controls appear when the stream controller is selected.
- **Start with Windows is opt-in for new installs** instead of silently enabling itself.
- **Room actions respect per-device sync toggles** so all-white and room effects do not hit Govee devices that users turned off in the Devices tab.
- **Lighting effects are more complete** with 60+ effects, animated previews, premium palettes, gradient editing, gamma calibration, and hardware hover preview.
- **README and release docs now present N3 as a supported beta device** with a clear setup story and hardware table.

### 🧠 Optimized
- **Reduced idle CPU use** by trimming unnecessary timers, polling, redraws, and audio-session refreshes while the app is idle or minimized.
- **Lowered memory churn** by cleaning up view handlers, audio resources, OSD subscriptions, N3 display work, and device/session references.
- **Improved tray behavior** with smarter popup lifetime management, DPI conversion, and taskbar-edge detection.
- **Improved Govee throughput** with global rate limiting and safer multi-session LAN sync.
- **Improved screen/audio work scheduling** so expensive loops run only when the related feature is active.

### 🛠️ Fixed
- **Program picker no longer crashes** after assigning an app to a button, and assigned program names display correctly.
- **Room tab effects now control Corsair iCUE lights again** when Aura or room effects are selected.
- **All-white lighting no longer affects disabled Govee devices** when their sync toggle is off.
- **Tray popup positioning no longer opens off-screen** on shifted, scaled, or edge-mounted taskbars.
- **Taskbar icon now appears correctly** instead of falling back to the generic WPF icon.
- **Audio source changes are more resilient** with safer audio-device/session refresh and COM cleanup.
- **N3 disconnects are surfaced instead of silently failing**.
- **Corsair callbacks, Govee sync, AudioMixer disposal, OSD subscriptions, and shutdown flow** now fail more gracefully.
- **Bundled font and app manifest issues were cleaned up** for release packaging.

### ⚠️ Beta Notes
- N3 support is new and marked beta while more real-world hardware setups are tested.
- The Windows installer is the primary 1.0 beta artifact. The macOS port remains alpha while the shared core continues to mature.

---

## Earlier Alpha Highlights

### 🎨 v0.9.8
- Animated effect tile previews, Phosphor icon polish, card layout cleanup, and visual hierarchy improvements.

### 🏠 v0.9.7
- Room Effect redesign, Favorites, premium palettes, VU Fill modes, Music Reactive sensitivity, and lighting fixes.

### 🔥 v0.9.6
- Unified Room tab, Corsair controls, Govee menus, global groups, settings footer, and OSD fixes.

### ⚡ v0.9.x
- Tray mixer, audio sessions in Mixer, profile overview, Quick Wheel, DreamView screen sync, and smart automations.

### 🌱 v0.5.x
- Auto-ducking, auto-profile switching, tray quick mixer, app groups, profile import/export, and first major UI polish.

### 💡 v0.4.x
- Audio-reactive RGB, global lighting, response curves, custom sliders, and effect/action picker redesigns.

### 🐣 v0.3.x
- Project renamed from WolfMixer to Amp Up, app icon added, GitHub releases started, and the updater landed.

---

[1.0.0-beta]: https://github.com/audioslayer/ampup/releases/tag/v1.0.0-beta
