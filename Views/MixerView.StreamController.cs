using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class MixerView
{
    private const int ScChannelCount = 3;

    private readonly AnimatedKnobControl[] _scKnobs = new AnimatedKnobControl[ScChannelCount];
    private readonly VuMeterControl[] _scVuMeters = new VuMeterControl[ScChannelCount];
    private readonly TextBlock[] _scVolLabels = new TextBlock[ScChannelCount];
    private readonly TextBox[] _scChannelLabels = new TextBox[ScChannelCount];
    private readonly TextBlock[] _scTargetDisplays = new TextBlock[ScChannelCount];
    private readonly GridPicker[] _scTargetPickers = new GridPicker[ScChannelCount];
    private readonly StackPanel[] _scAppsPanels = new StackPanel[ScChannelCount];
    private readonly WrapPanel[] _scAppsListPanels = new WrapPanel[ScChannelCount];
    private readonly RangeSlider[] _scRangeSliders = new RangeSlider[ScChannelCount];
    private readonly StyledSlider[] _scSensitivitySliders = new StyledSlider[ScChannelCount];
    private readonly ChannelGlowControl[] _scGlows = new ChannelGlowControl[ScChannelCount];
    private readonly Color[] _scDisplayedColors = new Color[ScChannelCount];
    private bool _scBuilt;

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
        if (_scBuilt) return;
        StreamControllerMixerContent.Children.Clear();

        var grid = new Grid { Margin = new Thickness(0) };
        for (int c = 0; c < ScChannelCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 4; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < ScChannelCount; i++)
        {
            int idx = i;

            // ── Strip card (knob, label, vol%) ─────────────────────────
            // ClipToBounds keeps the radial glow inside the rounded card;
            // padding moves to the inner StackPanel so the glow can fill edge-to-edge.
            var stripCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 10),
                ClipToBounds = true,
            };
            stripCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            stripCard.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            // Ambient audio-reactive glow layered behind the strip content.
            // Mirrors the Turn Up mixer — tinted to the LED color, pulses on peak.
            var stripLayers = new Grid();
            var glow = new ChannelGlowControl();
            stripLayers.Children.Add(glow);
            _scGlows[i] = glow;

            var stripStack = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
            stripLayers.Children.Add(stripStack);

            var label = new TextBox
            {
                Text = $"Encoder {i + 1}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                CaretBrush = FindBrush("AccentBrush"),
                SelectionBrush = FindBrush("AccentDimBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 0, 6),
                MaxLength = 20,
                ToolTip = "Click to rename this encoder",
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
                if (!_loading) SaveScLabel(idx, label.Text);
            };
            _scChannelLabels[i] = label;
            stripStack.Children.Add(label);

            var knobVuGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            knobVuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var knob = new AnimatedKnobControl
            {
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Turn the encoder to adjust volume",
            };
            Grid.SetColumn(knob, 0);
            _scKnobs[i] = knob;
            knobVuGrid.Children.Add(knob);

            var vu = new VuMeterControl
            {
                Width = 6,
                Height = 54,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = "Audio level",
            };
            Grid.SetColumn(vu, 1);
            _scVuMeters[i] = vu;
            knobVuGrid.Children.Add(vu);

            stripStack.Children.Add(knobVuGrid);

            var volLabel = new TextBlock
            {
                Text = "0%",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _scVolLabels[i] = volLabel;
            stripStack.Children.Add(volLabel);

            var targetDisplay = new TextBlock
            {
                FontSize = 10,
                Foreground = FindBrush("TextSecBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            _scTargetDisplays[i] = targetDisplay;
            stripStack.Children.Add(targetDisplay);

            stripCard.Child = stripLayers;
            Grid.SetRow(stripCard, 0);
            Grid.SetColumn(stripCard, i);
            grid.Children.Add(stripCard);

            // ── Target picker card ─────────────────────────────────────
            var targetPicker = new GridPicker
            {
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "What this encoder controls",
                Tag = targetDisplay,
            };
            // Picker items populated lazily once _config is bound — the
            // full integration-aware catalogue requires knowing which
            // integrations are enabled (see RebuildScTargetPickerItems).
            targetPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                SaveScTargetSelection(idx);
                UpdateScPickerVisibility(idx, GetSelectedTarget(_scTargetPickers[idx]));
            };
            _scTargetPickers[i] = targetPicker;

            // App Group chip list — mirrors the Turn Up mixer's setup.
            var appsContainer = new StackPanel { Visibility = Visibility.Collapsed };
            appsContainer.Children.Add(new TextBlock
            {
                Text = "APP GROUP",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 6, 0, 4),
            });
            appsContainer.ToolTip = "Click apps to add or remove from this group";
            var appsListPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            appsContainer.Children.Add(appsListPanel);
            _scAppsPanels[i] = appsContainer;
            _scAppsListPanels[i] = appsListPanel;

            var targetCard = MakeSectionCard("TARGET", targetPicker, appsContainer);
            targetCard.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 10);
            Grid.SetRow(targetCard, 1);
            Grid.SetColumn(targetCard, i);
            grid.Children.Add(targetCard);

            // ── Sensitivity slider card ────────────────────────────────
            // N3 encoders are digital infinite scrollers — what matters is
            // how much volume change one detent produces. Slider value is
            // displayed as "% per click" (1-13%) and stored as raw step
            // (×10, clamped to 128). 3% per click ≈ legacy default.
            var sensitivity = new StyledSlider
            {
                Minimum = 1,
                Maximum = 13,
                Value = 3,
                Step = 1,
                LabelFormat = "F0",
                Suffix = "% per click",
                Height = 28,
                ToolTip = "How much the volume changes with each click of the wheel. Lower = finer control.",
            };
            sensitivity.ValueChanged += (_, _) =>
            {
                if (!_loading) SaveScSensitivity(idx);
            };
            _scSensitivitySliders[i] = sensitivity;

            var sensCard = MakeSectionCard("SENSITIVITY", sensitivity);
            sensCard.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 10);
            Grid.SetRow(sensCard, 2);
            Grid.SetColumn(sensCard, i);
            grid.Children.Add(sensCard);

            // ── Range slider row ───────────────────────────────────────
            var range = new RangeSlider
            {
                Minimum = 0,
                Maximum = 100,
                LowerValue = 0,
                UpperValue = 100,
                Height = 28,
                ToolTip = "Set the min and max volume this encoder can reach",
            };

            var minLabel = new TextBlock { Text = "0%", FontSize = 10, Foreground = FindBrush("TextDimBrush") };
            var maxLabel = new TextBlock { Text = "100%", FontSize = 10, Foreground = FindBrush("TextDimBrush"), HorizontalAlignment = HorizontalAlignment.Right };
            range.LowerValueChanged += (_, _) =>
            {
                minLabel.Text = $"{(int)range.LowerValue}%";
                if (!_loading) SaveScRange(idx);
            };
            range.UpperValueChanged += (_, _) =>
            {
                maxLabel.Text = $"{(int)range.UpperValue}%";
                if (!_loading) SaveScRange(idx);
            };
            _scRangeSliders[i] = range;

            var labelsRow = new Grid();
            labelsRow.Children.Add(minLabel);
            labelsRow.Children.Add(maxLabel);

            var rangeStack = new StackPanel();
            rangeStack.Children.Add(new TextBlock
            {
                Text = "VOLUME RANGE",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            rangeStack.Children.Add(range);
            rangeStack.Children.Add(labelsRow);

            var rangeHost = new Border
            {
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 8),
                Child = rangeStack,
            };
            Grid.SetRow(rangeHost, 3);
            Grid.SetColumn(rangeHost, i);
            grid.Children.Add(rangeHost);
        }

        StreamControllerMixerContent.Children.Add(grid);
        _scBuilt = true;
    }

    /// <summary>
    /// Re-populate every SC target picker with the full integration-aware
    /// catalogue, plus the N3-only Stream Controller navigation targets.
    /// Must be called after _config is available and whenever an
    /// integration flag or groups list changes.
    /// </summary>
    private void RebuildScTargetPickerItems(AppConfig config)
    {
        for (int i = 0; i < ScChannelCount; i++)
        {
            var picker = _scTargetPickers[i];
            if (picker == null) continue;
            PopulateTargetPickerItems(picker, config, includeN3Nav: true);
        }
    }

    /// <summary>Show/hide the SC strip's App Group chip list based on the selected target.</summary>
    private void UpdateScPickerVisibility(int idx, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        bool showApps = baseTarget == "apps";
        if (_scAppsPanels[idx] != null)
            _scAppsPanels[idx].Visibility = showApps ? Visibility.Visible : Visibility.Collapsed;
        if (showApps)
            RebuildScAppToggles(idx);
    }

    /// <summary>SC-side chip rebuild — same logic as the Turn Up path, writes to N3 knobs.</summary>
    private void RebuildScAppToggles(int idx)
    {
        if (_config == null || _mixer == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;
        RebuildAppTogglesFor(_scAppsListPanels[idx], knob, () => RebuildScAppToggles(idx));
    }

    private void LoadStreamControllerMixerConfig(AppConfig config)
    {
        MixerDeviceSelector.SelectedIndex = config.TabSelection.Mixer switch
        {
            DeviceSurface.StreamController => 1,
            DeviceSurface.Both => 2,
            _ => 0,
        };

        BuildStreamControllerMixerPanel();

        // Populate every SC target picker with the full integration-aware
        // catalogue (App Group, HA, Groups, Room Lights, Govee, VM,
        // Corsair, Stream Controller nav). Runs every config load so the
        // picker stays in sync when the user flips integration toggles.
        RebuildScTargetPickerItems(config);

        for (int i = 0; i < ScChannelCount; i++)
        {
            var knob = config.N3.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            _scChannelLabels[i].Text = GetDisplayLabel(knob);
            SelectTarget(_scTargetPickers[i], knob.Target, knob.DeviceId);
            // Encoder step → "% per click" display (round-trip via /10).
            int stepRaw = knob.EncoderStep > 0 ? knob.EncoderStep : 32;
            _scSensitivitySliders[i].Value = Math.Clamp((int)Math.Round(stepRaw / 10.0), 1, 13);
            _scRangeSliders[i].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
            _scRangeSliders[i].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);

            UpdateScTargetDisplay(i);
            UpdateScPickerVisibility(i, knob.Target);
        }

        UpdateMixerSurfaceVisibility(config.TabSelection.Mixer);
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

    private void UpdateScTargetDisplay(int idx)
    {
        if (_config == null) return;
        var picker = _scTargetPickers[idx];
        var display = _scTargetDisplays[idx];
        var target = GetSelectedTarget(picker);

        if (!string.IsNullOrEmpty(picker.SelectedSubTag))
        {
            if (target is "output_device" or "input_device")
            {
                var device = _audioDevices.FirstOrDefault(d => d.Id == picker.SelectedSubTag);
                display.Text = device.Name ?? picker.SelectedSubTag;
            }
            else
            {
                display.Text = picker.SelectedSubTag;
            }
        }
        else
        {
            display.Text = HATargetDisplayNames.TryGetValue(target, out var dn) ? dn : FormatTargetName(target);
        }
    }

    private void SaveScTargetSelection(int idx)
    {
        if (_config == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        var picker = _scTargetPickers[idx];
        var target = GetSelectedTarget(picker);

        if (target is "output_device" or "input_device" && !string.IsNullOrEmpty(picker.SelectedSubTag))
        {
            knob.Target = target;
            knob.DeviceId = picker.SelectedSubTag;
        }
        else
        {
            knob.Target = target;
            knob.DeviceId = "";
        }

        UpdateScTargetDisplay(idx);
        QueueSave();
    }

    private void SaveScLabel(int idx, string text)
    {
        if (_config == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;
        knob.Label = text.Trim();
        QueueSave();
    }

    private void SaveScRange(int idx)
    {
        if (_config == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;
        knob.MinVolume = (int)Math.Round(_scRangeSliders[idx].LowerValue);
        knob.MaxVolume = (int)Math.Round(_scRangeSliders[idx].UpperValue);
        QueueSave();
    }

    private void SaveScSensitivity(int idx)
    {
        if (_config == null) return;
        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;
        // UI value is "% per click" (1-13). Stored as raw step = ×10, clamped 1-128.
        int pct = (int)Math.Round(_scSensitivitySliders[idx].Value);
        knob.EncoderStep = Math.Clamp(pct * 10, 1, 128);
        QueueSave();
    }

    private void TickStreamControllerMixer()
    {
        if (StreamControllerMixerPanel.Visibility != Visibility.Visible) return;
        if (_mixer == null || _config == null) return;

        for (int i = 0; i < ScChannelCount; i++)
        {
            var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            try
            {
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

                _scKnobs[i].SetTarget(vol);
                _scKnobs[i].Tick();
                int pct = (int)Math.Round(vol * 100);
                _scKnobs[i].PercentText = $"{pct}%";
                _scVolLabels[i].Text = $"{pct}%";

                _scVuMeters[i].Level = peak;
                _scVuMeters[i].Tick();

                // Ambient glow pulse — same audio-reactive effect as Turn Up mixer.
                _scGlows[i]?.SetLevel(peak);
                _scGlows[i]?.Tick();

                // Tint to match Light color for the corresponding index
                var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
                if (light != null)
                {
                    byte cr = (byte)Math.Clamp(light.R, 0, 255);
                    byte cg = (byte)Math.Clamp(light.G, 0, 255);
                    byte cb = (byte)Math.Clamp(light.B, 0, 255);
                    if (cr == 0 && cg == 0 && cb == 0)
                    {
                        cr = ThemeManager.Accent.R;
                        cg = ThemeManager.Accent.G;
                        cb = ThemeManager.Accent.B;
                    }
                    var color = EnsureMinBrightness(Color.FromRgb(cr, cg, cb));
                    if (color != _scDisplayedColors[i])
                    {
                        _scDisplayedColors[i] = color;
                        _scKnobs[i].ArcColor = color;
                        _scVuMeters[i].BarColor = color;
                        if (_scGlows[i] != null) _scGlows[i].GlowColor = color;
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();
                        _scVolLabels[i].Foreground = brush;
                    }
                }
            }
            catch { }
        }
    }

    private void UpdateStreamControllerMixerLiveState()
    {
        // Legacy hook — superseded by TickStreamControllerMixer().
    }
}
