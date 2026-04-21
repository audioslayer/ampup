using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AmpUp.Controls;

/// <summary>
/// 4-step wizard that walks the user through creating a Spotify app,
/// configuring the redirect URI (with a copy button so they don't type
/// it wrong), enabling Web API scope, and pasting the Client ID. Modal
/// returns WasSuccessful + ClientId for the caller to kick OAuth.
/// </summary>
public class SpotifySetupGuide : Window
{
    public string ClientId { get; private set; } = "";
    public bool WasSuccessful { get; private set; } = false;

    private int _currentStep = 0;
    private readonly List<UIElement> _stepPanels = new();
    private readonly List<Ellipse[]> _dotSets = new();
    private ContentPresenter _stepContent = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;
    private TextBox _clientIdBox = null!;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static SolidColorBrush BgBase     => (SolidColorBrush)Res("BgBaseBrush");
    private static SolidColorBrush CardBorder => (SolidColorBrush)Res("CardBorderBrush");
    private static SolidColorBrush InputBg    => (SolidColorBrush)Res("InputBgBrush");
    private static SolidColorBrush InputBorder=> (SolidColorBrush)Res("InputBorderBrush");
    private static readonly SolidColorBrush SpotifyGreen = new(Color.FromRgb(0x1D, 0xB9, 0x54));
    private static readonly SolidColorBrush SpotifyGreenGlow = new(Color.FromRgb(0x1E, 0xD7, 0x60));
    private static readonly SolidColorBrush TextPrimary   = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush TextSec       = new(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush TextDim       = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush DangerRed     = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static SolidColorBrush DotInactive => (SolidColorBrush)Res("InputBorderBrush");
    private static SolidColorBrush NavButtonBg => (SolidColorBrush)Res("CardBorderBrush");
    private static SolidColorBrush NavButtonHover => new(Color.FromRgb(0x38, 0x38, 0x38));

    public const string RedirectUrl = "http://127.0.0.1:5543/callback";
    private const string DashboardUrl = "https://developer.spotify.com/dashboard";

    public SpotifySetupGuide()
    {
        WindowStyle = WindowStyle.None;
        Background = BgBase;
        Width = 560;
        Height = 620;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();
        UpdateStep();
    }

    private UIElement BuildContent()
    {
        var outer = new Border
        {
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Background = BgBase,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });

        root.Children.Add(BuildTitleBar());
        var contentArea = new Grid();
        _stepContent = new ContentPresenter
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        contentArea.Children.Add(_stepContent);
        Grid.SetRow(contentArea, 1);
        root.Children.Add(contentArea);

        _stepPanels.Add(BuildStep1_CreateApp());
        _stepPanels.Add(BuildStep2_RedirectUri());
        _stepPanels.Add(BuildStep3_Scopes());
        _stepPanels.Add(BuildStep4_Paste());

        var navBar = BuildNavBar();
        Grid.SetRow(navBar, 2);
        root.Children.Add(navBar);

        outer.Child = root;
        return outer;
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = Res("BgDarkBrush") };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        stack.Children.Add(new TextBlock
        {
            Text = "♪",
            FontSize = 18,
            Foreground = SpotifyGreen,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            FontWeight = FontWeights.Bold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Spotify Setup Guide",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(stack, 0);
        bar.Children.Add(stack);

        var close = new Button
        {
            Content = "✕",
            Width = 48, Height = 48,
            Background = Brushes.Transparent,
            Foreground = TextSec,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand,
        };
        close.Click += (_, _) => { DialogResult = false; Close(); };
        close.MouseEnter += (_, _) => close.Foreground = DangerRed;
        close.MouseLeave += (_, _) => close.Foreground = TextSec;
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);

        bar.Children.Add(new Border { Height = 1, Background = CardBorder, VerticalAlignment = VerticalAlignment.Bottom });
        return bar;
    }

    // ── Step builders ───────────────────────────────────────────────────

    private UIElement BuildStep1_CreateApp()
    {
        var panel = MakeStepFrame(out var _);
        panel.Children.Add(MakeNumberIcon("1", "#1DB954"));
        panel.Children.Add(MakeTitle("Create a Spotify app"));
        panel.Children.Add(MakeBody("Head to the Spotify Developer Dashboard and create a new app. Any name + description works — this is private to you."));

        // Button that opens the dashboard in the user's browser.
        var openBtn = MakePillButton("Open Spotify Dashboard ↗", true);
        openBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DashboardUrl) { UseShellExecute = true }); }
            catch { }
        };
        panel.Children.Add(openBtn);

        panel.Children.Add(MakeSubtext("You'll need a Spotify account (free or Premium both work for dev mode)."));
        return panel;
    }

    private UIElement BuildStep2_RedirectUri()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("2", "#1DB954"));
        panel.Children.Add(MakeTitle("Add the Redirect URI"));
        panel.Children.Add(MakeBody(
            "In your new app's settings, click \"Edit\" and add this exact Redirect URI. Click Add, then Save."));

        // Copyable redirect URL card — the whole point of this wizard.
        panel.Children.Add(MakeCopyCard("REDIRECT URI", RedirectUrl));

        panel.Children.Add(MakeSubtext("The URL must match exactly — trailing slash, port number, and /callback path included."));
        return panel;
    }

    private UIElement BuildStep3_Scopes()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("3", "#1DB954"));
        panel.Children.Add(MakeTitle("Enable Web API"));
        panel.Children.Add(MakeBody(
            "On the same settings screen, under \"Which API/SDKs are you planning to use?\" check \"Web API\" — AmpUp uses it for playback control and now-playing."));
        panel.Children.Add(MakeSubtext("Save again once Web API is selected."));
        return panel;
    }

    private UIElement BuildStep4_Paste()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("4", "#1DB954"));
        panel.Children.Add(MakeTitle("Paste your Client ID"));
        panel.Children.Add(MakeBody(
            "Back on your app's overview page, copy the Client ID (shown below the app name) and paste it here. Then click Finish — Settings will open the OAuth login."));

        _clientIdBox = new TextBox
        {
            Width = 420, Height = 40,
            FontSize = 13,
            Background = InputBg,
            Foreground = TextPrimary,
            CaretBrush = SpotifyGreen,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _clientIdBox.GotFocus  += (_, _) => _clientIdBox.BorderBrush = SpotifyGreen;
        _clientIdBox.LostFocus += (_, _) => _clientIdBox.BorderBrush = InputBorder;
        panel.Children.Add(_clientIdBox);

        panel.Children.Add(MakeSubtext("Your Client ID is safe to paste here — it isn't a secret."));
        return panel;
    }

    // ── Visual helpers ──────────────────────────────────────────────────

    private StackPanel MakeStepFrame(out StackPanel inner)
    {
        var sp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 460,
        };
        inner = sp;
        return sp;
    }

    private Border MakeNumberIcon(string label, string colorHex)
    {
        var c = (Color)ColorConverter.ConvertFromString(colorHex);
        var card = new Border
        {
            Width = 72, Height = 72,
            CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(c),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
            Effect = new DropShadowEffect
            {
                Color = c,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.3,
            },
        };
        card.Child = new TextBlock
        {
            Text = label,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(c),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return card;
    }

    private TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 12),
        TextWrapping = TextWrapping.Wrap,
    };

    private TextBlock MakeBody(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = TextSec,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 16),
    };

    private TextBlock MakeSubtext(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = TextDim,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        FontStyle = FontStyles.Italic,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private Button MakePillButton(string text, bool accent)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(22, 8, 22, 8),
            Background = accent ? SpotifyGreen : NavButtonBg,
            Foreground = accent ? new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)) : TextSec,
            BorderThickness = new Thickness(accent ? 0 : 1),
            BorderBrush = CardBorder,
            FontSize = 13,
            FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };
        if (accent)
        {
            btn.MouseEnter += (_, _) => btn.Background = SpotifyGreenGlow;
            btn.MouseLeave += (_, _) => btn.Background = SpotifyGreen;
        }
        return btn;
    }

    /// <summary>
    /// Big card that shows a LABEL and a VALUE (the redirect URI) with a
    /// prominent Copy button. The point of the whole wizard — users kept
    /// mistyping the URI from the small inline text in Settings.
    /// </summary>
    private Border MakeCopyCard(string label, string value)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = InputBg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 10, 10),
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextDim,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetRow(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            Foreground = SpotifyGreen,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(valueBlock, 0);
        Grid.SetRow(valueBlock, 1);
        grid.Children.Add(valueBlock);

        var copyBtn = new Button
        {
            Content = "Copy",
            Padding = new Thickness(14, 6, 14, 6),
            Background = SpotifyGreen,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
                copyBtn.Content = "Copied!";
                // Revert after a moment
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += (_, _) => { copyBtn.Content = "Copy"; t.Stop(); };
                t.Start();
            }
            catch { }
        };
        copyBtn.MouseEnter += (_, _) => copyBtn.Background = SpotifyGreenGlow;
        copyBtn.MouseLeave += (_, _) => copyBtn.Background = SpotifyGreen;
        Grid.SetColumn(copyBtn, 1);
        Grid.SetRowSpan(copyBtn, 2);
        grid.Children.Add(copyBtn);

        card.Child = grid;
        return card;
    }

    private UIElement BuildNavBar()
    {
        var nav = new Grid();
        nav.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        nav.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var sep = new Border { Background = CardBorder, Height = 1, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(sep, 0);
        nav.Children.Add(sep);

        var grid = new Grid { Margin = new Thickness(24, 0, 24, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _backButton = MakeNavButton("← Back", false);
        _backButton.Click += (_, _) => NavigateTo(_currentStep - 1);
        Grid.SetColumn(_backButton, 0);
        grid.Children.Add(_backButton);

        var dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var dots = new Ellipse[4];
        for (int i = 0; i < 4; i++)
        {
            dots[i] = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = DotInactive,
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
            };
            dotsPanel.Children.Add(dots[i]);
        }
        _dotSets.Add(dots);
        Grid.SetColumn(dotsPanel, 1);
        grid.Children.Add(dotsPanel);

        _nextButton = MakeNavButton("Next →", true);
        _nextButton.Click += (_, _) =>
        {
            if (_currentStep < 3) NavigateTo(_currentStep + 1);
            else OnFinish();
        };
        Grid.SetColumn(_nextButton, 2);
        grid.Children.Add(_nextButton);

        Grid.SetRow(grid, 1);
        nav.Children.Add(grid);
        return nav;
    }

    private Button MakeNavButton(string label, bool accent)
    {
        var btn = new Button
        {
            Content = label,
            Width = 110, Height = 36,
            Background = accent ? SpotifyGreen : NavButtonBg,
            Foreground = accent ? new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)) : TextSec,
            BorderThickness = new Thickness(accent ? 0 : 1),
            BorderBrush = CardBorder,
            FontSize = 13,
            FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (accent)
        {
            btn.MouseEnter += (_, _) => btn.Background = SpotifyGreenGlow;
            btn.MouseLeave += (_, _) => btn.Background = SpotifyGreen;
        }
        else
        {
            btn.MouseEnter += (_, _) => btn.Background = NavButtonHover;
            btn.MouseLeave += (_, _) => btn.Background = NavButtonBg;
        }
        return btn;
    }

    private void NavigateTo(int step)
    {
        _currentStep = step;
        UpdateStep();
    }

    private void UpdateStep()
    {
        _stepContent.Content = _stepPanels[_currentStep];
        if (_dotSets.Count > 0)
        {
            for (int i = 0; i < 4; i++)
                _dotSets[0][i].Fill = (i == _currentStep) ? SpotifyGreen : DotInactive;
        }
        _backButton.Visibility = (_currentStep == 0) ? Visibility.Hidden : Visibility.Visible;
        _nextButton.Content = (_currentStep == 3) ? "Finish" : "Next →";
    }

    private void OnFinish()
    {
        var id = _clientIdBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(id))
        {
            // Shake feedback — just focus the box.
            _clientIdBox?.Focus();
            return;
        }
        ClientId = id;
        WasSuccessful = true;
        DialogResult = true;
        Close();
    }
}
