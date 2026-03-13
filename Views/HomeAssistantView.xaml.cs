using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmpUp.Views;

public partial class HomeAssistantView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private HAIntegration? _ha;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Connection status
    private TextBlock _statusLabel = null!;
    private Border _statusDot = null!;
    private Button _testBtn = null!;
    private Button _refreshBtn = null!;

    // Per-column header labels
    private readonly TextBlock[] _headerLabels = new TextBlock[5];
    private readonly TextBlock[] _headerIcons = new TextBlock[5];
    private readonly TextBlock[] _headerTypes = new TextBlock[5];

    // Knob binding controls
    private readonly ComboBox[] _knobTypeCombos = new ComboBox[5];
    private readonly ComboBox[] _knobEntityCombos = new ComboBox[5];
    private readonly StackPanel[] _knobEntityPanels = new StackPanel[5];

    // Button binding controls
    private readonly ComboBox[] _btnActionCombos = new ComboBox[5];
    private readonly ComboBox[] _btnEntityCombos = new ComboBox[5];
    private readonly StackPanel[] _btnEntityPanels = new StackPanel[5];

    // Entity cache
    private List<HAEntity> _entities = new();

    private static (string Icon, Color Color) GetDomainStyle(string domain)
        => HADomainStyles.GetStyle(domain);

    // HA target types for knobs
    private static readonly (string Display, string Value, string Domain, string Icon)[] KnobTypes =
    {
        ("None", "", "", "—"),
        ("Light Brightness", "ha_light", "light", "\U0001F4A1"),
        ("Media Volume", "ha_media", "media_player", "\U0001F50A"),
        ("Fan Speed", "ha_fan", "fan", "\U0001F32C"),
        ("Cover Position", "ha_cover", "cover", "\U0001F6AA"),
    };

    // HA button action types
    private static readonly (string Display, string Value, string Icon)[] BtnActions =
    {
        ("None", "none", "—"),
        ("Toggle Entity", "ha_toggle", "\u26A1"),
        ("Activate Scene", "ha_scene", "\U0001F3AC"),
        ("Call Service", "ha_service", "\u2699"),
    };

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    public HomeAssistantView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SaveBindings();
        };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildConnectionCard();
        BuildColumns();
    }

    public HAIntegration? GetHA() => _ha;

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

            // Update header label
            var label = !string.IsNullOrWhiteSpace(knob.Label) ? knob.Label : FormatTargetName(knob.Target);
            _headerLabels[i].Text = label;

            // Check if current target is an HA target
            if (knob.Target.StartsWith("ha_"))
            {
                var (type, entityId) = HAIntegration.ParseTarget(knob.Target);
                int typeIdx = -1;
                for (int t = 0; t < KnobTypes.Length; t++)
                    if (KnobTypes[t].Value == $"ha_{type}") { typeIdx = t; break; }
                _knobTypeCombos[i].SelectedIndex = typeIdx >= 0 ? typeIdx : 0;
                _knobEntityCombos[i].Tag = entityId; // stash for later
                UpdateKnobHeader(i);
            }
            else
            {
                _knobTypeCombos[i].SelectedIndex = 0;
                _knobEntityCombos[i].Tag = null;
                UpdateKnobHeader(i);
            }
        }

        // Load button bindings
        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            int actionIdx = -1;
            for (int a = 0; a < BtnActions.Length; a++)
                if (BtnActions[a].Value == btn.Action) { actionIdx = a; break; }

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

    // ── Connection Card ─────────────────────────────────────────────

    private void BuildConnectionCard()
    {
        var grid = ConnContent;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Header row: accent bar + title + status dot
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

        var accentBar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        headerRow.Children.Add(accentBar);

        var title = new TextBlock
        {
            Text = "HOME ASSISTANT",
            Style = FindStyle("HeaderText"),
        };
        headerRow.Children.Add(title);

        _statusDot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(_statusDot);

        _statusLabel = new TextBlock
        {
            Text = "Not configured",
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        headerRow.Children.Add(_statusLabel);

        Grid.SetRow(headerRow, 0);
        Grid.SetColumnSpan(headerRow, 2);
        grid.Children.Add(headerRow);

        // Buttons row
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

        _testBtn = new Button
        {
            Content = "Test Connection",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
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
        btnRow.Children.Add(_testBtn);

        _refreshBtn = new Button
        {
            Content = "Refresh Entities",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
        };
        _refreshBtn.Click += async (_, _) =>
        {
            _refreshBtn.IsEnabled = false;
            await FetchEntitiesAsync();
            _refreshBtn.IsEnabled = true;
        };
        btnRow.Children.Add(_refreshBtn);

        Grid.SetRow(btnRow, 1);
        Grid.SetColumnSpan(btnRow, 2);
        grid.Children.Add(btnRow);
    }

    // ── Build 5 columns ─────────────────────────────────────────────

    private void BuildColumns()
    {
        var grids = new[] { Ch0Grid, Ch1Grid, Ch2Grid, Ch3Grid, Ch4Grid };

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var grid = grids[i];

            // Define rows: Header | Sep | KNOB | Sep | BUTTON
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Header" });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sep
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Knob" });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sep
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Button" });

            // ── Row 0: Header ──
            var headerStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var headerLabel = new TextBlock
            {
                Text = $"CH {i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            _headerLabels[i] = headerLabel;
            headerStack.Children.Add(headerLabel);

            var headerIcon = new TextBlock
            {
                Text = "\U0001F3E0", // house icon
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
            };
            _headerIcons[i] = headerIcon;
            headerStack.Children.Add(headerIcon);

            var headerType = new TextBlock
            {
                Text = "Not Bound",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            _headerTypes[i] = headerType;
            headerStack.Children.Add(headerType);

            Grid.SetRow(headerStack, 0);
            grid.Children.Add(headerStack);

            // ── Row 1: Sep ──
            var sep1 = MakeSeparator(12);
            Grid.SetRow(sep1, 1);
            grid.Children.Add(sep1);

            // ── Row 2: KNOB section ──
            var knobSection = new StackPanel();
            knobSection.Children.Add(MakeSectionHeader("KNOB"));

            // Type combo with icons
            var typeCombo = MakeKnobTypeCombo();
            typeCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                PopulateKnobEntities(idx);
                UpdateKnobHeader(idx);
                QueueSave();
            };
            _knobTypeCombos[i] = typeCombo;
            knobSection.Children.Add(typeCombo);

            // Entity combo (hidden until type selected)
            var entityPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            entityPanel.Children.Add(MakeLabel("ENTITY"));
            var entityCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
            };
            entityCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                QueueSave();
            };
            _knobEntityCombos[i] = entityCombo;
            _knobEntityPanels[i] = entityPanel;
            entityPanel.Children.Add(entityCombo);
            knobSection.Children.Add(entityPanel);

            Grid.SetRow(knobSection, 2);
            grid.Children.Add(knobSection);

            // ── Row 3: Sep ──
            var sep2 = MakeSeparator(14);
            Grid.SetRow(sep2, 3);
            grid.Children.Add(sep2);

            // ── Row 4: BUTTON section ──
            var btnSection = new StackPanel();
            btnSection.Children.Add(MakeSectionHeader("BUTTON"));

            // Action combo with icons
            var actionCombo = MakeBtnActionCombo();
            actionCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                PopulateButtonEntities(idx);
                QueueSave();
            };
            _btnActionCombos[i] = actionCombo;
            btnSection.Children.Add(actionCombo);

            // Entity combo (hidden until action selected)
            var btnEntityPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            btnEntityPanel.Children.Add(MakeLabel("ENTITY"));
            var btnEntityCombo = new ComboBox
            {
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
            };
            btnEntityCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                QueueSave();
            };
            _btnEntityCombos[i] = btnEntityCombo;
            _btnEntityPanels[i] = btnEntityPanel;
            btnEntityPanel.Children.Add(btnEntityCombo);
            btnSection.Children.Add(btnEntityPanel);

            Grid.SetRow(btnSection, 4);
            grid.Children.Add(btnSection);
        }
    }

    // ── Header display update ──────────────────────────────────────

    private void UpdateKnobHeader(int idx)
    {
        var typeIdx = _knobTypeCombos[idx].SelectedIndex;
        if (typeIdx <= 0)
        {
            _headerIcons[idx].Text = "\U0001F3E0";
            _headerIcons[idx].Foreground = FindBrush("TextDimBrush");
            _headerTypes[idx].Text = "Not Bound";
            _headerTypes[idx].Foreground = FindBrush("TextDimBrush");
        }
        else
        {
            var knobType = KnobTypes[typeIdx];
            var (domainIcon, domainColor) = GetDomainStyle(knobType.Domain);
            _headerIcons[idx].Text = domainIcon;
            _headerIcons[idx].Foreground = new SolidColorBrush(domainColor);
            _headerTypes[idx].Text = knobType.Display;
            _headerTypes[idx].Foreground = new SolidColorBrush(Color.FromArgb(0xCC, domainColor.R, domainColor.G, domainColor.B));
        }
    }

    // ── Control factories ───────────────────────────────────────────

    private Grid MakeSectionHeader(string title)
    {
        var accent = ThemeManager.Accent;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        _sectionHeaders.Add((bar, label));
        return grid;
    }

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            bar.Background = new SolidColorBrush(accent);
            label.Foreground = new SolidColorBrush(accent);
        }
    }

    private Border MakeSeparator(int spacing = 10)
    {
        return new Border
        {
            Height = 1,
            Background = FindBrush("CardBorderBrush"),
            Margin = new Thickness(0, spacing, 0, spacing),
        };
    }

    private ComboBox MakeKnobTypeCombo()
    {
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
        };

        foreach (var (display, _, _, icon) in KnobTypes)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"{icon}  {display}",
                Tag = display,
            });
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private ComboBox MakeBtnActionCombo()
    {
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
        };

        foreach (var (display, _, icon) in BtnActions)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"{icon}  {display}",
                Tag = display,
            });
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3),
        };
    }

    // ── Entity management ───────────────────────────────────────────

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
                for (int i = 0; i < 5; i++)
                {
                    PopulateKnobEntities(i);
                    PopulateButtonEntities(i);
                }
                _statusLabel.Text = $"Connected \u2014 {_entities.Count} entities";
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

        if (typeIdx <= 0 || typeIdx >= KnobTypes.Length)
        {
            _knobEntityPanels[idx].Visibility = Visibility.Collapsed;
            return;
        }

        _knobEntityPanels[idx].Visibility = Visibility.Visible;
        var domain = KnobTypes[typeIdx].Domain;
        var filtered = _entities.Where(e => e.Domain == domain).OrderBy(e => e.FriendlyName).ToList();

        foreach (var entity in filtered)
        {
            var (dIcon, dColor) = HADomainStyles.GetStyle(entity.EntityId);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = dIcon, Foreground = new SolidColorBrush(dColor), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = entity.FriendlyName, VerticalAlignment = VerticalAlignment.Center });
            combo.Items.Add(new ComboBoxItem { Content = row, Tag = entity.EntityId });
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
            _btnEntityPanels[idx].Visibility = Visibility.Collapsed;
            return;
        }

        _btnEntityPanels[idx].Visibility = Visibility.Visible;

        var actionVal = BtnActions[actionIdx].Value;
        List<HAEntity> filtered;
        if (actionVal == "ha_scene")
            filtered = _entities.Where(e => e.Domain == "scene").OrderBy(e => e.FriendlyName).ToList();
        else if (actionVal == "ha_toggle")
            filtered = _entities.Where(e => e.Domain is "light" or "switch" or "fan" or "cover" or "media_player" or "input_boolean")
                .OrderBy(e => e.FriendlyName).ToList();
        else
            filtered = _entities.OrderBy(e => e.FriendlyName).ToList();

        foreach (var entity in filtered)
        {
            var (dIcon, dColor) = HADomainStyles.GetStyle(entity.EntityId);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = dIcon, Foreground = new SolidColorBrush(dColor), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = entity.FriendlyName, VerticalAlignment = VerticalAlignment.Center });
            combo.Items.Add(new ComboBoxItem { Content = row, Tag = entity.EntityId });
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
                    break;
                }
            }
            combo.Tag = null;
        }
    }

    // ── Connection status ───────────────────────────────────────────

    private void UpdateConnectionStatus()
    {
        if (_config == null) return;

        if (!_config.HomeAssistant.Enabled)
        {
            _statusLabel.Text = "Disabled \u2014 enable in Settings";
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

    // ── Save ────────────────────────────────────────────────────────

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
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
                if (knob.Target.StartsWith("ha_"))
                    knob.Target = "none";
                continue;
            }

            var entityId = GetSelectedEntityId(_knobEntityCombos[i]);
            if (!string.IsNullOrEmpty(entityId))
                knob.Target = $"{KnobTypes[typeIdx].Value}:{entityId}";
        }

        // Save button HA actions
        for (int i = 0; i < 5; i++)
        {
            var btn = _config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            var actionIdx = _btnActionCombos[i].SelectedIndex;
            if (actionIdx <= 0)
            {
                if (btn.Action.StartsWith("ha_"))
                {
                    btn.Action = "none";
                    btn.Path = "";
                }
                continue;
            }

            var entityId = GetSelectedEntityId(_btnEntityCombos[i]);
            btn.Action = BtnActions[actionIdx].Value;
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

    // ── Resource helpers ────────────────────────────────────────────

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

/// <summary>
/// Shared domain icon + color styles for Home Assistant entities.
/// Uses Unicode symbols (not emoji) so WPF renders them with Foreground color.
/// </summary>
public static class HADomainStyles
{
    public static readonly Dictionary<string, (string Icon, Color Color)> Domains = new()
    {
        { "light",         ("\u2600", Color.FromRgb(0xFF, 0xD5, 0x4F)) }, // ☀ sun — yellow
        { "switch",        ("\u23FB", Color.FromRgb(0x42, 0xA5, 0xF5)) }, // ⏻ power — blue
        { "scene",         ("\u25BA", Color.FromRgb(0xFF, 0xA7, 0x26)) }, // ► play — orange
        { "fan",           ("\u2732", Color.FromRgb(0x26, 0xC6, 0xDA)) }, // ✲ asterisk — teal
        { "climate",       ("\u2668", Color.FromRgb(0xEF, 0x53, 0x50)) }, // ♨ hot springs — red
        { "media_player",  ("\u266B", Color.FromRgb(0x66, 0xBB, 0x6A)) }, // ♫ music — green
        { "cover",         ("\u2261", Color.FromRgb(0xAB, 0x47, 0xBC)) }, // ≡ bars — purple
        { "automation",    ("\u26A1", Color.FromRgb(0xFF, 0xB7, 0x4D)) }, // ⚡ lightning — amber
        { "script",        ("\u25B6", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ▶ play — grey
        { "input_boolean", ("\u25C6", Color.FromRgb(0x29, 0xB6, 0xF6)) }, // ◆ diamond — blue
        { "lock",          ("\u2302", Color.FromRgb(0xFF, 0xD5, 0x4F)) }, // ⌂ house — gold
        { "sensor",        ("\u25A0", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ■ square — grey
        { "binary_sensor", ("\u25CF", Color.FromRgb(0x78, 0x90, 0x9C)) }, // ● circle — grey
        { "button",        ("\u25C9", Color.FromRgb(0x42, 0xA5, 0xF5)) }, // ◉ fisheye — blue
    };

    public static (string Icon, Color Color) GetStyle(string entityIdOrDomain)
    {
        var domain = entityIdOrDomain.Contains('.') ? entityIdOrDomain.Split('.')[0] : entityIdOrDomain;
        return Domains.TryGetValue(domain, out var style) ? style : ("\u25CF", Color.FromRgb(0x88, 0x88, 0x88));
    }
}
