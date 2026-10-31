using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using FloatingDeskAssistant.Configuration;
using Brushes = System.Windows.Media.Brushes;

namespace FloatingDeskAssistant.Application;

internal sealed class FusionDisplayController : IDisposable
{
    private readonly Window _workspace;
    private Window? _backdrop;
    private Window? _originalOwner;
    private DualMachineConfig _options = new();
    private bool _disposed;

    public FusionDisplayController(Window workspace)
    {
        _workspace = workspace;
        workspace.IsVisibleChanged += OnVisibilityChanged;
        workspace.StateChanged += OnStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }

    public void Update(DualMachineConfig options)
    {
        _options = options.Clone();
        Synchronize();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Synchronize();
    private void OnStateChanged(object? sender, EventArgs e) => Synchronize();
    private void OnDisplaysChanged(object? sender, EventArgs e) => _workspace.Dispatcher.BeginInvoke(() =>
    {
        if (!_disposed && _backdrop is not null) PositionBackdrop();
    });

    private void Synchronize()
    {
        if (_disposed) return;
        if (!_options.IsFusionDisplay || !_workspace.IsVisible || _workspace.WindowState == WindowState.Minimized)
        {
            CloseBackdrop();
            return;
        }
        if (_backdrop is null)
        {
            _originalOwner = _workspace.Owner;
            _backdrop = new Window
            {
                Title = "willGo 融合器幕布", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Focusable = false, Topmost = true,
                Width = 1, Height = 1, Left = -32000, Top = -32000,
                Background = _options.FusionBackdrop == FusionBackdrop.White ? Brushes.White : Brushes.Black
            };
            WindowCaptureProtection.Attach(_backdrop);
            _backdrop.SourceInitialized += (_, _) =>
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(_backdrop).Handle);
                source?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                    return IntPtr.Zero;
                });
            };
            _backdrop.Show();
            // The workspace and its dialogs remain above the full-screen backdrop.
            _workspace.Owner = _backdrop;
        }
        _backdrop.Background = _options.FusionBackdrop == FusionBackdrop.White ? Brushes.White : Brushes.Black;
        PositionBackdrop();
    }

    private void PositionBackdrop()
    {
        if (_backdrop is null) return;
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        // Physical pixels cover taskbars and negative-origin monitors at mixed DPI.
        if (!SetWindowPos(new WindowInteropHelper(_backdrop).Handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private void CloseBackdrop()
    {
        if (_backdrop is null) return;
        var backdrop = _backdrop;
        _backdrop = null;
        _workspace.Owner = _originalOwner;
        _originalOwner = null;
        backdrop.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.IsVisibleChanged -= OnVisibilityChanged;
        _workspace.StateChanged -= OnStateChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        CloseBackdrop();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
