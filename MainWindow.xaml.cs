using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using AmpUp.Views;

namespace AmpUp;

public partial class MainWindow : FluentWindow
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();
    private readonly HomeAssistantView _haView = new();
    private readonly AmbienceView _ambienceView = new();

    private System.Windows.Controls.Button? _activeNavButton;
    private System.Windows.Controls.Border? _activeNavBar;

    private AppConfig _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onConfigChanged;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/icon/ampup-48.png", UriKind.Absolute));

        _config = ConfigManager.Load();
        VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
        UpdateProfileButton();
        UpdateAccentDependentUI();
        NavigateTo(_mixerView, NavMixer);
        SetupTrafficLightHovers();

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(UpdateAccentDependentUI);

        // Silent startup update check
        Loaded += async (_, _) => await CheckForUpdateOnStartup();
    }

    /// <summary>
    /// Update UI elements that depend on the accent color but can't use DynamicResource
    /// (DropShadowEffect Color, GradientStop, etc.).
    /// </summary>
    private void UpdateAccentDependentUI()
    {
        // Profile button border gradient
        ProfileButton.BorderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x88),
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x44),
            new Point(0, 0), new Point(1, 1));

        // Profile button drop shadow
        ProfileButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = ThemeManager.Accent,
            BlurRadius = 12,
            Opacity = 0.25,
            ShadowDepth = 0
        };

        // Connection dot glow
        ConnectionDotGlow.Color = ThemeManager.Accent;
    }

    /// <summary>
    /// Wire up backend references and load config into all views.
    /// </summary>
    public void Initialize(AppConfig config, AudioMixer mixer, Action<AppConfig> onConfigChanged)
    {
        _config = config;
        _mixer = mixer;
        _onConfigChanged = onConfigChanged;
        RefreshViews();
    }

    public void RefreshViews()
    {
        Action<AppConfig> saveHandler = cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        };

        _settingsView.LoadConfig(_config, saveHandler);
        _lightsView.LoadConfig(_config, saveHandler, _mixer);
        _buttonsView.LoadConfig(_config, _mixer!, saveHandler);
        _mixerView.LoadConfig(_config, _mixer!, saveHandler);
        _haView.LoadConfig(_config, saveHandler);
        _ambienceView.LoadConfig(_config, saveHandler);
    }

    private void NavMixer_Click(object sender, RoutedEventArgs e) => NavigateTo(_mixerView, NavMixer);
    private void NavButtons_Click(object sender, RoutedEventArgs e) => NavigateTo(_buttonsView, NavButtons);
    private void NavLights_Click(object sender, RoutedEventArgs e)
    {
        // Refresh lights view to pick up label/color changes from mixer tab
        _lightsView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        }, _mixer);
        NavigateTo(_lightsView, NavLights);
    }
    private void NavHA_Click(object sender, RoutedEventArgs e)
    {
        _haView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_haView, NavHA);
    }
    private void NavAmbience_Click(object sender, RoutedEventArgs e)
    {
        _ambienceView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_ambienceView, NavAmbience);
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);

    public void NavigateToSettings() => NavigateTo(_settingsView, NavSettings);

    private void NavImport_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new ImportWizardWindow { Owner = this };
        wizard.ShowDialog();

        if (wizard.ImportedProfileName != null)
        {
            var profileName = wizard.ImportedProfileName;

            if (!_config.Profiles.Contains(profileName))
                _config.Profiles.Add(profileName);

            var loaded = ConfigManager.LoadProfile(profileName);
            if (loaded != null)
            {
                loaded.ActiveProfile = profileName;
                PreserveGlobalSettings(loaded);
                _config = loaded;
                _onConfigChanged?.Invoke(_config);
                RefreshViews();
                RefreshProfilePicker();
            }
        }
    }

    // Map nav buttons to their indicator bars
    private Dictionary<System.Windows.Controls.Button, System.Windows.Controls.Border> GetNavBars() => new()
    {
        { NavMixer,    NavMixerBar },
        { NavButtons,  NavButtonsBar },
        { NavLights,   NavLightsBar },
        { NavHA,       NavHABar },
        { NavAmbience, NavAmbienceBar },
        { NavSettings, NavSettingsBar },
    };

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;

        // Update sidebar highlight (icon + label)
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var dimIcon = (SolidColorBrush)FindResource("TextSecBrush");
        var dimLabel = (SolidColorBrush)FindResource("TextSecBrush");

        if (_activeNavButton != null)
        {
            var (oldIcon, oldLabel) = FindNavChildren(_activeNavButton);
            if (oldIcon != null) oldIcon.Foreground = dimIcon;
            if (oldLabel != null) oldLabel.Foreground = dimLabel;

            // Hide previous indicator bar
            if (_activeNavBar != null)
                _activeNavBar.Visibility = Visibility.Collapsed;
        }

        var (newIcon, newLabel) = FindNavChildren(navButton);
        if (newIcon != null) newIcon.Foreground = accent;
        if (newLabel != null) newLabel.Foreground = accent;

        // Show new indicator bar
        var bars = GetNavBars();
        if (bars.TryGetValue(navButton, out var bar))
        {
            bar.Visibility = Visibility.Visible;
            _activeNavBar = bar;
        }

        _activeNavButton = navButton;
    }

    private static (SymbolIcon? Icon, System.Windows.Controls.TextBlock? Label) FindNavChildren(System.Windows.Controls.Button button)
    {
        // Content is now a Grid wrapping a Border (indicator bar) + StackPanel
        var grid = button.Content as System.Windows.Controls.Grid;
        var sp = grid != null
            ? grid.Children.OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()
            : button.Content as System.Windows.Controls.StackPanel;

        if (sp != null)
        {
            SymbolIcon? icon = null;
            System.Windows.Controls.TextBlock? label = null;
            foreach (var child in sp.Children)
            {
                if (child is SymbolIcon si) icon = si;
                if (child is System.Windows.Controls.TextBlock tb) label = tb;
            }
            return (icon, label);
        }
        return (button.Content as SymbolIcon, null);
    }

    // ── Window drag ─────────────────────────────────────────────────

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    // ── Mac-style traffic light buttons ───────────────────────────

    private void SetupTrafficLightHovers()
    {
        // Show icons on hover over any of the 3 buttons
        var buttons = new[] { BtnMinimize, BtnMaximize, BtnClose };
        var icons = new[] { MinIcon, MaxIcon, CloseIcon };

        foreach (var btn in buttons)
        {
            btn.MouseEnter += (_, _) => { foreach (var ic in icons) ic.Opacity = 1; };
            btn.MouseLeave += (_, _) => { foreach (var ic in icons) ic.Opacity = 0; };
        }
    }

    private void BtnMinimize_Click(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, MouseButtonEventArgs e)
    {
        Close(); // triggers MainWindow_Closing → hides to tray
    }

    private bool _checkingUpdate;
    private (string Tag, string Url)? _pendingUpdate;

    private async Task CheckForUpdateOnStartup()
    {
        try
        {
            var update = await UpdateChecker.CheckForUpdateAsync();
            if (update != null)
            {
                _pendingUpdate = update;
                VersionLabel.Text = $"Update available: {update.Value.Tag}";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Startup update check failed: {ex.Message}");
        }
    }

    private async void VersionLabel_Click(object sender, MouseButtonEventArgs e)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;

        VersionLabel.Text = "Checking...";
        VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");

        try
        {
            var update = _pendingUpdate ?? await UpdateChecker.CheckForUpdateAsync();
            _pendingUpdate = null;
            if (update == null)
            {
                VersionLabel.Text = "Up to date!";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("SuccessGrnBrush");
                await Task.Delay(2000);
                VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
            }
            else
            {
                var (tag, url) = update.Value;
                if (GlassDialog.Confirm($"A new version ({tag}) is available. Download and install?", "UPDATE", owner: this))
                {
                    VersionLabel.Text = "Downloading...";
                    await UpdateChecker.DownloadAndInstallAsync(url, progress =>
                    {
                        Dispatcher.Invoke(() => VersionLabel.Text = $"Downloading {progress}%");
                    });
                }
                else
                {
                    VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
                    VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check error: {ex.Message}");
            VersionLabel.Text = "Update failed";
            VersionLabel.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
            await Task.Delay(2000);
            VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
            VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    // ── Profile flyout ────────────────────────────────────────────

    // Icon options for profile picker — Fluent SymbolRegular names
    private static readonly (string Category, string[] Symbols)[] ProfileIconCategories =
    {
        ("Audio & Music", new[] {
            "Speaker224", "Speaker024", "SpeakerMute24", "Headphones24",
            "MusicNote124", "MusicNote224", "Mic24", "MicOff24"
        }),
        ("Gaming & Fun", new[] {
            "Games24", "Trophy24", "Rocket24", "Star24",
            "Heart24", "Emoji24", "Bot24", "PersonBoard24"
        }),
        ("Lights & Effects", new[] {
            "LightbulbFilament24", "Flash24", "Sparkle24", "Weather24",
            "WeatherMoon24", "WeatherSunny24", "Drop24", "Fire24"
        }),
        ("Work & Streaming", new[] {
            "Desktop24", "Laptop24", "Keyboard24", "Video24",
            "Record24", "Globe24", "Megaphone24", "SlideText24"
        }),
        ("Home & System", new[] {
            "Home24", "Settings24", "Shield24", "Lock24",
            "Eye24", "Power24", "Bluetooth24", "Wifi124"
        }),
    };

    // Color presets for profile icons
    private static readonly (string Name, string Hex)[] ProfileIconColors =
    {
        ("Green",  "#00E676"),
        ("Cyan",   "#00B4D8"),
        ("Blue",   "#4FC3F7"),
        ("Purple", "#BB86FC"),
        ("Pink",   "#FF4081"),
        ("Red",    "#FF6B6B"),
        ("Orange", "#FF7043"),
        ("Amber",  "#FFB800"),
        ("Mint",   "#69F0AE"),
        ("White",  "#E8E8E8"),
    };

    private void UpdateProfileButton()
    {
        var icon = _config.ProfileIcons.GetValueOrDefault(_config.ActiveProfile) ?? new ProfileIconConfig();
        if (Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(icon.Symbol, out var sym))
            ProfileIcon.Symbol = sym;
        try { ProfileIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Color)); } catch { }
        ProfileLabel.Text = _config.ActiveProfile;
    }

    private void ProfileButton_Click(object sender, MouseButtonEventArgs e)
    {
        BuildProfileFlyout();
        ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
        e.Handled = true;
    }

    private void BuildProfileFlyout()
    {
        ProfilePopupPanel.Children.Clear();

        // Header
        var header = new System.Windows.Controls.TextBlock
        {
            Text = "PROFILES",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(6, 4, 0, 8)
        };
        ProfilePopupPanel.Children.Add(header);

        // Profile items
        foreach (var profile in _config.Profiles)
        {
            var profileCapture = profile;
            bool isActive = profile == _config.ActiveProfile;
            var iconCfg = _config.ProfileIcons.GetValueOrDefault(profile) ?? new ProfileIconConfig();

            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            // Icon button (clickable to change icon)
            var iconElement = new SymbolIcon { FontSize = 18, Margin = new Thickness(0, 0, 8, 0) };
            if (Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(iconCfg.Symbol, out var sym))
                iconElement.Symbol = sym;
            try { iconElement.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconCfg.Color)); } catch { }
            iconElement.Cursor = Cursors.Hand;
            iconElement.ToolTip = "Change icon";
            iconElement.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                ShowIconPicker(profileCapture);
            };
            System.Windows.Controls.Grid.SetColumn(iconElement, 0);
            row.Children.Add(iconElement);

            // Profile name
            var nameBlock = new System.Windows.Controls.TextBlock
            {
                Text = profile,
                FontSize = 12,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isActive
                    ? (SolidColorBrush)FindResource("AccentBrush")
                    : (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(nameBlock, 1);
            row.Children.Add(nameBlock);

            // Delete button (not for Default)
            if (profile != "Default")
            {
                var deleteBtn = new System.Windows.Controls.TextBlock
                {
                    Text = "✕",
                    FontSize = 9,
                    Foreground = (SolidColorBrush)FindResource("DangerRedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "Delete profile"
                };
                deleteBtn.MouseEnter += (_, _) => deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x77));
                deleteBtn.MouseLeave += (_, _) => deleteBtn.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
                deleteBtn.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    DeleteProfile(profileCapture);
                };
                System.Windows.Controls.Grid.SetColumn(deleteBtn, 2);
                row.Children.Add(deleteBtn);
            }

            var rowBorder = new System.Windows.Controls.Border
            {
                Padding = new Thickness(6, 6, 6, 6),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xB4, 0xD8))
                    : System.Windows.Media.Brushes.Transparent,
                Child = row
            };

            // Hover
            rowBorder.MouseEnter += (_, _) =>
            {
                if (profileCapture != _config.ActiveProfile)
                    rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
            };
            rowBorder.MouseLeave += (_, _) =>
            {
                rowBorder.Background = profileCapture == _config.ActiveProfile
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xB4, 0xD8))
                    : System.Windows.Media.Brushes.Transparent;
            };

            // Click to switch
            rowBorder.MouseLeftButtonDown += (_, ev) =>
            {
                if (profileCapture == _config.ActiveProfile) return;
                SwitchToProfile(profileCapture);
                ProfilePopup.IsOpen = false;
            };

            ProfilePopupPanel.Children.Add(rowBorder);
        }

        // Divider
        var divider = new System.Windows.Controls.Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Margin = new Thickness(4, 6, 4, 6)
        };
        ProfilePopupPanel.Children.Add(divider);

        // Add new profile button
        var addRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        addRow.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "+",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0)
        });
        addRow.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "New Profile",
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var addBorder = new System.Windows.Controls.Border
        {
            Padding = new Thickness(6, 6, 6, 6),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Child = addRow
        };
        addBorder.MouseEnter += (_, _) =>
        {
            addBorder.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        };
        addBorder.MouseLeave += (_, _) =>
        {
            addBorder.Background = System.Windows.Media.Brushes.Transparent;
        };
        addBorder.MouseLeftButtonDown += (_, _) =>
        {
            ProfilePopup.IsOpen = false;
            AddNewProfile();
        };
        ProfilePopupPanel.Children.Add(addBorder);
    }

    private void ShowIconPicker(string profileName)
    {
        // Close profile popup, show icon picker popup
        ProfilePopup.IsOpen = false;

        var currentIcon = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();

        var iconPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = ProfileButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            HorizontalOffset = 4
        };

        var outerPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(8) };

        // ── Color swatches at top ──
        var colorLabel = new System.Windows.Controls.TextBlock
        {
            Text = "COLOR",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(2, 0, 0, 6)
        };
        outerPanel.Children.Add(colorLabel);

        var colorWrap = new System.Windows.Controls.WrapPanel { Width = 280 };
        string selectedColor = currentIcon.Color;

        // We'll track all icon symbols so we can update their color when user picks a new one
        var allIconElements = new List<SymbolIcon>();

        foreach (var (name, hex) in ProfileIconColors)
        {
            var colorHex = hex;
            var swatch = new System.Windows.Controls.Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2),
                ToolTip = name,
                BorderThickness = new Thickness(colorHex == selectedColor ? 2 : 0),
                BorderBrush = new SolidColorBrush(Colors.White)
            };
            try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }

            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = colorHex;
                _config.ProfileIcons[profileName] = new ProfileIconConfig
                {
                    Symbol = _config.ProfileIcons.GetValueOrDefault(profileName)?.Symbol ?? "Speaker224",
                    Color = colorHex
                };
                _onConfigChanged?.Invoke(_config);
                UpdateProfileButton();

                // Update all icon previews in the popup to show the new color
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                foreach (var si in allIconElements) si.Foreground = brush;

                // Update swatch borders
                foreach (System.Windows.Controls.Border child in colorWrap.Children)
                {
                    child.BorderThickness = new Thickness(0);
                }
                swatch.BorderThickness = new Thickness(2);
            };
            colorWrap.Children.Add(swatch);
        }
        outerPanel.Children.Add(colorWrap);

        // ── Icon categories ──
        bool first = true;
        foreach (var (category, symbols) in ProfileIconCategories)
        {
            var header = new System.Windows.Controls.TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
                Margin = new Thickness(2, first ? 10 : 8, 0, 4)
            };
            first = false;
            outerPanel.Children.Add(header);

            var wrapPanel = new System.Windows.Controls.WrapPanel { Width = 280 };
            foreach (var symName in symbols)
            {
                var symbolCapture = symName;
                if (!Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symName, out var parsedSym))
                    continue;

                var iconEl = new SymbolIcon
                {
                    Symbol = parsedSym,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                try { iconEl.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedColor)); } catch { }
                allIconElements.Add(iconEl);

                var btn = new System.Windows.Controls.Border
                {
                    Width = 34, Height = 34,
                    CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(1),
                    Child = iconEl,
                    Background = symbolCapture == currentIcon.Symbol
                        ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
                        : System.Windows.Media.Brushes.Transparent
                };
                btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                btn.MouseLeave += (_, _) =>
                {
                    var cur = _config.ProfileIcons.GetValueOrDefault(profileName)?.Symbol ?? "";
                    btn.Background = symbolCapture == cur
                        ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
                        : System.Windows.Media.Brushes.Transparent;
                };
                btn.MouseLeftButtonDown += (_, _) =>
                {
                    _config.ProfileIcons[profileName] = new ProfileIconConfig
                    {
                        Symbol = symbolCapture,
                        Color = selectedColor
                    };
                    _onConfigChanged?.Invoke(_config);
                    UpdateProfileButton();
                    iconPopup.IsOpen = false;
                };
                wrapPanel.Children.Add(btn);
            }
            outerPanel.Children.Add(wrapPanel);
        }

        var popupBorder = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = new System.Windows.Controls.ScrollViewer
            {
                Content = outerPanel,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                MaxHeight = 460
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, Opacity = 0.6, ShadowDepth = 6
            }
        };

        iconPopup.Child = popupBorder;
        iconPopup.IsOpen = true;
    }

    /// <summary>
    /// Carry over global settings from current config to a loaded profile.
    /// </summary>
    private void PreserveGlobalSettings(AppConfig loaded)
    {
        loaded.Osd = _config.Osd;
        loaded.Serial = _config.Serial;
        loaded.StartWithWindows = _config.StartWithWindows;
        loaded.HomeAssistant = _config.HomeAssistant;
        loaded.Ambience = _config.Ambience;
        loaded.Profiles = _config.Profiles;
        loaded.ProfileIcons = _config.ProfileIcons;
    }

    public void SetAmbienceSync(AmbienceSync sync)
    {
        _ambienceView.SetSync(sync);
    }

    private void SwitchToProfile(string profileName)
    {
        // Save current profile before switching
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        var loaded = ConfigManager.LoadProfile(profileName);
        if (loaded != null)
        {
            loaded.ActiveProfile = profileName;
            PreserveGlobalSettings(loaded);
            _config = loaded;
        }
        else
        {
            _config.ActiveProfile = profileName;
        }

        _onConfigChanged?.Invoke(_config);
        UpdateProfileButton();
        RefreshViews();
    }

    private void AddNewProfile()
    {
        var name = GlassDialog.Prompt("Enter profile name:", "NEW PROFILE", owner: this);
        if (!string.IsNullOrWhiteSpace(name))
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (_config.Profiles.Contains(name))
            {
                GlassDialog.ShowWarning($"Profile \"{name}\" already exists.", owner: this);
                return;
            }

            _config.Profiles.Add(name);
            _config.ProfileIcons[name] = new ProfileIconConfig();
            _config.ActiveProfile = name;
            _onConfigChanged?.Invoke(_config);
            ConfigManager.SaveProfile(_config, name);
            UpdateProfileButton();
            RefreshViews();
        }
    }

    private void DeleteProfile(string profileName)
    {
        ProfilePopup.IsOpen = false;

        if (!GlassDialog.Confirm($"Delete profile \"{profileName}\"?", "DELETE PROFILE", dangerYes: true, owner: this))
            return;

        _config.Profiles.Remove(profileName);
        _config.ProfileIcons.Remove(profileName);

        // Delete profile file
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp");
        var safe = string.Concat(profileName.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        var path = System.IO.Path.Combine(configDir, $"profile_{safe}.json");
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }

        // Switch to Default if we deleted the active profile
        if (_config.ActiveProfile == profileName)
            SwitchToProfile("Default");
        else
        {
            _onConfigChanged?.Invoke(_config);
            UpdateProfileButton();
        }
    }

    /// <summary>
    /// Called externally when profiles list changes (new/delete from Settings).
    /// </summary>
    public void RefreshProfilePicker()
    {
        UpdateProfileButton();
    }

    /// <summary>
    /// Forward an immediate knob position update to the MixerView (bypasses the 50ms poll).
    /// </summary>
    public void UpdateKnobPosition(int idx, float position)
    {
        _mixerView.UpdateKnobPosition(idx, position);
    }

    public void SetConnectionStatus(bool connected, string? portName = null)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionDot.Fill = connected
                ? (SolidColorBrush)FindResource("SuccessGrnBrush")
                : (SolidColorBrush)FindResource("TextDimBrush");
            ConnectionLabel.Text = connected ? "Connected" : "Disconnected";
            _settingsView.UpdateConnectionStatus(connected, portName);

            // Glow the dot when connected
            ConnectionDotGlow.BlurRadius = connected ? 8 : 0;
            ConnectionDotGlow.Opacity = connected ? 0.5 : 0;

            var pulse = (System.Windows.Media.Animation.Storyboard)FindResource("PulseAnimation");
            if (connected)
                pulse.Begin(this, true);
            else
            {
                pulse.Stop(this);
                ConnectionDot.Opacity = 1.0;
            }
        });
    }
}
