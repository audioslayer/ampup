namespace AmpUp.Core.Plugins;

/// <summary>Base interface for all AmpUp plugins.</summary>
public interface IPlugin
{
    /// <summary>Unique plugin identifier (e.g. "com.example.myplugin").</summary>
    string Id { get; }

    /// <summary>Display name shown in settings.</summary>
    string Name { get; }

    /// <summary>Semantic version string (e.g. "1.0.0").</summary>
    string Version { get; }

    /// <summary>Optional description shown in plugin manager.</summary>
    string Description => "";

    /// <summary>Called once when the plugin is loaded.</summary>
    void Initialize(IPluginHost host);

    /// <summary>Called when AmpUp is shutting down or the plugin is unloaded.</summary>
    void Shutdown();
}
