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
        ("Ember",      "#140A08", "#1C100C", "#281810", "#3A2418", "#301E16", "#4A3228"),
        ("Forest",     "#081208", "#0C1A0E", "#122414", "#1C3420", "#182C1A", "#264030"),
        ("Violet",     "#100A16", "#18101E", "#22182C", "#30243C", "#2A1E34", "#3E3050"),
        ("Slate",      "#0C0E12", "#121418", "#1A1E24", "#282E36", "#22282E", "#343C44"),
        ("Obsidian",   "#060606", "#0A0A0A", "#101010", "#1A1A1A", "#151515", "#222222"),
        ("Mocha",      "#120C08", "#1A1210", "#261C16", "#382A22", "#2E221A", "#443830"),
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

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
