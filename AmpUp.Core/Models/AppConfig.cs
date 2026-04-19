using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AmpUp.Core.Models;

public class AppConfig
{
    public SerialConfig Serial { get; set; } = new();
    public List<KnobConfig> Knobs { get; set; } = new();
    public List<ButtonConfig> Buttons { get; set; } = new();
    public N3Config N3 { get; set; } = new();
    [JsonConverter(typeof(StringEnumConverter))]
    public HardwareMode HardwareMode { get; set; } = HardwareMode.Auto;
    public HardwareTabSelection TabSelection { get; set; } = new();
    public List<LightConfig> Lights { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public int LedBrightness { get; set; } = 100;
    public int MuteBrightness { get; set; } = 15; // 0-100, dim level for muted LEDs (Issue #9)
    public double GammaR { get; set; } = 1.0;
    public double GammaG { get; set; } = 1.0;
    public double GammaB { get; set; } = 1.0;
    public string AccentColor { get; set; } = "#00E676";
    public string CardTheme { get; set; } = "Midnight";
    public string ActiveProfile { get; set; } = "Default";
    public List<string> Profiles { get; set; } = new() { "Default" };
    public Dictionary<string, string> ProfileEmojis { get; set; } = new();
    public Dictionary<string, ProfileIconConfig> ProfileIcons { get; set; } = new()
    {
        { "Default", new ProfileIconConfig() }
    };
    public OsdConfig Osd { get; set; } = new();
    public GlobalLightConfig GlobalLight { get; set; } = new();
    [JsonConverter(typeof(StringEnumConverter))]
    public ProfileTransition ProfileTransition { get; set; } = ProfileTransition.Cascade;
    public HomeAssistantConfig HomeAssistant { get; set; } = new();
    public AmbienceConfig Ambience { get; set; } = new();
    public DuckingConfig Ducking { get; set; } = new();
    public AutoSwitchConfig AutoSwitch { get; set; } = new();
    public ObsConfig Obs { get; set; } = new();
    public VoiceMeeterConfig VoiceMeeter { get; set; } = new();
    public bool HasCompletedSetup { get; set; } = false;
    public bool AutoSuggestLayout { get; set; } = false;
    public string LastWelcomeVersion { get; set; } = "";
    public List<string> HiddenTrayApps { get; set; } = new();
    public List<string> PinnedTrayApps { get; set; } = new();
    public Dictionary<string, List<string>> CycleDeviceSubset { get; set; } = new();
    public bool AutoCheckUpdates { get; set; } = true;
    public bool HasShownAudioGuide { get; set; } = false;
    public CorsairConfig Corsair { get; set; } = new();
    public List<DeviceGroup> Groups { get; set; } = new();
    public RoomLayout RoomLayout { get; set; } = new();
    public List<ColorPalette> CustomPalettes { get; set; } = new();
    /// <summary>LightEffect enum names saved as favorites — shown in the Favorites tab of the effect picker.</summary>
    public List<string> FavoriteEffects { get; set; } = new();
}

public class DeviceGroup
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#69F0AE"; // accent color for the group
    public List<GroupDevice> Devices { get; set; } = new();
}

public class GroupDevice
{
    public string Type { get; set; } = ""; // "govee" | "corsair" | "ha" | "audio_output"
    public string DeviceId { get; set; } = ""; // IP for govee, device ID for corsair, entity_id for HA
    public string Name { get; set; } = ""; // friendly name
    public string Action { get; set; } = "toggle"; // "toggle" | "on" | "off" — for HA entities
}

public class CorsairConfig
{
    public bool Enabled { get; set; } = false;
    public bool FanEnabled { get; set; } = false;
    public string FanSyncMode { get; set; } = "manual"; // "manual" | "audio_reactive"
    public int PumpFanSpeed { get; set; } = 50;    // 0-100%
    public int CaseFanSpeed { get; set; } = 50;    // 0-100%
    public int FanMinPercent { get; set; } = 20;   // audio-reactive floor
    public int FanMaxPercent { get; set; } = 100;  // audio-reactive ceiling
    public string LightSyncMode { get; set; } = "off"; // "off" | "static" | "dreamview" | "vu_reactive"
    public int LightBrightness { get; set; } = 100; // 0-200%, boost for screen sync colors
    public string StaticColor { get; set; } = "#00E676";
    public string SelectedMural { get; set; } = "";
    public bool SyncToGlobal { get; set; } = true; // Corsair follows Global tab effects
}

public class SerialConfig
{
    public string Port { get; set; } = "COM3";
    public int Baud { get; set; } = 115200;
}

public class KnobConfig
{
    public int Idx { get; set; }
    public string Label { get; set; } = "";
    public string Target { get; set; } = "none";
    public string DeviceId { get; set; } = "";
    public int MinVolume { get; set; } = 0;
    public int MaxVolume { get; set; } = 100;
    [JsonConverter(typeof(StringEnumConverter))]
    public ResponseCurve Curve { get; set; } = ResponseCurve.Linear;
    public List<string> Apps { get; set; } = new();
    public int LastRawValue { get; set; } = -1; // -1 = never saved, skip startup restore
}

public enum ResponseCurve
{
    Linear,
    Logarithmic,
    Exponential,
    Exponential2
}

public enum CycleDeviceType
{
    Both,
    Media,
    Communications
}

public class ButtonConfig
{
    public int Idx { get; set; }
    public string Label { get; set; } = "";
    public string Action { get; set; } = "none";
    public string Path { get; set; } = "";
    public string HoldAction { get; set; } = "none";
    public string HoldPath { get; set; } = "";
    public string DoublePressAction { get; set; } = "none";
    public string DoublePressPath { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public List<string> DeviceIds { get; set; } = new();
    public string MacroKeys { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public List<string> ProfileNames { get; set; } = new(); // for cycle_profile subset
    public string PowerAction { get; set; } = "";
    public int LinkedKnobIdx { get; set; } = -1;
    [JsonConverter(typeof(StringEnumConverter))]
    public CycleDeviceType CycleDeviceType { get; set; } = CycleDeviceType.Both;
    public string HoldDeviceId { get; set; } = "";
    public List<string> HoldDeviceIds { get; set; } = new();
    public string HoldMacroKeys { get; set; } = "";
    public string HoldProfileName { get; set; } = "";
    public List<string> HoldProfileNames { get; set; } = new();
    public string HoldPowerAction { get; set; } = "";
    public int HoldLinkedKnobIdx { get; set; } = -1;
    [JsonConverter(typeof(StringEnumConverter))]
    public CycleDeviceType HoldCycleDeviceType { get; set; } = CycleDeviceType.Both;
    public string DoublePressDeviceId { get; set; } = "";
    public List<string> DoublePressDeviceIds { get; set; } = new();
    public string DoublePressMacroKeys { get; set; } = "";
    public string DoublePressProfileName { get; set; } = "";
    public List<string> DoublePressProfileNames { get; set; } = new();
    public string DoublePressPowerAction { get; set; } = "";
    public int DoublePressLinkedKnobIdx { get; set; } = -1;
    [JsonConverter(typeof(StringEnumConverter))]
    public CycleDeviceType DoublePressCycleDeviceType { get; set; } = CycleDeviceType.Both;
}

public class DeviceColorEntry
{
    public string DeviceId { get; set; } = "";
    public int R { get; set; } = 0;
    public int G { get; set; } = 150;
    public int B { get; set; } = 255;
}

public class LightConfig
{
    public int Idx { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public LightEffect Effect { get; set; } = LightEffect.SingleColor;
    public int R2 { get; set; }
    public int G2 { get; set; }
    public int B2 { get; set; }
    public int EffectSpeed { get; set; } = 50;
    public int Brightness { get; set; } = 100; // per-knob brightness 0-100
    [JsonConverter(typeof(StringEnumConverter))]
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.SpectrumBands;
    public string ProgramName { get; set; } = "";
    public List<DeviceColorEntry> DeviceColors { get; set; } = new();
    public string PaletteName { get; set; } = "";
}

public class GlobalLightConfig
{
    public bool Enabled { get; set; } = false;
    [JsonConverter(typeof(StringEnumConverter))]
    public LightEffect Effect { get; set; } = LightEffect.RainbowWave;
    public int R { get; set; } = 0;
    public int G { get; set; } = 230;
    public int B { get; set; } = 118;
    public int R2 { get; set; } = 255;
    public int G2 { get; set; } = 255;
    public int B2 { get; set; } = 255;
    public int EffectSpeed { get; set; } = 50;
    [JsonConverter(typeof(StringEnumConverter))]
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.SpectrumBands;
    public List<string> GradientColors { get; set; } = new();
    public List<int> DisabledKnobs { get; set; } = new();
    public string PaletteName { get; set; } = "";
    /// <summary>Idle effect for AudioPositionBlend — plays when no music detected.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public LightEffect IdleEffect { get; set; } = LightEffect.PositionBlend;
}

public enum LightEffect
{
    SingleColor, ColorBlend, PositionFill, Blink, Pulse,
    RainbowWave, RainbowCycle, MicStatus, DeviceMute, AudioReactive,
    Breathing, Fire, Comet, Sparkle, GradientFill,
    PositionBlend, PositionBlendMute,
    PingPong, Stack, Wave, Candle, Wheel, RainbowWheel,
    ProgramMute, AppGroupMute, DeviceSelect,
    Scanner, MeteorRain, ColorWave, Segments, TheaterChase,
    RainbowScanner, SparkleRain, BreathingSync, FireWall,
    DualRacer, Lightning, Fillup, Ocean, Collision, DNA, Rainfall, PoliceLights,
    CycleFill, RainbowFill,
    Heartbeat, Plasma, Drip,
    Aurora, Matrix, Starfield,
    AudioPositionBlend,
    Equalizer, Waterfall, Lava, VuWave,
    NebulaDrift,
    Vortex, Shockwave, Tidal, Prism, EmberDrift, Glitch,
    OpalWave, Bloom, ColorTwinkle,
}

public class N3Config
{
    public bool Enabled { get; set; } = true;
    public bool MirrorFirstThreeKnobs { get; set; } = true;
    public int EncoderStep { get; set; } = 32;
    public List<KnobConfig> Knobs { get; set; } = new();
    public List<ButtonConfig> Buttons { get; set; } = new();
    public List<StreamControllerDisplayKeyConfig> DisplayKeys { get; set; } = new();
    public int DisplayBrightness { get; set; } = 100;
    public bool ScreensaverEnabled { get; set; } = false;
    [JsonConverter(typeof(StringEnumConverter))]
    public StreamControllerScreensaverEffect ScreensaverEffect { get; set; } = StreamControllerScreensaverEffect.Rainbow;
    public int ScreensaverOpacity { get; set; } = 55;
    public int ScreensaverSpeed { get; set; } = 50;
}

public class HardwareTabSelection
{
    [JsonConverter(typeof(StringEnumConverter))]
    public DeviceSurface Mixer { get; set; } = DeviceSurface.TurnUp;
    [JsonConverter(typeof(StringEnumConverter))]
    public DeviceSurface Buttons { get; set; } = DeviceSurface.TurnUp;
    [JsonConverter(typeof(StringEnumConverter))]
    public DeviceSurface Lights { get; set; } = DeviceSurface.TurnUp;
}

public class StreamControllerDisplayKeyConfig
{
    public int Idx { get; set; }
    public string ImagePath { get; set; } = "";
    public string PresetIconKind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string BackgroundColor { get; set; } = "#1C1C1C";
    public string AccentColor { get; set; } = "#00E676";
}

public enum HardwareMode
{
    Auto,
    TurnUpOnly,
    StreamControllerOnly,
    DualMode,
}

public enum DeviceSurface
{
    TurnUp,
    StreamController,
    Both,
}

public enum StreamControllerScreensaverEffect
{
    Rainbow,
    Aurora,
    Fire,
    Prism,
    MusicBounce,
    ScreenSync,
}

public enum ReactiveMode
{
    BeatPulse, SpectrumBands, ColorShift,
}

public enum VuFillMode
{
    Classic,      // standard bottom→top fill
    Split,        // left panel=bass, right panel=treble (independent levels)
    Rainfall,     // drips fall from top on beats
    Pulse,        // all segments pulse together with bass energy
    Spectrum,     // each segment = a frequency band (like equalizer)
    Drip,         // liquid drips spawn at top, fall, splash at bottom
}

public enum ProfileTransition
{
    None, Flash, Cascade, RainbowSweep, Ripple, ColorBurst, Wipe,
}

public class OsdConfig
{
    public bool ShowVolume { get; set; } = true;
    public bool ShowProfileSwitch { get; set; } = true;
    public bool ShowDeviceSwitch { get; set; } = true;
    public double VolumeDuration { get; set; } = 2.0;
    public double ProfileDuration { get; set; } = 3.5;
    public double DeviceDuration { get; set; } = 2.5;
    public double WheelDuration { get; set; } = 0.0;
    [JsonConverter(typeof(StringEnumConverter))]
    public OsdPosition Position { get; set; } = OsdPosition.BottomRight;
    public int MonitorIndex { get; set; } = 0;
    public bool HideInFullscreen { get; set; } = false;
    // Legacy single wheel — kept for backwards compat on config load
    public QuickWheelConfig? QuickWheel { get; set; }
    public List<QuickWheelConfig> QuickWheels { get; set; } = new();
}

public class QuickWheelConfig
{
    public bool Enabled { get; set; } = false;
    public int TriggerButton { get; set; } = 0;
    public string TriggerGesture { get; set; } = "hold";
    public int NavigationKnob { get; set; } = 0;

    [JsonConverter(typeof(StringEnumConverter))]
    public QuickWheelMode Mode { get; set; } = QuickWheelMode.Profile;

    public List<CustomWheelSlot> CustomSlots { get; set; } = new();
}

public enum QuickWheelMode
{
    Profile,
    OutputDevice,
    MediaControls,
    Custom,
}

public class CustomWheelSlot
{
    public string ActionId { get; set; } = "";
    public string Label { get; set; } = "";
}

public enum OsdPosition
{
    TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight
}

public class ProfileIconConfig
{
    public string Symbol { get; set; } = "AccountCircleOutline";
    public string Color { get; set; } = "#00E676";
}

public class HomeAssistantConfig
{
    public bool Enabled { get; set; } = false;
    public string Url { get; set; } = "http://homeassistant.local:8123";
    public string Token { get; set; } = "";
}

public class AmbienceConfig
{
    public bool GoveeEnabled { get; set; } = false;
    public bool GoveeCloudEnabled { get; set; } = false;
    public List<GoveeDeviceConfig> GoveeDevices { get; set; } = new();
    public bool LinkToLights { get; set; } = false;
    public bool SyncRoomToTurnUp { get; set; } = false;
    public int MusicSensitivity { get; set; } = 50; // 1-100, controls music reactive intensity
    [JsonConverter(typeof(StringEnumConverter))]
    public VuFillMode VuFillMode { get; set; } = VuFillMode.Classic;
    public int BrightnessScale { get; set; } = 75;
    public bool WarmToneShift { get; set; } = false;
    public string GoveeApiKey { get; set; } = "";
    public ScreenSyncConfig ScreenSync { get; set; } = new();
    public bool GameModeEnabled { get; set; } = false;
    public bool GoveeSyncToGlobal { get; set; } = true; // Govee follows Global tab effects
    public bool SpatialSync { get; set; } = false; // false=Mirror (all same), true=Spatial (flow across)
    // Persisted room effect state — restored on startup
    public string? RoomEffect { get; set; } = null; // null = not active
    public string RoomColor1 { get; set; } = "#00E676";
    public string RoomColor2 { get; set; } = "#FFFFFF";
    public int RoomEffectSpeed { get; set; } = 50;
}

public class GoveeDeviceConfig
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string SyncMode { get; set; } = "off";
    public bool UseSegmentProtocol { get; set; } = true;
    /// <summary>Tracks user's on/off intent. When false, color sync won't send frames (which would turn device on).</summary>
    public bool PoweredOn { get; set; } = true;
}

public enum ZoneSide { Full, Left, Right, Top, Bottom, LeftVertical, RightVertical }

public enum DeviceCropMode { Content, FullScreen, Ambient }

public class ZoneDeviceMapping
{
    public string DeviceIp { get; set; } = "";
    public ZoneSide Side { get; set; } = ZoneSide.Full;
    public bool UseAutoSpatial { get; set; } = false;
    [JsonConverter(typeof(StringEnumConverter))]
    public DeviceCropMode CropMode { get; set; } = DeviceCropMode.Content;
}

public class ContentBounds
{
    public double LeftPct { get; set; } = 0;    // 0.0-0.5, inset from left edge
    public double RightPct { get; set; } = 0;   // 0.0-0.5, inset from right edge
    public double TopPct { get; set; } = 0;
    public double BottomPct { get; set; } = 0;
    public bool AutoDetect { get; set; } = true;
}

public class ScreenSyncConfig
{
    public bool Enabled { get; set; } = false;
    public int MonitorIndex { get; set; } = 0;
    public int TargetFps { get; set; } = 30;
    public int ZoneCount { get; set; } = 8;
    public float Saturation { get; set; } = 1.5f;
    public int Sensitivity { get; set; } = 5;
    public bool CropBlackBars { get; set; } = true;
    public bool SyncToTurnUp { get; set; } = false;
    public ContentBounds ContentBounds { get; set; } = new();
    public List<ZoneDeviceMapping> DeviceMappings { get; set; } = new();
}

// Ducking config (from DuckingEngine)
public class DuckingConfig
{
    public bool Enabled { get; set; } = false;
    public List<DuckingRule> Rules { get; set; } = new();
}

public class DuckingRule
{
    public string TriggerApp { get; set; } = "";
    public List<string> TargetApps { get; set; } = new();
    public int DuckPercent { get; set; } = 50;
    public int FadeInMs { get; set; } = 500;
    public int FadeOutMs { get; set; } = 200;
    public float ActivationThreshold { get; set; } = 0.01f;
}

// Auto-switch config (from AutoProfileSwitcher)
public class AutoSwitchConfig
{
    public bool Enabled { get; set; } = false;
    public List<AutoSwitchRule> Rules { get; set; } = new();
    public bool RevertToDefault { get; set; } = true;
    public string DefaultProfile { get; set; } = "Default";
}

public class AutoSwitchRule
{
    public string ProcessName { get; set; } = "";
    public string ProfileName { get; set; } = "";
}

public class ObsConfig
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
}

public class VoiceMeeterConfig
{
    public bool Enabled { get; set; } = false;
}

// ── Room Layout (3D spatial mapping for room lighting) ──

public class MonitorPlacement
{
    public double X { get; set; } = 6.0;       // center, feet from left wall
    public double Y { get; set; } = 1.0;       // near front wall (desk)
    public double Z { get; set; } = 3.5;       // desk height
    public double Rotation { get; set; } = 0;   // degrees
    public double WidthFt { get; set; } = 2.8;  // physical width (~34" ultrawide)
    public double HeightFt { get; set; } = 1.0;  // physical height
    public int MonitorIndex { get; set; } = 0;   // links to ScreenSyncConfig.MonitorIndex
}

public class RoomLayout
{
    public double WidthFt { get; set; } = 12.0;
    public double DepthFt { get; set; } = 10.0;
    public double HeightFt { get; set; } = 8.0;
    [JsonConverter(typeof(StringEnumConverter))]
    public EffectDirection Direction { get; set; } = EffectDirection.LeftToRight;
    public MonitorPlacement? Monitor { get; set; }
    public List<RoomDevicePlacement> Devices { get; set; } = new();
}

public class RoomDevicePlacement
{
    public string DeviceType { get; set; } = "govee"; // "govee" | "corsair"
    public string DeviceId { get; set; } = "";         // IP for Govee LAN, device ID for Corsair
    public string Name { get; set; } = "";
    public double X { get; set; } = 0;                 // feet from left wall
    public double Y { get; set; } = 0;                 // feet from front wall (depth)
    public double Z { get; set; } = 4.0;               // feet from floor (height)
    public double Rotation { get; set; } = 0;           // degrees, 0 = facing forward
    public double LengthFt { get; set; } = 1.5;        // physical length of the device
    public int SegmentCount { get; set; } = 1;          // cached segment count
    public bool Reversed { get; set; } = false;         // reverse segment order (right-to-left)
    public bool SplitLR { get; set; } = false;          // render as two halves (left + right unit)
    public double SplitGapFt { get; set; } = 2.0;      // gap between left and right halves in feet
}

public enum EffectDirection
{
    LeftToRight,
    FrontToBack,
    BottomToTop,
    Radial,
    Diagonal
}
