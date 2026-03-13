using System.Windows;
using System.Windows.Media;

namespace AmpUp;

/// <summary>
/// Helper for creating TextBlock icons using the Material Symbols Outlined variable font.
/// Replaces WPF-UI SymbolIcon / SymbolRegular throughout the app.
/// Font is embedded at Fonts/MaterialSymbolsOutlined.ttf.
/// </summary>
public static class MaterialIcon
{
    private static FontFamily? _fontFamily;

    public static FontFamily Font =>
        _fontFamily ??= new FontFamily("pack://application:,,,/Fonts/#Material Symbols Outlined");

    // ── Codepoint map (name → Unicode char) ─────────────────────────────────
    // Maps Fluent SymbolRegular names used in this app → Material Symbols codepoints.
    private static readonly Dictionary<string, string> Codepoints = new()
    {
        // Nav / sidebar icons
        { "Person24",            "\uE61A" },  // person
        { "ArrowUpload24",       "\uF09B" },  // upload
        { "DataBarVertical24",   "\uEF3E" },  // analytics
        { "MusicNote124",        "\uE405" },  // music_note
        { "ControlButton24",     "\uF1C1" },  // smart_button
        { "Color24",             "\uE40A" },  // palette
        { "LightbulbFilament24", "\uE0F0" },  // lightbulb
        { "Settings24",          "\uE8B8" },  // settings

        // OSD / volume icons
        { "Speaker224",          "\uE050" },  // volume_up
        { "Speaker024",          "\uE04E" },  // volume_down (low)
        { "SpeakerMute24",       "\uE04F" },  // volume_off
        { "Headphones24",        "\uE310" },  // headphones
        { "Mic24",               "\uE029" },  // mic
        { "MicOff24",            "\uE02B" },  // mic_off

        // Music
        { "MusicNote224",        "\uE405" },  // music_note

        // Gaming / fun
        { "Games24",             "\uE021" },  // sports_esports
        { "Trophy24",            "\uEAC9" },  // emoji_events (trophy)
        { "Rocket24",            "\uEB9F" },  // rocket_launch
        { "Star24",              "\uE838" },  // star
        { "Heart24",             "\uE87D" },  // favorite (heart)
        { "Emoji24",             "\uE7F2" },  // emoji_emotions
        { "Bot24",               "\uEFF1" },  // smart_toy (bot)
        { "PersonBoard24",       "\uF48C" },  // account_box

        // Lights & effects
        { "Flash24",             "\uE162" },  // bolt
        { "Sparkle24",           "\uEAD1" },  // auto_awesome (sparkle)
        { "Weather24",           "\uE818" },  // cloud
        { "WeatherMoon24",       "\uE894" },  // dark_mode (moon)
        { "WeatherSunny24",      "\uE430" },  // wb_sunny
        { "Drop24",              "\uE798" },  // water_drop
        { "Fire24",              "\uE3F0" },  // local_fire_department

        // Work & streaming
        { "Desktop24",           "\uE30C" },  // desktop_windows
        { "Laptop24",            "\uE31E" },  // laptop
        { "Keyboard24",          "\uE312" },  // keyboard
        { "Video24",             "\uE04B" },  // videocam
        { "Record24",            "\uE061" },  // fiber_manual_record
        { "Globe24",             "\uE80B" },  // public (globe)
        { "Megaphone24",         "\uE0C8" },  // campaign (megaphone)
        { "SlideText24",         "\uF05A" },  // co_present (slideshow)

        // Home & system
        { "Home24",              "\uE88A" },  // home
        { "Shield24",            "\uE9E0" },  // security (shield)
        { "Lock24",              "\uE897" },  // lock
        { "Eye24",               "\uE8F4" },  // visibility (eye)
        { "Power24",             "\uE8AC" },  // power_settings_new
        { "Bluetooth24",         "\uE1A7" },  // bluetooth
        { "Wifi124",             "\uE63E" },  // wifi
    };

    /// <summary>
    /// Returns the Unicode character for a named icon (Fluent SymbolRegular name).
    /// Falls back to a bullet if the name is not mapped.
    /// </summary>
    public static string Char(string symbolName)
    {
        return Codepoints.TryGetValue(symbolName, out var cp) ? cp : "\u2022";
    }

    /// <summary>
    /// Creates a TextBlock that renders a Material Symbol icon.
    /// </summary>
    public static System.Windows.Controls.TextBlock Create(
        string symbolName,
        double fontSize = 22,
        Brush? foreground = null)
    {
        return new System.Windows.Controls.TextBlock
        {
            Text = Char(symbolName),
            FontFamily = Font,
            FontSize = fontSize,
            Foreground = foreground ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.Normal,
            FontStyle = FontStyles.Normal,
        };
    }
}
