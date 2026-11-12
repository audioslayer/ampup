using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AmpUp.Controls;

/// <summary>
/// Shared chrome for the integration setup wizards (Home Assistant, OBS,
/// VoiceMeeter, Corsair, SignalRGB). Same look and flow as
/// SpotifySetupGuide: title bar, one step at a time, Back / dots / Next,
/// Finish on the last step. Subclasses add their steps in the constructor
/// via AddStep, then call Init(). Finish sets WasSuccessful and
/// DialogResult = true so the caller can run the integration's Connect.
/// </summary>
public abstract class IntegrationSetupGuide : Window
{
    public bool WasSuccessful { get; private set; } = false;

    private int _currentStep = 0;
    private readonly List<UIElement> _stepPanels = new();
    private Ellipse[] _dots = Array.Empty<Ellipse>();
    private ContentPresenter _stepContent = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;

    private readonly string _titleText;
    private readonly string _glyph;
    private readonly string _accentHex;
    protected readonly SolidColorBrush Accent;
    protected readonly SolidColorBrush AccentGlow;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static SolidColorBrush BgBase     => (SolidColorBrush)Res("BgBaseBrush");
    private static SolidColorBrush CardBorder => (SolidColorBrush)Res("CardBorderBrush");
    protected static SolidColorBrush InputBg    => (SolidColorBrush)Res("InputBgBrush");
    protected static SolidColorBrush InputBorder=> (SolidColorBrush)Res("InputBorderBrush");
    protected static readonly SolidColorBrush TextPrimary   = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush TextSec       = new(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush TextDim       = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush DangerRed     = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static SolidColorBrush DotInactive => (SolidColorBrush)Res("InputBorderBrush");
    private static SolidColorBrush NavButtonBg => (SolidColorBrush)Res("CardBorderBrush");
    private static SolidColorBrush NavButtonHover => new(Color.FromRgb(0x38, 0x38, 0x38));

    protected IntegrationSetupGuide(string title, string glyph, string accentHex, string accentGlowHex)
    {
        _titleText = title;
        _glyph = glyph;
        _accentHex = accentHex;
        Accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
        AccentGlow = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentGlowHex));

        WindowStyle = WindowStyle.None;
        Background = BgBase;
        Width = 560;
        Height = 620;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    /// <summary>Call once at the end of the subclass constructor, after all AddStep calls.</summary>
    protected void Init()
    {
        Content = BuildContent();
        UpdateStep();
    }

    protected void AddStep(UIElement panel) => _stepPanels.Add(panel);

    /// <summary>
    /// Last-step validation. Return false (and focus the bad field) to stay
    /// on the step; return true to close with success.
    /// </summary>
    protected virtual bool TryFinish() => true;

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

        var navBar = BuildNavBar();
        Grid.SetRow(navBar, 2);
        root.Children.Add(navBar);

        outer.Child = root;
        return outer;
    }

    private UIElement BuildTitleBar()
        => BuildGuideHeader(this, _glyph, _titleText, Accent.Color, () => { DialogResult = false; Close(); });

    /// <summary>
    /// Modal header shared by every setup guide (same chrome as GroupNameDialog): glowing tile with the
    /// integration glyph in its brand colour + title / subtitle, close button, drag to move.
    /// </summary>
    internal static UIElement BuildGuideHeader(Window window, string glyph, string title, Color accent, Action onClose)
    {
        var bar = new Grid { Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Grid or TextBlock or StackPanel)) return;
            try { window.DragMove(); } catch (InvalidOperationException) { }
        };

        var tile = new Border
        {
            Width = 38, Height = 38,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
            Margin = new Thickness(20, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.35 },
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        bar.Children.Add(tile);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var st = new TextBlock { Text = "Follow the steps to connect", FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(t);
        titles.Children.Add(st);
        Grid.SetColumn(titles, 1);
        bar.Children.Add(titles);

        var close = UiKit.IconButton(Material.Icons.MaterialIconKind.Close, "Close", _ => onClose(), size: 26);
        close.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(close, 2);
        bar.Children.Add(close);

        var sep = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        sep.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        Grid.SetColumnSpan(sep, 3);
        bar.Children.Add(sep);
        return bar;
    }

    // ── Step building helpers (for subclasses) ──────────────────────────

    /// <summary>Standard step: number badge, title, body, optional subtext.</summary>
    protected StackPanel MakeStep(int number, string title, string body)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 460,
        };
        panel.Children.Add(MakeNumberIcon(number.ToString(), _accentHex));
        panel.Children.Add(MakeTitle(title));
        panel.Children.Add(MakeBody(body));
        return panel;
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

    protected TextBlock MakeSubtext(string text) => new()
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

    /// <summary>Accent pill button that opens a URL in the default browser.</summary>
    protected Button MakeLinkButton(string text, string url)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(22, 8, 22, 8),
            Background = Accent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            BorderThickness = new Thickness(0),
            BorderBrush = CardBorder,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };
        btn.MouseEnter += (_, _) => btn.Background = AccentGlow;
        btn.MouseLeave += (_, _) => btn.Background = Accent;
        btn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        };
        return btn;
    }

    /// <summary>LABEL + monospace VALUE card with a Copy button (same as Spotify's redirect URI card).</summary>
    protected Border MakeCopyCard(string label, string value)
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
            Foreground = Accent,
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
            Background = Accent,
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
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += (_, _) => { copyBtn.Content = "Copy"; t.Stop(); };
                t.Start();
            }
            catch { }
        };
        copyBtn.MouseEnter += (_, _) => copyBtn.Background = AccentGlow;
        copyBtn.MouseLeave += (_, _) => copyBtn.Background = Accent;
        Grid.SetColumn(copyBtn, 1);
        Grid.SetRowSpan(copyBtn, 2);
        grid.Children.Add(copyBtn);

        card.Child = grid;
        return card;
    }

    /// <summary>Small caption above an input field.</summary>
    protected TextBlock MakeFieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextDim,
        Width = 420,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 4),
    };

    /// <summary>Text input styled like Spotify's Client ID box.</summary>
    protected TextBox MakeTextInput(string initial = "")
    {
        var box = new TextBox
        {
            Text = initial,
            Width = 420, Height = 40,
            FontSize = 13,
            Background = InputBg,
            Foreground = TextPrimary,
            CaretBrush = Accent,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.GotFocus  += (_, _) => box.BorderBrush = Accent;
        box.LostFocus += (_, _) => box.BorderBrush = InputBorder;
        return box;
    }

    /// <summary>Password input matching MakeTextInput.</summary>
    protected PasswordBox MakePasswordInput()
    {
        var box = new PasswordBox
        {
            Width = 420, Height = 40,
            FontSize = 13,
            Background = InputBg,
            Foreground = TextPrimary,
            CaretBrush = Accent,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.GotFocus  += (_, _) => box.BorderBrush = Accent;
        box.LostFocus += (_, _) => box.BorderBrush = InputBorder;
        return box;
    }

    // ── Navigation ──────────────────────────────────────────────────────

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
        _dots = new Ellipse[_stepPanels.Count];
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = DotInactive,
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
            };
            dotsPanel.Children.Add(_dots[i]);
        }
        Grid.SetColumn(dotsPanel, 1);
        grid.Children.Add(dotsPanel);

        _nextButton = MakeNavButton("Next →", true);
        _nextButton.Click += (_, _) =>
        {
            if (_currentStep < _stepPanels.Count - 1) NavigateTo(_currentStep + 1);
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
            Background = accent ? Accent : NavButtonBg,
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
            btn.MouseEnter += (_, _) => btn.Background = AccentGlow;
            btn.MouseLeave += (_, _) => btn.Background = Accent;
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
        if (_stepPanels.Count == 0) return;
        _stepContent.Content = _stepPanels[_currentStep];
        for (int i = 0; i < _dots.Length; i++)
            _dots[i].Fill = (i == _currentStep) ? Accent : DotInactive;
        int last = _stepPanels.Count - 1;
        _backButton.Visibility = (_currentStep == 0) ? Visibility.Hidden : Visibility.Visible;
        _nextButton.Content = (_currentStep == last) ? "Finish" : "Next →";
    }

    private void OnFinish()
    {
        if (!TryFinish()) return;
        WasSuccessful = true;
        DialogResult = true;
        Close();
    }
}
