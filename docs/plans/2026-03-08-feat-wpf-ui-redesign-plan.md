---
title: "feat: WPF UI Redesign with Glassmorphism Theme"
type: feat
date: 2026-03-08
deepened: 2026-03-08
---

# feat: WPF UI Redesign with Glassmorphism Theme

## Enhancement Summary

**Deepened on:** 2026-03-08
**Agents used:** Architecture Strategist, Performance Oracle, Security Sentinel, Code Simplicity Reviewer, Pattern Recognition Specialist, Best Practices Researcher, Frontend Design Skill

### Key Improvements
1. **Simplified architecture** — dropped full MVVM in favor of code-behind (simplicity reviewer). This is a personal project, not enterprise software.
2. **Critical performance patterns** — pre-allocate/freeze all drawing resources, use `StreamGeometry` for arcs, `DrawingVisual` for VU meters, dirty-checking on property setters
3. **Security hardening** — encrypt HA token with DPAPI, add serial buffer overflow protection, remove unused `AllowUnsafeBlocks`
4. **wpfui v4.x** — library is at v4.2.0, not v3. Updated all references.
5. **Per-monitor DPI V2** — requires app manifest + csproj config for multi-monitor setup
6. **Design polish specifications** — monospace readouts, staggered column reveals, noise texture, LED glow effects, strict 8px grid
7. **Phase reordering** — build easiest views first (Settings → Lights → Buttons → Mixer) to reduce risk
8. **Thread marshaling strategy** — explicitly use `Dispatcher.BeginInvoke` at the orchestrator→UI boundary, never inside backend classes

### Simplicity Decision

The code simplicity reviewer made a strong case for dropping MVVM entirely and using WPF code-behind. For a single-developer, single-user tray app with 5 knobs, MVVM adds complexity without benefit. **This plan adopts the simplified approach:**

- No ViewModels directory — code-behind on each view
- No CommunityToolkit.Mvvm — direct property reads/writes
- One `Theme.xaml` instead of 3 resource dictionaries
- AppOrchestrator logic stays in `App.xaml.cs` (no separate service class)
- 4 phases instead of 7

---

## Overview

Full rewrite of WolfMixer's UI layer from WinForms to WPF (.NET 8). The new UI features a glassmorphism dark theme with Windows 11 Mica backdrop, sidebar navigation, built-in app icon pack, GPU-accelerated animated knobs, real-time VU meters, and a premium mixer-first experience inspired by Elgato Wave Link and GoXLR.

All backend logic (SerialReader, AudioMixer, ButtonHandler, RgbController, Config, MonitorBrightness, HAIntegration, FanController, Logger) remains untouched. This is purely a UI-layer replacement.

## Problem Statement

The current WinForms UI is functional but dated. WinForms limits visual quality — no GPU-accelerated rendering, no blur/transparency effects, no smooth animations, and GDI+ custom controls look pixelated on high-DPI displays. The tab-based layout crams all configuration into a single 900x720 window without visual hierarchy. Premium audio mixer apps (Elgato Wave Link, GoXLR, SteelSeries Sonar) set a much higher bar for desktop mixer UX.

## Proposed Solution

Replace all UI code with WPF using the `wpfui` library (v4.x) for Fluent Design controls and Mica/Acrylic backdrop. Use `H.NotifyIcon.Wpf` for system tray integration. Custom controls (animated knob, VU meter) rewritten with WPF's `DrawingContext` for GPU-accelerated rendering. Code-behind for simplicity — no MVVM framework needed for this project size.

## Technical Approach

### Architecture (Simplified)

```
WolfMixer/
├── App.xaml / App.xaml.cs              # WPF entry point, theme, tray setup, orchestration
├── MainWindow.xaml / .cs               # Shell: sidebar + content area + header
├── Views/
│   ├── MixerView.xaml / .cs            # 5 channel strips with knobs + VU meters
│   ├── ButtonsView.xaml / .cs          # 5 button columns, 3 gestures each
│   ├── LightsView.xaml / .cs           # 5 light columns, effects + colors
│   └── SettingsView.xaml / .cs         # Connection, startup, profiles, integrations
├── Controls/
│   ├── AnimatedKnobControl.cs          # WPF custom control (OnRender with arcs)
│   └── VuMeterControl.cs              # WPF custom control (DrawingVisual)
├── Theme.xaml                          # Colors, brushes, and control styles (single file)
├── Assets/
│   ├── Icons/                          # Built-in app icon PNGs (64x64)
│   └── app.manifest                    # Per-monitor DPI V2
├── NativeMethods.cs                    # Consolidated P/Invoke declarations
│
│   # --- Backend (unchanged) ---
├── SerialReader.cs
├── AudioMixer.cs
├── ButtonHandler.cs
├── RgbController.cs
├── Config.cs
├── MonitorBrightness.cs
├── HAIntegration.cs
├── FanController.cs
└── Logger.cs
```

**~10 new/modified files** (down from 20+ in the original plan).

### Research Insights: Architecture

**Why code-behind over MVVM (Simplicity Reviewer):**
- You have one user, no tests, no designers. Code-behind is appropriate.
- `Config.cs` already IS your model. Read from it on load, write back on save.
- One `MainWindow.xaml.cs` with orchestration wiring replaces both `TrayApp.cs` and the proposed `AppOrchestrator.cs`.

**Why NOT extract AppOrchestrator (Architecture Strategist):**
- The actual orchestration logic in `TrayApp.cs` is only ~100 lines of wiring code. The rest is UI.
- For this project, the wiring can live in `App.xaml.cs` directly. It's the composition root.
- If you ever want to extract it later, the refactor is trivial.

**Key architectural decisions:**

1. **wpfui FluentWindow** — Provides Mica backdrop, dark theme, and `NavigationView` sidebar. Falls back to solid dark background on Windows 10. **Use v4.x** (current: 4.2.0, January 2025).

2. **H.NotifyIcon.Wpf** — WPF-native tray icon. Only essential library besides wpfui.

3. **Code-behind wiring** — `App.xaml.cs` owns `SerialReader`, `AudioMixer`, `ButtonHandler`, `RgbController`. Event handlers in code-behind. Direct reads/writes to `AppConfig`.

4. **Single Window instance** — Created at startup, hidden to tray on close. Tray click toggles visibility. Stop VU timer when hidden to save CPU.

5. **`NativeMethods.cs`** — Consolidate duplicated P/Invoke declarations from `AudioMixer.cs` and `ButtonHandler.cs` (`GetForegroundWindow`, `GetWindowThreadProcessId`, `SetSuspendState`).

### Research Insights: Threading (Architecture Strategist + Performance Oracle)

**Strategy:** Polling from UI thread, never marshaling serial events to Dispatcher.

- Serial events (`OnKnob`, `OnButton`) fire on background threads and call `AudioMixer.SetVolume()` / `ButtonHandler.HandleDown()` directly — these are already thread-safe (locks, volatile refs).
- A `DispatcherTimer` at 50ms polls volume/peak levels for UI display. This decouples UI refresh rate from serial event rate.
- **Never call `Dispatcher.Invoke` from serial handlers.** The backend path should never touch the UI thread.
- Guard the timer: `if (!IsVisible) return;` to skip rendering when window is hidden.

**COM threading for `GetPeakLevel()`:** WASAPI `AudioMeterInformation.MasterPeakValue` must be queried using the existing `_enumerator` + `_enumLock` pattern. Do not cache `AudioMeterInformation` objects across calls. Create a fresh `MMDevice` reference within the lock, read the value, dispose.

### Research Insights: Performance (Performance Oracle)

**Critical — pre-allocate ALL drawing resources:**

```csharp
// AnimatedKnobControl.cs — class-level, created once
private static readonly Pen TrackPen;
private Pen _glowPen, _valuePen;

static AnimatedKnobControl()
{
    TrackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)), 5);
    TrackPen.Freeze(); // Frozen = 212 bytes vs 972 bytes unfrozen
}
```

- **Use `StreamGeometry`** instead of `PathGeometry` for arcs — write-once, cheaper allocation
- **Use `DrawingVisual` children** for VU meters (not `OnRender`) — avoids full layout pass on every update
- **Dirty-checking in property setters** — skip `InvalidateVisual()` if value hasn't materially changed (`Math.Abs(delta) < 0.001f`)
- **Cache `MMDevice` objects** in AudioMixer instead of creating/disposing on every poll
- **No per-control timers** — drive all animation from a single 50ms `DispatcherTimer` in the MixerView
- **Icon caching** — cache extracted process icons in `Dictionary<string, ImageSource>` to avoid repeated file system access

**VU Meter: DrawingVisual pattern:**
```csharp
public class VuMeterVisual : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private static readonly SolidColorBrush UnlitBrush;
    private static readonly Rect[] SegmentRects; // pre-computed

    static VuMeterVisual()
    {
        UnlitBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        UnlitBrush.Freeze();
        // Pre-compute all 16 segment rectangles...
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    public void UpdateLevel(float level, float peak)
    {
        using var dc = _visual.RenderOpen();
        // Draw directly — no InvalidateVisual, no layout pass
    }
}
```

**Memory target:** WinForms sits at 25-40MB. WPF adds ~20-30MB baseline. Target: 50-70MB, well under 80MB limit — if allocation issues are fixed.

### Research Insights: Security (Security Sentinel)

**Priority fixes to include in migration:**

| Priority | Fix | Effort |
|----------|-----|--------|
| **HIGH** | Encrypt HA token with DPAPI (`ProtectedData.Protect`) | 30 min |
| **QUICK** | Remove `AllowUnsafeBlocks` from csproj (not used anywhere) | 1 min |
| **QUICK** | Add 256-byte serial buffer size limit in `SerialReader.cs` | 5 min |
| **LOW** | Restrict `close_program` substring match to 3+ chars | 10 min |
| **LOW** | Add `/t 5` delay to shutdown/restart power actions | 5 min |

**HA Token encryption pattern:**
```csharp
// On save:
var encrypted = ProtectedData.Protect(
    Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
config.HomeAssistant.Token = Convert.ToBase64String(encrypted);

// On load:
var bytes = ProtectedData.Unprotect(
    Convert.FromBase64String(config.HomeAssistant.Token), null, DataProtectionScope.CurrentUser);
var token = Encoding.UTF8.GetString(bytes);
```

### Research Insights: DPI (Best Practices Researcher)

**Per-monitor DPI V2 setup (required for multi-monitor):**

1. Create `Assets/app.manifest`:
```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

2. Add to csproj: `<ApplicationManifest>Assets\app.manifest</ApplicationManifest>`

3. In custom controls, read DPI from `VisualTreeHelper.GetDpi(this)` and scale pixel measurements.

---

### Implementation Phases

#### Phase 1: Scaffold — Project + Shell + Tray + Theme

Create the WPF project, get a window on screen with tray icon working.

- [x] Modify `WolfMixer.csproj`: add `<UseWPF>true</UseWPF>`, keep `<UseWindowsForms>true</UseWindowsForms>` temporarily
- [x] Remove `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (not used)
- [x] Add NuGet packages: `WPF-UI` v4.x, `H.NotifyIcon.Wpf` v2.x
- [x] Create `Assets/app.manifest` for per-monitor DPI V2
- [x] Create `App.xaml` / `App.xaml.cs` — WPF entry point with single-instance Mutex
- [x] Create `Theme.xaml` — single file with colors, brushes, and control styles
- [x] Create `MainWindow.xaml` using `ui:FluentWindow` — 1000x700, Mica backdrop, dark theme
- [x] Implement sidebar with 4 nav buttons (Mixer, Buttons, Lights, Settings) with icons
- [x] Implement header bar (48px) — branding, connection status dot
- [ ] Create `TaskbarIcon` using `H.NotifyIcon.Wpf` — custom 32x32 icon, context menu, tray volume display
- [ ] Window close → hide (`OnClosing` override). Tray double-click → show/activate.
- [x] Create empty placeholder views for each section
- [x] Wire navigation: sidebar click → swap `ContentControl.Content`
- [x] Create `NativeMethods.cs` — consolidate P/Invoke declarations
- [x] Replace `ButtonHandler.SetSuspendState` with P/Invoke to `Powrprof.dll`

**`WolfMixer.csproj` (target state):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ApplicationManifest>Assets\app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\wolfmixer.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WPF-UI" Version="4.*" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.*" />
    <PackageReference Include="NAudio" Version="2.2.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="System.IO.Ports" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**Theme.xaml merge order in App.xaml (Pattern Recognition):**
```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- wpfui theme FIRST -->
            <ui:ThemesDictionary Theme="Dark" />
            <ui:ControlsDictionary />
            <!-- custom overrides AFTER -->
            <ResourceDictionary Source="Theme.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

**wpfui v4 gotchas (Best Practices Researcher):**
- Always set `TargetPageType` on every `NavigationViewItem` — omitting it silently breaks navigation
- Mica only works on Windows 11 Build 22000+. Falls back to solid dark background automatically.
- wpfui controls use their own theme dictionaries — if mixing standard WPF controls, manually apply theme resources

**Success criteria:** Project builds. Window opens with Mica backdrop. Sidebar navigates between 4 empty views. Tray icon works. Serial/audio/RGB wiring functional.

---

#### Phase 2: Views — Settings, Lights, Buttons (Easiest First)

Port the 3 config views that don't need custom controls. Build easiest first to reduce risk.

**Settings View (simplest):**
- [x] Create `SettingsView.xaml` / `.cs` — glassmorphic cards for each section
- [x] Sections: Connection (port/baud), Startup (start with Windows), Profiles (save/load/new/delete), Integrations (HA + FanControl)
- [ ] Encrypt HA token with DPAPI on save, decrypt on load (Security fix H1)
- [x] Profile management: save current, create new, load dropdown, delete with confirmation

**Lights View:**
- [x] Create `LightsView.xaml` / `.cs` — 5 columns with effect dropdown, color pickers, speed slider
- [x] Color preview rectangle per channel showing current color
- [x] Global brightness slider at bottom
- [x] Conditionally show color2 and speed based on effect type

**Buttons View:**
- [x] Create `ButtonsView.xaml` / `.cs` — 5 columns, 3 gesture sections per column
- [x] Context-sensitive sub-controls via `Visibility` binding: action dropdown → show/hide path, macro, device, profile, power fields
- [x] 3 gesture rows per button (Tap / Double / Hold) with dividers

**Backend fixes to bundle here:**
- [x] Add 256-byte buffer limit to `SerialReader.cs` (Security fix M2)
- [x] Create `NativeMethods.cs` — consolidate `GetForegroundWindow`, `GetWindowThreadProcessId` from AudioMixer + ButtonHandler
- [x] Replace `ButtonHandler.SetSuspendState` with P/Invoke to `Powrprof.dll`

**Success criteria:** All 3 config views fully functional. Config changes persist correctly. Profile switching works. All 17 button actions configurable. All 9 LED effects configurable.

---

#### Phase 3: Mixer View — Custom Controls + Real-Time

The hardest phase. Custom WPF controls for knobs and VU meters with real-time data.

- [ ] Create `AnimatedKnobControl.cs` — WPF `FrameworkElement` using `OnRender(DrawingContext)`:
  - Arc sweep from 225deg with 270deg sweep using `StreamGeometry` + `ArcSegment`
  - Glow layer at 35% opacity drawn behind value arc
  - Needle dot (8px filled + glow) at arc tip
  - Center circle with percentage text using `FormattedText`
  - **All Pen/Brush/Font objects pre-allocated and Frozen as static fields**
  - Dirty-checking: skip `InvalidateVisual()` if `Math.Abs(delta) < 0.001f`

- [ ] Create `VuMeterControl.cs` — WPF `FrameworkElement` using `DrawingVisual` children (NOT OnRender):
  - 16 segments, pre-computed `Rect[]` array
  - Color zones: 10 bar color, 2 transitional (green-yellow blend), 3 yellow, 1 red
  - Peak hold indicator (gray #E6E6E6) at peak segment, 1.5s hold, smooth 400ms fade-out
  - **All brushes pre-allocated and Frozen (only 5 distinct brushes: bar, yellow, red, unlit, peak)**

- [ ] Add `GetPeakLevel(KnobConfig)` to `AudioMixer.cs` — reads `AudioMeterInformation.MasterPeakValue` per session. Use `_enumerator` + `_enumLock`, don't cache `AudioMeterInformation` objects.

- [ ] Create `MixerView.xaml` / `.cs` — 5 channel strip cards in horizontal `UniformGrid`
  - Each strip: app icon (24x24 in 32x32 container), label, AnimatedKnobControl (100x100), VuMeterControl (14x80), volume %, target/curve/range controls
  - Controls section in recessed sub-panel (#181818 bg, 1px top border #2A2A2A)

- [ ] Implement icon resolution with caching:
  1. Check `Assets/Icons/` embedded resources by target name
  2. Fall back to `SHGetFileInfo` P/Invoke for process icon → `Imaging.CreateBitmapSourceFromHIcon()`
  3. Cache in `Dictionary<string, ImageSource>`
  4. Generic waveform icon as final fallback

- [ ] Wire single `DispatcherTimer` (50ms) for all live updates:
  - Poll `AudioMixer.GetVolume()` for knob positions
  - Poll `AudioMixer.GetPeakLevel()` for VU meters
  - Guard: `if (!IsVisible) return;`

**Design polish (Frontend Design Skill):**
- [ ] Volume percentage: always 3-char width (` 0%`, `42%`, `100%`), use tabular/monospace font
- [ ] Mute state: dim entire strip (opacity 0.4), show "MUTE" badge in #FF4444
- [ ] Equal-width channel strips via `UniformGrid` — never auto-size
- [ ] 16-20px gutters between strips
- [ ] Device preview strip (24px tall) showing mini knob positions + LED glow dots

**Built-in icon pack (~15 icons, 64x64 PNG, transparent background):**

| Icon file | Matches target |
|-----------|---------------|
| `master.png` | `master` |
| `mic.png` | `mic` |
| `system.png` | `system` |
| `active.png` | `active_window` |
| `monitor.png` | `monitor` |
| `discord.png` | `discord` |
| `spotify.png` | `spotify` |
| `chrome.png` | `chrome` |
| `firefox.png` | `firefox` |
| `steam.png` | `steam` |
| `obs.png` | `obs` |
| `vlc.png` | `vlc` |
| `teams.png` | `teams` |
| `slack.png` | `slack` |
| `generic.png` | fallback |

**Success criteria:** Mixer view shows 5 live channel strips. Physical knob turn animates on-screen knob and VU meter within ~50ms. App icons display correctly.

---

#### Phase 4: Polish + Cleanup + WinForms Removal

- [ ] Page transition animations: stagger-fade columns left-to-right (40ms delay per column, 200ms duration)
- [ ] Knob value animation: 60-80ms ease-out transition on value change
- [ ] Connection status pill: cyan pulse animation (opacity 1.0→0.4→1.0, 2s cycle) when connected, static gray when disconnected
- [ ] Sidebar icons: glow on hover (soft cyan shadow), 200ms transition
- [ ] Hover states on controls: border brightens #363636→#00B4D8 on hover, 150ms
- [ ] Noise texture overlay: 2-3% opacity across form background for analog warmth
- [ ] Remove `<UseWindowsForms>true</UseWindowsForms>` from csproj — confirm `SHGetFileInfo` replaces `Icon.ExtractAssociatedIcon`
- [ ] Delete old WinForms files: `TrayApp.cs`, `Program.cs`, `ConfigForm.cs`, `IntegrationSetupForm.cs`, old `AnimatedKnobControl.cs`, old `VuMeterControl.cs`
- [ ] DPI testing on LG QHD and OMEN 35 monitors
- [ ] Mica fallback verification (solid #141414 on Windows 10)
- [ ] Test all 17 button actions, all 9 LED effects, all knob targets with physical hardware
- [ ] Verify config.json backward compatibility (load old config, save new, reload)
- [ ] Update `CLAUDE.md` with new file structure and WPF patterns
- [ ] Update `deploy.bat` if build output path changes

**Success criteria:** Visually polished, all features working, no regressions from WinForms, config backward compatible, WinForms fully removed.

---

## Design Specification (Frontend Design Skill)

### Typography
- **Branding:** JetBrains Mono or IBM Plex Mono for "WOLFMIXER" wordmark — mechanical instrument-panel feel
- **Body/UI:** Segoe UI (Windows default, pairs well with wpfui) for controls, menus, labels
- **Numeric readouts:** Monospace/tabular figures for volume %, Hz, values — prevents horizontal jitter as digits change
- **Font scale:** Header branding 14px semibold, channel labels 12px medium, knob percentages 18px bold (hero numbers), sub-controls 11px regular. Max 3 sizes per view.

### Spacing (8px Grid)
- Channel strip gutters: 16-20px
- Icon-to-knob: 16px, knob-to-percentage: 8px, percentage-to-VU: 12px
- Card/panel padding: 16px all sides, inner sections 12px
- Sidebar: 56px wide, 32px icons, 12px vertical spacing, 3px accent bar on active
- Header: 48px height, 16px horizontal padding, branding left, status right, middle empty

### Visual Hierarchy
1. **Heroes:** Knob arcs + VU meters — brightest, most detailed, cyan glow (#00E5FF)
2. **Secondary:** Channel names + percentages — #E8E8E8
3. **Tertiary:** Labels, controls — #9A9A9A
4. **Cyan accent appears sparingly:** Active sidebar icon, knob arcs, connection status dot, selected tab. Not everywhere.
5. **Depth:** Base (#0F0F0F) → Form (#141414) → Cards (#1C1C1C) → Inputs (#242424) — 4-layer depth system

### Micro-Interactions
- **Tab switch:** Stagger-fade columns left-to-right (40ms delay per column, 200ms duration) — the signature animation
- **Knob rotation:** 60-80ms ease-out following hardware knob. Never snap.
- **VU segments:** 10ms stagger per segment lighting up, smooth decay (not snap off), peak hold fades 400ms
- **Connection dot:** Cyan pulse (2s cycle) when connected. Static gray when disconnected.
- **Hover:** Border brightens to accent (150ms). Sidebar icon gets soft cyan glow (200ms).
- **No bouncing, elastic, or playful motion.** Studio hardware — smooth, damped, precise.

### Polish Details
- Noise texture overlay (2-3% opacity) across background — analog warmth, not dead LCD
- LED glow rings on device preview: radial gradient, knob color, 60% center → 0% at 8px
- Mute state dims entire strip (opacity 0.4) + #FF4444 "MUTE" badge
- VU color transitions: 10 green, 2 transitional (green-yellow blend), 3 yellow, 1 red — like real LED meters
- App icons: 24x24 in 32x32 container, 4px padding, circular #242424 background

---

## Alternative Approaches Considered

| Approach | Pros | Cons | Why rejected |
|----------|------|------|-------------|
| **Full MVVM** | Testable, separation of concerns | Overkill for single-user tray app, 20+ files | Complexity without benefit (simplicity reviewer) |
| **Avalonia** | Cross-platform, Skia rendering | No native Mica, smaller ecosystem | Windows-only project (WASAPI, COM, DDC/CI) |
| **MAUI Blazor Hybrid** | HTML/CSS flexibility | Heavy runtime, less native feel | Not appropriate for a utility |
| **Enhanced WinForms** | Less work | GDI+ ceiling, no blur/transparency | Already maxed visual potential |
| **Electron / Tauri** | Web UI flexibility | Huge bundle, performance overhead | Wrong tool for low-latency audio mixer |

## Acceptance Criteria

### Functional Requirements

- [ ] All 5 knobs display live volume with animated knob controls and real VU meters (peak levels, not volume setting)
- [ ] All knob target types work: master, mic, system, any, active_window, output_device, input_device, monitor, process name match, ha_*, fanctrl:*
- [ ] All 3 response curves (Linear, Logarithmic, Exponential) and min/max volume range work
- [ ] All 17 button actions configurable across 3 gestures (tap, double, hold) per button
- [ ] All 9 LED effects configurable with dual colors and speed
- [ ] Profile save/load/create/switch works
- [ ] System tray icon with live volume display
- [ ] config.json backward compatible — old config loads correctly in new UI
- [ ] Home Assistant and Fan Control integrations accessible from Settings
- [ ] HA token encrypted at rest with DPAPI
- [ ] Start with Windows toggle functional

### Non-Functional Requirements

- [ ] Window renders with Mica backdrop on Windows 11 22H2+
- [ ] Graceful fallback to solid dark background on Windows 10
- [ ] VU meters update at 20+ FPS without UI jank
- [ ] Physical knob turns reflected in UI within ~50ms
- [ ] App uses < 80MB RAM at idle
- [ ] Clean shutdown — no ghost tray icons, no orphan processes
- [ ] Crisp rendering on high-DPI displays (per-monitor DPI V2)
- [ ] Zero GDI+/brush allocations in render loops (all pre-allocated and frozen)

### Quality Gates

- [ ] All knob targets verified with physical hardware
- [ ] All button actions verified with physical hardware
- [ ] All LED effects verified with physical hardware
- [ ] Profile round-trip: save → switch away → switch back → all settings preserved
- [ ] Disconnect/reconnect cycle: unplug USB → tray goes gray → replug → tray goes cyan → LEDs restore
- [ ] Config migration: load config.json from current WinForms build → all values populate correctly
- [ ] VU meter timer stops when window hidden, restarts on show

## Dependencies & Prerequisites

| Dependency | Version | Purpose |
|-----------|---------|---------|
| .NET 8 SDK | 8.0+ | Runtime |
| WPF-UI (wpfui) | **4.x** | FluentWindow, Mica, NavigationView, dark theme |
| H.NotifyIcon.Wpf | 2.x | System tray icon |
| NAudio | 2.2.1 | WASAPI audio (existing) |
| Newtonsoft.Json | 13.0.3 | Config serialization (existing) |
| System.IO.Ports | 8.0.0 | Serial communication (existing) |
| Windows 11 22H2+ | Recommended | Mica backdrop (falls back gracefully) |

**Dropped:** CommunityToolkit.Mvvm (not needed without MVVM pattern)

## Risk Analysis & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **Application lifecycle cutover** | App won't start | **HIGH** | The switch from `Application.Run(new TrayApp())` to WPF `App.xaml` is atomic. Build WPF tray icon in parallel, flip in one commit. Test thoroughly. |
| **COM threading for peak metering** | Crashes | Medium | Query `AudioMeterInformation` using `_enumerator` + `_enumLock`. Don't cache device refs across calls. |
| **WPF custom control rendering** | Visual bugs | Medium | Build `AnimatedKnobControl` as isolated spike first. The `DrawArc` → `ArcSegment` translation is error-prone. |
| Mica not available on target PC | Visual degradation | Low | wpfui handles fallback automatically |
| wpfui v4 breaking changes | Build failures | Low | Pin exact version |
| Ghost tray icon on crash | Annoyance | Medium | Dispose `TaskbarIcon` in `App.OnExit`, use try/finally |
| Serial write contention | Corrupted RGB frames | Low | Add lock around `RgbController.Send()` |
| Namespace ambiguity (WPF + WinForms loaded) | Compile errors | Low | Use fully qualified names during transition |
| `FluentWindow.Show()` after `Hide()` may reset Mica | Visual glitch | Low | Test early in Phase 1. If broken, use `Visibility` toggle instead. |

## Future Considerations

- **Interactive knobs** — allow dragging on-screen knobs (would need "software override" mode)
- **Audio visualizer** — waveform or spectrum display per channel
- **Per-app routing matrix** — Elgato-style horizontal routing view
- **Theme customization** — user-selectable accent colors (resource dictionary swap)
- **OBS integration** — scene switching, source mute via obs-websocket
- **Multiple device support** — detect and manage multiple Turn Up units
- **USB PnP auto-detection** — scan for CH343 devices instead of hardcoded COM3
- **Consolidate MMDeviceEnumerator** — 3 separate instances exist in AudioMixer, ButtonHandler, TrayApp. Share one. (Pattern Recognition)
- **Extract active-window audio lookup** — duplicated between AudioMixer and ButtonHandler. (Pattern Recognition)

## References & Research

### Internal References

- Brainstorm: `docs/brainstorms/2026-03-08-wpf-ui-redesign-brainstorm.md`
- Current UI: `ConfigForm.cs` (1800+ lines, complete rewrite target)
- Backend API surface: `SerialReader.cs`, `AudioMixer.cs`, `ButtonHandler.cs`, `RgbController.cs`
- Config model: `Config.cs` (AppConfig, KnobConfig, ButtonConfig, LightConfig)
- Current tray: `TrayApp.cs` (orchestrator + tray icon)
- WinForms custom controls: `AnimatedKnobControl.cs`, `VuMeterControl.cs`
- Integrations: `HAIntegration.cs`, `FanController.cs`, `IntegrationSetupForm.cs`
- Color palette: `ConfigForm.cs:13-24` and `CLAUDE.md` Color Palette Reference

### External References

- [WPF-UI v4.x documentation](https://wpfui.lepo.co/)
- [WPF-UI GitHub](https://github.com/lepoco/wpfui) — v4.2.0, Jan 2025
- [WPF-UI v4 Migration Guide](https://wpfui.lepo.co/migration/v4-migration.html)
- [H.NotifyIcon.Wpf GitHub](https://github.com/HavenDV/H.NotifyIcon)
- [WPF Mica backdrop sample](https://github.com/Difegue/Mica-WPF-Sample)
- [DWM_SYSTEMBACKDROP_TYPE API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [WPF animation best practices](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/animation-overview)
- [ArcSegment drawing in WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-create-an-elliptical-arc)
- [WPF 2D Graphics Performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging)
- [Per-monitor DPI for .NET 8 WPF](https://learn.microsoft.com/en-us/answers/questions/2033882/)
- [StreamGeometry vs PathGeometry](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-create-a-shape-using-a-streamgeometry)
- [Reducing Memory Usage in WPF](https://moldstud.com/articles/p-reducing-memory-usage-in-wpf-case-studies-and-success-stories-for-optimal-performance)

### Design Inspiration

- Elgato Wave Link 3.0 — clean channel layout, signal-flow clarity
- GoXLR App — channel photos/icons, hardware visualization
- SteelSeries Sonar — peak meters in sliders, drag-and-drop routing
- Discord Settings — sidebar navigation, dark glassmorphic cards
- Teenage Engineering — precision instrument aesthetic
