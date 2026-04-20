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

## Remaining / next
- Native SC support in the OSD overlay (currently still shows the Turn Up 5-knob layout when the Active Surface is SC)
- Tighter throttle heuristics for clock/dynamic display refresh (currently 3s cadence with an early-return when asleep)
- Multi-device support (multiple Turn Ups or multiple N3s) is tracked in the roadmap as a separate feature
