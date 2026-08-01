# Changelog

All notable changes to Amp Up are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.3.3] - 2026-08-01

### Fixed
- Fixed a Core Audio deadlock introduced by v1.3.2 endpoint-cache invalidation. Windows audio-device callbacks now return immediately and schedule endpoint disposal through the existing debounced refresh instead of disposing native audio objects while Windows holds its notification lock.
- Prevented the tray mixer UI, Turn Up worker, N3 worker, and audio-session refresh from becoming blocked behind the same Core Audio lock after a device-state notification.

---

## [1.3.2] - 2026-08-01

### Fixed
- Reworked issue #23 recovery so a validated Turn Up COM handle is retained, the last working port gets a fast reconnect attempt, and transient CH343 failures no longer incur the old multi-second retry delay.
- Refreshed cached DDC/CI monitor handles after display power/topology changes, checked native brightness-write failures, and retried per-monitor writes once with fresh handles.
- Stopped absent N3 hardware from being probed and logged every five seconds: Auto mode now retries quietly at low frequency and USB arrival triggers immediate discovery.
- Added live blocked-input-handler diagnostics and default-audio-device change logging for future hardware and Bluetooth troubleshooting.

---

## [1.3.1] - 2026-07-25

### Fixed
- Fixed issue #24: Turn Up and N3 buttons now retain independent output-device assignments across buttons, profiles, and N3 tap/double/hold gestures.

---

## [1.3] - 2026-07-20

### Added
- Added a one-click in-app updater that downloads the matching GitHub release installer, verifies its size and SHA-256 digest, installs it after a normal Windows elevation prompt, and relaunches Amp Up.
- Added live Windows audio-endpoint notifications so newly connected, removed, changed, and default Bluetooth or USB devices refresh without restarting Amp Up.

### Changed
- The tray update banner now starts the in-app update flow instead of opening the GitHub releases page.
- Turn Up and N3 events now use independent ordered input workers so slow actions cannot block hardware reads; high-frequency absolute knob positions are coalesced to the newest value.
- Audio device cycling now uses a fresh Windows device enumerator, while device-dependent views refresh through a short debounce instead of rebuilding unrelated UI.
- Runtime logging now rotates at 1 MB and rate-limits repeated missing-device and refresh failures.
- Tightened resource ownership across Windows audio sessions, tray rows, HTTP integrations, Govee UDP writes, process handles, timers, and animated UI elements.

### Fixed
- Fixed issue #22: Bluetooth and other audio devices connected after startup now remain available to output cycling, selection lists, and device-color lighting.
- Fixed issue #23: prevented stalled controls, multi-second hardware-input latency, and stale knob-event backlogs during slow actions or refresh work.
- Added Turn Up serial read-stall detection and reconnect handling, stopped unnecessary COM scanning in Stream Controller-only mode, and made RGB refresh non-reentrant.
- Preserved temporarily unavailable selected audio devices and disconnected-device color mappings instead of silently discarding them.
- Stopped invalid Spotify refresh credentials from retrying continuously and cleared the stale saved session after the first failed restore.
- Made audio-session refresh non-reentrant and cleaned up stale session wrappers after errors.
- Hardened configuration and profile writes so the last known-good file remains available if replacement fails.

---

## [1.1] - 2026-06-18

### Added
- Added Discord RPC authorization flow, token refresh handling, and persistent Discord auth across profile switches. Discord actions are currently tester-gated: users must DM audio on Discord so they can be added as a friend/tester until official Discord support is available.
- Added direct Discord RPC actions for mute, deafen, explicit voice states, leave voice, and noise suppression.
- Added settings backup and restore.
- Added N3 hardware monitoring tiles for CPU, GPU, RAM, VRAM, temperatures, usage, and fan metrics.
- Added gauge display options for N3 hardware metrics, including max overrides, color-by-value, label overrides, and dimmed tracks.
- Added smooth N3 title auto-scrolling and wraparound behavior for long labels.
- Added state-specific App Status lighting effects for unmuted, muted, not-running, and activity-flash states.
- Added automatic effect colors and 15 room-bright ambient LED effects.

### Changed
- Renamed Global Span lighting to Scenes.
- Replaced the state-effect dropdown with a compact effect chip grid.
- Reworked and polished N3 hardware metric editor layout and options.
- Improved hot-path performance across engines, UI, and I/O.

### Fixed
- Fixed Discord RPC redirect and PKCE verifier handling for tester authorization.
- Fixed App Status sub-effects ignoring the knob dead zone.
- Fixed N3 editor bugs around title scrolling, maximize button bubbling, and clearing icon styling.
- Improved hardware monitoring with VRAM fixes, HWiNFO fallbacks, and fan picker refinements.

---

## [1.0.9.5] - 2026-06-09

### Added
- App Status lighting can now use separate effects for unmuted, muted, and not-running app states, including an explicit Off option.

---

## [1.0.9.3] - 2026-06-05

### Added
- Added direct Discord RPC button actions for toggle mute, toggle deafen, explicit mute/deafen on/off, leave voice, and noise suppression.

### Changed
- Hid Discord RPC credential fields from normal Settings UI for public builds; credentials remain local/env-only for test configurations.

---

## [1.0.9.2] - 2026-06-04

### Added
- Added SignalRGB bridge support for driving Turn Up LEDs through a bundled local SignalRGB plugin.
- Added SignalRGB button/N3 actions for applying effects, cycling effects, blackout, restore, and profile-to-effect/layout sync.
- Added Room Temperature mode with a Kelvin slider and expanded warm-to-cool preset pills.

### Changed
- Tuned warm room-temperature colors to better match Govee-style amber/candle lighting.
- Expanded N3 integration actions and effect/template helpers.

### Fixed
- Improved Govee RGBIC segment handling for wall lights, rope lights, and Flow Plus light bars in Temperature mode.
- Fixed Home Assistant light color actions for stream-controller workflows.

---

## [1.0.4-beta] - 2026-05-17

### Fixed
- Added a Restore Removed action for Govee devices so users can clear the hidden-device list and rescan lights they previously removed.
- Clarified Govee scan status when discovered devices are filtered because they were removed, instead of reporting that as a LAN scan failure.

---

## [1.0.3-beta] - 2026-05-15

### Changed
- Discontinued and removed the experimental macOS alpha due to lack of user interest and the maintenance cost of keeping a separate Core Audio/Avalonia port healthy.
- Updated public documentation to present Amp Up as a Windows 10/11 app.

### Fixed
- Hardened Govee startup power handling so stale saved state does not wake lights unexpectedly.
- Added Turn Up serial stall recovery when the USB serial stream stops delivering data.
- Decoupled Turn Up and N3 hardware input handling from low-level read loops so slow actions cannot block device reads.
- Added sampled hardware-input logging and N3 reconnect attempts after disconnected states.

---

## [1.0.2-beta] - 2026-05-12

### Added
- Added **Dev+Pos** / `DevicePositionFill` lighting mode, combining active default output-device colors with Turn Up knob position fill.

### Fixed
- Fixed position-fill lighting briefly showing 100% on startup when a saved knob position is zero.

---

## [1.0.1-beta] - 2026-05-10

### Changed
- Improved App Group discoverability in the Mixer target picker by showing overflow scrolling and moving App Group to the top of the Apps section.
- Added app-group controls to the Mixer Audio Sessions list so running apps can be added to or removed from knob app groups directly from the session rows.

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
- **Release packaging** — manual installer packaging, updater prerelease handling, and safer beta distribution defaults.

### ✨ Changed
- **Hardware Mode now defaults to Turn Up** for new users, while still exposing Stream Controller and Both modes when users connect an N3.
- **Active Surface controls the UI more clearly** so users with only a Turn Up mixer see Turn Up-first pages, and N3 controls appear when the stream controller is selected.
- **Start with Windows stays enabled by default** for new installs, with a Settings toggle for users who prefer to launch Amp Up manually.
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
- The Windows installer is the supported 1.0 beta artifact.

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

[1.3.3]: https://github.com/audioslayer/ampup/releases/tag/v1.3.3
[1.3.2]: https://github.com/audioslayer/ampup/releases/tag/v1.3.2
[1.3.1]: https://github.com/audioslayer/ampup/releases/tag/v1.3.1
[1.3]: https://github.com/audioslayer/ampup/releases/tag/v1.3
[1.0.3-beta]: https://github.com/audioslayer/ampup/releases/tag/v1.0.3-beta
[1.0.2-beta]: https://github.com/audioslayer/ampup/releases/tag/v1.0.2-beta
[1.0.1-beta]: https://github.com/audioslayer/ampup/releases/tag/v1.0.1-beta
[1.0.0-beta]: https://github.com/audioslayer/ampup/releases/tag/v1.0.0-beta
