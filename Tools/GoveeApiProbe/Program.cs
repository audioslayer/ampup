using AmpUp.Core;
using AmpUp.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AmpUp.Tools.GoveeApiProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var argList = new List<string>(args);
        string? apiKey = ConsumeOption(argList, "--api-key");

        if (argList.Count == 0 || IsHelp(argList[0]))
        {
            PrintUsage();
            return 0;
        }

        apiKey = ResolveApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("No Govee API key found. Use --api-key, GOVEE_API_KEY / AMPUP_GOVEE_API_KEY, or your saved AmpUp config.");
            return 1;
        }

        string command = argList[0].ToLowerInvariant();
        using var api = new GoveeCloudApi(apiKey);
        var devices = await api.GetDevicesAsync();

        try
        {
            switch (command)
            {
                case "list":
                    ListDevices(devices);
                    return 0;

                case "inspect":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        await InspectDeviceAsync(api, device);
                        return 0;
                    }

                case "state":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        await PrintStateAsync(api, device);
                        return 0;
                    }

                case "raw-state":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        await PrintRawStateAsync(api, device);
                        return 0;
                    }

                case "on":
                case "off":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, GoveeCloudApi.TurnOnOff(command == "on"));
                        Console.WriteLine(success ? "OK" : "FAILED");
                        return success ? 0 : 2;
                    }

                case "toggle":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        string instance = RequireArg(argList, 2, "toggle instance");
                        bool enabled = ParseOnOff(RequireArg(argList, 3, "on|off"));
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, BuildToggleCapability(instance, enabled));
                        Console.WriteLine(success ? $"OK: {instance}={(enabled ? "on" : "off")}" : "FAILED");
                        return success ? 0 : 2;
                    }

                case "brightness":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        int brightness = int.Parse(RequireArg(argList, 2, "brightness"));
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, GoveeCloudApi.SetBrightness(brightness));
                        Console.WriteLine(success ? "OK" : "FAILED");
                        return success ? 0 : 2;
                    }

                case "color":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        var (r, g, b) = ParseColorArgs(argList.Skip(2).ToList());
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, GoveeCloudApi.SetColor(r, g, b));
                        Console.WriteLine(success ? "OK" : "FAILED");
                        return success ? 0 : 2;
                    }

                case "scene":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        string sceneQuery = JoinTail(argList, 2);
                        var scene = await ResolveSceneAsync(api, device, sceneQuery);
                        if (scene == null) return 1;
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, GoveeCloudApi.SetScene(BuildScenePayload(scene)));
                        Console.WriteLine(success ? $"OK: {scene.Name}" : "FAILED");
                        return success ? 0 : 2;
                    }

                case "scene-cycle":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        int delayMs = argList.Count > 2 && int.TryParse(argList[2], out var parsedDelay) ? parsedDelay : 5000;
                        int limit = argList.Count > 3 && int.TryParse(argList[3], out var parsedLimit) ? parsedLimit : 0;
                        return await CycleScenesAsync(api, device, delayMs, limit);
                    }

                case "music":
                    {
                        var device = ResolveDevice(devices, RequireArg(argList, 1, "device"));
                        if (device == null) return 1;
                        int modeId = int.Parse(RequireArg(argList, 2, "music mode id"));
                        int sensitivity = argList.Count > 3 && int.TryParse(argList[3], out var parsedSensitivity)
                            ? parsedSensitivity
                            : 50;
                        bool success = await api.ControlDeviceAsync(
                            device.Device, device.Sku, GoveeCloudApi.SetMusicMode(modeId, sensitivity));
                        Console.WriteLine(success ? "OK" : "FAILED");
                        return success ? 0 : 2;
                    }

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> CycleScenesAsync(GoveeCloudApi api, GoveeDeviceInfo device, int delayMs, int limit)
    {
        var scenes = await api.GetDynamicScenesAsync(device.Device, device.Sku);
        if (scenes.Count == 0)
        {
            Console.Error.WriteLine("No dynamic scenes found for that device.");
            return 1;
        }

        int total = limit > 0 ? Math.Min(limit, scenes.Count) : scenes.Count;
        Console.WriteLine($"Cycling {total} scene(s) on {device.DeviceName} with {delayMs}ms delay...");
        for (int i = 0; i < total; i++)
        {
            var scene = scenes[i];
            Console.WriteLine($"[{i + 1}/{total}] {scene.Name} ({scene.Id})");
            bool success = await api.ControlDeviceAsync(
                device.Device, device.Sku, GoveeCloudApi.SetScene(BuildScenePayload(scene)));
            Console.WriteLine(success ? "  -> OK" : "  -> FAILED");
            if (i < total - 1)
                await Task.Delay(delayMs);
        }

        return 0;
    }

    private static async Task InspectDeviceAsync(GoveeCloudApi api, GoveeDeviceInfo device)
    {
        Console.WriteLine($"Name:   {device.DeviceName}");
        Console.WriteLine($"SKU:    {device.Sku}");
        Console.WriteLine($"Device: {device.Device}");
        Console.WriteLine($"Caps:   {string.Join(", ", device.Capabilities)}");
        Console.WriteLine();

        var state = await api.GetDeviceStateAsync(device.Device, device.Sku);
        if (state != null)
        {
            Console.WriteLine($"State: online={state.Online} on={state.On} brightness={state.Brightness} rgb=({state.R},{state.G},{state.B}) tempK={state.ColorTemp}");
            Console.WriteLine();
        }

        Console.WriteLine("Capabilities:");
        if (device.RawCapabilities is { Count: > 0 } rawCaps)
        {
            foreach (var cap in rawCaps)
            {
                string type = cap["type"]?.ToString() ?? "";
                string instance = cap["instance"]?.ToString() ?? "";
                Console.WriteLine($"- {type} | {instance}");
                var parameters = cap["parameters"];
                if (parameters != null)
                {
                    Console.WriteLine($"  parameters: {parameters.ToString(Formatting.None)}");
                }
            }
        }
        else
        {
            Console.WriteLine("- none");
        }

        Console.WriteLine();
        var scenes = await api.GetDynamicScenesAsync(device.Device, device.Sku);
        Console.WriteLine($"Dynamic Scenes ({scenes.Count}):");
        foreach (var scene in scenes)
            Console.WriteLine($"- {scene.Name} | {scene.Id} | raw={scene.RawValue?.ToString(Formatting.None)}");

        Console.WriteLine();
        var diyScenes = await api.GetDiyScenesAsync(device.Device, device.Sku);
        Console.WriteLine($"DIY Scenes ({diyScenes.Count}):");
        foreach (var scene in diyScenes)
            Console.WriteLine($"- {scene.Name} | {scene.Id} | raw={scene.RawValue?.ToString(Formatting.None)}");
    }

    private static async Task PrintStateAsync(GoveeCloudApi api, GoveeDeviceInfo device)
    {
        var state = await api.GetDeviceStateAsync(device.Device, device.Sku);
        if (state == null)
        {
            Console.Error.WriteLine("No state returned.");
            return;
        }

        Console.WriteLine(JsonConvert.SerializeObject(state, Formatting.Indented));
    }

    private static async Task PrintRawStateAsync(GoveeCloudApi api, GoveeDeviceInfo device)
    {
        var state = await api.GetDeviceStateRawAsync(device.Device, device.Sku);
        if (state == null)
        {
            Console.Error.WriteLine("No raw state returned.");
            return;
        }

        Console.WriteLine(state.ToString(Formatting.Indented));
    }

    private static object BuildScenePayload(GoveeScene scene)
    {
        if (scene.RawValue != null)
            return scene.RawValue;

        return new { id = scene.Id };
    }

    private static GoveeCapability BuildToggleCapability(string instance, bool enabled)
    {
        return new GoveeCapability
        {
            Type = "devices.capabilities.toggle",
            Instance = instance,
            Value = enabled ? 1 : 0
        };
    }

    private static bool ParseOnOff(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "on" or "1" or "true" => true,
            "off" or "0" or "false" => false,
            _ => throw new ArgumentException($"Expected on/off, got '{value}'.")
        };
    }

    private static async Task<GoveeScene?> ResolveSceneAsync(GoveeCloudApi api, GoveeDeviceInfo device, string query)
    {
        var scenes = await api.GetDynamicScenesAsync(device.Device, device.Sku);
        if (scenes.Count == 0)
        {
            Console.Error.WriteLine("No dynamic scenes found for that device.");
            return null;
        }

        var exact = scenes.FirstOrDefault(s =>
            string.Equals(s.Id, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name, query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var partial = scenes.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || s.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (partial.Count == 1) return partial[0];
        if (partial.Count > 1)
        {
            Console.Error.WriteLine("Scene query matched multiple scenes:");
            foreach (var scene in partial)
                Console.Error.WriteLine($"- {scene.Name} ({scene.Id})");
            return null;
        }

        Console.Error.WriteLine($"No scene matched '{query}'.");
        return null;
    }

    private static GoveeDeviceInfo? ResolveDevice(List<GoveeDeviceInfo> devices, string query)
    {
        var exact = devices.FirstOrDefault(d =>
            string.Equals(d.Device, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.DeviceName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Sku, query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var partial = devices.Where(d =>
                d.Device.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.DeviceName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.Sku.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (partial.Count == 1) return partial[0];
        if (partial.Count > 1)
        {
            Console.Error.WriteLine("Device query matched multiple devices:");
            foreach (var device in partial)
                Console.Error.WriteLine($"- {device.DeviceName} | {device.Sku} | {device.Device}");
            return null;
        }

        Console.Error.WriteLine($"No device matched '{query}'.");
        return null;
    }

    private static void ListDevices(List<GoveeDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            Console.WriteLine("No devices returned.");
            return;
        }

        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            Console.WriteLine($"[{i}] {d.DeviceName} | SKU={d.Sku} | Device={d.Device} | Caps={string.Join(", ", d.Capabilities)}");
        }
    }

    private static (int R, int G, int B) ParseColorArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            string hex = args[0].Trim().TrimStart('#');
            if (hex.Length != 6)
                throw new ArgumentException("Hex color must be RRGGBB.");

            return (
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        if (args.Count == 3)
            return (int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]));

        throw new ArgumentException("Color must be either RRGGBB or three integers: R G B.");
    }

    private static string ResolveApiKey(string? cliApiKey)
    {
        if (!string.IsNullOrWhiteSpace(cliApiKey))
            return cliApiKey;

        string? envApiKey = Environment.GetEnvironmentVariable("GOVEE_API_KEY")
                            ?? Environment.GetEnvironmentVariable("AMPUP_GOVEE_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
            return envApiKey;

        try
        {
            var config = ConfigManager.Load();
            if (!string.IsNullOrWhiteSpace(config.Ambience.GoveeApiKey))
                return config.Ambience.GoveeApiKey;
        }
        catch
        {
            // Ignore config load failures and fall through.
        }

        return "";
    }

    private static string? ConsumeOption(List<string> args, string optionName)
    {
        int idx = args.FindIndex(a => string.Equals(a, optionName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return null;
        if (idx == args.Count - 1)
            throw new ArgumentException($"Missing value for {optionName}");
        string value = args[idx + 1];
        args.RemoveAt(idx + 1);
        args.RemoveAt(idx);
        return value;
    }

    private static string RequireArg(List<string> args, int index, string label)
    {
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Missing required argument: {label}");
        return args[index];
    }

    private static string JoinTail(List<string> args, int start)
    {
        if (start >= args.Count)
            throw new ArgumentException("Missing trailing argument.");
        return string.Join(" ", args.Skip(start));
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine("""
GoveeApiProbe

Usage:
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] list
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] inspect <device>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] state <device>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] raw-state <device>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] on <device>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] off <device>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] toggle <device> <instance> <on|off>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] brightness <device> <1-100>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] color <device> <RRGGBB|R G B>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] scene <device> <scene id or scene name>
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] scene-cycle <device> [delayMs=5000] [limit=0]
  dotnet run --project Tools/GoveeApiProbe/GoveeApiProbe.csproj -- [--api-key KEY] music <device> <modeId> [sensitivity=50]

Notes:
  - <device> can be a device ID, exact name, SKU, or a unique substring.
  - API key resolution order: --api-key, GOVEE_API_KEY / AMPUP_GOVEE_API_KEY, then AmpUp saved config.
  - Example toggle: toggle "DreamView G1S Pro" dreamViewToggle on
  - Use inspect first on your DreamView device to see whether monitor-sync-like scenes or music modes are exposed.
""");
    }
}
