using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AmpUp.Services;

/// <summary>
/// Sends keyboard combos ("ctrl+shift+m", "f13", "alt+numadd") via SendInput with scan codes
/// (apps like Discord's global keybinds and games ignore scan-code-less events).
/// Also owns the key-name table shared by the parser and the key-capture dialog.
/// </summary>
public static class KeyboardInjector
{
    // ── Key name table ────────────────────────────────────────────────

    private static readonly Dictionary<string, ushort> NameToVk = BuildNameTable();
    private static readonly Dictionary<ushort, string> VkToName = BuildReverseTable();

    private static readonly HashSet<ushort> Modifiers = new()
    {
        0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C
    };

    private static readonly HashSet<ushort> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home arrows
        0x2C, 0x2D, 0x2E,                               // PrtSc Ins Del
        0x5B, 0x5C, 0x5D,                               // Win keys, Menu
        0x6F, 0x90,                                     // numpad /, NumLock
        0xA3, 0xA5,                                     // RCtrl, RAlt
    };

    private static Dictionary<string, ushort> BuildNameTable()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // modifiers
            ["ctrl"] = 0xA2, ["control"] = 0xA2, ["lctrl"] = 0xA2, ["rctrl"] = 0xA3,
            ["shift"] = 0xA0, ["lshift"] = 0xA0, ["rshift"] = 0xA1,
            ["alt"] = 0xA4, ["lalt"] = 0xA4, ["ralt"] = 0xA5, ["altgr"] = 0xA5,
            ["win"] = 0x5B, ["windows"] = 0x5B, ["super"] = 0x5B, ["lwin"] = 0x5B, ["rwin"] = 0x5C,
            // basic
            ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09,
            ["escape"] = 0x1B, ["esc"] = 0x1B, ["space"] = 0x20, ["spacebar"] = 0x20,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["delete"] = 0x2E, ["del"] = 0x2E, ["backspace"] = 0x08,
            ["insert"] = 0x2D, ["ins"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
            ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
            ["printscreen"] = 0x2C, ["prtsc"] = 0x2C,
            ["capslock"] = 0x14, ["numlock"] = 0x90, ["scrolllock"] = 0x91,
            ["pause"] = 0x13, ["menu"] = 0x5D, ["apps"] = 0x5D,
            // punctuation
            ["semicolon"] = 0xBA, ["equals"] = 0xBB, ["plus"] = 0xBB, ["comma"] = 0xBC,
            ["minus"] = 0xBD, ["period"] = 0xBE, ["slash"] = 0xBF, ["backtick"] = 0xC0,
            ["lbracket"] = 0xDB, ["backslash"] = 0xDC, ["rbracket"] = 0xDD, ["quote"] = 0xDE,
            // numpad
            ["nummultiply"] = 0x6A, ["numadd"] = 0x6B, ["numsubtract"] = 0x6D,
            ["numdecimal"] = 0x6E, ["numdivide"] = 0x6F,
            // media
            ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["volumemute"] = 0xAD,
            ["medianext"] = 0xB0, ["mediaprev"] = 0xB1, ["mediastop"] = 0xB2, ["mediaplay"] = 0xB3,
        };
        for (int i = 1; i <= 24; i++) d[$"f{i}"] = (ushort)(0x6F + i);
        for (int i = 0; i <= 9; i++) { d[i.ToString()] = (ushort)(0x30 + i); d[$"num{i}"] = (ushort)(0x60 + i); }
        for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = (ushort)char.ToUpperInvariant(c);
        return d;
    }

    private static Dictionary<ushort, string> BuildReverseTable()
    {
        // Canonical names (first wins) for the capture dialog.
        var canonical = new[]
        {
            "ctrl", "shift", "alt", "win", "rctrl", "rshift", "ralt", "rwin",
            "enter", "tab", "esc", "space", "up", "down", "left", "right", "delete", "backspace",
            "insert", "home", "end", "pageup", "pagedown", "printscreen",
            "capslock", "numlock", "scrolllock", "pause", "menu",
            "semicolon", "equals", "comma", "minus", "period", "slash", "backtick",
            "lbracket", "backslash", "rbracket", "quote",
            "nummultiply", "numadd", "numsubtract", "numdecimal", "numdivide",
            "volumeup", "volumedown", "volumemute", "medianext", "mediaprev", "mediastop", "mediaplay",
        };
        var r = new Dictionary<ushort, string>();
        foreach (var n in canonical) r.TryAdd(NameToVk[n], n);
        for (int i = 1; i <= 24; i++) r[(ushort)(0x6F + i)] = $"f{i}";
        for (int i = 0; i <= 9; i++) { r[(ushort)(0x30 + i)] = i.ToString(); r[(ushort)(0x60 + i)] = $"num{i}"; }
        for (char c = 'a'; c <= 'z'; c++) r[(ushort)char.ToUpperInvariant(c)] = c.ToString();
        return r;
    }

    /// <summary>Resolve one key name ("f13", "a", ";") to a virtual-key code; 0 if unknown.</summary>
    public static ushort ResolveVk(string name)
    {
        name = name.Trim();
        if (NameToVk.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1)
        {
            short r = VkKeyScan(name[0]);
            byte b = (byte)(r & 0xFF);
            if (b != 0xFF) return b;
        }
        return 0;
    }

    /// <summary>Canonical name for a virtual-key code (used by the capture dialog); null if unsupported.</summary>
    public static string? NameForVk(int vk) => VkToName.TryGetValue((ushort)vk, out var n) ? n : null;

    public static bool IsModifierVk(int vk) => Modifiers.Contains((ushort)vk);

    /// <summary>Parse "ctrl+shift+f13" into modifier VKs + key VKs. Unknown parts are skipped.</summary>
    public static (List<ushort> mods, List<ushort> keys) Parse(string combo)
    {
        var mods = new List<ushort>();
        var keys = new List<ushort>();
        if (string.IsNullOrWhiteSpace(combo)) return (mods, keys);

        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // A trailing/standalone "+" key ("ctrl++") yields an empty part; treat as plus.
        if (combo.TrimEnd().EndsWith("++")) keys.Add(0xBB);

        foreach (var part in parts)
        {
            ushort vk = ResolveVk(part);
            if (vk == 0) continue;
            (Modifiers.Contains(vk) ? mods : keys).Add(vk);
        }
        return (mods, keys);
    }

    // ── Sequences ─────────────────────────────────────────────────────

    public const int MinDelayMs = 10;
    public const int MaxDelayMs = 60000;

    /// <summary>One step of a macro sequence: a combo to tap, or a pause (Combo == null).</summary>
    public readonly record struct MacroStep(string? Combo, int DelayMs)
    {
        public bool IsDelay => Combo == null;
        public override string ToString() => Combo ?? $"wait:{DelayMs}";
    }

    public static int ClampDelay(int ms) => Math.Clamp(ms, MinDelayMs, MaxDelayMs);

    /// <summary>
    /// Split a sequence ("ctrl+1 wait:500 2") into steps. Steps are whitespace-separated; a step is a
    /// combo or "wait:N" (ms, clamped 10..60000). A single combo with no spaces is a one-step sequence.
    /// </summary>
    public static List<MacroStep> ParseSequence(string? seq)
    {
        var steps = new List<MacroStep>();
        if (string.IsNullOrWhiteSpace(seq)) return steps;
        // Pre-1.4.5 macros were a single combo and spaces around '+' were allowed ("win + ctrl + v").
        // Glue those back into one combo so they aren't split into separate steps.
        seq = System.Text.RegularExpressions.Regex.Replace(seq, @"\s*\+\s*", "+");
        foreach (var tok in seq.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok.StartsWith("wait:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(tok.AsSpan(5), out int ms)) steps.Add(new MacroStep(null, ClampDelay(ms)));
                continue;
            }
            steps.Add(new MacroStep(tok.ToLowerInvariant(), 0));
        }
        return steps;
    }

    /// <summary>Join steps back into the stored sequence string.</summary>
    public static string FormatSequence(IEnumerable<MacroStep> steps) => string.Join(" ", steps);

    /// <summary>First combo step of a sequence (used by hold-while-pressed); "" if none.</summary>
    public static string FirstCombo(string? seq)
    {
        foreach (var s in ParseSequence(seq)) if (!s.IsDelay) return s.Combo!;
        return "";
    }

    private static int _sequenceRunning;

    public static bool IsSequenceRunning => Volatile.Read(ref _sequenceRunning) != 0;

    /// <summary>
    /// Run a sequence off the calling thread: taps each combo (chords pressed together) and waits for
    /// pauses. Returns false without running if another sequence is still in progress (no overlap).
    /// </summary>
    public static Task<bool> RunSequenceAsync(string seq, CancellationToken ct = default)
    {
        var steps = ParseSequence(seq);
        if (steps.Count == 0) return Task.FromResult(true);
        if (Interlocked.CompareExchange(ref _sequenceRunning, 1, 0) != 0) return Task.FromResult(false);

        return Task.Run(async () =>
        {
            try
            {
                foreach (var step in steps)
                {
                    if (ct.IsCancellationRequested) break;
                    if (step.IsDelay) await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
                    else Tap(step.Combo!);
                }
            }
            catch (OperationCanceledException) { }
            finally { Volatile.Write(ref _sequenceRunning, 0); }
            return true;
        });
    }

    // ── Sending ───────────────────────────────────────────────────────

    /// <summary>Press and release a combo (modifiers held around the keys).</summary>
    public static void Tap(string combo)
    {
        var (mods, keys) = Parse(combo);
        if (mods.Count == 0 && keys.Count == 0) return;

        foreach (var m in mods) { Send(m, false); Thread.Sleep(4); }
        // Press every key before releasing any, so "t+y" is a true chord (both down together).
        foreach (var k in keys) { Send(k, false); Thread.Sleep(4); }
        Thread.Sleep(12);
        for (int i = keys.Count - 1; i >= 0; i--) Send(keys[i], true);
        for (int i = mods.Count - 1; i >= 0; i--) Send(mods[i], true);
    }

    /// <summary>Press (and keep held) every key of a combo. Returns the VKs to pass to <see cref="Release"/>.</summary>
    public static List<ushort> Hold(string combo)
    {
        var (mods, keys) = Parse(combo);
        var all = new List<ushort>(mods);
        all.AddRange(keys);
        foreach (var vk in all) { Send(vk, false); Thread.Sleep(4); }
        return all;
    }

    /// <summary>Release keys pressed by <see cref="Hold"/> (reverse order).</summary>
    public static void Release(IReadOnlyList<ushort> vks)
    {
        for (int i = vks.Count - 1; i >= 0; i--) Send(vks[i], true);
    }

    private static void Send(ushort vk, bool keyUp)
    {
        uint scan = MapVirtualKey(vk, 0);
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (Extended.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // ── P/Invoke ──────────────────────────────────────────────────────

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);
}
