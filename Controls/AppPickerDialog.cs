using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Modal app picker dialog — shows running apps + common presets.
/// Returns the selected app's launch path (exe + args if needed).
/// </summary>
public class AppPickerDialog : Window
{
    private string? _selectedPath;
    private TextBox? _searchBox;
    private StackPanel? _resultsPanel;
    private readonly List<AppEntry> _allEntries = new();

    // Common apps with their typical install paths
    private static readonly (string Name, string Path, MaterialIconKind Icon)[] CommonApps =
    {
        ("Discord", @"%LocalAppData%\Discord\Update.exe --processStart Discord.exe", MaterialIconKind.Chat),
        ("Spotify", @"%AppData%\Spotify\Spotify.exe", MaterialIconKind.Music),
        ("Steam", @"C:\Program Files (x86)\Steam\steam.exe", MaterialIconKind.Steam),
        ("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", MaterialIconKind.GoogleChrome),
        ("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", MaterialIconKind.Firefox),
        ("Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", MaterialIconKind.MicrosoftEdge),
        ("OBS Studio", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", MaterialIconKind.Video),
        ("VLC", @"C:\Program Files\VideoLAN\VLC\vlc.exe", MaterialIconKind.Play),
        ("Notepad++", @"C:\Program Files\Notepad++\notepad++.exe", MaterialIconKind.NoteEdit),
        ("VS Code", @"%LocalAppData%\Programs\Microsoft VS Code\Code.exe", MaterialIconKind.MicrosoftVisualStudioCode),
        ("File Explorer", @"explorer.exe", MaterialIconKind.Folder),
        ("Task Manager", @"taskmgr.exe", MaterialIconKind.ChartBar),
        ("Calculator", @"calc.exe", MaterialIconKind.Calculator),
        ("Slack", @"%LocalAppData%\slack\slack.exe", MaterialIconKind.Slack),
        ("Teams", @"%LocalAppData%\Microsoft\Teams\Update.exe --processStart ms-teams.exe", MaterialIconKind.MicrosoftTeams),
    };

    private record AppEntry(string Name, string Path, ImageSource? Icon, bool IsRunning, MaterialIconKind? MaterialIcon = null);

    public string? SelectedPath => _selectedPath;

    public AppPickerDialog()
    {
        Title = "Pick an App";
        Width = 400;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x14));

        BuildUI();
        LoadApps();
    }

    private void BuildUI()
    {
        var root = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
        };

        var mainPanel = new StackPanel { Margin = new Thickness(16) };

        // Header row with title + close button
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = new TextBlock
        {
            Text = "PICK AN APP",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var closeBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 16,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeBtn.MouseLeftButtonDown += (_, _) => { DialogResult = false; Close(); };
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerRow.Children.Add(closeBtn);
        headerRow.Children.Add(title);
        mainPanel.Children.Add(headerRow);

        // Search box
        var searchBorder = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 10),
        };
        _searchBox = new TextBox
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
            FontSize = 12,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Placeholder
        var placeholder = new TextBlock
        {
            Text = "Search apps...",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 12,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        var searchGrid = new Grid();
        searchGrid.Children.Add(_searchBox);
        searchGrid.Children.Add(placeholder);
        _searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            FilterApps(_searchBox.Text);
        };
        searchBorder.Child = searchGrid;
        mainPanel.Children.Add(searchBorder);

        // Scrollable results
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
        };
        _resultsPanel = new StackPanel();
        scroll.Content = _resultsPanel;
        mainPanel.Children.Add(scroll);

        root.Child = mainPanel;
        Content = root;

        // Drag to move
        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

        // Escape to close
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }

    private void LoadApps()
    {
        _allEntries.Clear();

        // Get running processes with their exe paths
        var runningApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                var name = proc.ProcessName;
                if (string.Equals(name, "AmpUp", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase)) continue;
                if (runningApps.ContainsKey(name)) continue;

                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; } catch { }
                runningApps[name] = exePath ?? name;
            }
            catch { }
        }

        // Add running apps
        foreach (var (name, path) in runningApps.OrderBy(kv => kv.Key))
        {
            ImageSource? icon = null;
            try
            {
                if (File.Exists(path))
                {
                    var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (sysIcon != null)
                        icon = Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }

            _allEntries.Add(new AppEntry(name, path, icon, true));
        }

        // Add common apps (only if they exist and aren't already in running list)
        foreach (var (name, rawPath, matIcon) in CommonApps)
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
            var exePath = expandedPath.Contains(' ') ? expandedPath.Split(' ', 2)[0] : expandedPath;

            // Skip if already shown as running
            if (runningApps.Keys.Any(k => k.Contains(name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                continue;

            if (File.Exists(exePath) || exePath == "explorer.exe" || exePath == "taskmgr.exe" || exePath == "calc.exe")
            {
                _allEntries.Add(new AppEntry(name, rawPath, null, false, matIcon));
            }
        }

        RenderEntries(_allEntries);
    }

    private void FilterApps(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RenderEntries(_allEntries);
            return;
        }

        var filtered = _allEntries
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || e.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        RenderEntries(filtered);
    }

    private void RenderEntries(List<AppEntry> entries)
    {
        if (_resultsPanel == null) return;
        _resultsPanel.Children.Clear();

        var runningApps = entries.Where(e => e.IsRunning).ToList();
        var commonApps = entries.Where(e => !e.IsRunning).ToList();

        if (runningApps.Count > 0)
        {
            _resultsPanel.Children.Add(MakeCategoryHeader("RUNNING"));
            foreach (var entry in runningApps)
                _resultsPanel.Children.Add(MakeAppRow(entry));
        }

        if (commonApps.Count > 0)
        {
            _resultsPanel.Children.Add(MakeCategoryHeader("COMMON APPS"));
            foreach (var entry in commonApps)
                _resultsPanel.Children.Add(MakeAppRow(entry));
        }

        if (entries.Count == 0)
        {
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = "No apps found",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 12,
                Margin = new Thickness(4, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    private static TextBlock MakeCategoryHeader(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
        Margin = new Thickness(4, 10, 0, 4),
    };

    private Border MakeAppRow(AppEntry entry)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
        };

        var content = new DockPanel();

        // Icon (extracted exe icon or material icon)
        FrameworkElement iconElement;
        if (entry.Icon != null)
        {
            iconElement = new System.Windows.Controls.Image
            {
                Source = entry.Icon,
                Width = 24, Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
        }
        else if (entry.MaterialIcon.HasValue)
        {
            iconElement = new MaterialIcon
            {
                Kind = entry.MaterialIcon.Value,
                Width = 24, Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeManager.Accent),
                Margin = new Thickness(0, 0, 10, 0),
            };
        }
        else
        {
            iconElement = new MaterialIcon
            {
                Kind = MaterialIconKind.Application,
                Width = 24, Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                Margin = new Thickness(0, 0, 10, 0),
            };
        }
        DockPanel.SetDock(iconElement, Dock.Left);
        content.Children.Add(iconElement);

        // Name + path
        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
        });

        // Show abbreviated path
        var displayPath = entry.Path.Replace(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"%LocalAppData%")
                                    .Replace(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"%AppData%");
        textPanel.Children.Add(new TextBlock
        {
            Text = displayPath,
            FontSize = 9,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(textPanel);

        row.Child = content;

        // Hover effect
        row.MouseEnter += (_, _) =>
        {
            row.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30,
                ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C));
        };

        // Click to select
        row.MouseLeftButtonDown += (_, _) =>
        {
            // Use unexpanded path with env vars for portability
            _selectedPath = entry.Path;
            DialogResult = true;
            Close();
        };

        return row;
    }
}
