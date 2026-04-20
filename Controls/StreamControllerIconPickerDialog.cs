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
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Svg.Skia;

namespace AmpUp.Controls;

public class StreamControllerIconPickerDialog : Window
{
    private static readonly HttpClient _http = CreateHttpClient();
    private static readonly string[] OnlinePrefixes =
    {
        "streamline-color",
        "streamline-plump-color",
        "streamline-cyber-color",
        "streamline-flex-color",
        "streamline-freehand-color",
        "streamline-kameleon-color",
        "streamline-sharp-color",
        "streamline-stickies-color",
        "streamline-ultimate-color",
        "fluent-color",
        "logos",
        "skill-icons",
        "devicon",
        "vscode-icons",
        "catppuccin",
        "twemoji",
        "fluent-emoji-flat",
        "cryptocurrency-color",
        "token-branded",
        "simple-icons"
    };

    private readonly List<IconPresetEntry> _entries = BuildEntries();
    private readonly Dictionary<string, string> _onlineSvgCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _resultsGrid = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _searchBox;
    private readonly SegmentedControl _categoryPicker;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _resultStatus;
    private readonly DispatcherTimer _searchDebounce;
    private readonly Border _searchChrome;
    private readonly TextBlock _searchPlaceholder;
    private readonly Button _clearSearchButton;

    private CancellationTokenSource? _searchCts;

    public string? SelectedIconKind { get; private set; }
    public string? SelectedDownloadedImagePath { get; private set; }

    /// <summary>
    /// Accent colour associated with the icon the user picked. Caller
    /// should apply this to the key's AccentColor so the on-device glow
    /// matches the hue the user saw in the picker (e.g. orange for
    /// VolumeHigh, cyan for Cloud, etc.) instead of resetting to green.
    /// </summary>
    public Color? SelectedAccent { get; private set; }

    private sealed record IconPresetEntry(string Label, string Kind, string Category, Color Accent);

    private sealed class OnlineIconEntry
    {
        public required string IconId { get; init; }
        public required string Prefix { get; init; }
        public required string Name { get; init; }
        public required string Label { get; init; }
        public required bool Palette { get; init; }
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
            CaretBrush = new SolidColorBrush(ThemeManager.Accent),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            FontSize = 13,
            Height = 40,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0)
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.GotKeyboardFocus += (_, _) => UpdateSearchChrome();
        _searchBox.LostKeyboardFocus += (_, _) => UpdateSearchChrome();

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

        _resultStatus = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            Margin = new Thickness(0, 6, 0, 0),
            Text = "Showing built-in quick icons. Search to pull in colorful app logos, emoji, and bright icon packs."
        };

        _searchChrome = new Border
        {
            Background = (Brush)Application.Current.FindResource("InputBgBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("InputBorderBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 12)
        };

        _searchPlaceholder = new TextBlock
        {
            Text = "Search colorful icons like play, spotify, camera, game, fire...",
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        _clearSearchButton = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Content = new MaterialIcon
            {
                Kind = MaterialIconKind.CloseCircle,
                Width = 16,
                Height = 16,
                Foreground = (Brush)Application.Current.FindResource("TextDimBrush")
            }
        };
        _clearSearchButton.Click += (_, _) =>
        {
            _searchBox.Clear();
            _searchBox.Focus();
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
        titleRow.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
        header.Children.Add(titleRow);
        header.Children.Add(_subtitle);

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Magnify,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(searchIcon, 0);
        searchGrid.Children.Add(searchIcon);

        var searchBoxHost = new Grid();
        searchBoxHost.Children.Add(_searchBox);
        searchBoxHost.Children.Add(_searchPlaceholder);
        Grid.SetColumn(searchBoxHost, 1);
        searchGrid.Children.Add(searchBoxHost);

        Grid.SetColumn(_clearSearchButton, 2);
        searchGrid.Children.Add(_clearSearchButton);

        _searchChrome.Child = searchGrid;
        header.Children.Add(_searchChrome);

        var categoryRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var uploadButton = BuildUploadButton();
        DockPanel.SetDock(uploadButton, Dock.Right);
        categoryRow.Children.Add(uploadButton);
        _categoryPicker.Margin = new Thickness(0);
        categoryRow.Children.Add(_categoryPicker);
        header.Children.Add(categoryRow);

        main.Children.Add(header);

        var content = new Grid
        {
            Margin = new Thickness(18, 0, 18, 18)
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_resultStatus, 0);
        content.Children.Add(_resultStatus);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 6, 0, 0),
            Content = _resultsGrid,
            CanContentScroll = false
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);
        main.Children.Add(content);

        Content = outer;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseDialog(false);
        };
        Closed += (_, _) => _searchCts?.Cancel();
        UpdateSearchChrome();
        _subtitle.Text = "Browse built-in icons instantly, then search a curated library of free colorful icon packs, logos, and emoji without leaving the same picker.";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSearchChrome();
        RefreshLocalGrid();

        _searchDebounce.Stop();
        if (string.IsNullOrWhiteSpace(_searchBox.Text))
        {
            _searchCts?.Cancel();
            _resultStatus.Text = $"Showing {_resultsGrid.Children.Count} built-in quick icons. Search to pull in colorful online results.";
            return;
        }

        _resultStatus.Text = $"Showing {_resultsGrid.Children.Count} built-in matches. Searching colorful packs...";
        _searchDebounce.Start();
    }

    private void UpdateSearchChrome()
    {
        bool hasText = !string.IsNullOrWhiteSpace(_searchBox.Text);
        bool focused = _searchBox.IsKeyboardFocused;

        _searchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        _clearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        _searchChrome.BorderBrush = focused
            ? new SolidColorBrush(ThemeManager.Accent)
            : (Brush)Application.Current.FindResource("InputBorderBrush");
        _searchChrome.Background = focused
            ? (Brush)Application.Current.FindResource("CardBgBrush")
            : (Brush)Application.Current.FindResource("InputBgBrush");
    }

    private void RefreshLocalGrid()
    {
        _resultsGrid.Children.Clear();
        string category = _categoryPicker.SelectedTag as string ?? "All";
        string query = _searchBox.Text.Trim();

        var filtered = _entries.Where(e =>
                (category == "All" || e.Category == category) &&
                (string.IsNullOrWhiteSpace(query)
                    || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var entry in filtered)
            _resultsGrid.Children.Add(BuildLocalTile(entry));

        _resultStatus.Text = string.IsNullOrWhiteSpace(query)
            ? $"Showing {filtered.Count} built-in quick icons. Search to pull in colorful online results."
            : $"Showing {filtered.Count} built-in matches. Searching colorful packs...";
    }

    private async Task SearchOnlineAsync(string query)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            int localCount = _resultsGrid.Children.Count;
            _resultStatus.Text = $"Showing {localCount} built-in matches. Searching colorful packs for \"{query}\"...";

            string prefixes = string.Join(",", OnlinePrefixes);
            string url = $"https://api.iconify.design/search?query={Uri.EscapeDataString(query)}&limit=120&prefixes={Uri.EscapeDataString(prefixes)}";
            using var response = await _http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));
            var icons = json["icons"]?.Values<string?>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() ?? new List<string>();
            var paletteMap = BuildPaletteMap(json["collections"] as JObject);
            if (token.IsCancellationRequested)
                return;

            if (icons.Count == 0)
            {
                _resultStatus.Text = localCount > 0
                    ? $"Showing {localCount} built-in matches. No extra online icons found for \"{query}\"."
                    : $"No icons found yet. Try a broader search like play, spotify, camera, game, emoji, or browser.";
                return;
            }

            var entries = icons
                .Select(iconId => BuildOnlineEntry(iconId, paletteMap))
                .Where(e => e != null)
                .Cast<OnlineIconEntry>()
                .OrderByDescending(e => e.Palette)
                .ThenBy(e => GetPrefixPriority(e.Prefix))
                .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var entry in entries)
                _resultsGrid.Children.Add(entry.Card);

            _resultStatus.Text = $"Showing {localCount} built-in matches and {entries.Count} online icons.";
            await LoadOnlinePreviewMarkupAsync(entries, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested)
                    _resultStatus.Text = "Colorful icon search hit a snag. Check your connection and try again.";
        }
    }

    private OnlineIconEntry? BuildOnlineEntry(string? iconId, IReadOnlyDictionary<string, bool> paletteMap)
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
            Palette = paletteMap.TryGetValue(parts[0], out var palette) && palette,
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

                    string svgMarkup = BuildSvgMarkup(iconData, defaultWidth, defaultHeight, entry.Palette);
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
            _resultStatus.Text = "Finishing icon download...";
            svgMarkup = await FetchSingleSvgMarkupAsync(iconId, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(svgMarkup))
            {
                _resultStatus.Text = "That icon could not be loaded.";
                return;
            }

            _onlineSvgCache[iconId] = svgMarkup;
        }

        _resultStatus.Text = "Caching selected icon...";
        try
        {
            string filePath = await Task.Run(() => CacheSelectedIcon(iconId, svgMarkup));
            SelectedDownloadedImagePath = filePath;
            SelectedIconKind = null;
            CloseDialog(true);
        }
        catch
        {
            _resultStatus.Text = "The icon preview loaded, but saving it failed.";
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

    private static string BuildSvgMarkup(JObject iconData, int defaultWidth, int defaultHeight, bool preservePalette)
    {
        int left = (int?)iconData["left"] ?? 0;
        int top = (int?)iconData["top"] ?? 0;
        int width = (int?)iconData["width"] ?? defaultWidth;
        int height = (int?)iconData["height"] ?? defaultHeight;
        string body = (string?)iconData["body"] ?? "";
        string colorAttribute = preservePalette ? "" : " color=\"#F5F5F5\"";

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="{left} {top} {width} {height}"{colorAttribute}>
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
        bool preservePalette = IsLikelyPalettePrefix(parts[0]);
        return BuildSvgMarkup(iconData, defaultWidth, defaultHeight, preservePalette);
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
        else if (entry.Kind.StartsWith("neon_") || entry.Kind.StartsWith("material_"))
        {
            string filename = entry.Kind switch
            {
                "neon_play" => "play_button_neon.jpg",
                "neon_pause" => "pause_button_neon.jpg",
                "neon_next" => "next_track_neon.jpg",
                "neon_spotify" => "spotify_neon.jpg",
                "neon_discord" => "discord_neon.jpg",
                "neon_chrome" => "neon_chrome.jpg",
                "neon_obs" => "neon_obs.jpg",
                "neon_youtube" => "neon_youtube.jpg",
                "neon_folder" => "neon_folder.jpg",
                "neon_power" => "neon_power.jpg",
                "neon_mic" => "mic_active_neon.jpg",
                "neon_mic_mute" => "mic_mute_neon.jpg",
                "neon_volume" => "speaker_volume_neon.jpg",
                "neon_panels" => "light_panel_white_cyan.jpg",
                "neon_geometric" => "light_geometric_cyan_purple.jpg",
                "neon_wall_light_on" => "neon_wall_light_on.jpg",
                "neon_wall_light_off" => "neon_wall_light_off.jpg",
                "neon_case_light_on" => "neon_case_light_on.jpg",
                "neon_case_light_off" => "neon_case_light_off.jpg",
                "neon_bulb_on" => "neon_bulb_on.jpg",
                "neon_bulb_off" => "neon_bulb_off.jpg",
                "neon_monitor" => "neon_monitor.jpg",
                "material_playpause" => "material_playpause.jpg",
                "material_next" => "material_next.jpg",
                "material_prev" => "material_prev.jpg",
                "material_home" => "material_home.jpg",
                "material_settings" => "material_settings.jpg",
                "material_gamepad" => "material_gamepad.jpg",
                "material_power" => "material_power.jpg",
                "material_webcam" => "material_webcam.jpg",
                "material_mic" => "material_mic.jpg",
                "material_mic_mute" => "material_mic_mute.jpg",
                "material_volume" => "material_volume.jpg",
                "material_volume_mute" => "material_volume_mute.jpg",
                _ => ""
            };

            if (!string.IsNullOrEmpty(filename))
            {
                try 
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icons", filename);
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        
                        iconWrap.Child = new Image
                        {
                            Source = bmp,
                            Width = 42,
                            Height = 42,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                    }
                }
                catch { }
            }
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
            if (entry.Kind.StartsWith("neon_") || entry.Kind.StartsWith("material_"))
            {
                string filename = entry.Kind switch
                {
                    "neon_play" => "play_button_neon.jpg",
                    "neon_pause" => "pause_button_neon.jpg",
                    "neon_next" => "next_track_neon.jpg",
                    "neon_spotify" => "spotify_neon.jpg",
                    "neon_discord" => "discord_neon.jpg",
                    "neon_chrome" => "neon_chrome.jpg",
                    "neon_obs" => "neon_obs.jpg",
                    "neon_youtube" => "neon_youtube.jpg",
                    "neon_folder" => "neon_folder.jpg",
                    "neon_power" => "neon_power.jpg",
                    "neon_mic" => "mic_active_neon.jpg",
                    "neon_mic_mute" => "mic_mute_neon.jpg",
                    "neon_volume" => "speaker_volume_neon.jpg",
                    "neon_panels" => "light_panel_white_cyan.jpg",
                    "neon_geometric" => "light_geometric_cyan_purple.jpg",
                    "neon_wall_light_on" => "neon_wall_light_on.jpg",
                    "neon_wall_light_off" => "neon_wall_light_off.jpg",
                    "neon_case_light_on" => "neon_case_light_on.jpg",
                    "neon_case_light_off" => "neon_case_light_off.jpg",
                    "neon_bulb_on" => "neon_bulb_on.jpg",
                    "neon_bulb_off" => "neon_bulb_off.jpg",
                    "neon_monitor" => "neon_monitor.jpg",
                    "material_playpause" => "material_playpause.jpg",
                    "material_next" => "material_next.jpg",
                    "material_prev" => "material_prev.jpg",
                    "material_home" => "material_home.jpg",
                    "material_settings" => "material_settings.jpg",
                    "material_gamepad" => "material_gamepad.jpg",
                    "material_power" => "material_power.jpg",
                    "material_webcam" => "material_webcam.jpg",
                    "material_mic" => "material_mic.jpg",
                    "material_mic_mute" => "material_mic_mute.jpg",
                    "material_volume" => "material_volume.jpg",
                    "material_volume_mute" => "material_volume_mute.jpg",
                    _ => ""
                };
                
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icons", filename);
                SelectedIconKind = null;
                SelectedDownloadedImagePath = path;
            }
            else
            {
                SelectedIconKind = entry.Kind;
                SelectedDownloadedImagePath = null;
            }

            SelectedAccent = entry.Accent;
            CloseDialog(true);
        };

        return card;
    }

    private Button BuildUploadButton()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new MaterialIcon
        {
            Kind = MaterialIconKind.Upload,
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 6, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Upload Image",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush")
        });

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(12, 6, 12, 6),
            Background = (Brush)Application.Current.FindResource("InputBgBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B)),
            BorderThickness = new Thickness(1),
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Use your own image file"
        };
        button.Click += (_, _) => UploadCustomImage();
        return button;
    }

    private void UploadCustomImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Title = "Choose Stream Controller image"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedDownloadedImagePath = dialog.FileName;
            SelectedIconKind = null;
            CloseDialog(true);
        }
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

    private static Dictionary<string, bool> BuildPaletteMap(JObject? collections)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (collections == null)
            return map;

        foreach (var property in collections.Properties())
            map[property.Name] = property.Value["palette"]?.Value<bool>() ?? false;

        return map;
    }

    private static int GetPrefixPriority(string prefix)
    {
        return prefix switch
        {
            "streamline-color" => 0,
            "streamline-plump-color" => 1,
            "fluent-color" => 2,
            "streamline-kameleon-color" => 3,
            "streamline-ultimate-color" => 4,
            "streamline-flex-color" => 5,
            "streamline-cyber-color" => 6,
            "skill-icons" => 7,
            "logos" => 8,
            "vscode-icons" => 9,
            "catppuccin" => 10,
            "devicon" => 11,
            "twemoji" => 12,
            "fluent-emoji-flat" => 13,
            "simple-icons" => 14,
            _ => 20
        };
    }

    private static bool IsLikelyPalettePrefix(string prefix)
    {
        return prefix switch
        {
            "logos" => true,
            "simple-icons" => true,
            "skill-icons" => true,
            "devicon" => true,
            "vscode-icons" => true,
            "catppuccin" => true,
            "streamline-color" => true,
            "streamline-plump-color" => true,
            "streamline-cyber-color" => true,
            "streamline-flex-color" => true,
            "streamline-freehand-color" => true,
            "streamline-kameleon-color" => true,
            "streamline-sharp-color" => true,
            "streamline-stickies-color" => true,
            "streamline-ultimate-color" => true,
            "fluent-color" => true,
            "fluent-emoji-flat" => true,
            "twemoji" => true,
            "emojione" => true,
            "noto" => true,
            "cryptocurrency-color" => true,
            "token-branded" => true,
            "unjs" => true,
            _ => false
        };
    }

    private static List<IconPresetEntry> BuildEntries()
    {
        return new()
        {
            // Custom Neon Pack
            new("Neon Play", "neon_play", "Media", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Neon Pause", "neon_pause", "Media", Color.FromRgb(0x00, 0xC8, 0xFF)),
            new("Neon Next", "neon_next", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
            new("Neon Spotify", "neon_spotify", "Apps", Color.FromRgb(0x1D, 0xB9, 0x54)),
            new("Neon Discord", "neon_discord", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
            new("Neon Chrome", "neon_chrome", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Neon OBS", "neon_obs", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Neon YouTube", "neon_youtube", "Apps", Color.FromRgb(0xFF, 0x3D, 0x3D)),
            new("Neon Folder", "neon_folder", "Apps", Color.FromRgb(0xFF, 0xC1, 0x07)),
            new("Neon Mic", "neon_mic", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Neon Mic Mute", "neon_mic_mute", "Audio", Color.FromRgb(0xFF, 0x5C, 0x5C)),
            new("Neon Volume", "neon_volume", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("Neon Panels", "neon_panels", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
            new("Neon Geometric", "neon_geometric", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
            new("Neon Wall Light ON", "neon_wall_light_on", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
            new("Neon Wall Light OFF", "neon_wall_light_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
            new("Neon Case Light ON", "neon_case_light_on", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
            new("Neon Case Light OFF", "neon_case_light_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
            new("Neon Lightbulb ON", "neon_bulb_on", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
            new("Neon Lightbulb OFF", "neon_bulb_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
            new("Neon Monitor", "neon_monitor", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
            new("Neon Power", "neon_power", "System", Color.FromRgb(0xFF, 0x7B, 0x39)),

            // Custom Material 3D Pack
            new("3D Play/Pause", "material_playpause", "Media", Color.FromRgb(0x00, 0xC8, 0xFF)),
            new("3D Next Track", "material_next", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
            new("3D Prev Track", "material_prev", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
            new("3D Home", "material_home", "System", Color.FromRgb(0x00, 0xB4, 0xD8)),
            new("3D Settings", "material_settings", "System", Color.FromRgb(0x7A, 0x87, 0xFF)),
            new("3D Gamepad", "material_gamepad", "Creative", Color.FromRgb(0x69, 0xF0, 0xAE)),
            new("3D Power", "material_power", "System", Color.FromRgb(0xFF, 0x7B, 0x39)),
            new("3D Webcam", "material_webcam", "Streaming", Color.FromRgb(0x5C, 0xD7, 0xFF)),
            new("3D Mic", "material_mic", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("3D Mic Mute", "material_mic_mute", "Audio", Color.FromRgb(0xFF, 0x5C, 0x5C)),
            new("3D Volume", "material_volume", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
            new("3D Volume Mute", "material_volume_mute", "Audio", Color.FromRgb(0xFF, 0x6E, 0x6E)),

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
