using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Mac.Controls;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class MixerView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    private readonly DispatcherTimer _liveTimer;

    // Per-channel control arrays
    private readonly KnobControl[] _knobs = new KnobControl[5];
    private readonly VuMeterControl[] _vuMeters = new VuMeterControl[5];
    private readonly TextBlock[] _volLabels = new TextBlock[5];
    private readonly TextBox[] _channelLabels = new TextBox[5];
    private readonly ComboBox[] _targetPickers = new ComboBox[5];
    private readonly Border[] _curveButtons = new Border[15]; // 3 per channel (Lin/Log/Exp)
    private readonly TextBlock[] _rangeMinLabels = new TextBlock[5];
    private readonly TextBlock[] _rangeMaxLabels = new TextBlock[5];
    private readonly Border[] _stripBorders = new Border[5];

    // App group panels
    private readonly WrapPanel[] _appsListPanels = new WrapPanel[5];
    private readonly StackPanel[] _appsPanels = new StackPanel[5];

    private bool _smartMixExpanded;

    // Target options for ComboBox
    private static readonly string[] TargetOptions =
    {
        "master", "mic", "system", "any", "active_window",
        "output_device", "input_device", "monitor", "led_brightness",
        "discord", "spotify", "chrome", "apps"
    };

    private static readonly string[] TargetDisplayNames =
    {
        "Master", "Mic", "System", "Any", "Active Window",
        "Output Device", "Input Device", "Monitor", "LED Brightness",
        "Discord", "Spotify", "Chrome", "App Group"
    };

    public MixerView()
    {
        InitializeComponent();

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _liveTimer.Tick += LiveTimer_Tick;

        BuildChannelControls();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _liveTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _liveTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public void LoadConfig(AppConfig config, Action<AppConfig>? onConfigChanged = null)
    {
        _loading = true;
        _config = config;
        _onSave = onConfigChanged;

        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            _channelLabels[i].Text = GetDisplayLabel(knob);
            SelectTarget(_targetPickers[i], knob.Target);
            SelectCurve(i, knob.Curve);
            _rangeMinLabels[i].Text = $"{knob.MinVolume}%";
            _rangeMaxLabels[i].Text = $"{knob.MaxVolume}%";
            UpdatePickerVisibility(i, knob.Target);

            // Set LED color from config
            var light = config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light != null)
            {
                var color = Color.FromRgb(
                    (byte)Math.Clamp(light.R, 0, 255),
                    (byte)Math.Clamp(light.G, 0, 255),
                    (byte)Math.Clamp(light.B, 0, 255));
                if (color.R < 40 && color.G < 40 && color.B < 40)
                    color = Color.Parse("#00E676");
                _knobs[i].ArcColor = color;
                _vuMeters[i].BarColor = color;
                _volLabels[i].Foreground = new SolidColorBrush(color);
            }
        }

        _loading = false;
        _liveTimer.Start();
    }

    /// <summary>
    /// Called from main app when hardware knob turns.
    /// </summary>
    public void UpdateKnobPosition(int idx, float position)
    {
        if (idx < 0 || idx >= 5) return;
        _knobs[idx].SetTarget(position);
        _knobs[idx].Tick();
        int pct = (int)Math.Round(position * 100);
        _knobs[idx].PercentText = $"{pct}%";
        _volLabels[idx].Text = $"{pct}%";
    }

    private void LiveTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;

        // Tick animations only — positions are driven by hardware events via UpdateKnobPosition().
        // We do NOT reset knob positions here; that would override live hardware values.
        for (int i = 0; i < 5; i++)
        {
            _knobs[i].Tick();
            _vuMeters[i].Tick();
        }
    }

    private void BuildChannelControls()
    {
        var panels = new[] { Ch0Panel, Ch1Panel, Ch2Panel, Ch3Panel, Ch4Panel };
        var borders = new[] { Ch0Border, Ch1Border, Ch2Border, Ch3Border, Ch4Border };

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var panel = panels[i];
            _stripBorders[i] = borders[i];

            // ── LABEL ──
            var label = new TextBox
            {
                Text = $"Knob {i + 1}",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                CaretBrush = FindBrush("AccentBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 0, 2),
                MaxLength = 20,
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
                if (!_loading) QueueSave();
            };
            _channelLabels[i] = label;
            panel.Children.Add(label);

            // ── KNOB + VU GRID ──
            var knobVuGrid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4),
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto")
            };

            var knob = new KnobControl
            {
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(knob, 0);
            _knobs[i] = knob;
            knobVuGrid.Children.Add(knob);

            var vuMeter = new VuMeterControl
            {
                Width = 6,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
            };
            Grid.SetColumn(vuMeter, 1);
            _vuMeters[i] = vuMeter;
            knobVuGrid.Children.Add(vuMeter);

            panel.Children.Add(knobVuGrid);

            // ── VOLUME LABEL ──
            var volLabel = new TextBlock
            {
                Text = "0%",
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _volLabels[i] = volLabel;
            panel.Children.Add(volLabel);

            // ── DIVIDER ──
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
                Margin = new Thickness(0, 8, 0, 10)
            });

            // ── TARGET ──
            panel.Children.Add(MakeSectionHeader("TARGET"));

            var targetPicker = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            for (int t = 0; t < TargetOptions.Length; t++)
            {
                targetPicker.Items.Add(new ComboBoxItem
                {
                    Content = TargetDisplayNames[t],
                    Tag = TargetOptions[t]
                });
            }
            targetPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var tag = (targetPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "master";
                UpdatePickerVisibility(idx, tag);
                QueueSave();
            };
            _targetPickers[i] = targetPicker;
            panel.Children.Add(targetPicker);

            // ── APP GROUP (hidden unless target=apps) ──
            var appsContainer = new StackPanel { IsVisible = false };
            appsContainer.Children.Add(MakeLabel("APP GROUP"));
            var appsListPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _appsListPanels[i] = appsListPanel;
            appsContainer.Children.Add(appsListPanel);
            _appsPanels[i] = appsContainer;
            panel.Children.Add(appsContainer);

            // ── SEPARATOR ──
            panel.Children.Add(MakeSeparator(8));

            // ── CURVE ──
            panel.Children.Add(MakeSectionHeader("CURVE"));

            var curveGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,*,*"),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var curveNames = new[] { "Lin", "Log", "Exp" };
            var curveValues = new[] { ResponseCurve.Linear, ResponseCurve.Logarithmic, ResponseCurve.Exponential };

            for (int c = 0; c < 3; c++)
            {
                int curveIdx = c;
                var curveBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#1A1A1A")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(0, 6),
                    Margin = new Thickness(c == 0 ? 0 : 2, 0, c == 2 ? 0 : 2, 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };

                var curveLabel = new TextBlock
                {
                    Text = curveNames[c],
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#9A9A9A")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                };
                curveBorder.Child = curveLabel;

                curveBorder.PointerPressed += (_, _) =>
                {
                    if (_loading) return;
                    SelectCurve(idx, curveValues[curveIdx]);
                    QueueSave();
                };

                Grid.SetColumn(curveBorder, c);
                curveGrid.Children.Add(curveBorder);
                _curveButtons[i * 3 + c] = curveBorder;
            }
            panel.Children.Add(curveGrid);

            // ── SEPARATOR ──
            panel.Children.Add(MakeSeparator(8));

            // ── VOLUME RANGE ──
            panel.Children.Add(MakeSectionHeader("VOLUME RANGE"));

            var rangeRow = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var minLabel = new TextBlock
            {
                Text = "0%",
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(minLabel, 0);
            rangeRow.Children.Add(minLabel);
            _rangeMinLabels[i] = minLabel;

            var rangeFill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.Parse("#00E676")),
                Opacity = 0.3,
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(rangeFill, 1);
            rangeRow.Children.Add(rangeFill);

            var maxLabel = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(maxLabel, 2);
            rangeRow.Children.Add(maxLabel);
            _rangeMaxLabels[i] = maxLabel;

            panel.Children.Add(rangeRow);
        }
    }

    private void SelectTarget(ComboBox picker, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        for (int i = 0; i < picker.Items.Count; i++)
        {
            if (picker.Items[i] is ComboBoxItem item && item.Tag?.ToString() == baseTarget)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        picker.SelectedIndex = 0; // default to master
    }

    private void SelectCurve(int channelIdx, ResponseCurve curve)
    {
        var accentColor = Color.Parse("#00E676");
        var dimColor = Color.Parse("#2A2A2A");
        var accentBg = Color.Parse("#0D3318");
        var normalBg = Color.Parse("#1A1A1A");

        var curveIdx = curve switch
        {
            ResponseCurve.Logarithmic => 1,
            ResponseCurve.Exponential => 2,
            _ => 0
        };

        for (int c = 0; c < 3; c++)
        {
            var btn = _curveButtons[channelIdx * 3 + c];
            if (btn == null) continue;

            bool selected = c == curveIdx;
            btn.Background = new SolidColorBrush(selected ? accentBg : normalBg);
            btn.BorderBrush = new SolidColorBrush(selected ? accentColor : dimColor);

            if (btn.Child is TextBlock tb)
            {
                tb.Foreground = new SolidColorBrush(selected ? accentColor : Color.Parse("#9A9A9A"));
            }
        }
    }

    private void UpdatePickerVisibility(int idx, string target)
    {
        var baseTarget = target.Contains(':') ? target.Split(':')[0] : target;
        bool showApps = baseTarget == "apps";
        _appsPanels[idx].IsVisible = showApps;

        if (showApps)
            RebuildAppToggles(idx);
    }

    private void RebuildAppToggles(int idx)
    {
        var panel = _appsListPanels[idx];
        panel.Children.Clear();

        if (_config == null) return;
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
        if (knob == null) return;

        var accent = Color.Parse("#00E676");

        if (knob.Apps.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No apps added yet",
                FontSize = 10,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 2, 0, 4),
            });
        }
        else
        {
            foreach (var app in knob.Apps.ToList())
            {
                var appName = app; // capture for closure
                var chipText = new TextBlock
                {
                    Text = app,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var removeBtn = new TextBlock
                {
                    Text = "×",
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };

                var chipContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                chipContent.Children.Add(chipText);
                chipContent.Children.Add(removeBtn);

                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 8, 4),
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = chipContent,
                };

                removeBtn.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    if (_config == null) return;
                    var k = _config.Knobs.FirstOrDefault(kk => kk.Idx == idx);
                    if (k != null)
                    {
                        k.Apps.Remove(appName);
                        RebuildAppToggles(idx);
                        QueueSave();
                    }
                };

                panel.Children.Add(chip);
            }
        }

        // Add-app row: text input + button
        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var appInput = new TextBox
        {
            FontSize = 10,
            Watermark = "process name…",
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2),
            MinWidth = 90,
        };

        var addBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "+ Add",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(accent),
            },
        };

        addBtn.PointerPressed += (_, _) =>
        {
            var name = appInput.Text?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || _config == null) return;
            var k = _config.Knobs.FirstOrDefault(kk => kk.Idx == idx);
            if (k != null && !k.Apps.Contains(name))
            {
                k.Apps.Add(name);
                appInput.Text = "";
                RebuildAppToggles(idx);
                QueueSave();
            }
        };

        // Also add on Enter key
        appInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                var name = appInput.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(name) || _config == null) return;
                var k = _config.Knobs.FirstOrDefault(kk => kk.Idx == idx);
                if (k != null && !k.Apps.Contains(name))
                {
                    k.Apps.Add(name);
                    appInput.Text = "";
                    RebuildAppToggles(idx);
                    QueueSave();
                }
            }
        };

        addRow.Children.Add(appInput);
        addRow.Children.Add(addBtn);
        panel.Children.Add(addRow);
    }

    private void QueueSave()
    {
        if (_config == null || _onSave == null) return;
        CollectAndSave();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            knob.Label = _channelLabels[i].Text?.Trim() ?? "";
            var selectedItem = _targetPickers[i].SelectedItem as ComboBoxItem;
            knob.Target = selectedItem?.Tag?.ToString() ?? "master";

            // Determine selected curve from button states
            for (int c = 0; c < 3; c++)
            {
                var btn = _curveButtons[i * 3 + c];
                if (btn?.BorderBrush is SolidColorBrush brush && brush.Color == Color.Parse("#00E676"))
                {
                    knob.Curve = c switch
                    {
                        1 => ResponseCurve.Logarithmic,
                        2 => ResponseCurve.Exponential,
                        _ => ResponseCurve.Linear,
                    };
                    break;
                }
            }
        }

        _onSave(_config);
    }

    // ── Helpers ──

    private StackPanel MakeSectionHeader(string title)
    {
        var sp = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };

        sp.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 1, 8, 1),
            Height = 14,
        });

        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("AccentBrush"),
        });

        return sp;
    }

    private Border MakeSeparator(int spacing = 10)
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
            Margin = new Thickness(0, spacing, 0, spacing),
        };
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 4, 0, 3),
        };
    }

    private IBrush FindBrush(string key)
    {
        if (this.TryFindResource(key, out var res) && res is IBrush b)
            return b;
        return Brushes.White;
    }

    private static string GetDisplayLabel(KnobConfig knob)
    {
        if (!string.IsNullOrWhiteSpace(knob.Label))
            return knob.Label;
        return FormatTargetName(knob.Target);
    }

    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none")
            return "None";

        var idx = Array.IndexOf(TargetOptions, target);
        if (idx >= 0) return TargetDisplayNames[idx];

        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }

    private void SmartMixHeader_Click(object? sender, PointerPressedEventArgs e)
    {
        _smartMixExpanded = !_smartMixExpanded;
        SmartMixContent.IsVisible = _smartMixExpanded;
        SmartMixArrow.Text = _smartMixExpanded ? "▼" : "▶";
    }
}
