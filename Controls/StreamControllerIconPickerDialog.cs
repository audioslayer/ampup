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
                private readonly SegmentedControl _packPicker;
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
            
                    _packPicker = new SegmentedControl
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        AccentColor = ThemeManager.Accent,
                        Margin = new Thickness(10, 0, 0, 10)
                    };
                    _packPicker.AddSegment("All Packs", "All");
                    _packPicker.AddSegment("Neon Pack", "Neon");
                    _packPicker.AddSegment("3D Material", "Material3D");
                    _packPicker.AddSegment("8-Bit Retro", "Retro");
                    _packPicker.AddSegment("Built-in", "BuiltIn");
                    _packPicker.SelectedIndex = 0;
                    _packPicker.SelectionChanged += (_, _) => RefreshLocalGrid();
            
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
            
                    // Two dedicated rows instead of stuffing both segmented
                    // controls + upload button on one line. Each picker now
                    // has full horizontal breathing room.
                    //
                    //   Row 1 — CATEGORY label + segmented control (full width)
                    //   Row 2 — PACK label + segmented control + Upload button
                    _categoryPicker.Margin = new Thickness(0);
                    _packPicker.Margin = new Thickness(0);

                    TextBlock MakeFilterLabel(string text) => new()
                    {
                        Text = text,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
                        Margin = new Thickness(2, 0, 0, 4),
                    };

                    var categoryRow = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(0, 0, 0, 10),
                    };
                    categoryRow.Children.Add(MakeFilterLabel("CATEGORY"));
                    categoryRow.Children.Add(_categoryPicker);
                    header.Children.Add(categoryRow);

                    var packRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                    var uploadButton = BuildUploadButton();
                    DockPanel.SetDock(uploadButton, Dock.Right);
                    packRow.Children.Add(uploadButton);

                    var packStack = new StackPanel { Orientation = Orientation.Vertical };
                    packStack.Children.Add(MakeFilterLabel("ICON PACK"));
                    packStack.Children.Add(_packPicker);
                    packRow.Children.Add(packStack);
                    header.Children.Add(packRow);
            
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
                    string pack = _packPicker.SelectedTag as string ?? "All";
                    string query = _searchBox.Text.Trim();
            
                    var filtered = _entries.Where(e =>
                            (category == "All" || e.Category == category) &&
                            (pack == "All" || 
                             (pack == "Neon" && (e.Kind.StartsWith("neon_") || e.Kind.StartsWith("synthwave_") || e.Kind.StartsWith("cyber_") || e.Kind.StartsWith("fx_"))) ||
                             (pack == "Material3D" && e.Kind.StartsWith("material_")) || 
                             (pack == "Retro" && e.Kind.StartsWith("retro_")) ||
                             (pack == "BuiltIn" && !e.Kind.StartsWith("neon_") && !e.Kind.StartsWith("material_") && !e.Kind.StartsWith("synthwave_") && !e.Kind.StartsWith("cyber_") && !e.Kind.StartsWith("retro_") && !e.Kind.StartsWith("fx_"))) &&
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
                    else if (entry.Kind.StartsWith("neon_") || entry.Kind.StartsWith("material_") || entry.Kind.StartsWith("synthwave_") || entry.Kind.StartsWith("cyber_") || entry.Kind.StartsWith("retro_") || entry.Kind.StartsWith("fx_"))
                    {
                        string filename = entry.Kind switch
                        {
                            "neon_play" => "play_button_neon.jpg",
                            "neon_pause" => "pause_button_neon.jpg",
                            "neon_next" => "next_track_neon.jpg",
                            "neon_spotify" => "spotify_neon.jpg",
                            "neon_discord" => "discord_neon.jpg",
                            "neon_discord_mute" => "neon_discord_mute.jpg",
                            "neon_chrome" => "neon_chrome.jpg",
                            "neon_obs" => "neon_obs.jpg",
                            "neon_youtube" => "neon_youtube.jpg",
                            "neon_folder" => "neon_folder.jpg",
                            "neon_twitch" => "neon_twitch.jpg",
                            "neon_brightness_up" => "neon_brightness_up.jpg",
                            "retro_twitch" => "retro_twitch.jpg",
                            "retro_discord" => "retro_discord.jpg",
                            "retro_spotify" => "retro_spotify.jpg",
                            "retro_brightness_up" => "retro_brightness_up.jpg",
                            "retro_snes_coin" => "retro_snes_coin.gif",
                            "neon_power" => "neon_power.jpg",
                            "neon_fastforward" => "neon_fastforward.jpg",
                            "neon_rewind" => "neon_rewind.jpg",
                            "neon_shuffle" => "neon_shuffle.jpg",
                            "neon_repeat" => "neon_repeat.jpg",
                            "neon_record" => "neon_record.jpg",
                            "neon_stop" => "neon_stop.jpg",
                            "neon_eject" => "neon_eject.jpg",
                            "neon_led_strip" => "neon_led_strip.jpg",
                            "neon_desk_lamp" => "neon_desk_lamp.jpg",
                            "neon_floor_lamp" => "neon_floor_lamp.jpg",
                            "neon_light_bars" => "neon_light_bars.jpg",
                            "neon_sconces" => "neon_sconces.jpg",
                            "neon_table_lamp" => "neon_table_lamp.jpg",
                            "neon_pc_fan" => "neon_pc_fan.jpg",
                            "neon_lava_lamp" => "neon_lava_lamp.jpg",
                            "neon_ring_light" => "neon_ring_light.jpg",
                            "neon_tv_backlight" => "neon_tv_backlight.jpg",
                            "neon_neon_sign" => "neon_neon_sign.jpg",
                            "neon_ceiling_fan" => "neon_ceiling_fan.jpg",
                            "neon_triangles" => "neon_triangles.jpg",
                            "neon_lock" => "neon_lock.jpg",
                            "neon_sleep" => "neon_sleep.jpg",
                            "neon_taskmgr" => "neon_taskmgr.jpg",
                            "neon_terminal" => "neon_terminal.jpg",
                            "neon_calculator" => "neon_calculator.jpg",
                            "neon_restart" => "neon_restart.jpg",
                            "neon_network" => "neon_network.jpg",
                            "neon_bluetooth" => "neon_bluetooth.jpg",
                            "neon_space_1" => "neon_space_1.jpg",
                            "neon_space_2" => "neon_space_2.jpg",
                            "neon_space_3" => "neon_space_3.jpg",
                            "neon_space_4" => "neon_space_4.jpg",
                            "synthwave_play" => "synthwave_play.jpg",
                            "synthwave_pause" => "synthwave_pause.jpg",
                            "cyber_play" => "cyber_play.jpg",
                            "cyber_pause" => "cyber_pause.jpg",
                            "retro_play" => "retro_play.jpg",
                            "retro_pause" => "retro_pause.jpg",
                            "retro_gamepad" => "retro_gamepad.jpg",
                            "retro_home" => "retro_home.jpg",
                            "retro_volume" => "retro_volume.jpg",
                            "retro_mic" => "retro_mic.jpg",
                            "retro_monitor" => "retro_monitor.jpg",
                            "retro_lightbulb" => "retro_lightbulb.jpg",
                            "retro_next" => "retro_next.jpg",
                            "retro_prev" => "retro_prev.jpg",
                            "retro_stop" => "retro_stop.jpg",
                            "retro_power" => "retro_power.jpg",
                            "retro_settings" => "retro_settings.jpg",
                            "retro_folder" => "retro_folder.jpg",
                            "retro_lock" => "retro_lock.jpg",
                            "retro_sleep" => "retro_sleep.jpg",
                            "neon_mic" => "mic_active_neon.jpg",
                            "neon_mic_mute" => "mic_mute_neon.jpg",
                            "neon_game_mute" => "neon_game_mute.jpg",
                            "neon_volume" => "speaker_volume_neon.jpg",
                            "neon_volume_up" => "neon_volume_up.jpg",
                            "neon_volume_down" => "neon_volume_down.jpg",
                            "neon_panels" => "light_panel_white_cyan.jpg",
                            "neon_geometric" => "light_geometric_cyan_purple.jpg",
                            "neon_wall_light_on" => "neon_wall_light_on.jpg",
                            "neon_wall_light_off" => "neon_wall_light_off.jpg",
                            "neon_case_light_on" => "neon_case_light_on.jpg",
                            "neon_case_light_off" => "neon_case_light_off.jpg",
                            "neon_bulb_on" => "neon_bulb_on.jpg",
                            "neon_bulb_off" => "neon_bulb_off.jpg",
                            "neon_light_group" => "neon_light_group.jpg",
                            "neon_room_office" => "neon_room_office.jpg",
                            "neon_room_bedroom" => "neon_room_bedroom.jpg",
                            "neon_room_living" => "neon_room_living.jpg",
                            "neon_room_kitchen" => "neon_room_kitchen.jpg",
                            "neon_room_arcade" => "neon_room_arcade.jpg",
                            "neon_room_bathroom" => "neon_room_bathroom.jpg",
                            "neon_room_exterior" => "neon_room_exterior.jpg",
                            "neon_monitor" => "neon_monitor.jpg",
                            "neon_brightness_down" => "neon_brightness_down.jpg",
                            "retro_brightness_down" => "retro_brightness_down.jpg",
                            "neon_camera" => "neon_camera.jpg",
                            "retro_camera" => "retro_camera.jpg",
                            "neon_copy" => "neon_copy.jpg",
                            "neon_paste" => "neon_paste.jpg",
                            "retro_paste" => "retro_paste.jpg",
                            "neon_mixer" => "neon_mixer.jpg",
                            "retro_mixer" => "retro_mixer.jpg",
                            "neon_clapper" => "neon_clapper.jpg",
                            "retro_clapper" => "retro_clapper.jpg",
                            "neon_photoshop" => "neon_photoshop.jpg",
                            "retro_photoshop" => "retro_photoshop.jpg",
                            "neon_steam" => "neon_steam.jpg",
                            "retro_steam" => "retro_steam.jpg",
                            "neon_premiere" => "neon_premiere.jpg",
                            "retro_premiere" => "retro_premiere.jpg",
                            "retro_chrome" => "retro_chrome.jpg",
                            "neon_camera_off" => "neon_camera_off.jpg",
                            "retro_camera_off" => "retro_camera_off.jpg",
                            "retro_copy" => "retro_copy.jpg",
                            "retro_volume_mute" => "retro_volume_mute.jpg",
                            "retro_browser" => "retro_browser.jpg",
                            "retro_mail" => "retro_mail.jpg",
                            "retro_edit" => "retro_edit.jpg",
                            "retro_cut" => "retro_cut.jpg",
                            "retro_undo" => "retro_undo.jpg",
                            "retro_redo" => "retro_redo.jpg",
                            "retro_save" => "retro_save.jpg",
                            "retro_search" => "retro_search.jpg",
                            "retro_fullscreen" => "retro_fullscreen.jpg",
                            "retro_minimize" => "retro_minimize.jpg",
                            "retro_maximize" => "retro_maximize.jpg",
                            "retro_logout" => "retro_logout.jpg",
                            "retro_shutdown" => "retro_shutdown.jpg",
                            "retro_reboot" => "retro_reboot.jpg",
                            "retro_folder_open" => "retro_folder_open.jpg",
                            "retro_file" => "retro_file.jpg",
                            "retro_terminal" => "retro_terminal.jpg",
                            "retro_chat" => "retro_chat.jpg",
                            "retro_teams" => "retro_teams.jpg",
                            "retro_zoom" => "retro_zoom.jpg",
                            "retro_vlc" => "retro_vlc.jpg",
                            "retro_netflix" => "retro_netflix.jpg",
                            "retro_screenshot" => "retro_screenshot.jpg",
                            "retro_stream_start" => "retro_stream_start.jpg",
                            "neon_browser" => "neon_browser.jpg",
                            "neon_mail" => "neon_mail.jpg",
                            "neon_save" => "neon_save.jpg",
                            "neon_search" => "neon_search.jpg",
                            "neon_volume_mute" => "neon_volume_mute.jpg",
                            "neon_chat" => "neon_chat.jpg",
                            "neon_terminal_window" => "neon_terminal_window.jpg",
                            "neon_teams" => "neon_teams.jpg",
                            "neon_zoom" => "neon_zoom.jpg",
                            "neon_slack" => "neon_slack.jpg",
                            "retro_slack" => "retro_slack.jpg",
                            "neon_github" => "neon_github.jpg",
                            "retro_github" => "retro_github.jpg",
                            "neon_notion" => "neon_notion.jpg",
                            "neon_obsidian" => "neon_obsidian.jpg",
                            "neon_meet" => "neon_meet.jpg",
                            "neon_edge" => "neon_edge.jpg",
                            "neon_firefox" => "neon_firefox.jpg",
                            "neon_brave" => "neon_brave.jpg",
                            "neon_files" => "neon_files.jpg",
                            "retro_files" => "retro_files.jpg",
                            "neon_vlc" => "neon_vlc.jpg",
                            "neon_netflix" => "neon_netflix.jpg",
                            "neon_screenshot" => "neon_screenshot.jpg",
                            "neon_vscode" => "neon_vscode.jpg",
                            "retro_vscode" => "retro_vscode.jpg",
                            "neon_playnite" => "neon_playnite.jpg",
                            "retro_playnite" => "retro_playnite.jpg",
                            "neon_gemini" => "neon_gemini.jpg",
                            "retro_gemini" => "retro_gemini.jpg",
                            "neon_explorer" => "neon_explorer.jpg",
                            "retro_explorer" => "retro_explorer.jpg",
                            "neon_phone_link" => "neon_phone_link.jpg",
                            "retro_phone_link" => "retro_phone_link.jpg",
                            "neon_dude" => "neon_dude.jpg",
                            "retro_dude" => "retro_dude.jpg",
                            "neon_outlook" => "neon_outlook.jpg",
                            "retro_outlook" => "retro_outlook.jpg",
                            "neon_gmail" => "neon_gmail.jpg",
                            "retro_gmail" => "retro_gmail.jpg",
                            "neon_wolfden" => "neon_wolfden.jpg",
                            "retro_wolfden" => "retro_wolfden.jpg",
                            "neon_wolfbyte" => "neon_wolfbyte.jpg",
                            "retro_wolfbyte" => "retro_wolfbyte.jpg",
                            "neon_wolfvault" => "neon_wolfvault.jpg",
                            "retro_wolfvault" => "retro_wolfvault.jpg",
                            "neon_wolfhome" => "neon_wolfhome.jpg",
                            "retro_wolfhome" => "retro_wolfhome.jpg",
                            "neon_mission_control" => "neon_mission_control.jpg",
                            "retro_mission_control" => "retro_mission_control.jpg",
                            "neon_nanoarcade" => "neon_nanoarcade.jpg",
                            "retro_nanoarcade" => "retro_nanoarcade.jpg",
                            "neon_churchportals" => "neon_churchportals.jpg",
                            "retro_churchportals" => "retro_churchportals.jpg",
                            _ => entry.Kind.StartsWith("fx_") ? entry.Kind + ".jpg" : ""
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
                        if (entry.Kind.StartsWith("neon_") || entry.Kind.StartsWith("material_") || entry.Kind.StartsWith("synthwave_") || entry.Kind.StartsWith("cyber_") || entry.Kind.StartsWith("retro_"))
                        {
                            string filename = entry.Kind switch
                            {
                                "neon_play" => "play_button_neon.jpg",
                                "neon_pause" => "pause_button_neon.jpg",
                                "neon_next" => "next_track_neon.jpg",
                                "neon_spotify" => "spotify_neon.jpg",
                                "neon_discord" => "discord_neon.jpg",
                                "neon_discord_mute" => "neon_discord_mute.jpg",
                                "neon_chrome" => "neon_chrome.jpg",
                                "neon_obs" => "neon_obs.jpg",
                                "neon_youtube" => "neon_youtube.jpg",
                                "neon_folder" => "neon_folder.jpg",
                            "neon_twitch" => "neon_twitch.jpg",
                            "neon_brightness_up" => "neon_brightness_up.jpg",
                            "retro_twitch" => "retro_twitch.jpg",
                            "retro_discord" => "retro_discord.jpg",
                            "retro_spotify" => "retro_spotify.jpg",
                            "retro_brightness_up" => "retro_brightness_up.jpg",
                            "retro_snes_coin" => "retro_snes_coin.gif",
                                "neon_power" => "neon_power.jpg",
                                "neon_fastforward" => "neon_fastforward.jpg",
                                "neon_rewind" => "neon_rewind.jpg",
                                "neon_shuffle" => "neon_shuffle.jpg",
                                "neon_repeat" => "neon_repeat.jpg",
                                "neon_record" => "neon_record.jpg",
                                "neon_stop" => "neon_stop.jpg",
                                "neon_eject" => "neon_eject.jpg",
                                "neon_led_strip" => "neon_led_strip.jpg",
                                "neon_desk_lamp" => "neon_desk_lamp.jpg",
                                "neon_floor_lamp" => "neon_floor_lamp.jpg",
                                "neon_light_bars" => "neon_light_bars.jpg",
                                "neon_sconces" => "neon_sconces.jpg",
                                "neon_table_lamp" => "neon_table_lamp.jpg",
                                "neon_pc_fan" => "neon_pc_fan.jpg",
                                "neon_lava_lamp" => "neon_lava_lamp.jpg",
                                "neon_ring_light" => "neon_ring_light.jpg",
                                "neon_tv_backlight" => "neon_tv_backlight.jpg",
                                "neon_neon_sign" => "neon_neon_sign.jpg",
                                "neon_ceiling_fan" => "neon_ceiling_fan.jpg",
                                "neon_triangles" => "neon_triangles.jpg",
                                "neon_lock" => "neon_lock.jpg",
                                "neon_sleep" => "neon_sleep.jpg",
                                "neon_taskmgr" => "neon_taskmgr.jpg",
                                "neon_terminal" => "neon_terminal.jpg",
                                "neon_calculator" => "neon_calculator.jpg",
                                "neon_restart" => "neon_restart.jpg",
                                "neon_network" => "neon_network.jpg",
                                "neon_bluetooth" => "neon_bluetooth.jpg",
                                "neon_space_1" => "neon_space_1.jpg",
                                "neon_space_2" => "neon_space_2.jpg",
                                "neon_space_3" => "neon_space_3.jpg",
                                "neon_space_4" => "neon_space_4.jpg",
                                "synthwave_play" => "synthwave_play.jpg",
                                "synthwave_pause" => "synthwave_pause.jpg",
                                "cyber_play" => "cyber_play.jpg",
                                "cyber_pause" => "cyber_pause.jpg",
                                "retro_play" => "retro_play.jpg",
                                "retro_pause" => "retro_pause.jpg",
                                "retro_gamepad" => "retro_gamepad.jpg",
                                "retro_home" => "retro_home.jpg",
                                "retro_volume" => "retro_volume.jpg",
                                "retro_mic" => "retro_mic.jpg",
                                "retro_monitor" => "retro_monitor.jpg",
                                "retro_lightbulb" => "retro_lightbulb.jpg",
                            "retro_next" => "retro_next.jpg",
                            "retro_prev" => "retro_prev.jpg",
                            "retro_stop" => "retro_stop.jpg",
                            "retro_power" => "retro_power.jpg",
                            "retro_settings" => "retro_settings.jpg",
                            "retro_folder" => "retro_folder.jpg",
                            "retro_lock" => "retro_lock.jpg",
                            "retro_sleep" => "retro_sleep.jpg",
                                "neon_mic" => "mic_active_neon.jpg",
                                "neon_mic_mute" => "mic_mute_neon.jpg",
                                "neon_game_mute" => "neon_game_mute.jpg",
                                "neon_volume" => "speaker_volume_neon.jpg",
                                "neon_volume_up" => "neon_volume_up.jpg",
                                "neon_volume_down" => "neon_volume_down.jpg",
                                "neon_panels" => "light_panel_white_cyan.jpg",
                                "neon_geometric" => "light_geometric_cyan_purple.jpg",
                                "neon_wall_light_on" => "neon_wall_light_on.jpg",
                                "neon_wall_light_off" => "neon_wall_light_off.jpg",
                                "neon_case_light_on" => "neon_case_light_on.jpg",
                                "neon_case_light_off" => "neon_case_light_off.jpg",
                                "neon_bulb_on" => "neon_bulb_on.jpg",
                                "neon_bulb_off" => "neon_bulb_off.jpg",
                                "neon_light_group" => "neon_light_group.jpg",
                                "neon_room_office" => "neon_room_office.jpg",
                                "neon_room_bedroom" => "neon_room_bedroom.jpg",
                                "neon_room_living" => "neon_room_living.jpg",
                                "neon_room_kitchen" => "neon_room_kitchen.jpg",
                                "neon_room_arcade" => "neon_room_arcade.jpg",
                                "neon_room_bathroom" => "neon_room_bathroom.jpg",
                                "neon_room_exterior" => "neon_room_exterior.jpg",
                                "neon_monitor" => "neon_monitor.jpg",
                                "neon_brightness_down" => "neon_brightness_down.jpg",
                                "retro_brightness_down" => "retro_brightness_down.jpg",
                                "neon_camera" => "neon_camera.jpg",
                                "retro_camera" => "retro_camera.jpg",
                                "neon_copy" => "neon_copy.jpg",
                                "neon_paste" => "neon_paste.jpg",
                                "retro_paste" => "retro_paste.jpg",
                                "neon_mixer" => "neon_mixer.jpg",
                                "retro_mixer" => "retro_mixer.jpg",
                                "neon_clapper" => "neon_clapper.jpg",
                                "retro_clapper" => "retro_clapper.jpg",
                                "neon_photoshop" => "neon_photoshop.jpg",
                                "retro_photoshop" => "retro_photoshop.jpg",
                                "neon_steam" => "neon_steam.jpg",
                                "retro_steam" => "retro_steam.jpg",
                                "neon_premiere" => "neon_premiere.jpg",
                                "retro_premiere" => "retro_premiere.jpg",
                                "retro_chrome" => "retro_chrome.jpg",
                                "neon_camera_off" => "neon_camera_off.jpg",
                                "retro_camera_off" => "retro_camera_off.jpg",
                                "retro_copy" => "retro_copy.jpg",
                            "retro_volume_mute" => "retro_volume_mute.jpg",
                            "retro_browser" => "retro_browser.jpg",
                            "retro_mail" => "retro_mail.jpg",
                            "retro_edit" => "retro_edit.jpg",
                            "retro_cut" => "retro_cut.jpg",
                            "retro_undo" => "retro_undo.jpg",
                            "retro_redo" => "retro_redo.jpg",
                            "retro_save" => "retro_save.jpg",
                            "retro_search" => "retro_search.jpg",
                            "retro_fullscreen" => "retro_fullscreen.jpg",
                            "retro_minimize" => "retro_minimize.jpg",
                            "retro_maximize" => "retro_maximize.jpg",
                            "retro_logout" => "retro_logout.jpg",
                            "retro_shutdown" => "retro_shutdown.jpg",
                            "retro_reboot" => "retro_reboot.jpg",
                            "retro_folder_open" => "retro_folder_open.jpg",
                            "retro_file" => "retro_file.jpg",
                            "retro_terminal" => "retro_terminal.jpg",
                            "retro_chat" => "retro_chat.jpg",
                            "retro_teams" => "retro_teams.jpg",
                            "retro_zoom" => "retro_zoom.jpg",
                            "retro_vlc" => "retro_vlc.jpg",
                            "retro_netflix" => "retro_netflix.jpg",
                            "retro_screenshot" => "retro_screenshot.jpg",
                            "retro_stream_start" => "retro_stream_start.jpg",
                            "neon_browser" => "neon_browser.jpg",
                            "neon_mail" => "neon_mail.jpg",
                            "neon_save" => "neon_save.jpg",
                            "neon_search" => "neon_search.jpg",
                            "neon_volume_mute" => "neon_volume_mute.jpg",
                            "neon_chat" => "neon_chat.jpg",
                            "neon_terminal_window" => "neon_terminal_window.jpg",
                            "neon_teams" => "neon_teams.jpg",
                            "neon_zoom" => "neon_zoom.jpg",
                            "neon_slack" => "neon_slack.jpg",
                            "retro_slack" => "retro_slack.jpg",
                            "neon_gmail" => "neon_gmail.jpg",
                            "retro_gmail" => "retro_gmail.jpg",
                            "neon_outlook" => "neon_outlook.jpg",
                            "retro_outlook" => "retro_outlook.jpg",
                            "neon_vscode" => "neon_vscode.jpg",
                            "retro_vscode" => "retro_vscode.jpg",
                            "neon_github" => "neon_github.jpg",
                            "retro_github" => "retro_github.jpg",
                            "neon_notion" => "neon_notion.jpg",
                            "neon_obsidian" => "neon_obsidian.jpg",
                            "neon_meet" => "neon_meet.jpg",
                            "neon_edge" => "neon_edge.jpg",
                            "neon_firefox" => "neon_firefox.jpg",
                            "neon_brave" => "neon_brave.jpg",
                            "neon_files" => "neon_files.jpg",
                            "retro_files" => "retro_files.jpg",
                            "neon_vlc" => "neon_vlc.jpg",
                            "neon_netflix" => "neon_netflix.jpg",
                            "neon_screenshot" => "neon_screenshot.jpg",
                                _ => entry.Kind.StartsWith("fx_") ? entry.Kind + ".jpg" : ""
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
                        new("Neon Discord Mute", "neon_discord_mute", "Apps", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Chrome", "neon_chrome", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon OBS", "neon_obs", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon YouTube", "neon_youtube", "Apps", Color.FromRgb(0xFF, 0x3D, 0x3D)),
                        new("Neon Folder", "neon_folder", "Apps", Color.FromRgb(0xFF, 0xC1, 0x07)),
                        new("Neon Twitch", "neon_twitch", "Apps", Color.FromRgb(0x91, 0x46, 0xFF)),
                        new("Neon Fast Forward", "neon_fastforward", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
                        new("Neon Rewind", "neon_rewind", "Media", Color.FromRgb(0x73, 0xA5, 0xFF)),
                        new("Neon Shuffle", "neon_shuffle", "Media", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Repeat", "neon_repeat", "Media", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Record", "neon_record", "Media", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Stop", "neon_stop", "Media", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Eject", "neon_eject", "Media", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Mic", "neon_mic", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Mic Mute", "neon_mic_mute", "Audio", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Game Mute", "neon_game_mute", "Audio", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Volume", "neon_volume", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Vol Up", "neon_volume_up", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Vol Down", "neon_volume_down", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Panels", "neon_panels", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon Geometric", "neon_geometric", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Triangles", "neon_triangles", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon Wall Light ON", "neon_wall_light_on", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon Wall Light OFF", "neon_wall_light_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
                        new("Neon Case Light ON", "neon_case_light_on", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Case Light OFF", "neon_case_light_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
                        new("Neon Lightbulb ON", "neon_bulb_on", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Lightbulb OFF", "neon_bulb_off", "Creative", Color.FromRgb(0x55, 0x55, 0x55)),
                        new("Neon Desk Lamp", "neon_desk_lamp", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Floor Lamp", "neon_floor_lamp", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Light Bars", "neon_light_bars", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Sconces", "neon_sconces", "Creative", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon Table Lamp", "neon_table_lamp", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon PC Fan", "neon_pc_fan", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Lava Lamp", "neon_lava_lamp", "Creative", Color.FromRgb(0xFF, 0x5A, 0xA5)),
                        new("Neon Ring Light", "neon_ring_light", "Creative", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon TV Backlight", "neon_tv_backlight", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Sign", "neon_neon_sign", "Creative", Color.FromRgb(0xFF, 0x5A, 0xA5)),
                        new("Neon LED Strip", "neon_led_strip", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Light Group", "neon_light_group", "Creative", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Neon Ceiling Fan", "neon_ceiling_fan", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Lock", "neon_lock", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Sleep", "neon_sleep", "System", Color.FromRgb(0x7A, 0x87, 0xFF)),
                        new("Neon Task Manager", "neon_taskmgr", "System", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon Terminal", "neon_terminal", "System", Color.FromRgb(0x69, 0xF0, 0xAE)),
                        new("Neon Calculator", "neon_calculator", "System", Color.FromRgb(0x00, 0xB4, 0xD8)),
                        new("Neon Restart", "neon_restart", "System", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Neon Network", "neon_network", "System", Color.FromRgb(0x26, 0xC6, 0xDA)),
                        new("Neon Bluetooth", "neon_bluetooth", "System", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Synthwave Play", "synthwave_play", "Media", Color.FromRgb(0xFF, 0x00, 0x7A)),
                        new("Synthwave Pause", "synthwave_pause", "Media", Color.FromRgb(0xFF, 0x00, 0x7A)),
                        new("Cyber Play", "cyber_play", "Media", Color.FromRgb(0x00, 0xFF, 0x00)),
                        new("Cyber Pause", "cyber_pause", "Media", Color.FromRgb(0x00, 0xFF, 0x00)),
                        new("Retro Play", "retro_play", "Media", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Pause", "retro_pause", "Media", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Gamepad", "retro_gamepad", "Creative", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Retro Home", "retro_home", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Volume", "retro_volume", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Mic", "retro_mic", "Audio", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Monitor", "retro_monitor", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Lightbulb", "retro_lightbulb", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Next", "retro_next", "Media", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Prev", "retro_prev", "Media", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Stop", "retro_stop", "Media", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Power", "retro_power", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Settings", "retro_settings", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Folder", "retro_folder", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Lock", "retro_lock", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Sleep", "retro_sleep", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Twitch", "retro_twitch", "Apps", Color.FromRgb(0x91, 0x46, 0xFF)),
                        new("Retro Discord", "retro_discord", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Retro Spotify", "retro_spotify", "Apps", Color.FromRgb(0x1D, 0xB9, 0x54)),
                        new("Retro Brightness Up", "retro_brightness_up", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("SNES Coin (animated)", "retro_snes_coin", "Creative", Color.FromRgb(0xFF, 0xC8, 0x28)),
                        new("Neon Office", "neon_room_office", "Creative", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Neon Bedroom", "neon_room_bedroom", "Creative", Color.FromRgb(0x9B, 0x8C, 0xFF)),
                        new("Neon Living Room", "neon_room_living", "Creative", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Kitchen", "neon_room_kitchen", "Creative", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Arcade", "neon_room_arcade", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon Bathroom", "neon_room_bathroom", "Creative", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon Exterior", "neon_room_exterior", "Creative", Color.FromRgb(0xFF, 0x8A, 0x3D)),
                        new("Neon Monitor", "neon_monitor", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Neon Power", "neon_power", "System", Color.FromRgb(0xFF, 0x7B, 0x39)),
            
                        // Custom Material 3D Pack
            
                        new("Neon Brightness Down", "neon_brightness_down", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Brightness Down", "retro_brightness_down", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Camera", "neon_camera", "Streaming", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Camera", "retro_camera", "Streaming", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Copy", "neon_copy", "System", Color.FromRgb(0xFF, 0xC1, 0x07)),
                        new("Neon Paste", "neon_paste", "System", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Paste", "retro_paste", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Mixer Sliders", "neon_mixer", "Audio", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Mixer Sliders", "retro_mixer", "Audio", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Clapperboard", "neon_clapper", "Streaming", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Clapperboard", "retro_clapper", "Streaming", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Photoshop", "neon_photoshop", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Photoshop", "retro_photoshop", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Steam", "neon_steam", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Steam", "retro_steam", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Premiere Pro", "neon_premiere", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro Premiere Pro", "retro_premiere", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Chrome", "retro_chrome", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Camera OFF", "neon_camera_off", "Streaming", Color.FromRgb(0xFF, 0x17, 0x44)),
                        new("Retro Camera OFF", "retro_camera_off", "Streaming", Color.FromRgb(0xFF, 0x17, 0x44)),
                        new("Retro Copy", "retro_copy", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),

                        new("Retro Volume Mute", "retro_volume_mute", "Audio", Color.FromRgb(0xFF, 0x6E, 0x6E)),
                        new("Retro Browser", "retro_browser", "Apps", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Mail", "retro_mail", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Edit", "retro_edit", "Creative", Color.FromRgb(0xFF, 0xC1, 0x07)),
                        new("Retro Cut", "retro_cut", "Creative", Color.FromRgb(0xFF, 0x8A, 0x65)),
                        new("Retro Undo", "retro_undo", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Redo", "retro_redo", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Save", "retro_save", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Search", "retro_search", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Fullscreen", "retro_fullscreen", "System", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Minimize", "retro_minimize", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro Maximize", "retro_maximize", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Logout", "retro_logout", "System", Color.FromRgb(0xFF, 0x8A, 0x65)),
                        new("Retro Shutdown", "retro_shutdown", "System", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Retro Reboot", "retro_reboot", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro Folder Open", "retro_folder_open", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro File", "retro_file", "Apps", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Terminal", "retro_terminal", "System", Color.FromRgb(0x69, 0xF0, 0xAE)),
                        new("Retro Chat", "retro_chat", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Retro Teams", "retro_teams", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Retro Zoom", "retro_zoom", "Apps", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Retro VLC", "retro_vlc", "Apps", Color.FromRgb(0xFF, 0x8A, 0x3D)),
                        new("Retro Netflix", "retro_netflix", "Apps", Color.FromRgb(0xFF, 0x44, 0x44)),
                        new("Retro Screenshot", "retro_screenshot", "Streaming", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("Retro Stream Start", "retro_stream_start", "Streaming", Color.FromRgb(0xFF, 0x5C, 0x5C)),
                        new("Neon Browser", "neon_browser", "Apps", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon Mail", "neon_mail", "Apps", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Neon Save", "neon_save", "System", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon Search", "neon_search", "System", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Volume Mute", "neon_volume_mute", "Audio", Color.FromRgb(0xFF, 0x6E, 0x6E)),
                        new("Neon Terminal Chat", "neon_chat", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Neon Terminal Window", "neon_terminal_window", "System", Color.FromRgb(0x69, 0xF0, 0xAE)),
                        new("Neon Teams", "neon_teams", "Apps", Color.FromRgb(0x72, 0x89, 0xDA)),
                        new("Neon Zoom", "neon_zoom", "Apps", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon Slack", "neon_slack", "Apps", Color.FromRgb(0xFF, 0x66, 0xC4)),
                        new("Retro Slack", "retro_slack", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Gmail", "neon_gmail", "Apps", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Retro Gmail", "retro_gmail", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon Outlook", "neon_outlook", "Apps", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Retro Outlook", "retro_outlook", "Apps", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon VS Code", "neon_vscode", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro VS Code", "retro_vscode", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon GitHub", "neon_github", "Apps", Color.FromRgb(0xD6, 0xDA, 0xFF)),
                        new("Retro GitHub", "retro_github", "Apps", Color.FromRgb(0xD6, 0xDA, 0xFF)),
                        new("Neon Notion", "neon_notion", "Apps", Color.FromRgb(0xF5, 0xF7, 0xFF)),
                        new("Neon Obsidian", "neon_obsidian", "Apps", Color.FromRgb(0xB8, 0x74, 0xFF)),
                        new("Neon Meet", "neon_meet", "Apps", Color.FromRgb(0x50, 0xFF, 0x78)),
                        new("Neon Edge", "neon_edge", "Apps", Color.FromRgb(0x3C, 0xFF, 0xD2)),
                        new("Neon Firefox", "neon_firefox", "Apps", Color.FromRgb(0xFF, 0x78, 0x3C)),
                        new("Neon Brave", "neon_brave", "Apps", Color.FromRgb(0xFF, 0x78, 0x3C)),
                        new("Neon Files", "neon_files", "Apps", Color.FromRgb(0xFF, 0xD2, 0x50)),
                        new("Retro Files", "retro_files", "Apps", Color.FromRgb(0xFF, 0xD2, 0x50)),
                        new("Neon VLC", "neon_vlc", "Apps", Color.FromRgb(0xFF, 0x8A, 0x3D)),
                        new("Neon Netflix", "neon_netflix", "Apps", Color.FromRgb(0xFF, 0x44, 0x44)),
                        new("Neon Screenshot", "neon_screenshot", "Streaming", Color.FromRgb(0x5C, 0xD7, 0xFF)),
                        new("Neon Playnite", "neon_playnite", "Apps", Color.FromRgb(0x7B, 0x5C, 0xFF)),
                        new("Retro Playnite", "retro_playnite", "Apps", Color.FromRgb(0x7B, 0x5C, 0xFF)),
                        new("Neon Gemini", "neon_gemini", "Apps", Color.FromRgb(0x8A, 0x7C, 0xFF)),
                        new("Retro Gemini", "retro_gemini", "Apps", Color.FromRgb(0x8A, 0x7C, 0xFF)),
                        new("Neon File Explorer", "neon_explorer", "Apps", Color.FromRgb(0xFF, 0xC2, 0x47)),
                        new("Retro File Explorer", "retro_explorer", "Apps", Color.FromRgb(0xFF, 0xC2, 0x47)),
                        new("Neon Phone Link", "neon_phone_link", "Apps", Color.FromRgb(0x38, 0xD3, 0xFF)),
                        new("Retro Phone Link", "retro_phone_link", "Apps", Color.FromRgb(0x38, 0xD3, 0xFF)),
                        new("Neon The Dude", "neon_dude", "Apps", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Retro The Dude", "retro_dude", "Apps", Color.FromRgb(0xFF, 0x7B, 0x39)),
                        new("Neon WolfDen", "neon_wolfden", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Retro WolfDen", "retro_wolfden", "Apps", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("Neon WolfByte", "neon_wolfbyte", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Retro WolfByte", "retro_wolfbyte", "Apps", Color.FromRgb(0x00, 0xD0, 0xFF)),
                        new("Neon WolfVault", "neon_wolfvault", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Retro WolfVault", "retro_wolfvault", "Apps", Color.FromRgb(0xFF, 0xD7, 0x40)),
                        new("Neon WolfHome", "neon_wolfhome", "Apps", Color.FromRgb(0xFF, 0x9F, 0x43)),
                        new("Retro WolfHome", "retro_wolfhome", "Apps", Color.FromRgb(0xFF, 0x9F, 0x43)),
                        new("Neon Mission Control", "neon_mission_control", "Apps", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Retro Mission Control", "retro_mission_control", "Apps", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("Neon NanoArcade", "neon_nanoarcade", "Apps", Color.FromRgb(0xFF, 0x44, 0xAA)),
                        new("Retro NanoArcade", "retro_nanoarcade", "Apps", Color.FromRgb(0xFF, 0x44, 0xAA)),
                        new("Neon ChurchPortals", "neon_churchportals", "Apps", Color.FromRgb(0x69, 0xF0, 0xAE)),
                        new("Retro ChurchPortals", "retro_churchportals", "Apps", Color.FromRgb(0x69, 0xF0, 0xAE)),

                        // FX Pack — room-effect neon icons (PNG)
                        new("FX Aurora", "fx_aurora", "Creative", Color.FromRgb(0x69, 0xF0, 0xAE)),
                        new("FX Ocean", "fx_ocean", "Creative", Color.FromRgb(0x29, 0xB6, 0xF6)),
                        new("FX Starfield", "fx_starfield", "Creative", Color.FromRgb(0xB0, 0xBE, 0xC5)),
                        new("FX Plasma", "fx_plasma", "Creative", Color.FromRgb(0xE0, 0x40, 0xFB)),
                        new("FX Nebula Drift", "fx_nebuladrift", "Creative", Color.FromRgb(0x7C, 0x4D, 0xFF)),
                        new("FX Breathing Sync", "fx_breathingsync", "Creative", Color.FromRgb(0x90, 0xA4, 0xAE)),
                        new("FX Fire", "fx_fire", "Creative", Color.FromRgb(0xFF, 0x57, 0x22)),
                        new("FX Lava", "fx_lava", "Creative", Color.FromRgb(0xFF, 0x6B, 0x35)),
                        new("FX Lightning", "fx_lightning", "Creative", Color.FromRgb(0xFF, 0xEB, 0x3B)),
                        new("FX Police", "fx_police", "Creative", Color.FromRgb(0xF4, 0x43, 0x36)),
                        new("FX Scanner", "fx_scanner", "Creative", Color.FromRgb(0xF4, 0x43, 0x36)),
                        new("FX Matrix", "fx_matrix", "Creative", Color.FromRgb(0x00, 0xE6, 0x76)),
                        new("FX Color Wave", "fx_colorwave", "Creative", Color.FromRgb(0x00, 0xAC, 0xC1)),
                        new("FX Rainfall", "fx_rainfall", "Creative", Color.FromRgb(0x4F, 0xC3, 0xF7)),
                        new("FX Waterfall", "fx_waterfall", "Creative", Color.FromRgb(0x4D, 0xD0, 0xE1)),
                        new("FX Rainbow", "fx_rainbow", "Creative", Color.FromRgb(0xC5, 0x5D, 0xFF)),
                        new("FX Meteor", "fx_meteor", "Creative", Color.FromRgb(0xFF, 0x98, 0x00)),
                        new("FX Heartbeat", "fx_heartbeat", "Creative", Color.FromRgb(0xE9, 0x1E, 0x63)),

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
            