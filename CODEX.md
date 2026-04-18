# AmpUp — CODEX Notes

## Current hardware support
- Primary supported hardware is the original Turn Up USB mixer (CH343 serial, 5 knobs, 5 buttons, 15 RGB LEDs)

## Future controller target: TreasLin / VSDinside N3
- Exact listing: `https://www.amazon.com/TreasLin-Controller-Customizable-Creators-Compatible/dp/B0FM3NP9ZB`
- Surface: 6 LCD keys, 3 physical buttons, 3 knobs with rotate left/right + press
- Strong direct-control evidence exists:
  - `4ndv/opendeck-akp03` explicitly supports TreasLin N3
  - reported VID/PID: `0x5548` / `0x1001`
  - reverse-engineering notes exist in `bitfocus/companion-surface-mirabox-stream-dock` issue #21
- Working conclusion: direct HID/USB control is realistic, and AmpUp should aim to implement it natively in C# with no long-term dependency on VSD Craft, OpenDeck, or helper software. The clearest current path is to mirror the proven `mirajazz` behavior: protocol v3, HID usage page `65440` / usage `1`, init with `CRT ... DIS` + `CRT ... LIG`, ACK-prefixed input parsing, 60x60 rotated JPEG button images, and `STP`-style image commit handling. Treat firmware version reads as optional on Windows

## Recommended evaluation when device arrives
1. Enumerate USB/HID interfaces and confirm VID/PID
2. Capture raw button, knob, and knob-press events
3. Determine any required startup/init sequence
4. Test LCD image update path separately from input
5. Decide whether AmpUp should support raw HID directly, VSD plugin integration, or both

## Product direction
- Preferred goal is to support N3 without long-term reliance on VSD Craft
- Short-term hybrid usage is acceptable for reference/testing if it speeds reverse engineering
- Dual-device use with original Turn Up + N3 at the same time is a valid target
