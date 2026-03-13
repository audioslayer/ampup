using System.Windows.Media;

namespace AmpUp;

/// <summary>
/// Shared domain icon + color styles for Home Assistant entities.
/// Uses Unicode symbols (not emoji) so WPF renders them with Foreground color.
/// </summary>
public static class HADomainStyles
{
    public static readonly Dictionary<string, (string Icon, Color Color)> Domains = new()
    {
        { "light",         ("\u2600", Color.FromRgb(0xFF, 0xD5, 0x4F)) }, // ☀ sun — yellow
        { "switch",        ("\u23FB", Color.FromRgb(0x42, 0xA5, 0xF5)) }, // ⏻ power — blue
        { "scene",         ("\u25BA", Color.FromRgb(0xFF, 0xA7, 0x26)) }, // ► play — orange
        { "fan",           ("\u2732", Color.FromRgb(0x26, 0xC6, 0xDA)) }, // ✲ asterisk — teal
        { "climate",       ("\u2668", Color.FromRgb(0xEF, 0x53, 0x50)) }, // ♨ hot springs — red
        { "media_player",  ("\u266B", Color.FromRgb(0x66, 0xBB, 0x6A)) }, // ♫ music — green
        { "cover",         ("\u2261", Color.FromRgb(0xAB, 0x47, 0xBC)) }, // ≡ bars — purple
        { "automation",    ("\u26A1", Color.FromRgb(0xFF, 0xB7, 0x4D)) }, // ⚡ lightning — amber
        { "script",        ("\u25B6", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ▶ play — grey
        { "input_boolean", ("\u25C6", Color.FromRgb(0x29, 0xB6, 0xF6)) }, // ◆ diamond — blue
        { "lock",          ("\u2302", Color.FromRgb(0xFF, 0xD5, 0x4F)) }, // ⌂ house — gold
        { "sensor",        ("\u25A0", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ■ square — grey
        { "binary_sensor", ("\u25CF", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ● circle — grey
        { "button",        ("\u25C9", Color.FromRgb(0x42, 0xA5, 0xF5)) }, // ◉ fisheye — blue
    };

    public static (string Icon, Color Color) GetStyle(string entityIdOrDomain)
    {
        var domain = entityIdOrDomain.Contains('.') ? entityIdOrDomain.Split('.')[0] : entityIdOrDomain;
        return Domains.TryGetValue(domain, out var style) ? style : ("\u25CF", Color.FromRgb(0x88, 0x88, 0x88));
    }
}
