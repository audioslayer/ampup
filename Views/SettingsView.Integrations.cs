using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

// Integrations page: a sidebar of services (LIGHTING / APPS & SERVICES) on the left,
// and only the selected service's existing section Border on the right, under a
// header with its icon, description, status pill and Setup guide button.
public partial class SettingsView
{
    private enum IntegrationId { Govee, Corsair, SignalRgb, HomeAssistant, Obs, VoiceMeeter, Discord, Spotify }

    private sealed class IntegrationEntry
    {
        public IntegrationId Id;
        public string Name = "";
        public string Description = "";
        public MaterialIconKind Kind;
        public bool Lighting;
        public FrameworkElement Section = null!;
        public Border IconHost = null!;
        public Border? Row;
        public Border? RowIcon;
        public Ellipse? RowDot;
        public TextBlock? RowStatus;
    }

    private readonly List<IntegrationEntry> _integrations = new();

    // Survives tab switches and config reloads for the life of the view (the session).
    private IntegrationId _selectedIntegration = IntegrationId.Govee;

    private static readonly Color StatusConnected = Color.FromRgb(0x00, 0xE6, 0x76);
    private static readonly Color StatusOn = Color.FromRgb(0xFF, 0xB8, 0x00);
    private static readonly Color StatusOff = Color.FromRgb(0x55, 0x55, 0x55);

    private void InitIntegrationsPage()
    {
        void Add(IntegrationId id, string name, string description, MaterialIconKind kind, bool lighting,
                 FrameworkElement section, Border iconHost)
            => _integrations.Add(new IntegrationEntry
            {
                Id = id, Name = name, Description = description, Kind = kind, Lighting = lighting,
                Section = section, IconHost = iconHost,
            });

        Add(IntegrationId.Govee, "Govee", "Connect room lighting through LAN sync or the Cloud API.",
            MaterialIconKind.LightbulbOutline, true, GoveeIntegration, GoveeIconHost);
        Add(IntegrationId.Corsair, "Corsair iCUE", "Bring Corsair devices into your room lighting setup.",
            MaterialIconKind.Chip, true, CorsairIntegration, CorsairIconHost);
        Add(IntegrationId.SignalRgb, "SignalRGB", "Sync Turn Up lighting and link effects to your Amp Up profiles.",
            MaterialIconKind.PaletteSwatchOutline, true, SignalRgbIntegration, SignalRgbIconHost);
        Add(IntegrationId.HomeAssistant, "Home Assistant", "Connect your home controls and entities to Amp Up.",
            MaterialIconKind.HomeAssistant, false, HomeAssistantIntegration, HomeAssistantIconHost);
        Add(IntegrationId.Obs, "OBS Studio", "Control source audio and switch scenes from your controller.",
            MaterialIconKind.Video, false, ObsIntegration, ObsIconHost);
        Add(IntegrationId.VoiceMeeter, "VoiceMeeter", "Adjust strip and bus volume, and toggle mute.",
            MaterialIconKind.TuneVertical, false, VoiceMeeterIntegration, VoiceMeeterIconHost);
        Add(IntegrationId.Discord, "Discord", "Discord voice controls and connection status.",
            MaterialIconKind.Headset, false, DiscordIntegration, DiscordIconHost);
        Add(IntegrationId.Spotify, "Spotify", "Control playback, likes and shuffle, and show album art on LCD keys.",
            MaterialIconKind.Spotify, false, SpotifyIntegration, SpotifyIconHost);

        BuildIntegrationNav();

        // Keep the sidebar dots and the header pill in step with the existing status text.
        foreach (var sw in new[] { ChkGoveeEnabled, ChkGoveeCloudEnabled, ChkCorsairEnabled, ChkSignalRgbEnabled,
                                   ChkHaEnabled, ChkObsEnabled, ChkVmEnabled })
        {
            sw.Checked += (_, _) => RefreshIntegrationStatuses();
            sw.Unchecked += (_, _) => RefreshIntegrationStatuses();
        }
        var textDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        foreach (var text in new[] { TxtGoveeStatus, TxtGoveeCloudStatus, TxtCorsairStatus, TxtSignalRgbStatus,
                                     TxtHaStatusHeader, TxtObsStatusHeader, TxtVmStatusHeader, TxtDiscordStatus, TxtSpotifyStatus })
            textDescriptor.AddValueChanged(text, (_, _) => RefreshIntegrationStatuses());

        BtnIntegrationSetupGuide.Click += OnIntegrationSetupGuide;
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshIntegrationNavSelection);

        SelectIntegration(_selectedIntegration);
    }

    private void BuildIntegrationNav()
    {
        IntegrationNavList.Children.Clear();
        bool? lastGroup = null;
        foreach (var entry in _integrations)
        {
            if (lastGroup != entry.Lighting)
            {
                var header = new TextBlock
                {
                    Text = entry.Lighting ? "LIGHTING" : "APPS & SERVICES",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, lastGroup == null ? 0 : 14, 0, 6),
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
                IntegrationNavList.Children.Add(header);
                lastGroup = entry.Lighting;
            }
            IntegrationNavList.Children.Add(BuildIntegrationRow(entry));
        }
        RefreshIntegrationIcons();
        RefreshIntegrationStatuses();
    }

    private Border BuildIntegrationRow(IntegrationEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(names, 1);
        var title = new TextBlock
        {
            Text = entry.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        names.Children.Add(title);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        statusRow.Children.Add(dot);
        statusRow.Children.Add(status);
        names.Children.Add(statusRow);
        grid.Children.Add(names);

        var row = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.MouseEnter += (_, _) =>
        {
            if (entry.Id != _selectedIntegration)
                row.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        };
        row.MouseLeave += (_, _) =>
        {
            if (entry.Id != _selectedIntegration)
                row.Background = Brushes.Transparent;
        };
        row.MouseLeftButtonDown += (_, _) => SelectIntegration(entry.Id);

        entry.Row = row;
        entry.RowIcon = icon;
        entry.RowDot = dot;
        entry.RowStatus = status;
        return row;
    }

    private void SelectIntegration(IntegrationId id)
    {
        if (_integrations.Count == 0) return;
        var selected = _integrations.FirstOrDefault(i => i.Id == id) ?? _integrations[0];
        _selectedIntegration = selected.Id;

        foreach (var entry in _integrations)
            entry.Section.Visibility = entry == selected ? Visibility.Visible : Visibility.Collapsed;

        IntegrationDetailTitle.Text = selected.Name;
        IntegrationDetailDescription.Text = selected.Description;
        IntegrationDetailIconHost.Child = CloneIntegrationIcon(selected, IntegrationDetailIconHost, 52, 28, 14);

        RefreshIntegrationNavSelection();
        RefreshIntegrationStatuses();
    }

    /// <summary>
    /// Deep-link entry point: open Settings → Integrations on a service
    /// ("govee", "corsair", "signalrgb", "homeassistant", "obs", "voicemeeter", "discord", "spotify").
    /// Unknown keys just open the tab on the last selected service.
    /// </summary>
    public void ShowIntegration(string key)
    {
        SelectSettingsTab(IntegrationsTabIndex);
        if (Enum.TryParse<IntegrationId>(key, ignoreCase: true, out var id))
            SelectIntegration(id);
    }

    private void RefreshIntegrationNavSelection()
    {
        var accent = ThemeManager.Accent;
        foreach (var entry in _integrations)
        {
            if (entry.Row == null) continue;
            bool active = entry.Id == _selectedIntegration;
            // Fill only: selected = accent-tinted fill, no outline or glow.
            entry.Row.Background = active
                ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x30))
                : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Copies each section's icon tile (real app icon once ApplyIntegrationAppIcons
    /// has resolved it, otherwise the Phosphor glyph) into the sidebar and header.
    /// </summary>
    private void RefreshIntegrationIcons()
    {
        foreach (var entry in _integrations)
        {
            if (entry.RowIcon != null)
                entry.RowIcon.Child = CloneIntegrationIcon(entry, entry.RowIcon, 28, 16, 7);
        }
        var selected = _integrations.FirstOrDefault(i => i.Id == _selectedIntegration);
        if (selected != null)
            IntegrationDetailIconHost.Child = CloneIntegrationIcon(selected, IntegrationDetailIconHost, 52, 28, 14);
    }

    private static UIElement CloneIntegrationIcon(IntegrationEntry entry, Border target, double size, double glyph, double radius)
    {
        var source = entry.IconHost;
        if (source.Child is System.Windows.Controls.Image image && image.Source != null)
        {
            target.Background = Brushes.Transparent;
            target.BorderBrush = Brushes.Transparent;
            var copy = new System.Windows.Controls.Image
            {
                Source = image.Source,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius),
            };
            RenderOptions.SetBitmapScalingMode(copy, BitmapScalingMode.HighQuality);
            return copy;
        }

        // Glyph tile: Home Assistant's branded tile is a solid background; others use the accent tile.
        if (entry.Id == IntegrationId.HomeAssistant && source.Background is SolidColorBrush solid
            && source.Child is MaterialIcon brand)
        {
            target.Background = solid;
            target.BorderBrush = Brushes.Transparent;
            return new MaterialIcon
            {
                Kind = brand.Kind, Width = glyph, Height = glyph, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        target.SetResourceReference(Border.BackgroundProperty, "AccentBg30Brush");
        target.SetResourceReference(Border.BorderBrushProperty, "AccentBorder55Brush");
        target.BorderThickness = new Thickness(1);
        var icon = new MaterialIcon
        {
            Kind = entry.Kind, Width = glyph, Height = glyph,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        return icon;
    }

    /// <summary>One-word status: Connected / On / Off / Not set up, from the existing toggles and status text.</summary>
    private (string Word, Color Fill) GetIntegrationStatus(IntegrationId id)
    {
        static bool Connected(TextBlock t) => t.Text.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
        (string, Color) Toggle(bool enabled, bool connected) =>
            !enabled ? ("Off", StatusOff) : connected ? ("Connected", StatusConnected) : ("On", StatusOn);

        switch (id)
        {
            case IntegrationId.Govee:
                bool lan = ChkGoveeEnabled.IsChecked == true, cloud = ChkGoveeCloudEnabled.IsChecked == true;
                bool goveeLive = (lan && (_config?.Ambience.GoveeDevices.Count ?? 0) > 0) || (cloud && Connected(TxtGoveeCloudStatus));
                return Toggle(lan || cloud, goveeLive);
            case IntegrationId.Corsair:
                return Toggle(ChkCorsairEnabled.IsChecked == true, IsICueRunning());
            case IntegrationId.SignalRgb:
                return Toggle(ChkSignalRgbEnabled.IsChecked == true, TxtSignalRgbStatus.Text == "Receiving");
            case IntegrationId.HomeAssistant:
                return Toggle(ChkHaEnabled.IsChecked == true, Connected(TxtHaStatusHeader));
            case IntegrationId.Obs:
                return Toggle(ChkObsEnabled.IsChecked == true, Connected(TxtObsStatusHeader));
            case IntegrationId.VoiceMeeter:
                return Toggle(ChkVmEnabled.IsChecked == true, Connected(TxtVmStatusHeader));
            case IntegrationId.Discord:
                return Connected(TxtDiscordStatus) ? ("Connected", StatusConnected) : ("Not set up", StatusOff);
            case IntegrationId.Spotify:
                if (Connected(TxtSpotifyStatus)) return ("Connected", StatusConnected);
                return string.IsNullOrWhiteSpace(_config?.Spotify.ClientId) ? ("Not set up", StatusOff) : ("Off", StatusOff);
            default:
                return ("Off", StatusOff);
        }
    }

    /// <summary>True when the Corsair iCUE app is running (the SDK needs it).</summary>
    private static bool IsICueRunning()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("iCUE"))
            {
                p.Dispose();
                return true;
            }
        }
        catch
        {
            // Process list unavailable: report not running.
        }
        return false;
    }

    private void RefreshIntegrationStatuses()
    {
        foreach (var entry in _integrations)
        {
            var (word, color) = GetIntegrationStatus(entry.Id);
            if (entry.RowDot != null) entry.RowDot.Fill = new SolidColorBrush(color);
            if (entry.RowStatus != null) entry.RowStatus.Text = word;
            if (entry.Id == _selectedIntegration)
            {
                IntegrationDetailStatusDot.Fill = new SolidColorBrush(color);
                IntegrationDetailStatusText.Text = word;
            }
        }
    }

    private void OnIntegrationSetupGuide(object sender, RoutedEventArgs e)
    {
        switch (_selectedIntegration)
        {
            case IntegrationId.Govee: OnGoveeSetupGuide(sender, e); break;
            case IntegrationId.Discord: OnDiscordSetupGuide(sender, e); break;
            case IntegrationId.Spotify: OnSpotifySetupGuide(sender, e); break;
            case IntegrationId.HomeAssistant: OnHaSetupGuide(sender, e); break;
            case IntegrationId.Obs: OnObsSetupGuide(sender, e); break;
            case IntegrationId.VoiceMeeter: OnVmSetupGuide(sender, e); break;
            case IntegrationId.Corsair: OnCorsairSetupGuide(sender, e); break;
            case IntegrationId.SignalRgb: OnSignalRgbSetupGuide(sender, e); break;
        }
    }

    // Finish wiring follows Controls/SETUP_GUIDES.md: switch on first so the save has Enabled = true.

    private void OnHaSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new AmpUp.Controls.HomeAssistantSetupGuide { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
        ChkHaEnabled.IsChecked = true;
        TxtHaUrl.Text = guide.Url;
        TxtHaToken.Password = guide.Token;
        CollectAndSave();
        OnHaTest(this, e);
    }

    private void OnObsSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new AmpUp.Controls.ObsSetupGuide { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
        ChkObsEnabled.IsChecked = true;
        TxtObsHost.Text = guide.Host;
        TxtObsPort.Text = guide.Port.ToString();
        TxtObsPassword.Password = guide.Password;
        CollectAndSave();
        OnObsTest(this, e);
    }

    private void OnVmSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new AmpUp.Controls.VoiceMeeterSetupGuide { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
        ChkVmEnabled.IsChecked = true; // the Checked handler saves
    }

    private void OnCorsairSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new AmpUp.Controls.CorsairSetupGuide { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
        ChkCorsairEnabled.IsChecked = true; // OnCorsairEnabledChanged saves
    }

    private void OnSignalRgbSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new AmpUp.Controls.SignalRgbSetupGuide { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
        ChkSignalRgbEnabled.IsChecked = true; // OnSignalRgbEnabledChanged saves
        OnSignalRgbInstallPlugin(this, e);
    }
}
