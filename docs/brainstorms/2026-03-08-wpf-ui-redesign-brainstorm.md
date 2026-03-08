---
topic: WPF UI Redesign
date: 2026-03-08
status: decided
---

# WolfMixer WPF UI Redesign

## What We're Building

A complete UI rewrite from WinForms to WPF with a glassmorphism dark theme, sidebar navigation, and built-in app icon pack. The goal is a premium audio mixer experience on par with Elgato Wave Link and GoXLR — not a utility, but an app you *want* to open.

All backend logic (SerialReader, AudioMixer, ButtonHandler, RgbController, Config, MonitorBrightness) stays untouched. This is a UI-layer replacement only.

## Why WPF

- WolfMixer is Windows-only (WASAPI, COM IPolicyConfig, DDC/CI) — no cross-platform need
- WPF gives GPU-accelerated rendering, proper animations, blur/acrylic materials, vector graphics
- Mature ecosystem, XAML designer support, extensive community resources
- Same .NET 8 runtime — can reference all existing classes directly

## Key Decisions

### 1. Framework: WPF (.NET 8)
Replaces WinForms. Keeps the same .csproj targeting `net8.0-windows`. All non-UI code (SerialReader, AudioMixer, ButtonHandler, RgbController, Config, MonitorBrightness, Logger) remains as-is.

### 2. Visual Style: Glassmorphism Dark
- Frosted glass panels with blur/transparency (Windows 11 Mica/Acrylic materials)
- Floating cards with depth and soft shadows
- Glowing cyan accent (#00B4D8 / #00E5FF)
- Subtle gradients, not flat — but not skeuomorphic either
- Current color palette as base, enhanced with transparency layers

### 3. Navigation: Sidebar + Main View
- Left sidebar with icon-only nav (Mixer, Buttons, Lights, Settings)
- Sidebar expands on hover or stays collapsed — icons + optional labels
- Main content area fills the rest of the window
- Smooth page transitions between views (slide or fade)
- Header bar with WolfMixer branding + connection status

### 4. App Icons: Built-in Icon Pack
- Ship curated high-quality icons for common targets: Discord, Spotify, Chrome, Firefox, Steam, OBS, VLC, System Sounds, Master Volume, Microphone, Monitor, Active Window
- Stored as embedded resources (SVG or high-res PNG)
- Fallback to process icon extraction for unlisted apps
- Each mixer channel displays the icon prominently above the knob

### 5. Window: Compact (1000x700)
- Slightly larger than current 900x720 to accommodate sidebar
- Fixed or min-size with tasteful layout at that size
- System tray behavior unchanged (minimize to tray, click to restore)

### 6. Mixer View (Primary)
- 5 channel strips with:
  - App icon (large, prominent)
  - Channel label
  - Animated knob (WPF rewrite of AnimatedKnobControl — vector-based, smooth animation)
  - VU meter (WPF rewrite — smoother rendering, glow effects)
  - Volume percentage
  - Target info (subtle, below)
- Device preview strip at top (mini hardware visualization, carried over concept)

### 7. Other Views
- **Buttons**: Same 5-column layout, 3 gestures per button, context-sensitive sub-controls
- **Lights**: Same 5-column layout, effect picker, color pickers, speed slider, global brightness
- **Settings**: Connection, startup, profiles — cleaner layout with glassmorphic cards

## Approach: Incremental Migration

1. **Phase 1**: New WPF project structure, MainWindow with sidebar shell, theme/styles
2. **Phase 2**: Mixer view — 5 channel strips with WPF knob + VU controls, app icons
3. **Phase 3**: Buttons view, Lights view, Settings view
4. **Phase 4**: Wire up to existing backend (SerialReader, AudioMixer, etc.)
5. **Phase 5**: Tray icon, system tray integration, polish

Each phase is testable independently. Backend classes don't change.

## Open Questions

- Should we use a WPF UI toolkit (e.g., Material Design in XAML, ModernWpf) or go fully custom?
- Acrylic blur: use Windows 11 native APIs (DwmSetWindowAttribute) or render in-app?
- Should profiles be visible in the sidebar or stay in Settings?
- Icon pack format: SVG (via SharpVectors) or pre-rendered PNG at multiple resolutions?

## Competitor Inspiration

| App | What to borrow |
|-----|---------------|
| Elgato Wave Link | Clean channel layout, signal-flow clarity, flat premium aesthetic |
| GoXLR | Channel photos/icons, dense but organized, hardware visualization |
| SteelSeries Sonar | Peak meters in sliders, drag-and-drop routing, modern minimalism |
| Discord Settings | Sidebar navigation pattern, dark glassmorphic cards |

## What NOT to Do

- Don't redesign the serial protocol or audio logic
- Don't add features — this is purely visual
- Don't over-animate — subtle transitions only
- Don't break config.json compatibility
