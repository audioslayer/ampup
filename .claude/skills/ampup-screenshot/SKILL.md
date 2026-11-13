---
name: ampup-screenshot
description: Capture screenshots of the running AmpUp app to see the real UI (Mixer, Buttons, Lights, Room, Groups, OSD, Overview, Settings). Use when reviewing or changing any AmpUp UI/XAML/view code, checking a layout, comparing before/after, or when the user says "look at the app" / "screenshot".
---

# AmpUp screenshot server

AmpUp has a dev-only HTTP endpoint (`Services/DevScreenshotServer.cs`) that renders the main
window to PNG. It runs **only** when AmpUp was started by `deploy.bat`, which passes
`--dev-screenshots`. Normal installs and Start-with-Windows launches never open the port.

- Address: `http://127.0.0.1:47823` (loopback only)
- The app runs from `Z:\Projects\ampup\bin\Debug\net8.0-windows\AmpUp.exe` on this PC.

## Don't build or launch it yourself

The user runs `deploy.bat` (pull → build → kill old → launch). Your loop is:

1. Make the code change, commit and push.
2. Ask the user to run `deploy.bat`.
3. Capture screenshots and review them.

Don't run `deploy.bat`, kill AmpUp or start the exe. The user owns that step.
`dotnet build AmpUp.csproj -o <scratchpad>\build` is fine for a compile check, because it
doesn't touch the running app's `bin\Debug`.

## Check it's up

```bash
curl -s --max-time 3 http://127.0.0.1:47823/health      # → ok
curl -s http://127.0.0.1:47823/pages                     # → ["mixer","buttons",...]
```

No answer means AmpUp wasn't launched via `deploy.bat`, or it's an older build without the
server. Ask the user to run `deploy.bat`.

## Capture

Save into your scratchpad directory, then open the PNG with the Read tool. Read shows images.

```bash
curl -s "http://127.0.0.1:47823/screenshot?page=mixer" -o "<scratchpad>/mixer.png"
```

Query parameters (all optional):

| Param | Meaning |
|-|-|
| `page` | `mixer`, `buttons`, `lights`, `room`, `groups`, `osd`, `overview`, `settings`. Navigates exactly like clicking the sidebar. Omit it to capture the current page. |
| `scroll` | Vertical offset in DIPs for the page's scroll viewer, e.g. `scroll=800` for lower sections. |
| `w`, `h` | Resize the window first, in DIPs, e.g. `w=1440&h=900`. This changes the user's window size, so only use it when testing layout breakpoints. |
| `delay` | Extra settle time in ms before capturing (default 450, max 5000). Raise it for pages with entrance animations. |

## Tips

- A capture brings AmpUp out of the tray and switches its page. Mention this if the user
  is using the app at the same time.
- For long pages, take several shots: `scroll=0`, `scroll=700`, `scroll=1400`.
- For before/after comparisons, capture first, make the change, have the user run
  `deploy.bat`, then capture again with the same params.
- The window background (Mica) is painted with the theme's `BgBaseBrush`, so shots are
  opaque.
- Errors come back as HTTP 500 with a plain-text message, such as an unknown page name.
  Run `curl -s` without `-o` to read it.
