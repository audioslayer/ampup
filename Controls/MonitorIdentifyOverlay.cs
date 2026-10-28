using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Material.Icons;

namespace AmpUp.Controls;

/// <summary>
/// "Identify" for the monitor pickers, like Windows' own: flashes each screen's number and name
/// on that screen for a few seconds. The numbers are the ones the pickers show ("2 · VS32254K"),
/// so two monitors of the same model can be told apart.
/// </summary>
internal static class MonitorIdentifyOverlay
{
    private const double ShowSeconds = 3;
    private const int ScreenMarginPx = 48;

    private static readonly List<Window> _open = new();
    private static DispatcherTimer? _closeTimer;

    /// <summary>Small icon button that runs Identify; sits next to a monitor picker.</summary>
    public static FrameworkElement CreateButton() =>
        UiKit.IconButton(MaterialIconKind.MonitorEye, "Identify monitors: show each screen's number on it", _ => Show(), size: 30);

    public static void Show()
    {
        CloseAll();
        foreach (var monitor in DisplayMonitorResolver.GetMonitors())
        {
            try { _open.Add(ShowOn(monitor)); }
            catch (Exception ex) { Logger.Log($"Identify monitors: {monitor.DeviceName} failed: {ex.Message}"); }
        }

        _closeTimer ??= CreateCloseTimer();
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private static DispatcherTimer CreateCloseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ShowSeconds) };
        timer.Tick += (_, _) => { timer.Stop(); CloseAll(); };
        return timer;
    }

    private static void CloseAll()
    {
        foreach (var w in _open)
            try { w.Close(); } catch { }
        _open.Clear();
    }

    private static Window ShowOn(DisplayMonitorInfo monitor)
    {
        var accent = new SolidColorBrush(ThemeManager.Accent);

        var number = new TextBlock
        {
            Text = monitor.Number.ToString(),
            FontSize = 120,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -10, 0, -6),
        };
        var name = new TextBlock
        {
            Text = monitor.DisplayName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var stack = new StackPanel();
        stack.Children.Add(number);
        stack.Children.Add(name);
        if (monitor.IsPrimary)
        {
            var primary = new TextBlock
            {
                Text = "PRIMARY",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            primary.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            stack.Children.Add(primary);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1.5),
            BorderBrush = accent,
            Padding = new Thickness(44, 10, 44, 22),
            MinWidth = 200,
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ThemeManager.Accent, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.45,
            },
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        // Display-only, never activated; it closes on its own or on click. Transparency is
        // what gives the card its rounded corners and glow.
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Content = new Border { Padding = new Thickness(30), Child = card }, // room for the glow
        };
        window.MouseLeftButtonUp += (_, _) => CloseAll();
        window.Show();

        // Place in physical pixels: the app is per-monitor DPI aware, so WPF rescales the
        // window for that screen's DPI once it lands there.
        var hwnd = new WindowInteropHelper(window).Handle;
        var wa = monitor.WorkingArea;
        SetWindowPos(hwnd, IntPtr.Zero, wa.Left + ScreenMarginPx, wa.Top + ScreenMarginPx, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return window;
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
