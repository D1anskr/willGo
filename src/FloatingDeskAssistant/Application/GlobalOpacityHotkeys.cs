using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FloatingDeskAssistant.Application;

public sealed class GlobalOpacityHotkeys : IDisposable
{
    private const int IncreaseId = 0x5741;
    private const int DecreaseId = 0x5742;
    private const int WmHotkey = 0x0312;
    private const uint Modifiers = 0x0002 | 0x0004; // Control + Shift; allow held-key repeats.
    private readonly HwndSource _source;
    private readonly Action<double> _adjustOpacity;
    private bool _disposed;
    private IntPtr _keyboardHook;
    private KeyboardHook? _hookCallback;
    private readonly HashSet<int> _pressedKeys = new();
    public bool UsesKeyboardHook => _keyboardHook != IntPtr.Zero;

    public GlobalOpacityHotkeys(Action<double> adjustOpacity)
    {
        _adjustOpacity = adjustOpacity;
        _source = new HwndSource(new HwndSourceParameters("willGo opacity hotkeys")
        {
            ParentWindow = new IntPtr(-3), // Message-only window survives hiding the chat.
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        _source.AddHook(WindowProc);

        var increaseRegistered = RegisterHotKey(_source.Handle, IncreaseId, Modifiers, 0x27);
        var decreaseRegistered = RegisterHotKey(_source.Handle, DecreaseId, Modifiers, 0x25);
        if (!increaseRegistered || !decreaseRegistered)
        {
            if (increaseRegistered) UnregisterHotKey(_source.Handle, IncreaseId);
            if (decreaseRegistered) UnregisterHotKey(_source.Handle, DecreaseId);
            // Desktop tools can reserve these chords; intercept only the requested opacity keys.
            _hookCallback = KeyboardProc;
            _keyboardHook = SetWindowsHookEx(13, _hookCallback, GetModuleHandle(null), 0);
            if (!UsesKeyboardHook)
            {
                var error = Marshal.GetLastWin32Error();
                Dispose();
                throw new Win32Exception(error, "Could not enable Ctrl+Shift+Left/Right opacity shortcuts.");
            }
        }
    }

    private IntPtr KeyboardProc(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed)
        {
            var key = Marshal.ReadInt32(data);
            if (key is 0x25 or 0x27)
            {
                var keyUp = message.ToInt32() is 0x0101 or 0x0105;
                if (keyUp && _pressedKeys.Remove(key)) return new IntPtr(1);
                if (!keyUp && IsKeyDown(0x11) && IsKeyDown(0x10) && !IsKeyDown(0x12) && !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
                {
                    _pressedKeys.Add(key);
                    _source.Dispatcher.BeginInvoke(() => { if (!_disposed) _adjustOpacity(key == 0x27 ? 0.05 : -0.05); });
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && (wParam.ToInt32() == IncreaseId || wParam.ToInt32() == DecreaseId))
        {
            handled = true;
            _adjustOpacity(wParam.ToInt32() == IncreaseId ? 0.05 : -0.05);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _pressedKeys.Clear();
        UnregisterHotKey(_source.Handle, IncreaseId);
        UnregisterHotKey(_source.Handle, DecreaseId);
        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, KeyboardHook callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
