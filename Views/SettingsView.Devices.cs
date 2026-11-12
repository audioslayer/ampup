using System.Windows;
using System.Windows.Controls;
using AmpUp.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AmpUp.Core.Models;
using Material.Icons;

namespace AmpUp.Views;

// Devices (Hardware) settings page: compact mode chooser, then a side list of devices with the
// selected device's open detail on the right (OpenCanvas.MasterDetail).
// SegHardwareMode (hidden) stays the source of truth; the mode options just drive and mirror it.
public partial class SettingsView
{
    private readonly System.Windows.Shapes.Ellipse _devListDotTurnUp = NewDeviceListDot();
    private readonly System.Windows.Shapes.Ellipse _devListDotStreamController = NewDeviceListDot();

    private static System.Windows.Shapes.Ellipse NewDeviceListDot() => new() { Width = 7, Height = 7 };

    /// <summary>Moves the two device detail Borders out of DeviceCardGrid into a side list + open detail.</summary>
    private void BuildDeviceMasterDetail()
    {
        if (DeviceCardGrid.Parent is not Panel host) return;
        DeviceCardGrid.Children.Remove(DevCardTurnUp);
        DeviceCardGrid.Children.Remove(DevCardStreamController);
        DeviceCardGrid.Visibility = Visibility.Collapsed;

        var md = new OpenCanvas.MasterDetail();
        md.AddItem("turnup", "Turn Up", MaterialIconKind.Knob, _devListDotTurnUp);
        md.AddItem("n3", "Stream Controller", MaterialIconKind.ViewGridOutline, _devListDotStreamController);
        md.SelectionChanged += key => md.ContentHost.Child = key == "n3" ? DevCardStreamController : DevCardTurnUp;
        md.ContentHost.Child = md.SelectedKey == "n3" ? DevCardStreamController : DevCardTurnUp;
        host.Children.Insert(host.Children.IndexOf(DeviceCardGrid) + 1, md);
    }

    private void InitDevicesPage()
    {
        BuildDeviceMasterDetail();
        LoadDeviceImage("turnup", ImgTurnUp, ArtTurnUp);
        LoadDeviceImage("n3", ImgStreamController, ArtStreamController);
        BtnBuyN3.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.amazon.com/dp/B0FM3NP9ZB") { UseShellExecute = true }); }
            catch { }
        };

        ConnectionCard.SizeChanged += (_, e) =>
        {
            double w = e.NewSize.Width;
            ModeCardGrid.Columns = w < 640 ? 2 : 4;
        };
        // BeginInvoke: OpenCanvas drops its cached accent brushes in its own OnAccentChanged handler first.
        ThemeManager.OnAccentChanged += () => Dispatcher.BeginInvoke(RefreshDevicesPage);
        ThemeManager.OnReduceEffectsChanged += () => Dispatcher.Invoke(RefreshDevicesPage);
        RefreshDevicesPage();
    }

    /// <summary>
    /// Shows Assets/devices/&lt;deviceId&gt;.png (WPF Resource) when it was bundled;
    /// otherwise keeps the drawn placeholder visible.
    /// </summary>
    private static void LoadDeviceImage(string deviceId, System.Windows.Controls.Image target, FrameworkElement placeholder)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/devices/{deviceId}.png", UriKind.Absolute);
            if (Application.GetResourceStream(uri) is not { } info) return;
            info.Stream.Dispose();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 800;
            bmp.EndInit();
            bmp.Freeze();

            target.Source = bmp;
            target.Visibility = Visibility.Visible;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Missing or unreadable image: placeholder art stays.
        }
    }

    private void ModeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out int idx)) return;
        SegHardwareMode.SelectAndNotify(idx);
        RefreshDevicesPage();
    }

    private void RefreshDevicesPage()
    {
        int sel = SegHardwareMode.SelectedIndex;
        var cards = new (Border Card, FrameworkElement Check)[]
        {
            (ModeCardAuto, ModeCheckAuto),
            (ModeCardTurnUp, ModeCheckTurnUp),
            (ModeCardStreamController, ModeCheckStreamController),
            (ModeCardBoth, ModeCheckBoth),
        };
        for (int i = 0; i < cards.Length; i++)
        {
            bool on = i == sel;
            var (card, check) = cards[i];
            // Open option: cached accent fill/border on the selected one only, no glow.
            card.Background = on ? OpenCanvas.SelectedFill : Brushes.Transparent;
            card.BorderBrush = on ? OpenCanvas.SelectedBorder : Brushes.Transparent;
            check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        var mode = SegHardwareMode.SelectedTag as HardwareMode? ?? HardwareMode.Auto;
        bool turnUpAllowed = mode != HardwareMode.StreamControllerOnly;
        bool scAllowed = mode != HardwareMode.TurnUpOnly;
        StyleDeviceCard(DevCardTurnUp, _turnUpConnected, turnUpAllowed);
        StyleDeviceCard(DevCardStreamController, _streamControllerConnected, scAllowed);
        StyleDeviceListDot(_devListDotTurnUp, _turnUpConnected, turnUpAllowed);
        StyleDeviceListDot(_devListDotStreamController, _streamControllerConnected, scAllowed);

        // "Drives tabs" pills mirror the hidden SegActiveSurface (0 TurnUp, 1 StreamController, 2 Both).
        bool dual = mode == HardwareMode.DualMode;
        int surf = SegActiveSurface.SelectedIndex;
        StyleDrivesPill(DrivesPillTurnUp, DrivesIconTurnUp, DrivesTextTurnUp, dual, surf is 0 or 2);
        StyleDrivesPill(DrivesPillStreamController, DrivesIconStreamController, DrivesTextStreamController, dual, surf is 1 or 2);

        string modeName = mode switch
        {
            HardwareMode.TurnUpOnly => "Turn Up only",
            HardwareMode.StreamControllerOnly => "Stream Controller only",
            HardwareMode.DualMode => "Both controllers",
            _ => "Auto",
        };
        int expected = (turnUpAllowed ? 1 : 0) + (scAllowed ? 1 : 0);
        int connected = (turnUpAllowed && _turnUpConnected ? 1 : 0) + (scAllowed && _streamControllerConnected ? 1 : 0);
        TxtHwSummary.Text = $"Mode: {modeName}";
        TxtHeroStatus.Text = connected == 0 ? "No devices connected"
            : connected == expected ? (connected == 1 ? "Connected" : "All connected")
            : $"{connected} of {expected} connected";
        HeroStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            connected == 0 ? "DangerRedBrush" : connected == expected ? "SuccessGrnBrush" : "WarnYellowBrush");
    }

    private void DrivesPill_Click(object sender, MouseButtonEventArgs e)
    {
        int surf = SegActiveSurface.SelectedIndex;
        bool tu = surf is 0 or 2, sc = surf is 1 or 2;
        if (sender == DrivesPillTurnUp) tu = !tu;
        else if (sender == DrivesPillStreamController) sc = !sc;
        else return;
        if (!tu && !sc) return; // the last remaining surface can't be switched off
        SegActiveSurface.SelectAndNotify(tu && sc ? 2 : tu ? 0 : 1);
        RefreshDevicesPage();
    }

    private static void StyleDrivesPill(Border pill, Material.Icons.WPF.MaterialIcon icon, TextBlock text, bool visible, bool on)
    {
        pill.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        pill.SetResourceReference(Border.BackgroundProperty, on ? "AccentBg30Brush" : "InputBgBrush");
        pill.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBorder55Brush" : "CardBorderBrush");
        icon.Kind = on ? Material.Icons.MaterialIconKind.CheckCircle : Material.Icons.MaterialIconKind.CircleOutline;
        icon.SetResourceReference(Material.Icons.WPF.MaterialIcon.ForegroundProperty, on ? "AccentBrush" : "TextDimBrush");
        text.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextPrimaryBrush" : "TextSecBrush");
    }

    private static void StyleDeviceListDot(System.Windows.Shapes.Ellipse dot, bool connected, bool allowed)
    {
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            !allowed ? "TextDimBrush" : connected ? "SuccessGrnBrush" : "DangerRedBrush");
        dot.ToolTip = !allowed ? "Not used in the current hardware mode" : connected ? "Connected" : "Not connected";
    }

    private static void StyleDeviceCard(Border card, bool connected, bool allowed)
    {
        // Open detail pane: no border or glow; dim it when the hardware mode doesn't use this device.
        card.Opacity = allowed ? 1.0 : 0.5;
        card.ToolTip = allowed ? null : "Not used in the current hardware mode";
    }
}
