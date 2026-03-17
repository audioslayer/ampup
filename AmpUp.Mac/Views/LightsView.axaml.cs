using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AmpUp.Mac.Controls;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class LightsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    // Per-channel controls
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly TextBlock[] _headerEffects = new TextBlock[5];
    private readonly EffectPickerControl[] _effectPickers = new EffectPickerControl[5];
    private readonly Border[] _color1Swatches = new Border[5];
    private readonly Border[] _color2Swatches = new Border[5];
    private readonly StackPanel[] _color2Panels = new StackPanel[5];
    private readonly Slider[] _speedSliders = new Slider[5];
    private readonly StackPanel[] _speedPanels = new StackPanel[5];
    private readonly Color[] _colors1 = new Color[5];
    private readonly Color[] _colors2 = new Color[5];

    // Global lighting controls
    private CheckBox? _globalEnableCheck;
    private EffectPickerControl? _globalEffectPicker;
    private Border? _globalColor1Swatch;
    private Border? _globalColor2Swatch;
    private StackPanel? _globalColor2Panel;
    private Slider? _globalSpeedSlider;
    private StackPanel? _globalSpeedPanel;
    private Slider? _brightnessSlider;
    private StackPanel? _globalSettingsPanel;
    private Color _globalColor1 = Color.Parse("#00E676");
    private Color _globalColor2 = Colors.White;

    private static readonly LightEffect[] EffectsNeedingColor2 =
    {
        LightEffect.ColorBlend, LightEffect.Blink, LightEffect.Pulse, LightEffect.MicStatus,
        LightEffect.DeviceMute, LightEffect.AudioReactive, LightEffect.GradientFill,
        LightEffect.Fire, LightEffect.PingPong, LightEffect.Candle, LightEffect.Scanner,
        LightEffect.ColorWave, LightEffect.Segments, LightEffect.PositionBlend,
        LightEffect.ProgramMute, LightEffect.AppGroupMute,
    };

    private static readonly LightEffect[] EffectsNeedingSpeed =
    {
        LightEffect.Blink, LightEffect.Pulse, LightEffect.RainbowWave, LightEffect.RainbowCycle,
        LightEffect.AudioReactive, LightEffect.Breathing, LightEffect.Comet, LightEffect.Sparkle,
        LightEffect.PingPong, LightEffect.Stack, LightEffect.Wave, LightEffect.Candle,
        LightEffect.Scanner, LightEffect.MeteorRain, LightEffect.ColorWave, LightEffect.Segments,
        LightEffect.Wheel, LightEffect.RainbowWheel,
    };

    public LightsView()
    {
        InitializeComponent();
        BuildGlobalCard();
        BuildChannelControls();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        var gl = config.GlobalLight;
        if (_globalEnableCheck != null) _globalEnableCheck.IsChecked = gl.Enabled;
        if (_globalEffectPicker != null) _globalEffectPicker.SelectedEffect = gl.Effect;
        _globalColor1 = Color.FromRgb((byte)gl.R, (byte)gl.G, (byte)gl.B);
        _globalColor2 = Color.FromRgb((byte)gl.R2, (byte)gl.G2, (byte)gl.B2);
        UpdateSwatchColor(_globalColor1Swatch, _globalColor1);
        UpdateSwatchColor(_globalColor2Swatch, _globalColor2);
        if (_globalSpeedSlider != null) _globalSpeedSlider.Value = Math.Clamp(gl.EffectSpeed, 1, 100);
        if (_brightnessSlider != null) _brightnessSlider.Value = Math.Clamp(config.LedBrightness, 0, 100);

        UpdateGlobalVisibility();

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob != null)
            {
                var name = !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : FormatTarget(knob.Target);
                _headers[i].Text = name;
            }

            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            _effectPickers[i].SelectedEffect = light.Effect;
            UpdateHeaderEffect(i);

            _colors1[i] = Color.FromRgb((byte)light.R, (byte)light.G, (byte)light.B);
            _colors2[i] = Color.FromRgb((byte)light.R2, (byte)light.G2, (byte)light.B2);
            UpdateSwatchColor(_color1Swatches[i], _colors1[i]);
            UpdateSwatchColor(_color2Swatches[i], _colors2[i]);

            _speedSliders[i].Value = Math.Clamp(light.EffectSpeed, 1, 100);
            UpdatePerKnobVisibility(i, light.Effect);
        }

        _loading = false;
    }

    private void BuildGlobalCard()
    {
        var panel = this.FindControl<StackPanel>("GlobalCardPanel")!;

        // Header row
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _globalEnableCheck = new CheckBox();
        _globalEnableCheck.IsCheckedChanged += (_, _) =>
        {
            UpdateGlobalVisibility();
            if (!_loading) Save();
        };
        headerRow.Children.Add(_globalEnableCheck);
        headerRow.Children.Add(new TextBlock
        {
            Text = "GLOBAL LIGHTING",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(headerRow);

        // Settings panel (hidden when disabled)
        _globalSettingsPanel = new StackPanel { Spacing = 8, IsVisible = false };

        // Effect picker
        _globalSettingsPanel.Children.Add(MakeSectionHeader("EFFECT"));
        _globalEffectPicker = new EffectPickerControl(showGlobal: true);
        _globalEffectPicker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            UpdateGlobalEffectVisibility(_globalEffectPicker.SelectedEffect);
            Save();
        };
        _globalSettingsPanel.Children.Add(_globalEffectPicker);

        // Color section
        _globalSettingsPanel.Children.Add(MakeSectionHeader("COLOR"));
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        colorRow.Children.Add(MakeSubLabel("PRIMARY"));
        _globalColor1Swatch = MakeColorSwatch(_globalColor1, () =>
        {
            // Simple: cycle hue on click (placeholder for full color picker)
            _globalColor1 = CycleHue(_globalColor1);
            UpdateSwatchColor(_globalColor1Swatch, _globalColor1);
            if (!_loading) Save();
        });
        colorRow.Children.Add(_globalColor1Swatch);

        _globalColor2Panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _globalColor2Panel.Children.Add(MakeSubLabel("SECONDARY"));
        _globalColor2Swatch = MakeColorSwatch(_globalColor2, () =>
        {
            _globalColor2 = CycleHue(_globalColor2);
            UpdateSwatchColor(_globalColor2Swatch, _globalColor2);
            if (!_loading) Save();
        });
        _globalColor2Panel.Children.Add(_globalColor2Swatch);
        colorRow.Children.Add(_globalColor2Panel);
        _globalSettingsPanel.Children.Add(colorRow);

        // Speed slider
        _globalSpeedPanel = new StackPanel { Spacing = 4, IsVisible = false };
        _globalSpeedPanel.Children.Add(MakeSectionHeader("SPEED"));
        _globalSpeedSlider = new Slider { Minimum = 1, Maximum = 100, Value = 50 };
        _globalSpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_loading) Save();
        };
        _globalSpeedPanel.Children.Add(_globalSpeedSlider);
        _globalSettingsPanel.Children.Add(_globalSpeedPanel);

        // Brightness slider (always visible when global enabled)
        var brightnessPanel = new StackPanel { Spacing = 4 };
        brightnessPanel.Children.Add(MakeSectionHeader("BRIGHTNESS"));
        _brightnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100 };
        _brightnessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_loading) Save();
        };
        brightnessPanel.Children.Add(_brightnessSlider);
        _globalSettingsPanel.Children.Add(brightnessPanel);

        panel.Children.Add(_globalSettingsPanel);
    }

    private void BuildChannelControls()
    {
        var panels = new[]
        {
            this.FindControl<StackPanel>("Led0Panel")!,
            this.FindControl<StackPanel>("Led1Panel")!,
            this.FindControl<StackPanel>("Led2Panel")!,
            this.FindControl<StackPanel>("Led3Panel")!,
            this.FindControl<StackPanel>("Led4Panel")!,
        };

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var panel = panels[i];

            // Header
            var header = new TextBlock
            {
                Text = $"LED {i + 1}",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _headers[i] = header;
            panel.Children.Add(header);

            var headerEffect = new TextBlock
            {
                Text = "Solid",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            _headerEffects[i] = headerEffect;
            panel.Children.Add(headerEffect);

            // Separator
            panel.Children.Add(MakeSeparator());

            // Effect picker
            panel.Children.Add(MakeSectionHeader("EFFECT"));
            var effectPicker = new EffectPickerControl();
            effectPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                UpdatePerKnobVisibility(idx, effectPicker.SelectedEffect);
                UpdateHeaderEffect(idx);
                Save();
            };
            _effectPickers[i] = effectPicker;
            panel.Children.Add(effectPicker);

            panel.Children.Add(MakeSeparator());

            // Color section
            panel.Children.Add(MakeSectionHeader("COLOR"));
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            colorRow.Children.Add(MakeSubLabel("PRIMARY"));
            _color1Swatches[i] = MakeColorSwatch(Colors.Green, () =>
            {
                _colors1[idx] = CycleHue(_colors1[idx]);
                UpdateSwatchColor(_color1Swatches[idx], _colors1[idx]);
                if (!_loading) Save();
            });
            colorRow.Children.Add(_color1Swatches[i]);

            _color2Panels[i] = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            _color2Panels[i].Children.Add(MakeSubLabel("SECONDARY"));
            _color2Swatches[i] = MakeColorSwatch(Colors.Red, () =>
            {
                _colors2[idx] = CycleHue(_colors2[idx]);
                UpdateSwatchColor(_color2Swatches[idx], _colors2[idx]);
                if (!_loading) Save();
            });
            _color2Panels[i].Children.Add(_color2Swatches[i]);
            colorRow.Children.Add(_color2Panels[i]);
            panel.Children.Add(colorRow);

            // Speed slider
            _speedPanels[i] = new StackPanel { Spacing = 4 };
            _speedPanels[i].Children.Add(MakeSectionHeader("SPEED"));
            _speedSliders[i] = new Slider { Minimum = 1, Maximum = 100, Value = 50 };
            _speedSliders[i].PropertyChanged += (_, e) =>
            {
                if (e.Property == Slider.ValueProperty && !_loading) Save();
            };
            _speedPanels[i].Children.Add(_speedSliders[i]);
            panel.Children.Add(_speedPanels[i]);
        }
    }

    private void UpdateGlobalVisibility()
    {
        bool enabled = _globalEnableCheck?.IsChecked ?? false;
        if (_globalSettingsPanel != null) _globalSettingsPanel.IsVisible = enabled;
        var grid = this.FindControl<Grid>("PerKnobGrid");
        if (grid != null) grid.IsVisible = !enabled;

        if (enabled && _globalEffectPicker != null)
            UpdateGlobalEffectVisibility(_globalEffectPicker.SelectedEffect);
    }

    private void UpdateGlobalEffectVisibility(LightEffect effect)
    {
        if (_globalColor2Panel != null)
            _globalColor2Panel.IsVisible = EffectsNeedingColor2.Contains(effect);
        if (_globalSpeedPanel != null)
            _globalSpeedPanel.IsVisible = EffectsNeedingSpeed.Contains(effect);
    }

    private void UpdatePerKnobVisibility(int idx, LightEffect effect)
    {
        _color2Panels[idx].IsVisible = EffectsNeedingColor2.Contains(effect);
        _speedPanels[idx].IsVisible = EffectsNeedingSpeed.Contains(effect);
    }

    private void UpdateHeaderEffect(int idx)
    {
        var effect = _effectPickers[idx].SelectedEffect;
        var display = System.Text.RegularExpressions.Regex.Replace(effect.ToString(), "(?<!^)([A-Z])", " $1");
        _headerEffects[idx].Text = display;
        var color = EffectPickerControl.EffectColors.GetValueOrDefault(effect, Color.Parse("#00E676"));
        _headerEffects[idx].Foreground = new SolidColorBrush(color);
    }

    private void Save()
    {
        if (_config == null || _onSave == null) return;

        var gl = _config.GlobalLight;
        gl.Enabled = _globalEnableCheck?.IsChecked ?? false;
        if (_globalEffectPicker != null) gl.Effect = _globalEffectPicker.SelectedEffect;
        gl.R = _globalColor1.R; gl.G = _globalColor1.G; gl.B = _globalColor1.B;
        gl.R2 = _globalColor2.R; gl.G2 = _globalColor2.G; gl.B2 = _globalColor2.B;
        if (_globalSpeedSlider != null) gl.EffectSpeed = (int)_globalSpeedSlider.Value;

        for (int i = 0; i < 5; i++)
        {
            var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null) continue;

            light.Effect = _effectPickers[i].SelectedEffect;
            light.R = _colors1[i].R; light.G = _colors1[i].G; light.B = _colors1[i].B;
            light.R2 = _colors2[i].R; light.G2 = _colors2[i].G; light.B2 = _colors2[i].B;
            light.EffectSpeed = (int)_speedSliders[i].Value;
        }

        if (_brightnessSlider != null) _config.LedBrightness = (int)_brightnessSlider.Value;

        _onSave(_config);
    }

    // ── UI Helpers ──────────────────────────────────────────────────

    private Grid MakeSectionHeader(string title)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(3, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var bar = new Border
        {
            Background = FindBrush("AccentBrush"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("AccentBrush"),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        return grid;
    }

    private Border MakeSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = FindBrush("CardBorderBrush"),
            Margin = new Thickness(0, 8),
        };
    }

    private static TextBlock MakeSubLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#99CCCCCC")),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border MakeColorSwatch(Color initial, Action onClick)
    {
        var inner = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(initial),
        };
        var outer = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Color.Parse("#444444")),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.Parse("#1A1A1A")),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = inner,
            Tag = inner,
        };
        outer.PointerPressed += (_, _) => onClick();
        return outer;
    }

    private static void UpdateSwatchColor(Border? swatch, Color color)
    {
        if (swatch?.Tag is Border inner)
            inner.Background = new SolidColorBrush(color);
    }

    private static Color CycleHue(Color c)
    {
        // Simple hue rotation by 30 degrees for click-to-change color
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, v = max;
        double d = max - min;
        s = max == 0 ? 0 : d / max;
        if (d > 0)
        {
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        h = (h + 1.0 / 12.0) % 1.0; // rotate 30°
        if (s < 0.1) s = 0.8;
        if (v < 0.1) v = 0.8;
        // HSV to RGB
        int hi = (int)(h * 6) % 6;
        double f = h * 6 - (int)(h * 6);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        double ro, go, bo;
        switch (hi)
        {
            case 0: ro = v; go = t; bo = p; break;
            case 1: ro = q; go = v; bo = p; break;
            case 2: ro = p; go = v; bo = t; break;
            case 3: ro = p; go = q; bo = v; break;
            case 4: ro = t; go = p; bo = v; break;
            default: ro = v; go = p; bo = q; break;
        }
        return Color.FromRgb((byte)(ro * 255), (byte)(go * 255), (byte)(bo * 255));
    }

    private IBrush FindBrush(string key)
    {
        if (this.TryFindResource(key, this.ActualThemeVariant, out var res) && res is IBrush brush)
            return brush;
        return Brushes.White;
    }

    private static string FormatTarget(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none") return "None";
        return string.Join(' ', target.Replace('_', ' ').Split(' ')
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }
}
