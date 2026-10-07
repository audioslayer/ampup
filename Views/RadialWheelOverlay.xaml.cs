using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// "Halo" radial overlay for the Quick Wheel: a ring with exactly one segment per item
/// (no blank slots) and a center card naming the highlighted item in full.
/// Set*() populates; Highlight() follows knob navigation; OnSegmentClicked fires on selection.
/// </summary>
public partial class RadialWheelOverlay : Window
{
    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Fires when user clicks a segment (index) or presses Enter/Space, or -1 on Escape.</summary>
    public Action<int>? OnSegmentClicked;

    /// <summary>Most items the ring can show cleanly.</summary>
    public const int MaxSlots = 12;

    private int _monitorIndex; // which monitor to center on (from OSD config)
    private readonly List<string> _ids = new();
    private readonly List<string> _labels = new();
    private readonly List<Color> _colors = new();
    private readonly List<string> _symbols = new();
    private string _kind = "";
    private int _activeIndex = -1; // item that's currently in effect (active profile / default device)
    private int _highlighted = -1;
    private bool _dismissing;

    // Geometry (canvas is 420x420)
    private const double CenterX = 210;
    private const double CenterY = 210;
    private const double RingInner = 154, RingOuter = 186;
    private const double HlInner = 148, HlOuter = 196;
    private const double GapDeg = 2.2;

    private static Color AccentColor => ThemeManager.Accent;
    private static readonly Color DimText = Color.FromRgb(0x8A, 0x8A, 0x8A);

    public RadialWheelOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            OuterGlow.Color = AccentColor;
            CenterDisc.Stroke = new SolidColorBrush(Color.FromArgb(0x24, AccentColor.R, AccentColor.G, AccentColor.B));
            Focus();
            Keyboard.Focus(RootGrid);
            PlayFadeIn();
        };
        // Mouse wheel steps the highlight like a knob turn
        MouseWheel += (_, e) => Highlight(_highlighted + (e.Delta < 0 ? 1 : -1));
    }

    /// <summary>Populate from built wheel content (see QuickWheelContent). Call before Show().</summary>
    public void SetContent(AmpUp.Services.QuickWheelContent content)
    {
        _ids.Clear(); _labels.Clear(); _symbols.Clear(); _colors.Clear();
        foreach (var it in content.Items.Take(MaxSlots))
        {
            _ids.Add(it.Id);
            _labels.Add(it.Label);
            _symbols.Add(it.Symbol);
            _colors.Add(it.Color);
        }
        _kind = content.Kind;
        _activeIndex = content.ActiveIndex;
        _highlighted = _ids.Count == 0 ? -1 : Math.Clamp(content.CurrentIndex, 0, _ids.Count - 1);
        Render();
        CenterOnScreen();
    }

    public int GetTotalSlots() => Math.Max(1, _ids.Count);

    /// <summary>Returns the ID string at the highlighted index, or null if empty.</summary>
    public string? GetSelectedId()
    {
        if (_highlighted >= 0 && _highlighted < _ids.Count)
            return _ids[_highlighted];
        return null;
    }

    /// <summary>Move the highlight to the given item index (0-based, wraps).</summary>
    public void Highlight(int index)
    {
        if (_ids.Count == 0) return;
        index = ((index % _ids.Count) + _ids.Count) % _ids.Count;
        if (index == _highlighted) return;
        _highlighted = index;
        Render();
    }

    public int GetSelectedIndex() => _highlighted;

    /// <summary>Fade out and close without selecting.</summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        PlayFadeOut(() => Close());
    }

    // ── Rendering ────────────────────────────────────────────────────

    private void Render()
    {
        BuildRing();
        BuildCenter();
    }

    private void BuildRing()
    {
        SegmentCanvas.Children.Clear();
        int n = _ids.Count;
        if (n == 0) return;
        double step = 360.0 / n;

        for (int i = 0; i < n; i++)
        {
            bool hl = i == _highlighted;
            var c = _colors[i];
            double mid = -90.0 + i * step;

            var path = new Path
            {
                Data = BuildRingSlice(mid - step / 2 + GapDeg, mid + step / 2 - GapDeg,
                    hl ? HlInner : RingInner, hl ? HlOuter : RingOuter),
                Fill = new SolidColorBrush(hl ? c : Color.FromArgb(0x33, c.R, c.G, c.B)),
                Cursor = Cursors.Hand,
            };
            if (hl)
                path.Effect = new DropShadowEffect { Color = c, BlurRadius = 18, Opacity = 0.6, ShadowDepth = 0 };
            int cap = i;
            path.MouseEnter += (_, _) => OnSegHover(cap);
            path.MouseLeftButtonDown += (_, _) => ConfirmAndDismiss(cap);
            SegmentCanvas.Children.Add(path);

            if (Enum.TryParse<MaterialIconKind>(_symbols[i], out var kind))
            {
                double size = hl ? 22 : 18;
                double r = hl ? 172 : 170;
                double rad = mid * Math.PI / 180.0;
                var icon = new MaterialIcon
                {
                    Kind = kind,
                    Width = size, Height = size,
                    IsHitTestVisible = false,
                    // Dark glyph on the solid highlighted segment, colored glyph on the dim ones
                    Foreground = new SolidColorBrush(hl ? Color.FromRgb(0x0F, 0x0F, 0x0F) : c),
                };
                Canvas.SetLeft(icon, CenterX + r * Math.Cos(rad) - size / 2);
                Canvas.SetTop(icon, CenterY + r * Math.Sin(rad) - size / 2);
                SegmentCanvas.Children.Add(icon);
            }
        }
    }

    private void BuildCenter()
    {
        CenterPanel.Children.Clear();
        if (_highlighted < 0 || _highlighted >= _ids.Count) return;
        var c = _colors[_highlighted];

        if (Enum.TryParse<MaterialIconKind>(_symbols[_highlighted], out var kind))
        {
            CenterPanel.Children.Add(new MaterialIcon
            {
                Kind = kind,
                Width = 34, Height = 34,
                Foreground = new SolidColorBrush(c),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        CenterPanel.Children.Add(new TextBlock
        {
            Text = _labels[_highlighted],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 44, // two lines
        });

        if (!string.IsNullOrEmpty(_kind))
        {
            CenterPanel.Children.Add(new TextBlock
            {
                Text = _kind,
                FontSize = 11,
                Foreground = new SolidColorBrush(DimText),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        if (_highlighted == _activeIndex)
        {
            var a = AccentColor;
            CenterPanel.Children.Add(new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x24, a.R, a.G, a.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, a.R, a.G, a.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 2, 9, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = "ACTIVE",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(a),
                },
            });
        }
        else
        {
            CenterPanel.Children.Add(new TextBlock
            {
                Text = $"{_highlighted + 1} of {_ids.Count}",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x6F)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
            });
        }
    }

    /// <summary>Annular slice between two angles (degrees, 0 = right, clockwise).</summary>
    private static PathGeometry BuildRingSlice(double startDeg, double endDeg, double innerR, double outerR)
    {
        double s = startDeg * Math.PI / 180.0, e = endDeg * Math.PI / 180.0;
        Point Pt(double r, double a) => new(CenterX + r * Math.Cos(a), CenterY + r * Math.Sin(a));
        bool large = endDeg - startDeg > 180;

        var fig = new PathFigure { StartPoint = Pt(innerR, s), IsClosed = true };
        fig.Segments.Add(new LineSegment(Pt(outerR, s), true));
        fig.Segments.Add(new ArcSegment(Pt(outerR, e), new Size(outerR, outerR), 0,
            large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(Pt(innerR, e), true));
        fig.Segments.Add(new ArcSegment(Pt(innerR, s), new Size(innerR, innerR), 0,
            large, SweepDirection.Counterclockwise, true));
        return new PathGeometry(new[] { fig });
    }

    private void OnSegHover(int idx)
    {
        if (idx == _highlighted) return;
        _highlighted = idx;
        Render();
    }

    private void ConfirmAndDismiss(int idx)
    {
        if (_dismissing) return;
        if (idx < 0 || idx >= _ids.Count) return;
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
            Highlight(_highlighted - 1);
        }
        else if (e.Key == Key.Right || e.Key == Key.Down)
        {
            Highlight(_highlighted + 1);
        }
    }
}
