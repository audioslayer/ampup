namespace AmpUp.Controls;

/// <summary>
/// 3-step wizard for SignalRGB: install SignalRGB, let Amp Up install its
/// plugin, restart SignalRGB. No input; Finish returns WasSuccessful so the
/// caller can turn the integration on and install the plugin.
/// </summary>
public class SignalRgbSetupGuide : IntegrationSetupGuide
{
    private const string SignalRgbUrl = "https://www.signalrgb.com";

    public SignalRgbSetupGuide()
        : base("SignalRGB Setup Guide", "✦", "#FF3D7F", "#FF6E9E")
    {
        var s1 = MakeStep(1, "Install SignalRGB",
            "Install SignalRGB and open it once so it creates its plugin folder.");
        s1.Children.Add(MakeLinkButton("Get SignalRGB ↗", SignalRgbUrl));
        AddStep(s1);

        var s2 = MakeStep(2, "Install the Amp Up plugin",
            "When you click Finish, Settings turns on SignalRGB and installs the Amp Up plugin, which sends SignalRGB's colors to your knob lights.");
        s2.Children.Add(MakeSubtext("You can reinstall it later with Update plugin in Settings."));
        AddStep(s2);

        var s3 = MakeStep(3, "Restart SignalRGB",
            "After Finish, restart SignalRGB or reload its devices so it picks up the plugin. The status in Settings changes to Receiving once colors arrive.");
        AddStep(s3);

        Init();
    }
}
