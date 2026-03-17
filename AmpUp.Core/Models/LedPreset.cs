namespace AmpUp.Core.Models;

public class LedPreset
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Ambient";
    public List<LightConfig> Lights { get; set; } = new();
    public LightConfig? GlobalLight { get; set; }
    public bool IsBuiltIn { get; set; } = false;
}
