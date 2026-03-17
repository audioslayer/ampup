using AmpUp.Core.Models;

namespace AmpUp.Core.Plugins;

/// <summary>Host services exposed to plugins. Injected via IPlugin.Initialize().</summary>
public interface IPluginHost
{
    /// <summary>Current application config (read-only snapshot).</summary>
    AppConfig Config { get; }

    /// <summary>Log a message to AmpUp's log file.</summary>
    void Log(string message);

    /// <summary>Subscribe to serial knob events. Callback: (knobIdx, rawValue 0-1023).</summary>
    IDisposable OnKnobEvent(Action<int, int> handler);

    /// <summary>Subscribe to serial button events. Callback: (buttonIdx, isDown).</summary>
    IDisposable OnButtonEvent(Action<int, bool> handler);

    /// <summary>Get current audio levels per frequency band (5 bands, 0.0-1.0).</summary>
    float[] GetAudioBands();

    /// <summary>Set LED color for a specific knob and LED index.</summary>
    void SetLedColor(int knobIdx, int ledIdx, byte r, byte g, byte b);

    /// <summary>Request a config save (debounced).</summary>
    void RequestConfigSave();
}
