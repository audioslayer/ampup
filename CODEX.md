# AmpUp — CODEX Notes

## Current hardware support
- Primary supported hardware is the original Turn Up USB mixer (CH343 serial, 5 knobs, 5 buttons, 15 RGB LEDs)

## Stream Controller family (TreasLin / VSDinside N3-class)
- Exact listing: `https://www.amazon.com/TreasLin-Controller-Customizable-Creators-Compatible/dp/B0FM3NP9ZB`
- Surface: 6 LCD keys, 3 physical buttons, 3 knobs with rotate left/right + press
- Strong direct-control evidence exists:
  - `4ndv/opendeck-akp03` explicitly supports TreasLin N3
  - reported VID/PID: `0x5548` / `0x1001`
  - reverse-engineering notes exist in `bitfocus/companion-surface-mirabox-stream-dock` issue #21
- Working conclusion: direct HID/USB control is realistic, and AmpUp should aim to implement it natively in C# with no long-term dependency on VSD Craft, OpenDeck, or helper software. The clearest current path is to mirror the proven `mirajazz` behavior: protocol v3, HID usage page `65440` / usage `1`, init with `CRT ... DIS` + `CRT ... LIG`, ACK-prefixed input parsing, 60x60 rotated JPEG button images, and `STP`-style image commit handling. Treat firmware version reads as optional on Windows

## Confirmed on our hardware
- Device enumerates as `VID 5548 / PID 1001`
- Product string on this unit: `HOTSPOTEKUSB HID DEMO`
- Primary HID interface is the vendor-defined `MI_00` path with large reports:
  - input report length `513`
  - output report length `1025`
  - usage page `0xFFA0` / `65440`
  - usage `0x0001`
- The device can run alongside the original Turn Up at the same time
- Direct native HID support now works in AmpUp

## Confirmed input protocol
- Input packets are ACK-prefixed on this unit and parse correctly from `MI_00`
- Minimal init for working input is still:
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
- Working display path is:
  - `CRT BAT` header
  - image byte length in big-endian
  - target key index as `key + 1`
  - raw JPEG data streamed in HID output reports
  - `CRT STP` commit
- Working image format on our tool path:
  - `60x60`
  - JPEG
  - rotated `90` degrees
- Clear path is:
  - `CRT CLE`
  - `CRT STP`

## Naming direction in AmpUp
- Keep `N3` as the internal protocol/model shorthand in code when helpful
- Use `Stream Controller` as the user-facing product label in the app
- When we know the concrete device, show it as detected hardware detail, for example:
  - `Stream Controller connected`
  - `TreasLin N3 detected`
  - `Ajazz AKP03 detected`

## Current implementation direction
1. Keep the existing main tabs and add a device selector such as `Turn Up`, `Stream Controller`, and `Both`
2. Treat the Buttons tab as the main Stream Controller editor:
   - 6 LCD keys
   - 3 side buttons
   - 3 encoder presses
   - image upload and action binding on the same page
3. Preserve dual-device support so Turn Up and Stream Controller can both stay active
4. Continue using native HID rather than depending on VSD Craft long-term

## Product direction
- Preferred goal is to support this Stream Controller family without long-term reliance on VSD Craft
- Short-term hybrid usage is acceptable for reference/testing if it speeds reverse engineering
- Dual-device use with original Turn Up + Stream Controller at the same time is a valid target
