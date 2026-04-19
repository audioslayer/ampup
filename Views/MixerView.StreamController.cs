using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class MixerView
{
    private readonly TextBlock[] _scMixerKnobLabels = new TextBlock[3];
    private readonly TextBlock[] _scMixerKnobTargets = new TextBlock[3];

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

        BuildStreamControllerMixerPanel();
    }

    private void BuildStreamControllerMixerPanel()
    {
        StreamControllerMixerContent.Children.Clear();

        StreamControllerMixerContent.Children.Add(new TextBlock
        {
            Text = "STREAM CONTROLLER",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 0, 4)
        });
        StreamControllerMixerContent.Children.Add(new TextBlock
        {
            Text = "The three Stream Controller encoders currently mirror the first three Amp Up mixer knobs.",
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var row = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 8)
        };

        for (int i = 0; i < 3; i++)
        {
            var card = new Border
            {
                Background = FindBrush("BgDarkBrush"),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0),
                Padding = new Thickness(14)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"Encoder {i + 1}",
                Foreground = FindBrush("TextDimBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            _scMixerKnobLabels[i] = new TextBlock
            {
                Text = $"Knob {i + 1}",
                Margin = new Thickness(0, 8, 0, 4),
                Foreground = FindBrush("TextPrimaryBrush"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            _scMixerKnobTargets[i] = new TextBlock
            {
                Text = "Unassigned",
                Foreground = FindBrush("TextSecBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            stack.Children.Add(_scMixerKnobLabels[i]);
            stack.Children.Add(_scMixerKnobTargets[i]);
            card.Child = stack;
            row.Children.Add(card);
        }

        StreamControllerMixerContent.Children.Add(row);
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

        for (int i = 0; i < 3; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
            _scMixerKnobLabels[i].Text = string.IsNullOrWhiteSpace(knob.Label) ? $"Knob {i + 1}" : knob.Label;
            _scMixerKnobTargets[i].Text = string.IsNullOrWhiteSpace(knob.Target)
                ? "Unassigned"
                : knob.Target == "apps" && knob.Apps.Count > 0
                    ? $"App Group: {string.Join(", ", knob.Apps)}"
                    : knob.Target;
        }
    }

    private void UpdateMixerSurfaceVisibility(DeviceSurface surface)
    {
        TurnUpMixerContent.Visibility = surface is DeviceSurface.TurnUp or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
        StreamControllerMixerPanel.Visibility = surface is DeviceSurface.StreamController or DeviceSurface.Both
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
