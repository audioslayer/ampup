using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NAudio.CoreAudioApi;
using Wpf.Ui.Appearance;

namespace AmpUp.Controls;

public class TrayMixerPopup : Window
{
    private StackPanel _sessionList = null!;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<SessionRow> _rows = new();
    private MMDevice? _masterDevice;
    private Slider? _masterSlider;
    private TextBlock? _masterVolLabel;
    private readonly System.Windows.Threading.DispatcherTimer _pollTimer;
    private bool _updatingFromPoll; // prevent slider.ValueChanged feedback loop

    private record SessionRow(
        string ProcessName,
        AudioSessionControl Session,
        Slider VolumeSlider,
        TextBlock VolLabel,
        Button MuteBtn
    );

    public TrayMixerPopup()
    {
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 290;
        SizeToContent = SizeToContent.Height;

        Deactivated += (_, _) => Hide();

        _pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _pollTimer.Tick += PollVolumes;

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        // Outer border — drop shadow + rounded glass
        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(242, 0x0F, 0x0F, 0x0F)), // #F20F0F0F
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.7,
                Direction = 270
            },
            Margin = new Thickness(8, 8, 8, 8) // room for shadow
        };

        var root = new DockPanel();
        outer.Child = root;

        // Header
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Padding = new Thickness(14, 10, 14, 10)
        };
        DockPanel.SetDock(header, Dock.Top);

        var headerPanel = new DockPanel();
        var title = new TextBlock
        {
            Text = "AMP UP MIXER",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var closeBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 12,
            Padding = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => Hide();
        closeBtn.MouseEnter += (_, _) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerPanel.Children.Add(closeBtn);
        headerPanel.Children.Add(title);
        header.Child = headerPanel;
        root.Children.Add(header);

        // Divider
        var divider = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
        };
        DockPanel.SetDock(divider, Dock.Top);
        root.Children.Add(divider);

        // Device switcher section
        var deviceSection = BuildDeviceSwitcher();
        DockPanel.SetDock(deviceSection, Dock.Top);
        root.Children.Add(deviceSection);

        // Divider after devices
        var divider2 = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
        };
        DockPanel.SetDock(divider2, Dock.Top);
        root.Children.Add(divider2);

        // Scrollable session list
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 440,
            Style = BuildScrollViewerStyle()
        };
        DockPanel.SetDock(scroll, Dock.Top);

        _sessionList = new StackPanel { Orientation = Orientation.Vertical };
        scroll.Content = _sessionList;

        // Wrap scroll in padded container with bottom radius
        var wrapper = new Border
        {
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            Padding = new Thickness(0, 4, 0, 8),
            Child = scroll
        };
        DockPanel.SetDock(wrapper, Dock.Top);
        root.Children.Add(wrapper);

        return outer;
    }

    public void ShowPopup()
    {
        try
        {
            RefreshSessions();
            PositionNearTray();
            Show();
            Activate();
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.ShowPopup error: {ex.Message}");
        }
    }

    private new void Hide()
    {
        _pollTimer.Stop();
        base.Hide();
    }

    private void RefreshSessions()
    {
        _rows.Clear();
        _sessionList.Children.Clear();

        try
        {
            // Master volume row (always first)
            // Store in field so it lives as long as the popup; disposed in OnClosed
            _masterDevice?.Dispose();
            _masterDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            float masterVol = _masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool masterMuted = _masterDevice.AudioEndpointVolume.Mute;

            _sessionList.Children.Add(BuildMasterRow(_masterDevice, masterVol, masterMuted));

            // Divider after master
            _sessionList.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(10, 2, 10, 2)
            });

            // Per-app sessions
            var sessionMgr = _masterDevice.AudioSessionManager;
            var sessions = sessionMgr.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    var pid = (int)s.GetProcessID;
                    if (pid == 0) continue; // skip System Sounds

                    var proc = Process.GetProcessById(pid);
                    var name = proc.ProcessName;

                    var row = BuildSessionRow(name, s);
                    if (row != null)
                        _sessionList.Children.Add(row);
                }
                catch { }
            }

            if (_sessionList.Children.Count <= 2) // only master + divider
            {
                var empty = new TextBlock
                {
                    Text = "No active audio apps",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 8)
                };
                _sessionList.Children.Add(empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.RefreshSessions error: {ex.Message}");
            _sessionList.Children.Add(new TextBlock
            {
                Text = "Audio unavailable",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 8)
            });
        }
    }

    private void PollVolumes(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        _updatingFromPoll = true;
        try
        {
            // Update master slider
            if (_masterDevice != null && _masterSlider != null && _masterVolLabel != null)
            {
                try
                {
                    float vol = _masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                    int pct = (int)Math.Round(vol * 100);
                    _masterSlider.Value = pct;
                    _masterVolLabel.Text = $"{pct}%";
                }
                catch { }
            }

            // Update per-app sliders
            foreach (var row in _rows)
            {
                try
                {
                    float vol = row.Session.SimpleAudioVolume.Volume;
                    int pct = (int)Math.Round(vol * 100);
                    row.VolumeSlider.Value = pct;
                    row.VolLabel.Text = $"{pct}%";
                }
                catch { }
            }
        }
        finally
        {
            _updatingFromPoll = false;
        }
    }

    private UIElement BuildDeviceSwitcher()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(6, 6, 6, 4)
        };

        // Output device row
        panel.Children.Add(BuildDeviceRow("OUTPUT", DataFlow.Render));

        // Input device row
        panel.Children.Add(BuildDeviceRow("INPUT", DataFlow.Capture));

        return panel;
    }

    private UIElement BuildDeviceRow(string label, DataFlow flow)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Cursor = Cursors.Hand,
        };

        var dock = new DockPanel { LastChildFill = true };

        // Label (OUTPUT / INPUT)
        var typeLabel = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Width = 48,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(typeLabel, Dock.Left);
        dock.Children.Add(typeLabel);

        // Current device name
        string currentName = "Unknown";
        try
        {
            var role = flow == DataFlow.Capture ? Role.Communications : Role.Multimedia;
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, role);
            currentName = device.FriendlyName;
            // Shorten long names
            if (currentName.Length > 30)
                currentName = currentName[..28] + "…";
        }
        catch { }

        var nameLabel = new TextBlock
        {
            Text = currentName,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        dock.Children.Add(nameLabel);

        row.Child = dock;

        // Hover effect
        var accent = GetAccentColor();
        row.MouseEnter += (_, _) =>
            row.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
        row.MouseLeave += (_, _) =>
            row.Background = Brushes.Transparent;

        // Click to cycle to next device
        row.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
                if (devices.Count < 2) return; // nothing to cycle

                var role = flow == DataFlow.Capture ? Role.Communications : Role.Multimedia;
                using var current = _enumerator.GetDefaultAudioEndpoint(flow, role);
                string currentId = current.ID;

                // Find next device
                int currentIdx = -1;
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].ID == currentId) { currentIdx = i; break; }
                }
                int nextIdx = (currentIdx + 1) % devices.Count;
                var next = devices[nextIdx];

                ButtonHandler.SetDefaultAudioDevice(next.ID);
                nameLabel.Text = next.FriendlyName.Length > 30
                    ? next.FriendlyName[..28] + "…"
                    : next.FriendlyName;
            }
            catch (Exception ex)
            {
                Logger.Log($"TrayMixerPopup device switch error: {ex.Message}");
            }
        };

        return row;
    }

    private UIElement BuildMasterRow(MMDevice device, float vol, bool muted)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(6, 4, 6, 2),
            CornerRadius = new CornerRadius(6)
        };

        var panel = new DockPanel { LastChildFill = true };

        // Icon letter
        var icon = BuildLetterIcon("M", Color.FromRgb(0x00, 0xE6, 0x76));
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);

        // Mute button
        var muteBtn = BuildMuteButton(muted);
        DockPanel.SetDock(muteBtn, Dock.Right);

        // Vol% label
        var volLabel = new TextBlock
        {
            Text = $"{(int)Math.Round(vol * 100)}%",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            Width = 34,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0)
        };
        DockPanel.SetDock(volLabel, Dock.Right);

        panel.Children.Add(muteBtn);
        panel.Children.Add(volLabel);

        // Center: label + slider
        var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
        var label = new TextBlock
        {
            Text = "MASTER",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };

        var slider = BuildVolumeSlider(vol * 100);
        _masterSlider = slider;
        _masterVolLabel = volLabel;
        slider.ValueChanged += (_, e) =>
        {
            if (_updatingFromPoll) return;
            try
            {
                float newVol = (float)(e.NewValue / 100.0);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = newVol;
                volLabel.Text = $"{(int)Math.Round(e.NewValue)}%";
            }
            catch { }
        };

        muteBtn.Click += (_, _) =>
        {
            try
            {
                device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
                bool m = device.AudioEndpointVolume.Mute;
                UpdateMuteButton(muteBtn, m);
            }
            catch { }
        };

        center.Children.Add(label);
        center.Children.Add(slider);
        panel.Children.Add(center);
        row.Child = panel;
        return row;
    }

    private UIElement? BuildSessionRow(string processName, AudioSessionControl session)
    {
        try
        {
            float vol = session.SimpleAudioVolume.Volume;
            bool muted = session.SimpleAudioVolume.Mute;

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(6)
            };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var panel = new DockPanel { LastChildFill = true };

            // Icon letter
            var firstChar = processName.Length > 0 ? char.ToUpperInvariant(processName[0]).ToString() : "?";
            var iconColor = GetAppColor(processName);
            var icon = BuildLetterIcon(firstChar, iconColor);
            DockPanel.SetDock(icon, Dock.Left);
            panel.Children.Add(icon);

            // Mute button
            var muteBtn = BuildMuteButton(muted);
            DockPanel.SetDock(muteBtn, Dock.Right);

            // Vol% label
            var volLabel = new TextBlock
            {
                Text = $"{(int)Math.Round(vol * 100)}%",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 11,
                Width = 34,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            DockPanel.SetDock(volLabel, Dock.Right);

            panel.Children.Add(muteBtn);
            panel.Children.Add(volLabel);

            // Center: name + slider
            var center = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
            var nameLabel = new TextBlock
            {
                Text = TitleCase(processName),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };

            var slider = BuildVolumeSlider(vol * 100);
            _rows.Add(new SessionRow(processName, session, slider, volLabel, muteBtn));
            slider.ValueChanged += (_, e) =>
            {
                if (_updatingFromPoll) return;
                try
                {
                    session.SimpleAudioVolume.Volume = (float)(e.NewValue / 100.0);
                    volLabel.Text = $"{(int)Math.Round(e.NewValue)}%";
                }
                catch { }
            };

            muteBtn.Click += (_, _) =>
            {
                try
                {
                    session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
                    UpdateMuteButton(muteBtn, session.SimpleAudioVolume.Mute);
                }
                catch { }
            };

            center.Children.Add(nameLabel);
            center.Children.Add(slider);
            panel.Children.Add(center);
            row.Child = panel;
            return row;
        }
        catch
        {
            return null;
        }
    }

    private static Border BuildLetterIcon(string letter, Color bg)
    {
        return new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(60, bg.R, bg.G, bg.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, bg.R, bg.G, bg.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0),
            Child = new TextBlock
            {
                Text = letter,
                Foreground = new SolidColorBrush(bg),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Button BuildMuteButton(bool muted)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            ToolTip = muted ? "Unmute" : "Mute"
        };
        UpdateMuteButton(btn, muted);
        return btn;
    }

    private static void UpdateMuteButton(Button btn, bool muted)
    {
        btn.Content = new TextBlock
        {
            Text = muted ? "🔇" : "🔊",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = muted ? 0.5 : 1.0
        };
        btn.ToolTip = muted ? "Unmute" : "Mute";
    }

    private Slider BuildVolumeSlider(double value)
    {
        var accentColor = GetAccentColor();

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(value, 0, 100),
            Height = 16,
            Margin = new Thickness(0, 3, 0, 0),
            Style = BuildSliderStyle(accentColor)
        };
        return slider;
    }

    private static Color GetAccentColor()
    {
        try
        {
            var accent = ApplicationAccentColorManager.SystemAccent;
            return Color.FromRgb(accent.R, accent.G, accent.B);
        }
        catch
        {
            return Color.FromRgb(0x00, 0xE6, 0x76); // fallback green
        }
    }

    private static Style BuildSliderStyle(Color accent)
    {
        // Use XAML string to build the slider template — FrameworkElementFactory
        // can't set Track.Thumb/DecreaseRepeatButton/IncreaseRepeatButton as properties.
        var accentHex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        var xaml = $@"
<ControlTemplate TargetType='Slider'
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Grid>
        <Border Height='4' CornerRadius='2' Background='#363636' VerticalAlignment='Center' />
        <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton>
                <RepeatButton Opacity='0' IsHitTestVisible='False' />
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton Opacity='0' IsHitTestVisible='False' />
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb Width='12' Height='12' Cursor='Hand'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Border Background='White' CornerRadius='6' Width='12' Height='12' />
                        </ControlTemplate>
                    </Thumb.Template>
                </Thumb>
            </Track.Thumb>
        </Track>
    </Grid>
</ControlTemplate>";

        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        var style = new Style(typeof(Slider));
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    private static Style BuildScrollViewerStyle()
    {
        // Return null to use default; custom scroll styling is complex in code-behind
        // The slim scrollbar from Theme.xaml will apply if merged in App.xaml
        return new Style(typeof(ScrollViewer));
    }

    private void PositionNearTray()
    {
        // Use the monitor where the cursor is (near the tray icon)
        var cursorPos = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        var workArea = screen.WorkingArea;
        // Measure the window to get actual height
        Measure(new Size(Width, double.PositiveInfinity));
        double height = DesiredSize.Height > 0 ? DesiredSize.Height : 400;

        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - height - 8;
    }

    private static Color GetAppColor(string processName)
    {
        var lower = processName.ToLowerInvariant();
        return lower switch
        {
            var n when n.Contains("spotify") => Color.FromRgb(0x1D, 0xB9, 0x54),
            var n when n.Contains("discord") => Color.FromRgb(0x58, 0x65, 0xF2),
            var n when n.Contains("chrome") => Color.FromRgb(0xFF, 0xA0, 0x00),
            var n when n.Contains("firefox") => Color.FromRgb(0xFF, 0x67, 0x11),
            var n when n.Contains("vlc") => Color.FromRgb(0xFF, 0x87, 0x00),
            var n when n.Contains("steam") => Color.FromRgb(0x17, 0x1A, 0x21),
            var n when n.Contains("foobar") => Color.FromRgb(0x00, 0x9A, 0xFF),
            var n when n.Contains("msedge") || n.Contains("edge") => Color.FromRgb(0x00, 0x78, 0xD4),
            var n when n.Contains("teams") => Color.FromRgb(0x46, 0x4E, 0xB8),
            var n when n.Contains("zoom") => Color.FromRgb(0x22, 0x8B, 0xFF),
            _ => Color.FromRgb(0x00, 0xE6, 0x76) // default accent green
        };
    }

    private static string TitleCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _masterDevice?.Dispose(); } catch { }
        try { _enumerator.Dispose(); } catch { }
    }
}
