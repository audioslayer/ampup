# AmpUp — CODEX Notes

## Current hardware support
- Primary supported hardware is the original Turn Up USB mixer (CH343 serial, 5 knobs, 5 buttons, 15 RGB LEDs)
- Stream Controller family (TreasLin / VSDinside N3) is a first-class supported device with full native HID control — no dependency on VSD Craft, OpenDeck, or any helper software

## Stream Controller family (TreasLin / VSDinside N3-class)
- Exact listing: `https://www.amazon.com/TreasLin-Controller-Customizable-Creators-Compatible/dp/B0FM3NP9ZB`
- Surface: 6 LCD keys, 3 physical buttons, 3 knobs with rotate left/right + press
- Product path we used for reference: `4ndv/opendeck-akp03`, `bitfocus/companion-surface-mirabox-stream-dock` issue #21, and the `mirajazz` Rust driver (the most reliable clue source)
- Confirmed VID/PID on our hardware: `0x5548` / `0x1001`

## Confirmed on our hardware
- Device enumerates as `VID 5548 / PID 1001`
- Product string on this unit: `HOTSPOTEKUSB HID DEMO`
- Primary HID interface is the vendor-defined `MI_00` path with large reports:
  - input report length `513`
  - output report length `1025`
  - usage page `0xFFA0` / `65440`
  - usage `0x0001`
- The device can run alongside the original Turn Up at the same time
- Direct native HID support is active in AmpUp — see `AmpUp.Core/Services/N3Controller.cs`

## Confirmed input protocol
- Input packets are ACK-prefixed on this unit and parse correctly from `MI_00`
- Minimal init:
  - `CRT DIS`
  - `CRT LIG`
- Confirmed control map on our hardware:
  - LCD keys: `0x01` through `0x06`
  - side buttons: `0x25`, `0x30`, `0x31`
  - encoder presses: `0x33`, `0x35`, `0x34`
  - left encoder turn: `0x90` / `0x91`
  - middle encoder turn: `0x50` / `0x51`
  - right encoder turn: `0x60` / `0x61`

## Confirmed display protocol
- LCD image writes are feasible directly over HID
- Working display path:
  - `CRT BAT` header
  - image byte length in big-endian
  - target key index as `key + 1`
  - raw JPEG data streamed in HID output reports
  - `CRT STP` commit
- Working image format on our tool path:
  - `60x60`
  - JPEG
  - rotated `90` degrees
- Clear path:
  - `CRT CLE`
  - `CRT STP`

## Confirmed sleep protocol
- `CRT HAN` puts the device into firmware standby (real LCD power-down, not a brightness=0 dim)
- Wake is the standard init sequence: `CRT DIS` + `CRT LIG`
- We drive both sides from `App.OnStreamControllerRefreshTick` using `NativeMethods.GetIdleMilliseconds()` against the user's configured idle threshold, plus a `SystemEvents.PowerModeChanged` hook for system suspend/resume

## Naming direction in AmpUp
- `N3` is the internal protocol/model shorthand in code (field names, class prefixes, config)
- `Stream Controller` is the user-facing product label everywhere the user sees — in the device selector, mixer segmented control, overview section header, etc.
- `Space` is the user-facing name for a key grouping (internal type is still `ButtonFolderConfig`, but never surfaced as "folder")
- `Home` is the user-facing name for the default/root Space (internally represented as an empty-string folder name)

## Implementation direction — current state
1. **Device selector** in Mixer / Buttons / Lights tabs: `Turn Up` / `Stream Controller` / `Both` via `DeviceSurface` enum
2. **Buttons tab V2 designer** is the active code path for N3 editing:
   - Skeuomorphic chassis that visually merges LCD tiles + page dots + side buttons + encoders as one device
   - Two-column split — left = device canvas, right = DESIGN / ACTION tab bar + inline-editable header
   - Spaces management below the chassis, with Home pinned at the top
3. **Overview tab** renders a dedicated Stream Controller block per profile (2x3 LCD preview grid + side buttons + encoder cards) when the user's Active Surface includes SC
4. **Mixer tab** has SC parity with Turn Up for TARGET values — App Group, HA, Device Groups, Room Lights, Govee, VoiceMeeter, Corsair, plus SC-only knob-twist nav (`sc_space_cycle`, `sc_page_cycle`)
5. **Dual-device use** is fully supported — Turn Up and Stream Controller both active, each with its own knob config + mixer strip + button bindings
6. **Hardware probes are deferred post-show** (`InitializeHardwareDeferred`) so the window paints instantly and device detection runs on background Task.Run

## Buttons V2 designer concepts
- **QuickActionPicker** — accordion-style action picker with categories (Media / Mute / App Control / Device / System / Power / Integrations / Stream Controller / Advanced). Action-specific options render INSIDE the picker via `OptionsHost` right under SELECTED. Search box pops above via a slim magnifier in the tab bar.
- **StreamControllerTile** — unified tile for LCD keys / side buttons / encoders. Theme-aware accent (live `ThemeManager.Accent`), shimmery gradient selection ring + diffuse DropShadow, rounded-corner clip on inner preview.
- **GlassContextMenuHost** — modern right-click menu on keys: dark card, accent gradient ribbon, Material icon per row, cascading submenus, check-glyph for active item.
- **Space / Home model** — each Space has its own `DisplayKeys` + `Buttons` + `PageCount`. Navigating between Spaces is treated as opening/closing a folder (see `App.NavigateToN3Folder`) with the device re-syncing only the active Space's keys. Breadcrumb banner at the top of the chassis (`← HOME › 📐 Space`) for in-Space navigation.

## Rendering pipeline
- `StreamControllerDisplayRenderer.CreateEditorPreview(key, size)` — high-quality PNG for in-app UI (editor preview, tile grid, overview thumbnails). Skips the 60x60 JPEG round-trip so vector icons stay crisp at any scale.
- `StreamControllerDisplayRenderer.CreateDeviceJpeg(key)` / `ComposeDeviceBitmap` + `EncodeDeviceBitmap` — 60x60 rotated JPEG for the actual hardware. Compose is UI-thread-bound (WPF `RenderTargetBitmap`), encode + HID write are thread-safe, so the SC pipeline composes on UI then Task.Run's the I/O. Keeps folder/page navigation from freezing the UI for ~500ms.
- Keys honor `IconColor` (MaterialIcon tint) and `AccentColor` (radial glow). Both are user-controllable via DESIGN tab swatches; the icon picker also carries its per-icon accent forward to the key so the on-device glow matches the hue the user saw in the picker.

## Recent N3 scrolling / test-lane notes
- Long single-line N3 LCD titles auto-scroll when measured text width exceeds the display region. There is no user checkbox anymore; overflow detection is automatic in `ShouldScrollTitle`.
- Smooth N3 title scrolling is image-frame based, not a device text primitive. `CreateScrollingTitleAnimation` builds editor/device frames; `App.StreamControllerAnimatedRefreshIntervalMs` is `80` ms so those frames are actually sent smoothly. Scroll step is `1.25f * scale`, capped at 128 moving frames.
- Scroll loop wraparound draws a second copy of the title after the gap in `DrawScrollingTextOverlay`, avoiding the old blank pause when the first copy fully left the clipped text region.
- v1.0.9.5 test builds include N3 page-2+ LCD binding fixes, font-size selection fixes, title text cursor/space fixes, clock display fixes, maximized-titlebar drag fix, Clear Icon button, and automatic long-title scrolling.
- Side buttons and encoder presses now use high virtual ids (`10000+`) instead of colliding with page-2+ LCD ids (`106+`). Config migration in `AmpUp.Core/ConfigManager.cs` preserves old ambiguous bindings where possible.
- `LoadStreamControllerSelection()` uses `_loading = true` during control population so selecting one key no longer writes the previous key's font size/action values into the new selection.
- Live title saves no longer trim spaces, fixing cursor jumps when typing words followed by spaces.
- `ClearSelectedStreamControllerIcon()` resets bitmap path, preset icon, accent/glow/icon color, dynamic glow color, and solid background defaults so "Clear Icon" does not leave the cyan/blue icon frame behind.

## Test build lane
- Manual installer builds still come from the separate checkout at `C:\Users\audio\Desktop\AmpUp`; always `git pull --ff-only` there before `build-installer.bat`.
- Current test lane folder: `W:\wolfden\assets\ampup-test-builds`.
- Stable latest installer URL: `https://plexwolf.duckdns.org/assets/ampup-test-builds/AmpUp-Test-Latest.exe`.
- Latest uploaded test build in this memory pass: `AmpUp-Setup-1.0.9.5.exe` / `AmpUp-Test-Latest.exe`, built from commit `f54bd06` (`Smooth N3 scroll wraparound`) on 2026-06-08.
- SHA256 for that uploaded installer: `fe1aa58a058094f9a2b3aa6215d6d555df86fc0b9f8c8d25a5d546fd8992a373`.
- When updating the private test page, it is okay to update the hero summary/checksums, but do not change the "Test Flow" section unless Tyson explicitly asks.

## In-app self-updater
- `AmpUp.Core/Services/UpdateChecker.cs` owns the complete update flow. It checks `audioslayer/ampup` releases using the running assembly's informational version and prerelease-aware semantic comparison.
- An eligible release must contain an asset named exactly `AmpUp-Setup-{version}.exe` (for example, tag `v1.4` requires `AmpUp-Setup-1.4.exe`). Keep this naming contract when manually uploading release assets or the app will intentionally ignore the release.
- The updater only accepts HTTPS download URLs under `github.com/audioslayer/ampup/releases/download/`. It requires GitHub release metadata to include a positive asset size and a valid `sha256:` digest.
- Before execution, the download is written as a temporary `.download` file and checked for the Windows `MZ` header, exact GitHub-reported byte count, and exact SHA-256 digest. Invalid or partial downloads are deleted and never launched.
- After verification, a hidden PowerShell helper waits for AmpUp to exit cleanly, starts the Inno Setup installer with the normal Windows UAC prompt plus silent-install switches, and relaunches the same AmpUp executable path after success.
- If elevation is canceled or the installer fails, the helper relaunches the existing AmpUp installation and displays an update error. The helper records details in `%TEMP%\AmpUp\Updates\{version}\update-helper.log`.
- Only one install handoff can run at a time. The MainWindow version label, Settings `Check for Updates`, and tray update banner all use the same `UpdateInfo`/`DownloadAndInstallAsync` path; the tray no longer opens a browser.
- `App.NotifyUpdateAvailable(UpdateInfo)` retains the update even when the tray popup has not been created yet, so opening the tray later still shows the install banner.
- The release metadata parser was validated on 2026-07-20 against real release `v1.1`: `AmpUp-Setup-1.1.exe`, 67,627,215 bytes, SHA-256 `49df5bc93d9e3ab7f63316b4fd86239f49938d045cbed77837f90679cfa261f6`. Debug and Release builds passed with zero warnings/errors.
- Full install/relaunch testing needs a GitHub release newer than the locally running version. At implementation time the app was `1.3` while GitHub's latest release was `v1.1`, so deliberately do not use the older release for an end-to-end updater test.

## v1.0.0-beta additions
### Space Templates
- New TEMPLATES section in the Buttons tab — pre-built `ButtonFolderConfig` layouts the user adds with one click. Unique-name collision handling (Media → Media (2)); auto-navigates into the new Space after Add
- Factories live in `Services/SpaceTemplates.cs`; each entry defines Name / Description / AccentHex / CardIconKind + a `Build()` that returns a fresh folder config. Ships with 7 starters: Room Effects, Media, Discord, System, Apps, Audio Profiles, Spotify
- Pattern avoids the config-race problem of editing `config.json` directly while AmpUp is running — the app's own save path owns the result

### FX icon pack
- `Icons/fx_*.jpg` — 18 bespoke neon-outlined icons for the room effects (Aurora, Ocean, Fire, Lava, Lightning, Police, Scanner, Matrix, Plasma, Nebula, Breathing, Starfield, ColorWave, Rainfall, Waterfall, Rainbow, Meteor, Heartbeat). Generated via Gemini 3 Pro image-gen, cropped to strip Gemini's default app-store-mockup white surround
- `TryResolveCustomPackImagePath` now accepts `fx_` in addition to `neon_` / `material_` / `retro_` / `synthwave_` / `cyber_`, and checks both `.png` and `.jpg`. `PresetIconKind = "fx_aurora"` etc. wires a DisplayKey to the shipped image

### Tap / Double / Hold gesture editor
- Segmented bar at the top of the ACTION tab, shown only when the selection is a side button (idx 106-108) or encoder press (idx 109-111). LCD keys don't get the bar — their `_v2Gesture` stays pinned to Tap
- `GetGestureAction / SetGestureAction`, `GetGesturePath / SetGesturePath`, `GetGestureMacroKeys / SetGestureMacroKeys`, `GetGestureFolderName / SetGestureFolderName` route reads/writes to `.Action / .Path / .MacroKeys / .FolderName` or their `DoublePress*` / `Hold*` variants based on the active gesture
- `_v2SelectionKind` (LcdKey / SideButton / EncoderPress) is stamped at click time from the `StreamControllerSelection.DisplayIdx.HasValue` bit + idx range — resolves the idx 106-117 ambiguity without guessing from config state

### Idx collision runtime fix
- Page-1+ folder LCDs compute `idx = 100 + page*6 + slot` = 106-117, overlapping side buttons (106-108) + encoder presses (109-111). The gesture engine's hold/click timers resolve by bare idx so without help a page-1 LCD press fired the global side-button binding
- `App.PreresolveLcdButton(idx)` runs before every LCD dispatch and stashes the folder ButtonConfig in `_n3ButtonOverride[idx]`. The resolver prefers the stash when present; side/encoder dispatches `.Remove(idx)` first so their global bindings still win when clicked physically

### Per-gesture FolderName
- `ButtonConfig` gained `DoublePressFolderName` / `HoldFolderName`. Empty values fall back to `.FolderName` so older configs keep working during upgrade
- `ButtonGestureEngine` copies the right value into the cloned ButtonConfig it emits for hold/double presses so `open_folder` routes to the per-gesture Space at runtime

### Govee
- `AmbienceSync.ClearAllSegmentTracking()` is now called from `SetGoveePower(dev, true)` — segment devices lose segment mode on power-cycle and our `_segmentEnabled` cache would otherwise sit on stale true for up to 25 s (the keep-alive interval), during which the device ignores razer segment frames and shows its power-on default (often white)
- `SetGoveePower` routes both LAN and Cloud devices — group toggles + room toggle use it so cloud-only devices like the G1S Pro aren't silently skipped
- Room Lights knob (`room_lights` target) handles cloud devices via `GoveeCloudApi.SetBrightness`, throttled to 1.5 s per device to stay inside the ~100 req/min limit. `ApplyRoomLightsBrightness` helper in App.xaml.cs

### Corsair
- `SetStaticColorAllAsync` now respects `_paused` — music reactive + VU Fill timers were bypassing Stop()'s pause gate and repainting LEDs after toggles
- Group toggle + room toggle both flip `Corsair.Enabled = false` on off (not just Stop()) — otherwise `OnConfigChanged` reads Enabled=true and Start()'s Corsair back up, undoing the pause
- `HandleRoomToggle` saves the prior `Corsair.LightSyncMode` into `_roomToggleSavedCorsairMode` on off and restores it on on, so user's static-color choice survives the cycle
- Music-reactive timer guards on `_config.Corsair.Enabled` up front — defense in depth with the SetStaticColorAllAsync pause check

### Room effect resume
- `RoomView.ResumeRoomEffect` falls back to `config.Ambience.RoomEffect` when `_activePattern` is null — otherwise Govee devices power on to white and stay there until the user opens the Room tab
- `HandleRoomToggle` fires `ResumeRoomEffect` immediately (no 800 ms delay) — the 20 FPS frame loop catches dropped frames during device power-up and users don't sit staring at white for a full second

### N3 device JPEG corners
- `EncodeForDevice` no longer zeroes the four 4x4 corner blocks. The mitigation was for rounded-corner clip artifacts that never manifest on the N3's flat rectangular LCDs — those fills showed up as visible black boxes on device

### Version
- Bumped to `1.0.0-beta` in `AmpUp.csproj` and `installer/version.iss`

## Remaining / next
- Native SC support in the OSD overlay (currently still shows the Turn Up 5-knob layout when the Active Surface is SC)
- Tighter throttle heuristics for clock/dynamic display refresh (currently 3s cadence with an early-return when asleep)
- Multi-device support (multiple Turn Ups or multiple N3s) is tracked in the roadmap as a separate feature
- Simpler / more streamlined icon pack for the non-FX templates (Media, Discord, System, Apps, Audio Profiles, Spotify) — user wants a flat/minimal aesthetic instead of the neon reuse
