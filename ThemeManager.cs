using System.Windows;
using System.Windows.Media;

namespace AmpUp;

public static class ThemeManager
{
    public static Color Accent { get; private set; } = Color.FromRgb(0x00, 0xE6, 0x76);
    public static Color AccentGlow { get; private set; }
    public static Color AccentDim { get; private set; }
    public static string CurrentCardTheme { get; private set; } = "Midnight";

    // Card theme presets: (Name, BgBase, BgDark, CardBg, CardBorder, InputBg, InputBorder)
    public static readonly (string Name, string BgBase, string BgDark, string CardBg, string CardBorder, string InputBg, string InputBorder)[] CardThemes =
    {
        ("Midnight",   "#0F0F0F", "#141414", "#1C1C1C", "#2A2A2A", "#242424", "#363636"),
        ("Blue Steel", "#0A0E14", "#0F1520", "#152230", "#1E3048", "#1A2838", "#2A3E56"),
        ("Ocean",      "#081014", "#0C1820", "#10223A", "#183050", "#142840", "#1E3E5E"),
        ("Teal",       "#081210", "#0C1A18", "#102824", "#183834", "#14302C", "#1E4842"),
        ("Ice",        "#0C1218", "#101A24", "#182838", "#24384E", "#1E3040", "#2C4660"),
        ("Ember",      "#140A08", "#1C100C", "#281810", "#3A2418", "#301E16", "#4A3228"),
        ("Forest",     "#081208", "#0C1A0E", "#122414", "#1C3420", "#182C1A", "#264030"),
        ("Violet",     "#100A16", "#18101E", "#22182C", "#30243C", "#2A1E34", "#3E3050"),
        ("Rose",       "#140A10", "#1E0E16", "#2C1420", "#3E2030", "#341A28", "#4C2C3E"),
        ("Slate",      "#0C0E12", "#121418", "#1A1E24", "#282E36", "#22282E", "#343C44"),
        ("Obsidian",   "#060606", "#0A0A0A", "#101010", "#1A1A1A", "#151515", "#222222"),
        ("Mocha",      "#120C08", "#1A1210", "#261C16", "#382A22", "#2E221A", "#443830"),
        ("Burgundy",   "#140608", "#1E0A0E", "#2C1018", "#3E1C28", "#341420", "#4C2838"),
        ("Charcoal",   "#0E0E0E", "#161616", "#1E1E1E", "#2C2C2C", "#262626", "#383838"),
        ("Cobalt",     "#060A14", "#0A1020", "#101A30", "#182848", "#142038", "#1E3456"),
        ("Copper",     "#140E08", "#1E1610", "#2C2018", "#3E3026", "#342820", "#4C3E34"),
        ("Graphite",   "#0A0A0C", "#101014", "#18181E", "#26262E", "#20202A", "#32323E"),
        ("Indigo",     "#08061A", "#0E0A26", "#161236", "#221C4E", "#1C1640", "#2A2260"),
        ("Jade",       "#061008", "#0A1810", "#102218", "#183428", "#142C20", "#1E4234"),
        ("Plum",       "#120816", "#1A0E20", "#26142E", "#382040", "#301A38", "#482C50"),
        ("Storm",      "#0A0C10", "#101418", "#181E26", "#242E38", "#1E2830", "#303E48"),
        ("Wine",       "#160810", "#200E18", "#2E1422", "#422034", "#381A2C", "#502C42"),
    };

    /// <summary>
    /// Sets the card/background theme and updates all background-related resources.
    /// Call from UI thread.
    /// </summary>
    public static void SetCardTheme(string themeName)
    {
        var theme = Array.Find(CardThemes, t => t.Name == themeName);
        if (theme.Name == null) return; // invalid name

        CurrentCardTheme = themeName;

        var bgBase = (Color)ColorConverter.ConvertFromString(theme.BgBase);
        var bgDark = (Color)ColorConverter.ConvertFromString(theme.BgDark);
        var cardBg = (Color)ColorConverter.ConvertFromString(theme.CardBg);
        var cardBorder = (Color)ColorConverter.ConvertFromString(theme.CardBorder);
        var inputBg = (Color)ColorConverter.ConvertFromString(theme.InputBg);
        var inputBorder = (Color)ColorConverter.ConvertFromString(theme.InputBorder);

        var res = Application.Current.Resources;

        // Update colors
        res["BgBase"] = bgBase;
        res["BgDark"] = bgDark;
        res["CardBg"] = cardBg;
        res["CardBorder"] = cardBorder;
        res["InputBg"] = inputBg;
        res["InputBorder"] = inputBorder;

        // Update brushes
        res["BgBaseBrush"] = Freeze(new SolidColorBrush(bgBase));
        res["BgDarkBrush"] = Freeze(new SolidColorBrush(bgDark));
        res["CardBgBrush"] = Freeze(new SolidColorBrush(cardBg));
        res["CardBorderBrush"] = Freeze(new SolidColorBrush(cardBorder));
        res["InputBgBrush"] = Freeze(new SolidColorBrush(inputBg));
        res["InputBorderBrush"] = Freeze(new SolidColorBrush(inputBorder));

        OnCardThemeChanged?.Invoke();
    }

    /// <summary>Event fired after card theme changes.</summary>
    public static event Action? OnCardThemeChanged;

    /// <summary>
    /// Sets the accent color and updates all accent-related resources in Application.Current.Resources.
    /// Call from UI thread.
    /// </summary>
    public static void SetAccentColor(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        SetAccentColor(color);
    }

    public static void SetAccentColor(Color color)
    {
        Accent = color;
        AccentGlow = Lighten(color, 0.4f);
        AccentDim = Darken(color, 0.35f);

        var res = Application.Current.Resources;

        // Core colors
        res["Accent"] = Accent;
        res["AccentGlow"] = AccentGlow;
        res["AccentDim"] = AccentDim;

        // Core brushes
        res["AccentBrush"] = Freeze(new SolidColorBrush(Accent));
        res["AccentGlowBrush"] = Freeze(new SolidColorBrush(AccentGlow));
        res["AccentDimBrush"] = Freeze(new SolidColorBrush(AccentDim));

        // Alpha-variant brushes used throughout the UI
        res["AccentBorder55Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x55)));
        res["AccentBorder22Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x22)));
        res["AccentBorder33Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x33)));
        res["AccentBorder44Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x44)));
        res["AccentHover1ABrush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x1A)));
        res["AccentHover18Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x18)));
        res["AccentBg30Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x30)));
        res["AccentLabel66Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x66)));
        res["AccentLabel77Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x77)));
        res["AccentLabel88Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x88)));

        // Scrollbar alpha variants
        res["AccentScroll40Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x40)));
        res["AccentScroll70Brush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0x70)));
        res["AccentScrollAABrush"] = Freeze(new SolidColorBrush(WithAlpha(Accent, 0xAA)));

        // For DropShadowEffect Color bindings (can't bind DropShadow Color to DynamicResource,
        // so we provide the raw Color as a resource)
        // Code-behind will read ThemeManager.Accent directly for DropShadowEffect colors.

        OnAccentChanged?.Invoke();
    }

    /// <summary>Event fired after accent color changes. Use to rebuild code-behind UI elements.</summary>
    public static event Action? OnAccentChanged;

    /// <summary>Creates a Color with the given alpha and the RGB from the source.</summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Returns accent hex string like "#00E676".</summary>
    public static string AccentHex => $"#{Accent.R:X2}{Accent.G:X2}{Accent.B:X2}";

    private static Color Lighten(Color c, float amount)
    {
        byte r = (byte)Math.Min(255, c.R + (255 - c.R) * amount);
        byte g = (byte)Math.Min(255, c.G + (255 - c.G) * amount);
        byte b = (byte)Math.Min(255, c.B + (255 - c.B) * amount);
        return Color.FromRgb(r, g, b);
    }

    private static Color Darken(Color c, float amount)
    {
        byte r = (byte)(c.R * (1 - amount));
        byte g = (byte)(c.G * (1 - amount));
        byte b = (byte)(c.B * (1 - amount));
        return Color.FromRgb(r, g, b);
    }

    /// <summary>
    /// Generates a dark card theme from a single seed color (hue tint).
    /// The seed is desaturated and darkened to produce 6 coordinated background shades.
    /// </summary>
    public static (string BgBase, string BgDark, string CardBg, string CardBorder, string InputBg, string InputBorder)
        GenerateThemeFromColor(Color seed)
    {
        // Extract hue from seed, then generate very dark tinted shades
        float r = seed.R / 255f, g = seed.G / 255f, b = seed.B / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float hue = 0;
        if (max != min)
        {
            float d = max - min;
            if (max == r) hue = ((g - b) / d + (g < b ? 6 : 0)) * 60;
            else if (max == g) hue = ((b - r) / d + 2) * 60;
            else hue = ((r - g) / d + 4) * 60;
        }

        // Generate 6 shades with the seed hue at very low saturation and brightness
        Color Shade(float sat, float val)
        {
            float h = hue / 60f;
            int i = (int)Math.Floor(h) % 6;
            float f = h - (float)Math.Floor(h);
            float p = val * (1 - sat);
            float q = val * (1 - sat * f);
            float t = val * (1 - sat * (1 - f));
            var (rv, gv, bv) = i switch
            {
                0 => (val, t, p), 1 => (q, val, p), 2 => (p, val, t),
                3 => (p, q, val), 4 => (t, p, val), _ => (val, p, q),
            };
            return Color.FromRgb((byte)(rv * 255), (byte)(gv * 255), (byte)(bv * 255));
        }

        string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        return (
            BgBase:      Hex(Shade(0.25f, 0.055f)),
            BgDark:      Hex(Shade(0.25f, 0.085f)),
            CardBg:      Hex(Shade(0.22f, 0.13f)),
            CardBorder:  Hex(Shade(0.20f, 0.20f)),
            InputBg:     Hex(Shade(0.22f, 0.16f)),
            InputBorder: Hex(Shade(0.20f, 0.26f))
        );
    }

    /// <summary>
    /// Applies a custom card theme generated from a seed color.
    /// </summary>
    public static void SetCustomTheme(Color seed)
    {
        var t = GenerateThemeFromColor(seed);
        CurrentCardTheme = "Custom";

        var bgBase = (Color)ColorConverter.ConvertFromString(t.BgBase);
        var bgDark = (Color)ColorConverter.ConvertFromString(t.BgDark);
        var cardBg = (Color)ColorConverter.ConvertFromString(t.CardBg);
        var cardBorder = (Color)ColorConverter.ConvertFromString(t.CardBorder);
        var inputBg = (Color)ColorConverter.ConvertFromString(t.InputBg);
        var inputBorder = (Color)ColorConverter.ConvertFromString(t.InputBorder);

        var res = Application.Current.Resources;
        res["BgBase"] = bgBase;       res["BgDark"] = bgDark;
        res["CardBg"] = cardBg;       res["CardBorder"] = cardBorder;
        res["InputBg"] = inputBg;     res["InputBorder"] = inputBorder;
        res["BgBaseBrush"] = Freeze(new SolidColorBrush(bgBase));
        res["BgDarkBrush"] = Freeze(new SolidColorBrush(bgDark));
        res["CardBgBrush"] = Freeze(new SolidColorBrush(cardBg));
        res["CardBorderBrush"] = Freeze(new SolidColorBrush(cardBorder));
        res["InputBgBrush"] = Freeze(new SolidColorBrush(inputBg));
        res["InputBorderBrush"] = Freeze(new SolidColorBrush(inputBorder));

        OnCardThemeChanged?.Invoke();
    }

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
