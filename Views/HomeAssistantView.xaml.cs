using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AmpUp.Views;

public partial class HomeAssistantView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private HAIntegration? _ha;
    private bool _loading;

    // Connection status
    private TextBlock _statusLabel = null!;
    private Border _statusDot = null!;
    private Button _testBtn = null!;

    // Knob binding controls
    private readonly ComboBox[] _knobTypeCombos = new ComboBox[5];
    private readonly ComboBox[] _knobEntityCombos = new ComboBox[5];
    private readonly TextBlock[] _knobLabels = new TextBlock[5];

    // Button binding controls
    private readonly ComboBox[] _btnActionCombos = new ComboBox[5];
    private readonly ComboBox[] _btnEntityCombos = new ComboBox[5];

    // Entity cache
    private List<HAEntity> _entities = new();

    // HA target types for knobs
    private static readonly string[] KnobTypes = { "(none)", "Light Brightness", "Media Volume", "Fan Speed", "Cover Position" };
    private static readonly string[] KnobTypePrefixes = { "", "ha_light", "ha_media", "ha_fan", "ha_cover" };
    private static readonly string[] KnobTypeDomains = { "", "light", "media_player", "fan", "cover" };

    // HA button action types
    private static readonly string[] BtnActions = { "(none)", "Toggle Entity", "Activate Scene", "Call Service" };
    private static readonly string[] BtnActionValues = { "none", "ha_toggle", "ha_scene", "ha_service" };

    public HomeAssistantView()
    {
        InitializeComponent();
        BuildUI();
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // Create/update HA client
        if (_ha == null)
            _ha = new HAIntegration(config.HomeAssistant);
        else
            _ha.UpdateConfig(config.HomeAssistant);

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

            // Check if current target is an HA target
            if (knob.Target.StartsWith("ha_"))
            {
                var (type, entityId) = HAIntegration.ParseTarget(knob.Target);
                int typeIdx = Array.IndexOf(KnobTypePrefixes, $"ha_{type}");
                _knobTypeCombos[i].SelectedIndex = typeIdx >= 0 ? typeIdx : 0;
                // Entity will be set after fetch
                _knobEntityCombos[i].Tag = entityId; // stash for later
            }
            else
            {
                _knobTypeCombos[i].SelectedIndex = 0;
                _knobEntityCombos[i].Tag = null;
            }
        }

        // Load button bindings
        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            int actionIdx = Array.IndexOf(BtnActionValues, btn.Action);
            if (actionIdx >= 0)
            {
                _btnActionCombos[i].SelectedIndex = actionIdx;
                _btnEntityCombos[i].Tag = btn.Path; // stash entity_id
            }
            else
            {
                _btnActionCombos[i].SelectedIndex = 0;
            }
        }

        _loading = false;

        // Fetch entities if connected
        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
            _ = FetchEntitiesAsync();
    }

    private async Task FetchEntitiesAsync()
    {
        if (_ha == null) return;

        try
        {
            var connected = await _ha.TestConnectionAsync();
            Dispatcher.Invoke(() => SetConnectionDot(connected));

            if (!connected) return;

            _entities = await _ha.GetEntitiesAsync();

            Dispatcher.Invoke(() =>
            {
                // Populate knob entity combos based on selected type
                for (int i = 0; i < 5; i++)
                    PopulateKnobEntities(i);

                // Populate button entity combos
                for (int i = 0; i < 5; i++)
                    PopulateButtonEntities(i);

                _statusLabel.Text = $"Connected — {_entities.Count} entities";
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"HA fetch failed: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                SetConnectionDot(false);
                _statusLabel.Text = "Connection failed";
            });
        }
    }

    private void PopulateKnobEntities(int idx)
    {
        var combo = _knobEntityCombos[idx];
        var typeIdx = _knobTypeCombos[idx].SelectedIndex;
        combo.Items.Clear();

        if (typeIdx <= 0 || typeIdx >= KnobTypeDomains.Length)
        {
            combo.Visibility = Visibility.Collapsed;
            return;
        }

        combo.Visibility = Visibility.Visible;
        var domain = KnobTypeDomains[typeIdx];
        var filtered = _entities.Where(e => e.Domain == domain).OrderBy(e => e.FriendlyName).ToList();

        foreach (var entity in filtered)
            combo.Items.Add(new ComboBoxItem { Content = entity.FriendlyName, Tag = entity.EntityId });

        // Restore stashed selection
        var stashedId = combo.Tag as string;
        if (!string.IsNullOrEmpty(stashedId))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag as string == stashedId)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
            combo.Tag = null;
        }
    }

    private void PopulateButtonEntities(int idx)
    {
        var combo = _btnEntityCombos[idx];
        var actionIdx = _btnActionCombos[idx].SelectedIndex;
        combo.Items.Clear();

        if (actionIdx <= 0)
        {
            combo.Visibility = Visibility.Collapsed;
            return;
        }

        combo.Visibility = Visibility.Visible;

        // Filter entities by action type
        List<HAEntity> filtered;
        var actionVal = BtnActionValues[actionIdx];
        if (actionVal == "ha_scene")
            filtered = _entities.Where(e => e.Domain == "scene").OrderBy(e => e.FriendlyName).ToList();
        else if (actionVal == "ha_toggle")
            filtered = _entities.Where(e => e.Domain is "light" or "switch" or "fan" or "cover" or "media_player" or "input_boolean")
                .OrderBy(e => e.FriendlyName).ToList();
        else
            filtered = _entities.OrderBy(e => e.FriendlyName).ToList();

        foreach (var entity in filtered)
            combo.Items.Add(new ComboBoxItem { Content = $"[{entity.Domain}] {entity.FriendlyName}", Tag = entity.EntityId });

        // Restore stashed selection
        var stashedId = combo.Tag as string;
        if (!string.IsNullOrEmpty(stashedId))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag as string == stashedId)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
            combo.Tag = null;
        }
    }

    private void UpdateConnectionStatus()
    {
        if (_config == null) return;

        if (!_config.HomeAssistant.Enabled)
        {
            _statusLabel.Text = "Disabled — enable in Settings";
            SetConnectionDot(false);
        }
        else if (string.IsNullOrWhiteSpace(_config.HomeAssistant.Token))
        {
            _statusLabel.Text = "No token configured";
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

        // Save knob HA targets
        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob == null) continue;

            var typeIdx = _knobTypeCombos[i].SelectedIndex;
            if (typeIdx <= 0)
            {
                // Only clear if it was previously an HA target
                if (knob.Target.StartsWith("ha_"))
                    knob.Target = "none";
                continue;
            }

            var entityId = GetSelectedEntityId(_knobEntityCombos[i]);
            if (!string.IsNullOrEmpty(entityId))
                knob.Target = $"{KnobTypePrefixes[typeIdx]}:{entityId}";
        }

        // Save button HA actions
        for (int i = 0; i < 5; i++)
        {
            var btn = _config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            var actionIdx = _btnActionCombos[i].SelectedIndex;
            if (actionIdx <= 0)
            {
                // Only clear if it was previously an HA action
                if (btn.Action.StartsWith("ha_"))
                {
                    btn.Action = "none";
                    btn.Path = "";
                }
                continue;
            }

            var entityId = GetSelectedEntityId(_btnEntityCombos[i]);
            btn.Action = BtnActionValues[actionIdx];
            btn.Path = entityId;
        }

        _onSave(_config);
    }

    private static string GetSelectedEntityId(ComboBox combo)
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
            Text = "HOME ASSISTANT",
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
            if (_ha != null)
            {
                var ok = await _ha.TestConnectionAsync();
                SetConnectionDot(ok);
                _statusLabel.Text = ok ? "Connected" : "Connection failed";
                if (ok) await FetchEntitiesAsync();
            }
            _testBtn.IsEnabled = true;
        };
        statusRow.Children.Add(_testBtn);

        var refreshBtn = new Button
        {
            Content = "Refresh Entities",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0)
        };
        refreshBtn.Click += async (_, _) =>
        {
            refreshBtn.IsEnabled = false;
            await FetchEntitiesAsync();
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
            Text = "Bind knobs to Home Assistant entities. These override the Mixer tab target.",
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

            // Type combo
            col.Children.Add(MakeSmallLabel("TYPE"));
            var typeCombo = new ComboBox
            {
                ItemsSource = KnobTypes,
                SelectedIndex = 0,
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            typeCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                PopulateKnobEntities(idx);
                SaveBindings();
            };
            _knobTypeCombos[i] = typeCombo;
            col.Children.Add(typeCombo);

            // Entity combo
            col.Children.Add(MakeSmallLabel("ENTITY"));
            var entityCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Visibility = Visibility.Collapsed
            };
            entityCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                SaveBindings();
            };
            _knobEntityCombos[i] = entityCombo;
            col.Children.Add(entityCombo);

            Grid.SetColumn(col, i);
            knobGrid.Children.Add(col);
        }

        knobContent.Children.Add(knobGrid);
        knobCard.Child = knobContent;
        RootPanel.Children.Add(knobCard);

        // --- Button Bindings Card ---
        var btnCard = MakeCard();
        var btnContent = new StackPanel();
        btnContent.Children.Add(new TextBlock
        {
            Text = "BUTTON BINDINGS",
            Style = FindStyle("HeaderText"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        btnContent.Children.Add(new TextBlock
        {
            Text = "Bind button presses to Home Assistant actions. These override the Buttons tab action.",
            Style = FindStyle("SecondaryText"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var btnGrid = new Grid();
        for (int i = 0; i < 5; i++)
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var col = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 4 ? 0 : 4, 0) };

            col.Children.Add(new TextBlock
            {
                Text = $"Button {i + 1}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Action combo
            col.Children.Add(MakeSmallLabel("ACTION"));
            var actionCombo = new ComboBox
            {
                ItemsSource = BtnActions,
                SelectedIndex = 0,
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            actionCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                PopulateButtonEntities(idx);
                SaveBindings();
            };
            _btnActionCombos[i] = actionCombo;
            col.Children.Add(actionCombo);

            // Entity combo
            col.Children.Add(MakeSmallLabel("ENTITY"));
            var entityCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Visibility = Visibility.Collapsed
            };
            entityCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                SaveBindings();
            };
            _btnEntityCombos[i] = entityCombo;
            col.Children.Add(entityCombo);

            Grid.SetColumn(col, i);
            btnGrid.Children.Add(col);
        }

        btnContent.Children.Add(btnGrid);
        btnCard.Child = btnContent;
        RootPanel.Children.Add(btnCard);
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
