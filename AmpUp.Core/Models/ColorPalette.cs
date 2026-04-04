namespace AmpUp.Core.Models;

/// <summary>
/// A single color stop in a gradient palette, with a position (0.0–1.0) and RGB color.
/// </summary>
public class ColorStop
{
    public double Position { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public ColorStop() { }

    public ColorStop(double position, byte r, byte g, byte b)
    {
        Position = position;
        R = r;
        G = g;
        B = b;
    }
}

/// <summary>
/// A named color palette with 2–8 gradient stops. Effects sample this via Sample(t)
/// where t is 0.0–1.0 and the effect decides what t means (position, time, intensity, etc.).
/// </summary>
public class ColorPalette
{
    public string Name { get; set; } = "";
    public List<ColorStop> Stops { get; set; } = new();

    public ColorPalette() { }

    public ColorPalette(string name, params ColorStop[] stops)
    {
        Name = name;
        Stops = new List<ColorStop>(stops);
    }

    /// <summary>
    /// Sample the palette at position t (0.0–1.0). Linearly interpolates between
    /// the two bracketing stops. Returns (R, G, B) as integers 0–255.
    /// </summary>
    public (int R, int G, int B) Sample(float t)
    {
        if (Stops.Count == 0) return (0, 0, 0);
        if (Stops.Count == 1) return (Stops[0].R, Stops[0].G, Stops[0].B);

        t = Math.Clamp(t, 0f, 1f);

        // Stops should be sorted by position — find bracketing pair
        ColorStop? prev = null;
        ColorStop? next = null;
        for (int i = 0; i < Stops.Count; i++)
        {
            if (Stops[i].Position <= t) prev = Stops[i];
            if (Stops[i].Position >= t && next == null) next = Stops[i];
        }

        prev ??= Stops[0];
        next ??= Stops[^1];

        if (prev == next || Math.Abs(prev.Position - next.Position) < 0.001)
            return (prev.R, prev.G, prev.B);

        float f = (float)((t - prev.Position) / (next.Position - prev.Position));
        return (
            Math.Clamp((int)(prev.R + (next.R - prev.R) * f), 0, 255),
            Math.Clamp((int)(prev.G + (next.G - prev.G) * f), 0, 255),
            Math.Clamp((int)(prev.B + (next.B - prev.B) * f), 0, 255)
        );
    }

    /// <summary>
    /// Create a simple 2-stop palette from legacy color1/color2 values.
    /// </summary>
    public static ColorPalette FromTwoColors(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        return new ColorPalette("Custom",
            new ColorStop(0.0, (byte)r1, (byte)g1, (byte)b1),
            new ColorStop(1.0, (byte)r2, (byte)g2, (byte)b2));
    }

    /// <summary>
    /// Create a palette from evenly-spaced colors (no explicit positions).
    /// </summary>
    public static ColorPalette FromColors(string name, params (byte R, byte G, byte B)[] colors)
    {
        var stops = new ColorStop[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            double pos = colors.Length == 1 ? 0.5 : (double)i / (colors.Length - 1);
            stops[i] = new ColorStop(pos, colors[i].R, colors[i].G, colors[i].B);
        }
        return new ColorPalette(name, stops);
    }
}
