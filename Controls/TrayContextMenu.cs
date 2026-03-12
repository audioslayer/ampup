using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AmpUp.Controls;

public class TrayContextMenu : Window
{
    private readonly Action _onOpen;
    private readonly Action _onExit;
    private readonly AudioMixer _mixer;
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onSave;
    private readonly Action _onRefresh;

    private TextBlock _statusDot = null!;
    private TextBlock _statusText = null!;
    private StackPanel _assignExpandPanel = null!;
    private bool _assignExpanded;

    public TrayContextMenu(
        Action onOpen,
        Action onExit,
        AudioMixer mixer,
        AppConfig config,
        Action<AppConfig> onSave,
        Action onRefresh)
    {
        _onOpen = onOpen;
        _onExit = onExit;
        _mixer = mixer;
        _config = config;
        _onSave = onSave;
        _onRefresh = onRefresh;

        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 240;
        SizeToContent = SizeToContent.Height;

        Deactivated += (_, _) => Hide();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };

        Content = BuildContent();
    }

    public void ShowAt(double screenX, double screenY)
    {
        _assignExpanded = false;
        if (_assignExpandPanel != null)
            _assignExpandPanel.Visibility = Visibility.Collapsed;

        // Show and measure first
        Show();
        Measure(new Size(Width, double.PositiveInfinity));

        double h = DesiredSize.Height > 0 ? DesiredSize.Height : 200;
        double w = Width;

        // Position above cursor, right-aligned to cursor, but keep on screen
        var workArea = SystemParameters.WorkArea;
        double left = Math.Min(screenX - w, workArea.Right - w - 4);
        double top = Math.Max(screenY - h - 4, workArea.Top + 4);

        Left = left;
        Top = top;

        Activate();
    }

    public void UpdateStatus(bool connected, string? port)
    {
        if (_statusDot == null || _statusText == null) return;
        if (connected)
        {
            _statusDot.Text = "●";
            _statusDot.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0x77));
            _statusText.Text = port != null ? $"Connected ({port})" : "Connected";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        }
        else
        {
            _statusDot.Text = "○";
            _statusDot.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            _statusText.Text = "Disconnected";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        }
    }

    private UIElement BuildContent()
    {
        var accent = ThemeManager.Accent;

        var outer = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(242, 0x11, 0x11, 0x11)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 6,
                Opacity = 0.75,
                Direction = 270
            },
            Margin = new Thickness(10, 10, 10, 10)
        };

        var root = new StackPanel { Orientation = Orientation.Vertical };
        outer.Child = root;

        // --- Header ---
        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(14, 12, 14, 10)
        };

        // Icon + title row
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        // App icon
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon/ampup-16.png", UriKind.Absolute);
            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            var iconImg = new System.Windows.Controls.Image
            {
                Source = bitmapImage,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            titleRow.Children.Add(iconImg);
        }
        catch { }

        var titleText = new TextBlock
        {
            Text = "AMP UP",
            Foreground = new SolidColorBrush(accent),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(titleText);
        header.Children.Add(titleRow);

        // Connection status row
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        _statusDot = new TextBlock
        {
            Text = "○",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        _statusText = new TextBlock
        {
            Text = "Disconnected",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 9,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_statusText);
        header.Children.Add(statusRow);

        root.Children.Add(header);

        // --- Separator ---
        root.Children.Add(BuildSeparator(accent));

        // --- Menu items ---
        var menuArea = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(6, 4, 6, 6)
        };

        // Open Amp Up
        menuArea.Children.Add(BuildMenuItem(
            "▶  Open Amp Up",
            isBold: true,
            fontSize: 10,
            foreground: new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            onClick: () => { Hide(); _onOpen(); }
        ));

        // Assign Running Apps (expandable)
        var assignRow = BuildAssignRow(accent);
        menuArea.Children.Add(assignRow);

        // Separator before exit
        menuArea.Children.Add(BuildSeparator(accent));

        // Exit
        menuArea.Children.Add(BuildMenuItem(
            "Exit",
            isBold: false,
            fontSize: 9.5,
            foreground: new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
            onClick: () => { Hide(); _onExit(); }
        ));

        root.Children.Add(menuArea);

        return outer;
    }

    private Border BuildSeparator(Color accent)
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(51, accent.R, accent.G, accent.B)),
            Margin = new Thickness(16, 4, 16, 4)
        };
    }

    private FrameworkElement BuildMenuItem(string text, bool isBold, double fontSize, Brush foreground, Action onClick)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 7, 8, 7),
            Cursor = Cursors.Hand
        };

        var label = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Child = label;

        var accent = ThemeManager.Accent;
        row.MouseEnter += (_, _) =>
            row.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
        row.MouseLeave += (_, _) =>
            row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (_, _) => onClick();

        return row;
    }

    private FrameworkElement BuildAssignRow(Color accent)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical };

        // The clickable header row
        var headerRow = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 7, 8, 7),
            Cursor = Cursors.Hand
        };

        var headerPanel = new DockPanel { LastChildFill = true };
        var arrowLabel = new TextBlock
        {
            Text = "›",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0)
        };
        DockPanel.SetDock(arrowLabel, Dock.Right);
        headerPanel.Children.Add(arrowLabel);
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Assign Running Apps",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 9.5,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Child = headerPanel;

        headerRow.MouseEnter += (_, _) =>
            headerRow.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
        headerRow.MouseLeave += (_, _) =>
            headerRow.Background = Brushes.Transparent;

        // Expand panel (hidden by default)
        _assignExpandPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(4, 0, 4, 4)
        };

        headerRow.MouseLeftButtonDown += (_, _) =>
        {
            _assignExpanded = !_assignExpanded;
            if (_assignExpanded)
            {
                arrowLabel.Text = "⌄";
                arrowLabel.Margin = new Thickness(0, 2, 2, 0);
                PopulateAssignPanel();
                _assignExpandPanel.Visibility = Visibility.Visible;
            }
            else
            {
                arrowLabel.Text = "›";
                arrowLabel.Margin = new Thickness(0, -1, 0, 0);
                _assignExpandPanel.Visibility = Visibility.Collapsed;
            }
        };

        container.Children.Add(headerRow);
        container.Children.Add(_assignExpandPanel);
        return container;
    }

    private void PopulateAssignPanel()
    {
        _assignExpandPanel.Children.Clear();
        var accent = ThemeManager.Accent;

        List<string> apps;
        try { apps = _mixer.GetRunningAudioApps(); }
        catch { apps = new List<string>(); }

        if (apps.Count == 0)
        {
            _assignExpandPanel.Children.Add(new TextBlock
            {
                Text = "No audio apps running",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 9,
                FontFamily = new FontFamily("Segoe UI"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var appName in apps)
        {
            var appCapture = appName;

            var appRow = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 6, 5),
                Margin = new Thickness(0, 2, 0, 0)
            };

            var rowPanel = new DockPanel { LastChildFill = true };

            // Knob buttons on right
            var knobBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(knobBtns, Dock.Right);

            for (int k = 0; k < 5; k++)
            {
                int knobIdx = k;
                var knob = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
                string knobLabel = knob != null && !string.IsNullOrWhiteSpace(knob.Label)
                    ? (knob.Label.Length > 3 ? knob.Label[..3] : knob.Label)
                    : $"K{knobIdx + 1}";

                bool isAssigned = knob?.Target?.Equals(appCapture, StringComparison.OrdinalIgnoreCase) == true
                    || (knob?.Target == "apps" && (knob.Apps?.Contains(appCapture, StringComparer.OrdinalIgnoreCase) == true));

                var btn = new Border
                {
                    Width = 30,
                    Height = 22,
                    CornerRadius = new CornerRadius(3),
                    Background = isAssigned
                        ? new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B))
                        : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    BorderBrush = isAssigned
                        ? new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B))
                        : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = knobLabel,
                        Foreground = isAssigned
                            ? new SolidColorBrush(accent)
                            : new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                        FontSize = 8,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                btn.MouseEnter += (_, _) =>
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
                    btn.BorderBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
                };
                btn.MouseLeave += (_, _) =>
                {
                    // Recalculate assigned state after potential reassignment
                    var currentKnob = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
                    bool nowAssigned = currentKnob?.Target?.Equals(appCapture, StringComparison.OrdinalIgnoreCase) == true;
                    btn.Background = nowAssigned
                        ? new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B))
                        : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    btn.BorderBrush = nowAssigned
                        ? new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B))
                        : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
                };

                btn.MouseLeftButtonDown += (_, _) =>
                {
                    var cfg = _config.Knobs.FirstOrDefault(kn => kn.Idx == knobIdx);
                    if (cfg != null)
                    {
                        cfg.Target = appCapture;
                        cfg.Label = appCapture;
                        _onSave(_config);
                        _onRefresh();
                        Hide();
                    }
                };

                knobBtns.Children.Add(btn);
            }

            rowPanel.Children.Add(knobBtns);

            // App name label
            var display = TitleCase(appName);
            rowPanel.Children.Add(new TextBlock
            {
                Text = display,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                FontSize = 9.5,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            appRow.Child = rowPanel;
            _assignExpandPanel.Children.Add(appRow);
        }
    }

    private static string TitleCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }
}
