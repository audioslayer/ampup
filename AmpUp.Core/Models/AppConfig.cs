using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AmpUp.Core.Models;

public class AppConfig
{
    public SerialConfig Serial { get; set; } = new();
    public List<KnobConfig> Knobs { get; set; } = new();
    public List<ButtonConfig> Buttons { get; set; } = new();
    public List<LightConfig> Lights { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public int LedBrightness { get; set; } = 100;
    public double GammaR { get; set; } = 1.0;
    public double GammaG { get; set; } = 1.0;
    public double GammaB { get; set; } = 1.0;
    public string AccentColor { get; set; } = "#00E676";
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
    public int LastRawValue { get; set; } = 1023;
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
    public string PowerAction { get; set; } = "";
    public int LinkedKnobIdx { get; set; } = -1;
    [JsonConverter(typeof(StringEnumConverter))]
    public CycleDeviceType CycleDeviceType { get; set; } = CycleDeviceType.Both;
    public string HoldDeviceId { get; set; } = "";
    public List<string> HoldDeviceIds { get; set; } = new();
    public string HoldMacroKeys { get; set; } = "";
    public string HoldProfileName { get; set; } = "";
    public string HoldPowerAction { get; set; } = "";
    public int HoldLinkedKnobIdx { get; set; } = -1;
    [JsonConverter(typeof(StringEnumConverter))]
    public CycleDeviceType HoldCycleDeviceType { get; set; } = CycleDeviceType.Both;
    public string DoublePressDeviceId { get; set; } = "";
    public List<string> DoublePressDeviceIds { get; set; } = new();
    public string DoublePressMacroKeys { get; set; } = "";
    public string DoublePressProfileName { get; set; } = "";
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
    [JsonConverter(typeof(StringEnumConverter))]
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.SpectrumBands;
    public string ProgramName { get; set; } = "";
    public List<DeviceColorEntry> DeviceColors { get; set; } = new();
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
}

public enum ReactiveMode
{
    BeatPulse, SpectrumBands, ColorShift,
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
}

public enum QuickWheelMode
{
    Profile,
    OutputDevice,
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
    public int BrightnessScale { get; set; } = 75;
    public bool WarmToneShift { get; set; } = false;
    public string GoveeApiKey { get; set; } = "";
    public ScreenSyncConfig ScreenSync { get; set; } = new();
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

public enum ZoneSide { Full, Left, Right, Top, Bottom }

public class ZoneDeviceMapping
{
    public string DeviceIp { get; set; } = "";
    public ZoneSide Side { get; set; } = ZoneSide.Full;
}

public class ScreenSyncConfig
{
    public bool Enabled { get; set; } = false;
    public int MonitorIndex { get; set; } = 0;
    public int TargetFps { get; set; } = 30;
    public int ZoneCount { get; set; } = 8;
    public float Saturation { get; set; } = 1.2f;
    public int Sensitivity { get; set; } = 5;
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
