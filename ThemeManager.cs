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
        ("Blue Steel", "#0D0F12", "#121518", "#191D22", "#272C33", "#21252C", "#343A44"),
        ("Ember",      "#120E0D", "#171211", "#1E1918", "#2C2524", "#262020", "#38302F"),
        ("Forest",     "#0D110E", "#121613", "#191E1A", "#272C28", "#212621", "#333A34"),
        ("Violet",     "#100D12", "#151117", "#1C181F", "#29252D", "#232028", "#353039"),
        ("Slate",      "#0E0F11", "#131416", "#1B1C1F", "#29292E", "#232427", "#363739"),
        ("Obsidian",   "#080808", "#0C0C0C", "#131313", "#1F1F1F", "#191919", "#282828"),
        ("Mocha",      "#110F0D", "#161311", "#1E1A18", "#2C2826", "#252120", "#373230"),
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
