using System.Diagnostics;
using System.Runtime.InteropServices;
using GdTracker.ViewModels;

namespace GdTracker.App.Services;

/// <summary>
/// Глобальный монитор ввода на низкоуровневых хуках WinAPI (WH_KEYBOARD_LL/WH_MOUSE_LL).
/// Ставится на UI-потоке (нужен цикл сообщений), события поднимаются на нём же.
/// </summary>
public sealed class GlobalInputMonitor : IGlobalInputMonitor, IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int VK_UP = 0x26;
    private const int VK_SPACE = 0x20;
    private const int VK_W = 0x57;
    private const int VK_ESCAPE = 0x1B;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private HookProc? _keyboardProc; // держим ссылки, иначе делегаты соберёт GC
    private HookProc? _mouseProc;
    private readonly HashSet<int> _down = new();

    public event Action? Pressed;
    public event Action? StopRequested;

    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        _down.Clear();
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var hMod = GetModuleHandle(module.ModuleName);

        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _keyboardProc = null;
        _mouseProc = null;
        _down.Clear();
    }

    public void Dispose() => Stop();

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode — первое поле
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (vk == VK_ESCAPE)
                    StopRequested?.Invoke();
                else if ((vk == VK_UP || vk == VK_SPACE || vk == VK_W) && _down.Add(vk))
                    Pressed?.Invoke(); // только переход «отпущена → нажата»
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                _down.Remove(vk);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
            Pressed?.Invoke();
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
