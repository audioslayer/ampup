using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AmpUp.Controls
{
    /// <summary>
    /// Interactive 3D-styled representation of the Turn Up hardware device.
    /// Shows live knob positions, LED colors, and button states.
    /// Clickable knobs/buttons navigate to Mixer/Buttons tabs.
    /// </summary>
    public class HardwareWidget : Border
    {
        // ── Layout constants ────────────────────────────────────────────
        private const double WidgetWidth = 620;
        private const double WidgetHeight = 220;
        private const double KnobSize = 80;
        private const double ButtonSize = 28;
        private const double KnobSpacing = 114;
        private const double KnobStartX = 40;
        private const double KnobY = 30;
        private const double ButtonY = 145;

        // Arc constants (matching AnimatedKnobControl)
        private const double StartAngleDeg = 225.0;
        private const double TotalSweepDeg = 270.0;
        private const double ArcStroke = 3.0;
        private const double GlowStroke = 6.0;
        private const double KnobImageRatio = 0.72;

        // ── Static resources ────────────────────────────────────────────
        private static readonly BitmapImage s_knobImage;
        private static readonly Pen s_trackPen;

        static HardwareWidget()
        {
            s_knobImage = new BitmapImage(new Uri("pack://application:,,,/Assets/knob-face.png", UriKind.Absolute));
            s_knobImage.Freeze();

            var trackBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            trackBrush.Freeze();
            s_trackPen = new Pen(trackBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            s_trackPen.Freeze();
        }

        // ── State ───────────────────────────────────────────────────────
        private AppConfig? _config;
        private readonly Canvas _canvas;
        private readonly Border[] _knobHitAreas = new Border[5];
        private readonly Border[] _buttonBorders = new Border[5];
        private readonly KnobVisual[] _knobVisuals = new KnobVisual[5];
        private readonly TextBlock[] _knobLabels = new TextBlock[5];
        private readonly System.Windows.Threading.DispatcherTimer _updateTimer;

        private Action<int>? _onKnobClick;
        private Action<int>? _onButtonClick;

        // Cached colors for dirty checking
        private readonly byte[] _lastR = new byte[5], _lastG = new byte[5], _lastB = new byte[5];
        private readonly float[] _lastKnobPos = new float[5];

        public HardwareWidget()
        {
            // Outer border — the "enclosure"
            Width = WidgetWidth;
            Height = WidgetHeight;
            HorizontalAlignment = HorizontalAlignment.Center;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1.5);
            Margin = new Thickness(0, 0, 0, 20);
            ClipToBounds = true;

            // Enclosure gradient (dark brushed metal feel)
            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(0x1A, 0x1A, 0x1A),
                Color.FromRgb(0x0F, 0x0F, 0x0F),
                90);
            bgBrush.Freeze();
            Background = bgBrush;

            // Border: subtle bevel (lighter top)
            var borderBrush = new LinearGradientBrush(
                Color.FromRgb(0x3A, 0x3A, 0x3A),
                Color.FromRgb(0x22, 0x22, 0x22),
                90);
            borderBrush.Freeze();
            BorderBrush = borderBrush;

            // Drop shadow for 3D depth
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 30,
                ShadowDepth = 8,
                Opacity = 0.6,
                Direction = 270
            };

            // Inner canvas for absolute positioning
            _canvas = new Canvas
            {
                Width = WidgetWidth,
                Height = WidgetHeight,
                ClipToBounds = true
            };

            // Inner highlight line (top edge bevel)
            var topHighlight = new Border
            {
                Width = WidgetWidth - 20,
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(topHighlight, 10);
            Canvas.SetTop(topHighlight, 2);
            _canvas.Children.Add(topHighlight);

            // Build the 5 knob + button columns
            for (int i = 0; i < 5; i++)
            {
                double x = KnobStartX + i * KnobSpacing;
                BuildKnobColumn(i, x);
            }

            Child = _canvas;

            // Update timer for live data (100ms = 10fps)
            _updateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _updateTimer.Tick += (_, _) => { if (IsVisible) UpdateLiveState(); };

            Loaded += (_, _) => _updateTimer.Start();
            Unloaded += (_, _) => _updateTimer.Stop();
        }

        public void SetCallbacks(Action<int> onKnobClick, Action<int> onButtonClick)
        {
            _onKnobClick = onKnobClick;
            _onButtonClick = onButtonClick;
        }

        public void LoadConfig(AppConfig config)
        {
            _config = config;
            UpdateLabelsAndButtons();
            // Force immediate visual refresh
            for (int i = 0; i < 5; i++)
            {
                _lastR[i] = 0; _lastG[i] = 0; _lastB[i] = 0;
                _lastKnobPos[i] = -1;
            }
            UpdateLiveState();
        }

        // ── Build UI ────────────────────────────────────────────────────

        private void BuildKnobColumn(int idx, double x)
        {
            double knobCenterX = x + KnobSize / 2;

            // Knob visual (custom DrawingVisual for arc + knob image)
            var knobVisual = new KnobVisual(KnobSize);
            Canvas.SetLeft(knobVisual, x);
            Canvas.SetTop(knobVisual, KnobY);
            _canvas.Children.Add(knobVisual);
            _knobVisuals[idx] = knobVisual;

            // Invisible hit area over the knob for mouse interaction
            var hitArea = new Border
            {
                Width = KnobSize,
                Height = KnobSize,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(hitArea, x);
            Canvas.SetTop(hitArea, KnobY);

            var capturedIdx = idx;
            hitArea.MouseLeftButtonDown += (_, _) => _onKnobClick?.Invoke(capturedIdx);
            hitArea.MouseEnter += (_, _) => knobVisual.SetHovered(true);
            hitArea.MouseLeave += (_, _) => knobVisual.SetHovered(false);
            hitArea.ToolTip = BuildToolTipStyle($"Knob {idx + 1}", "Not assigned");
            _canvas.Children.Add(hitArea);
            _knobHitAreas[idx] = hitArea;

            // Label below knob
            var label = new TextBlock
            {
                Text = $"Knob {idx + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                TextAlignment = TextAlignment.Center,
                Width = KnobSize + 20,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(label, x - 10);
            Canvas.SetTop(label, KnobY + KnobSize + 4);
            _canvas.Children.Add(label);
            _knobLabels[idx] = label;

            // Button below the label
            double btnX = knobCenterX - ButtonSize / 2;
            var button = new Border
            {
                Width = ButtonSize,
                Height = ButtonSize,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                // Inner shadow effect for 3D button look
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 2,
                    Opacity = 0.4,
                    Direction = 270
                }
            };
            Canvas.SetLeft(button, btnX);
            Canvas.SetTop(button, ButtonY);

            button.MouseLeftButtonDown += (_, _) => _onButtonClick?.Invoke(capturedIdx);
            button.MouseEnter += (_, _) =>
            {
                button.Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35));
            };
            button.MouseLeave += (_, _) =>
            {
                UpdateButtonColor(capturedIdx);
            };
            button.ToolTip = BuildToolTipStyle($"Button {idx + 1}", "Not assigned");
            _canvas.Children.Add(button);
            _buttonBorders[idx] = button;
        }

        // ── Live updates ────────────────────────────────────────────────

        private void UpdateLiveState()
        {
            if (_config == null) return;

            for (int i = 0; i < 5; i++)
            {
                // Knob positions
                float pos = App.KnobPositions[i];
                if (Math.Abs(pos - _lastKnobPos[i]) > 0.005f)
                {
                    _lastKnobPos[i] = pos;
                    _knobVisuals[i].SetValue(pos);
                }

                // LED colors
                if (App.Rgb != null)
                {
                    var (r, g, b) = App.Rgb.GetCurrentColor(i);
                    if (r != _lastR[i] || g != _lastG[i] || b != _lastB[i])
                    {
                        _lastR[i] = r; _lastG[i] = g; _lastB[i] = b;

                        var color = (r > 5 || g > 5 || b > 5)
                            ? Color.FromRgb(r, g, b)
                            : Color.FromRgb(0x22, 0x22, 0x22);

                        // Update arc color
                        _knobVisuals[i].SetArcColor(color);
                    }
                }
            }
        }

        private void UpdateLabelsAndButtons()
        {
            if (_config == null) return;

            for (int i = 0; i < 5; i++)
            {
                // Knob labels + tooltips
                var knob = _config.Knobs.Count > i ? _config.Knobs[i] : null;
                string label = !string.IsNullOrEmpty(knob?.Label) ? knob!.Label : $"Knob {i + 1}";
                _knobLabels[i].Text = label;
                _knobHitAreas[i].ToolTip = BuildKnobTooltip(i, knob);

                // Button colors + tooltips
                var btn = _config.Buttons.Count > i ? _config.Buttons[i] : null;
                _buttonBorders[i].ToolTip = BuildButtonTooltip(i, btn);
                UpdateButtonColor(i);
            }
        }

        // ── Tooltip builders ────────────────────────────────────────────

        private static object BuildKnobTooltip(int idx, KnobConfig? knob)
        {
            string title = !string.IsNullOrEmpty(knob?.Label) ? knob!.Label : $"Knob {idx + 1}";
            string target = FormatTarget(knob);

            string detail = target;
            if (knob?.Target == "apps" && knob.Apps.Count > 0)
                detail = "App Group: " + string.Join(", ", knob.Apps);

            string curve = (knob?.Curve ?? ResponseCurve.Linear).ToString();
            string range = $"{knob?.MinVolume ?? 0}% – {knob?.MaxVolume ?? 100}%";
            string body = $"{detail}\nCurve: {curve}  |  Range: {range}";

            return BuildToolTipStyle(title, body);
        }

        private static object BuildButtonTooltip(int idx, ButtonConfig? btn)
        {
            string title = !string.IsNullOrEmpty(btn?.Label) ? btn!.Label : $"Button {idx + 1}";

            var panel = new StackPanel { MaxWidth = 280 };
            panel.Children.Add(MakeTooltipTitle(title));

            bool any = false;

            string? tap = FormatAction(btn?.Action, btn?.Path, btn?.MacroKeys, btn?.ProfileName);
            if (tap != null) { panel.Children.Add(MakeGestureRow("TAP", tap, Color.FromRgb(0x66, 0xBB, 0x6A))); any = true; }

            string? dbl = FormatAction(btn?.DoublePressAction, btn?.DoublePressPath, btn?.DoublePressMacroKeys, btn?.DoublePressProfileName);
            if (dbl != null) { panel.Children.Add(MakeGestureRow("DBL", dbl, Color.FromRgb(0xFF, 0xD5, 0x4F))); any = true; }

            string? hold = FormatAction(btn?.HoldAction, btn?.HoldPath, btn?.HoldMacroKeys, btn?.HoldProfileName);
            if (hold != null) { panel.Children.Add(MakeGestureRow("HOLD", hold, Color.FromRgb(0xFF, 0x8A, 0x3D))); any = true; }

            if (!any)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Not assigned",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    FontStyle = FontStyles.Italic
                });
            }

            return WrapTooltip(panel);
        }

        private static UIElement MakeGestureRow(string badge, string action, Color badgeColor)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            // Colored badge
            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, badgeColor.R, badgeColor.G, badgeColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badge,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(badgeColor)
                }
            });

            // Action text
            row.Children.Add(new TextBlock
            {
                Text = action,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            });

            return row;
        }

        private static TextBlock MakeTooltipTitle(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private static ToolTip BuildToolTipStyle(string title, string body)
        {
            var panel = new StackPanel { MaxWidth = 260 };
            panel.Children.Add(MakeTooltipTitle(title));

            panel.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                TextWrapping = TextWrapping.Wrap
            });

            return WrapTooltip(panel);
        }

        private static ToolTip WrapTooltip(StackPanel panel) => new()
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8)
        };

        // ── Formatting helpers ──────────────────────────────────────────

        private static readonly Dictionary<string, string> s_actionNames = new()
        {
            { "none", "" }, { "media_play_pause", "Play/Pause" }, { "media_next", "Next Track" },
            { "media_prev", "Prev Track" }, { "mute_master", "Mute Volume" }, { "mute_mic", "Mute Mic" },
            { "mute_program", "Mute App" }, { "mute_active_window", "Mute Window" },
            { "mute_app_group", "Mute Group" }, { "mute_device", "Mute Device" },
            { "launch_exe", "Launch" }, { "close_program", "Close App" },
            { "cycle_output", "Cycle Output" }, { "cycle_input", "Cycle Input" },
            { "select_output", "Set Output" }, { "select_input", "Set Input" },
            { "macro", "Macro" }, { "switch_profile", "Switch Profile" },
            { "cycle_brightness", "Cycle Brightness" },
            { "power_sleep", "Sleep" }, { "power_lock", "Lock" }, { "power_off", "Shut Down" },
            { "power_restart", "Restart" }, { "power_logoff", "Log Off" }, { "power_hibernate", "Hibernate" },
            { "ha_toggle", "HA Toggle" }, { "ha_scene", "HA Scene" }, { "ha_service", "HA Service" },
            { "govee_toggle", "Govee Toggle" }, { "govee_scene", "Govee Scene" },
        };

        /// <summary>Returns formatted action string, or null if action is none/empty.</summary>
        private static string? FormatAction(string? action, string? path, string? macroKeys, string? profileName)
        {
            if (string.IsNullOrEmpty(action) || action == "none") return null;

            s_actionNames.TryGetValue(action, out var name);
            // Clean fallback: replace underscores with spaces and title-case
            name ??= System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(action.Replace("_", " "));

            var context = action switch
            {
                "launch_exe" or "close_program" or "mute_program" when !string.IsNullOrEmpty(path)
                    => System.IO.Path.GetFileNameWithoutExtension(path),
                "switch_profile" when !string.IsNullOrEmpty(profileName) => profileName,
                "macro" when !string.IsNullOrEmpty(macroKeys) => macroKeys,
                _ => null
            };

            return context != null ? $"{name}: {context}" : name;
        }

        private static string FormatTarget(KnobConfig? knob)
        {
            var t = knob?.Target ?? "none";
            return t switch
            {
                "none" or "" => "Not assigned",
                "master" => "Master Volume",
                "mic" => "Microphone",
                "system" => "System Sounds",
                "any" => "Any Active App",
                "active_window" => "Active Window",
                "apps" => "App Group",
                "output_device" => "Output Device",
                "input_device" => "Input Device",
                "monitor" => "Monitor Brightness",
                "led_brightness" => "LED Brightness",
                _ when t.StartsWith("ha_") => "Home Assistant",
                _ when t.StartsWith("govee") => "Govee",
                _ => t
            };
        }

        private void UpdateButtonColor(int idx)
        {
            if (_config == null) return;

            var btn = _config.Buttons.Count > idx ? _config.Buttons[idx] : null;
            bool hasAction = btn != null && !string.IsNullOrEmpty(btn.Action) && btn.Action != "none";

            if (hasAction)
            {
                var actionColor = GetActionCategoryColor(btn!.Action);
                var dimmed = Color.FromArgb(0x60, actionColor.R, actionColor.G, actionColor.B);
                var brush = new SolidColorBrush(dimmed);
                brush.Freeze();
                _buttonBorders[idx].Background = brush;
                var borderBrush = new SolidColorBrush(Color.FromArgb(0x40, actionColor.R, actionColor.G, actionColor.B));
                borderBrush.Freeze();
                _buttonBorders[idx].BorderBrush = borderBrush;
            }
            else
            {
                var brush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
                brush.Freeze();
                _buttonBorders[idx].Background = brush;
                var borderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
                borderBrush.Freeze();
                _buttonBorders[idx].BorderBrush = borderBrush;
            }
        }

        private static Color GetActionCategoryColor(string action)
        {
            if (action.StartsWith("media_")) return Color.FromRgb(0x66, 0xBB, 0x6A); // green
            if (action.StartsWith("mute_"))  return Color.FromRgb(0xEF, 0x53, 0x50); // red
            if (action == "launch_exe" || action == "close_program") return Color.FromRgb(0x42, 0xA5, 0xF5); // blue
            if (action.StartsWith("cycle_") || action.StartsWith("select_")) return Color.FromRgb(0xAB, 0x47, 0xBC); // purple
            if (action == "macro") return Color.FromRgb(0xFF, 0xD5, 0x4F); // gold
            if (action == "switch_profile") return Color.FromRgb(0x29, 0xB6, 0xF6); // cyan
            if (action.StartsWith("power_")) return Color.FromRgb(0xFF, 0x44, 0x44); // red
            if (action.StartsWith("ha_")) return Color.FromRgb(0x26, 0xC6, 0xDA); // teal
            return Color.FromRgb(0x66, 0x66, 0x66);
        }

        // ── Knob rendering element ──────────────────────────────────────

        /// <summary>
        /// Lightweight element that renders a single knob (arc + knob-face image).
        /// </summary>
        internal class KnobVisual : FrameworkElement
        {
            private readonly double _size;
            private float _value;
            private Color _arcColor = Color.FromRgb(0x00, 0xE6, 0x76);
            private bool _hovered;

            // Cached pens
            private Pen? _valuePen;
            private Pen? _glowPen;
            private Color _cachedColor;

            public KnobVisual(double size)
            {
                _size = size;
                Width = size;
                Height = size;
                IsHitTestVisible = false; // hit testing handled by overlay Border
                RebuildPens();
            }

            public void SetValue(float value)
            {
                _value = Math.Clamp(value, 0f, 1f);
                InvalidateVisual();
            }

            public void SetArcColor(Color color)
            {
                if (color == _arcColor) return;
                _arcColor = color;
                RebuildPens();
                InvalidateVisual();
            }

            public void SetHovered(bool hovered)
            {
                if (_hovered == hovered) return;
                _hovered = hovered;
                InvalidateVisual();
            }

            private void RebuildPens()
            {
                if (_arcColor == _cachedColor && _valuePen != null) return;
                _cachedColor = _arcColor;

                var vb = new SolidColorBrush(_arcColor);
                vb.Freeze();
                _valuePen = new Pen(vb, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                _valuePen.Freeze();

                var gc = Color.FromArgb(0x50, _arcColor.R, _arcColor.G, _arcColor.B);
                var gb = new SolidColorBrush(gc);
                gb.Freeze();
                _glowPen = new Pen(gb, GlowStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                _glowPen.Freeze();
            }

            protected override void OnRender(DrawingContext dc)
            {
                double cx = _size / 2.0;
                double cy = _size / 2.0;
                double radius = _size / 2.0 - 6.0;

                // Hover highlight ring
                if (_hovered)
                {
                    var hoverBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                    hoverBrush.Freeze();
                    dc.DrawEllipse(hoverBrush, null, new Point(cx, cy), radius + 4, radius + 4);
                }

                // Track arc (background)
                var trackGeo = CreateArc(cx, cy, radius, StartAngleDeg, TotalSweepDeg);
                dc.DrawGeometry(null, s_trackPen, trackGeo);

                if (_value > 0.003f)
                {
                    double sweep = _value * TotalSweepDeg;

                    // Glow + value arc
                    var arcGeo = CreateArc(cx, cy, radius, StartAngleDeg, sweep);
                    dc.DrawGeometry(null, _glowPen, arcGeo);
                    dc.DrawGeometry(null, _valuePen, arcGeo);

                    // Tip dot
                    double tipRad = (StartAngleDeg + sweep - 90.0) * Math.PI / 180.0;
                    var tipBrush = new SolidColorBrush(_arcColor);
                    tipBrush.Freeze();
                    dc.DrawEllipse(tipBrush, null,
                        new Point(cx + radius * Math.Cos(tipRad), cy + radius * Math.Sin(tipRad)),
                        2.5, 2.5);
                }

                // Knob face image (rotated)
                double rotDeg = 45.0 + (_value * 270.0);
                double knobSize = (radius * 2.0) * KnobImageRatio;
                double knobLeft = cx - knobSize / 2.0;
                double knobTop = cy - knobSize / 2.0;

                dc.PushTransform(new RotateTransform(rotDeg, cx, cy));
                dc.DrawImage(s_knobImage, new Rect(knobLeft, knobTop, knobSize, knobSize));
                dc.Pop();
            }

            private static StreamGeometry CreateArc(double cx, double cy, double r, double startDeg, double sweepDeg)
            {
                double startRad = (startDeg - 90.0) * Math.PI / 180.0;
                double endRad = (startDeg - 90.0 + sweepDeg) * Math.PI / 180.0;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(
                        new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad)),
                        false, false);
                    ctx.ArcTo(
                        new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad)),
                        new Size(r, r), 0,
                        sweepDeg > 180.0, SweepDirection.Clockwise, true, false);
                }
                geo.Freeze();
                return geo;
            }
        }
    }
}
