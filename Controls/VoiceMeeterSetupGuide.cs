namespace AmpUp.Controls;

/// <summary>
/// 3-step wizard for VoiceMeeter: install, turn on the integration, and
/// point Amp Up at the install folder if auto-detection fails. No input;
/// Finish returns WasSuccessful so the caller can turn the integration on.
/// </summary>
public class VoiceMeeterSetupGuide : IntegrationSetupGuide
{
    public const string DefaultInstallFolder = @"C:\Program Files (x86)\VB\Voicemeeter";
    private const string DownloadUrl = "https://vb-audio.com/Voicemeeter/";

    public VoiceMeeterSetupGuide()
        : base("VoiceMeeter Setup Guide", "≋", "#3FA9F5", "#6CC0FA")
    {
        var s1 = MakeStep(1, "Install VoiceMeeter",
            "Install and open VoiceMeeter Basic, Banana, or Potato from VB-Audio.");
        s1.Children.Add(MakeLinkButton("Get VoiceMeeter ↗", DownloadUrl));
        AddStep(s1);

        var s2 = MakeStep(2, "Turn on the integration",
            "When you click Finish, Settings turns on VoiceMeeter. Amp Up checks for a connection every five seconds, so either app can start first.");
        AddStep(s2);

        var s3 = MakeStep(3, "If it can't find VoiceMeeter",
            "After Finish, if the status says Remote API not found, click Browse in Settings and select the VoiceMeeter install folder. The usual location is:");
        s3.Children.Add(MakeCopyCard("INSTALL FOLDER", DefaultInstallFolder));
        s3.Children.Add(MakeSubtext("Once connected, open Mixer to assign a VoiceMeeter strip or bus to a knob."));
        AddStep(s3);

        Init();
    }
}
