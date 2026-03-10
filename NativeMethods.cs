using System.Runtime.InteropServices;

namespace AmpUp;

internal static class NativeMethods
{
    // user32.dll — shared across AudioMixer, ButtonHandler, TrayApp
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    internal static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("user32.dll")]
    internal static extern short VkKeyScan(char ch);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // PowrProf.dll — replaces WinForms Application.SetSuspendState
    [DllImport("PowrProf.dll", SetLastError = true)]
    internal static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
