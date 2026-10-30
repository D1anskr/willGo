using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Windows;

internal static class NativeWindowFrameTests
{
    public static void Run(Window window, Action<bool, string> check, Action pump)
    {
        var config = AppConfig.CreateDefault(); config.WindowOpacity = 1;
        if (window is BattleWindow battle) battle.UpdateConfig(config);
        if (window is BattleHistoryWindow history) history.UpdateConfig(config);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 100; window.Top = 100; window.Width = 860; window.Height = 600;
        var originalOwner = window.Owner;
        var backdrop = new Window { Left = 80, Top = 80, Width = 900, Height = 640, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.Lime, Topmost = true, ShowInTaskbar = false };
        backdrop.Show();
        window.Owner = backdrop;
        WindowCaptureProtection.SetEnabled(false);
        window.Show(); window.Activate(); pump(); pump();
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var frame);
        GetClientRect(hwnd, out var client);
        var origin = new NativePoint(); ClientToScreen(hwnd, ref origin);
        Console.WriteLine($"FRAME: outer={frame.Right-frame.Left}x{frame.Bottom-frame.Top}, client={client.Right}x{client.Bottom}, offset={origin.X-frame.Left},{origin.Y-frame.Top}");
        using var bitmap = new Bitmap(frame.Right - frame.Left, frame.Bottom - frame.Top);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(frame.Left, frame.Top, 0, 0, bitmap.Size);
        var imageName = window switch { SettingsWindow => "native-settings-frame.png", BattleHistoryWindow => "native-history-frame.png", _ => "native-window-frame.png" };
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, imageName), ImageFormat.Png);
        check(origin.X == frame.Left && origin.Y == frame.Top && client.Right == bitmap.Width && client.Bottom == bitmap.Height,
            window.GetType().Name + " content occupies the complete native frame without a residual rectangular resize border");
        foreach (var point in new[] { (0,0), (bitmap.Width-1,0), (0,bitmap.Height-1), (bitmap.Width-1,bitmap.Height-1) })
        {
            var pixel = bitmap.GetPixel(point.Item1, point.Item2);
            var region = CreateRectRgn(0,0,0,0); GetWindowRgn(hwnd, region);
            Console.WriteLine($"CORNER: {point}, rgb={pixel}, inRegion={PtInRegion(region,point.Item1,point.Item2)}");
            DeleteObject(region);
            check(pixel.G > 220 && pixel.R < 30 && pixel.B < 30, $"native outer corner {point} reveals the green backdrop");
        }
        foreach (var point in new[] { (bitmap.Width/2,2), (2,30), (bitmap.Width-3,30), (bitmap.Width/2,bitmap.Height-3) })
        {
            var pixel = bitmap.GetPixel(point.Item1, point.Item2);
            check(pixel.R > 210 && pixel.G > 210 && pixel.B > 210, $"native edge {point} has a light surface without a black frame");
        }
        var cornerRegion = CreateRectRgn(0, 0, 0, 0); GetWindowRgn(hwnd, cornerRegion);
        try
        {
            var radius = (int)Math.Round(16 * System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX);
            foreach (var corner in new[] { (false,false), (true,false), (false,true), (true,true) })
            {
                var clean = true;
                for (var x = 0; x < radius; x++) for (var y = 0; y < radius; y++)
                {
                    var px = corner.Item1 ? bitmap.Width - 1 - x : x;
                    var py = corner.Item2 ? bitmap.Height - 1 - y : y;
                    var color = bitmap.GetPixel(px, py);
                    clean &= PtInRegion(cornerRegion, px, py)
                        ? color.R > 170 && color.G > 170 && color.B > 170
                        : color.G > 220 && color.R < 30 && color.B < 30;
                }
                check(clean, $"complete native corner {corner} has a clean light arc and no black wedge");
            }
        }
        finally { DeleteObject(cornerRegion); }
        foreach (var edge in new[] { (frame.Left+1,(frame.Top+frame.Bottom)/2,10), (frame.Right-2,(frame.Top+frame.Bottom)/2,11),
            ((frame.Left+frame.Right)/2,frame.Top+1,12), ((frame.Left+frame.Right)/2,frame.Bottom-2,15) })
        {
            var position = new IntPtr((edge.Item2 << 16) | (edge.Item1 & 0xffff));
            check(SendMessage(hwnd, 0x0084, IntPtr.Zero, position).ToInt32() == edge.Item3, $"custom native frame retains resize hit target {edge.Item3}");
        }
        window.Hide();
        window.Owner = originalOwner;
        backdrop.Close();
        WindowCaptureProtection.SetEnabled(true);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
