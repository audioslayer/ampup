namespace AmpUp.Controls;

/// <summary>
/// 3-step wizard for Corsair iCUE: install iCUE, enable its SDK (third-party
/// control), turn on the integration. No input; Finish returns
/// WasSuccessful so the caller can turn the integration on.
/// </summary>
public class CorsairSetupGuide : IntegrationSetupGuide
{
    private const string IcueUrl = "https://www.corsair.com/icue";

    public CorsairSetupGuide()
        : base("Corsair iCUE Setup Guide", "◆", "#FFD200", "#FFE04D")
    {
        var s1 = MakeStep(1, "Install iCUE",
            "Amp Up controls Corsair lighting through iCUE 5 or newer. Install it and make sure it is running.");
        s1.Children.Add(MakeLinkButton("Get iCUE ↗", IcueUrl));
        AddStep(s1);

        var s2 = MakeStep(2, "Enable the iCUE SDK",
            "In iCUE, open Settings and turn on the SDK option that lets third-party apps control lighting.");
        s2.Children.Add(MakeSubtext("Without it, Settings shows \"iCUE not detected\"."));
        AddStep(s2);

        var s3 = MakeStep(3, "Turn on the integration",
            "Click Finish to turn on Corsair iCUE in Settings. Your Corsair devices will appear in the list below the switch.");
        s3.Children.Add(MakeSubtext("No devices? Check that they show up in iCUE first."));
        AddStep(s3);

        Init();
    }
}
