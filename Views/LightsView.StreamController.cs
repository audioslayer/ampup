using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class LightsView
{
    private TextBlock? _scBrightnessValueLabel;
    private StyledSlider? _scBrightnessSlider;
    private CheckBox? _scScreensaverEnabled;
    private SegmentedControl? _scScreensaverEffect;
    private StyledSlider? _scScreensaverOpacitySlider;
    private StyledSlider? _scScreensaverSpeedSlider;
    private TextBlock? _scScreensaverOpacityLabel;
    private TextBlock? _scScreensaverSpeedLabel;
    private readonly Image[] _scScreensaverPreviewImages = new Image[6];

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

        StreamControllerLightsContent.Children.Add(BuildStreamControllerScreensaverCard());
    }

    private Border BuildStreamControllerScreensaverCard()
    {
        var card = new Border
        {
            Background = FindBrush("BgDarkBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Display Screensaver Overlay",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Lay animated color over your Stream Controller keys like a mini screensaver. Your button art stays underneath and the effect rides on top.",
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 6, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        _scScreensaverEnabled = new CheckBox
        {
            Content = "Enable display overlay effect",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scScreensaverEnabled.Checked += (_, _) => { UpdateStreamControllerScreensaverControls(); if (!_loading) QueueSave(); };
        _scScreensaverEnabled.Unchecked += (_, _) => { UpdateStreamControllerScreensaverControls(); if (!_loading) QueueSave(); };
        stack.Children.Add(_scScreensaverEnabled);

        _scScreensaverEffect = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scScreensaverEffect.AddSegment("Rainbow", StreamControllerScreensaverEffect.Rainbow);
        _scScreensaverEffect.AddSegment("Fire", StreamControllerScreensaverEffect.Fire);
        _scScreensaverEffect.AddSegment("Music", StreamControllerScreensaverEffect.MusicBounce);
        _scScreensaverEffect.SelectionChanged += (_, _) =>
        {
            RefreshStreamControllerScreensaverPreview();
            if (!_loading) QueueSave();
        };
        stack.Children.Add(_scScreensaverEffect);

        _scScreensaverOpacityLabel = new TextBlock
        {
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(_scScreensaverOpacityLabel);

        _scScreensaverOpacitySlider = new StyledSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 55,
            Step = 1,
            ShowLabel = false,
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scScreensaverOpacitySlider.ValueChanged += (_, _) =>
        {
            if (_scScreensaverOpacityLabel != null)
                _scScreensaverOpacityLabel.Text = $"Overlay Strength: {(int)Math.Round(_scScreensaverOpacitySlider.Value)}%";
            RefreshStreamControllerScreensaverPreview();
            if (!_loading) QueueSave();
        };
        stack.Children.Add(_scScreensaverOpacitySlider);

        _scScreensaverSpeedLabel = new TextBlock
        {
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(_scScreensaverSpeedLabel);

        _scScreensaverSpeedSlider = new StyledSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Step = 1,
            ShowLabel = false,
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _scScreensaverSpeedSlider.ValueChanged += (_, _) =>
        {
            if (_scScreensaverSpeedLabel != null)
                _scScreensaverSpeedLabel.Text = $"Animation Speed: {(int)Math.Round(_scScreensaverSpeedSlider.Value)}%";
            RefreshStreamControllerScreensaverPreview();
            if (!_loading) QueueSave();
        };
        stack.Children.Add(_scScreensaverSpeedSlider);

        stack.Children.Add(new TextBlock
        {
            Text = "Preview",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var previewGrid = new UniformGrid
        {
            Columns = 3,
            Rows = 2
        };
        for (int i = 0; i < 6; i++)
        {
            var image = new Image
            {
                Width = 56,
                Height = 56,
                Stretch = Stretch.UniformToFill
            };
            _scScreensaverPreviewImages[i] = image;

            previewGrid.Children.Add(new Border
            {
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(10),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                Background = FindBrush("BgBaseBrush"),
                Padding = new Thickness(4),
                Child = image
            });
        }
        stack.Children.Add(previewGrid);

        card.Child = stack;
        return card;
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
        if (_scScreensaverEnabled != null)
            _scScreensaverEnabled.IsChecked = config.N3.ScreensaverEnabled;
        if (_scScreensaverEffect != null)
            _scScreensaverEffect.SelectedIndex = config.N3.ScreensaverEffect switch
            {
                StreamControllerScreensaverEffect.Fire => 1,
                StreamControllerScreensaverEffect.MusicBounce => 2,
                _ => 0,
            };
        if (_scScreensaverOpacitySlider != null)
            _scScreensaverOpacitySlider.Value = Math.Clamp(config.N3.ScreensaverOpacity, 0, 100);
        if (_scScreensaverSpeedSlider != null)
            _scScreensaverSpeedSlider.Value = Math.Clamp(config.N3.ScreensaverSpeed, 0, 100);
        if (_scScreensaverOpacityLabel != null)
            _scScreensaverOpacityLabel.Text = $"Overlay Strength: {Math.Clamp(config.N3.ScreensaverOpacity, 0, 100)}%";
        if (_scScreensaverSpeedLabel != null)
            _scScreensaverSpeedLabel.Text = $"Animation Speed: {Math.Clamp(config.N3.ScreensaverSpeed, 0, 100)}%";
        UpdateStreamControllerScreensaverControls();
        RefreshStreamControllerScreensaverPreview();
    }

    private void SaveStreamControllerLightsConfig()
    {
        if (_config == null || _scBrightnessSlider == null) return;
        if (LightsDeviceSelector.SelectedTag is DeviceSurface surface)
            _config.TabSelection.Lights = surface;
        _config.N3.DisplayBrightness = (int)Math.Round(_scBrightnessSlider.Value);
        _config.N3.ScreensaverEnabled = _scScreensaverEnabled?.IsChecked == true;
        if (_scScreensaverEffect?.SelectedTag is StreamControllerScreensaverEffect effect)
            _config.N3.ScreensaverEffect = effect;
        if (_scScreensaverOpacitySlider != null)
            _config.N3.ScreensaverOpacity = (int)Math.Round(_scScreensaverOpacitySlider.Value);
        if (_scScreensaverSpeedSlider != null)
            _config.N3.ScreensaverSpeed = (int)Math.Round(_scScreensaverSpeedSlider.Value);
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

    private void UpdateStreamControllerScreensaverControls()
    {
        bool enabled = _scScreensaverEnabled?.IsChecked == true;
        if (_scScreensaverEffect != null) _scScreensaverEffect.IsEnabled = enabled;
        if (_scScreensaverOpacitySlider != null) _scScreensaverOpacitySlider.IsEnabled = enabled;
        if (_scScreensaverSpeedSlider != null) _scScreensaverSpeedSlider.IsEnabled = enabled;
        RefreshStreamControllerScreensaverPreview();
    }

    private void RefreshStreamControllerScreensaverPreview()
    {
        if (_config == null) return;

        var previewConfig = new N3Config
        {
            ScreensaverEnabled = _scScreensaverEnabled?.IsChecked == true,
            ScreensaverEffect = _scScreensaverEffect?.SelectedTag is StreamControllerScreensaverEffect effect
                ? effect
                : StreamControllerScreensaverEffect.Rainbow,
            ScreensaverOpacity = _scScreensaverOpacitySlider != null ? (int)Math.Round(_scScreensaverOpacitySlider.Value) : 55,
            ScreensaverSpeed = _scScreensaverSpeedSlider != null ? (int)Math.Round(_scScreensaverSpeedSlider.Value) : 50
        };

        var frame = new StreamControllerDisplayRenderer.StreamControllerEffectFrame(
            Environment.TickCount,
            App.AudioAnalyzer?.SmoothedBands);

        for (int i = 0; i < 6; i++)
        {
            var key = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i) ?? new StreamControllerDisplayKeyConfig { Idx = i };
            _scScreensaverPreviewImages[i].Source = StreamControllerDisplayRenderer.CreatePreview(key, previewConfig, frame);
        }
    }
}
