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
        root.Children.Add(scroll);

        // Wrap in padded container with bottom radius
        var wrapper = new Border
        {
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            Padding = new Thickness(0, 4, 0, 8),
            Child = scroll
        };
        DockPanel.SetDock(wrapper, Dock.Top);
        // Replace scroll in root with wrapper
        root.Children.Remove(scroll);
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
        }
        catch (Exception ex)
        {
            Logger.Log($"TrayMixerPopup.ShowPopup error: {ex.Message}");
        }
    }

    private void RefreshSessions()
    {
        _rows.Clear();
        _sessionList.Children.Clear();

        try
        {
            // Master volume row (always first)
            var masterDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            float masterVol = masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool masterMuted = masterDevice.AudioEndpointVolume.Mute;

            _sessionList.Children.Add(BuildMasterRow(masterDevice, masterVol, masterMuted));

            // Divider after master
            _sessionList.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(10, 2, 10, 2)
            });

            // Per-app sessions
            var sessionMgr = masterDevice.AudioSessionManager;
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
        slider.ValueChanged += (_, e) =>
        {
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
            slider.ValueChanged += (_, e) =>
            {
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
        var style = new Style(typeof(Slider));

        var template = new ControlTemplate(typeof(Slider));

        // Track background
        var trackBg = new FrameworkElementFactory(typeof(Border));
        trackBg.SetValue(Border.HeightProperty, 4.0);
        trackBg.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        trackBg.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)));
        trackBg.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);

        // Selection range (filled portion)
        var selectionFill = new FrameworkElementFactory(typeof(Border));
        selectionFill.SetValue(Border.HeightProperty, 4.0);
        selectionFill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        selectionFill.SetValue(Border.BackgroundProperty, new SolidColorBrush(accent));
        selectionFill.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
        selectionFill.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        // Thumb
        var thumb = new FrameworkElementFactory(typeof(Thumb));
        thumb.SetValue(Thumb.WidthProperty, 12.0);
        thumb.SetValue(Thumb.HeightProperty, 12.0);
        thumb.SetValue(Thumb.CursorProperty, Cursors.Hand);

        var thumbBorder = new FrameworkElementFactory(typeof(Border));
        thumbBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Colors.White));
        thumbBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        thumbBorder.SetValue(Border.WidthProperty, 12.0);
        thumbBorder.SetValue(Border.HeightProperty, 12.0);

        var thumbTemplate = new ControlTemplate(typeof(Thumb));
        thumbTemplate.VisualTree = thumbBorder;
        thumb.SetValue(Thumb.TemplateProperty, thumbTemplate);

        // Track
        var track = new FrameworkElementFactory(typeof(Track));
        track.SetValue(Track.NameProperty, "PART_Track");
        track.AppendChild(thumb);

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(trackBg);
        grid.AppendChild(track);

        template.VisualTree = grid;
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
        var workArea = SystemParameters.WorkArea;
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
        try { _enumerator.Dispose(); } catch { }
    }
}
