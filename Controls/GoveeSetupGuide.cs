using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AmpUp.Controls;

public class GoveeSetupGuide : Window
{
    public string ApiKey { get; private set; } = "";
    public bool WasSuccessful { get; private set; } = false;
    public Func<string, Task<bool>>? ValidateKeyAsync { get; set; }

    private int _currentStep = 0;
    private readonly List<UIElement> _stepPanels = new();
    private readonly List<Ellipse[]> _dotSets = new();
    private ContentPresenter _stepContent = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;
    private TextBox _apiKeyBox = null!;
    private TextBlock _feedbackText = null!;
    private Button _connectButton = null!;

    // Theme colors
    private static readonly SolidColorBrush BgBase       = new(Color.FromRgb(0x0F, 0x0F, 0x0F));
    private static readonly SolidColorBrush CardBg        = new(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly SolidColorBrush CardBorder    = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly SolidColorBrush InputBg       = new(Color.FromRgb(0x24, 0x24, 0x24));
    private static readonly SolidColorBrush InputBorder   = new(Color.FromRgb(0x36, 0x36, 0x36));
    private static readonly SolidColorBrush AccentBrush   = new(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly SolidColorBrush AccentGlow    = new(Color.FromRgb(0x69, 0xF0, 0xAE));
    private static readonly SolidColorBrush AccentDim     = new(Color.FromRgb(0x00, 0xA8, 0x54));
    private static readonly SolidColorBrush TextPrimary   = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush TextSec       = new(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush TextDim       = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush DangerRed     = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static readonly SolidColorBrush SuccessGrn    = new(Color.FromRgb(0x00, 0xDD, 0x77));
    private static readonly SolidColorBrush DotInactive   = new(Color.FromRgb(0x36, 0x36, 0x36));
    private static readonly SolidColorBrush NavButtonBg   = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly SolidColorBrush NavButtonHover = new(Color.FromRgb(0x38, 0x38, 0x38));

    public GoveeSetupGuide()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = BgBase;
        Width = 520;
        Height = 580;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();
        UpdateStep();
    }

    private UIElement BuildContent()
    {
        // Outer border for 1px border + clip
        var outerBorder = new Border
        {
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Background = BgBase,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });   // title bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });   // nav bar

        // --- Title bar ---
        var titleBar = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
        };

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };

        var titleIcon = new TextBlock
        {
            Text = "💡",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var titleText = new TextBlock
        {
            Text = "Govee Setup Guide",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleIcon);
        titleStack.Children.Add(titleText);
        Grid.SetColumn(titleStack, 0);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 48,
            Height = 48,
            Background = Brushes.Transparent,
            Foreground = TextSec,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => { DialogResult = false; Close(); };
        closeBtn.MouseEnter += (_, _) => closeBtn.Foreground = DangerRed;
        closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = TextSec;
        Grid.SetColumn(closeBtn, 1);

        titleBar.Children.Add(titleStack);
        titleBar.Children.Add(closeBtn);
        Grid.SetRow(titleBar, 0);

        // Separator under title bar
        var titleSep = new Border
        {
            Height = 1,
            Background = CardBorder,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        titleBar.Children.Add(titleSep);

        // --- Step content area ---
        _stepContent = new ContentPresenter
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var contentArea = new Grid();
        contentArea.Children.Add(_stepContent);
        Grid.SetRow(contentArea, 1);

        // Build step panels
        _stepPanels.Add(BuildStep(
            "1", "#4FC3F7",
            "Open the Govee App",
            "Open the Govee Home app on your phone.",
            "If you don't have it, download it from the App Store or Google Play."
        ));
        _stepPanels.Add(BuildStep(
            "2", "#AB47BC",
            "Go to Settings",
            "Tap your profile icon (bottom right), then the settings gear (top right).",
            "Look for the person icon → then the cog icon."
        ));
        _stepPanels.Add(BuildStep(
            "3", "#FFA726",
            "Request API Key",
            "Tap \"Apply for API Key\" and fill out the short form.",
            "Govee will email your API key — usually within minutes."
        ));
        _stepPanels.Add(BuildStep4());

        // --- Navigation bar ---
        var navBar = BuildNavBar();
        Grid.SetRow(navBar, 2);

        root.Children.Add(titleBar);
        root.Children.Add(contentArea);
        root.Children.Add(navBar);

        outerBorder.Child = root;
        return outerBorder;
    }

    private UIElement BuildStep(string label, string colorHex, string title, string body, string subtext)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 400,
        };

        // Colored numbered circle
        var circleColor = (Color)ColorConverter.ConvertFromString(colorHex);
        var iconCard = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0x30, circleColor.R, circleColor.G, circleColor.B)),
            BorderBrush = new SolidColorBrush(circleColor),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Effect = new DropShadowEffect
            {
                Color = circleColor,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.25
            }
        };
        var iconText = new TextBlock
        {
            Text = label,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(circleColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconCard.Child = iconText;
        panel.Children.Add(iconCard);

        // Title
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(titleBlock);

        // Body
        var bodyBlock = new TextBlock
        {
            Text = body,
            FontSize = 14,
            Foreground = TextSec,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(bodyBlock);

        // Subtext
        var subBlock = new TextBlock
        {
            Text = subtext,
            FontSize = 12,
            Foreground = TextDim,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic
        };
        panel.Children.Add(subBlock);

        return panel;
    }

    private UIElement BuildStep4()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 400
        };

        // Icon card
        var iconCard = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE6, 0x76)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x00, 0xE6, 0x76),
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.25
            }
        };
        iconCard.Child = new TextBlock
        {
            Text = "4",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(iconCard);

        panel.Children.Add(new TextBlock
        {
            Text = "Paste Your Key",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Paste your API key below to connect AmpUp to your Govee lights.",
            FontSize = 14,
            Foreground = TextSec,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });

        // API key input
        _apiKeyBox = new TextBox
        {
            Width = 380,
            Height = 40,
            FontSize = 13,
            Background = InputBg,
            Foreground = TextPrimary,
            CaretBrush = AccentBrush,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _apiKeyBox.GotFocus  += (_, _) => _apiKeyBox.BorderBrush = AccentBrush;
        _apiKeyBox.LostFocus += (_, _) => _apiKeyBox.BorderBrush = InputBorder;
        panel.Children.Add(_apiKeyBox);

        // Connect button
        _connectButton = new Button
        {
            Content = "Connect",
            Width = 380,
            Height = 40,
            Background = AccentBrush,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            BorderThickness = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _connectButton.Click += OnConnectClicked;
        _connectButton.MouseEnter += (_, _) => _connectButton.Background = AccentGlow;
        _connectButton.MouseLeave += (_, _) => _connectButton.Background = AccentBrush;
        panel.Children.Add(_connectButton);

        // Feedback text
        _feedbackText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_feedbackText);

        return panel;
    }

    private UIElement BuildNavBar()
    {
        // Separator above nav
        var navContainer = new Grid();
        navContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        navContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var sep = new Border { Background = CardBorder, Height = 1, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(sep, 0);
        navContainer.Children.Add(sep);

        var navGrid = new Grid { Margin = new Thickness(24, 0, 24, 0) };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Back button
        _backButton = MakeNavButton("← Back", false);
        _backButton.Click += (_, _) => NavigateTo(_currentStep - 1);
        Grid.SetColumn(_backButton, 0);

        // Dot indicators (centered)
        var dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dots = new Ellipse[4];
        for (int i = 0; i < 4; i++)
        {
            dots[i] = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = DotInactive,
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0)
            };
            dotsPanel.Children.Add(dots[i]);
        }
        _dotSets.Add(dots);
        Grid.SetColumn(dotsPanel, 1);

        // Next button
        _nextButton = MakeNavButton("Next →", true);
        _nextButton.Click += (_, _) =>
        {
            if (_currentStep < 3) NavigateTo(_currentStep + 1);
        };
        Grid.SetColumn(_nextButton, 2);

        navGrid.Children.Add(_backButton);
        navGrid.Children.Add(dotsPanel);
        navGrid.Children.Add(_nextButton);

        Grid.SetRow(navGrid, 1);
        navContainer.Children.Add(navGrid);

        return navContainer;
    }

    private Button MakeNavButton(string label, bool isAccent)
    {
        var bg = isAccent ? AccentBrush : NavButtonBg;
        var fg = isAccent ? new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)) : TextSec;

        var btn = new Button
        {
            Content = label,
            Width = 100,
            Height = 36,
            Background = bg,
            Foreground = fg,
            BorderThickness = new Thickness(isAccent ? 0 : 1),
            BorderBrush = CardBorder,
            FontSize = 13,
            FontWeight = isAccent ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isAccent)
        {
            btn.MouseEnter += (_, _) => btn.Background = AccentGlow;
            btn.MouseLeave += (_, _) => btn.Background = AccentBrush;
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

        // Update dots
        if (_dotSets.Count > 0)
        {
            for (int i = 0; i < 4; i++)
                _dotSets[0][i].Fill = (i == _currentStep) ? AccentBrush : DotInactive;
        }

        // Back button: hidden on step 0
        _backButton.Visibility = (_currentStep == 0) ? Visibility.Hidden : Visibility.Visible;

        // Next button: on step 4, hide it (Connect button inside the step handles confirmation)
        if (_currentStep == 3)
        {
            _nextButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            _nextButton.Visibility = Visibility.Visible;
            _nextButton.Content = "Next →";
            _nextButton.Background = AccentBrush;
            _nextButton.Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F));
        }

        // Clear feedback when leaving step 4
        if (_currentStep != 3 && _feedbackText != null)
        {
            _feedbackText.Text = "";
        }
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        var key = _apiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _feedbackText.Text = "Please enter your API key.";
            _feedbackText.Foreground = DangerRed;
            return;
        }

        _connectButton.IsEnabled = false;
        _connectButton.Content = "Connecting...";
        _connectButton.Background = AccentDim;
        _feedbackText.Text = "";

        bool success = false;
        try
        {
            if (ValidateKeyAsync != null)
                success = await ValidateKeyAsync(key);
            else
                success = true; // no validator, accept as-is
        }
        catch
        {
            success = false;
        }

        _connectButton.IsEnabled = true;
        _connectButton.Content = "Connect";
        _connectButton.Background = AccentBrush;

        if (success)
        {
            ApiKey = key;
            WasSuccessful = true;
            _feedbackText.Text = "✓ Connected! Your Govee lights are ready.";
            _feedbackText.Foreground = SuccessGrn;
            await Task.Delay(900);
            DialogResult = true;
            Close();
        }
        else
        {
            _feedbackText.Text = "✗ Could not connect. Check your API key and try again.";
            _feedbackText.Foreground = DangerRed;
        }
    }
}
