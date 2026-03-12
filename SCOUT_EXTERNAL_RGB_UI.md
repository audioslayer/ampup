# External RGB View — UI & Architecture Spec

> Scouted 2026-03-12. This document is the full implementation spec for the External RGB sidebar view. Hand directly to a coding agent.

---

## 1. Sidebar Nav Item

### Where to add it (MainWindow.xaml)

Insert a `NavExtRgb` button in the `StackPanel` inside `DockPanel Grid.Row="1"`, **between `NavHA` and `NavSettings`** (or after NavHA). Follow the exact same `Button` + `Grid` + `Border` (indicator bar) + `StackPanel` pattern used by all other nav items.

```xml
<Button x:Name="NavExtRgb" Style="{StaticResource NavButton}" Click="NavExtRgb_Click">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="3" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Border x:Name="NavExtRgbBar" Grid.Column="0" Width="3" Height="20" CornerRadius="2"
                Background="{DynamicResource AccentBrush}" VerticalAlignment="Center"
                Visibility="Collapsed" Margin="-4,0,0,0" />
        <StackPanel Grid.Column="1" HorizontalAlignment="Center">
            <ui:SymbolIcon Symbol="LightbulbFilament24" FontSize="22"
                           Foreground="{StaticResource TextSecBrush}" HorizontalAlignment="Center" />
            <TextBlock x:Name="NavExtRgbLabel" Text="Room RGB" FontSize="9"
                       Foreground="{StaticResource TextSecBrush}"
                       HorizontalAlignment="Center" Margin="0,3,0,0" />
        </StackPanel>
    </Grid>
</Button>
```

**Icon:** `LightbulbFilament24` — fits "room lighting" semantics better than `Color24` (already used by Lights tab) or `Bluetooth24`.
**Label:** `"Room RGB"` — short enough for 76px sidebar, unambiguous. Alternatives: `"Ext RGB"`, `"Room Sync"`. Recommend `"Room RGB"`.

### MainWindow.xaml.cs changes

1. Add field: `private readonly ExternalRgbView _extRgbView = new();`
2. Add to `GetNavBars()` dictionary: `{ NavExtRgb, NavExtRgbBar }`
3. Add click handler:
   ```csharp
   private void NavExtRgb_Click(object sender, RoutedEventArgs e)
   {
       _extRgbView.LoadConfig(_config, cfg =>
       {
           _config = cfg;
           _onConfigChanged?.Invoke(cfg);
       });
       NavigateTo(_extRgbView, NavExtRgb);
   }
   ```
4. Call `_extRgbView.LoadConfig(_config, saveHandler)` inside `RefreshViews()`.
5. Pass `ExternalRgb` config through `PreserveGlobalSettings()` (add alongside `HomeAssistant`):
   ```csharp
   loaded.ExternalRgb = _config.ExternalRgb;
   ```

---

## 2. ExternalRgbView Layout

### File structure
- `Views/ExternalRgbView.xaml` — XAML skeleton (mirrors HomeAssistantView.xaml pattern — mostly empty named Grids, UI built in code-behind)
- `Views/ExternalRgbView.xaml.cs` — all UI construction in code-behind

### XAML skeleton (ExternalRgbView.xaml)

```xml
<UserControl x:Class="AmpUp.Views.ExternalRgbView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">

    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="0,0,0,24">

            <!-- Govee section card — content built in code-behind -->
            <Border x:Name="GoveeCard" Style="{StaticResource CardPanel}" Margin="0,0,0,12">
                <Grid x:Name="GoveeContent" />
            </Border>

            <!-- OpenRGB section card — content built in code-behind -->
            <Border x:Name="OpenRgbCard" Style="{StaticResource CardPanel}" Margin="0,0,0,12">
                <Grid x:Name="OpenRgbContent" />
            </Border>

            <!-- Sync Settings card — content built in code-behind -->
            <Border x:Name="SyncCard" Style="{StaticResource CardPanel}" Margin="0,0,0,12">
                <Grid x:Name="SyncContent" />
            </Border>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

### Code-behind structure (ExternalRgbView.xaml.cs)

```csharp
public partial class ExternalRgbView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Govee controls
    private CheckBox _goveeEnabled = null!;
    private Border _goveeStatusDot = null!;
    private TextBlock _goveeStatusLabel = null!;
    private Button _goveeScanBtn = null!;
    private StackPanel _goveeDeviceList = null!;  // populated after scan

    // OpenRGB controls
    private CheckBox _openRgbEnabled = null!;
    private TextBox _openRgbHost = null!;
    private TextBox _openRgbPort = null!;
    private Button _openRgbConnectBtn = null!;
    private Border _openRgbStatusDot = null!;
    private TextBlock _openRgbStatusLabel = null!;
    private StackPanel _openRgbDeviceList = null!; // populated after connect

    // Sync settings controls
    private Slider _brightnessSlider = null!;
    private TextBlock _brightnessValue = null!;
    private CheckBox _warmToneShift = null!;

    // Section header elements (for accent color refresh)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    public ExternalRgbView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Save(); };
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);
        BuildGoveeCard();
        BuildOpenRgbCard();
        BuildSyncCard();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave) { ... }
}
```

---

### 2a. Govee Card

**Purpose:** Govee lights expose a LAN API (UDP on port 4001/4002) for local color control. Discovery sends a broadcast, devices respond with their IP and device info.

**Card header pattern** (matches HomeAssistantView's `BuildConnectionCard`):
```
[3px accent bar]  GOVEE                    [status dot] [status text]
```

**Contents (top to bottom):**

1. **Header row** — accent bar + `"GOVEE"` header + status dot + status label
2. **Enable toggle** — `CheckBox` labeled `"Enable Govee LAN sync"` (bound to `config.ExternalRgb.GoveeEnabled`)
3. **Scan button row** (visible only when enabled):
   - `Button` `"Scan Network"` — broadcasts UDP discovery, populates device list
   - Status label: `"No devices found"` / `"3 devices found"` / `"Scanning..."`
4. **Device list** (`StackPanel _goveeDeviceList`) — one row per discovered Govee device:
   - Device name + IP (e.g. `"H6054 — 192.168.1.42"`)
   - `ComboBox` for sync mode: `["Off", "Mirror Global", "Mirror Knob 1", "Mirror Knob 2", "Mirror Knob 3", "Mirror Knob 4", "Mirror Knob 5"]`
   - Persisted in `config.ExternalRgb.GoveeDevices` list by device IP

**Govee device row layout:**
```
[device name/IP]                     [sync mode combo ▾]
```

**Status dot colors:** grey = disabled, yellow = scanning, green = found devices, red = error

---

### 2b. OpenRGB Card

**Purpose:** OpenRGB is a local server (SDK server, default port 6742) that controls any RGB hardware. AmpUp connects as a client over TCP, lists devices, and pushes colors.

**Card header pattern:**
```
[3px accent bar]  OPENRGB                  [status dot] [status text]
```

**Contents (top to bottom):**

1. **Header row** — accent bar + `"OPENRGB"` header + status dot + status label
2. **Enable toggle** — `CheckBox` labeled `"Enable OpenRGB sync"`
3. **Connection row** (two `TextBox` side by side + connect button, visible when enabled):
   - `TextBox _openRgbHost` — label `"HOST"`, placeholder `"localhost"`, width 200
   - `TextBox _openRgbPort` — label `"PORT"`, placeholder `"6742"`, width 100
   - `Button "Connect"` (primary appearance) — triggers TCP connect attempt
   - `Button "Disconnect"` (secondary, shown when connected)
4. **Device list** (`StackPanel _openRgbDeviceList`) — populated after successful connect:
   - One row per OpenRGB device (e.g. `"Corsair Keyboard"`, `"ASUS Motherboard"`)
   - `ComboBox` for sync mode: same options as Govee (`["Off", "Mirror Global", "Mirror Knob 1" ... "Mirror Knob 5"]`)
   - `CheckBox "Sync all zones"` — if checked, all zones on the device get the same color; if unchecked, a zone index selector appears (for multi-zone devices like keyboards with separate underglow)
   - Persisted in `config.ExternalRgb.OpenRgbDevices` by device index (integer from OpenRGB device list)

**Status dot colors:** grey = disabled/disconnected, yellow = connecting, green = connected, red = error

**Device row layout:**
```
[device name]                    [sync mode ▾]   [☑ all zones]
```

---

### 2c. Sync Settings Card

**Contents (top to bottom):**

1. **Header row** — accent bar + `"SYNC SETTINGS"`
2. **Brightness scale slider:**
   - Label `"Room Brightness Scale"` with tooltip `"Scales all sent colors — room lights often look better at lower intensity"`
   - `Slider` 0–100, tick marks at 25/50/75, value label `"75%"` next to it
   - Default: 75 (room lights at full 100% are often overwhelming)
   - Bound to `config.ExternalRgb.BrightnessScale`
3. **Warm tone shift checkbox:**
   - `CheckBox "Warm tone shift"` — label `"Boost reds/shift cool colors warmer (matches LED bulb color temperature)"`
   - When enabled: apply a subtle warm shift before sending (multiply R by 1.15, divide B by 1.15, clamp 0-255)
   - Bound to `config.ExternalRgb.WarmToneShift`
4. **Update rate note** — `TextBlock` (dim secondary text): `"External sync runs at 20 FPS alongside device LEDs"`

---

## 3. Config Schema Additions

### New config class in Config.cs

Add **after** `HomeAssistantConfig`:

```csharp
public class ExternalRgbConfig
{
    // Govee LAN sync
    public bool GoveeEnabled { get; set; } = false;
    public List<GoveeDeviceConfig> GoveeDevices { get; set; } = new();

    // OpenRGB SDK sync
    public bool OpenRgbEnabled { get; set; } = false;
    public string OpenRgbHost { get; set; } = "localhost";
    public int OpenRgbPort { get; set; } = 6742;
    public List<OpenRgbDeviceConfig> OpenRgbDevices { get; set; } = new();

    // Shared sync settings
    public int BrightnessScale { get; set; } = 75;  // 0-100
    public bool WarmToneShift { get; set; } = false;
}

public class GoveeDeviceConfig
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public string SyncMode { get; set; } = "off";  // "off" | "global" | "knob0" | "knob1" | "knob2" | "knob3" | "knob4"
}

public class OpenRgbDeviceConfig
{
    public int DeviceIndex { get; set; }
    public string Name { get; set; } = "";
    public string SyncMode { get; set; } = "off";
    public bool SyncAllZones { get; set; } = true;
    public int ZoneIndex { get; set; } = 0;
}
```

### Add to AppConfig class (Config.cs)

```csharp
// External RGB sync (Govee LAN, OpenRGB SDK)
public ExternalRgbConfig ExternalRgb { get; set; } = new();
```

**JSON representation (config.json additions):**
```json
"externalRgb": {
  "goveeEnabled": false,
  "goveeDevices": [
    { "ip": "192.168.1.42", "name": "H6054", "syncMode": "global" }
  ],
  "openRgbEnabled": false,
  "openRgbHost": "localhost",
  "openRgbPort": 6742,
  "openRgbDevices": [
    { "deviceIndex": 0, "name": "Corsair Keyboard", "syncMode": "knob0", "syncAllZones": true, "zoneIndex": 0 }
  ],
  "brightnessScale": 75,
  "warmToneShift": false
}
```

Newtonsoft.Json serializes C# PascalCase → JSON camelCase automatically (as with all existing fields), so no custom converter needed.

### PreserveGlobalSettings in MainWindow.xaml.cs

Add to `PreserveGlobalSettings(AppConfig loaded)`:
```csharp
loaded.ExternalRgb = _config.ExternalRgb;
```
ExternalRgb is a global integration setting (not per-profile), same treatment as `HomeAssistant` and `Serial`.

---

## 4. RgbController Hook Points

### Where to attach external sync

**Hook location: end of `Tick()` in RgbController.cs (line ~311)**

The `Tick()` method runs every 50ms on a `System.Threading.Timer` thread. It calls `UpdateEffects()` (which fills `_colorMsg` buffer) then `Send()` (which writes the buffer to the serial port). After `Send()`, the `_colorMsg` buffer contains the finalized, gamma-corrected, brightness-adjusted RGB data for all 15 LEDs.

**The correct hook is: after `Send()`, read back pre-gamma values and push to external sync.**

However, `_colorMsg` at this point contains *post-gamma* bytes, which are perceptually compressed (gamma table is nonlinear). External RGB controllers expect linear sRGB 0-255. Therefore the hook should capture colors **before gamma is applied** — or extract them by applying inverse gamma. The cleanest approach:

**Add a parallel `_linearColors` buffer to RgbController:**

```csharp
// Linear RGB buffer (pre-gamma, post-brightness) — exposed for external sync
private readonly byte[] _linearColors = new byte[15 * 3]; // 15 LEDs × RGB
```

In `SetColor(int knobIdx, int ledIdx, int r, int g, int b)` — store the post-brightness, pre-gamma values:
```csharp
// Before gamma, store in linear buffer for external sync consumers
int globalLedIdx = knobIdx * 3 + ledIdx;
_linearColors[globalLedIdx * 3 + 0] = (byte)Math.Clamp(r * _brightness / 100, 0, 255);
_linearColors[globalLedIdx * 3 + 1] = (byte)Math.Clamp(g * _brightness / 100, 0, 255);
_linearColors[globalLedIdx * 3 + 2] = (byte)Math.Clamp(b * _brightness / 100, 0, 255);
```

**Expose via callback delegate (not direct coupling):**

```csharp
/// <summary>
/// Called after each frame is computed with the linear RGB colors for all 15 LEDs.
/// Parameter: byte[45] — knob0LED0R, knob0LED0G, knob0LED0B, knob0LED1R...
/// External sync consumers (Govee, OpenRGB) attach here.
/// </summary>
public Action<byte[]>? OnFrameReady;
```

At the end of `Tick()`:
```csharp
private void Tick()
{
    _animTick++;

    if (_transitionTick >= 0)
    {
        RenderTransition();
        _transitionTick++;
        if (_transitionTick >= TransitionDuration)
            _transitionTick = -1;
        Send();
        OnFrameReady?.Invoke(_linearColors);  // <-- hook
        return;
    }

    UpdateEffects();
    Send();
    OnFrameReady?.Invoke(_linearColors);  // <-- hook
}
```

### ExternalRgbSync class (new file: ExternalRgbSync.cs)

Create a new class `ExternalRgbSync` that:
- Holds `ExternalRgbConfig` reference
- Subscribes to `RgbController.OnFrameReady`
- On each frame: reads config, computes the correct color per device, sends via UDP (Govee) or TCP (OpenRGB)

**Lifecycle in App.xaml.cs:**
```csharp
private ExternalRgbSync? _externalRgb;

// In OnConnected / initialization (after _rgb = new RgbController()):
_externalRgb = new ExternalRgbSync(_config.ExternalRgb);
_rgb.OnFrameReady += _externalRgb.OnFrame;

// When config changes (in ApplyRgbConfig or equivalent):
_externalRgb?.UpdateConfig(_config.ExternalRgb);

// On shutdown:
_externalRgb?.Dispose();
```

This keeps external sync completely decoupled from RgbController internals.

### Color derivation per sync mode

`ExternalRgbSync.OnFrame(byte[] linear45)` maps sync modes to output colors:

| SyncMode | Color computation |
|---|---|
| `"off"` | Send nothing / black |
| `"global"` | Average all 15 LED colors: `(sum R[0..14]/15, sum G/15, sum B/15)` |
| `"knob0"` | Average 3 LEDs for knob 0: indices 0–2 |
| `"knob1"` | Average 3 LEDs for knob 1: indices 3–5 |
| `"knob2"` | Average 3 LEDs for knob 2: indices 6–8 |
| `"knob3"` | Average 3 LEDs for knob 3: indices 9–11 |
| `"knob4"` | Average 3 LEDs for knob 4: indices 12–14 |

Then apply `BrightnessScale` (multiply R/G/B by `scale/100`) and optionally `WarmToneShift` before sending.

### Govee LAN API protocol (for ExternalRgbSync)

Govee LAN control uses UDP:
- **Discovery:** broadcast `{"msg":{"cmd":"scan","data":{"account_topic":"reserve"}}}` to `239.255.255.250:4001`, devices respond on `4002`
- **Set color:** unicast to device IP port `4001`: `{"msg":{"cmd":"colorwc","data":{"color":{"r":255,"g":0,"b":0},"colorTemInKelvin":0}}}`
- **Rate limit:** max ~10-15 packets/sec per device. Since AmpUp runs 20fps with potentially multiple devices, throttle: only send if color changed more than a threshold (delta > 5 on any channel) OR every 500ms as keepalive.

### OpenRGB SDK protocol (for ExternalRgbSync)

The OpenRGB SDK uses a simple TCP binary protocol on port 6742. Rather than implementing raw sockets, use the **OpenRGB.NET** NuGet package (already well-maintained, MIT license). If adding a NuGet dependency is undesirable, the protocol is:
- Magic header: `OPENRGB`
- Packet header: device index (uint32), packet id (uint32), data size (uint32)
- `NET_PACKET_ID_SET_CLIENT_NAME = 50` — identify client
- `NET_PACKET_ID_REQUEST_CONTROLLER_COUNT = 0` — get device count
- `NET_PACKET_ID_REQUEST_CONTROLLER_DATA = 1` — get device info (name, zones, leds)
- `NET_PACKET_ID_RGBCONTROLLER_UPDATELEDS = 1050` — set all LEDs for a device

**Recommended:** Use `OpenRGB.NET` NuGet package. Add to `AmpUp.csproj`:
```xml
<PackageReference Include="OpenRGB.NET" Version="3.*" />
```

---

## 5. ExternalRgbView Code-Behind: Full Build Pattern

### BuildGoveeCard()

```
Grid layout (row-by-row):
  Row 0: Header row (StackPanel: accent bar + "GOVEE" + status dot + status label)
  Row 1: Enable checkbox
  Row 2: Scan button row (Visibility=Collapsed when disabled)
  Row 3: Device list StackPanel (Visibility=Collapsed when no devices)
```

Checkbox `_goveeEnabled.Checked/Unchecked` → toggle row 2/3 visibility + `QueueSave()`
Scan button → `async` handler: set status "Scanning...", send UDP broadcast, populate `_goveeDeviceList`, update status label

Device list row (per device):
```
StackPanel (Orientation=Horizontal):
  TextBlock "[name] — [ip]" (TextSec style, flex width)
  ComboBox (sync mode, 120px wide)
```

### BuildOpenRgbCard()

```
Grid layout:
  Row 0: Header row (accent bar + "OPENRGB" + status dot + status label)
  Row 1: Enable checkbox
  Row 2: Connection row (host textbox + port textbox + connect/disconnect buttons)
  Row 3: Device list StackPanel
```

Connect button → `async` handler: attempt TCP connect via OpenRGB.NET, populate device list on success, update status

Device list row (per device):
```
StackPanel (Orientation=Vertical):
  StackPanel (Orientation=Horizontal):
    TextBlock device name (flex)
    ComboBox sync mode
  StackPanel (Orientation=Horizontal, Visibility based on sync mode != "off"):
    CheckBox "All zones"
    (if unchecked) NumericUpDown/TextBox zone index
```

### BuildSyncCard()

```
Grid layout:
  Row 0: Header row (accent bar + "SYNC SETTINGS")
  Row 1: Brightness scale row
  Row 2: Warm tone checkbox
  Row 3: Update rate note
```

Brightness row:
```
TextBlock "Room Brightness Scale"
StackPanel (Horizontal):
  Slider (0-100, Width=*, Margin=0,0,12,0)
  TextBlock "75%" (40px wide, right-align, live-updating)
```

### Save() method

```csharp
private void Save()
{
    if (_config == null || _onSave == null || _loading) return;
    var cfg = _config.ExternalRgb;

    cfg.GoveeEnabled = _goveeEnabled.IsChecked == true;
    cfg.OpenRgbEnabled = _openRgbEnabled.IsChecked == true;
    cfg.OpenRgbHost = _openRgbHost.Text.Trim();
    if (int.TryParse(_openRgbPort.Text, out int port)) cfg.OpenRgbPort = port;
    cfg.BrightnessScale = (int)_brightnessSlider.Value;
    cfg.WarmToneShift = _warmToneShift.IsChecked == true;

    // Govee device sync modes — read from per-device ComboBoxes stored in Tag
    // OpenRGB device sync modes — same pattern

    _onSave(_config);
}
```

---

## 6. Implementation Order for Coding Agent

1. **Config.cs** — Add `ExternalRgbConfig`, `GoveeDeviceConfig`, `OpenRgbDeviceConfig` classes + `ExternalRgb` field on `AppConfig` + `PreserveGlobalSettings` update
2. **RgbController.cs** — Add `_linearColors` buffer, populate in `SetColor()`, add `OnFrameReady` delegate, invoke at end of `Tick()`
3. **ExternalRgbSync.cs** (new file) — `OnFrame()` handler, Govee UDP send, OpenRGB.NET TCP send, `UpdateConfig()`, `Dispose()`
4. **Views/ExternalRgbView.xaml** — Minimal skeleton (3 named Grid containers)
5. **Views/ExternalRgbView.xaml.cs** — Full UI build in code-behind following HomeAssistantView patterns
6. **MainWindow.xaml** — Add `NavExtRgb` button (with indicator bar)
7. **MainWindow.xaml.cs** — Add `_extRgbView` field, nav click handler, `GetNavBars()` entry, `RefreshViews()` call, `PreserveGlobalSettings()` update
8. **App.xaml.cs** — Instantiate `ExternalRgbSync`, wire `_rgb.OnFrameReady += _externalRgb.OnFrame`, call `UpdateConfig` on config changes, dispose on shutdown
9. **AmpUp.csproj** — Add `OpenRGB.NET` NuGet reference

---

## 7. Open Questions / Decisions for Coding Agent

- **OpenRGB.NET vs raw protocol:** Recommend NuGet package. If Tyson wants zero new dependencies, raw protocol is ~100 lines.
- **Govee rate limiting:** Throttle to max 10 sends/sec per device. Don't send if color delta < 5 on all channels. This prevents Govee firmware overload.
- **Govee discovery UI:** After scan, devices are shown even if disabled. On next open, if previously saved IPs are in config, show them immediately (don't require re-scan). Mark as "last seen" vs "online".
- **OpenRGB reconnect:** If OpenRGB server restarts, the TCP connection drops. Add simple reconnect on next frame attempt (catch + try reconnect with 5s backoff).
- **Thread safety:** `OnFrameReady` fires on the `System.Threading.Timer` thread (not the UI thread). All `ExternalRgbSync` network calls must be non-blocking or wrapped in `Task.Run`. UI updates from async callbacks must go through `Dispatcher.Invoke`.
