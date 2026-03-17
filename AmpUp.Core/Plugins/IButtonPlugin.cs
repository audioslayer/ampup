namespace AmpUp.Core.Plugins;

/// <summary>Plugin that provides a custom button action.</summary>
public interface IButtonPlugin : IPlugin
{
    /// <summary>Action identifier used in config (e.g. "plugin_obs_scene").</summary>
    string ActionId { get; }

    /// <summary>Display label shown in button action picker.</summary>
    string ActionLabel { get; }

    /// <summary>Category in the action picker (e.g. "Plugins").</summary>
    string ActionCategory => "Plugins";

    /// <summary>Called when the button gesture fires.</summary>
    void OnAction(int buttonIdx, string gesture);
}
