using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Core.Services;

namespace AmpUp.Views;

public partial class GroupsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Corsair integration reference
    private CorsairSync? _corsairSync;

    // HA integration reference (for entity picker)
    private HAIntegration? _ha;

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    public GroupsView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Save(); };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildTopBar();
    }

    public void SetCorsairSync(CorsairSync corsairSync)
    {
        _corsairSync = corsairSync;
    }

    public void SetHAIntegration(HAIntegration? ha)
    {
        _ha = ha;
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;
        _loading = false;

        RebuildGroupPanel();
    }

    // ── Top Bar ──────────────────────────────────────────────────────

    private TextBlock? _countLabel;

    private void BuildTopBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var (bar, title) = MakeSectionHeader("DEVICE GROUPS");
        var header = WrapHeader(bar, title);
        header.Margin = new Thickness(0, 0, 0, 4);
        left.Children.Add(header);

        _countLabel = new TextBlock
        {
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(13, 0, 0, 0),
        };
        _countLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        left.Children.Add(_countLabel);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var newBtn = BuildNewGroupButton(primary: true);
        newBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(newBtn, 1);
        grid.Children.Add(newBtn);

        Loaded += (_, _) => UpdateTopBarCount();

        TopBar.Children.Add(grid);
    }

    // ── Main Panel ───────────────────────────────────────────────────

    private void RebuildGroupPanel()
    {
        GroupPanel.Children.Clear();
        // Keep the top-bar header (index 0) registered for accent refresh
        if (_sectionHeaders.Count > 1) _sectionHeaders.RemoveRange(1, _sectionHeaders.Count - 1);

        if (_config == null) return;

        if (_config.Groups.Count == 0)
        {
            GroupPanel.Children.Add(MakeEmptyState());
        }
        else
        {
            for (int i = 0; i < _config.Groups.Count; i++)
            {
                GroupPanel.Children.Add(BuildGroupCard(_config.Groups[i], i));
            }
        }

        UpdateTopBarCount();
    }

    private void UpdateTopBarCount()
    {
        if (_countLabel == null) return;
        int count = _config?.Groups.Count ?? 0;
        _countLabel.Text = (count == 0 ? "No groups" : $"{count} group{(count == 1 ? "" : "s")}")
            + " · Control lights and audio devices together from one knob or button";
    }

    // ── Empty State ──────────────────────────────────────────────────

    private Border MakeEmptyState()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel { Margin = new Thickness(4, 12, 4, 12), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
        card.Child = stack;

        var t1 = new TextBlock
        {
            Text = "No device groups yet",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        t1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(t1);

        var t2 = new TextBlock
        {
            Text = "Create a group to organize your Govee, Corsair, Home Assistant, and audio devices together. "
                 + "Point a knob at a group to dim everything at once, or bind a button to toggle the whole group on and off.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };
        t2.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stack.Children.Add(t2);

        var btn = BuildNewGroupButton(primary: true);
        btn.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(btn);

        return card;
    }

    // ── Group Card ───────────────────────────────────────────────────

    private Border BuildGroupCard(DeviceGroup group, int groupIndex)
    {
        var groupColor = ParseColor(group.Color);

        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(3, 1, 1, 1),
            BorderBrush = new SolidColorBrush(groupColor),
        };

        var outerStack = new StackPanel();
        card.Child = outerStack;

        // ── Header row: swatch | name + subtitle | delete ──
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Color swatch (click to cycle)
        var colorSwatch = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(groupColor),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Click to change group color",
        };
        colorSwatch.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        int colorIdx = groupIndex;
        colorSwatch.MouseLeftButtonUp += (_, _) =>
        {
            if (_config == null) return;
            var g = _config.Groups[colorIdx];
            g.Color = NextGroupColor(g.Color);
            Save();
            RebuildGroupPanel();
        };
        Grid.SetColumn(colorSwatch, 0);
        headerRow.Children.Add(colorSwatch);

        // Editable name + subtitle
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var nameBox = new TextBox
        {
            Text = group.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 400,
            ToolTip = "Group name (click to rename)",
        };
        nameBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        nameBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorderBrush");
        nameBox.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        nameBox.GotFocus += (_, _) => nameBox.SetResourceReference(TextBox.BorderBrushProperty, "AccentBrush");
        int nameIdx = groupIndex;
        nameBox.LostFocus += (_, _) =>
        {
            nameBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorderBrush");
            if (_config == null) return;
            var text = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) text = "Untitled Group";
            _config.Groups[nameIdx].Name = text;
            QueueSave();
        };
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; }
        };
        nameStack.Children.Add(nameBox);

        var subtitle = new TextBlock
        {
            Text = BuildGroupSubtitle(group),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        nameStack.Children.Add(subtitle);
        Grid.SetColumn(nameStack, 1);
        headerRow.Children.Add(nameStack);

        // Delete button (right side)
        var deleteBtn = MakePillButton("Delete", danger: true, "Delete this group");
        deleteBtn.VerticalAlignment = VerticalAlignment.Center;
        int deleteIdx = groupIndex;
        deleteBtn.Click += (_, _) =>
        {
            if (_config == null) return;
            _config.Groups.RemoveAt(deleteIdx);
            Save();
            RebuildGroupPanel();
        };
        Grid.SetColumn(deleteBtn, 2);
        headerRow.Children.Add(deleteBtn);

        outerStack.Children.Add(headerRow);

        // ── Members (inner item card with chip list) ──
        var (devBar, devLabel) = MakeSectionHeader("MEMBERS");
        outerStack.Children.Add(WrapHeader(devBar, devLabel));

        var chips = new WrapPanel { Orientation = Orientation.Horizontal };

        if (group.Devices.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "No devices in this group",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 6),
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            chips.Children.Add(none);
        }
        else
        {
            for (int d = 0; d < group.Devices.Count; d++)
            {
                chips.Children.Add(BuildDeviceChip(group.Devices[d], groupIndex, d));
            }
        }

        // ── Add Device chip ──
        var addBtn = MakePillButton("+  Add device", danger: false, "Add a device to this group");
        addBtn.Margin = new Thickness(0, 0, 6, 6);
        int addGroupIdx = groupIndex;
        addBtn.Click += (_, _) => ShowAddDeviceMenu(addBtn, addGroupIdx);
        chips.Children.Add(addBtn);

        var itemCard = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 10, 10, 4),
            Child = chips,
        };
        itemCard.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        itemCard.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        outerStack.Children.Add(itemCard);

        return card;
    }

    private static string BuildGroupSubtitle(DeviceGroup group)
    {
        int n = group.Devices.Count;
        if (n == 0) return "Empty · add devices below";
        var types = group.Devices.Select(d => d.Type).Distinct().Select(GetTypeDisplayName);
        return $"{n} device{(n == 1 ? "" : "s")} · {string.Join(", ", types)}";
    }

    private static string GetTypeDisplayName(string type) => type switch
    {
        "govee" => "Govee",
        "corsair" => "Corsair iCUE",
        "ha" => "Home Assistant",
        "audio_output" => "Audio",
        _ => type,
    };

    // ── Device Chip ──────────────────────────────────────────────────

    private Border BuildDeviceChip(GroupDevice device, int groupIndex, int deviceIndex)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = string.IsNullOrEmpty(device.DeviceId) ? device.Name : $"{device.Name}\n{device.DeviceId}",
        };
        chip.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Child = row;

        // Type badge
        var typeBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = GetTypeBadgeBackground(device.Type),
        };
        typeBadge.Child = new TextBlock
        {
            Text = GetTypeLabel(device.Type),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#101010"),
        };
        row.Children.Add(typeBadge);

        // Device name
        var nameText = new TextBlock
        {
            Text = device.Name,
            FontSize = 11,
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        row.Children.Add(nameText);

        // Action tag for HA devices
        if (device.Type == "ha")
        {
            var actionLabel = device.Action switch
            {
                "on"  => "TURN ON",
                "off" => "TURN OFF",
                _     => "TOGGLE",
            };
            var tag = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = actionLabel,
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#9A9A9A"),
                },
            };
            tag.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
            tag.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            row.Children.Add(tag);
        }

        // Remove button
        var removeBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 11,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Remove from group",
        };
        removeBtn.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        int rGroup = groupIndex, rDevice = deviceIndex;
        removeBtn.MouseLeftButtonUp += (_, _) =>
        {
            if (_config == null) return;
            _config.Groups[rGroup].Devices.RemoveAt(rDevice);
            Save();
            RebuildGroupPanel();
        };
        removeBtn.MouseEnter += (_, _) => removeBtn.SetResourceReference(TextBlock.ForegroundProperty, "DangerRedBrush");
        removeBtn.MouseLeave += (_, _) => removeBtn.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        row.Children.Add(removeBtn);

        return chip;
    }

    /// <summary>Rounded pill button. Danger = red outline; primary = filled accent; else accent outline.</summary>
    private Button MakePillButton(string text, bool danger, string? tooltip, bool primary = false)
    {
        var btn = new Button { Cursor = Cursors.Hand, ToolTip = tooltip, Focusable = false };

        var tmpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        bd.Name = "Bd";
        bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(14));
        bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
        bd.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(14, 5, 14, 5));
        bd.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tmpl.VisualTree = bd;
        btn.Template = tmpl;

        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        btn.Content = label;

        void Paint(bool hover)
        {
            var c = danger ? ((SolidColorBrush)FindBrush("DangerRedBrush")).Color : ThemeManager.Accent;
            if (primary)
            {
                btn.Background = new SolidColorBrush(hover ? ThemeManager.WithAlpha(c, 0xDD) : c);
                btn.BorderBrush = new SolidColorBrush(c);
                label.SetResourceReference(TextBlock.ForegroundProperty, "BgBaseBrush");
            }
            else
            {
                btn.Background = new SolidColorBrush(ThemeManager.WithAlpha(c, (byte)(hover ? 0x33 : 0x14)));
                btn.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(c, (byte)(hover ? 0xCC : 0x77)));
                label.Foreground = new SolidColorBrush(c);
            }
        }
        Paint(false);
        btn.MouseEnter += (_, _) => Paint(true);
        btn.MouseLeave += (_, _) => Paint(false);
        // Only accent-colored pills need repainting; re-read on each render via Loaded too
        btn.Loaded += (_, _) => Paint(btn.IsMouseOver);
        return btn;
    }

    // ── Add Device Menu ──────────────────────────────────────────────

    private void ShowAddDeviceMenu(Button anchor, int groupIndex)
    {
        if (_config == null) return;

        var menu = new ContextMenu
        {
            Background = FindBrush("CardBgBrush"),
            BorderBrush = FindBrush("CardBorderBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
        };

        bool hasItems = false;

        // Govee LAN devices
        if (_config.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0)
        {
            var header = new MenuItem
            {
                Header = "GOVEE",
                IsEnabled = false,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#00E676"),
            };
            menu.Items.Add(header);

            foreach (var gd in _config.Ambience.GoveeDevices)
            {
                bool hasLan = !string.IsNullOrWhiteSpace(gd.Ip);
                bool hasCloud = !hasLan
                                && !string.IsNullOrWhiteSpace(gd.DeviceId)
                                && !string.IsNullOrWhiteSpace(gd.Sku);
                if (!hasLan && !hasCloud) continue;

                // Group stores the LAN IP (preferred) or the Cloud DeviceId as
                // the identifier. HandleGroupToggle matches on either field.
                string deviceKey = hasLan ? gd.Ip : gd.DeviceId;

                var group = _config.Groups[groupIndex];
                if (group.Devices.Any(d => d.Type == "govee" && d.DeviceId == deviceKey))
                    continue;

                string headerText = !string.IsNullOrWhiteSpace(gd.Name)
                    ? (hasLan ? $"{gd.Name} ({gd.Ip})" : $"{gd.Name} (API)")
                    : (hasLan ? gd.Ip : gd.DeviceId);

                var item = new MenuItem
                {
                    Header = headerText,
                    Foreground = FindBrush("TextPrimaryBrush"),
                };
                string name = !string.IsNullOrWhiteSpace(gd.Name) ? gd.Name : deviceKey;
                string capturedKey = deviceKey;
                item.Click += (_, _) =>
                {
                    _config.Groups[groupIndex].Devices.Add(new GroupDevice
                    {
                        Type = "govee",
                        DeviceId = capturedKey,
                        Name = name,
                    });
                    Save();
                    RebuildGroupPanel();
                };
                menu.Items.Add(item);
                hasItems = true;
            }

            menu.Items.Add(new Separator());
        }

        // Corsair devices
        if (_config.Corsair.Enabled && _corsairSync?.IsAvailable == true && _corsairSync.Devices.Count > 0)
        {
            var header = new MenuItem
            {
                Header = "CORSAIR iCUE",
                IsEnabled = false,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#FFB800"),
            };
            menu.Items.Add(header);

            foreach (var cd in _corsairSync.Devices)
            {
                var group = _config.Groups[groupIndex];
                if (group.Devices.Any(d => d.Type == "corsair" && d.DeviceId == cd.Id))
                    continue;

                var item = new MenuItem
                {
                    Header = $"{cd.Name} ({cd.Type})",
                    Foreground = FindBrush("TextPrimaryBrush"),
                };
                string id = cd.Id;
                string cname = cd.Name;
                item.Click += (_, _) =>
                {
                    _config.Groups[groupIndex].Devices.Add(new GroupDevice
                    {
                        Type = "corsair",
                        DeviceId = id,
                        Name = cname,
                    });
                    Save();
                    RebuildGroupPanel();
                };
                menu.Items.Add(item);
                hasItems = true;
            }

            menu.Items.Add(new Separator());
        }

        // Audio output devices
        {
            var audioHeader = new MenuItem
            {
                Header = "AUDIO DEVICES",
                IsEnabled = false,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#26C6DA"),
            };
            menu.Items.Add(audioHeader);

            try
            {
                using var audioEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var audioDevices = audioEnum.EnumerateAudioEndPoints(
                    NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                for (int i = 0; i < audioDevices.Count; i++)
                {
                    using var audioDev = audioDevices[i];
                    var group = _config.Groups[groupIndex];
                    if (group.Devices.Any(d => d.Type == "audio_output" && d.DeviceId == audioDev.ID))
                        continue;

                    var item = new MenuItem
                    {
                        Header = audioDev.FriendlyName,
                        Foreground = FindBrush("TextPrimaryBrush"),
                    };
                    string audioId = audioDev.ID;
                    string audioName = audioDev.FriendlyName;
                    item.Click += (_, _) =>
                    {
                        _config.Groups[groupIndex].Devices.Add(new GroupDevice
                        {
                            Type = "audio_output",
                            DeviceId = audioId,
                            Name = audioName,
                        });
                        Save();
                        RebuildGroupPanel();
                    };
                    menu.Items.Add(item);
                    hasItems = true;
                }
            }
            catch { }

            menu.Items.Add(new Separator());
        }

        // Home Assistant — manual entity_id entry
        if (_config.HomeAssistant.Enabled)
        {
            var header = new MenuItem
            {
                Header = "HOME ASSISTANT",
                IsEnabled = false,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#03A9F4"),
            };
            menu.Items.Add(header);

            var haItem = new MenuItem
            {
                Header = "Add Home Assistant entity...",
                Foreground = FindBrush("TextPrimaryBrush"),
            };
            haItem.Click += (_, _) => ShowHaEntityPicker(groupIndex);
            menu.Items.Add(haItem);
            hasItems = true;
        }

        if (!hasItems)
        {
            menu.Items.Clear();
            var noDevices = new MenuItem
            {
                Header = "No devices available — enable integrations in Settings",
                IsEnabled = false,
                Foreground = FindBrush("TextSecBrush"),
            };
            menu.Items.Add(noDevices);
        }

        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ── HA Entity Picker ─────────────────────────────────────────────

    private void ShowHaEntityPicker(int groupIndex)
    {
        if (_config == null) return;

        var accent = ThemeManager.Accent;
        var accentHex = ThemeManager.AccentHex;

        // ── Glass-style window (matches GlassDialog) ──────────────────
        var dialog = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Width = 440,
            SizeToContent = SizeToContent.Height,
        };
        dialog.MouseLeftButtonDown += (_, _) => { try { dialog.DragMove(); } catch { } };

        // Outer glass border
        var accentBorderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(accent, 0x55),
            ThemeManager.WithAlpha(accent, 0x22),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(8),
            BorderThickness = new Thickness(1.2),
            BorderBrush = accentBorderBrush,
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#EE111111"),
                (Color)ColorConverter.ConvertFromString("#DD0A0A0A"),
                new System.Windows.Point(0, 0), new System.Windows.Point(1, 1)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent, BlurRadius = 24, Opacity = 0.15, ShadowDepth = 0
            },
            Opacity = 0,
        };
        outerBorder.RenderTransform = new ScaleTransform(0.95, 0.95);
        outerBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

        // Fade-in on load
        dialog.Loaded += (_, _) =>
        {
            var da = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(150)) { EasingFunction = new System.Windows.Media.Animation.CubicEase() };
            var sx = new System.Windows.Media.Animation.DoubleAnimation(0.95, 1,
                TimeSpan.FromMilliseconds(150)) { EasingFunction = new System.Windows.Media.Animation.CubicEase() };
            outerBorder.BeginAnimation(UIElement.OpacityProperty, da);
            ((ScaleTransform)outerBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            ((ScaleTransform)outerBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        };

        var inner = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        // Title row: label + X close button
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = "HOME ASSISTANT",
            FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x66)),
        };
        Grid.SetColumn(titleText, 0);
        var closeBtn = new TextBlock
        {
            Text = "✕", FontSize = 11, Cursor = Cursors.Hand,
            Foreground = Brush("#555555"), VerticalAlignment = VerticalAlignment.Center,
        };
        closeBtn.MouseLeftButtonDown += (_, _) => dialog.Close();
        closeBtn.MouseEnter += (_, _) => closeBtn.Foreground = Brush("#E8E8E8");
        closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = Brush("#555555");
        Grid.SetColumn(closeBtn, 1);
        titleRow.Children.Add(titleText);
        titleRow.Children.Add(closeBtn);
        inner.Children.Add(titleRow);

        // Search box
        var searchBorder = new Border
        {
            Background = Brush("#1A242424"),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var searchBox = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = Brush("#E8E8E8"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = new SolidColorBrush(accent),
            Padding = new Thickness(10, 7, 10, 7),
        };
        searchBorder.Child = searchBox;
        inner.Children.Add(searchBorder);

        // Entity list
        var listBox = new ListBox
        {
            Background = (SolidColorBrush)FindResource("BgBaseBrush"),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MaxHeight = 320,
            FontFamily = new FontFamily("Segoe UI"),
        };
        inner.Children.Add(listBox);

        // Button row
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        Button MakeBtn(string label, bool primary)
        {
            var b = new Button
            {
                Content = label,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Regular,
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = primary ? new SolidColorBrush(accent) : (SolidColorBrush)FindResource("CardBgBrush"),
                Foreground = primary ? (SolidColorBrush)FindResource("BgBaseBrush") : Brush("#E8E8E8"),
                BorderBrush = primary ? new SolidColorBrush(accent) : (SolidColorBrush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
            };
            // Rounded template
            var tmpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bd.SetValue(Border.BackgroundProperty, b.Background);
            bd.SetValue(Border.BorderBrushProperty, b.BorderBrush);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tmpl.VisualTree = bd;
            b.Template = tmpl;
            return b;
        }

        // Action pills: Toggle / Turn On / Turn Off
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        actionRow.Children.Add(new TextBlock
        {
            Text = "ACTION",
            FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#8A8A8A"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });

        string selectedAction = "toggle";
        var actionPills = new List<Border>();

        Border MakePill(string label, string value)
        {
            var isSelected = value == selectedAction;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 5, 14, 5),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Background = isSelected ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)) : (SolidColorBrush)FindResource("BgDarkBrush"),
                BorderBrush = isSelected ? new SolidColorBrush(accent) : (SolidColorBrush)FindResource("InputBorderBrush"),
                Tag = value,
            };
            pill.Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = isSelected ? new SolidColorBrush(accent) : Brush("#9A9A9A"),
            };
            actionPills.Add(pill);
            return pill;
        }

        void SelectAction(string value)
        {
            selectedAction = value;
            foreach (var p in actionPills)
            {
                bool sel = p.Tag as string == value;
                p.Background = sel ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)) : (SolidColorBrush)FindResource("BgDarkBrush");
                p.BorderBrush = sel ? new SolidColorBrush(accent) : (SolidColorBrush)FindResource("InputBorderBrush");
                ((TextBlock)p.Child).Foreground = sel ? new SolidColorBrush(accent) : Brush("#9A9A9A");
            }
        }

        foreach (var (label, val) in new[] { ("Toggle", "toggle"), ("Turn On", "on"), ("Turn Off", "off") })
        {
            var pill = MakePill(label, val);
            pill.MouseLeftButtonUp += (_, _) => SelectAction(pill.Tag as string ?? "toggle");
            actionRow.Children.Add(pill);
        }
        inner.Children.Add(actionRow);

        var cancelBtn = MakeBtn("Cancel", false);
        cancelBtn.Click += (_, _) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        var addBtn = MakeBtn("Add", true);
        btnRow.Children.Add(addBtn);
        inner.Children.Add(btnRow);

        outerBorder.Child = inner;
        dialog.Content = outerBorder;

        // ── Entity population (async fetch if cache empty) ────────────
        var lightDomains = new[] { "light", "group", "switch", "scene", "script", "input_boolean", "media_player" };
        List<HAEntity> entities = new();

        void PopulateList(string filter)
        {
            listBox.Items.Clear();
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? entities
                : entities.Where(e =>
                    e.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    e.EntityId.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (filtered.Count == 0)
            {
                listBox.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = entities.Count == 0
                            ? "No entities found — check Home Assistant connection in Settings"
                            : "No matches",
                        FontSize = 12, Foreground = Brush("#8A8A8A"),
                        Margin = new Thickness(4, 4, 4, 4),
                    },
                    IsEnabled = false,
                });
                return;
            }

            string? lastDomain = null;
            foreach (var entity in filtered)
            {
                if (entity.Domain != lastDomain)
                {
                    lastDomain = entity.Domain;
                    listBox.Items.Add(new TextBlock
                    {
                        Text = entity.Domain.ToUpperInvariant(),
                        FontSize = 9, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x03, 0xA9, 0xF4)),
                        Margin = new Thickness(10, 8, 0, 2),
                        IsHitTestVisible = false,
                    });
                }
                var row = new ListBoxItem { Tag = entity, Padding = new Thickness(10, 6, 10, 6) };
                var rowStack = new StackPanel();
                rowStack.Children.Add(new TextBlock { Text = entity.FriendlyName, FontSize = 13, Foreground = Brush("#E8E8E8") });
                rowStack.Children.Add(new TextBlock { Text = entity.EntityId, FontSize = 10, Foreground = Brush("#8A8A8A"), Margin = new Thickness(0, 1, 0, 0) });
                row.Content = rowStack;
                listBox.Items.Add(row);
            }
        }

        void LoadEntities(List<HAEntity> all)
        {
            entities = all
                .OrderBy(e => Array.IndexOf(lightDomains, e.Domain) is int i && i >= 0 ? i : 99)
                .ThenBy(e => e.FriendlyName)
                .ToList();
            PopulateList(searchBox.Text);
        }

        searchBox.TextChanged += (_, _) => PopulateList(searchBox.Text);

        // Show loading state then fetch
        listBox.Items.Add(new ListBoxItem
        {
            Content = new TextBlock { Text = "Loading entities...", FontSize = 12, Foreground = Brush("#8A8A8A"), Margin = new Thickness(4) },
            IsEnabled = false,
        });

        dialog.Loaded += async (_, _) =>
        {
            searchBox.Focus();
            if (_ha == null) { LoadEntities(new()); return; }

            // Use cache if populated, otherwise fetch now
            var cached = _ha.CachedEntities;
            if (cached.Count > 0) { LoadEntities(cached); return; }

            var fetched = await _ha.GetEntitiesAsync();
            Dispatcher.Invoke(() => LoadEntities(fetched));
        };

        // Add action
        addBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not ListBoxItem { Tag: HAEntity selected }) return;
            _config!.Groups[groupIndex].Devices.Add(new GroupDevice
            {
                Type = "ha",
                DeviceId = selected.EntityId,
                Name = selected.FriendlyName,
                Action = selectedAction,
            });
            Save();
            RebuildGroupPanel();
            dialog.Close();
        };
        listBox.MouseDoubleClick += (_, _) => addBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        dialog.ShowDialog();
    }

    // ── New Group Button ─────────────────────────────────────────────

    private Button BuildNewGroupButton(bool primary)
    {
        var btn = MakePillButton("+  New group", danger: false, "Create a new device group", primary);
        btn.Click += (_, _) =>
        {
            if (_config == null) return;
            _config.Groups.Add(new DeviceGroup
            {
                Name = $"Group {_config.Groups.Count + 1}",
                Color = _groupColors[_config.Groups.Count % _groupColors.Length],
            });
            Save();
            RebuildGroupPanel();
        };
        return btn;
    }

    // ── Save ─────────────────────────────────────────────────────────

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Save()
    {
        if (_config == null || _onSave == null || _loading) return;
        _onSave(_config);
    }

    // ── UI Helpers ───────────────────────────────────────────────────

    private (Border bar, TextBlock label) MakeSectionHeader(string text)
    {
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sectionHeaders.Add((bar, label));
        return (bar, label);
    }

    private StackPanel WrapHeader(Border bar, TextBlock label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        row.Children.Add(bar);
        row.Children.Add(label);
        return row;
    }

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = FindBrush("CardBorderBrush"),
        Margin = new Thickness(0, 4, 0, 12),
    };

    // ── Accent color refresh ─────────────────────────────────────────

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            if (bar != null) bar.Background = new SolidColorBrush(accent);
            if (label != null) label.Foreground = new SolidColorBrush(accent);
        }
    }

    // ── Color helpers ────────────────────────────────────────────────

    private static readonly string[] _groupColors =
    {
        "#69F0AE", "#42A5F5", "#FF7043", "#AB47BC",
        "#FFCA28", "#26C6DA", "#EF5350", "#66BB6A",
        "#FF8A65", "#7E57C2",
    };

    private static string NextGroupColor(string current)
    {
        for (int i = 0; i < _groupColors.Length; i++)
        {
            if (_groupColors[i].Equals(current, StringComparison.OrdinalIgnoreCase))
                return _groupColors[(i + 1) % _groupColors.Length];
        }
        return _groupColors[0];
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x69, 0xF0, 0xAE); }
    }

    private static SolidColorBrush GetTypeBadgeBackground(string type) => type switch
    {
        "govee" => Brush("#00E676"),
        "corsair" => Brush("#FFB800"),
        "ha" => Brush("#03A9F4"),
        "audio_output" => Brush("#26C6DA"),
        _ => Brush("#555555"),
    };

    private static string GetTypeLabel(string type) => type switch
    {
        "govee" => "GOVEE",
        "corsair" => "iCUE",
        "ha" => "HA",
        "audio_output" => "AUDIO",
        _ => type.ToUpperInvariant(),
    };

    // ── Resource helpers ─────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
