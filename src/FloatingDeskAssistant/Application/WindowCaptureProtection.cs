using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToolTip = System.Windows.Controls.ToolTip;

namespace FloatingDeskAssistant.Application;

public static class WindowCaptureProtection
{
    private const uint ExcludeFromCapture = 0x00000011;
    private const uint ToolWindowStyle = 0x00000080;
    private const uint AppWindowStyle = 0x00040000;
    private static ILoggerService? _logger;
    private static readonly ConditionalWeakTable<Window, OpacityState> OpacityStates = new();

    public static bool Enabled { get; private set; } = true;

    public static void Initialize(ILoggerService logger)
    {
        _logger = logger;
        WindowInteraction.Initialize();
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
            new RoutedEventHandler(OnPopupOpened));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            logger.Warn("Capture exclusion requires Windows 10 version 2004 or newer.");
        }
    }

    public static void Attach(Window window, double cornerRadius = 0, double inset = 0, bool roundOpaqueWindow = false)
    {
        var opacityState = OpacityStates.GetOrCreateValue(window);
        // WPF per-pixel layered windows reject display affinity (Win32 error 8).
        // Uniform window opacity and native rounded regions remain capture-compatible.
        var useWindowRegion = window.AllowsTransparency || roundOpaqueWindow;
        if (useWindowRegion)
        {
            window.AllowsTransparency = false;
        }

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                PreserveNativeWindowStyles(handle, message, wParam, lParam, ref handled);
                if (message == 0x007D && wParam.ToInt64() == -20 &&
                    (Marshal.PtrToStructure<StyleChange>(lParam).NewStyle & 0x00080000) == 0)
                {
                    ScheduleOpacityUpdate(window, opacityState);
                }
                return IntPtr.Zero;
            });
            if (useWindowRegion)
            {
                UpdateWindowRegion(window, cornerRadius, inset);
            }

            EnsureToolWindow(hwnd);
            Apply(hwnd);
        };
        window.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            EnsureToolWindow(hwnd);
            Apply(hwnd);
        };
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible) EnsureToolWindow(new WindowInteropHelper(window).Handle);
        };
        window.ContentRendered += (_, _) =>
        {
            opacityState.HasRendered = true;
            if (opacityState.Value is double opacity)
            {
                SetOpacity(window, opacity);
            }
        };
        if (useWindowRegion)
        {
            window.SizeChanged += (_, _) =>
            {
                UpdateWindowRegion(window, cornerRadius, inset);
                ScheduleOpacityUpdate(window, opacityState);
            };
            window.DpiChanged += (_, _) => UpdateWindowRegion(window, cornerRadius, inset);
        }
        WindowInteraction.Attach(window);
    }

    private static void ScheduleOpacityUpdate(Window window, OpacityState state)
    {
        if (!state.HasRendered || state.Value is null || state.UpdatePending)
        {
            return;
        }

        state.UpdatePending = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            state.UpdatePending = false;
            if (window.IsLoaded && window.IsVisible && state.Value is double opacity)
            {
                SetOpacity(window, opacity);
            }
        }));
    }

    private static void UpdateWindowRegion(Window window, double cornerRadius, double inset)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var left = (int)Math.Round(inset * dpi.DpiScaleX);
        var top = (int)Math.Round(inset * dpi.DpiScaleY);
        var region = CreateRoundRectRgn(left, top, bounds.Right - bounds.Left - left + 1,
            bounds.Bottom - bounds.Top - top + 1,
            (int)Math.Round(cornerRadius * 2 * dpi.DpiScaleX),
            (int)Math.Round(cornerRadius * 2 * dpi.DpiScaleY));
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
            _logger?.Warn("Could not set the window's rounded region.");
        }
    }

    private static void EnsureToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        var desired = (style | ToolWindowStyle) & ~(long)AppWindowStyle;
        if (style == desired) return;
        SetWindowLongPtr(hwnd, -20, new IntPtr(desired));
        if ((GetWindowLongPtr(hwnd, -20).ToInt64() & (ToolWindowStyle | AppWindowStyle)) != ToolWindowStyle)
        {
            _logger?.Warn("Could not apply tool-window visibility flags.");
        }
    }

    private static IntPtr PreserveNativeWindowStyles(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x007C && wParam.ToInt64() == -20)
        {
            var styles = Marshal.PtrToStructure<StyleChange>(lParam);
            // Keep tool-window identity when WPF updates opacity, taskbar or activation styles.
            styles.NewStyle = (styles.NewStyle | ToolWindowStyle) & ~AppWindowStyle;
            Marshal.StructureToPtr(styles, lParam, false);
            // HwndTarget otherwise strips WS_EX_LAYERED from non-per-pixel WPF windows.
            if ((styles.NewStyle & 0x00080000) != 0)
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == (uint)Environment.ProcessId)
            {
                Apply(hwnd);
            }

            return true;
        }, IntPtr.Zero);
    }

    public static bool Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!SetWindowDisplayAffinity(hwnd, Enabled ? ExcludeFromCapture : 0))
        {
            _logger?.Warn($"Could not set capture exclusion on window {hwnd}: Win32 error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        return true;
    }

    public static void EnsureNativeOpacity(Window window)
    {
        SetOpacity(window, OpacityStates.GetOrCreateValue(window).Value ?? 1.0);
    }

    public static void SetOpacity(Window window, double opacity)
    {
        var state = OpacityStates.GetOrCreateValue(window);
        state.Value = Math.Clamp(opacity, 0.0, 1.0);
        // WPF recreates the render target during Show. Native alpha set before
        // the first frame is lost and can leave capture exclusion showing black.
        if (!state.HasRendered)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const int extendedStyleIndex = -20;
        const long layeredStyle = 0x00080000;
        var style = GetWindowLongPtr(hwnd, extendedStyleIndex).ToInt64();
        SetWindowLongPtr(hwnd, extendedStyleIndex, new IntPtr(style | layeredStyle));
        var alpha = (byte)Math.Round(state.Value.Value * 255);
        if (!SetLayeredWindowAttributes(hwnd, 0, alpha, 0x00000002))
        {
            _logger?.Warn($"Could not set window opacity: Win32 error {Marshal.GetLastWin32Error()}.");
        }

        // Changing the layered composition mode invalidates DWM's existing
        // exclusion surface even when GetWindowDisplayAffinity still says 0x11.
        if (Enabled && !SetWindowDisplayAffinity(hwnd, 0))
        {
            _logger?.Warn($"Could not refresh capture exclusion: Win32 error {Marshal.GetLastWin32Error()}.");
        }
        Apply(hwnd);
    }

    private static void OnPopupOpened(object sender, RoutedEventArgs e)
    {
        if (WindowInteraction.IsClickThrough) return;
        // Context menus and tooltips create their own per-pixel layered HWNDs.
        if (sender is FrameworkElement { Parent: Popup { AllowsTransparency: true } popup })
        {
            popup.PopupAnimation = PopupAnimation.None;
            popup.Closed += ReopenPopup;
            popup.SetCurrentValue(Popup.IsOpenProperty, false);

            void ReopenPopup(object? popupSender, EventArgs args)
            {
                popup.Closed -= ReopenPopup;
                popup.AllowsTransparency = false;
                popup.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (WindowInteraction.IsClickThrough) return;
                    var isOpenProperty = sender is ContextMenu ? ContextMenu.IsOpenProperty : ToolTip.IsOpenProperty;
                    ((DependencyObject)sender).SetCurrentValue(isOpenProperty, true);
                    popup.SetCurrentValue(Popup.IsOpenProperty, true);
                    if (PresentationSource.FromVisual((Visual)sender) is HwndSource popupSource)
                    {
                        Apply(popupSource.Handle);
                    }
                }));
            }

            return;
        }

        if (sender is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source)
        {
            Apply(source.Handle);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    private sealed class OpacityState
    {
        public bool HasRendered;
        public bool UpdatePending;
        public double? Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StyleChange
    {
        public uint OldStyle;
        public uint NewStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect bounds);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
