using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CafeSoftware.Services;

public sealed class KeyboardBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_CONTROL = 0x11;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int LLKHF_ALTDOWN = 0x20;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly HookProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    public bool Enabled { get; set; }

    public KeyboardBlocker() => _proc = Callback;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var p = Process.GetCurrentProcess();
        using var m = p.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(m.ModuleName), 0);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool alt = (k.flags & LLKHF_ALTDOWN) != 0;
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            bool block =
                k.vkCode == VK_LWIN || k.vkCode == VK_RWIN ||
                (alt && k.vkCode == VK_TAB) ||
                (alt && k.vkCode == VK_ESCAPE) ||
                (ctrl && k.vkCode == VK_ESCAPE);

            if (block) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}