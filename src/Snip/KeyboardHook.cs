using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Snip;

// Windows' shell owns Win+Shift+S, so RegisterHotKey fails for it. A low-level hook sees the
// keystroke first and can swallow it before the shell's hotkey handler runs.
// The hook lives on its own thread: if the UI thread is busy painting or saving, Windows would
// time the callback out, let the shell see Win+Shift+S, and eventually drop the hook.
sealed class KeyboardHook
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    // An unassigned virtual key. Tapping it between Win down and Win up stops the Start menu opening.
    const ushort VK_UNASSIGNED = 0xE8;

    readonly Native.LowLevelKeyboardProc proc;
    IntPtr handle;
    Keys swallowKeyUp = Keys.None;
    bool printScreenHeld;

    // Win+Shift+<key> shortcuts. Each action runs on the hook callback, so it must only post work.
    public readonly Dictionary<Keys, Action> WinShiftChords = new();

    // Null lets Print Screen through to Windows. May be swapped while the hook runs.
    public volatile Action PrintScreen;

    // Called with each key-down while it returns true for keys it consumed. Runs on the hook
    // callback, which Windows abandons after ~1s, so handlers must only post work.
    public Func<Keys, bool> KeyDownFilter;

    // Configure WinShiftChords and KeyDownFilter before calling Start.
    public KeyboardHook()
    {
        proc = Callback;
    }

    public void Start()
    {
        Exception failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            handle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero) failure = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (failure != null) return;
            using var refresh = new System.Windows.Forms.Timer { Interval = 30_000 };
            refresh.Tick += (_, _) => Reinstall();
            refresh.Start();
            Application.Run();
        }) { IsBackground = true, Name = "Keyboard hook" };
        thread.Start();
        ready.Wait();
        if (failure != null) throw failure;
    }

    // Windows removes a hook without telling it if a callback is ever too slow, which a long
    // garbage collection can cause. Installing a fresh one every 30 s caps the outage.
    void Reinstall()
    {
        var fresh = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(handle);
        handle = fresh;
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var key = (Keys)kb.vkCode;
            bool down = wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN;

            if (WinShiftChords.TryGetValue(key, out var chord))
            {
                // A held key repeats its key-down; only the first press counts. A press without
                // Win+Shift means the key-up was missed, so it is a fresh key.
                if (down && swallowKeyUp == key)
                {
                    if (IsWinShiftOnly()) return (IntPtr)1;
                    swallowKeyUp = Keys.None;
                }
                if (down && IsWinShiftOnly())
                {
                    swallowKeyUp = key;
                    Native.SendKeys(VK_UNASSIGNED);
                    chord();
                    return (IntPtr)1;
                }
                if (!down && swallowKeyUp == key)
                {
                    swallowKeyUp = Keys.None;
                    return (IntPtr)1;
                }
            }

            // Plain Print Screen only: Win+PrtSc and Alt+PrtSc keep their Windows meaning.
            if (key == Keys.PrintScreen && PrintScreen is { } printScreen && NoModifiers())
            {
                if (down && !printScreenHeld) printScreen();
                printScreenHeld = down;
                return (IntPtr)1;
            }

            if (down && KeyDownFilter != null && KeyDownFilter(key)) return (IntPtr)1;
        }
        return Native.CallNextHookEx(handle, nCode, wParam, lParam);
    }

    static bool IsWinShiftOnly() =>
        (Native.IsDown(VK_LWIN) || Native.IsDown(VK_RWIN))
        && Native.IsDown(VK_SHIFT)
        && !Native.IsDown(VK_CONTROL)
        && !Native.IsDown(VK_MENU);

    static bool NoModifiers() =>
        !Native.IsDown(VK_LWIN) && !Native.IsDown(VK_RWIN) && !Native.IsDown(VK_SHIFT)
        && !Native.IsDown(VK_CONTROL) && !Native.IsDown(VK_MENU);
}
