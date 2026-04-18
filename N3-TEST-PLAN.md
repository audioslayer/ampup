# TreasLin / VSDinside N3 — First Test Plan for AmpUp

This guide is for the first hands-on session after the N3 arrives.

## Goal
Implement self-contained native N3 support inside AmpUp, without long-term dependence on VSD Craft, OpenDeck, or any helper app.

We are trying to answer these questions, in order:
1. Does the device enumerate cleanly over USB/HID?
2. Can we read button and knob events directly?
3. Does it require init packets before input works?
4. Can we update the LCD keys directly?
5. Is direct support realistic enough to continue?

## Known research before hardware arrives
- Device family: TreasLin / VSDinside N3
- Exact listing: https://www.amazon.com/TreasLin-Controller-Customizable-Creators-Compatible/dp/B0FM3NP9ZB
- Known VID/PID from research: `0x5548` / `0x1001`
- HID query path used by existing code: **usage page `65440`, usage `1`**
- Important caution: N3-family devices are close cousins, not guaranteed identical. Existing code treats them as **protocol variants**, not one universal N3 behavior
- Open-source evidence:
  - `4ndv/opendeck-akp03`
  - `4ndv/mirajazz`
  - `bitfocus/companion-surface-mirabox-stream-dock` issue #21
- Important caveat: device may require init packets before HID reads/display control work correctly

## GitHub / code findings to reference first

### 1. `4ndv/opendeck-akp03`
Most important proof that direct control is realistic.

What matters:
- explicitly supports **TreasLin N3**
- reports **VID `0x5548` / PID `0x1001`**
- identifies the device in the N3 / Mirabox / Ajazz family
- marks TreasLin N3 as **protocol v3**
- uses HID query **usage page 65440 / usage 1**
- indicates direct HID support is not theoretical, it already exists in working code

Input mapping already implemented there:
- LCD keys: `1..=6`
- Non-LCD buttons: `0x25`, `0x30`, `0x31`
- Left knob rotate: `0x90` / `0x91`
- Middle knob rotate: `0x50` / `0x51`
- Right knob rotate: `0x60` / `0x61`
- Left knob press: `0x33`
- Middle knob press: `0x35`
- Right knob press: `0x34`

What to check when testing:
- whether the device reports exactly the same bytes on our hardware
- whether encoder press ordering matches this mapping exactly
- whether images need the same 60x60 JPEG + rotation handling

### 2. `4ndv/mirajazz`
This is the real low-level goldmine.

What matters:
- handles HID connect/open/read/write
- handles protocol versions internally
- protocol v3 uses:
  - **1024-byte packets**
  - unique serial number
  - support for both press states
  - extra clear/commit handling
- modern input packets are expected to begin with **`ACK`** (`65 67 75`)
- practical command sequence in known code is:
  - init: `CRT DIS`, then `CRT LIG`
  - clear: `CRT CLE` then `CRT STP`
  - image write: `CRT BAT` header, then raw image chunks, then `CRT STP`
  - keepalive: `CRT CONNECT`
  - shutdown / sleep path includes `CRT HAN`
- supports commands for:
  - brightness
  - button image clear/write
  - image flush / commit (`STP`)
  - keep alive (`CONNECT`)
  - sleep (`HAN`)
  - shutdown / disconnect-style clear
  - knob LED brightness / knob LED color writes
  - mode-like command paths (`CRT MOD`) seen in the protocol surface

Important implementation clues from code:
- image format path for v3 devices is **JPEG, 60x60, rotated 90°**
- image size in the `BAT` header is treated as **big-endian** in the known Rust implementation
- image writes are chunked into HID output reports
- clear-all and flush both require an extra **`STP`**-style commit for v2/v3 devices
- reader converts raw packets into button down/up, encoder down/up, and encoder twist events

### 3. `bitfocus/companion-surface-mirabox-stream-dock` issue #21
Most useful outside reverse-engineering confirmation.

What matters:
- discusses **VSDinside Stream Dock N3 / N3_354D**
- confirms HID event map consistent with the OpenDeck plugin
- says the device needs init packets before HID reads work
- specifically mentions init strings / packets like **CRT DIS / CRT LIG**

### 4. Useful caveats from issues

#### `mirajazz` issue #10
- **Windows firmware version reading is broken / unreliable** due to feature report limitations in the current stack
- Do **not** block first-day testing on firmware version reads under Windows
- Treat firmware read as optional, not required

#### `mirajazz` issue #7
- macOS users observed that devices can ACK packets but still not behave correctly until initialized
- Button reads may start working only after proper init
- Good reminder that **ACK alone does not prove correct init**

#### `opendeck-akp03` issue #15 / #14
- OpenDeck Windows/UI support around knobs has had rough edges
- existing N3-family reports also suggest some sibling devices can have different layout/rotation/button quirks even when they look similar
- That is a warning about **their app path**, not proof the device itself is unusable
- Focus on raw HID behavior, not OpenDeck UI quirks

## Rules for first test session
- Do **not** try to build full AmpUp support on day one
- Do **not** start with LCD graphics first
- Do **not** assume vendor software is required forever just because it helps with first inspection
- The target end state is **AmpUp-only**, with all N3 support built directly into the app
- Focus on proving the hardware path in small steps

## Phase 1 — Basic identification

### Objective
Confirm the machine can see the device and that it matches the expected USB identity.

### Steps
1. Plug in the N3 directly, not through a flaky hub if possible
2. Check USB device list / HID device list
3. Confirm:
   - vendor ID
   - product ID
   - number of HID interfaces
   - whether it exposes keyboard-like interfaces in addition to custom HID
   - usage page / usage if available
4. Record all descriptors we can get
5. Check whether the main HID interface matches usage page `65440`, usage `1`
6. Record packet/report sizes if tools expose them

### Success criteria
- We can confirm the device is present
- VID/PID matches expected value or at least is stable and attributable to the N3
- We know what interfaces exist

### Notes to capture
- Exact VID/PID
- Product string / manufacturer string
- Interface count
- Usage page / usage
- Any surprising extra devices or endpoints

## Phase 2 — Raw input capture

### Objective
See whether the N3 emits usable raw events for:
- LCD keys
- side buttons / physical buttons
- knob left/right rotation
- knob press

### Steps
1. Open raw HID monitoring tools
2. Press one control at a time
3. Log every packet change
4. Test each input separately:
   - each LCD key
   - each side/physical button
   - each knob rotate left/right
   - each knob click
5. Repeat with no vendor app running
6. Compare observed bytes against the GitHub notes above
7. Check whether modern packets begin with `ACK`
8. Watch for separate press/release states versus synthetic single-press behavior

### Success criteria
- We can clearly correlate raw packets to real controls
- We can distinguish button press/release and knob direction

### Notes to capture
- packet bytes for each event
- whether release packets differ from press packets
- whether knob turns emit relative increments or event IDs
- which events match the reverse-engineering notes exactly
- whether packets are ACK-prefixed

## Phase 3 — Init packet check

### Objective
Determine whether the device needs a startup handshake before input becomes readable.

### Steps
1. Test raw input with no vendor software running
2. If input is dead or partial, send the minimal known init path first:
   - `CRT ... DIS`
   - `CRT ... LIG`
3. If we test clear/write paths, always verify whether `STP` is required to commit
4. Observe whether device behavior changes after native init
5. Only if necessary, compare with VSD Craft behavior
6. Try to reproduce the minimal init path without leaving VSD Craft running

### Success criteria
- We know whether init is required
- We know whether init appears simple enough to reproduce

### Notes to capture
- input available before init: yes/no
- input available after native init: yes/no
- input available only after vendor software starts: yes/no
- any repeatable init sequence clues
- whether `CRT DIS` / `CRT LIG` is sufficient by itself

## Phase 4 — LCD/display probing

### Objective
Figure out whether LCD key images can be updated directly.

### Steps
1. Do this only after input path is understood
2. Start with the existing proven assumptions:
   - 60x60 image
   - JPEG
   - rotate 90°
3. Test a single key only
4. After writing image data, send the commit/flush path
5. If clearing the screen, verify whether `STP` is also required
6. Use vendor software only as a comparison reference if needed

### Success criteria
- We can identify the display write path, or conclusively say it is still unknown

### Notes to capture
- whether displays are controlled through HID output reports
- whether image packets are per-key or full-frame
- whether image format/compression seems required
- whether `STP`-style commit is required after writes
- whether existing open-source behavior appears complete enough to mirror in C#

## Phase 5 — Extra hardware checks

### Knob LED / extra output check
Because `mirajazz` already has code for knob LED brightness and knob LED color writes, test whether this exact N3 exposes those outputs. Do not assume all N3-family hardware exposes identical LED behavior.

Capture:
- whether knob LEDs exist / respond
- whether brightness control works
- whether per-LED color writes work

This is optional for day one, but worth checking if input/display work early.

## Phase 6 — Go / No-Go decision

### Green light
Continue direct AmpUp integration if:
- inputs are readable directly
- init is simple or reproducible
- display path looks possible, even if unfinished

### Yellow light
Continue cautiously if:
- inputs work but display path is still unclear
- vendor software is useful as a temporary reference only

### Red light
Pause/return device if:
- inputs are unstable or inaccessible
- required init is too opaque or fragile
- direct control appears dependent on vendor software in a way that is not practical

## Practical v1 if device is usable
Even if displays take longer, a good v1 could be:
- knobs control selected AmpUp channels
- knob press toggles mute
- buttons switch profile / media / app presets
- screens show simple labels later

## Native AmpUp direction
If the hardware path checks out, build native support in AmpUp roughly in this order:
1. HID discovery / matching
2. Per-device / per-protocol capability table
3. Init sequence
4. Input reader
5. Basic event mapping into AmpUp actions
6. Single-key image write
7. Display manager / state sync
8. Optional knob LED support

## Deliverables after first session
At the end of the first real test, write down:
- confirmed VID/PID
- confirmed event mappings
- whether init is required
- whether direct LCD control looks feasible
- whether knob LED control exists on this exact unit
- what code repo / issue was most accurate
- final recommendation: green / yellow / red

## Reminder to future us
Do not let the perfect version block the useful one.
If we can get raw controls working first, that already proves the N3 is a viable AmpUp companion device.
