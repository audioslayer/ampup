using System.Windows.Controls;

namespace AmpUp.Controls;

/// <summary>
/// 4-step wizard for Home Assistant: find the server URL, create a
/// long-lived access token, paste both. Modal returns WasSuccessful +
/// Url + Token for the caller to save and run Test connection.
/// </summary>
public class HomeAssistantSetupGuide : IntegrationSetupGuide
{
    public string Url { get; private set; } = "";
    public string Token { get; private set; } = "";

    public const string DefaultUrl = "http://homeassistant.local:8123";
    private const string TokenDocsUrl = "https://www.home-assistant.io/docs/authentication/";

    private readonly TextBox _urlBox;
    private readonly PasswordBox _tokenBox;

    public HomeAssistantSetupGuide()
        : base("Home Assistant Setup Guide", "⌂", "#18BCF2", "#4FD0FA")
    {
        var s1 = MakeStep(1, "Find your Home Assistant address",
            "Open Home Assistant in your browser and note the address you use. Most installs answer on port 8123.");
        s1.Children.Add(MakeCopyCard("DEFAULT URL", DefaultUrl));
        s1.Children.Add(MakeSubtext("If that name doesn't work, use the IP address instead, like http://192.168.1.50:8123."));
        AddStep(s1);

        var s2 = MakeStep(2, "Create a long-lived access token",
            "In Home Assistant, click your user name at the bottom left, open the Security tab, scroll to Long-lived access tokens and click Create token. Name it \"Amp Up\".");
        s2.Children.Add(MakeLinkButton("Token help ↗", TokenDocsUrl));
        s2.Children.Add(MakeSubtext("Copy the token right away. Home Assistant only shows it once."));
        AddStep(s2);

        var s3 = MakeStep(3, "Paste your details",
            "Enter the URL and paste the token. Click Finish and Settings will save them and test the connection.");
        s3.Children.Add(MakeFieldLabel("URL"));
        _urlBox = MakeTextInput(DefaultUrl);
        s3.Children.Add(_urlBox);
        s3.Children.Add(MakeFieldLabel("LONG-LIVED ACCESS TOKEN"));
        _tokenBox = MakePasswordInput();
        s3.Children.Add(_tokenBox);
        AddStep(s3);

        Init();
    }

    protected override bool TryFinish()
    {
        var url = _urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) { _urlBox.Focus(); return false; }
        var token = _tokenBox.Password.Trim();
        if (string.IsNullOrEmpty(token)) { _tokenBox.Focus(); return false; }
        Url = url;
        Token = token;
        return true;
    }
}
