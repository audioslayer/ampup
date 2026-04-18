# Codex Working Notes

These are my condensed working notes derived from `CLAUDE.md` for this repo.

## Project Shape

- AmpUp is a C# `.NET 8` WPF app with a code-behind architecture, not MVVM.
- UI stack uses `WPF-UI`, `FluentWindow`, Mica backdrop, and a dark glassmorphism style.
- Shared cross-platform logic lives in `AmpUp.Core/`.
- Windows build assumptions matter; WPF cannot be built on Linux.

## How I Should Work Here

- Prefer existing code-behind patterns over introducing MVVM abstractions.
- Preserve the current visual language instead of restyling toward generic defaults.
- Treat `ThemeManager` and `DynamicResource` as the source of truth for themed backgrounds and cards.
- Be careful with parallel edits across caller/callee APIs; verify interfaces line up before finishing.

## WPF Gotchas

- Fully qualify WPF types when `Wpf.Ui.Controls` creates namespace conflicts.
- Never use `AllowsTransparency=true` on popup/dialog windows.
- Do not try to use unsupported WPF properties like `LetterSpacing`.
- Avoid emoji-based UI because WPF renders them poorly.
- Popup-style hover logic is fragile across HWND boundaries; this repo often uses borderless windows instead.
- Any OSD/UI work triggered from serial/background threads must be marshaled onto the `Dispatcher`.

## Theme Rules

- Do not hardcode gray background hex values for cards and surfaces.
- Card theme colors are runtime-swapped through `DynamicResource`.
- Some render-path brushes/pens are frozen statics for performance; theme changes may require rebuild hooks instead of mutation.

## Runtime / Config Rules

- Runtime `config.json` lives next to the built `.exe`, not in the repo root.
- JSON is camelCase on disk while in-memory models use PascalCase with Newtonsoft.
- The app is single-instance, so stale processes can affect testing.

## Hardware / Protocol Notes

- Turn Up uses CH343 USB-to-serial, usually on `COM3`, at `115200`.
- Frames are wrapped with `FE` start and `FF` end.
- The device does not auto-send knob positions on connect; send `FE 01 FF` to request device info and knob batch state.
- RGB writes are `FE 05 [45 RGB bytes] FF` for 5 knobs x 3 LEDs.
- Default gamma should stay `1.0` unless calibration explicitly changes it.

## Audio / Device Notes

- Audio session matching has edge cases: UWP/MS Store helper processes, Discord multi-session, and low WASAPI peak levels.
- `AudioMixer` logic that skips AmpUp's own PID is important for active-window targeting.
- Low audio meter values are expected; sensitivity/boost code may look odd for a reason.

## Release / Build Notes

- Installer flow is manual-oriented and versioning matters across Windows and Mac projects.
- Framework-dependent Windows builds are intentional.

## Style Adjustments I Should Make

- Be a little more explicit about repo-specific constraints before making structural changes.
- Default toward surgical edits that fit existing patterns.
- Mention WPF/threading/theme pitfalls early when they are relevant to a task.
