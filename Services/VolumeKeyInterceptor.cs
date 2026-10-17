using System.Runtime.InteropServices;
using System.Threading;

namespace AmpUp.Services;

/// <summary>
/// Swallows the keyboard volume keys (Volume Up / Down / Mute) with a low-level
/// keyboard hook so Windows never shows its own volume flyout, and raises
/// <see cref="KeyPressed"/> so AmpUp can change the volume and show its OSD instead.
/// The hook runs on a dedicated thread with its own message loop: Windows calls
/// the hook for every keystroke system-wide, so it must never wait on a busy UI thread.
/// If AmpUp exits or crashes, Windows drops the hook and the keys behave normally again.
/// </summary>
public sealed class VolumeKeyInterceptor : IDisposable
{
    public enum VolumeKey { Up, Down, Mute }

    /// <summary>Raised on a thread-pool thread for each key down (including auto-repeat).</summary>
    public event Action<VolumeKey>? KeyPressed;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012;
    private const int VK_VOLUME_MUTE = 0xAD;
    private const int VK_VOLUME_DOWN = 0xAE;
    private const int VK_VOLUME_UP = 0xAF;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private HookProc? _proc; // held in a field so the GC can't collect it while hooked

    public bool IsRunning => _thread != null;

    public void Start()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim(); // not disposed: the thread may Set() after a Wait timeout
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                Logger.Log($"Volume key hook failed to install (error {Marshal.GetLastWin32Error()})");
            ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "VolumeKeyHook",
        };
        _thread.Start();
        ready.Wait(2000);
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread == null) return;
        _thread = null;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
    }

    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            VolumeKey? key = vk switch
            {
                VK_VOLUME_UP => VolumeKey.Up,
                VK_VOLUME_DOWN => VolumeKey.Down,
                VK_VOLUME_MUTE => VolumeKey.Mute,
                _ => null,
            };
            if (key is { } k)
            {
                int msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    // Never do audio work inside the hook — Windows times it out
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { KeyPressed?.Invoke(k); }
                        catch (Exception ex) { Logger.Log($"Volume key handler error: {ex.Message}"); }
                    });
                }
                return (IntPtr)1; // swallow both down and up so Windows never sees the key
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
