using System.Windows;
using System.Windows.Controls;
using AmpUp.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AmpUp.Core.Models;

namespace AmpUp.Views;

// Devices (Hardware) settings page: mode chooser cards, device cards with photo slots.
// SegHardwareMode (hidden) stays the source of truth; the cards just drive and mirror it.
public partial class SettingsView
{
    private void InitDevicesPage()
    {
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
            DeviceCardGrid.Columns = w < 780 ? 1 : 2;
            ModeCardGrid.Columns = w < 640 ? 2 : 4;
        };
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshDevicesPage);
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
            card.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "CardBorderBrush");
            card.SetResourceReference(Border.BackgroundProperty, on ? "AccentBg30Brush" : "BgDarkBrush");
            // Only the single selected mode card glows (GlassStyle: off with Reduce effects).
            card.Effect = on ? GlassStyle.GlowEffect(ThemeManager.Accent, 22, 0.45) : null;
            check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        var mode = SegHardwareMode.SelectedTag as HardwareMode? ?? HardwareMode.Auto;
        bool turnUpAllowed = mode != HardwareMode.StreamControllerOnly;
        bool scAllowed = mode != HardwareMode.TurnUpOnly;
        StyleDeviceCard(DevCardTurnUp, _turnUpConnected, turnUpAllowed);
        StyleDeviceCard(DevCardStreamController, _streamControllerConnected, scAllowed);

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

    private static void StyleDeviceCard(Border card, bool connected, bool allowed)
    {
        bool lit = connected && allowed;
        // Several device cards can be lit at once: brighter accent border, no shadow effect.
        card.SetResourceReference(Border.BorderBrushProperty, lit ? "AccentLabel88Brush" : "CardBorderBrush");
        card.Effect = null;
        card.Opacity = allowed ? 1.0 : 0.5;
        card.ToolTip = allowed ? null : "Not used in the current hardware mode";
    }
}
