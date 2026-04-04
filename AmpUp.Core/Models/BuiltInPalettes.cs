namespace AmpUp.Core.Models;

/// <summary>
/// Built-in color palettes for LED effects. Inspired by WLED's palette system.
/// Each palette has 4–7 color stops for rich multi-color gradients.
/// Effects sample these via palette.Sample(t) where t is 0.0–1.0.
/// </summary>
public static class BuiltInPalettes
{
    public static readonly ColorPalette Fire = new("Fire",
        new ColorStop(0.00, 0x33, 0x00, 0x00),   // dark ember
        new ColorStop(0.25, 0xFF, 0x20, 0x00),   // deep red
        new ColorStop(0.50, 0xFF, 0x6A, 0x00),   // orange
        new ColorStop(0.75, 0xFF, 0xAA, 0x00),   // amber
        new ColorStop(1.00, 0xFF, 0xDD, 0x33));  // yellow tip

    public static readonly ColorPalette Ocean = new("Ocean",
        new ColorStop(0.00, 0x00, 0x14, 0x2E),   // deep sea
        new ColorStop(0.25, 0x00, 0x4D, 0x80),   // dark blue
        new ColorStop(0.50, 0x00, 0x99, 0xCC),   // ocean blue
        new ColorStop(0.75, 0x00, 0xDD, 0xEE),   // bright cyan
        new ColorStop(1.00, 0x99, 0xEE, 0xFF));  // foam white

    public static readonly ColorPalette Sunset = new("Sunset",
        new ColorStop(0.00, 0xFF, 0x17, 0x44),   // deep pink
        new ColorStop(0.20, 0xFF, 0x6B, 0x35),   // orange
        new ColorStop(0.40, 0xFF, 0xAA, 0x00),   // golden
        new ColorStop(0.60, 0xFF, 0xD7, 0x00),   // gold
        new ColorStop(0.80, 0xCC, 0x44, 0xCC),   // purple
        new ColorStop(1.00, 0x66, 0x00, 0xAA));  // deep purple

    public static readonly ColorPalette Neon = new("Neon",
        new ColorStop(0.00, 0xFF, 0x00, 0xFF),   // magenta
        new ColorStop(0.25, 0x00, 0xFF, 0xFF),   // cyan
        new ColorStop(0.50, 0xFF, 0x00, 0x88),   // hot pink
        new ColorStop(0.75, 0x80, 0x00, 0xFF),   // purple
        new ColorStop(1.00, 0x00, 0xFF, 0x80));  // green

    public static readonly ColorPalette Arctic = new("Arctic",
        new ColorStop(0.00, 0xE0, 0xF7, 0xFA),   // ice white
        new ColorStop(0.25, 0x80, 0xDE, 0xEA),   // light blue
        new ColorStop(0.50, 0x00, 0xBD, 0xD0),   // teal
        new ColorStop(0.75, 0x33, 0x88, 0xFF),   // bright blue
        new ColorStop(1.00, 0x00, 0x55, 0xAA));  // deep blue

    public static readonly ColorPalette Forest = new("Forest",
        new ColorStop(0.00, 0x00, 0x33, 0x00),   // dark green
        new ColorStop(0.20, 0x00, 0x66, 0x22),   // forest green
        new ColorStop(0.40, 0x00, 0xAA, 0x44),   // green
        new ColorStop(0.60, 0x44, 0xCC, 0x33),   // bright green
        new ColorStop(0.80, 0x88, 0xDD, 0x55),   // lime
        new ColorStop(1.00, 0xCC, 0xEE, 0x77));  // pale lime

    public static readonly ColorPalette Lava = new("Lava",
        new ColorStop(0.00, 0x22, 0x00, 0x00),   // dark crust
        new ColorStop(0.20, 0x8B, 0x00, 0x00),   // dark red
        new ColorStop(0.40, 0xFF, 0x17, 0x00),   // red
        new ColorStop(0.60, 0xFF, 0x55, 0x00),   // red-orange
        new ColorStop(0.80, 0xFF, 0x99, 0x00),   // orange
        new ColorStop(1.00, 0xFF, 0xDD, 0x00));  // molten gold

    public static readonly ColorPalette Galaxy = new("Galaxy",
        new ColorStop(0.00, 0x0A, 0x00, 0x2E),   // void
        new ColorStop(0.20, 0x1A, 0x00, 0x5C),   // deep purple
        new ColorStop(0.40, 0x7C, 0x4D, 0xFF),   // violet
        new ColorStop(0.60, 0xBA, 0x68, 0xC8),   // orchid
        new ColorStop(0.80, 0xFF, 0x80, 0xAB),   // pink nebula
        new ColorStop(1.00, 0xE0, 0x40, 0xFF));  // bright purple

    public static readonly ColorPalette Aurora = new("Aurora",
        new ColorStop(0.00, 0x00, 0xFF, 0x87),   // green
        new ColorStop(0.20, 0x00, 0xCC, 0xBB),   // teal
        new ColorStop(0.40, 0x00, 0x88, 0xFF),   // blue
        new ColorStop(0.60, 0x7B, 0x2F, 0xFF),   // purple
        new ColorStop(0.80, 0xDD, 0x00, 0xFF),   // magenta
        new ColorStop(1.00, 0x00, 0xFF, 0x55));  // green

    public static readonly ColorPalette Vaporwave = new("Vaporwave",
        new ColorStop(0.00, 0xFF, 0x71, 0xCE),   // pink
        new ColorStop(0.25, 0x01, 0xCD, 0xFE),   // cyan
        new ColorStop(0.50, 0xB9, 0x67, 0xFF),   // lavender
        new ColorStop(0.75, 0x05, 0xFC, 0xC1),   // mint
        new ColorStop(1.00, 0xFF, 0x00, 0xA0));  // hot pink

    public static readonly ColorPalette Rainbow = new("Rainbow",
        new ColorStop(0.000, 0xFF, 0x00, 0x00),  // red
        new ColorStop(0.166, 0xFF, 0x88, 0x00),  // orange
        new ColorStop(0.333, 0xFF, 0xFF, 0x00),  // yellow
        new ColorStop(0.500, 0x00, 0xFF, 0x00),  // green
        new ColorStop(0.666, 0x00, 0x88, 0xFF),  // blue
        new ColorStop(0.833, 0x88, 0x00, 0xFF),  // indigo
        new ColorStop(1.000, 0xFF, 0x00, 0x88)); // violet

    public static readonly ColorPalette WarmWhite = new("Warm White",
        new ColorStop(0.00, 0xFF, 0xCC, 0x88),   // warm amber
        new ColorStop(0.33, 0xFF, 0xDD, 0xAA),   // soft warm
        new ColorStop(0.66, 0xFF, 0xEE, 0xCC),   // cream
        new ColorStop(1.00, 0xFF, 0xF5, 0xDD));  // pale warm

    public static readonly ColorPalette CoolWhite = new("Cool White",
        new ColorStop(0.00, 0xCC, 0xDD, 0xFF),   // blue-white
        new ColorStop(0.33, 0xDD, 0xEE, 0xFF),   // ice
        new ColorStop(0.66, 0xEE, 0xEE, 0xFF),   // pale blue
        new ColorStop(1.00, 0xFF, 0xFF, 0xFF));  // pure white

    public static readonly ColorPalette Party = new("Party",
        new ColorStop(0.00, 0xFF, 0x00, 0x44),   // red
        new ColorStop(0.20, 0xFF, 0xDD, 0x00),   // yellow
        new ColorStop(0.40, 0x00, 0xFF, 0x44),   // green
        new ColorStop(0.60, 0x00, 0x88, 0xFF),   // blue
        new ColorStop(0.80, 0xCC, 0x00, 0xFF),   // purple
        new ColorStop(1.00, 0xFF, 0x00, 0x44));  // back to red

    public static readonly ColorPalette Storm = new("Storm",
        new ColorStop(0.00, 0x22, 0x22, 0x33),   // dark grey
        new ColorStop(0.25, 0x44, 0x44, 0x66),   // grey
        new ColorStop(0.50, 0xDD, 0xDD, 0xFF),   // white flash
        new ColorStop(0.75, 0x33, 0x44, 0x77),   // slate
        new ColorStop(1.00, 0x11, 0x11, 0x22));  // near black

    public static readonly ColorPalette Ember = new("Ember",
        new ColorStop(0.00, 0x44, 0x00, 0x00),   // dark coal
        new ColorStop(0.30, 0x88, 0x11, 0x00),   // smolder
        new ColorStop(0.50, 0xCC, 0x33, 0x00),   // glow
        new ColorStop(0.70, 0xFF, 0x55, 0x00),   // bright ember
        new ColorStop(1.00, 0xFF, 0x22, 0x00));  // hot red

    public static readonly ColorPalette Toxic = new("Toxic",
        new ColorStop(0.00, 0x00, 0x22, 0x00),   // dark toxic
        new ColorStop(0.25, 0x00, 0x88, 0x00),   // green
        new ColorStop(0.50, 0x44, 0xFF, 0x00),   // bright toxic
        new ColorStop(0.75, 0x00, 0xFF, 0x88),   // cyan-green
        new ColorStop(1.00, 0xCC, 0xFF, 0x00));  // yellow-green

    public static readonly ColorPalette Inferno = new("Inferno",
        new ColorStop(0.00, 0x00, 0x00, 0x04),   // near black
        new ColorStop(0.20, 0x66, 0x00, 0x44),   // dark magenta
        new ColorStop(0.40, 0xCC, 0x33, 0x00),   // red-orange
        new ColorStop(0.60, 0xFF, 0x88, 0x00),   // orange
        new ColorStop(0.80, 0xFF, 0xDD, 0x00),   // yellow
        new ColorStop(1.00, 0xFF, 0xFF, 0xBB));  // pale yellow

    /// <summary>
    /// All built-in palettes, indexed by name for lookup.
    /// </summary>
    public static readonly Dictionary<string, ColorPalette> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        [Fire.Name] = Fire,
        [Ocean.Name] = Ocean,
        [Sunset.Name] = Sunset,
        [Neon.Name] = Neon,
        [Arctic.Name] = Arctic,
        [Forest.Name] = Forest,
        [Lava.Name] = Lava,
        [Galaxy.Name] = Galaxy,
        [Aurora.Name] = Aurora,
        [Vaporwave.Name] = Vaporwave,
        [Rainbow.Name] = Rainbow,
        [WarmWhite.Name] = WarmWhite,
        [CoolWhite.Name] = CoolWhite,
        [Party.Name] = Party,
        [Storm.Name] = Storm,
        [Ember.Name] = Ember,
        [Toxic.Name] = Toxic,
        [Inferno.Name] = Inferno,
    };

    /// <summary>
    /// All built-in palettes in display order.
    /// </summary>
    public static readonly ColorPalette[] All =
    {
        Fire, Ocean, Sunset, Neon, Arctic, Forest, Lava, Galaxy,
        Aurora, Vaporwave, Rainbow, WarmWhite, CoolWhite, Party,
        Storm, Ember, Toxic, Inferno,
    };

    /// <summary>
    /// Look up a palette by name — checks built-in first, then custom list.
    /// Returns a 2-stop fallback if not found.
    /// </summary>
    public static ColorPalette Resolve(string? name, List<ColorPalette>? customPalettes = null)
    {
        if (string.IsNullOrEmpty(name))
            return Fire;

        if (ByName.TryGetValue(name, out var builtIn))
            return builtIn;

        if (customPalettes != null)
        {
            var custom = customPalettes.Find(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (custom != null) return custom;
        }

        return Fire;
    }
}
