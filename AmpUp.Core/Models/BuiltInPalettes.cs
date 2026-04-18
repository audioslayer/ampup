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

    public static readonly ColorPalette Opaline = new("Opaline",
        new ColorStop(0.00, 0x1F, 0x12, 0x35),   // shadow violet
        new ColorStop(0.16, 0x4B, 0x39, 0x89),   // deep amethyst
        new ColorStop(0.32, 0x7A, 0x6F, 0xD7),   // opal purple
        new ColorStop(0.50, 0x92, 0xE3, 0xE6),   // sea glass cyan
        new ColorStop(0.68, 0xB8, 0xF1, 0xD7),   // mint pearl
        new ColorStop(0.84, 0xF6, 0xB7, 0xE8),   // pearlescent pink
        new ColorStop(1.00, 0xF3, 0xF2, 0xFF));  // milky white

    public static readonly ColorPalette Voltage = new("Voltage",
        new ColorStop(0.00, 0x05, 0x0A, 0x20),   // blackout navy
        new ColorStop(0.15, 0x00, 0x2A, 0x7A),   // charged blue
        new ColorStop(0.32, 0x00, 0x6A, 0xFF),   // electric blue
        new ColorStop(0.50, 0x00, 0xF5, 0xFF),   // plasma cyan
        new ColorStop(0.68, 0x4A, 0xFF, 0xD9),   // energized aqua
        new ColorStop(0.84, 0x8A, 0xFF, 0x00),   // acid lime
        new ColorStop(1.00, 0xF8, 0xFF, 0xD1));  // ionized glow

    public static readonly ColorPalette EmberBloom = new("Ember Bloom",
        new ColorStop(0.00, 0x18, 0x04, 0x00),   // coal black
        new ColorStop(0.14, 0x4B, 0x08, 0x00),   // buried ember
        new ColorStop(0.30, 0x8A, 0x14, 0x00),   // molten red
        new ColorStop(0.48, 0xD9, 0x34, 0x00),   // hot ember
        new ColorStop(0.64, 0xFF, 0x63, 0x00),   // orange flame
        new ColorStop(0.82, 0xFF, 0xB1, 0x2A),   // amber petal
        new ColorStop(1.00, 0xFF, 0xE6, 0x9A));  // pale gold

    public static readonly ColorPalette DeepSea = new("Deep Sea",
        new ColorStop(0.00, 0x01, 0x06, 0x10),   // abyss trench
        new ColorStop(0.16, 0x00, 0x16, 0x2D),   // deep current
        new ColorStop(0.32, 0x00, 0x2D, 0x52),   // midnight current
        new ColorStop(0.50, 0x00, 0x76, 0x8F),   // cold teal
        new ColorStop(0.68, 0x00, 0xA5, 0xAF),   // open water glow
        new ColorStop(0.84, 0x2E, 0xC4, 0xC7),   // bioluminescent aqua
        new ColorStop(1.00, 0xB7, 0xFF, 0xF4));  // surface shimmer

    public static readonly ColorPalette CandyPop = new("Candy Pop",
        new ColorStop(0.00, 0xFF, 0x3C, 0x8E),   // candy pink
        new ColorStop(0.16, 0xFF, 0x5B, 0x77),   // punch berry
        new ColorStop(0.32, 0xFF, 0x7A, 0x59),   // coral punch
        new ColorStop(0.50, 0xFF, 0xD1, 0x3B),   // mango pop
        new ColorStop(0.68, 0xB7, 0xF0, 0x48),   // citrus fizz
        new ColorStop(0.84, 0x6A, 0xF0, 0x7D),   // sour lime
        new ColorStop(1.00, 0x47, 0xB8, 0xFF));  // bubblegum blue

    public static readonly ColorPalette MidnightCity = new("Midnight City",
        new ColorStop(0.00, 0x06, 0x08, 0x16),   // city night
        new ColorStop(0.16, 0x18, 0x12, 0x34),   // alley shadow
        new ColorStop(0.32, 0x32, 0x1B, 0x59),   // neon alley
        new ColorStop(0.50, 0xA5, 0x23, 0xA7),   // magenta sign glow
        new ColorStop(0.68, 0xFF, 0x4A, 0xA3),   // hot signage
        new ColorStop(0.84, 0x00, 0xC2, 0xFF),   // cyan skyline
        new ColorStop(1.00, 0xF4, 0x63, 0x7B));  // distant tail lights

    public static readonly ColorPalette TropicalPunch = new("Tropical Punch",
        new ColorStop(0.00, 0x4A, 0x00, 0x5E),   // plum dusk
        new ColorStop(0.16, 0xA0, 0x11, 0x75),   // orchid bloom
        new ColorStop(0.32, 0xFF, 0x4D, 0x6D),   // hibiscus pink
        new ColorStop(0.50, 0xFF, 0x8C, 0x42),   // papaya orange
        new ColorStop(0.68, 0xFF, 0xB8, 0x50),   // sunlit mango
        new ColorStop(0.84, 0xFF, 0xD1, 0x66),   // golden sun
        new ColorStop(1.00, 0x00, 0xD6, 0xB9));  // tropical lagoon

    public static readonly ColorPalette NorthernSky = new("Northern Sky",
        new ColorStop(0.00, 0x03, 0x0D, 0x1D),   // polar night
        new ColorStop(0.16, 0x00, 0x2C, 0x46),   // frost blue
        new ColorStop(0.32, 0x00, 0x56, 0x7A),   // cold blue
        new ColorStop(0.50, 0x00, 0xC9, 0x88),   // aurora green
        new ColorStop(0.68, 0x35, 0xE1, 0xC3),   // polar shimmer
        new ColorStop(0.84, 0x70, 0x6C, 0xFF),   // indigo veil
        new ColorStop(1.00, 0xD8, 0xF5, 0xFF));  // frozen glow

    public static readonly ColorPalette RoseGold = new("Rose Gold",
        new ColorStop(0.00, 0x20, 0x0B, 0x12),   // dark rose shadow
        new ColorStop(0.16, 0x55, 0x1F, 0x33),   // wine rose
        new ColorStop(0.32, 0x8F, 0x3B, 0x57),   // muted rose
        new ColorStop(0.50, 0xD7, 0x86, 0x8A),   // blush metal
        new ColorStop(0.68, 0xE8, 0xA0, 0x92),   // polished blush
        new ColorStop(0.84, 0xF0, 0xB2, 0x91),   // champagne copper
        new ColorStop(1.00, 0xFF, 0xE8, 0xD0));  // soft highlight

    public static readonly ColorPalette DreamState = new("Dream State",
        new ColorStop(0.00, 0x12, 0x0E, 0x33),   // sleep blue
        new ColorStop(0.16, 0x2A, 0x1C, 0x61),   // deep dream violet
        new ColorStop(0.32, 0x4A, 0x2D, 0x8C),   // amethyst
        new ColorStop(0.50, 0x9A, 0x63, 0xFF),   // lucid violet
        new ColorStop(0.68, 0xD4, 0x7D, 0xF0),   // dream glow
        new ColorStop(0.84, 0xFF, 0x8F, 0xD8),   // dream pink
        new ColorStop(1.00, 0x8D, 0xF1, 0xFF));  // soft cyan wake

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
        [Opaline.Name] = Opaline,
        [Voltage.Name] = Voltage,
        [EmberBloom.Name] = EmberBloom,
        [DeepSea.Name] = DeepSea,
        [CandyPop.Name] = CandyPop,
        [MidnightCity.Name] = MidnightCity,
        [TropicalPunch.Name] = TropicalPunch,
        [NorthernSky.Name] = NorthernSky,
        [RoseGold.Name] = RoseGold,
        [DreamState.Name] = DreamState,
    };

    /// <summary>
    /// All built-in palettes in display order.
    /// </summary>
    public static readonly ColorPalette[] All =
    {
        Fire, Ocean, Sunset, Neon, Arctic, Forest, Lava, Galaxy,
        Aurora, Vaporwave, Cyberpunk, Sakura, Twilight, CoralReef,
        Lavender, Copper, Opaline, Voltage, EmberBloom, DeepSea,
        CandyPop, MidnightCity, TropicalPunch, NorthernSky, RoseGold, DreamState,
        Rainbow, WarmWhite, CoolWhite, Party,
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
