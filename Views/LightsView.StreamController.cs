using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class LightsView
{
    private TextBlock? _scBrightnessValueLabel;
    private StyledSlider? _scBrightnessSlider;

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

        BuildStreamControllerLightsPanel();
    }

    private void BuildStreamControllerLightsPanel()
    {
        StreamControllerLightsContent.Children.Clear();

        StreamControllerLightsContent.Children.Add(new TextBlock
        {
            Text = "STREAM CONTROLLER OUTPUTS",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 0, 4)
        });
        StreamControllerLightsContent.Children.Add(new TextBlock
        {
            Text = "Display images are designed on the Buttons tab. Use this panel for Stream Controller display brightness.",
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var brightnessCard = new Border
        {
            Background = FindBrush("BgDarkBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Display Brightness",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold
        });

        _scBrightnessValueLabel = new TextBlock
        {
            Text = "100%",
            Margin = new Thickness(0, 6, 0, 8),
            Foreground = FindBrush("TextSecBrush")
        };
        stack.Children.Add(_scBrightnessValueLabel);

        _scBrightnessSlider = new StyledSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Step = 1,
            ShowLabel = false,
            AccentColor = ThemeManager.Accent
        };
        _scBrightnessSlider.ValueChanged += (_, _) =>
        {
            if (_scBrightnessValueLabel != null)
                _scBrightnessValueLabel.Text = $"{(int)Math.Round(_scBrightnessSlider.Value)}%";
            if (!_loading)
                QueueSave();
        };
        stack.Children.Add(_scBrightnessSlider);

        brightnessCard.Child = stack;
        StreamControllerLightsContent.Children.Add(brightnessCard);
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

        if (_scBrightnessSlider != null)
            _scBrightnessSlider.Value = Math.Clamp(config.N3.DisplayBrightness, 0, 100);
        if (_scBrightnessValueLabel != null)
            _scBrightnessValueLabel.Text = $"{Math.Clamp(config.N3.DisplayBrightness, 0, 100)}%";
    }

    private void SaveStreamControllerLightsConfig()
    {
        if (_config == null || _scBrightnessSlider == null) return;
        if (LightsDeviceSelector.SelectedTag is DeviceSurface surface)
            _config.TabSelection.Lights = surface;
        _config.N3.DisplayBrightness = (int)Math.Round(_scBrightnessSlider.Value);
    }

    private void UpdateLightsSurfaceVisibility(DeviceSurface surface)
    {
        TurnUpLightsContent.Visibility = surface is DeviceSurface.TurnUp or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
        StreamControllerLightsPanel.Visibility = surface is DeviceSurface.StreamController or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
