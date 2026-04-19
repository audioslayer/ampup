using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class LightsView
{
    private TextBlock? _scBrightnessValueLabel;
    private StyledSlider? _scBrightnessSlider;
    private CheckBox? _scScreensaverEnabled;
    private SegmentedControl? _scScreensaverEffectRow1;
    private SegmentedControl? _scScreensaverEffectRow2;
    private StyledSlider? _scScreensaverOpacitySlider;
    private StyledSlider? _scScreensaverSpeedSlider;
    private TextBlock? _scScreensaverOpacityLabel;
    private TextBlock? _scScreensaverSpeedLabel;
    private readonly Image[] _scScreensaverPreviewImages = new Image[6];
    private DispatcherTimer? _scScreensaverPreviewTimer;

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

        _scScreensaverPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _scScreensaverPreviewTimer.Tick += (_, _) =>
        {
            if (StreamControllerLightsPanel.Visibility == Visibility.Visible)
                RefreshStreamControllerScreensaverPreview();
        };
        _scScreensaverPreviewTimer.Start();
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
            Text = "Turn the key displays into a full animated scene. These effects temporarily replace the normal key art so the Stream Controller can behave more like a mini room-RGB surface.",
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

        _scScreensaverEffectRow1 = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _scScreensaverEffectRow1.AddSegment("Rainbow", StreamControllerScreensaverEffect.Rainbow);
        _scScreensaverEffectRow1.AddSegment("Aurora", StreamControllerScreensaverEffect.Aurora);
        _scScreensaverEffectRow1.AddSegment("Lava", StreamControllerScreensaverEffect.Fire);
        _scScreensaverEffectRow1.AddSegment("Ocean", StreamControllerScreensaverEffect.Ocean);
        _scScreensaverEffectRow1.AddSegment("Nebula", StreamControllerScreensaverEffect.Nebula);
        _scScreensaverEffectRow1.AddSegment("Starfield", StreamControllerScreensaverEffect.Starfield);
        _scScreensaverEffectRow1.AddSegment("Plasma", StreamControllerScreensaverEffect.Plasma);
        _scScreensaverEffectRow1.SelectionChanged += (_, _) =>
        {
            if (_scScreensaverEffectRow1.SelectedIndex >= 0 && _scScreensaverEffectRow2?.SelectedIndex >= 0)
                _scScreensaverEffectRow2.SelectedIndex = -1;
            RefreshStreamControllerScreensaverPreview();
            if (!_loading) QueueSave();
        };
        stack.Children.Add(_scScreensaverEffectRow1);

        _scScreensaverEffectRow2 = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _scScreensaverEffectRow2.AddSegment("Prism", StreamControllerScreensaverEffect.Prism);
        _scScreensaverEffectRow2.AddSegment("Lightning", StreamControllerScreensaverEffect.Lightning);
        _scScreensaverEffectRow2.AddSegment("Cyber", StreamControllerScreensaverEffect.Cyber);
        _scScreensaverEffectRow2.AddSegment("Gradient", StreamControllerScreensaverEffect.GradientFlow);
        _scScreensaverEffectRow2.AddSegment("Matrix", StreamControllerScreensaverEffect.Matrix);
        _scScreensaverEffectRow2.AddSegment("Music", StreamControllerScreensaverEffect.MusicBounce);
        _scScreensaverEffectRow2.AddSegment("Screen", StreamControllerScreensaverEffect.ScreenSync);
        _scScreensaverEffectRow2.SelectionChanged += (_, _) =>
        {
            if (_scScreensaverEffectRow2.SelectedIndex >= 0 && _scScreensaverEffectRow1?.SelectedIndex >= 0)
                _scScreensaverEffectRow1.SelectedIndex = -1;
            RefreshStreamControllerScreensaverPreview();
            if (!_loading) QueueSave();
        };
        stack.Children.Add(_scScreensaverEffectRow2);

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
            Text = "Live Hardware Preview",
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
                Stretch = Stretch.Uniform
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
        SetSelectedScreensaverEffect(config.N3.ScreensaverEffect);
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
        _config.N3.ScreensaverEffect = GetSelectedScreensaverEffect();
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

    private StreamControllerScreensaverEffect GetSelectedScreensaverEffect()
    {
        if (_scScreensaverEffectRow1?.SelectedTag is StreamControllerScreensaverEffect e1) return e1;
        if (_scScreensaverEffectRow2?.SelectedTag is StreamControllerScreensaverEffect e2) return e2;
        return StreamControllerScreensaverEffect.Rainbow;
    }

    private void SetSelectedScreensaverEffect(StreamControllerScreensaverEffect effect)
    {
        // Try row 1 first
        if (_scScreensaverEffectRow1 != null)
        {
            for (int i = 0; i < _scScreensaverEffectRow1.SegmentCount; i++)
            {
                if (_scScreensaverEffectRow1.GetTagAt(i) is StreamControllerScreensaverEffect e && e == effect)
                {
                    _scScreensaverEffectRow1.SelectedIndex = i;
                    if (_scScreensaverEffectRow2 != null) _scScreensaverEffectRow2.SelectedIndex = -1;
                    return;
                }
            }
        }
        // Try row 2
        if (_scScreensaverEffectRow2 != null)
        {
            for (int i = 0; i < _scScreensaverEffectRow2.SegmentCount; i++)
            {
                if (_scScreensaverEffectRow2.GetTagAt(i) is StreamControllerScreensaverEffect e && e == effect)
                {
                    _scScreensaverEffectRow2.SelectedIndex = i;
                    if (_scScreensaverEffectRow1 != null) _scScreensaverEffectRow1.SelectedIndex = -1;
                    return;
                }
            }
        }
        // Fallback: select Rainbow (row1 index 0)
        if (_scScreensaverEffectRow1 != null) _scScreensaverEffectRow1.SelectedIndex = 0;
        if (_scScreensaverEffectRow2 != null) _scScreensaverEffectRow2.SelectedIndex = -1;
    }

    private void UpdateStreamControllerScreensaverControls()
    {
        bool enabled = _scScreensaverEnabled?.IsChecked == true;
        if (_scScreensaverEffectRow1 != null) _scScreensaverEffectRow1.IsEnabled = enabled;
        if (_scScreensaverEffectRow2 != null) _scScreensaverEffectRow2.IsEnabled = enabled;
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
            ScreensaverEffect = GetSelectedScreensaverEffect(),
            ScreensaverOpacity = _scScreensaverOpacitySlider != null ? (int)Math.Round(_scScreensaverOpacitySlider.Value) : 55,
            ScreensaverSpeed = _scScreensaverSpeedSlider != null ? (int)Math.Round(_scScreensaverSpeedSlider.Value) : 50
        };

        using var frame = StreamControllerDisplayRenderer.CreateFrame(
            previewConfig,
            Environment.TickCount,
            App.AudioAnalyzer?.SmoothedBands,
            _config.Ambience.ScreenSync.MonitorIndex);

        for (int i = 0; i < 6; i++)
        {
            var key = new StreamControllerDisplayKeyConfig
            {
                Idx = i,
                BackgroundColor = "#000000",
                AccentColor = _config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i)?.AccentColor ?? "#00E676"
            };
            _scScreensaverPreviewImages[i].Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key, previewConfig, frame);
        }
    }
}
