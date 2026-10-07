using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AmpUp.Controls;
using AmpUp.Services;

namespace AmpUp.Views;

/// <summary>
/// On-screen Quick Wheel: a topmost window around the Halo wheel (Controls/HaloWheel).
/// SetContent() populates; Highlight() follows knob navigation; OnSegmentClicked fires on selection.
/// </summary>
public partial class RadialWheelOverlay : Window
{
    /// <summary>Fires when user clicks a segment (index) or presses Enter/Space, or -1 on Escape.</summary>
    public Action<int>? OnSegmentClicked;

    /// <summary>Most items the ring can show cleanly.</summary>
    public const int MaxSlots = HaloWheel.MaxSlots;

    private readonly HaloWheel _wheel = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private int _monitorIndex; // which monitor to center on (from OSD config)
    private bool _dismissing;

    public RadialWheelOverlay()
    {
        InitializeComponent();
        RootGrid.Children.Add(_wheel);
        _wheel.SegmentClicked += ConfirmAndDismiss;
        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(RootGrid);
            PlayFadeIn();
        };
        // Mouse wheel steps the highlight like a knob turn
        MouseWheel += (_, e) => Highlight(_wheel.HighlightedIndex + (e.Delta < 0 ? 1 : -1));
    }

    /// <summary>Populate from built wheel content (see QuickWheelContent). Call before Show().</summary>
    public void SetContent(QuickWheelContent content)
    {
        _wheel.SetContent(content);
        CenterOnScreen();
    }

    public int GetTotalSlots() => Math.Max(1, _wheel.Count);

    /// <summary>Move the highlight to the given item index (0-based, wraps).</summary>
    public void Highlight(int index) => _wheel.Highlight(index);

    public int GetSelectedIndex() => _wheel.HighlightedIndex;

    /// <summary>Fade out and close without selecting.</summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        PlayFadeOut(() => Close());
    }

    private void ConfirmAndDismiss(int idx)
    {
        if (_dismissing) return;
        if (idx < 0 || idx >= _wheel.Count) return;
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
            ConfirmAndDismiss(_wheel.HighlightedIndex);
        }
        else if (e.Key == Key.Left || e.Key == Key.Up)
        {
            Highlight(_wheel.HighlightedIndex - 1);
        }
        else if (e.Key == Key.Right || e.Key == Key.Down)
        {
            Highlight(_wheel.HighlightedIndex + 1);
        }
    }
}
