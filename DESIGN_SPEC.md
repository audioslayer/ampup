# AmpUp UI/UX Design Spec — Overnight Overhaul

## Vision
Premium dark-theme audio mixer app. Think Elgato Wave Link meets a high-end hardware controller.
No rounded buttons that look like a music player. This is a serious tool with serious aesthetics.

## Color Palette

```csharp
BgBase     = #0F0F0F   // deepest background
BgDark     = #141414   // main form background
CardBg     = #1C1C1C   // cards / panels
CardBorder = #2A2A2A   // card borders
InputBg    = #242424   // textboxes, combos
InputBorder= #363636
Accent     = #00B4D8   // primary cyan (matches Turn Up green → wolfden cyan)
AccentGlow = #00E5FF   // bright cyan for active/glow states
AccentDim  = #007A94   // dim cyan for inactive
TextPrimary= #E8E8E8
TextSec    = #9A9A9A
TextDim    = #555555
DangerRed  = #FF4444
SuccessGrn = #00DD77
WarnYellow = #FFB800
```

## Form Layout

```
┌─────────────────────────────────────────────────────────────┐
│  🐺 WOLFMIXER   [● Connected]          [Profile 1 ▼]  [⚙]  │  ← Header bar
├──────────────────────────────────────────────────────────────┤
│  [KNOBS]  [BUTTONS]  [LIGHTS]  [SETTINGS]                    │  ← Tab strip (icons + text)
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ DEVICE PREVIEW ─────────────────────────────────────┐   │  ← Hardware visualization (knobs tab only)
│  │  [◯]  [◯]  [◯]  [◯]  [◯]   glowing knobs           │   │
│  │  [■]  [■]  [■]  [■]  [■]   LED buttons              │   │
│  └───────────────────────────────────────────────────────┘   │
│                                                              │
│  CH1      CH2      CH3      CH4      CH5                    │  ← Channel columns
│  [knob]   [knob]   [knob]   [knob]   [knob]                │
│  [VU]     [VU]     [VU]     [VU]     [VU]                  │
│  [label]  [label]  [label]  [label]  [label]               │
│  [target] [target] [target] [target] [target]              │
│  ...                                                        │
└─────────────────────────────────────────────────────────────┘
```

Form size: 900 x 700
FormBorderStyle: FixedDialog, MaximizeBox=false
Custom title bar font: "Segoe UI" 10pt Bold

## AnimatedKnobControl (new custom control)

File: `AnimatedKnobControl.cs`

Properties:
- `float Value` (0.0 to 1.0) — current position, drives arc angle
- `Color ArcColor` — the sweep arc / glow color
- `string PercentText` — e.g. "72%" shown in center
- `int KnobSize` — default 88px

Drawing (override OnPaint):
1. Background: fill circle with CardBg (#1C1C1C), draw outer ring in CardBorder
2. Track arc: thin (3px) dark arc from 225° to 495° (270° sweep), color=InputBorder
3. Value arc: colored arc from 225° to (225 + Value*270)°, color=ArcColor, width=4px
   - Use GraphicsPath + DrawArc with AntiAlias
4. Glow: draw the value arc again 2px wider with ArcColor at 40% opacity (soft glow effect)
5. Needle dot: small filled circle (7px) at the tip of the value arc, color=ArcColor with bright glow
6. Center label: draw PercentText in "Segoe UI" 9pt Bold, centered, color=TextPrimary
7. Optional: draw a very subtle radial gradient from center outward (dark to slightly lighter)

Use SmoothingMode.AntiAlias and CompositingQuality.HighQuality throughout.

## VuMeterControl (new custom control)

File: `VuMeterControl.cs`

Properties:
- `float Level` (0.0 to 1.0) — current audio level
- `Color BarColor` — base color (matches knob ArcColor)
- Orientation: Vertical (tall and narrow, ~14px wide × 60px tall)

Drawing:
- 16 stacked segment rects with 1px gap
- Bottom 10 segments: BarColor at full brightness
- Top 3 segments: #FFB800 (yellow warning)
- Top 1 segment: #FF4444 (red peak)
- Lit segments: draw filled with color
- Unlit segments: draw with CardBg + CardBorder outline
- Animate smoothly: use peak hold (hold peak for 1.5s, then fall) with a separate _peak field
- Smooth fall: lerp level down at ~0.05 per timer tick (50ms) when no new signal

## Tab Strip Design

Owner-drawn tab control:
- Tab item size: 130px wide × 36px tall
- Selected tab: CardBg background, Accent bottom border (3px), TextPrimary text
- Unselected: BgDark background, TextDim text
- Hover: slight CardBg tint
- Icons before text: use Unicode symbols — "◈ KNOBS", "⊞ BUTTONS", "❋ LIGHTS", "⚙ SETTINGS"

## Header Bar

Height: 48px, BgDark background, bottom border 1px CardBorder

Left side:
- Wolf emoji "🐺" + "WOLFMIXER" in Segoe UI 13pt Bold, Accent color
- Small "VOLUME MIXER" label in TextDim 7pt

Center-left:
- Connection status: filled circle dot (8px) — #00DD77 when connected, #FF4444 when not
- "Connected" / "No Device" text next to dot, TextSec 8pt

Right side:
- Profile dropdown (flat, dark, 120px wide)
- Small settings gear button

## Knobs Tab Layout

### Device Preview Strip
At top of knobs tab, height=70px, full width, CardBg background with CardBorder bottom line.

Draw:
- Centered, draws the 5 Turn Up knobs as circles (24px diameter) with a 10px glowing ring in the knob's LED color
- Below each knob: small 14×8px rectangle button LED in the button's LED color (or gray if action=none)
- Current knob position indicated by a small arc sweep on each mini-knob
- Label under each: CH1-CH5 in TextDim 7pt

### Channel Columns
5 columns, each 156px wide, with 6px gap.
Each column is a card (CardBg, CardBorder border, rounded corners via Paint event if desired).

Column layout (top to bottom):
```
CH {n}                          ← TextDim 7pt, centered
──────────────────────────────
[AnimatedKnobControl 88px]      ← centered, 88×88
[VuMeterControl  14×60]         ← centered below knob, left-aligned within padding
──────────────────────────────
Label: [TextBox]                ← editable name, centered, InputBg
Target: [ComboBox]              ← target selection
[Device ComboBox]               ← only visible for output_device/input_device
Response: [ComboBox]            ← curve
Range: [Min NUD] → [Max NUD]    ← side by side
```

Column height: ~500px

## Buttons Tab Layout

Scrollable panel, same 5-column card layout.
Per column:

```
BUTTON {n}                      ← header with accent-colored button shape icon
─────────────────────────────
[TAP]   [×2]   [HOLD]           ← pill-shaped gesture selector tabs
─────────────────────────────
Action: [ComboBox]
[context-sensitive sub-controls]
─────────────────────────────
```

Gesture rows: instead of 3 separate stacked sections, use a small 3-tab selector within the card.
This is cleaner and uses less vertical space.
Current gesture tab highlighted with Accent underline.
Each tab click shows/hides the action row for that gesture.

## Lights Tab Layout

5 columns, each showing:
```
CH {n}                          ← header
[Effect preview box 138×38px]   ← animated preview showing the current effect + colors
Effect: [ComboBox]              ← 9 effects
Color 1: [8 color swatches]     ← the colorful pill buttons
Color 2: [8 color swatches]     ← only shown for 2-color effects
Speed:   [TrackBar]             ← animated effects only
```

Effect preview box: draws a mini animated preview of the selected effect (e.g. pulsing, rainbow sweep, etc.)
Renders in real-time on a 200ms timer.

## Settings Tab

Two columns:
Left column:
- "CONNECTION" section: port, baud rate, test button, status
- "STARTUP" section: start with Windows checkbox

Right column:
- "PROFILES" section: active profile dropdown, Save, New, Delete buttons, profile list

Footer strip (bottom of Settings):
- Version: "AmpUp v1.0.0"
- GitHub link text
- Log file link

## Tray Icon (32×32 bitmap)

Draw a more polished icon:
1. Clear background
2. Draw 5 equalizer bars (different heights) with rounded tops
   - Heights for 5 bars: [6, 12, 20, 15, 9] pixels
   - Bar width: 4px, gap 2px
   - Total width: 5*4 + 4*2 = 28px, center in 32px
   - Color: Accent (#00B4D8) when connected, #888888 when disconnected
3. Draw a thin horizontal baseline across bottom of bars

## Tray Context Menu

Current:
- Configure
- Open Config Folder
- ---
- Exit

New:
- "🐺 AmpUp" (header label, non-clickable)
- ---
- Live volume display (5 items, non-clickable, updated every 500ms):
  "◈ Master      ████░░  72%"
  "◈ Discord     ██░░░░  34%"
  etc.
- ---
- Configure...
- Open Log
- Open Config Folder
- ---
- Exit

## Deploy Instructions (for README)

After running agents, update CLAUDE.md with new design notes.
Morning deployment: run deploy.bat from C:\Users\audio\Desktop\AmpUp\

## Implementation Notes for Agents

1. Preserve ALL existing functionality — this is a visual redesign only
2. No logic changes to AudioMixer.cs, ButtonHandler.cs, SerialReader.cs, RgbController.cs, Config.cs
3. AnimatedKnobControl and VuMeterControl are new files — they just draw stuff
4. ConfigForm.cs is a complete rewrite — keep all arrays, all event handlers, all save logic — just change the layout and painting
5. TrayApp.cs: only change BuildIcon() and BuildMenu() and add a timer for live tray volumes
6. Use Graphics.SmoothingMode = SmoothingMode.AntiAlias everywhere you draw custom shapes
7. All custom colors defined as static readonly at the top of ConfigForm, matching the palette above
8. The window does NOT need a custom title bar — Windows default title bar is fine, keep FormBorderStyle.FixedDialog

## Integrations (Phase 2)

### Home Assistant
- Knob targets: ha_light:{entity_id}, ha_media:{entity_id}, ha_cover:{entity_id}, ha_fan:{entity_id}
- Button actions: ha_toggle:{entity_id}, ha_scene:{scene_id}, ha_script:{script_id}
- Setup: Settings tab → "Integrations..." button → enter HA URL + long-lived access token
- Easy for end users: paste URL, paste token from HA profile page, click Test

### Fan Control (by Rem0o)
- Knob targets: fanctrl:{controlId}
- Setup: Install Fan Control app (free, github.com/Rem0o/FanControl.Releases), enable HTTP server in FC settings
- Enter FC URL in Integrations settings (default: http://localhost:5550)
- Works with: all fan controllers supported by Fan Control (hundreds of boards)

### Wire-up notes (for AudioMixer.cs)
In AudioMixer.SetVolume(), add target handling:
```csharp
if (target.StartsWith("ha_"))
{
    _ = _ha?.HandleKnobAsync(knob.Target, vol);
    return;
}
if (target.StartsWith("fanctrl:"))
{
    var controlId = FanController.ParseTarget(knob.Target);
    _ = _fanCtrl?.SetSpeedAsync(controlId, vol);
    return;
}
```
HAIntegration and FanController instances are passed into AudioMixer constructor.
