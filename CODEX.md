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

## N3 implementation gotchas
- Long single-line N3 LCD titles auto-scroll when measured text width exceeds the display region. There is no user checkbox anymore; overflow detection is automatic in `ShouldScrollTitle`.
- Smooth N3 title scrolling is image-frame based, not a device text primitive. `CreateScrollingTitleAnimation` builds editor/device frames; `App.StreamControllerAnimatedRefreshIntervalMs` is `80` ms so those frames are actually sent smoothly. Scroll step is `1.25f * scale`, capped at 128 moving frames.
- Scroll loop wraparound draws a second copy of the title after the gap in `DrawScrollingTextOverlay`, avoiding the old blank pause when the first copy fully left the clipped text region.
- Side buttons and encoder presses now use high virtual ids (`10000+`) instead of colliding with page-2+ LCD ids (`106+`). Config migration in `AmpUp.Core/ConfigManager.cs` preserves old ambiguous bindings where possible.
- Page-2+ LCD ids can still overlap legacy ids. `App.PreresolveLcdButton(idx)` stashes the active Space's binding in `_n3ButtonOverride`; physical side-button/encoder dispatch removes any stale override before resolving its global binding.
- Side buttons and encoder presses support Tap / Double / Hold. LCD keys remain Tap-only. `DoublePressFolderName` and `HoldFolderName` fall back to `FolderName` for old configs.
- `LoadStreamControllerSelection()` uses `_loading = true` during control population so selecting one key no longer writes the previous key's font size/action values into the new selection.
- Live title saves no longer trim spaces, fixing cursor jumps when typing words followed by spaces.
- `ClearSelectedStreamControllerIcon()` resets bitmap path, preset icon, accent/glow/icon color, dynamic glow color, and solid background defaults so "Clear Icon" does not leave the cyan/blue icon frame behind.
- Device JPEG encoding must not blank the four 4x4 corners; the N3 LCDs are rectangular and those blocks are visibly black on hardware.

## Release and build workflow
- Manual installer builds still come from the separate checkout at `C:\Users\audio\Desktop\AmpUp`; always `git pull --ff-only` there before `build-installer.bat`.
- `AmpUp.csproj` is the version source of truth. `build-installer.bat` generates `installer/version.iss`; do not hand-edit release metadata independently.
- Release work is done directly on `master` unless Tyson explicitly asks for a branch.
- Current public release (2026-07-25): [`v1.3.1`](https://github.com/audioslayer/ampup/releases/tag/v1.3.1), built from commit `17985650460ddf47441bf684681d2b95b6a94ce2`.
- Release asset: `AmpUp-Setup-1.3.1.exe`, 67,657,749 bytes, SHA-256 `573437585eea06fb20f10bc3de79c666ee2498a0ab3b4a29c6b3dc797b349d2f`. GitHub's asset digest was verified against the local installer before publishing.
- The installer is currently unsigned, so release notes should retain the SmartScreen/checksum guidance until code signing is added.

## In-app self-updater
- `AmpUp.Core/Services/UpdateChecker.cs` owns the complete update flow. It checks `audioslayer/ampup` releases using the running assembly's informational version and prerelease-aware semantic comparison.
- An eligible release must contain an asset named exactly `AmpUp-Setup-{version}.exe` (for example, tag `v1.4` requires `AmpUp-Setup-1.4.exe`). Keep this naming contract when manually uploading release assets or the app will intentionally ignore the release.
- The updater only accepts HTTPS download URLs under `github.com/audioslayer/ampup/releases/download/`. It requires GitHub release metadata to include a positive asset size and a valid `sha256:` digest.
- Before execution, the download is written as a temporary `.download` file and checked for the Windows `MZ` header, exact GitHub-reported byte count, and exact SHA-256 digest. Invalid or partial downloads are deleted and never launched.
- After verification, a hidden PowerShell helper waits for AmpUp to exit cleanly, starts the Inno Setup installer with the normal Windows UAC prompt plus silent-install switches, and relaunches the same AmpUp executable path after success.
- If elevation is canceled or the installer fails, the helper relaunches the existing AmpUp installation and displays an update error. The helper records details in `%TEMP%\AmpUp\Updates\{version}\update-helper.log`.
- Only one install handoff can run at a time. The MainWindow version label, Settings `Check for Updates`, and tray update banner all use the same `UpdateInfo`/`DownloadAndInstallAsync` path; the tray no longer opens a browser.
- `App.NotifyUpdateAvailable(UpdateInfo)` retains the update even when the tray popup has not been created yet, so opening the tray later still shows the install banner.
- The public `v1.3.1` release satisfies the updater's tag/filename/size/digest contract.

## v1.3.1 hotfix and reliability state
- Issue #24 is fixed and closed: Turn Up and N3 buttons retain independent output-device assignments across buttons, profiles, and N3 tap/double/hold gestures. The fix was manually verified before the v1.3.1 release.
- Issue #22 is fixed and closed: Windows endpoint notifications refresh Bluetooth/USB device lists, output cycling, and device-color lighting without an app restart. Bluetooth connect/switch behavior was manually verified on the installed v1.3 build.
- Issue #23 is fixed and closed: Turn Up and N3 inputs use separate `HardwareInputPump` workers; absolute knob events coalesce to the newest value; serial stalls trigger reconnect; refresh paths are non-reentrant; and stale resources/log floods are cleaned up.
- Debug and Release builds completed with zero warnings/errors, the NuGet vulnerability audit was clean, and the installed build passed a runtime soak without delayed handlers, session-refresh failures, or serial stalls.
- The old `HARDWARE_DISCONNECT_MEMORY.md` investigation was superseded by these fixes. If symptoms recur, collect `%APPDATA%\AmpUp\ampup.log` with the approximate failure time and reopen the relevant GitHub issue.

## Durable integration notes
- Space templates are created through `Services/SpaceTemplates.cs`; use the app's normal config save path instead of editing `config.json` while AmpUp is running.
- `Icons/fx_*.jpg` is the room-effects pack. `TryResolveCustomPackImagePath` supports `fx_`, `neon_`, `material_`, `retro_`, `synthwave_`, and `cyber_` names with `.png` or `.jpg` files.
- Govee segment tracking must be cleared after power-on because segment mode is lost across a device power cycle. Room/group power paths must support both LAN and cloud-only devices.
- Corsair effect writers must honor `_paused` and `Corsair.Enabled`; room off/on preserves and restores the prior `LightSyncMode`.
- `RoomView.ResumeRoomEffect` must fall back to the saved room effect when no in-memory active pattern exists, preventing devices from remaining at their power-on white state.
