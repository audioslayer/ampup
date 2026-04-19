using System.Windows;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class LightsView
{
    private void InitializeLightsDeviceSelector()
    {
        LightsDeviceSelector.AccentColor = ThemeManager.Accent;
        LightsDeviceSelector.ClearSegments();
        LightsDeviceSelector.AddSegment("Turn Up", DeviceSurface.TurnUp);
        LightsDeviceSelector.AddSegment("Stream Controller", DeviceSurface.StreamController);
        LightsDeviceSelector.AddSegment("Both", DeviceSurface.Both);
        LightsDeviceSelector.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            if (LightsDeviceSelector.SelectedTag is DeviceSurface surface)
            {
                _config.TabSelection.Lights = surface;
                UpdateLightsSurfaceVisibility(surface);
                QueueSave();
            }
        };
    }

    private void LoadStreamControllerLightsConfig(AppConfig config)
    {
        LightsDeviceSelector.SelectedIndex = config.TabSelection.Lights switch
        {
            DeviceSurface.StreamController => 1,
            DeviceSurface.Both => 2,
            _ => 0,
        };
        UpdateLightsSurfaceVisibility(config.TabSelection.Lights);
    }

    private void SaveStreamControllerLightsConfig()
    {
        if (_config == null) return;
        if (LightsDeviceSelector.SelectedTag is DeviceSurface surface)
            _config.TabSelection.Lights = surface;
    }

    private void UpdateLightsSurfaceVisibility(DeviceSurface surface)
    {
        // Turn Up lights content: show for TurnUp or Both
        TurnUpLightsContent.Visibility = surface is DeviceSurface.TurnUp or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
        // Stream Controller has no lights settings — hide its panel entirely
        StreamControllerLightsPanel.Visibility = Visibility.Collapsed;
    }
}
