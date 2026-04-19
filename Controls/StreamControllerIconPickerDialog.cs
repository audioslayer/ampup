using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Material.Icons;
using Material.Icons.WPF;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Svg.Skia;

namespace AmpUp.Controls;

public class StreamControllerIconPickerDialog : Window
{
    private static readonly HttpClient _http = CreateHttpClient();
    private static readonly string[] OnlinePrefixes =
    {
        "material-symbols",
        "mdi",
        "ph",
        "tabler",
        "fluent",
        "bi",
        "carbon",
        "simple-icons"
    };

    private readonly List<IconPresetEntry> _entries = BuildEntries();
    private readonly Dictionary<string, string> _onlineSvgCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _localGrid = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel _onlineGrid = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _searchBox;
    private readonly SegmentedControl _categoryPicker;
    private readonly SegmentedControl _sourcePicker;
    private readonly Border _localPanel;
    private readonly Border _onlinePanel;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _onlineStatus;
    private readonly DispatcherTimer _searchDebounce;

    private CancellationTokenSource? _searchCts;

    public string? SelectedIconKind { get; private set; }
    public string? SelectedDownloadedImagePath { get; private set; }

    private sealed record IconPresetEntry(string Label, string Kind, string Category, Color Accent);

    private sealed class OnlineIconEntry
    {
        public required string IconId { get; init; }
        public required string Prefix { get; init; }
        public required string Name { get; init; }
        public required string Label { get; init; }
        public required Border Card { get; init; }
        public required Image PreviewImage { get; init; }
    }

    public StreamControllerIconPickerDialog()
    {
        Title = "Choose Stream Controller Icon";
        Width = 860;
        Height = 700;
        MinWidth = 860;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = (Brush)Application.Current.FindResource("BgDarkBrush");

        _searchBox = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            FontSize = 12,
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += OnSearchTextChanged;

        _sourcePicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _sourcePicker.AddSegment("Built-In", "Local");
        _sourcePicker.AddSegment("Online", "Online");
        _sourcePicker.SelectedIndex = 0;
        _sourcePicker.SelectionChanged += (_, _) => UpdateSourceMode();

        _categoryPicker = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            AccentColor = ThemeManager.Accent,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _categoryPicker.AddSegment("All", "All");
        _categoryPicker.AddSegment("Media", "Media");
        _categoryPicker.AddSegment("Audio", "Audio");
        _categoryPicker.AddSegment("Apps", "Apps");
        _categoryPicker.AddSegment("System", "System");
        _categoryPicker.AddSegment("Creative", "Creative");
        _categoryPicker.AddSegment("Streaming", "Streaming");
        _categoryPicker.SelectedIndex = 0;
        _categoryPicker.SelectionChanged += (_, _) => RefreshLocalGrid();

        _subtitle = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _onlineStatus = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            Margin = new Thickness(0, 6, 0, 0),
            Text = "Search thousands of icons and only cache the one you pick."
        };

        _localPanel = new Border
        {
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _localGrid
            }
        };

        _onlinePanel = new Border
        {
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Children =
                {
                    _onlineStatus,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Margin = new Thickness(0, 6, 0, 0),
                        Content = _onlineGrid
                    }
                }
            }
        };

        _searchDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = SearchOnlineAsync(_searchBox.Text.Trim());
        };

        BuildUi();
        RefreshLocalGrid();
        UpdateSourceMode();
    }

    private void BuildUi()
    {
        var outer = new Border
        {
            Background = (Brush)Application.Current.FindResource("BgDarkBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };

        var main = new DockPanel { LastChildFill = true };
        outer.Child = main;

        var header = new StackPanel { Margin = new Thickness(18, 16, 18, 0) };
        DockPanel.SetDock(header, Dock.Top);

        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = new TextBlock
        {
            Text = "STREAM CONTROLLER ICONS",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent)
        };
        var close = new TextBlock
        {
            Text = "X",
            FontSize = 16,
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            Cursor = Cursors.Hand
        };
        close.MouseLeftButtonDown += (_, _) => CloseDialog(false);
        DockPanel.SetDock(close, Dock.Right);
        titleRow.Children.Add(close);
        titleRow.Children.Add(title);
        header.Children.Add(titleRow);
        header.Children.Add(_subtitle);
        header.Children.Add(_sourcePicker);

        var searchBorder = new Border
        {
            Background = (Brush)Application.Current.FindResource("CardBgBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var searchGrid = new Grid();
        var placeholder = new TextBlock
        {
            Text = "Search play, mute, spotify, obs, mic...",
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        };
        searchGrid.Children.Add(_searchBox);
        searchGrid.Children.Add(placeholder);
        searchBorder.Child = searchGrid;
        header.Children.Add(searchBorder);
        header.Children.Add(_categoryPicker);
        main.Children.Add(header);

        var content = new Grid
        {
            Margin = new Thickness(18, 0, 18, 18)
        };
        content.Children.Add(_localPanel);
        content.Children.Add(_onlinePanel);
        main.Children.Add(content);

        Content = outer;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseDialog(false);
        };
        Closed += (_, _) => _searchCts?.Cancel();
    }

    private void UpdateSourceMode()
    {
        bool online = IsOnlineMode;
        _categoryPicker.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
        _localPanel.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
        _onlinePanel.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
        _subtitle.Text = online
            ? "Browse a huge online icon library without shipping thousands of assets in Amp Up. Only the icon you pick gets cached locally."
            : "Use the built-in icon set that already ships with Amp Up for quick offline picks.";

        if (online)
        {
            if (string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                _onlineGrid.Children.Clear();
                _onlineStatus.Text = "Search thousands of icons and only cache the one you pick.";
            }
            else
            {
                _ = SearchOnlineAsync(_searchBox.Text.Trim());
            }
        }
        else
        {
            _searchDebounce.Stop();
            _searchCts?.Cancel();
            RefreshLocalGrid();
        }
    }

    private bool IsOnlineMode => string.Equals(_sourcePicker.SelectedTag as string, "Online", StringComparison.Ordinal);

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsOnlineMode)
        {
            RefreshLocalGrid();
            return;
        }

        _searchDebounce.Stop();
        if (string.IsNullOrWhiteSpace(_searchBox.Text))
        {
            _searchCts?.Cancel();
            _onlineGrid.Children.Clear();
            _onlineStatus.Text = "Search thousands of icons and only cache the one you pick.";
            return;
        }

        _onlineStatus.Text = "Searching...";
        _searchDebounce.Start();
    }

    private void RefreshLocalGrid()
    {
        _localGrid.Children.Clear();
        string category = _categoryPicker.SelectedTag as string ?? "All";
        string query = _searchBox.Text.Trim();

        var filtered = _entries.Where(e =>
                (category == "All" || e.Category == category) &&
                (string.IsNullOrWhiteSpace(query)
                    || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var entry in filtered)
            _localGrid.Children.Add(BuildLocalTile(entry));
    }

    private async Task SearchOnlineAsync(string query)
    {
        if (!IsOnlineMode)
            return;

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            _onlineGrid.Children.Clear();
            _onlineStatus.Text = $"Searching for \"{query}\"...";

            string prefixes = string.Join(",", OnlinePrefixes);
            string url = $"https://api.iconify.design/search?query={Uri.EscapeDataString(query)}&limit=72&prefixes={Uri.EscapeDataString(prefixes)}";
            using var response = await _http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));
            var icons = json["icons"]?.Values<string?>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() ?? new List<string>();
            if (token.IsCancellationRequested)
                return;

            if (icons.Count == 0)
            {
                _onlineStatus.Text = "No icons found yet. Try a broader search like play, mic, app, folder, game, or browser.";
                return;
            }

            var entries = icons
                .Select(BuildOnlineEntry)
                .Where(e => e != null)
                .Cast<OnlineIconEntry>()
                .ToList();

            foreach (var entry in entries)
                _onlineGrid.Children.Add(entry.Card);

            _onlineStatus.Text = $"Showing {entries.Count} icons";
            await LoadOnlinePreviewMarkupAsync(entries, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested)
                _onlineStatus.Text = "Online icon search hit a snag. Check your connection and try again.";
        }
    }

    private OnlineIconEntry? BuildOnlineEntry(string? iconId)
    {
        if (string.IsNullOrWhiteSpace(iconId))
            return null;

        var parts = iconId.Split(':');
        if (parts.Length != 2)
            return null;

        var image = new Image
        {
            Width = 34,
            Height = 34,
            Stretch = Stretch.Uniform
        };

        var card = new Border
        {
            Width = 116,
            Height = 118,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.FindResource("CardBgBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = iconId
        };

        var iconWrap = new Border
        {
            Width = 68,
            Height = 68,
            Margin = new Thickness(0, 10, 0, 8),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x1A, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B),
                Color.FromArgb(0x04, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B),
                90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            Child = image
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(iconWrap);
        stack.Children.Add(new TextBlock
        {
            Text = ToLabel(parts[1]),
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 8, 0),
            MaxHeight = 30
        });

        card.Child = stack;
        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x16, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
            card.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = (Brush)Application.Current.FindResource("CardBgBrush");
            card.BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush");
        };
        card.MouseLeftButtonUp += async (_, _) => await SelectOnlineIconAsync(card);

        return new OnlineIconEntry
        {
            IconId = iconId,
            Prefix = parts[0],
            Name = parts[1],
            Label = ToLabel(parts[1]),
            Card = card,
            PreviewImage = image
        };
    }

    private async Task LoadOnlinePreviewMarkupAsync(List<OnlineIconEntry> entries, CancellationToken token)
    {
        foreach (var group in entries.GroupBy(e => e.Prefix))
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                string names = string.Join(",", group.Select(e => e.Name));
                string url = $"https://api.iconify.design/{Uri.EscapeDataString(group.Key)}.json?icons={Uri.EscapeDataString(names)}";
                using var response = await _http.GetAsync(url, token);
                response.EnsureSuccessStatusCode();

                var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));
                var iconsObject = json["icons"] as JObject;
                if (iconsObject == null)
                    continue;

                int defaultWidth = (int?)json["width"] ?? 24;
                int defaultHeight = (int?)json["height"] ?? 24;

                foreach (var entry in group)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (iconsObject[entry.Name] is not JObject iconData)
                        continue;

                    string svgMarkup = BuildSvgMarkup(iconData, defaultWidth, defaultHeight);
                    _onlineSvgCache[entry.IconId] = svgMarkup;

                    var source = await Task.Run(() => RenderSvgToBitmapSource(svgMarkup, 72), token);
                    if (source == null || token.IsCancellationRequested)
                        continue;

                    await Dispatcher.InvokeAsync(() => entry.PreviewImage.Source = source, DispatcherPriority.Background, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private async Task SelectOnlineIconAsync(Border card)
    {
        if (card.Tag is not string iconId)
            return;

        if (!_onlineSvgCache.TryGetValue(iconId, out var svgMarkup))
        {
            _onlineStatus.Text = "Finishing icon download...";
            svgMarkup = await FetchSingleSvgMarkupAsync(iconId, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(svgMarkup))
            {
                _onlineStatus.Text = "That icon could not be loaded.";
                return;
            }

            _onlineSvgCache[iconId] = svgMarkup;
        }

        _onlineStatus.Text = "Caching selected icon...";
        try
        {
            string filePath = await Task.Run(() => CacheSelectedIcon(iconId, svgMarkup));
            SelectedDownloadedImagePath = filePath;
            SelectedIconKind = null;
            CloseDialog(true);
        }
        catch
        {
            _onlineStatus.Text = "The icon preview loaded, but saving it failed.";
        }
    }

    private static string CacheSelectedIcon(string iconId, string svgMarkup)
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmpUp",
            "IconCache");
        Directory.CreateDirectory(folder);

        string fileName = SanitizeFileName(iconId.Replace(':', '_')) + ".png";
        string filePath = Path.Combine(folder, fileName);
        byte[] pngBytes = RenderSvgToPngBytes(svgMarkup, 512);
        File.WriteAllBytes(filePath, pngBytes);
        return filePath;
    }

    private static string BuildSvgMarkup(JObject iconData, int defaultWidth, int defaultHeight)
    {
        int left = (int?)iconData["left"] ?? 0;
        int top = (int?)iconData["top"] ?? 0;
        int width = (int?)iconData["width"] ?? defaultWidth;
        int height = (int?)iconData["height"] ?? defaultHeight;
        string body = (string?)iconData["body"] ?? "";

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="{left} {top} {width} {height}" color="#F5F5F5">
            {body}
            </svg>
            """;
    }

    private async Task<string> FetchSingleSvgMarkupAsync(string iconId, CancellationToken token)
    {
        var parts = iconId.Split(':');
        if (parts.Length != 2)
            return "";

        string url = $"https://api.iconify.design/{Uri.EscapeDataString(parts[0])}.json?icons={Uri.EscapeDataString(parts[1])}";
        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();

        var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));
        var iconData = json["icons"]?[parts[1]] as JObject;
        if (iconData == null)
            return "";

        int defaultWidth = (int?)json["width"] ?? 24;
        int defaultHeight = (int?)json["height"] ?? 24;
        return BuildSvgMarkup(iconData, defaultWidth, defaultHeight);
    }

    private static BitmapSource? RenderSvgToBitmapSource(string svgMarkup, int size)
    {
        byte[] pngBytes = RenderSvgToPngBytes(svgMarkup, size);
        using var stream = new MemoryStream(pngBytes);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static byte[] RenderSvgToPngBytes(string svgMarkup, int size)
    {
        using var svg = new SKSvg();
        using var svgStream = new MemoryStream(Encoding.UTF8.GetBytes(svgMarkup));
        var picture = svg.Load(svgStream);
        if (picture == null)
            return Array.Empty<byte>();

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = new SKRect(0, 0, 24, 24);

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.86f;
        float dx = ((size - bounds.Width * scale) * 0.5f) - bounds.Left * scale;
        float dy = ((size - bounds.Height * scale) * 0.5f) - bounds.Top * scale;

        canvas.Translate(dx, dy);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray() ?? Array.Empty<byte>();
    }

    private UIElement BuildLocalTile(IconPresetEntry entry)
    {
        var card = new Border
        {
            Width = 108,
            Height = 110,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.FindResource("CardBgBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, entry.Accent.R, entry.Accent.G, entry.Accent.B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var iconWrap = new Border
        {
            Width = 62,
            Height = 62,
            Margin = new Thickness(0, 10, 0, 8),
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x2A, entry.Accent.R, entry.Accent.G, entry.Accent.B),
                Color.FromArgb(0x08, entry.Accent.R, entry.Accent.G, entry.Accent.B),
                90)
        };

        if (Enum.TryParse<MaterialIconKind>(entry.Kind, out var kind))
        {
            iconWrap.Child = new MaterialIcon
            {
                Kind = kind,
                Width = 28,
                Height = 28,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        stack.Children.Add(iconWrap);
        stack.Children.Add(new TextBlock
        {
            Text = entry.Label,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 8, 0)
        });

        card.Child = stack;

        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x18, entry.Accent.R, entry.Accent.G, entry.Accent.B));
            card.BorderBrush = new SolidColorBrush(entry.Accent);
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = (Brush)Application.Current.FindResource("CardBgBrush");
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, entry.Accent.R, entry.Accent.G, entry.Accent.B));
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            SelectedIconKind = entry.Kind;
            SelectedDownloadedImagePath = null;
            CloseDialog(true);
        };

        return card;
    }

    private void CloseDialog(bool result)
    {
        DialogResult = result;
        Close();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AmpUp/0.9.9-alpha");
        return client;
    }

    private static string ToLabel(string slug)
    {
        return string.Join(" ",
            slug.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length > 0
                    ? char.ToUpperInvariant(part[0]) + part[1..]
                    : part));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (char ch in name)
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        return builder.ToString();
    }

    private static List<IconPresetEntry> BuildEntries()
    {
        return new()
        {
            new("Play", "Play", "Media", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Pause", "Pause", "Media", Color.FromRgb(0x00, 0xC8, 0xFF)),
            new("Stop", "Stop", "Media", Color.FromRgb(0xFF, 0x5C, 0x5C)),
            new("Next", "SkipNext", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
            new("Previous", "SkipPrevious", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
            new("Playlist", "PlaylistPlay", "Media", Color.FromRgb(0x9B, 0x8C, 0xFF)),
            new("Record", "RecordCircle", "Streaming", Color.FromRgb(0xFF, 0x44, 0x44)),
            new("Live", "Video", "Streaming", Color.FromRgb(0xFF, 0x7B, 0x39)),
            new("Camera", "Camera", "Streaming", Color.FromRgb(0x00, 0xD0, 0xFF)),
            new("Video", "Video", "Streaming", Color.FromRgb(0x7C, 0x8C, 0xFF)),
            new("Mic", "Microphone", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Mic Off", "MicrophoneOff", "Audio", Color.FromRgb(0xFF, 0x5C, 0x5C)),
            new("Volume", "VolumeHigh", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Muted", "VolumeOff", "Audio", Color.FromRgb(0xFF, 0x6E, 0x6E)),
            new("Music", "Music", "Audio", Color.FromRgb(0xFF, 0x4D, 0xB0)),
            new("Equalizer", "ChartBar", "Audio", Color.FromRgb(0xFF, 0xD7, 0x40)),
            new("Voice Chat", "MicrophoneMessage", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
            new("Spotify", "Spotify", "Apps", Color.FromRgb(0x1D, 0xB9, 0x54)),
            new("Twitch", "Twitch", "Apps", Color.FromRgb(0x91, 0x44, 0xFF)),
            new("YouTube", "Youtube", "Apps", Color.FromRgb(0xFF, 0x3D, 0x3D)),
            new("Chrome", "GoogleChrome", "Apps", Color.FromRgb(0x57, 0xC8, 0x4D)),
            new("Folder", "Folder", "Apps", Color.FromRgb(0xFF, 0xC1, 0x07)),
            new("App", "Application", "Apps", Color.FromRgb(0x66, 0xBB, 0xFF)),
            new("Keyboard", "Keyboard", "System", Color.FromRgb(0x7C, 0x8C, 0xF8)),
            new("Mouse", "Mouse", "System", Color.FromRgb(0x00, 0xD0, 0xC8)),
            new("Monitor", "Monitor", "System", Color.FromRgb(0x00, 0xB4, 0xD8)),
            new("Power", "Power", "System", Color.FromRgb(0xFF, 0x7B, 0x39)),
            new("Lock", "Lock", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
            new("Sleep", "Sleep", "System", Color.FromRgb(0x7A, 0x87, 0xFF)),
            new("Web", "Web", "System", Color.FromRgb(0x26, 0xC6, 0xDA)),
            new("Rocket", "RocketLaunch", "System", Color.FromRgb(0xFF, 0x8A, 0x3D)),
            new("Palette", "Palette", "Creative", Color.FromRgb(0xFF, 0x5A, 0xA5)),
            new("Brush", "Brush", "Creative", Color.FromRgb(0xFF, 0x8A, 0x65)),
            new("Image", "ImageLock", "Creative", Color.FromRgb(0x66, 0xBB, 0xFF)),
            new("Light", "Lightbulb", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
            new("Gamepad", "Gamepad", "Creative", Color.FromRgb(0x69, 0xF0, 0xAE)),
            new("Scene Swap", "SwitchVideo", "Streaming", Color.FromRgb(0xC5, 0x5D, 0xFF)),
            new("Webcam", "Webcam", "Streaming", Color.FromRgb(0x5C, 0xD7, 0xFF)),
            new("Screen", "MonitorScreenshot", "Streaming", Color.FromRgb(0x4D, 0xD0, 0xE1)),
        };
    }
}
