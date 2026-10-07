using System.Runtime.InteropServices;
using Pixel.App.Cat;
using Pixel.Core.Input;

namespace Pixel.App.Input;

/// <summary>
/// Low-level keyboard and mouse hooks on a dedicated thread with its own message loop, so slow UI work can
/// never make Windows drop the hook. Callbacks only classify events and hand them off.
/// Falls back to RegisterHotKey (no left/right Shift distinction) if the hooks cannot be installed.
/// </summary>
internal sealed class HookThread(HotkeyDetector detector, Action<HotkeyAction> onHotkey, Action<InputEvent> onInput) : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const int WM_MOUSEWHEEL = 0x20A, WM_MOUSEHWHEEL = 0x20E, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;
    private const int WM_HOTKEY = 0x312, WM_QUIT = 0x12;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr MaskMarker = new(0x50495845); // "PIXE": tags our own injected key

    private readonly HashSet<int> _held = [];
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook, _mouseHook;
    private HookProc? _keyboardProc, _mouseProc; // keep delegates alive while hooked

    /// <summary>True when the low-level hooks are active (false = RegisterHotKey fallback).</summary>
    public bool HooksActive { get; private set; }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Pixel input hooks" };
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        HooksActive = _keyboardHook != IntPtr.Zero;
        if (!HooksActive)
        {
            RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x2E);
            RegisterHotKey(IntPtr.Zero, 2, MOD_CONTROL | MOD_WIN | MOD_NOREPEAT, 0x20);
        }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY) onHotkey(msg.wParam == 1 ? HotkeyAction.ToggleVanish : HotkeyAction.ToggleChat);
        }

        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        UnregisterHotKey(IntPtr.Zero, 1);
        UnregisterHotKey(IntPtr.Zero, 2);
    }

    private IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (info.dwExtraInfo == MaskMarker) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        var msg = (int)wParam;
        var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (!down && msg is not (WM_KEYUP or WM_SYSKEYUP)) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        var vk = (int)info.vkCode;

        if (down && vk is VK.Space or VK.Delete)
            detector.ResyncModifiers(m => (GetAsyncKeyState(m) & 0x8000) != 0); // key-ups can be lost to elevated windows
        var decision = detector.OnKey(vk, down);

        var injected = (info.flags & LLKHF_INJECTED) != 0;
        var isRepeat = down && !_held.Add(vk);
        if (!down) _held.Remove(vk);
        if (down && !isRepeat && !injected && !IsModifier(vk)) onInput(new InputEvent(InputKind.Key, CatController.Now));

        if (decision.InjectMask) SendMaskKey();
        if (decision.Action is { } action) onHotkey(action);
        return decision.Swallow ? 1 : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            switch ((int)wParam)
            {
                case WM_MOUSEWHEEL or WM_MOUSEHWHEEL:
                    onInput(new InputEvent(InputKind.Scroll, CatController.Now));
                    break;
                case WM_LBUTTONDOWN or WM_RBUTTONDOWN:
                    onInput(new InputEvent(InputKind.Pointer, CatController.Now));
                    break;
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool IsModifier(int vk) =>
        vk is VK.LControl or VK.RControl or VK.LShift or VK.RShift or VK.LWin or VK.RWin or 0x12 or 0xA4 or 0xA5;

    /// <summary>Taps an unassigned key so Windows sees "another key" between Win down and up (no Start menu).</summary>
    private static void SendMaskKey()
    {
        var inputs = new INPUT[2];
        for (var i = 0; i < 2; i++)
        {
            inputs[i].type = 1; // keyboard
            inputs[i].ki.wVk = VK.MaskKey;
            inputs[i].ki.dwFlags = i == 0 ? 0u : 2u; // KEYEVENTF_KEYUP
            inputs[i].ki.dwExtraInfo = MaskMarker;
        }
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    // ---------------------------------------------------------------- interop

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] private MOUSEINPUT _mouseSizer; // union must be as large as MOUSEINPUT
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
