namespace WolfMixer;

public class ConfigForm : Form
{
    private AppConfig _config;
    private readonly Action<AppConfig> _onSave;
    private readonly AudioMixer _mixer;
    private System.Windows.Forms.Timer _refreshTimer = new();
    private System.Windows.Forms.Timer _debounceTimer = new();

    // Theme
    private static readonly Color BgDark = Color.FromArgb(27, 27, 27);
    private static readonly Color CardBg = Color.FromArgb(38, 38, 38);
    private static readonly Color CardBorder = Color.FromArgb(55, 55, 55);
    private static readonly Color Accent = Color.FromArgb(0, 180, 216);
    private static readonly Color TextPrimary = Color.FromArgb(224, 224, 224);
    private static readonly Color TextDim = Color.FromArgb(120, 120, 120);
    private static readonly Color InputBg = Color.FromArgb(48, 48, 48);
    private static readonly Color TrackBg = Color.FromArgb(60, 60, 60);

    private static readonly string[] CommonTargets =
        { "master", "mic", "system", "any", "active_window", "output_device", "input_device", "monitor", "discord", "spotify", "chrome" };

    private static readonly (string Label, string Value)[] ButtonActions =
    {
        ("None",            "none"),
        ("Play/Pause",      "media_play_pause"),
        ("Next Track",      "media_next"),
        ("Prev Track",      "media_prev"),
        ("Mute Vol",        "mute_master"),
        ("Mute Mic",        "mute_mic"),
        ("Mute App",        "mute_program"),
        ("Mute Active",     "mute_active_window"),
        ("Launch App",      "launch_exe"),
        ("Close App",       "close_program"),
        ("Cycle Output",    "cycle_output"),
        ("Cycle Input",     "cycle_input"),
        ("Set Output",      "select_output"),
        ("Set Input",       "select_input"),
        ("Macro",           "macro"),
        ("Sys Power",       "system_power"),
        ("Switch Profile",  "switch_profile"),
    };

    private static readonly string[] PowerActions = { "sleep", "lock", "shutdown", "restart", "logoff", "hibernate" };

    private static readonly (string Name, Color Color)[] ColorPresets =
    {
        ("Red",    Color.FromArgb(255, 50, 50)),
        ("Orange", Color.FromArgb(255, 120, 0)),
        ("Yellow", Color.FromArgb(255, 230, 0)),
        ("Green",  Color.FromArgb(0, 220, 70)),
        ("Cyan",   Color.FromArgb(0, 230, 230)),
        ("Blue",   Color.FromArgb(0, 150, 255)),
        ("Purple", Color.FromArgb(160, 50, 255)),
        ("White",  Color.FromArgb(255, 255, 255)),
    };

    // --- Knobs tab controls ---
    private readonly TextBox[] _labelBoxes = new TextBox[5];
    private readonly ComboBox[] _targetCombos = new ComboBox[5];
    private readonly ComboBox[] _knobDeviceCombos = new ComboBox[5];
    private readonly Label[] _knobDeviceLabels = new Label[5];
    private readonly ComboBox[] _curveCombos = new ComboBox[5];
    private readonly NumericUpDown[] _minVolumeNuds = new NumericUpDown[5];
    private readonly NumericUpDown[] _maxVolumeNuds = new NumericUpDown[5];
    private readonly Panel[] _knobPanels = new Panel[5];
    private readonly Label[] _pctLabels = new Label[5];
    private readonly float[] _volumeValues = new float[5];
    private readonly Color[] _knobColors = new Color[5];

    // --- Buttons tab controls ---
    private readonly ComboBox[] _pressActionCombos = new ComboBox[5];
    private readonly TextBox[] _pressPathBoxes = new TextBox[5];
    private readonly Button[] _pressBrowseBtns = new Button[5];
    private readonly Panel[] _pressPathPanels = new Panel[5];
    private readonly TextBox[] _pressMacroBoxes = new TextBox[5];
    private readonly Panel[] _pressMacroPanels = new Panel[5];
    private readonly ComboBox[] _pressDeviceCombos = new ComboBox[5];
    private readonly Panel[] _pressDevicePanels = new Panel[5];
    private readonly ComboBox[] _pressProfileCombos = new ComboBox[5];
    private readonly Panel[] _pressProfilePanels = new Panel[5];
    private readonly ComboBox[] _pressPowerCombos = new ComboBox[5];
    private readonly Panel[] _pressPowerPanels = new Panel[5];

    private readonly ComboBox[] _dblActionCombos = new ComboBox[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly Button[] _dblBrowseBtns = new Button[5];
    private readonly Panel[] _dblPathPanels = new Panel[5];
    private readonly TextBox[] _dblMacroBoxes = new TextBox[5];
    private readonly Panel[] _dblMacroPanels = new Panel[5];
    private readonly ComboBox[] _dblDeviceCombos = new ComboBox[5];
    private readonly Panel[] _dblDevicePanels = new Panel[5];
    private readonly ComboBox[] _dblProfileCombos = new ComboBox[5];
    private readonly Panel[] _dblProfilePanels = new Panel[5];
    private readonly ComboBox[] _dblPowerCombos = new ComboBox[5];
    private readonly Panel[] _dblPowerPanels = new Panel[5];

    private readonly ComboBox[] _holdActionCombos = new ComboBox[5];
    private readonly TextBox[] _holdPathBoxes = new TextBox[5];
    private readonly Button[] _holdBrowseBtns = new Button[5];
    private readonly Panel[] _holdPathPanels = new Panel[5];
    private readonly TextBox[] _holdMacroBoxes = new TextBox[5];
    private readonly Panel[] _holdMacroPanels = new Panel[5];
    private readonly ComboBox[] _holdDeviceCombos = new ComboBox[5];
    private readonly Panel[] _holdDevicePanels = new Panel[5];
    private readonly ComboBox[] _holdProfileCombos = new ComboBox[5];
    private readonly Panel[] _holdProfilePanels = new Panel[5];
    private readonly ComboBox[] _holdPowerCombos = new ComboBox[5];
    private readonly Panel[] _holdPowerPanels = new Panel[5];

    // --- Lights tab controls ---
    private readonly ComboBox[] _effectCombos = new ComboBox[5];
    private readonly Panel[][] _color1Swatches = new Panel[5][];
    private readonly Panel[][] _color2Swatches = new Panel[5][];
    private readonly Color[] _color1 = new Color[5];
    private readonly Color[] _color2 = new Color[5];
    private readonly Panel[] _color2Panels = new Panel[5];
    private readonly Label[] _color2Labels = new Label[5];
    private readonly TrackBar[] _speedSliders = new TrackBar[5];
    private readonly Panel[] _speedPanels = new Panel[5];
    private TrackBar _brightnessSlider = null!;
    private Label _brightnessValueLabel = null!;

    // --- Settings tab controls ---
    private CheckBox _startWithWindowsCb = null!;
    private ComboBox _profileCombo = null!;
    private TextBox _serialPortBox = null!;
    private TextBox _baudRateBox = null!;

    // Cached audio devices
    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

    public ConfigForm(AppConfig config, AudioMixer mixer, Action<AppConfig> onSave)
    {
        _config = config;
        _mixer = mixer;
        _onSave = onSave;

        _audioDevices = _mixer.GetAudioDevices();

        for (int i = 0; i < 5; i++)
        {
            var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
            _knobColors[i] = light != null
                ? Color.FromArgb(light.R, light.G, light.B)
                : Color.FromArgb(0, 220, 70);
            _color1[i] = _knobColors[i];
            _color2[i] = light != null
                ? Color.FromArgb(light.R2, light.G2, light.B2)
                : Color.FromArgb(255, 50, 50);
        }

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "WolfMixer";
        Size = new Size(800, 680);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9);

        // Header
        var logo = new Label
        {
            Text = "WOLFMIXER",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(20, 8),
            AutoSize = true
        };
        Controls.Add(logo);

        var subtitle = new Label
        {
            Text = "VOLUME MIXER",
            Font = new Font("Segoe UI", 8),
            ForeColor = TextDim,
            Location = new Point(172, 14),
            AutoSize = true
        };
        Controls.Add(subtitle);

        // TabControl
        var tabs = new TabControl
        {
            Location = new Point(10, 38),
            Size = new Size(764, 590),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(100, 30),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Point(12, 4)
        };
        tabs.DrawItem += TabControl_DrawItem;
        Controls.Add(tabs);

        var knobsPage = new TabPage("Knobs") { BackColor = BgDark };
        var buttonsPage = new TabPage("Buttons") { BackColor = BgDark };
        var lightsPage = new TabPage("Lights") { BackColor = BgDark };
        var settingsPage = new TabPage("Settings") { BackColor = BgDark };

        tabs.TabPages.Add(knobsPage);
        tabs.TabPages.Add(buttonsPage);
        tabs.TabPages.Add(lightsPage);
        tabs.TabPages.Add(settingsPage);

        BuildKnobsTab(knobsPage);
        BuildButtonsTab(buttonsPage);
        BuildLightsTab(lightsPage);
        BuildSettingsTab(settingsPage);

        // Footer
        var hint = new Label
        {
            Text = "Changes apply automatically",
            ForeColor = TextDim,
            Location = new Point(20, 630),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f)
        };
        Controls.Add(hint);

        // Timers
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (s, e) => RefreshKnobs();
        _refreshTimer.Start();

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _debounceTimer.Tick += (s, e) =>
        {
            _debounceTimer.Stop();
            ApplyAndSave();
        };
    }

    // ---- Tab header painting (dark theme) ----

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tabCtrl = (TabControl)sender!;
        var page = tabCtrl.TabPages[e.Index];
        var bounds = tabCtrl.GetTabRect(e.Index);

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bgColor = selected ? CardBg : BgDark;
        Color fgColor = selected ? Accent : TextDim;

        using (var bgBrush = new SolidBrush(bgColor))
            e.Graphics.FillRectangle(bgBrush, bounds);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using (var fgBrush = new SolidBrush(fgColor))
            e.Graphics.DrawString(page.Text, tabCtrl.Font, fgBrush, bounds, sf);

        if (selected)
        {
            using var pen = new Pen(Accent, 2);
            e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }
    }

    // ================================================================
    //  TAB 1: KNOBS
    // ================================================================

    private void BuildKnobsTab(TabPage page)
    {
        const int colW = 138;
        const int colGap = 8;
        const int startX = 10;

        for (int i = 0; i < 5; i++)
        {
            int x = startX + i * (colW + colGap);
            BuildKnobColumn(page, i, x, 8, colW);
        }
    }

    private void BuildKnobColumn(TabPage page, int idx, int x, int y, int w)
    {
        var knob = _config.Knobs.Count > idx
            ? _config.Knobs[idx]
            : new KnobConfig { Idx = idx, Label = $"Knob {idx + 1}", Target = "master" };

        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, 540),
            BackColor = CardBg
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        page.Controls.Add(card);

        int cy = 6;
        int capturedIdx = idx;

        // Channel number
        var numLbl = new Label
        {
            Text = $"CH {idx + 1}",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(0, cy),
            Size = new Size(w, 14),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.Controls.Add(numLbl);
        cy += 16;

        // Label textbox
        _labelBoxes[idx] = new TextBox
        {
            Text = knob.Label,
            Location = new Point(8, cy),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center
        };
        _labelBoxes[idx].TextChanged += (s, e) => DebounceSave();
        card.Controls.Add(_labelBoxes[idx]);
        cy += 28;

        // Knob gauge
        int knobSize = 96;
        _knobPanels[idx] = new Panel
        {
            Location = new Point((w - knobSize) / 2, cy),
            Size = new Size(knobSize, knobSize),
            BackColor = Color.Transparent
        };
        _knobPanels[idx].Paint += (s, e) => PaintKnobGauge(e.Graphics, capturedIdx, knobSize);
        card.Controls.Add(_knobPanels[idx]);
        cy += knobSize + 2;

        // Percentage label
        _pctLabels[idx] = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = _knobColors[idx],
            Location = new Point(0, cy),
            Size = new Size(w, 18),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.Controls.Add(_pctLabels[idx]);
        cy += 22;

        // Target label
        var tgtLbl = new Label
        {
            Text = "TARGET",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(tgtLbl);
        cy += 13;

        // Target dropdown
        _targetCombos[idx] = new ComboBox
        {
            Location = new Point(8, cy),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f),
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _targetCombos[idx].Items.AddRange(CommonTargets);
        _targetCombos[idx].Text = knob.Target;
        _targetCombos[idx].TextChanged += (s, e) =>
        {
            UpdateKnobDeviceVisibility(capturedIdx);
            DebounceSave();
        };
        _targetCombos[idx].SelectedIndexChanged += (s, e) =>
        {
            UpdateKnobDeviceVisibility(capturedIdx);
            ApplyAndSave();
        };
        card.Controls.Add(_targetCombos[idx]);
        cy += 26;

        // Device picker label
        _knobDeviceLabels[idx] = new Label
        {
            Text = "DEVICE",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true,
            Visible = false
        };
        card.Controls.Add(_knobDeviceLabels[idx]);

        // Device picker combo
        _knobDeviceCombos[idx] = new ComboBox
        {
            Location = new Point(8, cy + 13),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7.5f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false
        };
        PopulateDeviceCombo(_knobDeviceCombos[idx], knob.Target == "input_device" ? false : true, knob.DeviceId);
        _knobDeviceCombos[idx].SelectedIndexChanged += (s, e) => ApplyAndSave();
        card.Controls.Add(_knobDeviceCombos[idx]);
        cy += 40;

        // Response Curve label
        var curveLbl = new Label
        {
            Text = "RESPONSE",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(curveLbl);
        cy += 13;

        _curveCombos[idx] = new ComboBox
        {
            Location = new Point(8, cy),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _curveCombos[idx].Items.AddRange(new object[] { "Linear", "Logarithmic", "Exponential" });
        _curveCombos[idx].SelectedIndex = (int)knob.Curve;
        _curveCombos[idx].SelectedIndexChanged += (s, e) => ApplyAndSave();
        card.Controls.Add(_curveCombos[idx]);
        cy += 28;

        // Volume Range label
        var rangeLbl = new Label
        {
            Text = "RANGE",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(rangeLbl);
        cy += 13;

        // Min/Max side by side
        var minLbl = new Label
        {
            Text = "Min",
            Font = new Font("Segoe UI", 7),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            Size = new Size(24, 16)
        };
        card.Controls.Add(minLbl);

        _minVolumeNuds[idx] = new NumericUpDown
        {
            Location = new Point(32, cy - 2),
            Size = new Size(44, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8),
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(knob.MinVolume, 0, 100)
        };
        _minVolumeNuds[idx].ValueChanged += (s, e) => DebounceSave();
        card.Controls.Add(_minVolumeNuds[idx]);

        var maxLbl = new Label
        {
            Text = "Max",
            Font = new Font("Segoe UI", 7),
            ForeColor = TextDim,
            Location = new Point(80, cy),
            Size = new Size(26, 16)
        };
        card.Controls.Add(maxLbl);

        _maxVolumeNuds[idx] = new NumericUpDown
        {
            Location = new Point(106, cy - 2),
            Size = new Size(44, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8),
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(knob.MaxVolume, 0, 100)
        };
        _maxVolumeNuds[idx].ValueChanged += (s, e) => DebounceSave();
        card.Controls.Add(_maxVolumeNuds[idx]);

        UpdateKnobDeviceVisibility(idx);
    }

    private void UpdateKnobDeviceVisibility(int idx)
    {
        if (_targetCombos[idx] == null || _knobDeviceCombos[idx] == null) return;
        string target = _targetCombos[idx].Text.Trim().ToLowerInvariant();
        bool showDevice = target == "output_device" || target == "input_device";
        _knobDeviceLabels[idx].Visible = showDevice;
        _knobDeviceCombos[idx].Visible = showDevice;
        if (showDevice)
        {
            bool isOutput = target == "output_device";
            PopulateDeviceCombo(_knobDeviceCombos[idx], isOutput, GetSelectedDeviceId(_knobDeviceCombos[idx]));
        }
    }

    // ================================================================
    //  TAB 2: BUTTONS
    // ================================================================

    private void BuildButtonsTab(TabPage page)
    {
        var scrollPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(760, 555),
            AutoScroll = true,
            BackColor = BgDark
        };
        page.Controls.Add(scrollPanel);

        const int colW = 138;
        const int colGap = 8;
        const int startX = 10;

        for (int i = 0; i < 5; i++)
        {
            int x = startX + i * (colW + colGap);
            BuildButtonColumn(scrollPanel, i, x, 8, colW);
        }
    }

    private void BuildButtonColumn(Panel parent, int idx, int x, int y, int w)
    {
        var btn = _config.Buttons.Count > idx
            ? _config.Buttons[idx]
            : new ButtonConfig { Idx = idx, Action = "none" };

        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, 540),
            BackColor = CardBg
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        parent.Controls.Add(card);

        int cy = 6;
        int capturedIdx = idx;

        // Header
        var hdr = new Label
        {
            Text = $"BUTTON {idx + 1}",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(0, cy),
            Size = new Size(w, 16),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.Controls.Add(hdr);
        cy += 20;

        // Press action
        cy = BuildButtonActionRow(card, idx, cy, w, "PRESS", btn.Action, btn.Path ?? "", btn.MacroKeys ?? "",
            btn.DeviceId ?? "", btn.ProfileName ?? "", btn.PowerAction ?? "",
            _pressActionCombos, _pressPathBoxes, _pressBrowseBtns, _pressPathPanels,
            _pressMacroBoxes, _pressMacroPanels, _pressDeviceCombos, _pressDevicePanels,
            _pressProfileCombos, _pressProfilePanels, _pressPowerCombos, _pressPowerPanels, capturedIdx);

        // Divider
        var div1 = new Panel { Location = new Point(8, cy), Size = new Size(w - 16, 1), BackColor = CardBorder };
        card.Controls.Add(div1);
        cy += 5;

        // Double press action
        cy = BuildButtonActionRow(card, idx, cy, w, "\u00D72 PRESS", btn.DoublePressAction ?? "none", btn.DoublePressPath ?? "", "",
            "", "", "",
            _dblActionCombos, _dblPathBoxes, _dblBrowseBtns, _dblPathPanels,
            _dblMacroBoxes, _dblMacroPanels, _dblDeviceCombos, _dblDevicePanels,
            _dblProfileCombos, _dblProfilePanels, _dblPowerCombos, _dblPowerPanels, capturedIdx);

        // Divider
        var div2 = new Panel { Location = new Point(8, cy), Size = new Size(w - 16, 1), BackColor = CardBorder };
        card.Controls.Add(div2);
        cy += 5;

        // Hold action
        cy = BuildButtonActionRow(card, idx, cy, w, "HOLD", btn.HoldAction ?? "none", btn.HoldPath ?? "", "",
            "", "", "",
            _holdActionCombos, _holdPathBoxes, _holdBrowseBtns, _holdPathPanels,
            _holdMacroBoxes, _holdMacroPanels, _holdDeviceCombos, _holdDevicePanels,
            _holdProfileCombos, _holdProfilePanels, _holdPowerCombos, _holdPowerPanels, capturedIdx);

        UpdateButtonSubControlVisibility(idx);
    }

    private int BuildButtonActionRow(Panel card, int idx, int cy, int w, string label,
        string currentAction, string currentPath, string currentMacro,
        string currentDeviceId, string currentProfile, string currentPower,
        ComboBox[] actionCombos, TextBox[] pathBoxes, Button[] browseBtns, Panel[] pathPanels,
        TextBox[] macroBoxes, Panel[] macroPanels, ComboBox[] deviceCombos, Panel[] devicePanels,
        ComboBox[] profileCombos, Panel[] profilePanels, ComboBox[] powerCombos, Panel[] powerPanels,
        int capturedIdx)
    {
        // Row label
        var lbl = new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(lbl);
        cy += 13;

        // Action dropdown
        actionCombos[idx] = new ComboBox
        {
            Location = new Point(8, cy),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7.5f),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var a in ButtonActions)
            actionCombos[idx].Items.Add(a.Label);
        int selIdx = Array.FindIndex(ButtonActions, a => a.Value == (currentAction ?? "none").ToLowerInvariant());
        actionCombos[idx].SelectedIndex = selIdx >= 0 ? selIdx : 0;
        actionCombos[idx].SelectedIndexChanged += (s, e) =>
        {
            UpdateButtonSubControlVisibility(capturedIdx);
            ApplyAndSave();
        };
        card.Controls.Add(actionCombos[idx]);
        cy += 24;

        // Path panel (for launch_exe, close_program)
        pathPanels[idx] = new Panel
        {
            Location = new Point(0, cy),
            Size = new Size(w, 42),
            BackColor = Color.Transparent,
            Visible = false
        };
        card.Controls.Add(pathPanels[idx]);

        pathBoxes[idx] = new TextBox
        {
            Text = currentPath,
            Location = new Point(8, 0),
            Size = new Size(w - 16, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 7),
            ReadOnly = true
        };
        pathPanels[idx].Controls.Add(pathBoxes[idx]);

        browseBtns[idx] = new Button
        {
            Text = "Browse...",
            Location = new Point(8, 22),
            Size = new Size(w - 16, 18),
            BackColor = InputBg,
            ForeColor = TextDim,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7),
            Cursor = Cursors.Hand
        };
        browseBtns[idx].FlatAppearance.BorderColor = CardBorder;
        browseBtns[idx].FlatAppearance.BorderSize = 1;
        browseBtns[idx].Click += (s, e) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = $"Select program for Button {capturedIdx + 1}",
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
            };
            if (!string.IsNullOrEmpty(pathBoxes[capturedIdx].Text))
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(pathBoxes[capturedIdx].Text); }
                catch { }
            }
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                pathBoxes[capturedIdx].Text = dlg.FileName;
                ApplyAndSave();
            }
        };
        pathPanels[idx].Controls.Add(browseBtns[idx]);
        cy += 42;

        // Macro panel
        macroPanels[idx] = new Panel
        {
            Location = new Point(0, cy - 42),
            Size = new Size(w, 22),
            BackColor = Color.Transparent,
            Visible = false
        };
        card.Controls.Add(macroPanels[idx]);

        macroBoxes[idx] = new TextBox
        {
            Text = currentMacro,
            Location = new Point(8, 0),
            Size = new Size(w - 16, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 7.5f)
        };
        macroBoxes[idx].PlaceholderText = "e.g. ctrl+shift+m";
        macroBoxes[idx].TextChanged += (s, e) => DebounceSave();
        macroPanels[idx].Controls.Add(macroBoxes[idx]);

        // Device panel (for select_output, select_input, cycle_output, cycle_input)
        devicePanels[idx] = new Panel
        {
            Location = new Point(0, cy - 42),
            Size = new Size(w, 22),
            BackColor = Color.Transparent,
            Visible = false
        };
        card.Controls.Add(devicePanels[idx]);

        deviceCombos[idx] = new ComboBox
        {
            Location = new Point(8, 0),
            Size = new Size(w - 16, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        PopulateDeviceCombo(deviceCombos[idx], true, currentDeviceId);
        deviceCombos[idx].SelectedIndexChanged += (s, e) => ApplyAndSave();
        devicePanels[idx].Controls.Add(deviceCombos[idx]);

        // Profile panel
        profilePanels[idx] = new Panel
        {
            Location = new Point(0, cy - 42),
            Size = new Size(w, 22),
            BackColor = Color.Transparent,
            Visible = false
        };
        card.Controls.Add(profilePanels[idx]);

        profileCombos[idx] = new ComboBox
        {
            Location = new Point(8, 0),
            Size = new Size(w - 16, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7.5f),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var p in _config.Profiles)
            profileCombos[idx].Items.Add(p);
        if (!string.IsNullOrEmpty(currentProfile))
        {
            int pi = profileCombos[idx].Items.IndexOf(currentProfile);
            if (pi >= 0) profileCombos[idx].SelectedIndex = pi;
        }
        profileCombos[idx].SelectedIndexChanged += (s, e) => ApplyAndSave();
        profilePanels[idx].Controls.Add(profileCombos[idx]);

        // Power panel
        powerPanels[idx] = new Panel
        {
            Location = new Point(0, cy - 42),
            Size = new Size(w, 22),
            BackColor = Color.Transparent,
            Visible = false
        };
        card.Controls.Add(powerPanels[idx]);

        powerCombos[idx] = new ComboBox
        {
            Location = new Point(8, 0),
            Size = new Size(w - 16, 20),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7.5f),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        powerCombos[idx].Items.AddRange(PowerActions);
        if (!string.IsNullOrEmpty(currentPower))
        {
            int pi = Array.IndexOf(PowerActions, currentPower.ToLowerInvariant());
            if (pi >= 0) powerCombos[idx].SelectedIndex = pi;
        }
        powerCombos[idx].SelectedIndexChanged += (s, e) => ApplyAndSave();
        powerPanels[idx].Controls.Add(powerCombos[idx]);

        return cy;
    }

    private void UpdateButtonSubControlVisibility(int idx)
    {
        UpdateActionRowVisibility(idx, _pressActionCombos, _pressPathPanels, _pressMacroPanels,
            _pressDevicePanels, _pressProfilePanels, _pressPowerPanels, _pressDeviceCombos);
        UpdateActionRowVisibility(idx, _dblActionCombos, _dblPathPanels, _dblMacroPanels,
            _dblDevicePanels, _dblProfilePanels, _dblPowerPanels, _dblDeviceCombos);
        UpdateActionRowVisibility(idx, _holdActionCombos, _holdPathPanels, _holdMacroPanels,
            _holdDevicePanels, _holdProfilePanels, _holdPowerPanels, _holdDeviceCombos);
    }

    private void UpdateActionRowVisibility(int idx, ComboBox[] actionCombos, Panel[] pathPanels,
        Panel[] macroPanels, Panel[] devicePanels, Panel[] profilePanels, Panel[] powerPanels, ComboBox[] deviceCombos)
    {
        if (actionCombos[idx] == null) return;
        int si = actionCombos[idx].SelectedIndex;
        string action = si >= 0 ? ButtonActions[si].Value : "none";

        bool showPath = action == "launch_exe" || action == "close_program";
        bool showMacro = action == "macro";
        bool showDevice = action == "select_output" || action == "select_input" || action == "cycle_output" || action == "cycle_input";
        bool showProfile = action == "switch_profile";
        bool showPower = action == "system_power";

        pathPanels[idx].Visible = showPath;
        macroPanels[idx].Visible = showMacro;
        devicePanels[idx].Visible = showDevice;
        profilePanels[idx].Visible = showProfile;
        powerPanels[idx].Visible = showPower;

        if (showDevice)
        {
            bool isOutput = action == "select_output" || action == "cycle_output";
            PopulateDeviceCombo(deviceCombos[idx], isOutput, GetSelectedDeviceId(deviceCombos[idx]));
        }

        // Reposition visible sub-panel
        Panel? visiblePanel = showPath ? pathPanels[idx]
            : showMacro ? macroPanels[idx]
            : showDevice ? devicePanels[idx]
            : showProfile ? profilePanels[idx]
            : showPower ? powerPanels[idx]
            : null;

        if (visiblePanel != null)
        {
            int comboBottom = actionCombos[idx].Bottom;
            visiblePanel.Location = new Point(0, comboBottom + 2);
        }
    }

    // ================================================================
    //  TAB 3: LIGHTS
    // ================================================================

    private void BuildLightsTab(TabPage page)
    {
        const int colW = 138;
        const int colGap = 8;
        const int startX = 10;

        for (int i = 0; i < 5; i++)
        {
            int x = startX + i * (colW + colGap);
            BuildLightColumn(page, i, x, 8, colW);
        }

        // Global brightness slider at bottom
        var brightnessLbl = new Label
        {
            Text = "GLOBAL BRIGHTNESS",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(startX + 8, 480),
            AutoSize = true
        };
        page.Controls.Add(brightnessLbl);

        _brightnessSlider = new TrackBar
        {
            Location = new Point(startX + 140, 475),
            Size = new Size(400, 30),
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_config.LedBrightness, 0, 100),
            TickFrequency = 10,
            BackColor = BgDark
        };
        _brightnessSlider.ValueChanged += (s, e) =>
        {
            _brightnessValueLabel.Text = $"{_brightnessSlider.Value}%";
            DebounceSave();
        };
        page.Controls.Add(_brightnessSlider);

        _brightnessValueLabel = new Label
        {
            Text = $"{_brightnessSlider.Value}%",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(startX + 550, 480),
            AutoSize = true
        };
        page.Controls.Add(_brightnessValueLabel);
    }

    private void BuildLightColumn(TabPage page, int idx, int x, int y, int w)
    {
        var light = _config.Lights.FirstOrDefault(l => l.Idx == idx)
            ?? new LightConfig { Idx = idx, R = 0, G = 150, B = 255 };

        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, 460),
            BackColor = CardBg
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        page.Controls.Add(card);

        int cy = 6;
        int capturedIdx = idx;

        // Header
        var hdr = new Label
        {
            Text = $"KNOB {idx + 1}",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(0, cy),
            Size = new Size(w, 16),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.Controls.Add(hdr);
        cy += 20;

        // Effect type label
        var effectLbl = new Label
        {
            Text = "EFFECT",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(effectLbl);
        cy += 13;

        _effectCombos[idx] = new ComboBox
        {
            Location = new Point(8, cy),
            Size = new Size(w - 16, 22),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7.5f),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        string[] effectNames = Enum.GetNames(typeof(LightEffect));
        _effectCombos[idx].Items.AddRange(effectNames);
        _effectCombos[idx].SelectedIndex = (int)light.Effect;
        _effectCombos[idx].SelectedIndexChanged += (s, e) =>
        {
            UpdateLightSubControlVisibility(capturedIdx);
            ApplyAndSave();
        };
        card.Controls.Add(_effectCombos[idx]);
        cy += 28;

        // Color 1 label
        var c1Lbl = new Label
        {
            Text = "COLOR 1",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(c1Lbl);
        cy += 14;

        // Color 1 swatches
        cy = BuildColorSwatches(card, idx, cy, w, _color1Swatches, _color1, capturedIdx, true);

        // Custom button for color 1
        var custom1Btn = new Button
        {
            Text = "Custom...",
            Location = new Point((w - 100) / 2, cy),
            Size = new Size(100, 18),
            BackColor = InputBg,
            ForeColor = TextDim,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7),
            Cursor = Cursors.Hand
        };
        custom1Btn.FlatAppearance.BorderColor = CardBorder;
        custom1Btn.FlatAppearance.BorderSize = 1;
        custom1Btn.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = _color1[capturedIdx], FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
                SetLightColor(capturedIdx, dlg.Color, true);
        };
        card.Controls.Add(custom1Btn);
        cy += 24;

        // Color 2 label (in a panel for visibility toggling)
        _color2Labels[idx] = new Label
        {
            Text = "COLOR 2",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, cy),
            AutoSize = true
        };
        card.Controls.Add(_color2Labels[idx]);
        cy += 14;

        // Color 2 panel (contains swatches + custom button)
        _color2Panels[idx] = new Panel
        {
            Location = new Point(0, cy),
            Size = new Size(w, 80),
            BackColor = Color.Transparent
        };
        card.Controls.Add(_color2Panels[idx]);

        int c2y = 0;
        // Color 2 swatches
        int swatchSize = 22;
        int swatchGap = 4;
        int swatchRowW = 4 * swatchSize + 3 * swatchGap;
        int swatchStartX = (w - swatchRowW) / 2;

        _color2Swatches[idx] = new Panel[ColorPresets.Length];
        for (int c = 0; c < ColorPresets.Length; c++)
        {
            int ci = c;
            int row = c / 4;
            int col = c % 4;
            var swatch = new Panel
            {
                Location = new Point(swatchStartX + col * (swatchSize + swatchGap), c2y + row * (swatchSize + swatchGap)),
                Size = new Size(swatchSize, swatchSize),
                BackColor = ColorPresets[c].Color,
                Cursor = Cursors.Hand
            };
            swatch.Paint += (s, e) => PaintSwatch(e.Graphics, swatch, capturedIdx, ColorPresets[ci].Color, false);
            swatch.Click += (s, e) => SetLightColor(capturedIdx, ColorPresets[ci].Color, false);
            _color2Panels[idx].Controls.Add(swatch);
            _color2Swatches[idx][c] = swatch;
        }
        c2y += 2 * (swatchSize + swatchGap) + 2;

        var custom2Btn = new Button
        {
            Text = "Custom...",
            Location = new Point(swatchStartX, c2y),
            Size = new Size(swatchRowW, 18),
            BackColor = InputBg,
            ForeColor = TextDim,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7),
            Cursor = Cursors.Hand
        };
        custom2Btn.FlatAppearance.BorderColor = CardBorder;
        custom2Btn.FlatAppearance.BorderSize = 1;
        custom2Btn.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = _color2[capturedIdx], FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
                SetLightColor(capturedIdx, dlg.Color, false);
        };
        _color2Panels[idx].Controls.Add(custom2Btn);
        cy += 80;

        // Speed panel
        _speedPanels[idx] = new Panel
        {
            Location = new Point(0, cy),
            Size = new Size(w, 50),
            BackColor = Color.Transparent
        };
        card.Controls.Add(_speedPanels[idx]);

        var speedLbl = new Label
        {
            Text = "SPEED",
            Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(8, 0),
            AutoSize = true
        };
        _speedPanels[idx].Controls.Add(speedLbl);

        _speedSliders[idx] = new TrackBar
        {
            Location = new Point(8, 14),
            Size = new Size(w - 16, 30),
            Minimum = 1,
            Maximum = 100,
            Value = Math.Clamp(light.EffectSpeed, 1, 100),
            TickFrequency = 10,
            BackColor = CardBg
        };
        _speedSliders[idx].ValueChanged += (s, e) => DebounceSave();
        _speedPanels[idx].Controls.Add(_speedSliders[idx]);

        UpdateLightSubControlVisibility(idx);
    }

    private int BuildColorSwatches(Panel card, int idx, int cy, int w, Panel[][] swatchArrayOut, Color[] colorArray, int capturedIdx, bool isPrimary)
    {
        int swatchSize = 22;
        int swatchGap = 4;
        int swatchRowW = 4 * swatchSize + 3 * swatchGap;
        int swatchStartX = (w - swatchRowW) / 2;

        swatchArrayOut[idx] = new Panel[ColorPresets.Length];
        for (int c = 0; c < ColorPresets.Length; c++)
        {
            int ci = c;
            int row = c / 4;
            int col = c % 4;
            var swatch = new Panel
            {
                Location = new Point(swatchStartX + col * (swatchSize + swatchGap), cy + row * (swatchSize + swatchGap)),
                Size = new Size(swatchSize, swatchSize),
                BackColor = ColorPresets[c].Color,
                Cursor = Cursors.Hand
            };
            swatch.Paint += (s, e) => PaintSwatch(e.Graphics, swatch, capturedIdx, ColorPresets[ci].Color, isPrimary);
            swatch.Click += (s, e) => SetLightColor(capturedIdx, ColorPresets[ci].Color, isPrimary);
            card.Controls.Add(swatch);
            swatchArrayOut[idx][c] = swatch;
        }
        cy += 2 * (swatchSize + swatchGap) + 2;
        return cy;
    }

    private void UpdateLightSubControlVisibility(int idx)
    {
        if (_effectCombos[idx] == null) return;
        var effect = (LightEffect)_effectCombos[idx].SelectedIndex;

        bool needsColor2 = effect == LightEffect.ColorBlend || effect == LightEffect.Blink ||
                           effect == LightEffect.Pulse || effect == LightEffect.MicStatus ||
                           effect == LightEffect.DeviceMute;
        bool needsSpeed = effect == LightEffect.Blink || effect == LightEffect.Pulse ||
                          effect == LightEffect.RainbowWave || effect == LightEffect.RainbowCycle;

        _color2Labels[idx].Visible = needsColor2;
        _color2Panels[idx].Visible = needsColor2;
        _speedPanels[idx].Visible = needsSpeed;
    }

    private void SetLightColor(int idx, Color color, bool isPrimary)
    {
        if (isPrimary)
        {
            _color1[idx] = color;
            _knobColors[idx] = color;
            _knobPanels[idx]?.Invalidate();
            if (_pctLabels[idx] != null)
                _pctLabels[idx].ForeColor = color;
            if (_color1Swatches[idx] != null)
                foreach (var s in _color1Swatches[idx])
                    s?.Invalidate();
        }
        else
        {
            _color2[idx] = color;
            if (_color2Swatches[idx] != null)
                foreach (var s in _color2Swatches[idx])
                    s?.Invalidate();
        }
        ApplyAndSave();
    }

    // ================================================================
    //  TAB 4: SETTINGS
    // ================================================================

    private void BuildSettingsTab(TabPage page)
    {
        int cy = 20;
        int leftX = 30;

        // Start with Windows
        _startWithWindowsCb = new CheckBox
        {
            Text = "Start with Windows",
            Font = new Font("Segoe UI", 10),
            ForeColor = TextPrimary,
            Location = new Point(leftX, cy),
            AutoSize = true,
            Checked = _config.StartWithWindows,
            BackColor = Color.Transparent
        };
        _startWithWindowsCb.CheckedChanged += (s, e) => ApplyAndSave();
        page.Controls.Add(_startWithWindowsCb);
        cy += 40;

        // Active Profile
        var profileLbl = new Label
        {
            Text = "ACTIVE PROFILE",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(leftX, cy),
            AutoSize = true
        };
        page.Controls.Add(profileLbl);
        cy += 18;

        _profileCombo = new ComboBox
        {
            Location = new Point(leftX, cy),
            Size = new Size(200, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var p in _config.Profiles)
            _profileCombo.Items.Add(p);
        int activeIdx = _profileCombo.Items.IndexOf(_config.ActiveProfile);
        if (activeIdx >= 0) _profileCombo.SelectedIndex = activeIdx;
        else if (_profileCombo.Items.Count > 0) _profileCombo.SelectedIndex = 0;
        _profileCombo.SelectedIndexChanged += (s, e) => ApplyAndSave();
        page.Controls.Add(_profileCombo);

        var saveProfileBtn = new Button
        {
            Text = "Save",
            Location = new Point(leftX + 210, cy),
            Size = new Size(60, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8),
            Cursor = Cursors.Hand
        };
        saveProfileBtn.FlatAppearance.BorderColor = CardBorder;
        saveProfileBtn.Click += (s, e) =>
        {
            string name = _profileCombo.SelectedItem?.ToString() ?? "Default";
            ConfigManager.SaveProfile(_config, name);
        };
        page.Controls.Add(saveProfileBtn);

        var loadProfileBtn = new Button
        {
            Text = "Load",
            Location = new Point(leftX + 278, cy),
            Size = new Size(60, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8),
            Cursor = Cursors.Hand
        };
        loadProfileBtn.FlatAppearance.BorderColor = CardBorder;
        loadProfileBtn.Click += (s, e) =>
        {
            string name = _profileCombo.SelectedItem?.ToString() ?? "Default";
            var loaded = ConfigManager.LoadProfile(name);
            if (loaded != null)
            {
                _config = loaded;
                _onSave(_config);
                Close();
            }
        };
        page.Controls.Add(loadProfileBtn);

        var newProfileBtn = new Button
        {
            Text = "New...",
            Location = new Point(leftX + 346, cy),
            Size = new Size(60, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8),
            Cursor = Cursors.Hand
        };
        newProfileBtn.FlatAppearance.BorderColor = CardBorder;
        newProfileBtn.Click += (s, e) =>
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("Profile name:", "New Profile", "");
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (!_config.Profiles.Contains(name))
                    _config.Profiles.Add(name);
                _config.ActiveProfile = name;
                _profileCombo.Items.Add(name);
                _profileCombo.SelectedItem = name;
                ApplyAndSave();
            }
        };
        page.Controls.Add(newProfileBtn);
        cy += 44;

        // Serial Port
        var portLbl = new Label
        {
            Text = "SERIAL PORT",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(leftX, cy),
            AutoSize = true
        };
        page.Controls.Add(portLbl);
        cy += 18;

        _serialPortBox = new TextBox
        {
            Text = _config.Serial.Port,
            Location = new Point(leftX, cy),
            Size = new Size(120, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9)
        };
        _serialPortBox.TextChanged += (s, e) => DebounceSave();
        page.Controls.Add(_serialPortBox);
        cy += 40;

        // Baud Rate
        var baudLbl = new Label
        {
            Text = "BAUD RATE",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TextDim,
            Location = new Point(leftX, cy),
            AutoSize = true
        };
        page.Controls.Add(baudLbl);
        cy += 18;

        _baudRateBox = new TextBox
        {
            Text = _config.Serial.Baud.ToString(),
            Location = new Point(leftX, cy),
            Size = new Size(120, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9)
        };
        _baudRateBox.TextChanged += (s, e) => DebounceSave();
        page.Controls.Add(_baudRateBox);
    }

    // ================================================================
    //  SHARED HELPERS
    // ================================================================

    private void PopulateDeviceCombo(ComboBox combo, bool isOutput, string selectedDeviceId)
    {
        combo.Items.Clear();
        combo.Tag = null;
        var devices = _audioDevices.Where(d => d.IsOutput == isOutput).ToList();
        int selIdx = -1;
        for (int i = 0; i < devices.Count; i++)
        {
            combo.Items.Add(devices[i].Name);
            if (devices[i].Id == selectedDeviceId)
                selIdx = i;
        }
        // Store device list in Tag for lookup
        combo.Tag = devices;
        if (selIdx >= 0)
            combo.SelectedIndex = selIdx;
        else if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static string GetSelectedDeviceId(ComboBox combo)
    {
        if (combo.Tag is List<(string Id, string Name, bool IsOutput)> devices && combo.SelectedIndex >= 0 && combo.SelectedIndex < devices.Count)
            return devices[combo.SelectedIndex].Id;
        return "";
    }

    // ---- Knob Gauge Painting ----

    private void PaintKnobGauge(Graphics g, int idx, int size)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int pad = 6;
        var rect = new Rectangle(pad, pad, size - pad * 2, size - pad * 2);
        float startAngle = 135f;
        float totalSweep = 270f;

        using (var trackPen = new Pen(TrackBg, 8f))
        {
            trackPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            trackPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawArc(trackPen, rect, startAngle, totalSweep);
        }

        float pct = _volumeValues[idx];
        if (pct > 0.005f)
        {
            float valueSweep = totalSweep * pct;
            using var fillPen = new Pen(_knobColors[idx], 8f);
            fillPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            fillPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawArc(fillPen, rect, startAngle, valueSweep);
        }

        int centerSize = 36;
        int cx = (size - centerSize) / 2;
        using (var centerBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
            g.FillEllipse(centerBrush, cx, cx, centerSize, centerSize);
        using (var centerPen = new Pen(CardBorder, 1.5f))
            g.DrawEllipse(centerPen, cx, cx, centerSize, centerSize);

        if (pct > 0.005f)
        {
            float angle = startAngle + totalSweep * pct;
            float rad = (float)(angle * Math.PI / 180.0);
            float arcR = (size - pad * 2) / 2f;
            float dotX = size / 2f + arcR * (float)Math.Cos(rad);
            float dotY = size / 2f + arcR * (float)Math.Sin(rad);
            using var dotBrush = new SolidBrush(Color.White);
            g.FillEllipse(dotBrush, dotX - 4, dotY - 4, 8, 8);
        }
    }

    // ---- Color Swatch Painting ----

    private void PaintSwatch(Graphics g, Panel swatch, int knobIdx, Color presetColor, bool isPrimary)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(presetColor);
        g.FillRectangle(brush, 1, 1, swatch.Width - 2, swatch.Height - 2);

        Color activeColor = isPrimary ? _color1[knobIdx] : _color2[knobIdx];
        if (activeColor.R == presetColor.R &&
            activeColor.G == presetColor.G &&
            activeColor.B == presetColor.B)
        {
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawRectangle(pen, 2, 2, swatch.Width - 5, swatch.Height - 5);
        }
    }

    // ---- Refresh ----

    private void RefreshKnobs()
    {
        for (int i = 0; i < 5; i++)
        {
            if (_config.Knobs.Count > i)
            {
                float vol = _mixer.GetVolume(_config.Knobs[i]);
                _volumeValues[i] = vol;
                _knobPanels[i]?.Invalidate();
                if (_pctLabels[i] != null)
                    _pctLabels[i].Text = $"{(int)(vol * 100)}%";
            }
        }
    }

    // ---- Save ----

    private void DebounceSave()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ApplyAndSave()
    {
        // Knobs
        for (int i = 0; i < 5; i++)
        {
            while (_config.Knobs.Count <= i)
                _config.Knobs.Add(new KnobConfig { Idx = i });

            if (_labelBoxes[i] != null)
                _config.Knobs[i].Label = _labelBoxes[i].Text.Trim();
            if (_targetCombos[i] != null)
                _config.Knobs[i].Target = _targetCombos[i].Text.Trim().ToLower();
            if (_knobDeviceCombos[i] != null)
                _config.Knobs[i].DeviceId = GetSelectedDeviceId(_knobDeviceCombos[i]);
            if (_curveCombos[i] != null)
                _config.Knobs[i].Curve = (ResponseCurve)_curveCombos[i].SelectedIndex;
            if (_minVolumeNuds[i] != null)
                _config.Knobs[i].MinVolume = (int)_minVolumeNuds[i].Value;
            if (_maxVolumeNuds[i] != null)
                _config.Knobs[i].MaxVolume = (int)_maxVolumeNuds[i].Value;
        }

        // Buttons
        for (int i = 0; i < 5; i++)
        {
            while (_config.Buttons.Count <= i)
                _config.Buttons.Add(new ButtonConfig { Idx = i });

            // Press
            if (_pressActionCombos[i] != null)
            {
                int ai = _pressActionCombos[i].SelectedIndex;
                _config.Buttons[i].Action = ai >= 0 ? ButtonActions[ai].Value : "none";
            }
            if (_pressPathBoxes[i] != null)
                _config.Buttons[i].Path = _pressPathBoxes[i].Text.Trim();
            if (_pressMacroBoxes[i] != null)
                _config.Buttons[i].MacroKeys = _pressMacroBoxes[i].Text.Trim();
            if (_pressDeviceCombos[i] != null)
                _config.Buttons[i].DeviceId = GetSelectedDeviceId(_pressDeviceCombos[i]);
            if (_pressProfileCombos[i] != null && _pressProfileCombos[i].SelectedItem is string profileStr)
                _config.Buttons[i].ProfileName = profileStr;
            if (_pressPowerCombos[i] != null && _pressPowerCombos[i].SelectedItem is string powerStr)
                _config.Buttons[i].PowerAction = powerStr;

            // Double press
            if (_dblActionCombos[i] != null)
            {
                int ai = _dblActionCombos[i].SelectedIndex;
                _config.Buttons[i].DoublePressAction = ai >= 0 ? ButtonActions[ai].Value : "none";
            }
            if (_dblPathBoxes[i] != null)
                _config.Buttons[i].DoublePressPath = _dblPathBoxes[i].Text.Trim();

            // Hold
            if (_holdActionCombos[i] != null)
            {
                int ai = _holdActionCombos[i].SelectedIndex;
                _config.Buttons[i].HoldAction = ai >= 0 ? ButtonActions[ai].Value : "none";
            }
            if (_holdPathBoxes[i] != null)
                _config.Buttons[i].HoldPath = _holdPathBoxes[i].Text.Trim();
        }

        // Lights
        for (int i = 0; i < 5; i++)
        {
            var light = _config.Lights.FirstOrDefault(l => l.Idx == i);
            if (light == null)
            {
                light = new LightConfig { Idx = i };
                _config.Lights.Add(light);
            }
            light.R = _color1[i].R;
            light.G = _color1[i].G;
            light.B = _color1[i].B;
            light.R2 = _color2[i].R;
            light.G2 = _color2[i].G;
            light.B2 = _color2[i].B;
            if (_effectCombos[i] != null)
                light.Effect = (LightEffect)_effectCombos[i].SelectedIndex;
            if (_speedSliders[i] != null)
                light.EffectSpeed = _speedSliders[i].Value;
        }

        // Global brightness
        if (_brightnessSlider != null)
            _config.LedBrightness = _brightnessSlider.Value;

        // Settings
        if (_startWithWindowsCb != null)
            _config.StartWithWindows = _startWithWindowsCb.Checked;
        if (_profileCombo != null && _profileCombo.SelectedItem != null)
            _config.ActiveProfile = _profileCombo.SelectedItem.ToString() ?? "Default";
        if (_serialPortBox != null)
            _config.Serial.Port = _serialPortBox.Text.Trim();
        if (_baudRateBox != null && int.TryParse(_baudRateBox.Text.Trim(), out int baud) && baud > 0)
            _config.Serial.Baud = baud;

        _onSave(_config);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _debounceTimer.Stop();
        base.OnFormClosing(e);
    }
}
