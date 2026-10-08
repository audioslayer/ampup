using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Controls;
using AmpUp.Core.Services;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// Groups page. Shows two kinds of groups:
///  - DEVICE GROUPS: <see cref="DeviceGroup"/> entries in config.Groups (Govee / Corsair / HA / audio outputs),
///    referenced by knob targets "group:&lt;name&gt;" and the "group_toggle" button action.
///  - APP GROUPS: knobs (Turn Up + N3 encoders) whose Target is "apps". These are created in the Mixer;
///    their <see cref="KnobConfig.Apps"/> list is edited here in place and saved via the same save path.
/// </summary>
public partial class GroupsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private AudioMixer? _mixer;

    private CorsairSync? _corsairSync;
    private HAIntegration? _ha;

    /// <summary>Raised after an app group's app list changes so the Mixer can redraw its chips.</summary>
    public Action? OnAppGroupsChanged { get; set; }

    /// <summary>Raised by an app group card's "Open in Mixer" button: (isStreamController, knob idx).</summary>
    public Action<bool, int>? OnOpenInMixer { get; set; }

    // Card to scroll to + highlight after the next rebuild (set by FocusAppGroup).
    private string? _focusKey;

    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();
    private readonly HashSet<string> _expanded = new();
    private const int CollapsedRowCount = 5;

    public GroupsView()
    {
        InitializeComponent();
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) RebuildGroupPanel(); };
    }

    public void SetCorsairSync(CorsairSync corsairSync) => _corsairSync = corsairSync;
    public void SetHAIntegration(HAIntegration? ha) => _ha = ha;
    public void SetMixer(AudioMixer? mixer) => _mixer = mixer;

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _config = config;
        _onSave = onSave;
        RebuildGroupPanel();
    }

    // ── Build ────────────────────────────────────────────────────────

    private void RebuildGroupPanel()
    {
        TopBar.Children.Clear();
        GroupPanel.Children.Clear();
        _sectionHeaders.Clear();
        if (_config == null) return;

        TopBar.Children.Add(BuildPageHeader());

        // DEVICE GROUPS
        GroupPanel.Children.Add(BuildSectionHeader("DEVICE GROUPS",
            _config.Groups.Count == 0 ? "" : $"{_config.Groups.Count}"));
        if (_config.Groups.Count == 0)
        {
            GroupPanel.Children.Add(BuildEmptyState(
                "No device groups yet",
                "Combine Govee lights, Corsair devices, Home Assistant entities and audio outputs. "
                + "Point a knob at a group to dim everything at once, or bind a button to toggle it.",
                withNewButton: true));
        }
        else
        {
            for (int i = 0; i < _config.Groups.Count; i++)
                GroupPanel.Children.Add(BuildDeviceGroupCard(_config.Groups[i], i));
        }

        // APP GROUPS (from Mixer)
        var appGroups = GetAppGroups();
        var appHeader = BuildSectionHeader("APP GROUPS", appGroups.Count == 0 ? "" : $"{appGroups.Count}");
        appHeader.Margin = new Thickness(0, 14, 0, 10);
        GroupPanel.Children.Add(appHeader);
        if (appGroups.Count == 0)
        {
            GroupPanel.Children.Add(BuildEmptyState(
                "No app groups yet",
                "App groups are created in the Mixer: set a knob's target to App Group and pick its apps. "
                + "They show up here so you can manage them alongside your device groups.",
                withNewButton: false));
        }
        else
        {
            foreach (var ag in appGroups)
                GroupPanel.Children.Add(BuildAppGroupCard(ag));
        }

        if (_focusKey != null)
        {
            var key = _focusKey;
            _focusKey = null;
            var target = GroupPanel.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == key);
            if (target != null)
                Dispatcher.BeginInvoke(() => HighlightCard(target), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Scroll an app group's card into view and pulse its border (used by the Mixer's "Manage" link).</summary>
    public void FocusAppGroup(bool streamController, int knobIdx)
    {
        _focusKey = AppGroupKey(streamController, knobIdx);
        RebuildGroupPanel();
    }

    private static string AppGroupKey(bool streamController, int knobIdx) => $"{(streamController ? "sc" : "tu")}:{knobIdx}";

    private static void HighlightCard(Border card)
    {
        card.BringIntoView();
        var accent = ThemeManager.Accent;
        var brush = new SolidColorBrush(accent);
        var oldBrush = card.BorderBrush;
        var oldThickness = card.BorderThickness;
        card.BorderBrush = brush;
        card.BorderThickness = new Thickness(1.5);
        var fade = new System.Windows.Media.Animation.ColorAnimation(accent, ThemeManager.WithAlpha(accent, 0), TimeSpan.FromMilliseconds(1600))
        {
            BeginTime = TimeSpan.FromMilliseconds(600),
        };
        fade.Completed += (_, _) => { card.BorderBrush = oldBrush; card.BorderThickness = oldThickness; };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    private FrameworkElement BuildPageHeader() =>
        UiKit.PageHeader("Groups", "Several devices or apps on one knob or button",
            BuildNewGroupButton(), Material.Icons.MaterialIconKind.Group);

    private FrameworkElement BuildSectionHeader(string text, string count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var bar = new Border
        {
            Width = 3, Height = 14,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sectionHeaders.Add((bar, label));
        row.Children.Add(bar);
        row.Children.Add(label);
        if (!string.IsNullOrEmpty(count))
        {
            var c = new TextBlock { Text = count, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            c.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            row.Children.Add(c);
        }
        return row;
    }

    private Border BuildEmptyState(string title, string body, bool withNewButton)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(0, 0, 0, 12),
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var icon = MakeIcon(withNewButton ? MaterialIconKind.LightbulbGroupOutline : MaterialIconKind.Apps, 22, "TextDimBrush");
        icon.Margin = new Thickness(0, 0, 16, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t1 = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold };
        t1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var t2 = new TextBlock { Text = body, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        t2.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stack.Children.Add(t1);
        stack.Children.Add(t2);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        if (withNewButton)
        {
            var btn = BuildNewGroupButton(primary: false);
            btn.Margin = new Thickness(16, 0, 0, 0);
            btn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);
        }
        return card;
    }

    // ── Generic card shell ───────────────────────────────────────────

    /// <summary>
    /// Card: [color bar] [icon tile] [name (+ inline edit) / subtitle] [actions], then a member list.
    /// </summary>
    private Border BuildCardShell(Color color, MaterialIconKind tileIcon, string tileTooltip, Action? onTileClick,
        string name, Action<string>? onRename, string subtitle, string? nameHint,
        FrameworkElement? actions, FrameworkElement memberList)
    {
        var card = new Border
        {
            Style = FindResource("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(0),
        };

        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = outer;

        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 14, 0, 14),
            Background = new SolidColorBrush(color),
        };
        outer.Children.Add(bar);

        var body = new StackPanel { Margin = new Thickness(14, 14, 16, 14) };
        Grid.SetColumn(body, 1);
        outer.Children.Add(body);

        // Header row
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(header);

        var tile = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x55)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tileTooltip,
            Child = new MaterialIcon { Kind = tileIcon, Width = 18, Height = 18, Foreground = new SolidColorBrush(color) },
        };
        if (onTileClick != null)
        {
            tile.Cursor = Cursors.Hand;
            tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x3A));
            tile.MouseLeave += (_, _) => tile.Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x22));
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; onTileClick(); };
        }
        header.Children.Add(tile);

        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        nameStack.Children.Add(BuildInlineName(name, onRename, nameHint));
        var subText = new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        subText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        nameStack.Children.Add(subText);
        Grid.SetColumn(nameStack, 1);
        header.Children.Add(nameStack);

        if (actions != null)
        {
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);
        }

        body.Children.Add(memberList);
        return card;
    }

    /// <summary>Name text that swaps to a borderless text box on click (when renamable).</summary>
    private FrameworkElement BuildInlineName(string name, Action<string>? onRename, string? hint)
    {
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        var text = new TextBlock
        {
            Text = name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = onRename != null ? "Click to rename" : hint,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        host.Children.Add(text);
        if (onRename == null) return host;

        text.Cursor = Cursors.IBeam;
        var box = new TextBox
        {
            Text = name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(-5, -2, 0, -2),
            MinWidth = 180,
            Visibility = Visibility.Collapsed,
        };
        box.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        box.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        box.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x88));
        host.Children.Add(box);

        bool committing = false;
        void Commit(bool accept)
        {
            if (committing) return;
            committing = true;
            var newName = box.Text.Trim();
            box.Visibility = Visibility.Collapsed;
            text.Visibility = Visibility.Visible;
            if (accept && !string.IsNullOrEmpty(newName) && newName != name)
                Dispatcher.BeginInvoke(() => onRename(newName));
            committing = false;
        }

        text.MouseEnter += (_, _) => text.TextDecorations = TextDecorations.Underline;
        text.MouseLeave += (_, _) => text.TextDecorations = null;
        text.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            box.Text = name;
            text.Visibility = Visibility.Collapsed;
            box.Visibility = Visibility.Visible;
            box.Focus();
            box.SelectAll();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => { if (box.Visibility == Visibility.Visible) Commit(true); };
        return host;
    }

    // ── Member list ──────────────────────────────────────────────────

    private sealed record MemberRow(
        FrameworkElement Icon, string Name, string? Detail, string TypeText,
        Action? OnTypeClick, string? TypeTooltip, Action OnRemove, string RemoveTooltip, bool Dim = false);

    private FrameworkElement BuildMemberList(string key, IReadOnlyList<MemberRow> rows, string emptyText,
        string addLabel, Action<FrameworkElement> onAdd, Func<MemberRow, FrameworkElement>? rowFactory = null)
    {
        rowFactory ??= BuildMemberRowElement;
        var list = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };
        list.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        list.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var stack = new StackPanel();
        list.Child = stack;

        if (rows.Count == 0)
        {
            var none = new TextBlock { Text = emptyText, FontSize = 11, Margin = new Thickness(10, 8, 10, 8) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            stack.Children.Add(none);
        }

        bool expanded = _expanded.Contains(key);
        int shown = expanded ? rows.Count : Math.Min(rows.Count, CollapsedRowCount);
        for (int i = 0; i < shown; i++)
            stack.Children.Add(rowFactory(rows[i]));

        if (rows.Count > CollapsedRowCount)
        {
            var toggle = BuildLinkRow(
                expanded ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown,
                expanded ? "Show less" : $"Show all {rows.Count}",
                accent: false);
            toggle.MouseLeftButtonUp += (_, _) =>
            {
                if (!_expanded.Remove(key)) _expanded.Add(key);
                RebuildGroupPanel();
            };
            stack.Children.Add(toggle);
        }

        var add = BuildLinkRow(MaterialIconKind.Plus, addLabel, accent: true);
        add.MouseLeftButtonUp += (_, _) => onAdd(add);
        stack.Children.Add(add);
        return list;
    }

    private FrameworkElement BuildMemberRowElement(MemberRow r)
    {
        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 4, 6), Background = Brushes.Transparent };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        r.Icon.Margin = new Thickness(0, 0, 10, 0);
        r.Icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(r.Icon);

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = r.Name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontStyle = r.Dim ? FontStyles.Italic : FontStyles.Normal,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, r.Dim ? "TextSecBrush" : "TextPrimaryBrush");
        nameRow.Children.Add(name);
        if (!string.IsNullOrEmpty(r.Detail))
        {
            var detail = new TextBlock { Text = r.Detail, FontSize = 10.5, Margin = new Thickness(8, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            nameRow.Children.Add(detail);
        }
        Grid.SetColumn(nameRow, 1);
        grid.Children.Add(nameRow);

        var type = new TextBlock
        {
            Text = r.TypeText,
            FontSize = 11,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = r.TypeTooltip,
        };
        type.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        if (r.OnTypeClick != null)
        {
            type.Cursor = Cursors.Hand;
            type.MouseEnter += (_, _) => type.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            type.MouseLeave += (_, _) => type.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            type.MouseLeftButtonUp += (_, e) => { e.Handled = true; r.OnTypeClick(); };
        }
        Grid.SetColumn(type, 2);
        grid.Children.Add(type);

        var remove = MakeIconButton(MaterialIconKind.Close, r.RemoveTooltip, danger: true, size: 24);
        remove.Opacity = 0;
        remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; r.OnRemove(); };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        row.MouseEnter += (_, _) => { row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush"); remove.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; remove.Opacity = 0; };
        return row;
    }

    private Border BuildLinkRow(MaterialIconKind icon, string text, bool accent)
    {
        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = new MaterialIcon { Kind = icon, Width = 14, Height = 14, Margin = new Thickness(1, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBlock { Text = text, FontSize = 11.5, FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (accent)
        {
            ic.Foreground = new SolidColorBrush(ThemeManager.Accent);
            tb.Foreground = new SolidColorBrush(ThemeManager.Accent);
        }
        else
        {
            ic.SetResourceReference(MaterialIcon.ForegroundProperty, "TextSecBrush");
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        }
        sp.Children.Add(ic);
        sp.Children.Add(tb);
        row.Child = sp;
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    // ── Device groups ────────────────────────────────────────────────

    private Border BuildDeviceGroupCard(DeviceGroup group, int groupIndex)
    {
        var color = ParseColor(group.Color);

        var rows = new List<MemberRow>();
        for (int d = 0; d < group.Devices.Count; d++)
        {
            var dev = group.Devices[d];
            int devIdx = d;
            string typeText = GetTypeDisplayName(dev.Type);
            Action? typeClick = null;
            string? typeTip = null;
            if (dev.Type == "ha")
            {
                typeText = $"Home Assistant · {HaActionLabel(dev.Action)}";
                typeTip = "Click to change what the group toggle does to this entity";
                var anchorDev = dev;
                typeClick = () => ShowHaActionMenu(anchorDev);
            }
            rows.Add(new MemberRow(
                MakeIcon(GetTypeIcon(dev.Type), 16, "TextSecBrush"),
                string.IsNullOrWhiteSpace(dev.Name) ? dev.DeviceId : dev.Name,
                null, typeText, typeClick, typeTip ?? dev.DeviceId,
                () =>
                {
                    if (_config == null || groupIndex >= _config.Groups.Count) return;
                    var devs = _config.Groups[groupIndex].Devices;
                    if (devIdx < devs.Count) devs.RemoveAt(devIdx);
                    Save();
                    RebuildGroupPanel();
                },
                "Remove from group"));
        }

        var list = BuildMemberList($"dev:{groupIndex}", rows, "No devices yet — add lights, fans or speakers below.",
            "Add device", anchor => ShowAddDeviceMenu(anchor, groupIndex));

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var more = MakeIconButton(MaterialIconKind.DotsHorizontal, "More", danger: false, size: 30);
        more.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowDeviceGroupMenu(more, groupIndex); };
        actions.Children.Add(more);

        return BuildCardShell(color, MaterialIconKind.LightbulbGroup, "Click to change group color",
            () => CycleColor(groupIndex),
            group.Name, newName => RenameGroup(groupIndex, newName), BuildGroupSubtitle(group), null,
            actions, list);
    }

    private void ShowDeviceGroupMenu(FrameworkElement anchor, int groupIndex)
    {
        if (_config == null || groupIndex >= _config.Groups.Count) return;
        var group = _config.Groups[groupIndex];

        var colorItems = _groupColors.Select(hex => new GlassMenuItem(
            ColorName(hex), MaterialIconKind.Circle,
            () => { group.Color = hex; Save(); RebuildGroupPanel(); },
            IsChecked: hex.Equals(group.Color, StringComparison.OrdinalIgnoreCase))).ToList();

        GlassContextMenuHost.Show(anchor, new List<GlassMenuItem>
        {
            new("Rename", MaterialIconKind.PencilOutline, () =>
            {
                var name = GlassDialog.Prompt("New name for this group:", "Rename group", Window.GetWindow(this));
                if (!string.IsNullOrWhiteSpace(name)) RenameGroup(groupIndex, name.Trim());
            }),
            new("Color", MaterialIconKind.PaletteOutline, null, colorItems),
            new("Add device", MaterialIconKind.Plus, () => ShowAddDeviceMenu(anchor, groupIndex)),
            GlassMenuItem.Sep,
            new("Delete group", MaterialIconKind.DeleteOutline, () => DeleteGroup(groupIndex), IsDanger: true),
        });
    }

    private void CycleColor(int groupIndex)
    {
        if (_config == null || groupIndex >= _config.Groups.Count) return;
        var g = _config.Groups[groupIndex];
        g.Color = NextGroupColor(g.Color);
        Save();
        RebuildGroupPanel();
    }

    private void DeleteGroup(int groupIndex)
    {
        if (_config == null || groupIndex >= _config.Groups.Count) return;
        var g = _config.Groups[groupIndex];
        int refs = CountGroupReferences(g.Name);
        string msg = $"Delete \"{g.Name}\"? This can't be undone.";
        if (refs > 0)
            msg += $"\n\n{refs} knob/button binding{(refs == 1 ? "" : "s")} use this group and will stop working.";
        if (!GlassDialog.Confirm(msg, "Delete group", dangerYes: true, Window.GetWindow(this))) return;
        _config.Groups.RemoveAt(groupIndex);
        _expanded.Remove($"dev:{groupIndex}");
        Save();
        RebuildGroupPanel();
    }

    /// <summary>
    /// Renames a group and rewrites every reference to it (knob targets "group:&lt;name&gt;" and
    /// group_toggle button paths) so existing bindings keep working.
    /// </summary>
    private void RenameGroup(int groupIndex, string newName)
    {
        if (_config == null || groupIndex >= _config.Groups.Count) return;
        var group = _config.Groups[groupIndex];
        var oldName = group.Name;
        if (newName == oldName) return;
        if (_config.Groups.Any(g => g != group && g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            GlassDialog.ShowWarning($"A group named \"{newName}\" already exists.", "Rename group", Window.GetWindow(this));
            RebuildGroupPanel();
            return;
        }
        group.Name = newName;

        string oldTarget = $"group:{oldName}", newTarget = $"group:{newName}";
        foreach (var k in AllKnobs())
            if (k.Target == oldTarget) k.Target = newTarget;
        foreach (var b in AllButtons())
        {
            if (b.Action == "group_toggle" && b.Path == oldName) b.Path = newName;
            if (b.DoublePressAction == "group_toggle" && b.DoublePressPath == oldName) b.DoublePressPath = newName;
            if (b.HoldAction == "group_toggle" && b.HoldPath == oldName) b.HoldPath = newName;
        }
        Save();
        RebuildGroupPanel();
    }

    private int CountGroupReferences(string name)
    {
        string target = $"group:{name}";
        int n = AllKnobs().Count(k => k.Target == target);
        foreach (var b in AllButtons())
        {
            if (b.Action == "group_toggle" && b.Path == name) n++;
            if (b.DoublePressAction == "group_toggle" && b.DoublePressPath == name) n++;
            if (b.HoldAction == "group_toggle" && b.HoldPath == name) n++;
        }
        return n;
    }

    private IEnumerable<KnobConfig> AllKnobs()
    {
        if (_config == null) yield break;
        foreach (var k in _config.Knobs) yield return k;
        foreach (var k in _config.N3.Knobs) yield return k;
        foreach (var ctx in _config.N3.EncoderContexts) foreach (var k in ctx.Knobs) yield return k;
        foreach (var f in _config.N3.Folders) foreach (var ctx in f.EncoderContexts) foreach (var k in ctx.Knobs) yield return k;
    }

    private IEnumerable<ButtonConfig> AllButtons()
    {
        if (_config == null) yield break;
        foreach (var b in _config.Buttons) yield return b;
        foreach (var b in _config.N3.Buttons) yield return b;
        foreach (var f in _config.N3.Folders) foreach (var b in f.Buttons) yield return b;
    }

    private void ShowHaActionMenu(GroupDevice dev)
    {
        GlassContextMenuHost.Show(this, new[] { ("Toggle", "toggle"), ("Turn On", "on"), ("Turn Off", "off") }
            .Select(a => new GlassMenuItem(a.Item1, MaterialIconKind.HomeAssistant, () =>
            {
                dev.Action = a.Item2;
                Save();
                RebuildGroupPanel();
            }, IsChecked: (dev.Action ?? "toggle") == a.Item2)).ToList());
    }

    private static string HaActionLabel(string? action) => action switch
    {
        "on" => "Turn on",
        "off" => "Turn off",
        _ => "Toggle",
    };

    private static string BuildGroupSubtitle(DeviceGroup group)
    {
        int n = group.Devices.Count;
        if (n == 0) return "Empty";
        var types = group.Devices.Select(d => d.Type).Distinct().Select(GetTypeDisplayName);
        return $"{n} device{(n == 1 ? "" : "s")} · {string.Join(", ", types)}";
    }

    private static string GetTypeDisplayName(string type) => type switch
    {
        "govee" => "Govee",
        "corsair" => "Corsair iCUE",
        "ha" => "Home Assistant",
        "audio_output" => "Audio output",
        _ => type,
    };

    private static MaterialIconKind GetTypeIcon(string type) => type switch
    {
        "govee" => MaterialIconKind.LightbulbOutline,
        "corsair" => MaterialIconKind.Fan,
        "ha" => MaterialIconKind.HomeAssistant,
        "audio_output" => MaterialIconKind.Speaker,
        _ => MaterialIconKind.Devices,
    };

    // ── Add device menu ──────────────────────────────────────────────

    private void ShowAddDeviceMenu(FrameworkElement anchor, int groupIndex)
    {
        if (_config == null || groupIndex >= _config.Groups.Count) return;
        var group = _config.Groups[groupIndex];
        var items = new List<GlassMenuItem>();

        void AddDevice(string type, string id, string name)
        {
            if (_config == null || groupIndex >= _config.Groups.Count) return;
            _config.Groups[groupIndex].Devices.Add(new GroupDevice { Type = type, DeviceId = id, Name = name });
            Save();
            RebuildGroupPanel();
        }

        // Govee (LAN IP preferred, else Cloud DeviceId — HandleGroupToggle matches either)
        if (_config.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0)
        {
            var sub = new List<GlassMenuItem>();
            foreach (var gd in _config.Ambience.GoveeDevices)
            {
                bool hasLan = !string.IsNullOrWhiteSpace(gd.Ip);
                bool hasCloud = !hasLan && !string.IsNullOrWhiteSpace(gd.DeviceId) && !string.IsNullOrWhiteSpace(gd.Sku);
                if (!hasLan && !hasCloud) continue;
                string key = hasLan ? gd.Ip : gd.DeviceId;
                if (group.Devices.Any(d => d.Type == "govee" && d.DeviceId == key)) continue;
                string label = !string.IsNullOrWhiteSpace(gd.Name)
                    ? (hasLan ? $"{gd.Name} ({gd.Ip})" : $"{gd.Name} (API)")
                    : (hasLan ? gd.Ip : gd.DeviceId);
                string name = !string.IsNullOrWhiteSpace(gd.Name) ? gd.Name : key;
                sub.Add(new GlassMenuItem(label, MaterialIconKind.LightbulbOutline, () => AddDevice("govee", key, name)));
            }
            if (sub.Count > 0) items.Add(new GlassMenuItem("Govee", MaterialIconKind.LightbulbOutline, null, sub));
        }

        // Corsair
        if (_config.Corsair.Enabled && _corsairSync?.IsAvailable == true && _corsairSync.Devices.Count > 0)
        {
            var sub = new List<GlassMenuItem>();
            foreach (var cd in _corsairSync.Devices)
            {
                if (group.Devices.Any(d => d.Type == "corsair" && d.DeviceId == cd.Id)) continue;
                string id = cd.Id, cname = cd.Name;
                sub.Add(new GlassMenuItem($"{cd.Name} ({cd.Type})", MaterialIconKind.Fan, () => AddDevice("corsair", id, cname)));
            }
            if (sub.Count > 0) items.Add(new GlassMenuItem("Corsair iCUE", MaterialIconKind.Fan, null, sub));
        }

        // Audio outputs
        {
            var sub = new List<GlassMenuItem>();
            try
            {
                using var audioEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var audioDevices = audioEnum.EnumerateAudioEndPoints(
                    NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                for (int i = 0; i < audioDevices.Count; i++)
                {
                    using var audioDev = audioDevices[i];
                    if (group.Devices.Any(d => d.Type == "audio_output" && d.DeviceId == audioDev.ID)) continue;
                    string audioId = audioDev.ID, audioName = audioDev.FriendlyName;
                    sub.Add(new GlassMenuItem(audioName, MaterialIconKind.Speaker, () => AddDevice("audio_output", audioId, audioName)));
                }
            }
            catch { }
            if (sub.Count > 0) items.Add(new GlassMenuItem("Audio outputs", MaterialIconKind.Speaker, null, sub));
        }

        // Home Assistant
        if (_config.HomeAssistant.Enabled)
            items.Add(new GlassMenuItem("Home Assistant entity…", MaterialIconKind.HomeAssistant, () => ShowHaEntityPicker(groupIndex)));

        if (items.Count == 0)
            items.Add(new GlassMenuItem("No devices available — enable integrations in Settings", MaterialIconKind.InformationOutline, null, IsEnabled: false));

        GlassContextMenuHost.Show(anchor, items);
    }

    // ── App groups (Mixer) ───────────────────────────────────────────

    private sealed record AppGroupRef(KnobConfig Knob, bool IsStreamController);

    private List<AppGroupRef> GetAppGroups()
    {
        var list = new List<AppGroupRef>();
        if (_config == null) return list;
        foreach (var k in _config.Knobs.Where(k => k.Target == "apps").OrderBy(k => k.Idx))
            list.Add(new AppGroupRef(k, false));
        foreach (var k in _config.N3.Knobs.Where(k => k.Target == "apps").OrderBy(k => k.Idx))
            list.Add(new AppGroupRef(k, true));
        return list;
    }

    private Border BuildAppGroupCard(AppGroupRef ag)
    {
        var knob = ag.Knob;
        string prefix = ag.IsStreamController ? $"Encoder {knob.Idx + 1}" : $"Knob {knob.Idx + 1}";
        string title = string.IsNullOrWhiteSpace(knob.Label) ? prefix : $"{prefix} · {knob.Label}";
        string key = AppGroupKey(ag.IsStreamController, knob.Idx);

        List<string> running;
        try { running = _mixer?.GetRunningAudioApps() ?? new List<string>(); }
        catch { running = new List<string>(); }

        var rows = new List<MemberRow>();
        foreach (var app in knob.Apps.ToList())
        {
            bool isRunning = running.Contains(app, StringComparer.OrdinalIgnoreCase);
            var appCapture = app;
            rows.Add(new MemberRow(
                MakeAppIcon(app, isRunning),
                MixerView.FormatTargetName(app), app,
                isRunning ? "Playing audio now" : "Not running",
                null, null,
                () =>
                {
                    knob.Apps.RemoveAll(a => a.Equals(appCapture, StringComparison.OrdinalIgnoreCase));
                    SaveAppGroups();
                },
                "Remove from group",
                Dim: !isRunning));
        }

        int n = knob.Apps.Count;
        string device = ag.IsStreamController ? "Stream Controller" : "Turn Up";
        string subtitle = n == 0 ? $"Empty · {device} mixer" : $"{n} app{(n == 1 ? "" : "s")} · {device} mixer";

        var list = BuildMemberList(key, rows, "No apps yet. Add one below.", "Add app",
            anchor => ShowAddAppMenu(anchor, knob), BuildAppMemberRow);

        var openMixer = MakeIconButton(MaterialIconKind.TuneVertical, "Open in Mixer", danger: false, size: 30);
        openMixer.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnOpenInMixer?.Invoke(ag.IsStreamController, knob.Idx); };

        var more = MakeIconButton(MaterialIconKind.DotsHorizontal, "More", danger: false, size: 30);
        more.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            GlassContextMenuHost.Show(more, new List<GlassMenuItem>
            {
                new("Add app", MaterialIconKind.Plus, () => ShowAddAppMenu(more, knob)),
                new("Add by process name…", MaterialIconKind.FormTextbox, () => PromptAddApp(knob)),
                GlassMenuItem.Sep,
                new("Remove all apps", MaterialIconKind.DeleteSweepOutline, () =>
                {
                    if (knob.Apps.Count == 0) return;
                    if (!GlassDialog.Confirm($"Remove all {knob.Apps.Count} apps from {title}?", "Clear app group",
                        dangerYes: true, Window.GetWindow(this))) return;
                    knob.Apps.Clear();
                    SaveAppGroups();
                }, IsDanger: true, IsEnabled: knob.Apps.Count > 0),
            });
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(openMixer);
        actions.Children.Add(more);

        var card = BuildCardShell(ThemeManager.Accent, MaterialIconKind.Apps,
            "Created in the Mixer", null,
            title, null, subtitle, "Rename this knob in the Mixer",
            actions, list);
        card.Tag = key;
        return card;
    }

    /// <summary>Searchable app chooser (same flyout as the Mixer) listing apps playing audio right now.</summary>
    private void ShowAddAppMenu(FrameworkElement anchor, KnobConfig knob)
    {
        List<string> running;
        try { running = _mixer?.GetRunningAudioApps() ?? new List<string>(); }
        catch { running = new List<string>(); }

        var choices = running
            .Where(a => !knob.Apps.Contains(a, StringComparer.OrdinalIgnoreCase))
            .OrderBy(a => MixerView.FormatTargetName(a), StringComparer.OrdinalIgnoreCase)
            .Select(a => new AppChooser.Choice(a, MixerView.FormatTargetName(a), "Playing audio now", MixerView.GetAppIcon(a)))
            .ToList();

        AppChooser.Show(anchor, choices, picked =>
        {
            if (!knob.Apps.Contains(picked, StringComparer.OrdinalIgnoreCase)) knob.Apps.Add(picked);
            SaveAppGroups();
        }, emptyText: "No other apps are playing audio. Type a process name to add one anyway.");
    }

    /// <summary>
    /// App row in the Mixer dropdown style: tinted icon tile with the real app icon,
    /// friendly name + status line, remove button on hover.
    /// </summary>
    private FrameworkElement BuildAppMemberRow(MemberRow r)
    {
        var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 4, 5), Background = Brushes.Transparent };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        var tile = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = r.Dim ? 0.6 : 1,
            Child = r.Icon,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        r.Icon.HorizontalAlignment = HorizontalAlignment.Center;
        r.Icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = r.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.Detail };
        name.SetResourceReference(TextBlock.ForegroundProperty, r.Dim ? "TextSecBrush" : "TextPrimaryBrush");
        text.Children.Add(name);
        var sub = new TextBlock { Text = r.TypeText, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Live status: green when the app is playing audio right now
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = r.Dim ? FindBrush("TextDimBrush") : new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0x77)),
            Opacity = r.Dim ? 0.4 : 1,
            ToolTip = r.TypeText,
        };
        Grid.SetColumn(dot, 2);
        grid.Children.Add(dot);

        var remove = MakeIconButton(MaterialIconKind.Close, r.RemoveTooltip, danger: true, size: 26);
        remove.Opacity = 0;
        remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; r.OnRemove(); };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        row.MouseEnter += (_, _) => { row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush"); remove.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; remove.Opacity = 0; };
        return row;
    }

    private void PromptAddApp(KnobConfig knob)
    {
        var name = GlassDialog.Prompt("Process name (e.g. spotify, chrome, game.exe):", "Add app", Window.GetWindow(this));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.ToLowerInvariant();
        if (!knob.Apps.Contains(name, StringComparer.OrdinalIgnoreCase)) knob.Apps.Add(name);
        SaveAppGroups();
    }

    private void SaveAppGroups()
    {
        Save();
        OnAppGroupsChanged?.Invoke();
        RebuildGroupPanel();
    }

    private FrameworkElement MakeAppIcon(string app, bool running)
    {
        // GetAppIcon's disk cache also covers apps that aren't running right now.
        var bmp = MixerView.GetAppIcon(app);
        if (bmp != null)
        {
            var img = new Image { Source = bmp, Width = 20, Height = 20, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        return MakeIcon(MaterialIconKind.Application, 17, running ? "TextSecBrush" : "TextDimBrush");
    }

    // ── New group ────────────────────────────────────────────────────

    private Button BuildNewGroupButton(bool primary = true)
    {
        var btn = MakePillButton("New group", MaterialIconKind.Plus, "Create a new device group", primary);
        btn.Click += (_, _) =>
        {
            if (_config == null) return;
            int n = _config.Groups.Count + 1;
            string name = $"Group {n}";
            while (_config.Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"Group {++n}";
            _config.Groups.Add(new DeviceGroup
            {
                Name = name,
                Color = _groupColors[_config.Groups.Count % _groupColors.Length],
            });
            Save();
            RebuildGroupPanel();
        };
        return btn;
    }

    // ── Save ─────────────────────────────────────────────────────────

    private void Save()
    {
        if (_config == null || _onSave == null) return;
        _onSave(_config);
    }

    // ── UI helpers ───────────────────────────────────────────────────

    private static MaterialIcon MakeIcon(MaterialIconKind kind, double size, string brushKey)
    {
        var ic = new MaterialIcon { Kind = kind, Width = size, Height = size };
        ic.SetResourceReference(MaterialIcon.ForegroundProperty, brushKey);
        return ic;
    }

    private Border MakeIconButton(MaterialIconKind kind, string tooltip, bool danger, double size)
    {
        var icon = MakeIcon(kind, size * 0.55, "TextDimBrush");
        var b = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = icon,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) =>
        {
            b.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, danger ? "DangerRedBrush" : "TextPrimaryBrush");
        };
        b.MouseLeave += (_, _) =>
        {
            b.Background = Brushes.Transparent;
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        };
        return b;
    }

    /// <summary>Rounded pill button with leading icon. Primary = filled accent; else accent outline.</summary>
    private Button MakePillButton(string text, MaterialIconKind icon, string? tooltip, bool primary)
    {
        var btn = new Button { Cursor = Cursors.Hand, ToolTip = tooltip, Focusable = false };

        var tmpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
        bd.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(12, 6, 14, 6));
        bd.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tmpl.VisualTree = bd;
        btn.Template = tmpl;

        var ic = new MaterialIcon { Kind = icon, Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(ic);
        sp.Children.Add(label);
        btn.Content = sp;

        void Paint(bool hover)
        {
            var c = ThemeManager.Accent;
            if (primary)
            {
                btn.Background = new SolidColorBrush(hover ? ThemeManager.WithAlpha(c, 0xDD) : c);
                btn.BorderBrush = new SolidColorBrush(c);
                label.SetResourceReference(TextBlock.ForegroundProperty, "BgBaseBrush");
                ic.SetResourceReference(MaterialIcon.ForegroundProperty, "BgBaseBrush");
            }
            else
            {
                btn.Background = new SolidColorBrush(ThemeManager.WithAlpha(c, (byte)(hover ? 0x2A : 0x10)));
                btn.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(c, (byte)(hover ? 0xCC : 0x66)));
                label.Foreground = new SolidColorBrush(c);
                ic.Foreground = new SolidColorBrush(c);
            }
        }
        Paint(false);
        btn.MouseEnter += (_, _) => Paint(true);
        btn.MouseLeave += (_, _) => Paint(false);
        btn.Loaded += (_, _) => Paint(btn.IsMouseOver);
        return btn;
    }

    private void RefreshAccentColors()
    {
        // Cards / buttons read ThemeManager.Accent at build time — rebuild for a full refresh.
        if (IsVisible) RebuildGroupPanel();
        else
        {
            var accent = ThemeManager.Accent;
            foreach (var (bar, label) in _sectionHeaders)
            {
                bar.Background = new SolidColorBrush(accent);
                label.Foreground = new SolidColorBrush(accent);
            }
        }
    }

    // ── Color helpers ────────────────────────────────────────────────

    private static readonly string[] _groupColors =
    {
        "#69F0AE", "#42A5F5", "#FF7043", "#AB47BC",
        "#FFCA28", "#26C6DA", "#EF5350", "#66BB6A",
        "#FF8A65", "#7E57C2",
    };

    private static readonly string[] _groupColorNames =
    {
        "Mint", "Blue", "Orange", "Purple", "Amber", "Cyan", "Red", "Green", "Coral", "Violet",
    };

    private static string ColorName(string hex)
    {
        int i = Array.FindIndex(_groupColors, c => c.Equals(hex, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? _groupColorNames[i] : hex;
    }

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

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    // ── HA Entity Picker (unchanged behaviour) ───────────────────────

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
}
