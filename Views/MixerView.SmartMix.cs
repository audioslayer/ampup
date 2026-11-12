using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

// Smart Mix — Voice ducking + automatic profile switching, in the Groups-page design language.
public partial class MixerView
{
    private static readonly Color SmartDuckColor = Color.FromRgb(0x26, 0xC6, 0xDA);
    private static readonly Color SmartProfileColor = Color.FromRgb(0xAB, 0x47, 0xBC);
    private static readonly string[] CommonVoiceApps = { "discord", "teams", "zoom", "slack" };

    // Working state (copied into config by CollectSmartMixConfig on the debounced save)
    private bool _duckEnabled;
    private string _duckTrigger = "";
    private readonly List<string> _duckTargets = new();
    private bool _autoSwitchEnabled;
    private bool _autoSwitchRevert;
    private readonly List<AutoSwitchRule> _autoSwitchRules = new();

    private Action<bool>? _setDuckSwitch, _setAutoSwitch, _setRevertSwitch;
    private StackPanel? _duckBody, _autoBody;
    private StackPanel? _duckTriggerRows, _duckTargetRows, _autoRuleRows;
    private TextBlock? _duckTargetCount, _autoRuleCount;
    private StyledSlider? _duckAmountSlider, _duckFadeOutSlider, _duckFadeInSlider;
    private TextBlock? _duckAmountLabel, _duckFadeOutLabel, _duckFadeInLabel;
    private readonly List<Action> _smartAccentRepaints = new();

    // ── Build ──────────────────────────────────────────────────────────

    private void BuildSmartMixSection()
    {
        SmartMixContent.Children.Add(UiKit.SectionHeader("SMART MIX"));
        var intro = new TextBlock
        {
            Text = "Automatic mixing that follows what you're doing — lower music while people talk, and switch profiles with the app in focus.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -4, 0, 12),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        SmartMixContent.Children.Add(intro);

        SmartMixContent.Children.Add(BuildDuckingCard());
        SmartMixContent.Children.Add(BuildAutoSwitchCard());
    }

    private FrameworkElement BuildDuckingCard()
    {
        var sw = MakeSmartSwitch(on =>
        {
            _duckEnabled = on;
            if (_duckBody != null) _duckBody.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            OnSmartMixChanged(null, EventArgs.Empty);
        }, out _setDuckSwitch, "Automatically lower other apps while the trigger app is playing audio");

        _duckBody = new StackPanel { Visibility = Visibility.Collapsed };

        // Trigger
        _duckBody.Children.Add(SubLabel("WHEN THIS APP IS PLAYING", null, out _));
        _duckBody.Children.Add(UiKit.ListContainer(out _duckTriggerRows));

        // Targets
        var targetsLabel = SubLabel("LOWER THESE APPS", "", out _duckTargetCount);
        targetsLabel.Margin = new Thickness(0, 14, 0, 6);
        _duckBody.Children.Add(targetsLabel);
        _duckBody.Children.Add(UiKit.ListContainer(out _duckTargetRows));

        // Amount + fades
        var sliders = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        sliders.Children.Add(SliderRow("Lower by", "How much to reduce volume (100% = fully muted)",
            0, 100, 1, 50, v => $"{(int)v}%", out _duckAmountSlider, out _duckAmountLabel));
        sliders.Children.Add(SliderRow("Fade out", "Time to fade volume down when the trigger starts",
            0, 2000, 10, 200, v => $"{(int)v} ms", out _duckFadeOutSlider, out _duckFadeOutLabel));
        sliders.Children.Add(SliderRow("Fade in", "Time to restore volume after the trigger stops",
            0, 3000, 10, 500, v => $"{(int)v} ms", out _duckFadeInSlider, out _duckFadeInLabel));
        _duckBody.Children.Add(sliders);

        var card = UiKit.Card(SmartDuckColor, MaterialIconKind.AccountVoice, "Voice ducking",
            "Lower other apps while a voice or call app is playing audio.", sw, _duckBody);
        RebuildDuckLists();
        return card.Root;
    }

    private FrameworkElement BuildAutoSwitchCard()
    {
        var sw = MakeSmartSwitch(on =>
        {
            _autoSwitchEnabled = on;
            if (_autoBody != null) _autoBody.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            OnSmartMixChanged(null, EventArgs.Empty);
        }, out _setAutoSwitch, "Switch profiles automatically based on the focused app");

        _autoBody = new StackPanel { Visibility = Visibility.Collapsed };

        _autoBody.Children.Add(SubLabel("RULES", "", out _autoRuleCount));
        _autoBody.Children.Add(UiKit.ListContainer(out _autoRuleRows));

        // Revert option
        var revertSw = MakeSmartSwitch(on => { _autoSwitchRevert = on; OnSmartMixChanged(null, EventArgs.Empty); },
            out _setRevertSwitch, "Go back to the Default profile when no rule matches the focused app");
        var revertRow = new Grid { Margin = new Thickness(2, 14, 0, 0) };
        revertRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        revertRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var revertText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var rt1 = new TextBlock { Text = "Return to Default", FontSize = 12 };
        rt1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var rt2 = new TextBlock { Text = "When the focused app has no rule, switch back to the Default profile.", FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 12, 0) };
        rt2.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        revertText.Children.Add(rt1);
        revertText.Children.Add(rt2);
        revertRow.Children.Add(revertText);
        Grid.SetColumn(revertSw, 1);
        revertRow.Children.Add(revertSw);
        _autoBody.Children.Add(revertRow);

        var card = UiKit.Card(SmartProfileColor, MaterialIconKind.SwapHorizontal, "Auto profile switching",
            "Switch to a profile automatically when a specific app is in focus.", sw, _autoBody);
        RebuildAutoSwitchRules();
        return card.Root;
    }

    // ── Load / collect ─────────────────────────────────────────────────

    private void LoadSmartMixConfig(AppConfig config)
    {
        var duckRule = config.Ducking.Rules.Count > 0 ? config.Ducking.Rules[0] : new DuckingRule();
        _duckEnabled = config.Ducking.Enabled;
        _duckTrigger = duckRule.TriggerApp ?? "";
        _duckTargets.Clear();
        _duckTargets.AddRange(duckRule.TargetApps.Where(a => !string.IsNullOrWhiteSpace(a)));
        _setDuckSwitch?.Invoke(_duckEnabled);
        if (_duckBody != null) _duckBody.Visibility = _duckEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_duckAmountSlider != null) _duckAmountSlider.Value = duckRule.DuckPercent;
        if (_duckFadeOutSlider != null) _duckFadeOutSlider.Value = duckRule.FadeOutMs;
        if (_duckFadeInSlider != null) _duckFadeInSlider.Value = duckRule.FadeInMs;
        if (_duckAmountLabel != null) _duckAmountLabel.Text = $"{duckRule.DuckPercent}%";
        if (_duckFadeOutLabel != null) _duckFadeOutLabel.Text = $"{duckRule.FadeOutMs} ms";
        if (_duckFadeInLabel != null) _duckFadeInLabel.Text = $"{duckRule.FadeInMs} ms";
        RebuildDuckLists();

        _autoSwitchEnabled = config.AutoSwitch.Enabled;
        _autoSwitchRevert = config.AutoSwitch.RevertToDefault;
        _setAutoSwitch?.Invoke(_autoSwitchEnabled);
        _setRevertSwitch?.Invoke(_autoSwitchRevert);
        if (_autoBody != null) _autoBody.Visibility = _autoSwitchEnabled ? Visibility.Visible : Visibility.Collapsed;
        _autoSwitchRules.Clear();
        foreach (var r in config.AutoSwitch.Rules)
            _autoSwitchRules.Add(new AutoSwitchRule { ProcessName = r.ProcessName, ProfileName = r.ProfileName });
        RebuildAutoSwitchRules();
    }

    private void CollectSmartMixConfig(AppConfig config)
    {
        config.Ducking.Enabled = _duckEnabled;
        var duckRule = config.Ducking.Rules.Count > 0 ? config.Ducking.Rules[0] : new DuckingRule();
        if (config.Ducking.Rules.Count == 0) config.Ducking.Rules.Add(duckRule);
        duckRule.TriggerApp = _duckTrigger;
        duckRule.TargetApps = new List<string>(_duckTargets); // empty = duck all
        duckRule.DuckPercent = (int)(_duckAmountSlider?.Value ?? 50);
        duckRule.FadeOutMs = (int)(_duckFadeOutSlider?.Value ?? 200);
        duckRule.FadeInMs = (int)(_duckFadeInSlider?.Value ?? 500);

        config.AutoSwitch.Enabled = _autoSwitchEnabled;
        config.AutoSwitch.RevertToDefault = _autoSwitchRevert;
        config.AutoSwitch.Rules = _autoSwitchRules
            .Where(r => !string.IsNullOrEmpty(r.ProcessName) && !string.IsNullOrEmpty(r.ProfileName))
            .Select(r => new AutoSwitchRule { ProcessName = r.ProcessName, ProfileName = r.ProfileName })
            .ToList();
    }

    private void OnSmartMixChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    // ── Ducking lists ──────────────────────────────────────────────────

    private void RebuildDuckLists()
    {
        if (_duckTriggerRows == null || _duckTargetRows == null) return;

        _duckTriggerRows.Children.Clear();
        if (string.IsNullOrEmpty(_duckTrigger))
        {
            _duckTriggerRows.Children.Add(UiKit.LinkRow(MaterialIconKind.Plus, "Choose trigger app", true,
                null, "The app whose audio triggers ducking (e.g. Discord)"));
            var link = (Border)_duckTriggerRows.Children[0];
            link.MouseLeftButtonUp += (_, e) => { e.Handled = true; ChooseDuckTrigger(link); };
        }
        else
        {
            var app = _duckTrigger;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(UiKit.IconButton(MaterialIconKind.SwapHorizontal, "Change trigger app", ChooseDuckTrigger));
            actions.Children.Add(UiKit.IconButton(MaterialIconKind.Close, "Remove trigger app", _ =>
            {
                _duckTrigger = "";
                RebuildDuckLists();
                OnSmartMixChanged(null, EventArgs.Empty);
            }, danger: true));
            _duckTriggerRows.Children.Add(UiKit.ListRow(SmartAppTile(app), FormatTargetName(app),
                IsAppRunning(app) ? "Playing audio now" : "Not running", null, actions));
        }

        _duckTargetRows.Children.Clear();
        if (_duckTargets.Count == 0)
        {
            var tile = UiKit.IconTile(SmartDuckColor, MaterialIconKind.Apps, 28);
            _duckTargetRows.Children.Add(UiKit.ListRow(tile, "All other apps", "Add apps below to only lower specific ones"));
        }
        foreach (var target in _duckTargets.ToList())
        {
            var app = target;
            var remove = UiKit.IconButton(MaterialIconKind.Close, "Remove", _ =>
            {
                _duckTargets.RemoveAll(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));
                RebuildDuckLists();
                OnSmartMixChanged(null, EventArgs.Empty);
            }, danger: true);
            _duckTargetRows.Children.Add(UiKit.ListRow(SmartAppTile(app), FormatTargetName(app),
                IsAppRunning(app) ? "Playing audio now" : "Not running", null, remove));
        }
        Border? addRow = null;
        addRow = UiKit.LinkRow(MaterialIconKind.Plus, "Add app", true, () => ChooseDuckTarget(addRow!));
        _duckTargetRows.Children.Add(addRow);
        if (_duckTargetCount != null)
            _duckTargetCount.Text = _duckTargets.Count == 0 ? "all" : _duckTargets.Count.ToString();
    }

    private void ChooseDuckTrigger(FrameworkElement anchor)
    {
        var running = SafeRunningApps();
        var choices = running.Select(a => MakeChoice(a, "Playing audio now")).ToList();
        foreach (var app in CommonVoiceApps)
            if (!running.Contains(app, StringComparer.OrdinalIgnoreCase))
                choices.Add(MakeChoice(app, "Common voice app · not running"));
        AppChooser.Show(anchor, choices, picked =>
        {
            _duckTrigger = picked;
            _duckTargets.RemoveAll(a => a.Equals(picked, StringComparison.OrdinalIgnoreCase));
            RebuildDuckLists();
            OnSmartMixChanged(null, EventArgs.Empty);
        });
    }

    private void ChooseDuckTarget(FrameworkElement anchor)
    {
        var choices = SafeRunningApps()
            .Where(a => !a.Equals(_duckTrigger, StringComparison.OrdinalIgnoreCase)
                     && !_duckTargets.Contains(a, StringComparer.OrdinalIgnoreCase))
            .Select(a => MakeChoice(a, "Playing audio now"))
            .ToList();
        AppChooser.Show(anchor, choices, picked =>
        {
            if (picked.Equals(_duckTrigger, StringComparison.OrdinalIgnoreCase)) return;
            if (!_duckTargets.Contains(picked, StringComparer.OrdinalIgnoreCase)) _duckTargets.Add(picked);
            RebuildDuckLists();
            OnSmartMixChanged(null, EventArgs.Empty);
        }, emptyText: "No other apps are playing audio right now.");
    }

    // ── Auto-switch rules ──────────────────────────────────────────────

    private void RebuildAutoSwitchRules()
    {
        if (_autoRuleRows == null) return;
        _autoRuleRows.Children.Clear();

        if (_autoSwitchRules.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "No rules yet — add one to switch profiles when an app is focused.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8, 10, 6),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            _autoRuleRows.Children.Add(hint);
        }

        foreach (var r in _autoSwitchRules.ToList())
        {
            var rule = r;
            bool hasProfile = !string.IsNullOrEmpty(rule.ProfileName);

            // Profile chip: "→ Gaming ▾"
            var chipText = new TextBlock { Text = hasProfile ? rule.ProfileName : "Choose profile", FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            chipText.SetResourceReference(TextBlock.ForegroundProperty, hasProfile ? "TextPrimaryBrush" : "AccentBrush");
            var chipSp = new StackPanel { Orientation = Orientation.Horizontal };
            var arrow = UiKit.Icon(MaterialIconKind.ArrowRight, 13);
            arrow.Margin = new Thickness(0, 0, 6, 0);
            chipSp.Children.Add(arrow);
            chipSp.Children.Add(chipText);
            var caret = UiKit.Icon(MaterialIconKind.MenuDown, 16);
            caret.Margin = new Thickness(2, 0, 0, 0);
            chipSp.Children.Add(caret);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 4, 3), Cursor = Cursors.Hand, Child = chipSp,
                ToolTip = "Profile to activate when the app is focused",
            };
            chip.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
            chip.SetResourceReference(Border.BorderBrushProperty, hasProfile ? "InputBorderBrush" : "AccentBrush");
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; GlassContextMenuHost.Show(chip, ProfileMenuItems(rule)); };

            var more = UiKit.MoreButton(() => new List<GlassMenuItem>
            {
                new("Change app…", MaterialIconKind.Application, () => ChooseRuleApp(chip, rule)),
                new("Switch to profile", MaterialIconKind.SwapHorizontal, null, ProfileMenuItems(rule)),
                GlassMenuItem.Sep,
                new("Remove rule", MaterialIconKind.DeleteOutline, () =>
                {
                    _autoSwitchRules.Remove(rule);
                    RebuildAutoSwitchRules();
                    OnSmartMixChanged(null, EventArgs.Empty);
                }, IsDanger: true),
            });
            more.Margin = new Thickness(6, 0, 0, 0);

            string detail = hasProfile ? $"Switches to {rule.ProfileName} when focused" : "Pick a profile to finish this rule";
            _autoRuleRows.Children.Add(UiKit.ListRow(SmartAppTile(rule.ProcessName), FormatTargetName(rule.ProcessName),
                detail, chip, more));
        }

        Border? addRow = null;
        addRow = UiKit.LinkRow(MaterialIconKind.Plus, "Add rule", true, () =>
            AppChooser.Show(addRow!, SafeRunningApps().Select(a => MakeChoice(a, "Playing audio now")).ToList(), picked =>
            {
                _autoSwitchRules.Add(new AutoSwitchRule { ProcessName = picked, ProfileName = "" });
                RebuildAutoSwitchRules();
                OnSmartMixChanged(null, EventArgs.Empty);
            }, emptyText: "No apps are playing audio — type the process name of the app instead."));
        _autoRuleRows.Children.Add(addRow);

        if (_autoRuleCount != null)
            _autoRuleCount.Text = _autoSwitchRules.Count.ToString();
    }

    private List<GlassMenuItem> ProfileMenuItems(AutoSwitchRule rule)
    {
        var profiles = _config?.Profiles ?? new List<string>();
        var items = profiles.Select(p => new GlassMenuItem(p, MaterialIconKind.AccountBoxOutline, () =>
        {
            rule.ProfileName = p;
            RebuildAutoSwitchRules();
            OnSmartMixChanged(null, EventArgs.Empty);
        }, IsChecked: p == rule.ProfileName)).ToList();
        if (items.Count == 0)
            items.Add(new GlassMenuItem("No profiles yet", MaterialIconKind.InformationOutline, null, IsEnabled: false));
        return items;
    }

    private void ChooseRuleApp(FrameworkElement anchor, AutoSwitchRule rule)
    {
        AppChooser.Show(anchor, SafeRunningApps().Select(a => MakeChoice(a, "Playing audio now")).ToList(), picked =>
        {
            rule.ProcessName = picked;
            RebuildAutoSwitchRules();
            OnSmartMixChanged(null, EventArgs.Empty);
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private List<string> SafeRunningApps()
    {
        try { return _mixer?.GetRunningAudioApps() ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private bool IsAppRunning(string app) =>
        SafeRunningApps().Any(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));

    private static AppChooser.Choice MakeChoice(string app, string description) =>
        new(app, FormatTargetName(app), description, GetAppIcon(app));

    private static FrameworkElement SmartAppTile(string app)
    {
        var accent = ThemeManager.Accent;
        var tile = UiKit.IconTile(accent, MaterialIconKind.Application, 28);
        var bmp = GetAppIcon(app);
        if (bmp != null) tile.Child = new Image { Source = bmp, Width = 16, Height = 16 };
        return tile;
    }

    private static FrameworkElement SubLabel(string text, string? count, out TextBlock countText)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 6) };
        var tb = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        sp.Children.Add(tb);
        countText = new TextBlock { Text = count ?? "", FontSize = 10, Margin = new Thickness(8, 0, 0, 0) };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        sp.Children.Add(countText);
        return sp;
    }

    private FrameworkElement SliderRow(string caption, string tooltip, double min, double max, double step, double initial,
        Func<double, string> format, out StyledSlider slider, out TextBlock valueLabel)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 0, 2), ToolTip = tooltip };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var cap = new TextBlock { Text = caption, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        cap.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        grid.Children.Add(cap);

        var s = new StyledSlider
        {
            Minimum = min, Maximum = max, Value = initial, Step = step,
            ShowLabel = false, Height = 26, AccentColor = ThemeManager.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(s, 1);
        grid.Children.Add(s);

        var lbl = new TextBlock
        {
            Text = format(initial), FontSize = 12, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        Grid.SetColumn(lbl, 2);
        grid.Children.Add(lbl);

        s.ValueChanged += (_, _) =>
        {
            lbl.Text = format(s.Value);
            OnSmartMixChanged(s, EventArgs.Empty);
        };
        _smartAccentRepaints.Add(() => s.AccentColor = ThemeManager.Accent);
        slider = s;
        valueLabel = lbl;
        return grid;
    }

    private Border MakeSmartSwitch(Action<bool> onChanged, out Action<bool> setState, string? tooltip = null)
    {
        bool on = false;
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 14, Height = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var track = new Border
        {
            Width = 38, Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Cursor = Cursors.Hand,
            Child = knob,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Paint()
        {
            var a = ThemeManager.Accent;
            if (on)
            {
                track.Background = new SolidColorBrush(Color.FromArgb(0x55, a.R, a.G, a.B));
                track.BorderBrush = new SolidColorBrush(a);
                knob.Fill = new SolidColorBrush(a);
            }
            else
            {
                track.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
                track.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
                knob.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextDimBrush");
            }
            knob.Margin = new Thickness(on ? 16 : 0, 0, 0, 0);
        }
        track.MouseLeftButtonUp += (_, e) => { e.Handled = true; on = !on; Paint(); onChanged(on); };
        setState = v => { on = v; Paint(); };
        _smartAccentRepaints.Add(Paint);
        Paint();
        return track;
    }

    private void RefreshSmartMixAccent()
    {
        foreach (var a in _smartAccentRepaints) a();
    }
}
