using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AmpUp;

public partial class DeviceSwitchOverlay : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private bool _closing;

    public DeviceSwitchOverlay()
    {
        InitializeComponent();

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            AnimateOut();
        };
    }

    public void ShowDevice(string deviceName, bool isOutput)
    {
        _closing = false;
        _dismissTimer.Stop();

        TypeLabel.Text = isOutput ? "OUTPUT DEVICE" : "INPUT DEVICE";
        DeviceIcon.Text = isOutput ? "\U0001F50A" : "\U0001F3A4"; // speaker or mic emoji
        DeviceName.Text = deviceName;

        PositionAboveTaskbar();
        Show();

        var fadeIn = (Storyboard)FindResource("FadeIn");
        fadeIn.Begin(this);

        _dismissTimer.Start();
    }

    private void AnimateOut()
    {
        if (_closing) return;
        _closing = true;

        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Completed += (_, _) => Hide();
        fadeOut.Begin(this);
    }

    private void PositionAboveTaskbar()
    {
        // Get the working area (excludes taskbar)
        var workArea = SystemParameters.WorkArea;

        // Measure desired size
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = DesiredSize.Width > 0 ? DesiredSize.Width : 320;
        var desiredHeight = DesiredSize.Height > 0 ? DesiredSize.Height : 80;

        // Position: bottom-right, just above the taskbar, 16px margin from edges
        Left = workArea.Right - desiredWidth - 16;
        Top = workArea.Bottom - desiredHeight - 16;
    }
}
