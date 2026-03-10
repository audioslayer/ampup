using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using WolfMixer.Views;

namespace WolfMixer;

public partial class MainWindow : FluentWindow
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();
    private readonly HomeAssistantView _haView = new();

    private System.Windows.Controls.Button? _activeNavButton;

    private AppConfig _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onConfigChanged;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/ampuplogo.png", UriKind.Absolute));

        _config = ConfigManager.Load();
        VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
        UpdateProfileButton();
        NavigateTo(_mixerView, NavMixer);
        SetupTrafficLightHovers();

        // Silent startup update check
        Loaded += async (_, _) => await CheckForUpdateOnStartup();
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

    private void RefreshViews()
    {
        Action<AppConfig> saveHandler = cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        };

        _settingsView.LoadConfig(_config, saveHandler);
        _lightsView.LoadConfig(_config, saveHandler);
        _buttonsView.LoadConfig(_config, _mixer!, saveHandler);
        _mixerView.LoadConfig(_config, _mixer!, saveHandler);
        _haView.LoadConfig(_config, saveHandler);
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
        });
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
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;

        // Update sidebar highlight (icon + label)
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var dimIcon = (SolidColorBrush)FindResource("TextSecBrush");
        var dimLabel = (SolidColorBrush)FindResource("TextDimBrush");

        if (_activeNavButton != null)
        {
            var (oldIcon, oldLabel) = FindNavChildren(_activeNavButton);
            if (oldIcon != null) oldIcon.Foreground = dimIcon;
            if (oldLabel != null) oldLabel.Foreground = dimLabel;
        }

        var (newIcon, newLabel) = FindNavChildren(navButton);
        if (newIcon != null) newIcon.Foreground = accent;
        if (newLabel != null) newLabel.Foreground = accent;

        _activeNavButton = navButton;
    }

    private static (SymbolIcon? Icon, System.Windows.Controls.TextBlock? Label) FindNavChildren(System.Windows.Controls.Button button)
    {
        if (button.Content is System.Windows.Controls.StackPanel sp)
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
        else
        {
            DragMove();
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
                var result = System.Windows.MessageBox.Show(
                    $"A new version ({tag}) is available. Download and install?",
                    "Amp Up Update",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);

                if (result == System.Windows.MessageBoxResult.Yes)
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

    // Emoji options for profile picker
    private static readonly string[] ProfileEmojiOptions =
    {
        "🎛", "🎮", "🎧", "🎵", "🎤", "🔊", "💻", "🖥", "📺", "🎬",
        "🏠", "🌙", "☀️", "🔥", "❄️", "⚡", "🎯", "🚀", "💡", "🎨",
        "🏢", "🎸", "🥁", "🎹", "🎻", "📻", "🎙", "📡", "⭐", "💎"
    };

    private void UpdateProfileButton()
    {
        var emoji = _config.ProfileEmojis.GetValueOrDefault(_config.ActiveProfile, "🎛");
        ProfileEmoji.Text = emoji;
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
            var emoji = _config.ProfileEmojis.GetValueOrDefault(profile, "🎛");

            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            // Emoji button (clickable to change emoji)
            var emojiBlock = new System.Windows.Controls.TextBlock
            {
                Text = emoji,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Change icon",
                Margin = new Thickness(0, 0, 8, 0)
            };
            emojiBlock.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                ShowEmojiPicker(profileCapture, emojiBlock);
            };
            System.Windows.Controls.Grid.SetColumn(emojiBlock, 0);
            row.Children.Add(emojiBlock);

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
                    Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "Delete profile"
                };
                deleteBtn.MouseEnter += (_, _) => deleteBtn.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
                deleteBtn.MouseLeave += (_, _) => deleteBtn.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
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

    private void ShowEmojiPicker(string profileName, System.Windows.Controls.TextBlock emojiTarget)
    {
        // Close profile popup, show emoji picker popup
        ProfilePopup.IsOpen = false;

        var emojiPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = ProfileButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            HorizontalOffset = 4
        };

        var wrapPanel = new System.Windows.Controls.WrapPanel
        {
            Width = 220,
            Margin = new Thickness(4)
        };

        foreach (var em in ProfileEmojiOptions)
        {
            var emojiCapture = em;
            var btn = new System.Windows.Controls.Border
            {
                Width = 36, Height = 36,
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2)
            };
            var txt = new System.Windows.Controls.TextBlock
            {
                Text = em,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Child = txt;
            btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            btn.MouseLeave += (_, _) => btn.Background = System.Windows.Media.Brushes.Transparent;
            btn.MouseLeftButtonDown += (_, _) =>
            {
                _config.ProfileEmojis[profileName] = emojiCapture;
                _onConfigChanged?.Invoke(_config);
                UpdateProfileButton();
                emojiPopup.IsOpen = false;
            };
            wrapPanel.Children.Add(btn);
        }

        var popupBorder = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = wrapPanel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, Opacity = 0.6, ShadowDepth = 6
            }
        };

        emojiPopup.Child = popupBorder;
        emojiPopup.IsOpen = true;
    }

    private void SwitchToProfile(string profileName)
    {
        // Save current profile before switching
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        var loaded = ConfigManager.LoadProfile(profileName);
        if (loaded != null)
        {
            loaded.ActiveProfile = profileName;
            loaded.Profiles = _config.Profiles;
            loaded.ProfileEmojis = _config.ProfileEmojis;
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
        var dialog = new InputDialog("New Profile", "Enter profile name:");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var name = dialog.ResponseText.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (_config.Profiles.Contains(name))
            {
                System.Windows.MessageBox.Show($"Profile \"{name}\" already exists.",
                    "Amp Up", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            _config.Profiles.Add(name);
            _config.ProfileEmojis[name] = "🎛";
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

        var result = System.Windows.MessageBox.Show(
            $"Delete profile \"{profileName}\"?",
            "Amp Up", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        _config.Profiles.Remove(profileName);
        _config.ProfileEmojis.Remove(profileName);

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

    public void SetConnectionStatus(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionDot.Fill = connected
                ? (SolidColorBrush)FindResource("SuccessGrnBrush")
                : (SolidColorBrush)FindResource("TextDimBrush");
            ConnectionLabel.Text = connected ? "Connected" : "Disconnected";

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
