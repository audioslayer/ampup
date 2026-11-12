using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AmpUp.Controls;

/// <summary>
/// 3-step guide for getting Discord tester access: friend-request the
/// owner, wait for the tester invite, then accept it and Connect. Discord
/// restricts the voice RPC scopes to approved testers. WasSuccessful tells
/// the caller to start Connect.
/// </summary>
public class DiscordSetupGuide : Window
{
    public bool WasSuccessful { get; private set; } = false;

    private const int StepCount = 3;
    private int _currentStep = 0;
    private readonly List<UIElement> _stepPanels = new();
    private readonly List<Ellipse[]> _dotSets = new();
    private ContentPresenter _stepContent = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static SolidColorBrush BgBase     => (SolidColorBrush)Res("BgBaseBrush");
    private static SolidColorBrush CardBorder => (SolidColorBrush)Res("CardBorderBrush");
    private static SolidColorBrush InputBg    => (SolidColorBrush)Res("InputBgBrush");
    private static SolidColorBrush InputBorder=> (SolidColorBrush)Res("InputBorderBrush");
    private static readonly SolidColorBrush DiscordBlurple = new(Color.FromRgb(0x58, 0x65, 0xF2));
    private static readonly SolidColorBrush DiscordBlurpleGlow = new(Color.FromRgb(0x6D, 0x78, 0xF5));
    private static readonly SolidColorBrush TextPrimary   = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush TextSec       = new(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush TextDim       = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush DangerRed     = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static SolidColorBrush DotInactive => (SolidColorBrush)Res("InputBorderBrush");
    private static SolidColorBrush NavButtonBg => (SolidColorBrush)Res("CardBorderBrush");
    private static SolidColorBrush NavButtonHover => new(Color.FromRgb(0x38, 0x38, 0x38));

    public const string OwnerUsername = "aud10sayer";
    private const string OwnerProfileUrl = "https://discord.com/users/233463503950839826";

    public DiscordSetupGuide()
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
            CornerRadius = new CornerRadius(12),
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
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

        _stepPanels.Add(BuildStep1_FriendRequest());
        _stepPanels.Add(BuildStep2_Wait());
        _stepPanels.Add(BuildStep3_Connect());

        var navBar = BuildNavBar();
        Grid.SetRow(navBar, 2);
        root.Children.Add(navBar);

        outer.Child = root;
        return outer;
    }

    private UIElement BuildTitleBar()
        => IntegrationSetupGuide.BuildGuideHeader(this, "◉", "Discord Setup Guide", DiscordBlurple.Color, () => { DialogResult = false; Close(); });

    // ── Step builders ───────────────────────────────────────────────────

    private UIElement BuildStep1_FriendRequest()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("1", "#5865F2"));
        panel.Children.Add(MakeTitle("Add audio on Discord"));
        panel.Children.Add(MakeBody(
            "Discord only lets approved testers use Amp Up's Discord controls for now. Send a friend request to audio and mention you want Amp Up Discord tester access."));

        panel.Children.Add(MakeCopyCard("DISCORD USERNAME", OwnerUsername));

        var openBtn = MakePillButton("Open audio's Discord profile ↗", true);
        openBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OwnerProfileUrl) { UseShellExecute = true }); }
            catch { }
        };
        panel.Children.Add(openBtn);

        panel.Children.Add(MakeSubtext("In Discord: Add Friend, paste the username, then send a short message saying you'd like Amp Up Discord tester access."));
        return panel;
    }

    private UIElement BuildStep2_Wait()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("2", "#5865F2"));
        panel.Children.Add(MakeTitle("Wait for your tester invite"));
        panel.Children.Add(MakeBody(
            "Once audio accepts your request, they'll add you as a tester and Discord will send you an invite. This is done by hand, so it can take a little while."));
        panel.Children.Add(MakeSubtext("You can close this guide and come back to it any time from Settings."));
        return panel;
    }

    private UIElement BuildStep3_Connect()
    {
        var panel = MakeStepFrame(out _);
        panel.Children.Add(MakeNumberIcon("3", "#5865F2"));
        panel.Children.Add(MakeTitle("Accept and connect"));
        panel.Children.Add(MakeBody(
            "Accept the tester invite from Discord. Then keep the Discord desktop app open and click Finish: Amp Up will connect, and Discord will ask you to Authorize it."));
        panel.Children.Add(MakeSubtext("If Discord says access isn't allowed yet, your invite hasn't been accepted. Try Connect again later."));
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
            Effect = GlassStyle.GlowEffect(c, 20, 0.3),
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
            Background = accent ? DiscordBlurple : NavButtonBg,
            Foreground = accent ? Brushes.White : TextSec,
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
            btn.MouseEnter += (_, _) => btn.Background = DiscordBlurpleGlow;
            btn.MouseLeave += (_, _) => btn.Background = DiscordBlurple;
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
            Foreground = DiscordBlurple,
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
            Background = DiscordBlurple,
            Foreground = Brushes.White,
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
        copyBtn.MouseEnter += (_, _) => copyBtn.Background = DiscordBlurpleGlow;
        copyBtn.MouseLeave += (_, _) => copyBtn.Background = DiscordBlurple;
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
        var dots = new Ellipse[StepCount];
        for (int i = 0; i < StepCount; i++)
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
            if (_currentStep < StepCount - 1) NavigateTo(_currentStep + 1);
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
            Background = accent ? DiscordBlurple : NavButtonBg,
            Foreground = accent ? Brushes.White : TextSec,
            BorderThickness = new Thickness(accent ? 0 : 1),
            BorderBrush = CardBorder,
            FontSize = 13,
            FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (accent)
        {
            btn.MouseEnter += (_, _) => btn.Background = DiscordBlurpleGlow;
            btn.MouseLeave += (_, _) => btn.Background = DiscordBlurple;
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
            for (int i = 0; i < StepCount; i++)
                _dotSets[0][i].Fill = (i == _currentStep) ? DiscordBlurple : DotInactive;
        }
        _backButton.Visibility = (_currentStep == 0) ? Visibility.Hidden : Visibility.Visible;
        _nextButton.Content = (_currentStep == StepCount - 1) ? "Finish" : "Next →";
    }

    private void OnFinish()
    {
        WasSuccessful = true;
        DialogResult = true;
        Close();
    }
}
