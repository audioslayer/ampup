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
    private readonly RangeSlider[] _scRangeSliders = new RangeSlider[ScChannelCount];
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
        for (int r = 0; r < 3; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < ScChannelCount; i++)
        {
            int idx = i;

            // ── Strip card (knob, label, vol%) ─────────────────────────
            var stripCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 12, 10, 12),
                Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 10),
            };
            stripCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            stripCard.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            var stripStack = new StackPanel();

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

            stripCard.Child = stripStack;
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
            AddDefaultTargetItems(targetPicker);
            targetPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                SaveScTargetSelection(idx);
            };
            _scTargetPickers[i] = targetPicker;
            var targetCard = MakeSectionCard("TARGET", targetPicker);
            targetCard.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == ScChannelCount - 1 ? 0 : 4, 10);
            Grid.SetRow(targetCard, 1);
            Grid.SetColumn(targetCard, i);
            grid.Children.Add(targetCard);

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
            Grid.SetRow(rangeHost, 2);
            Grid.SetColumn(rangeHost, i);
            grid.Children.Add(rangeHost);
        }

        StreamControllerMixerContent.Children.Add(grid);
        _scBuilt = true;
    }

    private void AddDefaultTargetItems(GridPicker picker)
    {
        var clrGreen  = Color.FromRgb(0x66, 0xBB, 0x6A);
        var clrRed    = Color.FromRgb(0xEF, 0x53, 0x50);
        var clrBlue   = Color.FromRgb(0x42, 0xA5, 0xF5);
        var clrTeal   = Color.FromRgb(0x26, 0xC6, 0xDA);
        var clrPurple = Color.FromRgb(0xAB, 0x47, 0xBC);
        var clrOrange = Color.FromRgb(0xFF, 0xA7, 0x26);
        var clrYellow = Color.FromRgb(0xFF, 0xD5, 0x4F);

        picker.AddCategory("Audio");
        picker.AddItem("Master",        "master",        "♪",  clrGreen);
        picker.AddItem("Mic",           "mic",           "◎",  clrRed);
        picker.AddItem("System",        "system",        "◆",  clrBlue);
        picker.AddItem("Any",           "any",           "◈",  clrTeal);
        picker.AddItem("Active Window", "active_window", "▣",  clrPurple);

        picker.AddCategory("Devices");
        picker.AddItem("Output Device",  "output_device",  "▶",  clrPurple);
        picker.AddItem("Input Device",   "input_device",   "◀",  clrRed);
        picker.AddItem("Monitor",        "monitor",        "▭",  clrOrange);
        picker.AddItem("LED Brightness", "led_brightness", "◉",  clrYellow);

        picker.AddCategory("Apps");
        picker.AddItem("Discord", "discord", "◉", Color.FromRgb(0x58, 0x65, 0xF2));
        picker.AddItem("Spotify", "spotify", "♪", Color.FromRgb(0x1D, 0xB9, 0x54));
        picker.AddItem("Chrome",  "chrome",  "◆", clrBlue);
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

        for (int i = 0; i < ScChannelCount; i++)
        {
            var knob = config.N3.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            _scChannelLabels[i].Text = GetDisplayLabel(knob);
            SelectTarget(_scTargetPickers[i], knob.Target, knob.DeviceId);
            _scRangeSliders[i].LowerValue = Math.Clamp(knob.MinVolume, 0, 100);
            _scRangeSliders[i].UpperValue = Math.Clamp(knob.MaxVolume, 0, 100);

            UpdateScTargetDisplay(i);
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
