using System.Windows.Controls;

namespace AmpUp.Controls;

/// <summary>
/// 4-step wizard for OBS Studio's built-in WebSocket server: install OBS,
/// enable the server, read Connect Info, enter host/port/password. Modal
/// returns WasSuccessful + Host + Port + Password for the caller to save
/// and run Test connection.
/// </summary>
public class ObsSetupGuide : IntegrationSetupGuide
{
    public string Host { get; private set; } = DefaultHost;
    public int Port { get; private set; } = DefaultPort;
    public string Password { get; private set; } = "";

    public const string DefaultHost = "localhost";
    public const int DefaultPort = 4455;
    private const string ObsUrl = "https://obsproject.com";

    private readonly TextBox _hostBox;
    private readonly TextBox _portBox;
    private readonly PasswordBox _passwordBox;

    public ObsSetupGuide()
        : base("OBS Studio Setup Guide", "●", "#9B7BFF", "#B49BFF")
    {
        var s1 = MakeStep(1, "Install OBS Studio",
            "Amp Up talks to OBS through the WebSocket server that is built into OBS Studio 28 and newer. Install or update OBS, then open it.");
        s1.Children.Add(MakeLinkButton("Get OBS Studio ↗", ObsUrl));
        AddStep(s1);

        var s2 = MakeStep(2, "Enable the WebSocket server",
            "In OBS, open Tools → WebSocket Server Settings and tick Enable WebSocket server. Click Apply.");
        s2.Children.Add(MakeCopyCard("DEFAULT PORT", DefaultPort.ToString()));
        s2.Children.Add(MakeSubtext("Leave the port at 4455 unless something else already uses it."));
        AddStep(s2);

        var s3 = MakeStep(3, "Copy the connect info",
            "In the same window, click Show Connect Info. Note the port and copy the server password.");
        s3.Children.Add(MakeSubtext("OBS on another PC? Use that PC's IP address as the host and allow the port through its firewall."));
        AddStep(s3);

        var s4 = MakeStep(4, "Enter the connection",
            "Fill in the host, port and password. Click Finish and Settings will save them and test the connection.");
        s4.Children.Add(MakeFieldLabel("HOST"));
        _hostBox = MakeTextInput(DefaultHost);
        s4.Children.Add(_hostBox);
        s4.Children.Add(MakeFieldLabel("PORT"));
        _portBox = MakeTextInput(DefaultPort.ToString());
        s4.Children.Add(_portBox);
        s4.Children.Add(MakeFieldLabel("PASSWORD"));
        _passwordBox = MakePasswordInput();
        s4.Children.Add(_passwordBox);
        AddStep(s4);

        Init();
    }

    protected override bool TryFinish()
    {
        var host = _hostBox.Text.Trim();
        if (string.IsNullOrEmpty(host)) { _hostBox.Focus(); return false; }
        if (!int.TryParse(_portBox.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            _portBox.Focus();
            return false;
        }
        Host = host;
        Port = port;
        Password = _passwordBox.Password;
        return true;
    }
}
