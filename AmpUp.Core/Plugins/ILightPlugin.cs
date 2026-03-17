namespace AmpUp.Core.Plugins;

/// <summary>Plugin that provides a custom LED effect.</summary>
public interface ILightPlugin : IPlugin
{
    /// <summary>Effect name shown in effect picker (e.g. "Aurora Borealis").</summary>
    string EffectName { get; }

    /// <summary>Category in the effect picker (e.g. "Plugins").</summary>
    string EffectCategory => "Plugins";

    /// <summary>Whether this effect uses Color2.</summary>
    bool UsesSecondColor => false;

    /// <summary>Whether this effect uses speed control.</summary>
    bool UsesSpeed => true;

    /// <summary>
    /// Called each animation tick (20 FPS) to compute LED colors.
    /// Returns array of 3 (r,g,b) tuples for the 3 LEDs on the knob.
    /// </summary>
    (byte R, byte G, byte B)[] Render(int knobIdx, int tick, int speed,
        byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, float[] audioBands);
}
