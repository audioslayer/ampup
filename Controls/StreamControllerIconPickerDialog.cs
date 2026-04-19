using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

public class StreamControllerIconPickerDialog : Window
{
    private readonly List<IconPresetEntry> _entries = BuildEntries();
    private readonly WrapPanel _grid = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _searchBox;
    private readonly SegmentedControl _categoryPicker;

    public string? SelectedIconKind { get; private set; }

    private sealed record IconPresetEntry(string Label, string Kind, string Category, Color Accent);

    public StreamControllerIconPickerDialog()
    {
        Title = "Choose Preset Icon";
        Width = 760;
        Height = 620;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
        _searchBox.TextChanged += (_, _) => RefreshGrid();

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
        _categoryPicker.SelectionChanged += (_, _) => RefreshGrid();

        BuildUi();
        RefreshGrid();
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
            Text = "PRESET ICONS",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent)
        };
        var close = new TextBlock
        {
            Text = "✕",
            FontSize = 16,
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            Cursor = Cursors.Hand
        };
        close.MouseLeftButtonDown += (_, _) => { DialogResult = false; Close(); };
        DockPanel.SetDock(close, Dock.Right);
        titleRow.Children.Add(close);
        titleRow.Children.Add(title);
        header.Children.Add(titleRow);

        header.Children.Add(new TextBlock
        {
            Text = "Uses the built-in Material icon pack already shipping with Amp Up, so you get Stream Deck-style preset symbols without increasing the app size much.",
            Foreground = (Brush)Application.Current.FindResource("TextSecBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

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
            Text = "Search icons...",
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

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(18, 0, 18, 18),
            Content = _grid
        };
        main.Children.Add(scroll);

        Content = outer;

        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }

    private void RefreshGrid()
    {
        _grid.Children.Clear();
        string category = _categoryPicker.SelectedTag as string ?? "All";
        string query = _searchBox.Text.Trim();

        var filtered = _entries.Where(e =>
                (category == "All" || e.Category == category) &&
                (string.IsNullOrWhiteSpace(query)
                    || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var entry in filtered)
            _grid.Children.Add(BuildTile(entry));
    }

    private UIElement BuildTile(IconPresetEntry entry)
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
            DialogResult = true;
            Close();
        };

        return card;
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
