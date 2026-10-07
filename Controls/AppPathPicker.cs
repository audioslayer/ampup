using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// GridPicker-style app chooser for button actions that target an app
/// (launch_exe / close_program / mute_program). The trigger looks exactly like
/// GridPicker's (icon tile + friendly name + muted description + rotating chevron);
/// clicking opens a borderless Window flyout with search, Running / Recently used /
/// Common apps sections, a Browse row and (process mode) a "Use “xyz”" fallback row.
/// The picker never touches config directly — it raises <see cref="ValuePicked"/>
/// with the exact string the caller stores (a launch path or a process name).
/// </summary>
public class AppPathPicker : System.Windows.Controls.Border
{
    public enum PickerMode { LaunchPath, ProcessName }

    /// <summary>LaunchPath stores exe paths / commands; ProcessName stores bare process names.</summary>
    public PickerMode Mode
    {
        get => _mode;
        set { if (_mode == value) return; _mode = value; UpdateTrigger(); }
    }

    /// <summary>Values already used in the user's configs for this kind of action.</summary>
    public Func<PickerMode, IEnumerable<string>>? RecentProvider { get; set; }

    /// <summary>Extra running process names (e.g. apps with audio sessions for mute_program).</summary>
    public Func<IEnumerable<string>>? ExtraRunningProvider { get; set; }

    public event Action<string>? ValuePicked;

    public string Value => _value;

    private PickerMode _mode = PickerMode.LaunchPath;
    private string _value = "";

    private readonly ContentControl _iconHost;
    private readonly TextBlock _label;
    private readonly TextBlock _sub;
    private readonly MaterialIcon _chevron;

    private Window? _flyout;
    private bool _isOpen;
    private DateTime _lastClose = DateTime.MinValue;

    private static readonly HashSet<string> SystemHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "svchost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
        "SearchHost", "SearchApp", "StartMenuExperienceHost", "explorer", "LockApp", "dwm", "csrss",
        "sihost", "ctfmon", "RuntimeBroker", "SecurityHealthSystray", "SecHealthUI", "TabTip",
        "WidgetBoard", "Widgets", "PhoneExperienceHost", "CrossDeviceResume", "NVIDIA Share",
        "NVIDIA Overlay", "GameBar", "GameBarFTServer", "XboxGameBarWidgets", "conhost",
        "WindowsTerminal_Host", "smartscreen", "UserOOBEBroker", "ShellHost", "Taskmgr_Host",
        "MicrosoftEdgeUpdate", "AmpUp",
    };

    public AppPathPicker()
    {
        this.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(8, 7, 12, 7);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        MinHeight = 50;
        this.SetResourceReference(BackgroundProperty, "InputBgBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _iconHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Focusable = false };
        grid.Children.Add(_iconHost);

        _label = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _sub = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _sub.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(_label);
        labels.Children.Add(_sub);
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);

        _chevron = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronDown, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
        };
        _chevron.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        Grid.SetColumn(_chevron, 2);
        grid.Children.Add(_chevron);

        Child = grid;
        UpdateTrigger();

        MouseEnter += (_, _) => ApplyHotChrome();
        MouseLeave += (_, _) => { if (!_isOpen) ResetChrome(); };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_isOpen) CloseFlyout();
            else if ((DateTime.UtcNow - _lastClose).TotalMilliseconds > 250) OpenFlyout();
        };
    }

    /// <summary>Update the displayed value (does not raise ValuePicked).</summary>
    public void SetValue(string? value)
    {
        value ??= "";
        if (value == _value) return;
        _value = value;
        UpdateTrigger();
    }

    // ── Trigger ───────────────────────────────────────────────────

    private void ApplyHotChrome()
    {
        var a = ThemeManager.Accent;
        BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0xAA));
        Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x14));
        _chevron.Foreground = new SolidColorBrush(a);
    }

    private void ResetChrome()
    {
        this.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        this.SetResourceReference(BackgroundProperty, "InputBgBrush");
        _chevron.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
    }

    private void RotateChevron(double angle)
        => ((RotateTransform)_chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    private void UpdateTrigger()
    {
        if (string.IsNullOrWhiteSpace(_value))
        {
            _label.Text = "Choose an app…";
            _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _sub.Text = _mode == PickerMode.ProcessName ? "Pick a running app or type a process name" : "Pick an app to launch";
            _iconHost.Content = GridPicker.BuildTile(MaterialIconKind.Application, null, null, Color.FromRgb(0x88, 0x88, 0x88), 32);
            ToolTip = null;
            return;
        }

        var info = Describe(_value, _mode);
        _label.Text = info.Title;
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _sub.Text = info.Description;
        _iconHost.Content = GridPicker.BuildTile(MaterialIconKind.Application, null, info.Icon, ThemeManager.Accent, 32);
        ToolTip = _value;
    }

    // ── App metadata helpers ──────────────────────────────────────

    private sealed record AppInfo(string Title, string Description, ImageSource? Icon);

    private sealed record Row(string Value, string Title, string Description, ImageSource? Icon, MaterialIconKind? Kind);

    private static AppInfo Describe(string value, PickerMode mode)
    {
        if (mode == PickerMode.ProcessName)
        {
            var name = StripExe(value.Trim());
            var running = FindRunningExe(name);
            string title = running != null ? FriendlyName(running, name) : CommonTitleFor(name) ?? name;
            string desc = running != null ? $"Running · {Path.GetFileName(running)}" : $"Process name · {name}";
            ImageSource? icon = running != null ? ExtractIcon(running) : null;
            icon ??= SafeMixerIcon(name);
            return new AppInfo(title, desc, icon);
        }
        else
        {
            var exe = ExtractExecutablePath(Environment.ExpandEnvironmentVariables(value));
            var procName = Path.GetFileNameWithoutExtension(exe);
            string title = File.Exists(exe) ? FriendlyName(exe, procName) : CommonTitleForPath(value) ?? procName;
            bool isRunning = !string.IsNullOrEmpty(procName) && IsProcessRunning(procName);
            string desc = isRunning ? $"Running · {Path.GetFileName(exe)}" : ShortenPath(value);
            var icon = ExtractIcon(exe) ?? SafeMixerIcon(procName);
            return new AppInfo(title, desc, icon);
        }
    }

    private static string StripExe(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    private static string? CommonTitleFor(string processName)
    {
        foreach (var c in AppPickerDialog.CommonApps)
            if (c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) return c.Name;
        return null;
    }

    private static string? CommonTitleForPath(string path)
    {
        foreach (var c in AppPickerDialog.CommonApps)
            if (c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return c.Name;
        return null;
    }

    private static ImageSource? SafeMixerIcon(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        try { return AmpUp.Views.MixerView.GetAppIcon(processName); } catch { return null; }
    }

    private static bool IsProcessRunning(string name)
    {
        var procs = Process.GetProcessesByName(name);
        try { return procs.Length > 0; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    private static string? FindRunningExe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var procs = Process.GetProcessesByName(name);
        try
        {
            foreach (var p in procs)
            {
                try { var f = p.MainModule?.FileName; if (!string.IsNullOrEmpty(f)) return f; } catch { }
            }
            return null;
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    internal static string FriendlyName(string exePath, string fallback)
    {
        try
        {
            if (File.Exists(exePath))
            {
                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(fvi.FileDescription)) return fvi.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(fvi.ProductName)) return fvi.ProductName.Trim();
            }
        }
        catch { }
        return string.IsNullOrWhiteSpace(fallback) ? Path.GetFileNameWithoutExtension(exePath) : fallback;
    }

    internal static ImageSource? ExtractIcon(string? exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    internal static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return command ?? "";
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            if (closing > 1) return command[1..closing];
        }
        int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0) return command[..(exeEnd + 4)];
        return command.Split(' ', 2)[0];
    }

    internal static string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var map = new List<(string Dir, string Token)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LocalAppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%AppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "%ProgramFiles(x86)%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "%ProgramFiles%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%UserProfile%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "%WinDir%"),
        };
        foreach (var (dir, token) in map.Where(m => !string.IsNullOrEmpty(m.Dir)).OrderByDescending(m => m.Dir.Length))
        {
            if (path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase))
                return token + path[dir.Length..];
        }
        return path;
    }

    // ── Data ──────────────────────────────────────────────────────

    private List<Row> BuildRunning()
    {
        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == self) continue;
                if (proc.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(proc.MainWindowTitle)) continue;
                var name = proc.ProcessName;
                if (SystemHosts.Contains(name) || !seen.Add(name)) continue;
                string? exe = null;
                try { exe = proc.MainModule?.FileName; } catch { }
                rows.Add(MakeRunningRow(name, exe));
            }
            catch { }
            finally { proc.Dispose(); }
        }

        if (ExtraRunningProvider != null)
        {
            try
            {
                foreach (var name in ExtraRunningProvider())
                {
                    if (string.IsNullOrWhiteSpace(name) || SystemHosts.Contains(name) || !seen.Add(name)) continue;
                    rows.Add(MakeRunningRow(name, FindRunningExe(name)));
                }
            }
            catch { }
        }

        // Launch mode needs a real path to store; drop entries we couldn't resolve.
        if (_mode == PickerMode.LaunchPath)
            rows = rows.Where(r => r.Value.Contains('\\')).ToList();
        return rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Row MakeRunningRow(string name, string? exe)
    {
        string title = exe != null ? FriendlyName(exe, name) : name;
        string desc = $"Running · {(exe != null ? Path.GetFileName(exe) : name + ".exe")}";
        var icon = ExtractIcon(exe) ?? SafeMixerIcon(name);
        string value = _mode == PickerMode.ProcessName ? name : (exe ?? name);
        return new Row(value, title, desc, icon, null);
    }

    private List<Row> BuildRecent()
    {
        var rows = new List<Row>();
        if (RecentProvider == null) return rows;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var v in RecentProvider(_mode))
            {
                if (string.IsNullOrWhiteSpace(v) || !seen.Add(v.Trim())) continue;
                var info = Describe(v.Trim(), _mode);
                rows.Add(new Row(v.Trim(), info.Title, info.Description, info.Icon, MaterialIconKind.History));
            }
        }
        catch { }
        return rows;
    }

    private List<Row> BuildCommon()
    {
        var rows = new List<Row>();
        foreach (var (name, rawPath, processName, kind) in AppPickerDialog.CommonApps)
        {
            var exe = ExtractExecutablePath(Environment.ExpandEnvironmentVariables(rawPath));
            bool builtin = exe is "explorer.exe" or "taskmgr.exe" or "calc.exe";
            if (!builtin && !File.Exists(exe)) continue;
            string value = _mode == PickerMode.ProcessName ? processName : rawPath;
            string desc = _mode == PickerMode.ProcessName ? $"Process name · {processName}" : ShortenPath(rawPath);
            rows.Add(new Row(value, name, desc, builtin ? null : ExtractIcon(exe), kind));
        }
        return rows;
    }

    // ── Flyout ────────────────────────────────────────────────────

    private void CloseFlyout()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastClose = DateTime.UtcNow;
        var f = _flyout;
        _flyout = null;
        f?.Close();
        RotateChevron(0);
        if (!IsMouseOver) ResetChrome();
    }

    private void Commit(string value)
    {
        CloseFlyout();
        value = value.Trim();
        if (value.Length == 0) return;
        SetValue(value);
        ValuePicked?.Invoke(value);
    }

    private void BrowseForFile()
    {
        CloseFlyout();
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables (*.exe)|*.exe|Batch Files (*.bat)|*.bat|All Files (*.*)|*.*",
            FilterIndex = 1,
        };
        var current = _mode == PickerMode.LaunchPath ? ExtractExecutablePath(Environment.ExpandEnvironmentVariables(_value)) : "";
        if (!string.IsNullOrEmpty(current) && Path.GetDirectoryName(current) is string dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        else
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            var v = _mode == PickerMode.ProcessName ? Path.GetFileNameWithoutExtension(dlg.FileName) : dlg.FileName;
            SetValue(v);
            ValuePicked?.Invoke(v);
        }
    }

    private void OpenFlyout()
    {
        var accent = ThemeManager.Accent;
        var running = BuildRunning();
        var recent = BuildRecent();
        var common = BuildCommon();

        var listPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = listPanel,
            Focusable = false,
        };

        var searchBox = new TextBox
        {
            FontSize = 12.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2), VerticalContentAlignment = VerticalAlignment.Center,
        };
        searchBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        searchBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        var ph = new TextBlock
        {
            Text = _mode == PickerMode.ProcessName ? "Search apps or type a process name…" : "Search apps…",
            FontSize = 12.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0),
        };
        ph.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mag = UiKit.Icon(MaterialIconKind.Magnify, 16);
        mag.Margin = new Thickness(0, 0, 8, 0);
        searchGrid.Children.Add(mag);
        var textHost = new Grid();
        textHost.Children.Add(searchBox);
        textHost.Children.Add(ph);
        Grid.SetColumn(textHost, 1);
        searchGrid.Children.Add(textHost);
        var searchBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 8), Child = searchGrid,
        };
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        searchBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        var hint = new TextBlock { Text = "↑ ↓ to move  ·  Enter to choose  ·  Esc to close", FontSize = 10, Margin = new Thickness(4, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var root = new DockPanel();
        DockPanel.SetDock(searchBorder, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(searchBorder);
        root.Children.Add(hint);
        root.Children.Add(scroll);

        var popup = new System.Windows.Controls.Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = root,
        };
        popup.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgDarkBrush");
        popup.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "InputBorderBrush");

        var navRows = new List<(System.Windows.Controls.Border Row, Action Activate)>();
        int nav = -1;
        System.Windows.Controls.Border? currentRow = null;

        void SetNav(int idx)
        {
            if (nav >= 0 && nav < navRows.Count) navRows[nav].Row.BorderBrush = Brushes.Transparent;
            nav = idx;
            if (idx >= 0 && idx < navRows.Count)
            {
                navRows[idx].Row.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x88));
                navRows[idx].Row.BringIntoView();
            }
        }

        FrameworkElement Tile(ImageSource? icon, MaterialIconKind? kind, Color color)
            => GridPicker.BuildTile(kind ?? MaterialIconKind.Application, null, icon, color, 32);

        System.Windows.Controls.Border MakeRow(FrameworkElement tile, string title, string? desc, bool selected, Action activate)
        {
            var row = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 10, 6), Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, SnapsToDevicePixels = true,
                Background = selected ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)) : Brushes.Transparent,
            };
            var normalBg = row.Background;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tile.Margin = new Thickness(0, 0, 10, 0);
            g.Children.Add(tile);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            if (selected) t.Foreground = new SolidColorBrush(accent);
            else t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            sp.Children.Add(t);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = new TextBlock { Text = desc, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = desc };
                d.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
                sp.Children.Add(d);
            }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            if (selected)
            {
                var check = new MaterialIcon { Kind = MaterialIconKind.Check, Width = 16, Height = 16, Foreground = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(check, 2);
                g.Children.Add(check);
                currentRow = row;
            }
            row.Child = g;
            row.MouseEnter += (_, _) => { if (!selected) row.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush"); };
            row.MouseLeave += (_, _) => { if (!selected) row.Background = normalBg; };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; activate(); };
            navRows.Add((row, activate));
            return row;
        }

        bool IsCurrent(string v) => !string.IsNullOrWhiteSpace(_value) && v.Equals(_value.Trim(), StringComparison.OrdinalIgnoreCase);

        void Rebuild()
        {
            listPanel.Children.Clear();
            navRows.Clear();
            nav = -1;
            currentRow = null;
            string q = searchBox.Text.Trim();
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool Match(Row r) => terms.All(t => (r.Title + " " + r.Value + " " + r.Description).Contains(t, StringComparison.OrdinalIgnoreCase));

            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Section(string header, IEnumerable<Row> source)
            {
                var items = source.Where(Match).Where(r => !shown.Contains(r.Value)).ToList();
                if (items.Count == 0) return;
                listPanel.Children.Add(UiKit.SectionHeader(header, items.Count.ToString()));
                foreach (var r in items)
                {
                    shown.Add(r.Value);
                    var v = r.Value;
                    listPanel.Children.Add(MakeRow(Tile(r.Icon, r.Kind, accent), r.Title, r.Description, IsCurrent(v), () => Commit(v)));
                }
            }

            Section("RUNNING APPS", running);
            Section("RECENTLY USED", recent);
            Section("COMMON APPS", common);

            if (navRows.Count == 0 && q.Length > 0)
            {
                var none = new TextBlock { Text = "No matching apps.", FontSize = 11.5, Margin = new Thickness(6, 4, 6, 8) };
                none.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
                listPanel.Children.Add(none);
            }

            listPanel.Children.Add(UiKit.SectionHeader("OTHER"));
            bool looksLikePath = q.Contains('\\') || q.Contains('/') || q.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (q.Length > 0 && (_mode == PickerMode.ProcessName || looksLikePath)
                && !shown.Contains(q))
            {
                string custom = q;
                listPanel.Children.Add(MakeRow(Tile(null, MaterialIconKind.FormTextbox, accent), $"Use “{custom}”",
                    _mode == PickerMode.ProcessName ? "Match any process whose name contains this text" : "Launch this path or command",
                    false, () => Commit(custom)));
            }
            else if (_mode == PickerMode.ProcessName && q.Length == 0)
            {
                listPanel.Children.Add(MakeRow(Tile(null, MaterialIconKind.FormTextbox, accent), "Type a process name…",
                    "Start typing above (e.g. discord, game.exe) to target an app that isn't running", false,
                    () => { searchBox.Focus(); Keyboard.Focus(searchBox); }));
            }
            listPanel.Children.Add(MakeRow(Tile(null, MaterialIconKind.FolderOpenOutline, accent), "Browse for a file…",
                _mode == PickerMode.ProcessName ? "Pick an .exe — its process name is used" : "Choose an .exe or .bat on disk",
                false, BrowseForFile));

            if (navRows.Count > 0 && q.Length > 0) SetNav(0);
        }

        searchBox.TextChanged += (_, _) =>
        {
            ph.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
            scroll.ScrollToTop();
        };

        // ── Placement (DPI-aware, flips upward when there's no room below) ──
        double width = Math.Max(ActualWidth, 380);
        var belowPx = PointToScreen(new Point(0, ActualHeight + 4));
        var abovePx = PointToScreen(new Point(0, -4));
        var source = PresentationSource.FromVisual(this);
        double dpiX = 1, dpiY = 1;
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
        }
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)belowPx.X, (int)belowPx.Y)).WorkingArea;
        double waTop = wa.Top / dpiY, waBottom = wa.Bottom / dpiY, waLeft = wa.Left / dpiX, waRight = wa.Right / dpiX;
        double belowY = belowPx.Y / dpiY, aboveY = abovePx.Y / dpiY;
        double spaceBelow = waBottom - belowY - 8, spaceAbove = aboveY - waTop - 8;
        bool openUp = spaceBelow < 380 && spaceAbove > spaceBelow;
        double height = Math.Clamp(openUp ? spaceAbove : spaceBelow, 240, 480);
        popup.Width = width;
        popup.Height = height;
        double left = belowPx.X / dpiX;
        if (left + width > waRight) left = Math.Max(waLeft, waRight - width);
        double top = openUp ? aboveY - height : belowY;

        _flyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Content = popup,
            Left = left,
            Top = top,
        };
        _flyout.SetResourceReference(Window.BackgroundProperty, "BgDarkBrush");
        _flyout.Deactivated += (_, _) => CloseFlyout();
        _flyout.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (searchBox.Text.Length > 0) searchBox.Text = ""; else CloseFlyout();
                    e.Handled = true; break;
                case Key.Down:
                    if (navRows.Count > 0) SetNav(nav < 0 ? 0 : Math.Min(nav + 1, navRows.Count - 1));
                    e.Handled = true; break;
                case Key.Up:
                    if (navRows.Count > 0) SetNav(nav < 0 ? navRows.Count - 1 : Math.Max(nav - 1, 0));
                    e.Handled = true; break;
                case Key.Enter:
                    if (nav >= 0 && nav < navRows.Count) navRows[nav].Activate();
                    else if (navRows.Count > 0) navRows[0].Activate();
                    e.Handled = true; break;
            }
        };

        _isOpen = true;
        ApplyHotChrome();
        RotateChevron(180);
        Rebuild();
        var translate = new TranslateTransform(0, openUp ? 8 : -8);
        popup.RenderTransform = translate;
        popup.Opacity = 0;
        _flyout.Show();
        _flyout.Activate();
        searchBox.Focus();
        Keyboard.Focus(searchBox);
        currentRow?.BringIntoView();
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(openUp ? 8 : -8, 0, TimeSpan.FromMilliseconds(130))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        popup.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
    }
}
