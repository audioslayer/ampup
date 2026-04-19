using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class MixerView
{
    private readonly AnimatedKnobControl[] _scMixerKnobs = new AnimatedKnobControl[3];
    private readonly VuMeterControl[] _scMixerVuMeters = new VuMeterControl[3];
    private readonly TextBlock[] _scMixerPercentLabels = new TextBlock[3];
    private readonly TextBox[] _scMixerLabels = new TextBox[3];
    private readonly TextBlock[] _scMixerTargetDisplays = new TextBlock[3];
    private readonly GridPicker[] _scMixerTargetPickers = new GridPicker[3];
    private readonly CurvePickerControl[] _scMixerCurvePickers = new CurvePickerControl[3];
    private readonly RangeSlider[] _scMixerRangeSliders = new RangeSlider[3];
    private readonly StackPanel[] _scMixerAppsPanels = new StackPanel[3];
    private readonly WrapPanel[] _scMixerAppsListPanels = new WrapPanel[3];
    private readonly Border[] _scMixerCardBorders = new Border[3];
    private readonly Color[] _scDisplayedColors = new Color[3];

    private CheckBox? _scMirrorToggle;
    private StyledSlider? _scEncoderStepSlider;
    private TextBlock? _scEncoderStepValue;
    private TextBlock? _scModeBadge;
    private TextBlock? _scHeroSummary;

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
        StreamControllerMixerContent.Children.Add(BuildStreamControllerHeroCard());
        StreamControllerMixerContent.Children.Add(BuildStreamControllerControlStrip());

        var encoderRow = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 2, 0, 0)
        };

        for (int i = 0; i < 3; i++)
            encoderRow.Children.Add(BuildStreamControllerEncoderCard(i));

        StreamControllerMixerContent.Children.Add(encoderRow);
    }

    private Border BuildStreamControllerHeroCard()
    {
        var accent = ThemeManager.Accent;
        var hero = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(22, 20, 22, 20),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x10, 0x16, 0x1A),
                Color.FromRgb(0x19, 0x20, 0x24),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "STREAM CONTROLLER MIXER",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent)
        });
        left.Children.Add(new TextBlock
        {
            Text = "Three metal encoders, compact routing, and the same Amp Up control depth as the Turn Up mixer.",
            Margin = new Thickness(0, 8, 0, 10),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        _scModeBadge = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("BgBaseBrush")
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _scModeBadge
        };
        left.Children.Add(badge);

        _scHeroSummary = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = FindBrush("TextSecBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        left.Children.Add(_scHeroSummary);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var deviceCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0x08, 0x0C, 0x10)),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18)
        };

        var deviceStack = new StackPanel();
        var displays = new UniformGrid
        {
            Columns = 3,
            Rows = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        for (int i = 0; i < 6; i++)
        {
            displays.Children.Add(new Border
            {
                Margin = new Thickness(4),
                Height = 44,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(
                    Color.FromArgb((byte)(0x65 + (i * 8)), accent.R, accent.G, accent.B),
                    Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0),
                    new Point(1, 1))
            });
        }
        deviceStack.Children.Add(displays);

        var buttonsRow = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(20, 0, 20, 12)
        };
        for (int i = 0; i < 3; i++)
        {
            buttonsRow.Children.Add(new Border
            {
                Height = 8,
                Margin = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(4),
                Background = FindBrush("InputBorderBrush")
            });
        }
        deviceStack.Children.Add(buttonsRow);

        var knobRow = new UniformGrid { Columns = 3 };
        for (int i = 0; i < 3; i++)
        {
            var knobShell = new Border
            {
                Margin = new Thickness(10, 0, 10, 0),
                Width = 70,
                Height = 70,
                CornerRadius = new CornerRadius(35),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new RadialGradientBrush(
                    Color.FromRgb(0x58, 0x63, 0x69),
                    Color.FromRgb(0x1A, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1.25)
            };

            knobShell.Child = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x2B, 0x31, 0x36),
                    Color.FromRgb(0x0F, 0x12, 0x16),
                    new Point(0, 0),
                    new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1)
            };
            knobRow.Children.Add(knobShell);
        }
        deviceStack.Children.Add(knobRow);

        deviceCard.Child = deviceStack;
        Grid.SetColumn(deviceCard, 1);
        grid.Children.Add(deviceCard);

        hero.Child = grid;
        return hero;
    }

    private Border BuildStreamControllerControlStrip()
    {
        var accent = ThemeManager.Accent;
        var strip = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Background = FindBrush("CardBgBrush"),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "MIRROR MODE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent)
        });
        left.Children.Add(new TextBlock
        {
            Text = "When enabled, encoder turns drive the Stream Controller's own three mixer channels. If you disable it, the Stream Controller can still use its buttons and displays independently.",
            Margin = new Thickness(0, 5, 0, 10),
            Foreground = FindBrush("TextSecBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        _scMirrorToggle = new CheckBox
        {
            Content = "Enable Stream Controller encoder mixer control",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            IsChecked = true
        };
        _scMirrorToggle.Checked += (_, _) => OnStreamControllerMirrorChanged(true);
        _scMirrorToggle.Unchecked += (_, _) => OnStreamControllerMirrorChanged(false);
        left.Children.Add(_scMirrorToggle);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(new TextBlock
        {
            Text = "ENCODER STEP",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent)
        });
        _scEncoderStepValue = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 8),
            Foreground = FindBrush("TextSecBrush")
        };
        right.Children.Add(_scEncoderStepValue);

        _scEncoderStepSlider = new StyledSlider
        {
            Minimum = 1,
            Maximum = 128,
            Value = 32,
            Height = 38,
            AccentColor = accent,
            Suffix = "",
            LabelFormat = "F0",
            ToolTip = "How much each encoder detent moves the mirrored channel"
        };
        _scEncoderStepSlider.ValueChanged += (_, _) =>
        {
            if (_loading || _config == null || _scEncoderStepSlider == null) return;
            _config.N3.EncoderStep = (int)Math.Round(_scEncoderStepSlider.Value);
            UpdateStreamControllerModeState();
            QueueSave();
        };
        right.Children.Add(_scEncoderStepSlider);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        strip.Child = grid;
        return strip;
    }

    private Border BuildStreamControllerEncoderCard(int idx)
    {
        var card = new Border
        {
            Margin = new Thickness(idx == 0 ? 0 : 6, 0, idx == 2 ? 0 : 6, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = FindBrush("CardBorderBrush"),
            Background = FindBrush("BgDarkBrush")
        };
        _scMixerCardBorders[idx] = card;

        var stack = new StackPanel();

        var tagRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var encoderPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x28, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = $"ENCODER {idx + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("TextPrimaryBrush")
            }
        };
        DockPanel.SetDock(encoderPill, Dock.Left);
        tagRow.Children.Add(encoderPill);

        var routeLabel = new TextBlock
        {
            Text = "Mixer Lane",
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(routeLabel, Dock.Right);
        tagRow.Children.Add(routeLabel);
        stack.Children.Add(tagRow);

        var label = new TextBox
        {
            Text = $"Knob {idx + 1}",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            CaretBrush = FindBrush("AccentBrush"),
            SelectionBrush = FindBrush("AccentDimBrush"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 0, 4),
            MaxLength = 20
        };
        label.GotFocus += (_, _) =>
        {
            label.Background = FindBrush("InputBgBrush");
            label.BorderThickness = new Thickness(0, 0, 0, 1);
            label.BorderBrush = FindBrush("AccentBrush");
            label.SelectAll();
        };
        label.LostFocus += (_, _) =>
        {
            label.Background = Brushes.Transparent;
            label.BorderThickness = new Thickness(0);
            SyncStreamControllerMixerEditorToTurnUp(idx);
            if (!_loading) QueueSave();
        };
        _scMixerLabels[idx] = label;
        stack.Children.Add(label);

        var knobVuGrid = new Grid
        {
            Margin = new Thickness(0, 2, 0, 6)
        };
        knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var knob = new AnimatedKnobControl
        {
            Width = 96,
            Height = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _scMixerKnobs[idx] = knob;
        Grid.SetColumn(knob, 0);
        knobVuGrid.Children.Add(knob);

        var vuMeter = new VuMeterControl
        {
            Width = 6,
            Height = 62,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0)
        };
        _scMixerVuMeters[idx] = vuMeter;
        Grid.SetColumn(vuMeter, 1);
        knobVuGrid.Children.Add(vuMeter);

        stack.Children.Add(knobVuGrid);

        var percentLabel = new TextBlock
        {
            Text = "0%",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        _scMixerPercentLabels[idx] = percentLabel;
        stack.Children.Add(percentLabel);

        var targetDisplay = new TextBlock
        {
            Text = "Unassigned",
            Foreground = FindBrush("TextSecBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(4, 0, 4, 10),
            TextWrapping = TextWrapping.Wrap
        };
        _scMixerTargetDisplays[idx] = targetDisplay;
        stack.Children.Add(targetDisplay);

        var targetPicker = new GridPicker
        {
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "What this encoder controls"
        };
        targetPicker.Tag = targetDisplay;
        targetPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateStreamControllerTargetDisplay(idx);
            UpdateStreamControllerTargetVisibility(idx);
            SyncStreamControllerMixerEditorToTurnUp(idx);
            QueueSave();
        };
        _scMixerTargetPickers[idx] = targetPicker;

        var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
        appsContainer.Children.Add(MakeLabel("APP GROUP"));
        var appsList = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        appsContainer.Children.Add(appsList);
        _scMixerAppsPanels[idx] = appsContainer;
        _scMixerAppsListPanels[idx] = appsList;

        stack.Children.Add(MakeSectionCard("TARGET", targetPicker, appsContainer));

        var curvePicker = new CurvePickerControl
        {
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Linear: even response. Log: more sensitive at low values. Exp: more sensitive at high values."
        };
        curvePicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            SyncStreamControllerMixerEditorToTurnUp(idx);
            QueueSave();
        };
        _scMixerCurvePickers[idx] = curvePicker;
        stack.Children.Add(MakeSectionCard("CURVE", curvePicker));

        var rangeSlider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            LowerValue = 0,
            UpperValue = 100,
            Height = 28,
            AccentColor = ThemeManager.Accent,
            ToolTip = "Set the min and max range this encoder can reach"
        };
        rangeSlider.LowerValueChanged += (_, _) =>
        {
            if (_loading) return;
            SyncStreamControllerMixerEditorToTurnUp(idx);
            QueueSave();
        };
        rangeSlider.UpperValueChanged += (_, _) =>
        {
            if (_loading) return;
            SyncStreamControllerMixerEditorToTurnUp(idx);
            QueueSave();
        };
        _scMixerRangeSliders[idx] = rangeSlider;
        stack.Children.Add(MakeSectionCard("RANGE", rangeSlider));

        card.Child = stack;
        return card;
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

        RebuildStreamControllerTargetPickerItems(config);

        if (_scMirrorToggle != null)
            _scMirrorToggle.IsChecked = config.N3.MirrorFirstThreeKnobs;
        if (_scEncoderStepSlider != null)
            _scEncoderStepSlider.Value = Math.Clamp(config.N3.EncoderStep, 1, 128);

        for (int i = 0; i < 3; i++)
        {
            var knob = config.N3.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
            _scMixerLabels[i].Text = string.IsNullOrWhiteSpace(knob.Label) ? $"Encoder {i + 1}" : knob.Label;
            SelectTarget(_scMixerTargetPickers[i], knob.Target, knob.DeviceId);
            SelectCurve(_scMixerCurvePickers[i], knob.Curve);
            _scMixerRangeSliders[i].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
            _scMixerRangeSliders[i].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);
            UpdateStreamControllerTargetDisplay(i);
            UpdateStreamControllerTargetVisibility(i);
            RebuildStreamControllerAppToggles(i);
        }

        UpdateStreamControllerModeState();
        UpdateStreamControllerMixerLiveState();
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

    private void UpdateStreamControllerMixerLiveState()
    {
        if (_mixer == null || _config == null) return;

        for (int i = 0; i < 3; i++)
        {
            var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
            bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget == "monitor" || baseTarget == "led_brightness";

            float vol;
            float peak;
            if (isNonAudio)
            {
                vol = App.StreamControllerKnobPositions[i];
                peak = 0f;
            }
            else
            {
                vol = _mixer.GetVolume(knob);
                if (vol <= 0f)
                    vol = App.StreamControllerKnobPositions[i];
                peak = Math.Min(_mixer.GetPeakLevel(knob) * 2.3f, 1f);
            }

            _scMixerKnobs[i].SetTarget(vol);
            _scMixerKnobs[i].Tick();
            int pct = (int)Math.Round(vol * 100);
            _scMixerKnobs[i].PercentText = $"{pct}%";
            _scMixerPercentLabels[i].Text = $"{pct}%";
            _scMixerVuMeters[i].Level = peak;
            _scMixerVuMeters[i].Tick();

            var color = GetStreamControllerMixerColor(i);
            if (color != _scDisplayedColors[i])
            {
                _scDisplayedColors[i] = color;
                _scMixerKnobs[i].ArcColor = color;
                _scMixerVuMeters[i].BarColor = color;
                _scMixerPercentLabels[i].Foreground = new SolidColorBrush(color);
                _scMixerCardBorders[i].BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
            }
        }
    }

    private void RefreshStreamControllerAccentColors()
    {
        var accent = ThemeManager.Accent;
        for (int i = 0; i < 3; i++)
        {
            _scMixerTargetPickers[i].RefreshAccent();
            _scMixerRangeSliders[i].AccentColor = accent;
        }

        if (_scEncoderStepSlider != null)
            _scEncoderStepSlider.AccentColor = accent;

        UpdateStreamControllerModeState();
        UpdateStreamControllerMixerLiveState();
    }

    private void RebuildStreamControllerTargetPickerItems(AppConfig config)
    {
        bool haEnabled = config.HomeAssistant.Enabled;
        bool goveeEnabled = config.Ambience.GoveeEnabled && config.Ambience.GoveeDevices.Count > 0;
        bool vmEnabled = config.VoiceMeeter.Enabled;
        bool corsairEnabled = config.Corsair.Enabled && config.Corsair.FanEnabled;

        var clrGreen = Color.FromRgb(0x66, 0xBB, 0x6A);
        var clrRed = Color.FromRgb(0xEF, 0x53, 0x50);
        var clrBlue = Color.FromRgb(0x42, 0xA5, 0xF5);
        var clrTeal = Color.FromRgb(0x26, 0xC6, 0xDA);
        var clrPurple = Color.FromRgb(0xAB, 0x47, 0xBC);
        var clrOrange = Color.FromRgb(0xFF, 0xA7, 0x26);
        var clrYellow = Color.FromRgb(0xFF, 0xD5, 0x4F);
        var clrGovee = Color.FromRgb(0xFF, 0x6F, 0x00);
        var clrHA = Color.FromRgb(0x26, 0xC6, 0xDA);

        for (int i = 0; i < 3; i++)
        {
            var picker = _scMixerTargetPickers[i];
            picker.ClearItems();

            picker.AddCategory("Audio");
            picker.AddItem("Master", "master", "♪", clrGreen);
            picker.AddItem("Mic", "mic", "◎", clrRed);
            picker.AddItem("System", "system", "◆", clrBlue);
            picker.AddItem("Any", "any", "◈", clrTeal);
            picker.AddItem("Active Window", "active_window", "▣", clrPurple);

            picker.AddCategory("Devices");
            picker.AddItem("Output Device", "output_device", "▶", clrPurple);
            picker.AddItem("Input Device", "input_device", "◀", clrRed);
            picker.AddItem("Monitor", "monitor", "▭", clrOrange);
            picker.AddItem("LED Brightness", "led_brightness", "◉", clrYellow);
            picker.RegisterSubMenu("output_device", () => GetDeviceSubItems(isOutput: true));
            picker.RegisterSubMenu("input_device", () => GetDeviceSubItems(isOutput: false));
            picker.RegisterMultiSelectSubMenu("monitor", () => GetMonitorSubItems());

            if (haEnabled || goveeEnabled || vmEnabled || corsairEnabled || config.Groups.Count > 0)
            {
                picker.AddCategory("Integrations");

                if (haEnabled)
                {
                    picker.AddItem("Home Assistant", "ha_light", "◈", clrHA, "Light");
                    picker.AddItem("Home Assistant", "ha_media", "♪", clrHA, "Media Player");
                    picker.AddItem("Home Assistant", "ha_fan", "◎", clrHA, "Fan");
                    picker.AddItem("Home Assistant", "ha_cover", "▭", clrHA, "Cover");

                    foreach (var haKey in HATargetDomains.Keys)
                    {
                        var key = haKey;
                        picker.RegisterSubMenu(key, () => GetHASubItems(key));
                    }
                }

                if (config.Groups.Count > 0)
                {
                    foreach (var group in config.Groups)
                    {
                        var groupColor = Color.FromRgb(0x69, 0xF0, 0xAE);
                        try { groupColor = (Color)ColorConverter.ConvertFromString(group.Color); } catch { }
                        picker.AddItem(group.Name, $"group:{group.Name}", "▣", groupColor, "Groups");
                    }
                }

                if (goveeEnabled || corsairEnabled)
                    picker.AddItem("Room Lights", "room_lights", "◉", Color.FromRgb(0x69, 0xF0, 0xAE), "Room Lighting");

                if (goveeEnabled)
                {
                    picker.AddItem("Govee", "govee", "◈", clrGovee, "Room Lighting");
                    picker.RegisterSubMenu("govee", () => GetGoveeSubItems(config));
                }

                if (vmEnabled)
                {
                    var clrVm = Color.FromRgb(0xFF, 0x8F, 0x00);
                    for (int s = 0; s <= 4; s++)
                        picker.AddItem("VoiceMeeter", $"vm_strip:{s}", "♪", clrVm, $"Strip {s + 1}");
                    for (int b = 0; b <= 2; b++)
                        picker.AddItem("VoiceMeeter", $"vm_bus:{b}", "▶", clrVm, $"Bus {b + 1}");
                }

                if (corsairEnabled)
                {
                    var clrCorsair = Color.FromRgb(0xFF, 0xD3, 0x00);
                    picker.AddItem("Corsair", "corsair_pump_fan", "◎", clrCorsair, "Pump Fan");
                    picker.AddItem("Corsair", "corsair_case_fan", "◉", clrCorsair, "Case Fans");
                }
            }

            picker.AddCategory("Apps");
            picker.AddItem("Discord", "discord", "◉", Color.FromRgb(0x58, 0x65, 0xF2));
            picker.AddItem("Spotify", "spotify", "♪", Color.FromRgb(0x1D, 0xB9, 0x54));
            picker.AddItem("Chrome", "chrome", "◆", Color.FromRgb(0x42, 0x85, 0xF4));
            picker.AddItem("App Group", "apps", "▣", clrTeal);
        }
    }

    private void UpdateStreamControllerTargetDisplay(int idx)
    {
        var picker = _scMixerTargetPickers[idx];
        var target = GetSelectedTarget(picker);

        if (!string.IsNullOrEmpty(picker.SelectedSubTag))
        {
            _scMixerTargetDisplays[idx].Text = picker.SelectedSubTag;
            return;
        }

        if (picker.SelectedSubTags.Count > 0)
        {
            _scMixerTargetDisplays[idx].Text = $"{picker.SelectedSubTags.Count} targets";
            return;
        }

        _scMixerTargetDisplays[idx].Text = FormatTargetName(target);
    }

    private void UpdateStreamControllerTargetVisibility(int idx)
    {
        _scMixerAppsPanels[idx].Visibility = GetSelectedTarget(_scMixerTargetPickers[idx]) == "apps"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_scMixerAppsPanels[idx].Visibility == Visibility.Visible)
            RebuildStreamControllerAppToggles(idx);
    }

    private void RebuildStreamControllerAppToggles(int idx)
    {
        var panel = _scMixerAppsListPanels[idx];
        panel.Children.Clear();

        if (_config == null || _mixer == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        var allApps = new List<string>(knob.Apps);
        foreach (var app in _mixer.GetRunningAudioApps())
        {
            if (!allApps.Contains(app, StringComparer.OrdinalIgnoreCase))
                allApps.Add(app);
        }

        if (allApps.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No audio apps running yet",
                Foreground = FindBrush("TextDimBrush"),
                FontStyle = FontStyles.Italic
            });
            return;
        }

        var accent = ThemeManager.Accent;
        foreach (var app in allApps.OrderBy(a => a))
        {
            var appCapture = app;
            bool inGroup = knob.Apps.Contains(appCapture, StringComparer.OrdinalIgnoreCase);

            var chip = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(999),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = inGroup
                    ? new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B))
                    : FindBrush("BgBaseBrush"),
                BorderBrush = inGroup
                    ? new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B))
                    : FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = appCapture,
                    Foreground = FindBrush("TextPrimaryBrush"),
                    FontSize = 11
                }
            };

            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (_config == null) return;
                if (knob.Apps.Contains(appCapture, StringComparer.OrdinalIgnoreCase))
                    knob.Apps.RemoveAll(a => a.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
                else
                    knob.Apps.Add(appCapture);

                RebuildStreamControllerAppToggles(idx);
                QueueSave();
            };

            panel.Children.Add(chip);
        }
    }

    private void SyncStreamControllerMixerEditorToTurnUp(int idx)
    {
        if (idx < 0 || idx > 2) return;

        var resolvedTarget = ResolveStreamControllerKnobTarget(idx);
        var deviceId = ResolveStreamControllerDeviceId(idx);
        var curve = _scMixerCurvePickers[idx].SelectedTag is ResponseCurve selectedCurve
            ? selectedCurve
            : ResponseCurve.Linear;

        var knob = _config?.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        knob.Label = _scMixerLabels[idx].Text.Trim();
        knob.Target = resolvedTarget;
        knob.DeviceId = deviceId;
        knob.Curve = curve;
        knob.MinVolume = (int)_scMixerRangeSliders[idx].LowerValue;
        knob.MaxVolume = (int)_scMixerRangeSliders[idx].UpperValue;
    }

    private string ResolveStreamControllerKnobTarget(int idx)
    {
        var selectedTarget = GetSelectedTarget(_scMixerTargetPickers[idx]);

        if (HATargetDomains.ContainsKey(selectedTarget))
        {
            var entityId = _scMixerTargetPickers[idx].SelectedSubTag ?? "";
            return !string.IsNullOrEmpty(entityId) ? $"{selectedTarget}:{entityId}" : selectedTarget;
        }

        if (selectedTarget == "govee")
        {
            var deviceIp = _scMixerTargetPickers[idx].SelectedSubTag ?? "";
            return !string.IsNullOrEmpty(deviceIp) ? $"govee:{deviceIp}" : "govee";
        }

        return selectedTarget;
    }

    private string ResolveStreamControllerDeviceId(int idx)
    {
        var selectedTarget = GetSelectedTarget(_scMixerTargetPickers[idx]);

        if (selectedTarget is "output_device" or "input_device")
            return _scMixerTargetPickers[idx].SelectedSubTag ?? "";

        if (selectedTarget == "monitor")
        {
            var tags = _scMixerTargetPickers[idx].SelectedSubTags;
            return tags.Count > 0 ? string.Join(";", tags) : "";
        }

        return "";
    }

    private void OnStreamControllerMirrorChanged(bool enabled)
    {
        if (_loading || _config == null) return;
        _config.N3.MirrorFirstThreeKnobs = enabled;
        UpdateStreamControllerModeState();
        QueueSave();
    }

    private void UpdateStreamControllerModeState()
    {
        if (_config == null) return;

        bool mirroring = _config.N3.MirrorFirstThreeKnobs;
        if (_scModeBadge != null)
            _scModeBadge.Text = mirroring ? "Mirror Active" : "Mirror Paused";
        if (_scHeroSummary != null)
        {
            _scHeroSummary.Text = mirroring
                ? $"Encoders 1-3 are currently mapped to the Stream Controller's own three mixer channels. Each detent moves {_config.N3.EncoderStep} raw points."
                : "Encoder mixer control is paused. Your button and display bindings still work, but encoder turns will not drive the Stream Controller mixer until this is re-enabled.";
        }
        if (_scEncoderStepValue != null)
            _scEncoderStepValue.Text = $"Current step: {_config.N3.EncoderStep} per detent";
    }

    private Color GetStreamControllerMixerColor(int idx)
    {
        var light = _config?.Lights?.FirstOrDefault(l => l.Idx == idx);
        if (light == null)
            return ThemeManager.Accent;

        byte cr = (byte)Math.Clamp(light.R, 0, 255);
        byte cg = (byte)Math.Clamp(light.G, 0, 255);
        byte cb = (byte)Math.Clamp(light.B, 0, 255);
        if (cr == 0 && cg == 0 && cb == 0)
            return ThemeManager.Accent;

        return EnsureMinBrightness(Color.FromRgb(cr, cg, cb));
    }
}
