using System.Windows;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class MixerView
{
    private void InitializeMixerDeviceSelector()
    {
        MixerDeviceSelector.AccentColor = ThemeManager.Accent;
        MixerDeviceSelector.ClearSegments();
        MixerDeviceSelector.AddSegment("Turn Up", DeviceSurface.TurnUp);
        MixerDeviceSelector.AddSegment("Stream Controller", DeviceSurface.StreamController);
        MixerDeviceSelector.AddSegment("Both", DeviceSurface.Both);
        MixerDeviceSelector.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null) return;
            if (MixerDeviceSelector.SelectedTag is DeviceSurface surface)
            {
                _config.TabSelection.Mixer = surface;
                UpdateMixerSurfaceVisibility(surface);
                QueueSave();
            }
        };
    }

    private void LoadStreamControllerMixerConfig(AppConfig config)
    {
        MixerDeviceSelector.SelectedIndex = config.TabSelection.Mixer switch
        {
            DeviceSurface.StreamController => 1,
            DeviceSurface.Both => 2,
            _ => 0,
        };
        UpdateMixerSurfaceVisibility(config.TabSelection.Mixer);
    }

    private void UpdateMixerSurfaceVisibility(DeviceSurface surface)
    {
        // Turn Up mixer is always visible — it's the only mixer surface
        TurnUpMixerContent.Visibility = Visibility.Visible;
        // Stream Controller has no mixer UI — hide panel entirely
        StreamControllerMixerPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateStreamControllerMixerLiveState()
    {
        // No-op — SC mixer panel removed
    }
}
