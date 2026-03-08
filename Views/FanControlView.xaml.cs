using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WolfMixer.Views;

public partial class FanControlView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private FanController? _fc;
    private bool _loading;

    // Connection status
    private TextBlock _statusLabel = null!;
    private Border _statusDot = null!;
    private Button _testBtn = null!;

    // Knob binding controls
    private readonly ComboBox[] _knobControllerCombos = new ComboBox[5];
    private readonly TextBlock[] _knobLabels = new TextBlock[5];

    // Controller cache
    private List<FanControlSensor> _controllers = new();

    public FanControlView()
    {
        InitializeComponent();
        BuildUI();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // Create/update FC client
        if (_fc == null)
            _fc = new FanController(config.FanControl);
        else
            _fc.UpdateConfig(config.FanControl);

        // Update connection status
        UpdateConnectionStatus();

        // Load knob bindings
        for (int i = 0; i < 5; i++)
        {
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            // Update knob label
            var label = !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : FormatTargetName(knob.Target);
            _knobLabels[i].Text = label;

            // Check if current target is an FC target
            if (knob.Target.StartsWith("fc_fan:"))
            {
                var controlId = FanController.ParseTarget(knob.Target);
                _knobControllerCombos[i].Tag = controlId; // stash for later
            }
            else
            {
                _knobControllerCombos[i].Tag = null;
            }
        }

        _loading = false;

        // Fetch controllers if connected
        if (config.FanControl.Enabled && !string.IsNullOrWhiteSpace(config.FanControl.Url))
            _ = FetchControllersAsync();
    }

    private async Task FetchControllersAsync()
    {
        if (_fc == null) return;

        try
        {
            var connected = await _fc.TestConnectionAsync();
            Dispatcher.Invoke(() => SetConnectionDot(connected));

            if (!connected) return;

            _controllers = await _fc.GetControllersAsync();

            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < 5; i++)
                    PopulateControllerCombo(i);

                _statusLabel.Text = $"Connected — {_controllers.Count} controllers";
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"FanControl fetch failed: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                SetConnectionDot(false);
                _statusLabel.Text = "Connection failed";
            });
        }
    }

    private void PopulateControllerCombo(int idx)
    {
        var combo = _knobControllerCombos[idx];
        combo.Items.Clear();

        // Add "(none)" option
        combo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });

        foreach (var ctrl in _controllers.OrderBy(c => c.Name))
        {
            var pct = (int)Math.Round(ctrl.Value);
            combo.Items.Add(new ComboBoxItem { Content = $"{ctrl.Name} ({pct}%)", Tag = ctrl.Id });
        }

        // Restore stashed selection
        var stashedId = combo.Tag as string;
        if (!string.IsNullOrEmpty(stashedId))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag as string == stashedId)
                {
                    combo.SelectedIndex = i;
                    combo.Tag = null;
                    return;
                }
            }
            combo.Tag = null;
        }

        if (combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
    }

    private void UpdateConnectionStatus()
    {
        if (_config == null) return;

        if (!_config.FanControl.Enabled)
        {
            _statusLabel.Text = "Disabled — enable in Settings";
            SetConnectionDot(false);
        }
        else if (string.IsNullOrWhiteSpace(_config.FanControl.Url))
        {
            _statusLabel.Text = "No URL configured";
            SetConnectionDot(false);
        }
        else
        {
            _statusLabel.Text = "Connecting...";
        }
    }

    private void SetConnectionDot(bool connected)
    {
        _statusDot.Background = new SolidColorBrush(connected
            ? (Color)ColorConverter.ConvertFromString("#00DD77")
            : (Color)ColorConverter.ConvertFromString("#555555"));
    }

    private void SaveBindings()
    {
        if (_config == null || _onSave == null || _loading) return;

        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            var controlId = GetSelectedControlId(_knobControllerCombos[i]);
            if (!string.IsNullOrEmpty(controlId))
            {
                knob.Target = $"fc_fan:{controlId}";
            }
            else
            {
                // Only clear if it was previously an FC target
                if (knob.Target.StartsWith("fc_"))
                    knob.Target = "none";
            }
        }

        _onSave(_config);
    }

    private static string GetSelectedControlId(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "";
        return "";
    }

    private void BuildUI()
    {
        // --- Connection Status Card ---
        var connCard = MakeCard();
        var connHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        _statusDot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        connHeader.Children.Add(_statusDot);

        var connTitle = new TextBlock
        {
            Text = "FAN CONTROL",
            Style = FindStyle("HeaderText")
        };
        connHeader.Children.Add(connTitle);

        var connContent = new StackPanel();
        connContent.Children.Add(connHeader);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        _statusLabel = new TextBlock
        {
            Text = "Not configured",
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        statusRow.Children.Add(_statusLabel);

        _testBtn = new Button
        {
            Content = "Test Connection",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12
        };
        _testBtn.Click += async (_, _) =>
        {
            _testBtn.IsEnabled = false;
            _statusLabel.Text = "Testing...";
            if (_fc != null)
            {
                var ok = await _fc.TestConnectionAsync();
                SetConnectionDot(ok);
                _statusLabel.Text = ok ? "Connected" : "Connection failed";
                if (ok) await FetchControllersAsync();
            }
            _testBtn.IsEnabled = true;
        };
        statusRow.Children.Add(_testBtn);

        var refreshBtn = new Button
        {
            Content = "Refresh Controllers",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0)
        };
        refreshBtn.Click += async (_, _) =>
        {
            refreshBtn.IsEnabled = false;
            await FetchControllersAsync();
            refreshBtn.IsEnabled = true;
        };
        statusRow.Children.Add(refreshBtn);

        connContent.Children.Add(statusRow);
        connCard.Child = connContent;
        RootPanel.Children.Add(connCard);

        // --- Knob Bindings Card ---
        var knobCard = MakeCard();
        var knobContent = new StackPanel();
        knobContent.Children.Add(new TextBlock
        {
            Text = "KNOB BINDINGS",
            Style = FindStyle("HeaderText"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        knobContent.Children.Add(new TextBlock
        {
            Text = "Bind knobs to fan controllers. The knob position sets the fan speed (0-100%).",
            Style = FindStyle("SecondaryText"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var knobGrid = new Grid();
        for (int i = 0; i < 5; i++)
            knobGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var col = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 4 ? 0 : 4, 0) };

            // Knob label
            var knobLabel = new TextBlock
            {
                Text = $"Knob {i + 1}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _knobLabels[i] = knobLabel;
            col.Children.Add(knobLabel);

            // Controller combo
            col.Children.Add(MakeSmallLabel("CONTROLLER"));
            var ctrlCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ctrlCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
            ctrlCombo.SelectedIndex = 0;
            ctrlCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                SaveBindings();
            };
            _knobControllerCombos[i] = ctrlCombo;
            col.Children.Add(ctrlCombo);

            Grid.SetColumn(col, i);
            knobGrid.Children.Add(col);
        }

        knobContent.Children.Add(knobGrid);
        knobCard.Child = knobContent;
        RootPanel.Children.Add(knobCard);
    }

    private Border MakeCard()
    {
        return new Border
        {
            Style = FindStyle("CardPanel"),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private TextBlock MakeSmallLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3)
        };
    }

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;

    private static string FormatTargetName(string target)
    {
        if (string.IsNullOrEmpty(target) || target == "none") return "None";
        var words = target.Replace('_', ' ').Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
