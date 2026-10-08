using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

/// <summary>
/// Shared, named app groups. A knob (or N3 encoder) with Target "apps" links to an
/// <see cref="AppGroupDef"/> by name via <see cref="KnobConfig.AppGroup"/>; its
/// <see cref="KnobConfig.Apps"/> list is a mirror of the group's list so every existing
/// reader (audio routing, mute LEDs, mute_app_group buttons) keeps working unchanged.
/// Edits always go through here so every linked control stays in sync.
/// </summary>
public static class AppGroupSync
{
    /// <summary>Every knob config in the file: Turn Up knobs, N3 encoders and their per-context / per-Space copies.</summary>
    public static IEnumerable<KnobConfig> AllKnobs(AppConfig config)
    {
        foreach (var k in config.Knobs) yield return k;
        if (config.N3 == null) yield break;
        foreach (var k in config.N3.Knobs) yield return k;
        foreach (var ctx in config.N3.EncoderContexts) foreach (var k in ctx.Knobs) yield return k;
        foreach (var f in config.N3.Folders) foreach (var ctx in f.EncoderContexts) foreach (var k in ctx.Knobs) yield return k;
    }

    public static AppGroupDef? Find(AppConfig config, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : config.AppGroups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<KnobConfig> Users(AppConfig config, AppGroupDef group) =>
        AllKnobs(config).Where(k => k.Target == "apps" && k.AppGroup.Equals(group.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Load-time pass: give every app-group knob that has apps but no group its own named
    /// group (so existing setups carry over), create missing groups, and re-mirror lists.
    /// </summary>
    public static void Normalize(AppConfig config)
    {
        config.AppGroups ??= new();
        config.AppGroups.RemoveAll(g => g == null || string.IsNullOrWhiteSpace(g.Name));
        foreach (var k in AllKnobs(config))
        {
            k.AppGroup ??= "";
            if (k.Target != "apps") continue;
            if (string.IsNullOrWhiteSpace(k.AppGroup))
            {
                if (k.Apps.Count == 0) continue;
                var g = Create(config, string.IsNullOrWhiteSpace(k.Label) ? "App group" : k.Label, k.Apps);
                k.AppGroup = g.Name;
            }
            else if (Find(config, k.AppGroup) == null)
            {
                config.AppGroups.Add(new AppGroupDef { Name = k.AppGroup.Trim(), Apps = new List<string>(k.Apps) });
            }
        }
        foreach (var g in config.AppGroups) Mirror(config, g);
    }

    /// <summary>Create a group with a unique name ("Music", "Music 2", …).</summary>
    public static AppGroupDef Create(AppConfig config, string baseName, IEnumerable<string>? apps = null)
    {
        string name = UniqueName(config, baseName);
        var g = new AppGroupDef { Name = name, Apps = apps?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new() };
        config.AppGroups.Add(g);
        return g;
    }

    public static string UniqueName(AppConfig config, string baseName, AppGroupDef? except = null)
    {
        string root = string.IsNullOrWhiteSpace(baseName) ? "App group" : baseName.Trim();
        string name = root;
        for (int n = 2; config.AppGroups.Any(g => g != except && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{root} {n}";
        return name;
    }

    /// <summary>Point a knob at a group (or none with null) and copy its apps in.</summary>
    public static void Assign(AppConfig config, KnobConfig knob, AppGroupDef? group)
    {
        knob.AppGroup = group?.Name ?? "";
        knob.Apps = group != null ? new List<string>(group.Apps) : new List<string>();
    }

    /// <summary>
    /// After a knob's Apps list was edited in place: write it back to its group (creating
    /// one if the knob had none) and mirror it onto every other knob using that group.
    /// </summary>
    public static void CommitFromKnob(AppConfig config, KnobConfig knob)
    {
        var g = Find(config, knob.AppGroup);
        if (g == null)
        {
            if (knob.Apps.Count == 0) return;
            g = Create(config, string.IsNullOrWhiteSpace(knob.Label) ? "App group" : knob.Label);
            knob.AppGroup = g.Name;
        }
        g.Apps = knob.Apps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Mirror(config, g);
    }

    /// <summary>Copy the group's apps onto every knob that uses it.</summary>
    public static void Mirror(AppConfig config, AppGroupDef group)
    {
        foreach (var k in Users(config, group))
            k.Apps = new List<string>(group.Apps);
    }

    public static void Rename(AppConfig config, AppGroupDef group, string newName)
    {
        string name = UniqueName(config, newName, group);
        foreach (var k in Users(config, group).ToList()) k.AppGroup = name;
        group.Name = name;
    }

    /// <summary>Delete a group. Knobs that used it keep their current apps but are no longer linked.</summary>
    public static void Delete(AppConfig config, AppGroupDef group)
    {
        foreach (var k in Users(config, group).ToList()) k.AppGroup = "";
        config.AppGroups.Remove(group);
    }
}
