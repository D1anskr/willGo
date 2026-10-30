using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ToolTip = System.Windows.Controls.ToolTip;

namespace FloatingDeskAssistant.Application;

public static class WindowInteraction
{
    private const long PassthroughStyles = 0x00000020 | 0x08000000; // Transparent + no activate.
    private static readonly ConditionalWeakTable<Window, InputState> States = new();
    private static readonly List<WeakReference<FrameworkElement>> OpenPopups = new();

    public static bool IsClickThrough { get; private set; }
    public static event Action<bool>? Changed;

    public static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
    }

    public static void Attach(Window window)
    {
        var state = States.GetOrCreateValue(window);
        window.SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x007D && wParam.ToInt64() == -20 && !state.UpdatePending)
                {
                    var style = GetWindowLongPtr(hwnd, -20).ToInt64();
                    var desired = IsClickThrough ? PassthroughStyles : state.OriginalStyles;
                    if ((style & PassthroughStyles) != desired)
                    {
                        state.UpdatePending = true;
                        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                        {
                            state.UpdatePending = false;
                            Apply(window);
                        }));
                    }
                }
                return IntPtr.Zero;
            });
            Apply(window);
        };
        window.Loaded += (_, _) => Apply(window);
        window.ContentRendered += (_, _) => Apply(window);
    }

    public static void SetClickThrough(bool enabled)
    {
        if (IsClickThrough == enabled) return;
        IsClickThrough = enabled;
        if (enabled)
        {
            Mouse.Capture(null);
            Keyboard.ClearFocus();
            foreach (var reference in OpenPopups.ToArray())
            {
                if (reference.TryGetTarget(out var popup)) ClosePopup(popup);
            }
            OpenPopups.Clear();
        }

        if (System.Windows.Application.Current is { } app)
        {
            foreach (Window window in app.Windows) Apply(window);
        }
        foreach (System.Windows.Forms.Form form in System.Windows.Forms.Application.OpenForms)
        {
            if (form.IsHandleCreated) ApplyToNative(form.Handle);
        }
        Changed?.Invoke(enabled);
    }

    private static void Apply(Window window)
    {
        var state = States.GetOrCreateValue(window);
        if (!state.OriginalHitTestVisible.HasValue) state.OriginalHitTestVisible = window.IsHitTestVisible;
        window.SetCurrentValue(UIElement.IsHitTestVisibleProperty, IsClickThrough ? false : state.OriginalHitTestVisible.Value);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        if (!state.StyleCaptured)
        {
            state.OriginalStyles = style & PassthroughStyles;
            state.StyleCaptured = true;
        }
        var targetStyle = (style & ~PassthroughStyles) | (IsClickThrough ? PassthroughStyles : state.OriginalStyles);
        if (targetStyle != style) SetWindowLongPtr(hwnd, -20, new IntPtr(targetStyle));
        if (IsClickThrough)
        {
            // Native layered alpha makes transparent hit testing work across
            // processes. The capture helper defers it until WPF's first frame.
            WindowCaptureProtection.EnsureNativeOpacity(window);
        }
    }

    public static void ApplyToNative(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        var target = IsClickThrough ? style | PassthroughStyles : style & ~PassthroughStyles;
        if (target != style) SetWindowLongPtr(hwnd, -20, new IntPtr(target));
    }

    private static void OnPopupOpened(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement popup) return;
        if (IsClickThrough) ClosePopup(popup);
        else
        {
            OpenPopups.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!OpenPopups.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, popup)))
                OpenPopups.Add(new WeakReference<FrameworkElement>(popup));
        }
    }

    private static void ClosePopup(FrameworkElement popup)
    {
        if (popup is ContextMenu menu) menu.IsOpen = false;
        if (popup is ToolTip tooltip) tooltip.IsOpen = false;
    }

    private sealed class InputState
    {
        public bool? OriginalHitTestVisible;
        public bool StyleCaptured;
        public long OriginalStyles;
        public bool UpdatePending;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
