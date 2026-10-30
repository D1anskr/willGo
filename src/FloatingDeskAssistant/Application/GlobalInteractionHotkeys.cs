using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FloatingDeskAssistant.Application;

public sealed class GlobalInteractionHotkeys : IDisposable
{
    private const int ToggleId = 0x5743;
    private const int ScrollUpId = 0x5744;
    private const int ScrollDownId = 0x5745;
    private const int ScreenshotId = 0x5746;
    private const int PreviousQuestionId = 0x5747;
    private const int NextQuestionId = 0x5748;
    private readonly Action<int>? _selectQuestion;
    private readonly Action? _screenshot;
    private readonly HwndSource _source;
    private readonly Action _toggleMousePassthrough;
    private readonly Action<int> _scrollConversation;
    private readonly List<int> _registeredIds = new();
    private readonly List<string> _registrationErrors = new();
    private bool _disposed;
    private IntPtr _screenshotHook;
    private KeyboardHook? _hookCallback;
    private bool _screenshotPressed;
    public bool ScreenshotUsesKeyboardHook => _screenshotHook != IntPtr.Zero;

    public GlobalInteractionHotkeys(Action toggleMousePassthrough, Action<int> scrollConversation, Action? screenshot = null, Action<int>? selectQuestion = null)
    {
        _toggleMousePassthrough = toggleMousePassthrough;
        _scrollConversation = scrollConversation;
        _screenshot = screenshot;
        _selectQuestion = selectQuestion;
        _source = new HwndSource(new HwndSourceParameters("willGo interaction hotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        _source.AddHook(WindowProc);
        Register(ToggleId, 0x0002 | 0x4000, 0x31, "Ctrl+1");
        Register(ScrollUpId, 0x0002, 0x26, "Ctrl+Up");
        Register(ScrollDownId, 0x0002, 0x28, "Ctrl+Down");
    }

    public IReadOnlyList<string> RegistrationErrors => _registrationErrors;

    public bool SetQuestionNavigationEnabled(bool enabled)
    {
        if (_disposed) return false;
        foreach (var (id, key, label) in new[] { (PreviousQuestionId, 0x26u, "Ctrl+Shift+Up"), (NextQuestionId, 0x28u, "Ctrl+Shift+Down") })
        {
            if (!enabled)
            {
                if (_registeredIds.Remove(id)) UnregisterHotKey(_source.Handle, id);
            }
            else if (_selectQuestion is not null && !_registeredIds.Contains(id)) Register(id, 0x0002 | 0x0004, key, label);
        }
        return !enabled || _registeredIds.Contains(PreviousQuestionId) && _registeredIds.Contains(NextQuestionId);
    }

    public bool SetScreenshotEnabled(bool enabled)
    {
        if (_disposed) return false;
        if (!enabled)
        {
            if (_registeredIds.Remove(ScreenshotId)) UnregisterHotKey(_source.Handle, ScreenshotId);
            if (_screenshotHook != IntPtr.Zero) UnhookWindowsHookEx(_screenshotHook);
            _screenshotHook = IntPtr.Zero;
            _screenshotPressed = false;
            return true;
        }
        if (_registeredIds.Contains(ScreenshotId) || ScreenshotUsesKeyboardHook) return true;
        if (_screenshot is null) return false;
        if (RegisterHotKey(_source.Handle, ScreenshotId, 0x0002 | 0x0004 | 0x4000, 0x53))
        {
            _registeredIds.Add(ScreenshotId);
            return true;
        }
        // Some desktop tools reserve this combination. Intercept only this chord while battle mode is active.
        _hookCallback ??= ScreenshotKeyboardProc;
        _screenshotHook = SetWindowsHookEx(13, _hookCallback, GetModuleHandle(null), 0);
        if (ScreenshotUsesKeyboardHook) return true;
        _registrationErrors.Add($"Ctrl+Shift+S: Win32 error {Marshal.GetLastWin32Error()}. Screenshot shortcut is unavailable.");
        return false;
    }

    private IntPtr ScreenshotKeyboardProc(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed && Marshal.ReadInt32(data) == 0x53)
        {
            var keyUp = message.ToInt32() is 0x0101 or 0x0105;
            if (keyUp && _screenshotPressed) { _screenshotPressed = false; return new IntPtr(1); }
            if (!keyUp && IsKeyDown(0x11) && IsKeyDown(0x10) && !IsKeyDown(0x12) && !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
            {
                if (!_screenshotPressed)
                {
                    _screenshotPressed = true;
                    _source.Dispatcher.BeginInvoke(() => { if (!_disposed && ScreenshotUsesKeyboardHook) _screenshot?.Invoke(); });
                }
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(_screenshotHook, code, message, data);
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Register(int id, uint modifiers, uint key, string label)
    {
        if (RegisterHotKey(_source.Handle, id, modifiers, key))
        {
            _registeredIds.Add(id);
        }
        else
        {
            _registrationErrors.Add($"{label}: Win32 error {Marshal.GetLastWin32Error()}. The shortcut may be used by another app.");
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312 || !_registeredIds.Contains(wParam.ToInt32()))
        {
            return IntPtr.Zero;
        }

        handled = true;
        switch (wParam.ToInt32())
        {
            case ToggleId:
                _toggleMousePassthrough();
                break;
            case ScrollUpId:
                _scrollConversation(-1);
                break;
            case ScrollDownId:
                _scrollConversation(1);
                break;
            case ScreenshotId:
                _screenshot?.Invoke();
                break;
            case PreviousQuestionId:
                _selectQuestion?.Invoke(-1);
                break;
            case NextQuestionId:
                _selectQuestion?.Invoke(1);
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetScreenshotEnabled(false);
        _disposed = true;
        foreach (var id in _registeredIds) UnregisterHotKey(_source.Handle, id);
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
