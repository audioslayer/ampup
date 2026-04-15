using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// Radial pie-segment overlay for quick profile switching via hardware wheel.
/// Show() populates segments; Highlight() follows knob navigation; OnSegmentClicked fires on selection.
/// </summary>
public partial class RadialWheelOverlay : Window
{
    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Fires when user clicks a segment (index) or presses Enter/Space, or -1 on Escape.</summary>
    public Action<int>? OnSegmentClicked;

    private const int TotalSlots = 8; // always 8 petals
    private int _monitorIndex; // which monitor to center on (from OSD config)
    private List<string> _profiles = new();
    private readonly string[] _slotLabels = new string[TotalSlots]; // padded to 8
    private readonly Color[] _slotColors = new Color[TotalSlots];
    private readonly string[] _slotSymbols = new string[TotalSlots];
    private int _highlighted = -1;
    private bool _dismissing;

    // Geometry constants
    private const double CenterX = 210;
    private const double CenterY = 210;
    private const double OuterR = 200;
    private const double InnerR = 62;  // clears center label circle (120/2 + 2)

    // Colors
    private static readonly Color SegmentBase = Color.FromArgb(0xCC, 0x1C, 0x1C, 0x1C);
    private static readonly Color SegmentHover = Color.FromArgb(0xEE, 0x22, 0x22, 0x22);
    private static Color AccentColor => ThemeManager.Accent;

    private readonly List<Path> _segPaths = new();
    private readonly List<TextBlock> _segLabels = new();

    public RadialWheelOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            OuterGlow.Color = AccentColor;
            Focus();
            Keyboard.Focus(RootGrid);
            PlayFadeIn();
        };
    }

    /// <summary>
    /// Populate the wheel with profiles and show it centered on screen.
    /// Call before Show().
    /// </summary>
    public void SetProfiles(List<string> profiles, int currentIndex,
                            Dictionary<string, ProfileIconConfig>? icons = null)
    {
        _profiles = profiles;
        // Pad to 8 slots: profiles first, then blanks
        for (int i = 0; i < TotalSlots; i++)
        {
            _slotLabels[i] = i < profiles.Count ? profiles[i] : "";
            if (i < profiles.Count && icons != null
                && icons.TryGetValue(profiles[i], out var cfg))
            {
                try { _slotColors[i] = (Color)ColorConverter.ConvertFromString(cfg.Color); }
                catch { _slotColors[i] = AccentColor; }
                _slotSymbols[i] = cfg.Symbol;
            }
            else
            {
                _slotColors[i] = AccentColor;
                _slotSymbols[i] = "";
            }
        }
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
        CenterOnScreen();
    }

    public int GetTotalSlots() => TotalSlots;

    /// <summary>
    /// Populate the wheel with audio output devices. Pads to 8 slots.
    /// </summary>
    public void SetDevices(List<(string id, string name)> devices, int currentIndex)
    {
        _profiles = devices.Select(d => d.id).ToList(); // store IDs for selection
        for (int i = 0; i < TotalSlots; i++)
        {
            _slotLabels[i] = i < devices.Count ? devices[i].name : "";
            _slotColors[i] = i < devices.Count
                ? Color.FromRgb(0xAB, 0x47, 0xBC) // purple for devices
                : AccentColor;
            _slotSymbols[i] = i < devices.Count ? "VolumeHigh" : "";
        }
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
        CenterOnScreen();
    }

    /// <summary>
    /// Populate the wheel with arbitrary actions (for MediaControls and Custom modes).
    /// Each tuple: (id, label, materialIconName, color).
    /// </summary>
    public void SetActions(List<(string id, string label, string symbol, Color color)> actions, int currentIndex)
    {
        _profiles = actions.Select(a => a.id).ToList();
        for (int i = 0; i < TotalSlots; i++)
        {
            if (i < actions.Count)
            {
                _slotLabels[i] = actions[i].label;
                _slotColors[i] = actions[i].color;
                _slotSymbols[i] = actions[i].symbol;
            }
            else
            {
                _slotLabels[i] = "";
                _slotColors[i] = AccentColor;
                _slotSymbols[i] = "";
            }
        }
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
        CenterOnScreen();
    }

    /// <summary>Returns the ID string at the highlighted index, or null if empty.</summary>
    public string? GetSelectedId()
    {
        if (_highlighted >= 0 && _highlighted < _profiles.Count)
            return _profiles[_highlighted];
        return null;
    }

    /// <summary>
    /// Move the highlight to the given segment index (0-based).
    /// </summary>
    public void Highlight(int index)
    {
        index = ((index % TotalSlots) + TotalSlots) % TotalSlots;
        if (index == _highlighted) return;
        _highlighted = index;
        RefreshSegmentColors();
        CenterLabel.Text = !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted] : "";
    }

    public int GetSelectedIndex() => _highlighted;

    /// <summary>
    /// Fade out and close without selecting.
    /// </summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        PlayFadeOut(() => Close());
    }

    // ── Geometry helpers ─────────────────────────────────────────────

    private void BuildSegments()
    {
        SegmentCanvas.Children.Clear();
        _segPaths.Clear();
        _segLabels.Clear();

        double sweep = 360.0 / TotalSlots;

        for (int i = 0; i < TotalSlots; i++)
        {
            bool isEmpty = string.IsNullOrEmpty(_slotLabels[i]);
            double startAngle = -90.0 + i * sweep;
            var path = BuildSegmentPath(startAngle, sweep, i == _highlighted, isEmpty, _slotColors[i]);
            int cap = i;
            if (!isEmpty)
            {
                path.MouseEnter += (s, _) => OnSegHover(cap);
                path.MouseLeftButtonDown += (_, _) => ConfirmAndDismiss(cap);
                path.Cursor = Cursors.Hand;
            }
            SegmentCanvas.Children.Add(path);
            _segPaths.Add(path);

            // Label (icon + name stacked vertically)
            double midAngle = startAngle + sweep / 2.0;
            double labelR = (OuterR + InnerR) / 2.0;
            double lx = CenterX + labelR * Math.Cos(midAngle * Math.PI / 180.0);
            double ly = CenterY + labelR * Math.Sin(midAngle * Math.PI / 180.0);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false,
            };

            // Icon (Material Icon if available)
            if (!isEmpty && !string.IsNullOrEmpty(_slotSymbols[i])
                && Enum.TryParse<MaterialIconKind>(_slotSymbols[i], out var iconKind))
            {
                var icon = new MaterialIcon
                {
                    Kind = iconKind,
                    Width = 18, Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(_slotColors[i]),
                };
                stack.Children.Add(icon);
            }

            var tb = new TextBlock
            {
                Text = isEmpty ? "" : _slotLabels[i],
                FontSize = 10,
                FontWeight = isEmpty ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isEmpty
                    ? ((SolidColorBrush)Application.Current.FindResource("InputBorderBrush")).Color
                    : Colors.White),
                TextAlignment = TextAlignment.Center,
                MaxWidth = 75,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            stack.Children.Add(tb);

            stack.Measure(new Size(80, 60));
            Canvas.SetLeft(stack, lx - stack.DesiredSize.Width / 2);
            Canvas.SetTop(stack, ly - stack.DesiredSize.Height / 2);
            SegmentCanvas.Children.Add(stack);
            _segLabels.Add(tb);
        }

        CenterLabel.Text = _highlighted >= 0 && _highlighted < TotalSlots
            && !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted]
            : "";
    }

    private static readonly Color EmptyBase = Color.FromArgb(0x88, 0x12, 0x12, 0x12);

    private static Path BuildSegmentPath(double startAngleDeg, double sweepDeg, bool highlighted,
                                         bool isEmpty = false, Color? slotColor = null)
    {
        var geo = BuildPieSlice(startAngleDeg, sweepDeg);
        var sc = slotColor ?? AccentColor;
        Color fill, stroke;
        double strokeW;

        if (isEmpty)
        {
            fill = highlighted ? Color.FromArgb(0xAA, 0x18, 0x18, 0x18) : EmptyBase;
            stroke = highlighted
                ? Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B)
                : ((SolidColorBrush)Application.Current.FindResource("InputBgBrush")).Color;
            strokeW = 1;
        }
        else if (highlighted)
        {
            fill = Color.FromArgb(0x35, sc.R, sc.G, sc.B);
            stroke = Color.FromArgb(0xCC, sc.R, sc.G, sc.B);
            strokeW = 2;
        }
        else
        {
            fill = Color.FromArgb(0x18, sc.R, sc.G, sc.B);
            stroke = Color.FromArgb(0x40, sc.R, sc.G, sc.B);
            strokeW = 1;
        }

        var path = new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = strokeW,
        };
        if (highlighted && !isEmpty)
        {
            path.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = sc, BlurRadius = 14, Opacity = 0.55, ShadowDepth = 0
            };
        }
        return path;
    }

    private static PathGeometry BuildPieSlice(double startAngleDeg, double sweepDeg)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg - 0.5) * Math.PI / 180.0; // -0.5° gap

        var outerStart = new Point(CenterX + OuterR * Math.Cos(startRad), CenterY + OuterR * Math.Sin(startRad));
        var outerEnd = new Point(CenterX + OuterR * Math.Cos(endRad), CenterY + OuterR * Math.Sin(endRad));
        var innerStart = new Point(CenterX + InnerR * Math.Cos(startRad), CenterY + InnerR * Math.Sin(startRad));
        var innerEnd = new Point(CenterX + InnerR * Math.Cos(endRad), CenterY + InnerR * Math.Sin(endRad));

        bool isLargeArc = sweepDeg > 180;

        var fig = new PathFigure { StartPoint = innerStart, IsClosed = true };
        fig.Segments.Add(new LineSegment(outerStart, true));
        fig.Segments.Add(new ArcSegment(outerEnd, new Size(OuterR, OuterR), 0,
            isLargeArc, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(innerEnd, true));
        fig.Segments.Add(new ArcSegment(innerStart, new Size(InnerR, InnerR), 0,
            isLargeArc, SweepDirection.Counterclockwise, true));

        return new PathGeometry(new[] { fig });
    }

    private void RefreshSegmentColors()
    {
        for (int i = 0; i < _segPaths.Count; i++)
        {
            bool hl = i == _highlighted;
            bool empty = string.IsNullOrEmpty(_slotLabels[i]);
            var sc = _slotColors[i];

            if (empty)
            {
                _segPaths[i].Fill = new SolidColorBrush(hl
                    ? Color.FromArgb(0xAA, 0x18, 0x18, 0x18) : EmptyBase);
                _segPaths[i].Stroke = new SolidColorBrush(hl
                    ? Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B)
                    : ((SolidColorBrush)Application.Current.FindResource("InputBgBrush")).Color);
                _segPaths[i].StrokeThickness = 1;
                _segPaths[i].Effect = null;
            }
            else if (hl)
            {
                _segPaths[i].Fill = new SolidColorBrush(Color.FromArgb(0x35, sc.R, sc.G, sc.B));
                _segPaths[i].Stroke = new SolidColorBrush(Color.FromArgb(0xCC, sc.R, sc.G, sc.B));
                _segPaths[i].StrokeThickness = 2;
                _segPaths[i].Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = sc, BlurRadius = 14, Opacity = 0.55, ShadowDepth = 0
                };
            }
            else
            {
                _segPaths[i].Fill = new SolidColorBrush(Color.FromArgb(0x18, sc.R, sc.G, sc.B));
                _segPaths[i].Stroke = new SolidColorBrush(Color.FromArgb(0x40, sc.R, sc.G, sc.B));
                _segPaths[i].StrokeThickness = 1;
                _segPaths[i].Effect = null;
            }
        }
        CenterLabel.Text = _highlighted >= 0 && _highlighted < TotalSlots
            && !string.IsNullOrEmpty(_slotLabels[_highlighted])
            ? _slotLabels[_highlighted]
            : "";
    }

    private void OnSegHover(int idx)
    {
        if (idx == _highlighted) return;
        _highlighted = idx;
        RefreshSegmentColors();
    }

    private void ConfirmAndDismiss(int idx)
    {
        if (_dismissing) return;
        // Don't confirm on empty slots
        if (idx >= 0 && idx < TotalSlots && string.IsNullOrEmpty(_slotLabels[idx])) return;
        _dismissing = true;
        PlayFadeOut(() =>
        {
            Close();
            OnSegmentClicked?.Invoke(idx);
        });
    }

    /// <summary>Set which monitor to display on (matches OSD MonitorIndex).</summary>
    public void SetMonitor(int monitorIndex) => _monitorIndex = monitorIndex;

    private void CenterOnScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = (_monitorIndex >= 0 && _monitorIndex < screens.Length)
            ? screens[_monitorIndex]
            : System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
        Left = screen.WorkingArea.Left + (screen.WorkingArea.Width - Width) / 2;
        Top = screen.WorkingArea.Top + (screen.WorkingArea.Height - Height) / 2;
    }

    // ── Animation helpers ────────────────────────────────────────────

    private void PlayFadeIn()
    {
        var sb = (Storyboard)FindResource("FadeIn");
        sb.Begin(this, true);
    }

    private void PlayFadeOut(Action onComplete)
    {
        var sb = (Storyboard)FindResource("FadeOut");
        sb.Completed += (_, _) => Dispatcher.Invoke(onComplete);
        sb.Begin(this, true);
    }

    // ── Input handlers ───────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_dismissing) return;
            _dismissing = true;
            PlayFadeOut(() =>
            {
                Close();
                OnSegmentClicked?.Invoke(-1);
            });
        }
        else if (e.Key == Key.Return || e.Key == Key.Space)
        {
            ConfirmAndDismiss(_highlighted);
        }
        else if (e.Key == Key.Left || e.Key == Key.Up)
        {
            Highlight((_highlighted - 1 + _profiles.Count) % _profiles.Count);
        }
        else if (e.Key == Key.Right || e.Key == Key.Down)
        {
            Highlight((_highlighted + 1) % _profiles.Count);
        }
    }
}
