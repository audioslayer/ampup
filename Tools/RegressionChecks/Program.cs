using System.Reflection;
using System.Runtime.CompilerServices;
using AmpUp;
using AmpUp.Core.Engine;
using AmpUp.Core.Models;
using AmpUp.Core.Services;
using NAudio.Dsp;
using NAudio.Wave;

const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS {name}");
    passed++;
}
byte[] Frame(RgbController rgb)
{
    typeof(RgbController).GetMethod("Tick", PrivateInstance)!.Invoke(rgb, null);
    return ((byte[])typeof(RgbController).GetField("_linearColors", PrivateInstance)!.GetValue(rgb)!).ToArray();
}

using var rgb = new RgbController();
var light = new LightConfig { Idx = 0, Effect = LightEffect.ColorBlendMute, R = 20, G = 40, B = 60, R2 = 200, G2 = 100, B2 = 80 };
rgb.UpdateConfig(new() { light });
rgb.SetKnobPosition(0, 0);
var zero = Frame(rgb);
rgb.SetKnobPosition(0, 1);
var full = Frame(rgb);
Check(!zero.Take(9).SequenceEqual(full.Take(9)), "Blend+Mute spans its gradient");
rgb.SetTargetMuted(0, true);
Check(Frame(rgb).Take(9).SequenceEqual(zero.Take(9)), "Blend+Mute mute equals zero-volume color");
rgb.SetTargetMuted(0, false);
Check(Frame(rgb).Take(9).SequenceEqual(full.Take(9)), "Blend+Mute unmute restores volume color");
Check(full.Take(3).SequenceEqual(full.Skip(3).Take(3)) && full.Take(3).SequenceEqual(full.Skip(6).Take(3)), "Blend+Mute fills all three LEDs equally");
light.Effect = LightEffect.PositionBlendMute;
rgb.UpdateConfig(new() { light });
var positional = Frame(rgb);
rgb.SetTargetMuted(0, true);
var muted = Frame(rgb);
Check(muted.Take(3).SequenceEqual(new byte[] { 80, 40, 32 }), "Pos+Mute retains dim high-end mute color");
rgb.SetTargetMuted(0, false);
Check(Frame(rgb).Take(9).SequenceEqual(positional.Take(9)), "Pos+Mute unmute restores position fill");

// Exercise the empty-group polling branch without starting WPF or touching audio devices.
var app = (App)RuntimeHelpers.GetUninitializedObject(typeof(App));
var config = new AppConfig();
config.Knobs = new() { new KnobConfig { Idx = 0, Target = "apps", Apps = new() } };
config.Lights = new() { light };
typeof(App).GetField("_config", PrivateInstance)!.SetValue(app, config);
typeof(App).GetField("_rgb", PrivateInstance)!.SetValue(app, rgb);
rgb.SetTargetMuted(0, true);
typeof(App).GetMethod("PollLinkedTargetMuteStates", PrivateInstance)!.Invoke(app, new object?[] { null, null });
Check(Frame(rgb).Take(9).SequenceEqual(positional.Take(9)), "Clearing an app group clears stale mute lighting");
foreach (string target in new[] { "master", "mic", "vm_strip:2", "vm_bus:3" })
{
    config.Knobs[0].Target = target;
    bool vm = target.StartsWith("vm_");
    string method = vm ? "SetVoiceMeeterTargetMuteState" : "SetDirectTargetMuteState";
    foreach (bool state in new[] { true, false })
    {
        object[] notificationArgs = vm
            ? new object[] { target.StartsWith("vm_strip"), int.Parse(target.Split(':')[1]), state }
            : new object[] { target, state };
        typeof(App).GetMethod(method, PrivateInstance)!.Invoke(app, notificationArgs);
        Check(Frame(rgb).Take(9).SequenceEqual((state ? muted : positional).Take(9)),
            $"{target} notification immediately updates mute={state}");
    }
}
Check((int)typeof(App).GetMethod("GetMutePollingPeriodMs", PrivateInstance)!.Invoke(app, null)! == 1000,
    "Mute-aware effects select one-second polling");
var matcher = typeof(AudioMixer).GetMethod("FuzzyContains", BindingFlags.Static | BindingFlags.NonPublic)!;
Check((bool)matcher.Invoke(null, new object[] { "AppleMusic", "Apple Music" })!, "App matching ignores spaces");
Check(!(bool)matcher.Invoke(null, new object[] { "spotify", "discord" })!, "App matching rejects unrelated sessions");

var pidGroupConfig = new AppConfig();
pidGroupConfig.Knobs.Add(new KnobConfig
{
    Idx = 2, Target = "apps",
    Apps = new() { "amplibraryagent", "amplibraryagent:16136", "applemusic", "applemusic:7048", "vm_strip:1x" },
});
AmpUp.Core.ConfigManager.NormalizeAppGroups(pidGroupConfig);
Check(pidGroupConfig.Knobs[0].Apps.SequenceEqual(new[] { "amplibraryagent", "applemusic", "vm_strip:1x" }),
    "App groups drop stale PID-pinned session entries");

using var analyzer = new AudioAnalyzer();
var samples = (float[])typeof(AudioAnalyzer).GetField("_sampleBuffer", PrivateInstance)!.GetValue(analyzer)!;
for (int i = 0; i < samples.Length; i++) samples[i] = 0.01f * MathF.Sin(2 * MathF.PI * 1500 * i / 48000);
typeof(AudioAnalyzer).GetMethod("ProcessFft", PrivateInstance)!.Invoke(analyzer, new object[] { 48000 });
// Compare broad-band response to the original 1024-point analysis at the same amplitude.
var baseline = new Complex[1024];
for (int i = 0; i < baseline.Length; i++)
    baseline[i].X = 0.01f * MathF.Sin(2 * MathF.PI * 1500 * i / 48000)
        * (0.5f * (1f - MathF.Cos(2f * MathF.PI * i / 1023)));
FastFourierTransform.FFT(true, 10, baseline);
float power = 0;
for (int i = 5; i <= 42; i++) power += baseline[i].X * baseline[i].X + baseline[i].Y * baseline[i].Y;
float expected = MathF.Sqrt(power / 38) / 0.005f * 0.5f;
Check(MathF.Abs(analyzer.SmoothedBands[2] / expected - 1) < 0.03f, "4096-point FFT preserves previous broad-band sensitivity");
Span<float> spectrum = stackalloc float[15];
analyzer.CopySpectrum(spectrum, 20000, 20000);
Check(spectrum.ToArray().All(float.IsFinite), "Spectrum accepts its upper frequency boundary");
// Feed synthetic capture callbacks; no WASAPI device is opened.
var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
void SetAnalyzerField(string name, object value) => typeof(AudioAnalyzer).GetField(name, PrivateInstance)!.SetValue(analyzer, value);
SetAnalyzerField("_running", true);
SetAnalyzerField("_sourceFormat", format);
SetAnalyzerField("_normalizedFormat", format);
SetAnalyzerField("_formatChannels", 1);
SetAnalyzerField("_formatBytesPerSample", 4);
SetAnalyzerField("_formatFrameBytes", 4);
SetAnalyzerField("_formatSampleRate", 48000);
var callback = typeof(AudioAnalyzer).GetMethod("OnDataAvailable", PrivateInstance)!;
var bytes = new byte[samples.Length * 4];
Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
callback.Invoke(analyzer, new object?[] { null, new WaveInEventArgs(bytes, bytes.Length) });
Check((int)typeof(AudioAnalyzer).GetField("_bufferPos", PrivateInstance)!.GetValue(analyzer)! == 2048,
    "FFT retains half its window for 23Hz updates at 48kHz");
callback.Invoke(analyzer, new object?[] { null, new WaveInEventArgs(bytes, bytes.Length / 2) });
Check((int)typeof(AudioAnalyzer).GetField("_bufferPos", PrivateInstance)!.GetValue(analyzer)! == 2048,
    "Next FFT completes after 2048 new samples");
analyzer.Stop();
analyzer.CopySpectrum(spectrum);
Check(spectrum.ToArray().All(x => x == 0), "Stopping analysis clears spectrum");
// Fake only the native connection boundary; exercise the real login/monitor lifecycle.
VoiceMeeterIntegration MakeVm(Func<bool> discover, Func<int> login, Func<int> logout, Func<int> refresh)
    => (VoiceMeeterIntegration)Activator.CreateInstance(typeof(VoiceMeeterIntegration),
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { discover, login, logout, refresh }, null)!;
void PollVm(VoiceMeeterIntegration vm)
{
    var gate = typeof(VoiceMeeterIntegration).GetField("_lock", PrivateInstance)!.GetValue(vm)!;
    lock (gate)
        typeof(VoiceMeeterIntegration).GetMethod("RefreshConnection", PrivateInstance)!.Invoke(vm, null);
}
int vmLogins = 0, vmLogouts = 0, engineResult = -2;
using (var vm = MakeVm(() => true, () => { vmLogins++; return 1; },
    () => { vmLogouts++; return 0; }, () => engineResult))
{
    var states = new List<bool>();
    vm.StateChanged += () => states.Add(vm.IsConnected);
    Check(!vm.Connect() && vm.IsAvailable && !vm.IsConnected,
        "VoiceMeeter login before engine launch reports waiting, not connected");
    engineResult = 0;
    PollVm(vm);
    Check(vm.IsConnected && vmLogins == 1, "Later VoiceMeeter launch connects using the existing login");
    engineResult = -2;
    PollVm(vm);
    Check(!vm.IsConnected, "VoiceMeeter engine shutdown is detected without knob activity");
    engineResult = 1;
    PollVm(vm);
    Check(vm.IsConnected && vmLogins == 1 && states.SequenceEqual(new[] { true, false, true }),
        "VoiceMeeter restart reconnects once and publishes status transitions");
    vm.Disconnect();
    PollVm(vm);
    bool actionConnected = (bool)typeof(VoiceMeeterIntegration).GetMethod("EnsureConnected", PrivateInstance)!.Invoke(vm, null)!;
    Check(!vm.IsConnected && !actionConnected && vmLogins == 1 && vmLogouts == 1,
        "Disabling VoiceMeeter cancels retries and actions cannot re-enable it");
    Check(vm.Connect() && vmLogins == 2, "Re-enabling VoiceMeeter creates a fresh login");
}
Check(vmLogouts == 2, "VoiceMeeter disposal releases its final login exactly once");

int retryLogins = 0;
using var failedLoginVm = MakeVm(() => true,
    () => Interlocked.Increment(ref retryLogins) == 1 ? -1 : 0, () => 0, () => 0);
Check(!failedLoginVm.Connect(), "Initial VoiceMeeter login failure stays disconnected");
int dllReady = 0;
using var lateInstallVm = MakeVm(() => Volatile.Read(ref dllReady) == 1, () => 0, () => 0, () => 0);
Check(!lateInstallVm.Connect() && !lateInstallVm.IsAvailable, "Missing VoiceMeeter DLL still starts discovery retries");
Volatile.Write(ref dllReady, 1);
var retryDeadline = DateTime.UtcNow.AddSeconds(8);
while ((!failedLoginVm.IsConnected || !lateInstallVm.IsConnected) && DateTime.UtcNow < retryDeadline)
    Thread.Sleep(50);
Check(failedLoginVm.IsConnected && retryLogins == 2,
    "Background monitor retries a failed first login without user interaction");
Check(lateInstallVm.IsAvailable && lateInstallVm.IsConnected,
    "Background monitor discovers a later VoiceMeeter install without restarting AmpUp");
failedLoginVm.Dispose();
lateInstallVm.Dispose();
Check(!failedLoginVm.Connect() && !lateInstallVm.Connect(), "Disposed VoiceMeeter integrations cannot restart monitoring");

var parseInstall = typeof(VoiceMeeterIntegration).GetMethod("GetDirectoryFromUninstallString", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (string command in new[] { "\"C:\\Custom Apps\\VoiceMeeter\\uninstall.exe\" /S", "C:\\Custom Apps\\VoiceMeeter\\uninstall.exe /S" })
    Check((string?)parseInstall.Invoke(null, new object[] { command }) == @"C:\Custom Apps\VoiceMeeter",
        "VoiceMeeter detects custom install paths with spaces: " + command);
var manualDirectories = (IEnumerable<string>)typeof(VoiceMeeterIntegration)
    .GetMethod("GetInstallDirs", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, new object[] { @"D:\Audio Apps\VoiceMeeter" })!;
Check(manualDirectories.SequenceEqual(new[] { @"D:\Audio Apps\VoiceMeeter" }),
    "Explicit VoiceMeeter folder takes precedence over automatic discovery");
var savedVmConfig = new VoiceMeeterConfig { Enabled = true, InstallDirectory = @"D:\Audio Apps\VoiceMeeter" };
var restoredVmConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<VoiceMeeterConfig>(
    Newtonsoft.Json.JsonConvert.SerializeObject(savedVmConfig))!;
Check(restoredVmConfig.Enabled && restoredVmConfig.InstallDirectory == savedVmConfig.InstallDirectory,
    "VoiceMeeter install folder survives saving and loading configuration");
bool manualDllReady = false;
using (var manualVm = MakeVm(() => manualDllReady, () => 0, () => 0, () => 0))
{
    manualVm.Connect();
    manualDllReady = true;
    manualVm.SetInstallDirectory(savedVmConfig.InstallDirectory);
    Check(manualVm.IsConnected && manualVm.IsAvailable,
        "Selecting a folder immediately retries failed VoiceMeeter discovery");
    typeof(VoiceMeeterIntegration).GetField("_dllPath", PrivateInstance)!
        .SetValue(manualVm, System.IO.Path.Combine(savedVmConfig.InstallDirectory, "VoicemeeterRemote64.dll"));
    Check(!manualVm.RequiresRestart, "Matching the loaded VoiceMeeter folder does not require a restart");
    manualVm.SetInstallDirectory(@"E:\VoiceMeeter");
    Check(manualVm.RequiresRestart, "Switching an already loaded VoiceMeeter API requests a restart");
    manualVm.SetInstallDirectory("");
    Check(!manualVm.RequiresRestart, "Automatic detection clears the explicit VoiceMeeter folder override");
}
Console.WriteLine($"{passed} regression checks passed.");
