namespace AmpUp.Core.Plugins;

/// <summary>Plugin that provides a custom knob target (volume control destination).</summary>
public interface IKnobPlugin : IPlugin
{
    /// <summary>Target identifier used in config (e.g. "voicemeeter_strip1").</summary>
    string TargetId { get; }

    /// <summary>Display label shown in knob target picker.</summary>
    string TargetLabel { get; }

    /// <summary>Category in the target picker (e.g. "Plugins").</summary>
    string TargetCategory => "Plugins";

    /// <summary>Called when the knob value changes (0.0-1.0 after curve).</summary>
    void OnKnobChanged(int knobIdx, float normalizedValue);

    /// <summary>Get current volume level for display (0.0-1.0).</summary>
    float GetCurrentLevel();
}
