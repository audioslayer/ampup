using System.Drawing;
using System.Windows.Forms;

namespace WolfMixer;

public class IntegrationSetupForm : Form
{
    // Colors (matching main form palette)
    private static readonly Color BgDark = Color.FromArgb(0x14, 0x14, 0x14);
    private static readonly Color CardBg = Color.FromArgb(0x1C, 0x1C, 0x1C);
    private static readonly Color CardBorder = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color InputBg = Color.FromArgb(0x24, 0x24, 0x24);
    private static readonly Color InputBorder = Color.FromArgb(0x36, 0x36, 0x36);
    private static readonly Color Accent = Color.FromArgb(0x00, 0xB4, 0xD8);
    private static readonly Color TextPrimary = Color.FromArgb(0xE8, 0xE8, 0xE8);
    private static readonly Color TextSec = Color.FromArgb(0x9A, 0x9A, 0x9A);
    private static readonly Color TextDim = Color.FromArgb(0x55, 0x55, 0x55);
    private static readonly Color SuccessGrn = Color.FromArgb(0x00, 0xDD, 0x77);
    private static readonly Color DangerRed = Color.FromArgb(0xFF, 0x44, 0x44);

    private readonly AppConfig _config;
    private readonly HAIntegration _ha;
    private readonly FanController _fan;
    private readonly Action<AppConfig> _onSave;

    // HA controls
    private CheckBox _haEnabled = null!;
    private TextBox _haUrl = null!;
    private TextBox _haToken = null!;
    private Button _haShowToken = null!;
    private Label _haStatus = null!;

    // FC controls
    private CheckBox _fcEnabled = null!;
    private TextBox _fcUrl = null!;
    private Label _fcStatus = null!;

    public IntegrationSetupForm(AppConfig config, HAIntegration ha, FanController fan, Action<AppConfig> onSave)
    {
        _config = config;
        _ha = ha;
        _fan = fan;
        _onSave = onSave;
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Text = "WolfMixer — Integrations";
        Size = new Size(620, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BgDark;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9f);

        int y = 12;

        // === Home Assistant Section ===
        var haPanel = CreateCard("Home Assistant", 12, y, 580, 190);
        Controls.Add(haPanel);

        int py = 30;

        _haEnabled = CreateCheckBox("Enabled", _config.HomeAssistant.Enabled);
        _haEnabled.Location = new Point(14, py);
        haPanel.Controls.Add(_haEnabled);
        py += 30;

        haPanel.Controls.Add(CreateLabel("URL:", 14, py + 2));
        _haUrl = CreateTextBox(_config.HomeAssistant.Url, 60, py, 350);
        haPanel.Controls.Add(_haUrl);
        py += 30;

        haPanel.Controls.Add(CreateLabel("Token:", 14, py + 2));
        _haToken = CreateTextBox(_config.HomeAssistant.Token, 60, py, 310);
        _haToken.UseSystemPasswordChar = true;
        haPanel.Controls.Add(_haToken);

        _haShowToken = CreateButton("Show", 378, py, 55, 25);
        _haShowToken.Click += (_, _) =>
        {
            _haToken.UseSystemPasswordChar = !_haToken.UseSystemPasswordChar;
            _haShowToken.Text = _haToken.UseSystemPasswordChar ? "Show" : "Hide";
        };
        haPanel.Controls.Add(_haShowToken);
        py += 34;

        var haTestBtn = CreateButton("Test Connection", 14, py, 130, 28);
        haTestBtn.Click += async (_, _) => await TestHA();
        haPanel.Controls.Add(haTestBtn);

        var haBrowseBtn = CreateButton("Browse Entities", 154, py, 130, 28);
        haBrowseBtn.Click += async (_, _) => await BrowseHAEntities();
        haPanel.Controls.Add(haBrowseBtn);

        _haStatus = CreateLabel("", 300, py + 5);
        _haStatus.AutoSize = true;
        haPanel.Controls.Add(_haStatus);

        y += 200;

        // === Fan Control Section ===
        var fcPanel = CreateCard("Fan Control", 12, y, 580, 150);
        Controls.Add(fcPanel);

        py = 30;

        _fcEnabled = CreateCheckBox("Enabled", _config.FanControl.Enabled);
        _fcEnabled.Location = new Point(14, py);
        fcPanel.Controls.Add(_fcEnabled);
        py += 30;

        fcPanel.Controls.Add(CreateLabel("URL:", 14, py + 2));
        _fcUrl = CreateTextBox(_config.FanControl.Url, 60, py, 350);
        fcPanel.Controls.Add(_fcUrl);
        py += 34;

        var fcTestBtn = CreateButton("Test Connection", 14, py, 130, 28);
        fcTestBtn.Click += async (_, _) => await TestFC();
        fcPanel.Controls.Add(fcTestBtn);

        var fcListBtn = CreateButton("List Fans", 154, py, 130, 28);
        fcListBtn.Click += async (_, _) => await ListFans();
        fcPanel.Controls.Add(fcListBtn);

        _fcStatus = CreateLabel("", 300, py + 5);
        _fcStatus.AutoSize = true;
        fcPanel.Controls.Add(_fcStatus);

        y += 160;

        // === Tip Label ===
        var tipLabel = new Label
        {
            Text = "Knob targets:  ha_light:light.my_lamp  •  ha_media:media_player.tv  •  ha_fan:fan.bedroom  •  fanctrl:my-fan-id",
            Location = new Point(16, y + 4),
            Size = new Size(580, 18),
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 7.5f),
            BackColor = Color.Transparent
        };
        Controls.Add(tipLabel);

        y += 26;

        // === Save Button ===
        var saveBtn = new Button
        {
            Text = "Save & Close",
            Location = new Point(220, y),
            Size = new Size(160, 36),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) => SaveAndClose();
        Controls.Add(saveBtn);
    }

    private Panel CreateCard(string title, int x, int y, int w, int h)
    {
        var panel = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = CardBg,
            BorderStyle = BorderStyle.None
        };

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);

            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var brush = new SolidBrush(Accent);
            e.Graphics.DrawString(title, font, brush, 12, 8);
        };

        return panel;
    }

    private Label CreateLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(45, 20),
            ForeColor = TextSec,
            BackColor = Color.Transparent
        };
    }

    private TextBox CreateTextBox(string value, int x, int y, int width)
    {
        return new TextBox
        {
            Text = value,
            Location = new Point(x, y),
            Size = new Size(width, 24),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f)
        };
    }

    private CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            Checked = isChecked,
            ForeColor = TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Font = new Font("Segoe UI", 9f)
        };
    }

    private Button CreateButton(string text, int x, int y, int w, int h)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = InputBorder;
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    private async Task TestHA()
    {
        _haStatus.Text = "Testing...";
        _haStatus.ForeColor = TextSec;

        // Temporarily apply current form values
        var tempConfig = new HomeAssistantConfig
        {
            Enabled = true,
            Url = _haUrl.Text.Trim(),
            Token = _haToken.Text.Trim()
        };
        _ha.UpdateConfig(tempConfig);

        var ok = await _ha.TestConnectionAsync();
        _haStatus.Text = ok ? "✓ Connected" : "✗ Failed";
        _haStatus.ForeColor = ok ? SuccessGrn : DangerRed;
    }

    private async Task TestFC()
    {
        _fcStatus.Text = "Testing...";
        _fcStatus.ForeColor = TextSec;

        var tempConfig = new FanControlConfig
        {
            Enabled = true,
            Url = _fcUrl.Text.Trim()
        };
        _fan.UpdateConfig(tempConfig);

        var ok = await _fan.TestConnectionAsync();
        _fcStatus.Text = ok ? "✓ Connected" : "✗ Failed";
        _fcStatus.ForeColor = ok ? SuccessGrn : DangerRed;
    }

    private async Task BrowseHAEntities()
    {
        // Ensure latest config
        var tempConfig = new HomeAssistantConfig
        {
            Enabled = true,
            Url = _haUrl.Text.Trim(),
            Token = _haToken.Text.Trim()
        };
        _ha.UpdateConfig(tempConfig);

        var entities = await _ha.GetEntitiesAsync();
        if (entities.Count == 0)
        {
            MessageBox.Show("No entities found. Check connection and token.", "Home Assistant",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowListPopup("Home Assistant Entities",
            entities.Select(e => $"{e.EntityId}  —  {e.FriendlyName}").ToList());
    }

    private async Task ListFans()
    {
        var tempConfig = new FanControlConfig
        {
            Enabled = true,
            Url = _fcUrl.Text.Trim()
        };
        _fan.UpdateConfig(tempConfig);

        var controllers = await _fan.GetControllersAsync();
        if (controllers.Count == 0)
        {
            MessageBox.Show("No fan controllers found. Check connection.", "Fan Control",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowListPopup("Fan Controllers",
            controllers.Select(c => $"{c.Id}  —  {c.Name}  ({c.Value}%)").ToList());
    }

    private void ShowListPopup(string title, List<string> items)
    {
        var popup = new Form
        {
            Text = title,
            Size = new Size(500, 400),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = BgDark
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = CardBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f)
        };

        foreach (var item in items)
            listBox.Items.Add(item);

        popup.Controls.Add(listBox);
        popup.ShowDialog(this);
    }

    private void SaveAndClose()
    {
        _config.HomeAssistant.Enabled = _haEnabled.Checked;
        _config.HomeAssistant.Url = _haUrl.Text.Trim();
        _config.HomeAssistant.Token = _haToken.Text.Trim();

        _config.FanControl.Enabled = _fcEnabled.Checked;
        _config.FanControl.Url = _fcUrl.Text.Trim();

        // Re-init clients with final saved config
        _ha.UpdateConfig(_config.HomeAssistant);
        _fan.UpdateConfig(_config.FanControl);

        _onSave(_config);
        DialogResult = DialogResult.OK;
        Close();
    }
}
