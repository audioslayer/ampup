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
/// Returns the selected app's launch path plus its process name.
/// </summary>
public class AppPickerDialog : Window
{
    private string? _selectedPath;
    private string? _selectedProcessName;
    private string? _selectedName;
    private TextBox? _searchBox;
    private StackPanel? _resultsPanel;
    private readonly List<AppEntry> _allEntries = new();

    // Common apps with their typical install paths
    internal static readonly (string Name, string Path, string ProcessName, MaterialIconKind Icon)[] CommonApps =
    {
        ("Discord", @"%LocalAppData%\Discord\Update.exe --processStart Discord.exe", "Discord", MaterialIconKind.Chat),
        ("Spotify", @"%AppData%\Spotify\Spotify.exe", "Spotify", MaterialIconKind.Music),
        ("Steam", @"C:\Program Files (x86)\Steam\steam.exe", "steam", MaterialIconKind.Steam),
        ("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome", MaterialIconKind.GoogleChrome),
        ("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox", MaterialIconKind.Firefox),
        ("Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "msedge", MaterialIconKind.MicrosoftEdge),
        ("OBS Studio", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "obs64", MaterialIconKind.Video),
        ("VLC", @"C:\Program Files\VideoLAN\VLC\vlc.exe", "vlc", MaterialIconKind.Play),
        ("Notepad++", @"C:\Program Files\Notepad++\notepad++.exe", "notepad++", MaterialIconKind.NoteEdit),
        ("VS Code", @"%LocalAppData%\Programs\Microsoft VS Code\Code.exe", "Code", MaterialIconKind.MicrosoftVisualStudioCode),
        ("File Explorer", @"explorer.exe", "explorer", MaterialIconKind.Folder),
        ("Task Manager", @"taskmgr.exe", "Taskmgr", MaterialIconKind.ChartBar),
        ("Calculator", @"calc.exe", "CalculatorApp", MaterialIconKind.Calculator),
        ("Slack", @"%LocalAppData%\slack\slack.exe", "slack", MaterialIconKind.Slack),
        ("Teams", @"%LocalAppData%\Microsoft\Teams\Update.exe --processStart ms-teams.exe", "ms-teams", MaterialIconKind.MicrosoftTeams),
    };

    private record AppEntry(string Name, string Path, string ProcessName, ImageSource? Icon, bool IsRunning, MaterialIconKind? MaterialIcon = null);

    public string? SelectedPath => _selectedPath;
    public string? SelectedProcessName => _selectedProcessName;
    public string? SelectedName => _selectedName;

    public AppPickerDialog()
    {
        Title = "Pick an App";
        Width = 400;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgDarkBrush");

        BuildUI();
        LoadApps();
    }

    private void BuildUI()
    {
        var root = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgDarkBrush"),
            BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
        };

        var mainPanel = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };

        // Header: glowing accent icon tile + title / subtitle, close on the right (same chrome as GroupNameDialog)
        var accent = ThemeManager.Accent;
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 16), Background = System.Windows.Media.Brushes.Transparent };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = UiKit.IconTile(accent, MaterialIconKind.Application, 38);
        tile.Margin = new Thickness(0, 0, 12, 0);
        tile.Effect = GlassStyle.GlowEffect(accent, 18, 0.35);
        headerRow.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "Pick an App", FontSize = 15, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var subtitle = new TextBlock { Text = "Running apps and common presets", FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(title);
        titles.Children.Add(subtitle);
        Grid.SetColumn(titles, 1);
        headerRow.Children.Add(titles);
        var closeBtn = UiKit.IconButton(MaterialIconKind.Close, "Cancel (Esc)", _ =>
        {
            DialogResult = false;
            Close();
        }, size: 26);
        closeBtn.VerticalAlignment = VerticalAlignment.Top;
        // Keep the window-level drag handler from swallowing the click
        closeBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Grid.SetColumn(closeBtn, 2);
        headerRow.Children.Add(closeBtn);
        mainPanel.Children.Add(headerRow);

        // Search box (icon + text, accent border while focused)
        var searchBorder = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("InputBgBrush"),
            BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 2, 12, 2),
            Margin = new Thickness(0, 0, 0, 12),
        };
        _searchBox = new TextBox
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12.5,
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(accent),
        };
        _searchBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        // Placeholder
        var placeholder = new TextBlock
        {
            Text = "Search apps...",
            FontSize = 12.5,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var searchHost = new Grid();
        searchHost.Children.Add(_searchBox);
        searchHost.Children.Add(placeholder);
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var searchIcon = UiKit.Icon(MaterialIconKind.Magnify, 16);
        searchIcon.Margin = new Thickness(0, 0, 8, 0);
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        searchGrid.Children.Add(searchIcon);
        Grid.SetColumn(searchHost, 1);
        searchGrid.Children.Add(searchHost);
        _searchBox.GotKeyboardFocus += (_, _) => searchBorder.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x88));
        _searchBox.LostKeyboardFocus += (_, _) => searchBorder.BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("CardBorderBrush");
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
            MaxHeight = 370,
        };
        _resultsPanel = new StackPanel();
        scroll.Content = _resultsPanel;
        mainPanel.Children.Add(scroll);

        root.Child = mainPanel;
        Content = root;

        // Drag to move
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        };

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
            finally { proc.Dispose(); }
        }

        // Add running apps
        foreach (var (name, path) in runningApps.OrderBy(kv => kv.Key))
        {
            ImageSource? icon = null;
            try
            {
                if (File.Exists(path))
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (sysIcon != null)
                        icon = Imaging.CreateBitmapSourceFromHIcon(sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }

            _allEntries.Add(new AppEntry(name, path, name, icon, true));
        }

        // Add common apps (only if they exist and aren't already in running list)
        foreach (var (name, rawPath, processName, matIcon) in CommonApps)
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
            var exePath = ExtractExecutablePath(expandedPath);

            // Skip if already shown as running
            if (runningApps.Keys.Any(k => ProcessNameMatches(k, processName) || ProcessNameMatches(k, name)))
                continue;

            if (File.Exists(exePath) || exePath == "explorer.exe" || exePath == "taskmgr.exe" || exePath == "calc.exe")
            {
                _allEntries.Add(new AppEntry(name, rawPath, processName, null, false, matIcon));
            }
        }

        RenderEntries(_allEntries);
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return command;

        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int closingQuote = command.IndexOf('"', 1);
            if (closingQuote > 1)
                return command[1..closingQuote];
        }

        int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0)
            return command[..(exeEnd + 4)];

        return command.Split(' ', 2)[0];
    }

    private static bool ProcessNameMatches(string processName, string configuredName)
    {
        var needle = (configuredName ?? "").Trim();
        if (needle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            needle = needle[..^4];
        if (needle.Length == 0) return false;

        var compactNeedle = needle.Replace(" ", "");
        var compactProcessName = processName.Replace(" ", "");
        return compactProcessName.Contains(compactNeedle, StringComparison.OrdinalIgnoreCase)
            || compactNeedle.Contains(compactProcessName, StringComparison.OrdinalIgnoreCase);
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
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)),
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
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)),
        Margin = new Thickness(4, 10, 0, 4),
    };

    private Border MakeAppRow(AppEntry entry)
    {
        var row = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
            SnapsToDevicePixels = true,
            Cursor = Cursors.Hand,
        };
        row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

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
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(textPanel);

        row.Child = content;

        // Hover effect
        OpenCanvas.AttachHover(row);

        // Click to select
        row.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            // Use unexpanded path with env vars for portability
            _selectedPath = entry.Path;
            _selectedProcessName = entry.ProcessName;
            _selectedName = entry.Name;
            DialogResult = true;
            Close();
        };

        return row;
    }
}
