using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

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

    private List<string> _profiles = new();
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
    private static readonly Color AccentColor = Color.FromRgb(0x00, 0xE6, 0x76);

    private readonly List<Path> _segPaths = new();
    private readonly List<TextBlock> _segLabels = new();

    public RadialWheelOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(RootGrid);
            PlayFadeIn();
        };
    }

    /// <summary>
    /// Populate the wheel with profiles and show it centered on screen.
    /// Call before Show().
    /// </summary>
    public void SetProfiles(List<string> profiles, int currentIndex)
    {
        _profiles = profiles;
        _highlighted = currentIndex >= 0 ? currentIndex : 0;
        BuildSegments();
        CenterOnScreen();
    }

    /// <summary>
    /// Move the highlight to the given segment index (0-based).
    /// </summary>
    public void Highlight(int index)
    {
        if (_profiles.Count == 0) return;
        index = ((index % _profiles.Count) + _profiles.Count) % _profiles.Count;
        if (index == _highlighted) return;
        _highlighted = index;
        RefreshSegmentColors();
        CenterLabel.Text = _profiles[_highlighted];
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

        int count = _profiles.Count;
        if (count == 0) return;

        double sweep = 360.0 / count;

        for (int i = 0; i < count; i++)
        {
            double startAngle = -90.0 + i * sweep;
            var path = BuildSegmentPath(startAngle, sweep, i == _highlighted);
            path.MouseEnter += (_, _) =>
            {
                int idx = _segPaths.IndexOf((Path)((FrameworkElement)_ !).Source!);
                // re-resolve at mouse time from actual sender
            };

            int cap = i;
            path.MouseEnter += (s, _) => OnSegHover(cap);
            path.MouseLeave += (_, _) => { /* leave is handled by re-entering another */ };
            path.MouseLeftButtonDown += (_, _) => ConfirmAndDismiss(cap);
            path.Cursor = Cursors.Hand;
            SegmentCanvas.Children.Add(path);
            _segPaths.Add(path);

            // Label
            double midAngle = startAngle + sweep / 2.0;
            double labelR = (OuterR + InnerR) / 2.0;
            double lx = CenterX + labelR * Math.Cos(midAngle * Math.PI / 180.0);
            double ly = CenterY + labelR * Math.Sin(midAngle * Math.PI / 180.0);

            var tb = new TextBlock
            {
                Text = _profiles[i],
                FontSize = count <= 6 ? 12 : 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextAlignment = TextAlignment.Center,
                MaxWidth = 90,
                TextWrapping = TextWrapping.Wrap,
            };
            tb.Measure(new Size(90, 60));
            Canvas.SetLeft(tb, lx - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, ly - tb.DesiredSize.Height / 2);
            tb.IsHitTestVisible = false;
            SegmentCanvas.Children.Add(tb);
            _segLabels.Add(tb);
        }

        CenterLabel.Text = _highlighted >= 0 && _highlighted < _profiles.Count
            ? _profiles[_highlighted]
            : "";
    }

    private static Path BuildSegmentPath(double startAngleDeg, double sweepDeg, bool highlighted)
    {
        var geo = BuildPieSlice(startAngleDeg, sweepDeg);
        var path = new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(highlighted ? SegmentHover : SegmentBase),
            Stroke = new SolidColorBrush(highlighted
                ? Color.FromArgb(0xCC, AccentColor.R, AccentColor.G, AccentColor.B)
                : Color.FromRgb(0x2A, 0x2A, 0x2A)),
            StrokeThickness = highlighted ? 2 : 1,
        };
        if (highlighted)
        {
            path.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = AccentColor,
                BlurRadius = 14,
                Opacity = 0.55,
                ShadowDepth = 0
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
            _segPaths[i].Fill = new SolidColorBrush(hl ? SegmentHover : SegmentBase);
            _segPaths[i].Stroke = new SolidColorBrush(hl
                ? Color.FromArgb(0xCC, AccentColor.R, AccentColor.G, AccentColor.B)
                : Color.FromRgb(0x2A, 0x2A, 0x2A));
            _segPaths[i].StrokeThickness = hl ? 2 : 1;
            _segPaths[i].Effect = hl
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = AccentColor, BlurRadius = 14, Opacity = 0.55, ShadowDepth = 0
                }
                : null;
        }
        CenterLabel.Text = _highlighted >= 0 && _highlighted < _profiles.Count
            ? _profiles[_highlighted]
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
        _dismissing = true;
        PlayFadeOut(() =>
        {
            Close();
            OnSegmentClicked?.Invoke(idx);
        });
    }

    private void CenterOnScreen()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null)
        {
            Left = screen.WorkingArea.Left + (screen.WorkingArea.Width - Width) / 2;
            Top = screen.WorkingArea.Top + (screen.WorkingArea.Height - Height) / 2;
        }
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
