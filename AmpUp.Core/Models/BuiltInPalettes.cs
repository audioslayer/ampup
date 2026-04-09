namespace AmpUp.Core.Models;

/// <summary>
/// Built-in color palettes for LED effects. Inspired by WLED's palette system.
/// Each palette has 4–7 color stops for rich multi-color gradients.
/// Effects sample these via palette.Sample(t) where t is 0.0–1.0.
/// </summary>
public static class BuiltInPalettes
{
    public static readonly ColorPalette Fire = new("Fire",
        new ColorStop(0.00, 0x1A, 0x00, 0x00),   // charcoal ember
        new ColorStop(0.15, 0x6B, 0x08, 0x00),   // smoldering coal
        new ColorStop(0.35, 0xCC, 0x22, 0x00),   // deep crimson
        new ColorStop(0.55, 0xFF, 0x5E, 0x00),   // flame orange
        new ColorStop(0.75, 0xFF, 0x9E, 0x00),   // amber glow
        new ColorStop(0.90, 0xFF, 0xCC, 0x22),   // golden tip
        new ColorStop(1.00, 0xFF, 0xE0, 0x66));  // pale flame

    public static readonly ColorPalette Ocean = new("Ocean",
        new ColorStop(0.00, 0x00, 0x0C, 0x1A),   // abyss
        new ColorStop(0.20, 0x00, 0x2E, 0x5C),   // midnight blue
        new ColorStop(0.40, 0x00, 0x6E, 0xAA),   // deep ocean
        new ColorStop(0.60, 0x00, 0xAA, 0xCC),   // ocean blue
        new ColorStop(0.75, 0x00, 0xDD, 0xEE),   // bright aqua
        new ColorStop(0.90, 0x66, 0xEE, 0xFF),   // seafoam
        new ColorStop(1.00, 0xBB, 0xF5, 0xFF));  // foam white

    public static readonly ColorPalette Sunset = new("Sunset",
        new ColorStop(0.00, 0xAA, 0x00, 0x44),   // wine horizon
        new ColorStop(0.18, 0xFF, 0x17, 0x44),   // crimson
        new ColorStop(0.35, 0xFF, 0x6B, 0x35),   // tangerine
        new ColorStop(0.50, 0xFF, 0xAA, 0x00),   // golden hour
        new ColorStop(0.65, 0xFF, 0xD7, 0x00),   // warm gold
        new ColorStop(0.80, 0xCC, 0x44, 0xCC),   // dusk purple
        new ColorStop(1.00, 0x55, 0x00, 0x88));  // deep twilight

    public static readonly ColorPalette Neon = new("Neon",
        new ColorStop(0.00, 0xFF, 0x00, 0xDD),   // electric magenta
        new ColorStop(0.20, 0xBB, 0x00, 0xFF),   // neon violet
        new ColorStop(0.40, 0x00, 0x44, 0xFF),   // electric blue
        new ColorStop(0.60, 0x00, 0xFF, 0xCC),   // neon cyan
        new ColorStop(0.80, 0x00, 0xFF, 0x44),   // neon green
        new ColorStop(1.00, 0xFF, 0x00, 0x88));  // hot pink

    public static readonly ColorPalette Arctic = new("Arctic",
        new ColorStop(0.00, 0x00, 0x33, 0x66),   // deep glacial
        new ColorStop(0.20, 0x00, 0x6E, 0x99),   // frozen teal
        new ColorStop(0.40, 0x00, 0xAA, 0xCC),   // arctic blue
        new ColorStop(0.60, 0x66, 0xDD, 0xEE),   // pale ice
        new ColorStop(0.80, 0xBB, 0xEE, 0xF5),   // frost
        new ColorStop(1.00, 0xE8, 0xF8, 0xFF));  // ice crystal

    public static readonly ColorPalette Forest = new("Forest",
        new ColorStop(0.00, 0x00, 0x22, 0x00),   // deep shade
        new ColorStop(0.18, 0x00, 0x44, 0x11),   // dark moss
        new ColorStop(0.36, 0x00, 0x77, 0x22),   // forest floor
        new ColorStop(0.54, 0x22, 0xAA, 0x44),   // canopy green
        new ColorStop(0.72, 0x66, 0xCC, 0x55),   // sunlit leaf
        new ColorStop(0.88, 0xAA, 0xDD, 0x77),   // pale fern
        new ColorStop(1.00, 0xDD, 0xEE, 0x88));  // dappled light

    public static readonly ColorPalette Lava = new("Lava",
        new ColorStop(0.00, 0x11, 0x00, 0x00),   // obsidian
        new ColorStop(0.15, 0x44, 0x00, 0x00),   // dark crust
        new ColorStop(0.30, 0x88, 0x00, 0x00),   // cooling red
        new ColorStop(0.50, 0xDD, 0x22, 0x00),   // hot lava
        new ColorStop(0.65, 0xFF, 0x55, 0x00),   // flowing orange
        new ColorStop(0.80, 0xFF, 0x99, 0x00),   // molten
        new ColorStop(1.00, 0xFF, 0xDD, 0x22));  // incandescent gold

    public static readonly ColorPalette Galaxy = new("Galaxy",
        new ColorStop(0.00, 0x06, 0x00, 0x1A),   // void black
        new ColorStop(0.15, 0x14, 0x00, 0x44),   // deep space
        new ColorStop(0.30, 0x44, 0x11, 0x88),   // nebula core
        new ColorStop(0.50, 0x7C, 0x4D, 0xFF),   // violet star
        new ColorStop(0.65, 0xBB, 0x66, 0xDD),   // orchid mist
        new ColorStop(0.80, 0xFF, 0x77, 0xAA),   // pink nebula
        new ColorStop(1.00, 0xDD, 0x44, 0xEE));  // bright pulsar

    public static readonly ColorPalette Aurora = new("Aurora",
        new ColorStop(0.00, 0x00, 0xEE, 0x77),   // bright green curtain
        new ColorStop(0.17, 0x00, 0xCC, 0x99),   // teal shimmer
        new ColorStop(0.33, 0x00, 0x88, 0xDD),   // arctic blue
        new ColorStop(0.50, 0x55, 0x33, 0xFF),   // violet band
        new ColorStop(0.67, 0xAA, 0x00, 0xEE),   // magenta flare
        new ColorStop(0.83, 0x44, 0xDD, 0x88),   // green return
        new ColorStop(1.00, 0x00, 0xFF, 0x66));  // bright curtain

    public static readonly ColorPalette Vaporwave = new("Vaporwave",
        new ColorStop(0.00, 0xFF, 0x00, 0x99),   // hot pink
        new ColorStop(0.20, 0xEE, 0x66, 0xDD),   // warm pink
        new ColorStop(0.40, 0xAA, 0x55, 0xFF),   // lavender neon
        new ColorStop(0.55, 0x22, 0xBB, 0xFF),   // retro blue
        new ColorStop(0.70, 0x00, 0xEE, 0xCC),   // mint cyan
        new ColorStop(0.85, 0x44, 0xFF, 0xBB),   // aqua glow
        new ColorStop(1.00, 0xFF, 0x44, 0xAA));  // sunset pink

    public static readonly ColorPalette Rainbow = new("Rainbow",
        new ColorStop(0.000, 0xFF, 0x00, 0x00),  // red
        new ColorStop(0.166, 0xFF, 0x88, 0x00),  // orange
        new ColorStop(0.333, 0xFF, 0xFF, 0x00),  // yellow
        new ColorStop(0.500, 0x00, 0xFF, 0x00),  // green
        new ColorStop(0.666, 0x00, 0x88, 0xFF),  // blue
        new ColorStop(0.833, 0x88, 0x00, 0xFF),  // indigo
        new ColorStop(1.000, 0xFF, 0x00, 0x88)); // violet

    public static readonly ColorPalette WarmWhite = new("Warm White",
        new ColorStop(0.00, 0xFF, 0xBB, 0x77),   // amber glow
        new ColorStop(0.25, 0xFF, 0xCC, 0x88),   // warm honey
        new ColorStop(0.50, 0xFF, 0xDD, 0xAA),   // soft candlelight
        new ColorStop(0.75, 0xFF, 0xEE, 0xCC),   // cream
        new ColorStop(1.00, 0xFF, 0xF5, 0xDD));  // pale warm

    public static readonly ColorPalette CoolWhite = new("Cool White",
        new ColorStop(0.00, 0xAA, 0xCC, 0xFF),   // pale sky
        new ColorStop(0.25, 0xCC, 0xDD, 0xFF),   // cool blue
        new ColorStop(0.50, 0xDD, 0xEE, 0xFF),   // ice
        new ColorStop(0.75, 0xEE, 0xF2, 0xFF),   // mist
        new ColorStop(1.00, 0xFF, 0xFF, 0xFF));  // pure white

    public static readonly ColorPalette Party = new("Party",
        new ColorStop(0.00, 0xFF, 0x00, 0x44),   // red
        new ColorStop(0.20, 0xFF, 0xDD, 0x00),   // yellow
        new ColorStop(0.40, 0x00, 0xFF, 0x44),   // green
        new ColorStop(0.60, 0x00, 0x88, 0xFF),   // blue
        new ColorStop(0.80, 0xCC, 0x00, 0xFF),   // purple
        new ColorStop(1.00, 0xFF, 0x00, 0x44));  // back to red

    public static readonly ColorPalette Storm = new("Storm",
        new ColorStop(0.00, 0x0D, 0x0D, 0x1A),   // near black
        new ColorStop(0.20, 0x22, 0x22, 0x44),   // dark slate
        new ColorStop(0.40, 0x44, 0x44, 0x66),   // storm grey
        new ColorStop(0.55, 0xBB, 0xBB, 0xEE),   // lightning flash
        new ColorStop(0.65, 0xEE, 0xEE, 0xFF),   // white flash
        new ColorStop(0.80, 0x33, 0x33, 0x55),   // receding
        new ColorStop(1.00, 0x11, 0x11, 0x22));  // darkness

    public static readonly ColorPalette Ember = new("Ember",
        new ColorStop(0.00, 0x22, 0x00, 0x00),   // cold ash
        new ColorStop(0.15, 0x44, 0x00, 0x00),   // dark coal
        new ColorStop(0.35, 0x88, 0x11, 0x00),   // smolder
        new ColorStop(0.50, 0xBB, 0x22, 0x00),   // warm glow
        new ColorStop(0.65, 0xDD, 0x44, 0x00),   // bright ember
        new ColorStop(0.80, 0xFF, 0x55, 0x00),   // hot spot
        new ColorStop(1.00, 0xFF, 0x33, 0x00));  // burning edge

    public static readonly ColorPalette Toxic = new("Toxic",
        new ColorStop(0.00, 0x00, 0x11, 0x00),   // black ooze
        new ColorStop(0.20, 0x00, 0x44, 0x00),   // dark sludge
        new ColorStop(0.40, 0x00, 0xAA, 0x22),   // toxic green
        new ColorStop(0.55, 0x44, 0xFF, 0x00),   // radioactive
        new ColorStop(0.70, 0x00, 0xFF, 0x66),   // bright toxic
        new ColorStop(0.85, 0x88, 0xFF, 0x00),   // acid yellow
        new ColorStop(1.00, 0xCC, 0xFF, 0x00));  // neon lime

    public static readonly ColorPalette Inferno = new("Inferno",
        new ColorStop(0.00, 0x00, 0x00, 0x04),   // near black
        new ColorStop(0.15, 0x33, 0x00, 0x22),   // dark magenta
        new ColorStop(0.30, 0x77, 0x00, 0x44),   // deep red-purple
        new ColorStop(0.45, 0xCC, 0x33, 0x00),   // red-orange
        new ColorStop(0.60, 0xFF, 0x77, 0x00),   // bright orange
        new ColorStop(0.75, 0xFF, 0xBB, 0x00),   // golden
        new ColorStop(0.90, 0xFF, 0xEE, 0x55),   // pale yellow
        new ColorStop(1.00, 0xFF, 0xFF, 0xCC));  // white-yellow

    // ── New premium palettes ──

    public static readonly ColorPalette Cyberpunk = new("Cyberpunk",
        new ColorStop(0.00, 0x0A, 0x00, 0x1A),   // void
        new ColorStop(0.15, 0x22, 0x00, 0x44),   // deep purple
        new ColorStop(0.30, 0x66, 0x00, 0x88),   // dark violet
        new ColorStop(0.50, 0xFF, 0x00, 0x66),   // neon pink
        new ColorStop(0.65, 0xDD, 0x00, 0xCC),   // magenta
        new ColorStop(0.80, 0x00, 0x88, 0xFF),   // electric blue
        new ColorStop(1.00, 0x00, 0xCC, 0xFF));  // bright cyan

    public static readonly ColorPalette Sakura = new("Sakura",
        new ColorStop(0.00, 0xFF, 0xDD, 0xE8),   // pale petal
        new ColorStop(0.20, 0xFF, 0xAA, 0xCC),   // soft pink
        new ColorStop(0.40, 0xFF, 0x88, 0xAA),   // blossom
        new ColorStop(0.55, 0xFF, 0xCC, 0xDD),   // light petal
        new ColorStop(0.70, 0xFF, 0xEE, 0xEE),   // white
        new ColorStop(0.85, 0xCC, 0xEE, 0xBB),   // pale green leaf
        new ColorStop(1.00, 0xAA, 0xDD, 0x99));  // spring green

    public static readonly ColorPalette Twilight = new("Twilight",
        new ColorStop(0.00, 0x0D, 0x00, 0x22),   // deep night
        new ColorStop(0.18, 0x33, 0x00, 0x55),   // midnight violet
        new ColorStop(0.36, 0x77, 0x11, 0x77),   // dusk purple
        new ColorStop(0.50, 0xCC, 0x33, 0x66),   // rose horizon
        new ColorStop(0.65, 0xFF, 0x66, 0x44),   // warm orange glow
        new ColorStop(0.80, 0xFF, 0xAA, 0x33),   // golden horizon
        new ColorStop(1.00, 0xFF, 0xDD, 0x77));  // pale sky

    public static readonly ColorPalette CoralReef = new("Coral Reef",
        new ColorStop(0.00, 0x00, 0x22, 0x44),   // deep water
        new ColorStop(0.18, 0x00, 0x66, 0x77),   // ocean depth
        new ColorStop(0.35, 0x00, 0xAA, 0x99),   // reef teal
        new ColorStop(0.50, 0x33, 0xDD, 0xAA),   // bright coral green
        new ColorStop(0.65, 0xFF, 0x77, 0x66),   // living coral
        new ColorStop(0.80, 0xFF, 0xAA, 0x55),   // warm coral
        new ColorStop(1.00, 0xFF, 0xDD, 0x88));  // sandy glow

    public static readonly ColorPalette Lavender = new("Lavender",
        new ColorStop(0.00, 0x2A, 0x00, 0x55),   // deep violet
        new ColorStop(0.20, 0x55, 0x22, 0x99),   // rich purple
        new ColorStop(0.40, 0x88, 0x55, 0xCC),   // lavender
        new ColorStop(0.60, 0xBB, 0x88, 0xEE),   // soft lilac
        new ColorStop(0.80, 0xDD, 0xBB, 0xFF),   // pale wisteria
        new ColorStop(1.00, 0xEE, 0xDD, 0xFF));  // whisper violet

    public static readonly ColorPalette Copper = new("Copper",
        new ColorStop(0.00, 0x1A, 0x0A, 0x00),   // dark patina
        new ColorStop(0.20, 0x55, 0x22, 0x00),   // aged copper
        new ColorStop(0.40, 0x99, 0x44, 0x00),   // warm bronze
        new ColorStop(0.55, 0xCC, 0x66, 0x11),   // polished copper
        new ColorStop(0.70, 0xDD, 0x88, 0x33),   // bright copper
        new ColorStop(0.85, 0xEE, 0xAA, 0x55),   // copper shine
        new ColorStop(1.00, 0xFF, 0xCC, 0x88));  // golden copper

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
        [Cyberpunk.Name] = Cyberpunk,
        [Sakura.Name] = Sakura,
        [Twilight.Name] = Twilight,
        [CoralReef.Name] = CoralReef,
        [Lavender.Name] = Lavender,
        [Copper.Name] = Copper,
    };

    /// <summary>
    /// All built-in palettes in display order.
    /// </summary>
    public static readonly ColorPalette[] All =
    {
        Fire, Ocean, Sunset, Neon, Arctic, Forest, Lava, Galaxy,
        Aurora, Vaporwave, Cyberpunk, Sakura, Twilight, CoralReef,
        Lavender, Copper, Rainbow, WarmWhite, CoolWhite, Party,
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
