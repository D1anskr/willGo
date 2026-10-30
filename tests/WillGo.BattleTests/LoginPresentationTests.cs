using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

internal static class LoginPresentationTests
{
    public static int Run()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                WindowCaptureProtection.Initialize(new QuietLogger());
                var vm = new LoginViewModel(new LoginTests.FakeValidation(), new LoginTests.FixedIp(), new QuietLogger());
                var window = new LoginWindow(vm);
                window.Show(); await Task.Delay(120);
                GetWindowRect(new WindowInteropHelper(window).Handle, out var native);
                Check(native.Right - native.Left >= 300 && native.Bottom - native.Top >= 300,
                    $"native login window has a usable on-screen size ({native.Right - native.Left}x{native.Bottom - native.Top})");
                var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
                Check(native.Left >= area.Left && native.Top >= area.Top && native.Right <= area.Right && native.Bottom <= area.Bottom,
                    "native login bounds stay inside the selected monitor work area");
                var scroll = (ScrollViewer)window.FindName("LoginScroll");
                Check(window.ActualHeight <= SystemParameters.WorkArea.Height && scroll.ScrollableWidth == 0,
                    "default login fits the work area without horizontal overflow");
                var image = (System.Windows.Controls.Image)window.FindName("LoginArtwork");
                Check(image.Source is DrawingImage { Drawing.Bounds.Width: > 0 } && image.ActualWidth > 200 && image.ActualHeight > 90,
                    "bundled artwork resolves and occupies its intended drawing area");
                Check(((Button)window.FindName("LoginButton")).IsDefault && ((TextBox)window.FindName("UserIdTextBox")).IsKeyboardFocusWithin,
                    "login retains initial account focus and Enter submission");
                Save(window, "login-welcome.png");
                window.SizeToContent = SizeToContent.Manual; window.Width = 360; window.Height = 430;
                vm.UserId = "short"; vm.LoginCommand.Execute(null); await Task.Delay(80);
                var button = (Button)window.FindName("LoginButton");
                button.BringIntoView(); await Task.Delay(80);
                var bounds = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
                Check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth == 0 && bounds.Top >= 0 && bounds.Bottom <= scroll.ViewportHeight + 1,
                    "short narrow screens can scroll the login action fully into view without horizontal overflow");
                var status = (TextBlock)window.FindName("LoginStatusText");
                Check(status.ActualHeight >= 21 && status.Text.Contains("10") && status.TextWrapping == TextWrapping.Wrap,
                    "validation errors retain a readable wrapping area above the login action");
                Save(window, "login-welcome-small.png");
                window.Close();
                Console.WriteLine("PASS: 7 login presentation checks");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
            finally { app.Shutdown(); }
        };
        app.Run(); return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Save(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }
    private sealed class QuietLogger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
}
