using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Controls;

public partial class EffectPickerControl : UserControl
{
    public event EventHandler? SelectionChanged;

    private readonly bool _showGlobal;
    private readonly List<EffectTile> _tiles = new();
    private int _selectedIndex = -1;

    public LightEffect SelectedEffect
    {
        get => _selectedIndex >= 0 && _selectedIndex < _tiles.Count
            ? _tiles[_selectedIndex].Effect : LightEffect.SingleColor;
        set
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i].Effect == value) { SelectTile(i, false); return; }
            }
        }
    }

    private class EffectTile
    {
        public Border Container = null!;
        public TextBlock Label = null!;
        public LightEffect Effect;
        public Color TileColor;
    }

    public static readonly Dictionary<LightEffect, Color> EffectColors = new()
    {
        { LightEffect.SingleColor,       Color.Parse("#64B5F6") },
        { LightEffect.ColorBlend,        Color.Parse("#BA68C8") },
        { LightEffect.PositionFill,      Color.Parse("#4DD0E1") },
        { LightEffect.GradientFill,      Color.Parse("#AED581") },
        { LightEffect.PositionBlend,     Color.Parse("#80DEEA") },
        { LightEffect.PositionBlendMute, Color.Parse("#FF7043") },
        { LightEffect.CycleFill,        Color.Parse("#E06C9F") },
        { LightEffect.RainbowFill,      Color.Parse("#FFD54F") },
        { LightEffect.Blink,             Color.Parse("#FFD54F") },
        { LightEffect.Pulse,             Color.Parse("#E06C9F") },
        { LightEffect.Breathing,         Color.Parse("#80CBC4") },
        { LightEffect.Fire,              Color.Parse("#FF8A3D") },
        { LightEffect.Comet,             Color.Parse("#7C8CF8") },
        { LightEffect.Sparkle,           Color.Parse("#FFF176") },
        { LightEffect.RainbowWave,       Color.Parse("#EF5350") },
        { LightEffect.RainbowCycle,      Color.Parse("#66BB6A") },
        { LightEffect.PingPong,          Color.Parse("#29B6F6") },
        { LightEffect.Stack,             Color.Parse("#66BB6A") },
        { LightEffect.Wave,              Color.Parse("#26C6DA") },
        { LightEffect.Candle,            Color.Parse("#FFCA28") },
        { LightEffect.Wheel,             Color.Parse("#B39DDB") },
        { LightEffect.RainbowWheel,      Color.Parse("#EF5350") },
        { LightEffect.Heartbeat,        Color.Parse("#E53935") },
        { LightEffect.Plasma,           Color.Parse("#CE93D8") },
        { LightEffect.Drip,             Color.Parse("#42A5F5") },
        { LightEffect.MicStatus,         Color.Parse("#42A5F5") },
        { LightEffect.DeviceMute,        Color.Parse("#EF5350") },
        { LightEffect.AudioReactive,     Color.Parse("#00E676") },
        { LightEffect.ProgramMute,       Color.Parse("#FF8A65") },
        { LightEffect.AppGroupMute,      Color.Parse("#AB47BC") },
        { LightEffect.DeviceSelect,      Color.Parse("#4DD0E1") },
        { LightEffect.Scanner,           Color.Parse("#EF5350") },
        { LightEffect.MeteorRain,        Color.Parse("#7C8CF8") },
        { LightEffect.ColorWave,         Color.Parse("#BA68C8") },
        { LightEffect.Segments,          Color.Parse("#FF8A3D") },
        { LightEffect.TheaterChase,      Color.Parse("#26C6DA") },
        { LightEffect.RainbowScanner,    Color.Parse("#EC407A") },
        { LightEffect.SparkleRain,       Color.Parse("#FFF176") },
        { LightEffect.BreathingSync,     Color.Parse("#80CBC4") },
        { LightEffect.FireWall,          Color.Parse("#FF5722") },
        { LightEffect.DualRacer,         Color.Parse("#42A5F5") },
        { LightEffect.Lightning,         Color.Parse("#FFF176") },
        { LightEffect.Fillup,            Color.Parse("#66BB6A") },
        { LightEffect.Ocean,             Color.Parse("#29B6F6") },
        { LightEffect.Collision,         Color.Parse("#FF8A3D") },
        { LightEffect.DNA,               Color.Parse("#BA68C8") },
        { LightEffect.Rainfall,          Color.Parse("#4DD0E1") },
        { LightEffect.PoliceLights,      Color.Parse("#EF5350") },
        { LightEffect.Aurora,            Color.Parse("#69F0AE") },
        { LightEffect.Matrix,            Color.Parse("#00E676") },
        { LightEffect.Starfield,         Color.Parse("#B39DDB") },
    };

    private static readonly Dictionary<LightEffect, string> EffectLabels = new()
    {
        { LightEffect.SingleColor, "Solid" }, { LightEffect.ColorBlend, "Blend" },
        { LightEffect.PositionFill, "Fill" }, { LightEffect.GradientFill, "Gradient" },
        { LightEffect.PositionBlend, "PosBlend" }, { LightEffect.PositionBlendMute, "Blend+Mute" },
        { LightEffect.CycleFill, "CycleFill" }, { LightEffect.RainbowFill, "RbwFill" },
        { LightEffect.Blink, "Blink" }, { LightEffect.Pulse, "Pulse" },
        { LightEffect.Breathing, "Breathe" }, { LightEffect.Fire, "Fire" },
        { LightEffect.Comet, "Comet" }, { LightEffect.Sparkle, "Sparkle" },
        { LightEffect.RainbowWave, "Rainbow" }, { LightEffect.RainbowCycle, "Cycle" },
        { LightEffect.PingPong, "Pong" }, { LightEffect.Stack, "Stack" },
        { LightEffect.Wave, "Wave" }, { LightEffect.Candle, "Candle" },
        { LightEffect.Wheel, "Wheel" }, { LightEffect.RainbowWheel, "R.Wheel" },
        { LightEffect.MicStatus, "Mic" }, { LightEffect.DeviceMute, "Mute" },
        { LightEffect.AudioReactive, "Audio" }, { LightEffect.ProgramMute, "App Mute" },
        { LightEffect.AppGroupMute, "Group" }, { LightEffect.DeviceSelect, "Device" },
        { LightEffect.Scanner, "Scanner" }, { LightEffect.MeteorRain, "Meteor" },
        { LightEffect.ColorWave, "Wave" }, { LightEffect.Segments, "Bands" },
        { LightEffect.TheaterChase, "Chase" }, { LightEffect.RainbowScanner, "R.Scan" },
        { LightEffect.SparkleRain, "Sparkle" }, { LightEffect.BreathingSync, "Breath" },
        { LightEffect.FireWall, "Inferno" }, { LightEffect.DualRacer, "Racers" },
        { LightEffect.Lightning, "Bolt" }, { LightEffect.Fillup, "Fill Up" },
        { LightEffect.Ocean, "Ocean" }, { LightEffect.Collision, "Crash" },
        { LightEffect.DNA, "DNA" }, { LightEffect.Rainfall, "Rain" },
        { LightEffect.PoliceLights, "Police" },
        { LightEffect.Heartbeat, "Heart" }, { LightEffect.Plasma, "Plasma" },
        { LightEffect.Drip, "Drip" },
        { LightEffect.Aurora, "Aurora" }, { LightEffect.Matrix, "Matrix" },
        { LightEffect.Starfield, "Stars" },
    };

    public EffectPickerControl() : this(false) { }

    public EffectPickerControl(bool showGlobal)
    {
        _showGlobal = showGlobal;
        InitializeComponent();
        BuildTiles();
    }

    private void BuildTiles()
    {
        var root = this.FindControl<StackPanel>("RootPanel")!;

        AddCategory(root, "STATIC", new[]
        {
            LightEffect.SingleColor, LightEffect.ColorBlend, LightEffect.PositionFill,
            LightEffect.PositionBlend, LightEffect.PositionBlendMute,
            LightEffect.CycleFill, LightEffect.RainbowFill, LightEffect.GradientFill,
        });

        AddCategory(root, "ANIMATED", new[]
        {
            LightEffect.Blink, LightEffect.Pulse, LightEffect.Breathing, LightEffect.Fire,
            LightEffect.Comet, LightEffect.Sparkle, LightEffect.PingPong, LightEffect.Stack,
            LightEffect.Wave, LightEffect.Candle, LightEffect.RainbowWave, LightEffect.RainbowCycle,
            LightEffect.Wheel, LightEffect.RainbowWheel,
            LightEffect.Heartbeat, LightEffect.Plasma, LightEffect.Drip,
        });

        AddCategory(root, "REACTIVE", new[]
        {
            LightEffect.MicStatus, LightEffect.DeviceMute, LightEffect.AudioReactive,
            LightEffect.ProgramMute, LightEffect.AppGroupMute, LightEffect.DeviceSelect,
        });

        if (_showGlobal)
        {
            AddCategory(root, "GLOBAL SPAN", new[]
            {
                LightEffect.Scanner, LightEffect.MeteorRain, LightEffect.ColorWave,
                LightEffect.Segments, LightEffect.TheaterChase, LightEffect.RainbowScanner,
                LightEffect.SparkleRain, LightEffect.BreathingSync, LightEffect.FireWall,
                LightEffect.DualRacer, LightEffect.Lightning, LightEffect.Fillup,
                LightEffect.Ocean, LightEffect.Collision, LightEffect.DNA,
                LightEffect.Rainfall, LightEffect.PoliceLights,
                LightEffect.Aurora, LightEffect.Matrix, LightEffect.Starfield,
            });
        }

        // Default select first
        if (_tiles.Count > 0) SelectTile(0, false);
    }

    private void AddCategory(StackPanel parent, string title, LightEffect[] effects)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#555555")),
            Margin = new Thickness(0, 4, 0, 4),
        };
        parent.Children.Add(header);

        var wrap = new WrapPanel();
        foreach (var effect in effects)
        {
            var color = EffectColors.GetValueOrDefault(effect, Color.Parse("#00E676"));
            var label = EffectLabels.GetValueOrDefault(effect, effect.ToString());
            var tile = MakeTile(effect, label, color);
            wrap.Children.Add(tile.Container);
            _tiles.Add(tile);
        }
        parent.Children.Add(wrap);
    }

    private EffectTile MakeTile(LightEffect effect, string label, Color color)
    {
        var dimBg = Color.FromArgb(0x28, color.R, color.G, color.B);

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 9,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        var border = new Border
        {
            Width = 62,
            MinHeight = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(dimBg),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 6),
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = lbl,
        };

        var tile = new EffectTile
        {
            Container = border,
            Label = lbl,
            Effect = effect,
            TileColor = color,
        };

        border.PointerPressed += (_, e) =>
        {
            int idx = _tiles.IndexOf(tile);
            if (idx >= 0) SelectTile(idx, true);
        };

        border.PointerEntered += (_, _) =>
        {
            if (_tiles.IndexOf(tile) != _selectedIndex)
                border.BorderBrush = new SolidColorBrush(Colors.White);
        };
        border.PointerExited += (_, _) =>
        {
            if (_tiles.IndexOf(tile) != _selectedIndex)
                border.BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
        };

        return tile;
    }

    private void SelectTile(int index, bool fireEvent)
    {
        if (index == _selectedIndex && !fireEvent) return;
        _selectedIndex = index;
        UpdateVisuals();
        if (fireEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisuals()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            bool selected = i == _selectedIndex;
            if (selected)
            {
                t.Container.BorderBrush = new SolidColorBrush(t.TileColor);
                t.Container.BorderThickness = new Thickness(1.5);
                t.Container.Background = new SolidColorBrush(
                    Color.FromArgb(0x40, t.TileColor.R, t.TileColor.G, t.TileColor.B));
            }
            else
            {
                t.Container.BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
                t.Container.BorderThickness = new Thickness(1);
                t.Container.Background = new SolidColorBrush(
                    Color.FromArgb(0x28, t.TileColor.R, t.TileColor.G, t.TileColor.B));
            }
        }
    }
}
