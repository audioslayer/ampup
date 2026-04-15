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

    private void BuildTopBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var titleLabel = new TextBlock
        {
            Text = "Device Groups",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        row.Children.Add(titleLabel);

        var countLabel = new TextBlock
        {
            FontSize = 11,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(countLabel);

        Loaded += (_, _) =>
        {
            int count = _config?.Groups.Count ?? 0;
            countLabel.Text = count == 0 ? "No groups" : $"{count} group{(count == 1 ? "" : "s")}";
        };

        TopBar.Children.Add(row);
    }

    // ── Main Panel ───────────────────────────────────────────────────

    private void RebuildGroupPanel()
    {
        GroupPanel.Children.Clear();
        _sectionHeaders.Clear();

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

        // New Group button
        GroupPanel.Children.Add(BuildNewGroupButton());

        // Update top bar count
        UpdateTopBarCount();
    }

    private void UpdateTopBarCount()
    {
        if (TopBar.Children.Count > 0 && TopBar.Children[0] is StackPanel row)
        {
            foreach (var child in row.Children)
            {
                if (child is TextBlock tb && tb.Style == FindStyle("SecondaryText"))
                {
                    int count = _config?.Groups.Count ?? 0;
                    tb.Text = count == 0 ? "No groups" : $"{count} group{(count == 1 ? "" : "s")}";
                    break;
                }
            }
        }
    }

    // ── Empty State ──────────────────────────────────────────────────

    private Border MakeEmptyState()
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var stack = new StackPanel { Margin = new Thickness(4) };
        card.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "No device groups yet",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Create a group to organize your Govee, Corsair, Home Assistant, and audio devices together.",
            Style = FindStyle("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

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

        // ── Header row: name + color swatch + delete ──
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

        // Delete button (right side)
        var deleteBtn = new Button
        {
            Content = "Delete",
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Foreground = FindBrush("DangerRedBrush"),
            Background = Brushes.Transparent,
            BorderBrush = FindBrush("DangerRedBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "Delete this group",
        };
        int deleteIdx = groupIndex;
        deleteBtn.Click += (_, _) =>
        {
            if (_config == null) return;
            _config.Groups.RemoveAt(deleteIdx);
            Save();
            RebuildGroupPanel();
        };
        DockPanel.SetDock(deleteBtn, Dock.Right);
        headerRow.Children.Add(deleteBtn);

        // Color swatch (click to cycle)
        var colorSwatch = new Border
        {
            Width = 22, Height = 22,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(groupColor),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Click to change group color",
        };
        int colorIdx = groupIndex;
        colorSwatch.MouseLeftButtonUp += (_, _) =>
        {
            if (_config == null) return;
            var g = _config.Groups[colorIdx];
            g.Color = NextGroupColor(g.Color);
            Save();
            RebuildGroupPanel();
        };
        DockPanel.SetDock(colorSwatch, Dock.Left);
        headerRow.Children.Add(colorSwatch);

        // Editable name
        var nameBox = new TextBox
        {
            Text = group.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            Background = FindBrush("InputBgBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 180,
            MaxWidth = 400,
            VerticalAlignment = VerticalAlignment.Center,
            CaretBrush = FindBrush("AccentBrush"),
            ToolTip = "Group name",
        };
        int nameIdx = groupIndex;
        nameBox.LostFocus += (_, _) =>
        {
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
        headerRow.Children.Add(nameBox);

        outerStack.Children.Add(headerRow);
        outerStack.Children.Add(MakeSeparator());

        // ── Device list ──
        var (devBar, devLabel) = MakeSectionHeader("DEVICES");
        outerStack.Children.Add(WrapHeader(devBar, devLabel));

        if (group.Devices.Count == 0)
        {
            outerStack.Children.Add(new TextBlock
            {
                Text = "No devices in this group",
                Style = FindStyle("SecondaryText"),
                Margin = new Thickness(0, 0, 0, 10),
            });
        }
        else
        {
            for (int d = 0; d < group.Devices.Count; d++)
            {
                outerStack.Children.Add(BuildDeviceRow(group.Devices[d], groupIndex, d));
            }
        }

        // ── Add Device button ──
        var addBtn = new Button
        {
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Add a device to this group",
        };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal };
        addContent.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        addContent.Children.Add(new TextBlock
        {
            Text = "Add Device",
            FontSize = 11,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        addBtn.Content = addContent;
        int addGroupIdx = groupIndex;
        addBtn.Click += (_, _) => ShowAddDeviceMenu(addBtn, addGroupIdx);
        outerStack.Children.Add(addBtn);

        return card;
    }

    // ── Device Row ───────────────────────────────────────────────────

    private Border BuildDeviceRow(GroupDevice device, int groupIndex, int deviceIndex)
    {
        var row = new Border
        {
            Background = FindBrush("InputBgBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var dock = new DockPanel();
        row.Child = dock;

        // Remove button
        var removeBtn = new TextBlock
        {
            Text = "\u2715",
            FontSize = 13,
            Foreground = FindBrush("TextDimBrush"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Remove from group",
        };
        int rGroup = groupIndex, rDevice = deviceIndex;
        removeBtn.MouseLeftButtonUp += (_, _) =>
        {
            if (_config == null) return;
            _config.Groups[rGroup].Devices.RemoveAt(rDevice);
            Save();
            RebuildGroupPanel();
        };
        removeBtn.MouseEnter += (_, _) => removeBtn.Foreground = FindBrush("DangerRedBrush");
        removeBtn.MouseLeave += (_, _) => removeBtn.Foreground = FindBrush("TextDimBrush");
        DockPanel.SetDock(removeBtn, Dock.Right);
        dock.Children.Add(removeBtn);

        // Type badge
        var typeBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = GetTypeBadgeBackground(device.Type),
        };
        typeBadge.Child = new TextBlock
        {
            Text = GetTypeLabel(device.Type),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        DockPanel.SetDock(typeBadge, Dock.Left);
        dock.Children.Add(typeBadge);

        // Device name
        var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock
        {
            Text = device.Name,
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
        });
        var idLine = new StackPanel { Orientation = Orientation.Horizontal };
        idLine.Children.Add(new TextBlock
        {
            Text = device.DeviceId,
            FontSize = 9,
            Foreground = FindBrush("TextDimBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        // Show action badge for HA devices
        if (device.Type == "ha")
        {
            var actionLabel = device.Action switch
            {
                "on"  => "TURN ON",
                "off" => "TURN OFF",
                _     => "TOGGLE",
            };
            idLine.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#1A1A1A"),
                BorderBrush = Brush("#333333"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = actionLabel,
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#9A9A9A"),
                },
            });
        }
        namePanel.Children.Add(idLine);
        dock.Children.Add(namePanel);

        return row;
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
                // Skip if already in this group
                var group = _config.Groups[groupIndex];
                if (group.Devices.Any(d => d.Type == "govee" && d.DeviceId == gd.Ip))
                    continue;

                var item = new MenuItem
                {
                    Header = string.IsNullOrEmpty(gd.Name) ? gd.Ip : $"{gd.Name} ({gd.Ip})",
                    Foreground = FindBrush("TextPrimaryBrush"),
                };
                string ip = gd.Ip;
                string name = string.IsNullOrEmpty(gd.Name) ? gd.Ip : gd.Name;
                item.Click += (_, _) =>
                {
                    _config.Groups[groupIndex].Devices.Add(new GroupDevice
                    {
                        Type = "govee",
                        DeviceId = ip,
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
                    var audioDev = audioDevices[i];
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
            Background = Brush("#0D0D0D"),
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
                Background = primary ? new SolidColorBrush(accent) : Brush("#1C1C1C"),
                Foreground = primary ? Brush("#0F0F0F") : Brush("#E8E8E8"),
                BorderBrush = primary ? new SolidColorBrush(accent) : Brush("#2A2A2A"),
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
                Background = isSelected ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)) : Brush("#1A1A1A"),
                BorderBrush = isSelected ? new SolidColorBrush(accent) : Brush("#333333"),
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
                p.Background = sel ? new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x22)) : Brush("#1A1A1A");
                p.BorderBrush = sel ? new SolidColorBrush(accent) : Brush("#333333");
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

    private Button BuildNewGroupButton()
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "Create a new device group",
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = "New Group",
            FontSize = 12,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        btn.Content = content;
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
