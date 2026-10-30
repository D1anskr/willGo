using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Contains("--backdrop"))
        {
            var backdrop = new Window { Title = "willGo smoke test backdrop", Left = 0, Top = 0,
                Width = 1700, Height = 1000, WindowStyle = WindowStyle.None, Background = Brushes.Lime };
            var expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            expiry.Tick += (_, _) => app.Shutdown();
            expiry.Start();
            backdrop.Show();
            Console.WriteLine("READY");
            app.Run();
            return 0;
        }

        Process? background = null;
        try
        {
            var logger = new TestLogger();
            WindowCaptureProtection.Initialize(logger);
            WindowCaptureProtection.SetEnabled(true);
            TestHotkeys();
            background = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--backdrop")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
            var ready = background.StandardOutput.ReadLineAsync();
            Check(ready.Wait(TimeSpan.FromSeconds(5)) && ready.Result == "READY", "separate process backdrop ready");
            var config = AppConfig.CreateDefault();
            config.CompactChatMode = true;
            config.CompactModeLevel = CompactModeLevel.Mode3;
            config.BallOpacity = 1;
            config.WindowOpacity = 0.3;
            var service = new AppConfigService(new ApiKeyProtector(), logger);
            typeof(AppConfigService).GetField("_filePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(service, System.IO.Path.Combine(AppContext.BaseDirectory, "smoke-settings.json"));
            var vm = new MainViewModel(config, new NoNetworkRouter(), new NoScreenshotService(), service, logger);
            vm.Messages.Add(MessageItemViewModel.Create(ChatRole.Assistant, "Answer for mouse testing", null));
            var ball = new BallWindow(vm);
            var chat = new ChatWindow(vm);
            var battleVm = new BattleViewModel(new BattleSpeechService(), new NoNetworkRouter(), () => config, Dispatcher.CurrentDispatcher);
            var battle = new BattleWindow(battleVm, config);
            var windows = new Window[] { ball, chat, new LatestMessageColumnWindow(),
                battle,
                new SettingsWindow(config, AppConfig.CreateDefault()),
                new RemoteCaptureInfoWindow(new[] { "http://127.0.0.1:18080/" }, "Verification"),
                new BattleHistoryWindow(() => System.IO.Path.Combine(AppContext.BaseDirectory, "empty-native-history"), config) };
            foreach (var window in windows)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Show();
                window.Left = 100;
                window.Top = 100;
                Pump();
                var hwnd = new WindowInteropHelper(window).Handle;
                var owner = window.Owner;
                Check((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x40080) == 0x80,
                    window.GetType().Name + " is explicitly a native tool window without an app-window flag");
                var previousStyle = GetWindowLongPtr(hwnd, -20).ToInt64();
                SetWindowLongPtr(hwnd, -20, new IntPtr((previousStyle & ~0x80L) | 0x40000));
                Pump();
                Check((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x40080) == 0x80,
                    window.GetType().Name + " native style replacement preserves tool-window identity");
                window.Activate(); Pump();
                var point = window.PointToScreen(new Point(32, 32));
                Check(window.ActualWidth >= window.Width - 1 && window.ActualHeight < 1200,
                    window.GetType().Name + " retains its configured width and usable automatic height");
                Check(GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2) == hwnd,
                    window.GetType().Name + " receives native mouse hits in interactive mode");
                WindowInteraction.SetClickThrough(true);
                Pump();
                Check((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x08080020) == 0x08080020 && !window.IsHitTestVisible,
                    window.GetType().Name + " layered passthrough and no-activate flags set");
                Check(!window.ShowInTaskbar && (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x40080) == 0x80,
                    window.GetType().Name + " stays a tool window outside the taskbar in passthrough mode");
                var target = GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2);
                GetWindowThreadProcessId(target, out var hitProcess);
                Check(hitProcess == background.Id, window.GetType().Name + " passes mouse hit testing to another process");
                Check(GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == 0x11,
                    window.GetType().Name + " retains capture exclusion while locked");
                window.Width += 4;
                Pump();
                Check((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x08080020) == 0x08080020,
                    window.GetType().Name + " resize preserves passthrough flags");
                WindowInteraction.SetClickThrough(false);
                Pump();
                Check((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x20) == 0 && window.IsHitTestVisible,
                    window.GetType().Name + " toggles back to interactive");
                window.Hide(); window.Show(); Pump();
                Check(window.IsVisible && ReferenceEquals(window.Owner, owner) &&
                    (GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & 0x40080) == 0x80,
                    window.GetType().Name + " hide/show preserves tool-window identity and original ownership");
                window.Hide();
            }
            Check(!ball.ShowInTaskbar && ball.Title == "willGo", "legacy ball also stays out of taskbar");
            NativeWindowFrameTests.Run(battle, Check, Pump);
            var settingsFrame = new SettingsWindow(config, AppConfig.CreateDefault());
            NativeWindowFrameTests.Run(settingsFrame, Check, Pump);
            settingsFrame.Close();
            var historyFrame = new BattleHistoryWindow(() => System.IO.Path.Combine(AppContext.BaseDirectory, "empty-native-history"), config);
            NativeWindowFrameTests.Run(historyFrame, Check, Pump);
            historyFrame.Close();
            Check((GetWindowLongPtr(new WindowInteropHelper(ball).Handle, -20).ToInt64() & 0x40000) == 0,
                "interactive ball does not force a taskbar entry");
            var border = (Border)ball.FindName("BallBorder");
            Check(((SolidColorBrush)border.Background).Color == System.Windows.Media.Color.FromRgb(41, 41, 41), "interactive ball is grey-black");
            WindowInteraction.SetClickThrough(true);
            Check(((SolidColorBrush)border.Background).Color == Colors.White, "passthrough ball is white");
            var futureWindow = new SettingsWindow(config, AppConfig.CreateDefault()) { Left = 100, Top = 100 };
            futureWindow.Show();
            Pump();
            Check((GetWindowLongPtr(new WindowInteropHelper(futureWindow).Handle, -20).ToInt64() & 0x08080020) == 0x08080020,
                "window opened while locked inherits passthrough after first frame");
            futureWindow.Hide();
            ball.Show();
            Pump();
            vm.AdjustOpacity(-0.05);
            Pump();
            var ballHandle = new WindowInteropHelper(ball).Handle;
            Check((GetWindowLongPtr(ballHandle, -20).ToInt64() & 0x080C00A0) == 0x080800A0,
                "opacity hotkeys preserve white mode input and tool-window flags");
            Check(GetWindowDisplayAffinity(ballHandle, out var ballAffinity) && ballAffinity == 0x11,
                "opacity change preserves capture exclusion in white mode");
            var menu = new ContextMenu { PlacementTarget = ball };
            menu.Items.Add(new MenuItem { Header = "Smoke test" });
            menu.IsOpen = true;
            Pump();
            Check(!menu.IsOpen, "locked windows cannot leave an interactive popup open");
            using (var overlay = new ScreenshotOverlayForm())
            {
                Check((GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x08080020) == 0x08080020,
                    "screenshot overlay inherits passthrough");
            }
            ball.Hide();
            TestScrolling(config, service, logger);
            WindowInteraction.SetClickThrough(false);
            Check(((SolidColorBrush)border.Background).Color == System.Windows.Media.Color.FromRgb(41, 41, 41), "second toggle restores grey-black ball");
            Check(logger.Warnings.Count == 0, "no rendering or capture warnings");
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} Windows interaction checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (background is { HasExited: false }) background.Kill();
            background?.Dispose();
        }
    }

    private static void TestHotkeys()
    {
        var toggles = 0;
        var directions = new List<int>();
        using (var keys = new GlobalInteractionHotkeys(() => toggles++, directions.Add))
        {
            Check(keys.RegistrationErrors.Count == 0, "three interaction hotkeys register");
            var source = (HwndSource)typeof(GlobalInteractionHotkeys).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keys)!;
            foreach (var id in new[] { 0x5743, 0x5743, 0x5744, 0x5745 }) PostMessage(source.Handle, 0x0312, new IntPtr(id), IntPtr.Zero);
            Pump();
            Check(toggles == 2 && directions.SequenceEqual(new[] { -1, 1 }), "toggle twice and scroll directions dispatch correctly");
            using var duplicate = new GlobalInteractionHotkeys(() => { }, _ => { });
            Check(duplicate.RegistrationErrors.Count == 3, "conflicts report each occupied shortcut");
        }
        using var blocker = new HwndSource(new HwndSourceParameters("hotkey blocker") { ParentWindow = new IntPtr(-3) });
        Check(RegisterHotKey(blocker.Handle, 0x6710, 2, 0x26), "released Ctrl+Up can be registered again");
        using (var partial = new GlobalInteractionHotkeys(() => toggles++, directions.Add))
        {
            Check(partial.RegistrationErrors.Count == 1, "one conflict leaves other shortcuts available");
            var source = (HwndSource)typeof(GlobalInteractionHotkeys).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(partial)!;
            PostMessage(source.Handle, 0x0312, new IntPtr(0x5743), IntPtr.Zero);
            Pump();
            Check(toggles == 3, "toggle works when a scroll shortcut is occupied");
        }
        UnregisterHotKey(blocker.Handle, 0x6710);
        var screenshots = 0;
        using var captureKeys = new GlobalInteractionHotkeys(() => { }, _ => { }, () => screenshots++);
        Check(captureKeys.SetScreenshotEnabled(true), "battle screenshot shortcut Ctrl+Shift+S registers");
        var captureSource = (HwndSource)typeof(GlobalInteractionHotkeys).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(captureKeys)!;
        if (captureKeys.ScreenshotUsesKeyboardHook) SendScreenshotKeys();
        else { PostMessage(captureSource.Handle, 0x0312, new IntPtr(0x5746), IntPtr.Zero); Pump(); }
        Check(screenshots == 1, "global screenshot shortcut dispatches to battle capture");
        captureKeys.SetScreenshotEnabled(false);
        Check(!captureKeys.ScreenshotUsesKeyboardHook, "leaving battle releases screenshot keyboard hook");
        var reserved = RegisterHotKey(blocker.Handle, 0x6711, 2 | 4, 0x53);
        Check(captureKeys.SetScreenshotEnabled(true) && captureKeys.ScreenshotUsesKeyboardHook, "occupied screenshot shortcut uses battle-only keyboard hook");
        SendScreenshotKeys();
        Check(screenshots == 2, "occupied global screenshot combination still dispatches exactly once");
        if (reserved) UnregisterHotKey(blocker.Handle, 0x6711);
        var opacityDeltas = new List<double>();
        using (var opacity = new GlobalOpacityHotkeys(opacityDeltas.Add))
        {
            SendScreenshotKeys(0x27);
            SendScreenshotKeys(0x25);
            Check(opacityDeltas.SequenceEqual(new[] { .05, -.05 }), $"right increases opacity and left decreases opacity (hook={opacity.UsesKeyboardHook}, deltas={string.Join(',', opacityDeltas)})");
            var navigation = new List<int>();
            using var keys = new GlobalInteractionHotkeys(() => { }, _ => { }, selectQuestion: navigation.Add);
            Check(keys.SetQuestionNavigationEnabled(true), "title navigation registers Ctrl+Shift+Up and Down alongside opacity shortcuts");
            SendScreenshotKeys(0x26);
            SendScreenshotKeys(0x28);
            Check(navigation.SequenceEqual(new[] { -1, 1 }) && opacityDeltas.Count == 2, "title shortcuts dispatch previous and next without changing opacity");
            keys.SetQuestionNavigationEnabled(false);
            Check(RegisterHotKey(blocker.Handle, 0x6714, 2 | 4, 0x26) && RegisterHotKey(blocker.Handle, 0x6715, 2 | 4, 0x28), "leaving battle releases both title navigation shortcuts");
            UnregisterHotKey(blocker.Handle, 0x6714); UnregisterHotKey(blocker.Handle, 0x6715);
        }
    }

    private static void SendScreenshotKeys(byte key = 0x53)
    {
        var extended = key is >= 0x25 and <= 0x28 ? 1u : 0u;
        try
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x10, 0, 0, UIntPtr.Zero); Pump();
            keybd_event(key, 0, extended, UIntPtr.Zero); Pump();
        }
        finally
        {
            keybd_event(key, 0, extended | 2, UIntPtr.Zero); keybd_event(0x10, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero); Pump();
        }
    }

    private static void TestScrolling(AppConfig config, AppConfigService service, TestLogger logger)
    {
        var vm = new MainViewModel(config, new NoNetworkRouter(), new NoScreenshotService(), service, logger);
        var chat = new ChatWindow(vm) { Left = 100, Top = 100 };
        vm.Messages.Add(MessageItemViewModel.Create(ChatRole.Assistant,
            string.Join("\n", Enumerable.Range(0, 400).Select(i => $"Answer line {i}: scrolling smoke test.")), null));
        chat.Show();
        Pump();
        Pump();
        var columns = (List<LatestMessageColumnWindow>)typeof(ChatWindow)
            .GetField("_latestColumnWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chat)!;
        var overflow = columns.Where(column => column.IsVisible)
            .Select(column => (ScrollViewer)column.FindName("SegmentScrollViewer")).Last();
        Check(overflow.ScrollableHeight > 0, "long latest answer exposes scrollable overflow column");
        chat.ScrollConversation(1);
        Pump();
        Check(overflow.VerticalOffset > 0, "global down scroll moves latest answer while click-through");
        chat.ScrollConversation(-1);
        Pump();
        Check(overflow.VerticalOffset == 0, "global up scroll restores latest answer offset");
        overflow.ScrollToBottom();
        Pump();
        var bottom = overflow.VerticalOffset;
        chat.ScrollConversation(1);
        Pump();
        Check(overflow.VerticalOffset == bottom, "answer scroll clamps at bottom");
        chat.Hide();
        Pump();
        var hiddenOffset = overflow.VerticalOffset;
        chat.ScrollConversation(-1);
        Pump();
        Check(overflow.VerticalOffset == hiddenOffset, "hidden conversation ignores scroll hotkeys");

        var fullConfig = config.Clone();
        fullConfig.CompactChatMode = false;
        var fullVm = new MainViewModel(fullConfig, new NoNetworkRouter(), new NoScreenshotService(), service, logger);
        fullVm.Messages.Add(MessageItemViewModel.Create(ChatRole.User, string.Join("\n", Enumerable.Repeat("Previous question", 60)), null));
        fullVm.Messages.Add(MessageItemViewModel.Create(ChatRole.Assistant, "Short current answer", null));
        var fullChat = new ChatWindow(fullVm) { Left = 100, Top = 100 };
        fullChat.Show();
        Pump();
        Pump();
        var history = (ScrollViewer)fullChat.FindName("HistoryScrollViewer");
        Check(history.ScrollableHeight > 0, "question and answer history has a scrollable viewport");
        fullChat.ScrollConversation(1);
        Pump();
        Check(history.VerticalOffset > 0, "global down scroll falls back to history for short answers");
        fullChat.ScrollConversation(-1);
        Pump();
        Check(history.VerticalOffset == 0, "history scroll reverses without changing focus");
        fullChat.Hide();
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("FAIL: " + message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        DwmFlush();
    }

    private sealed class TestLogger : ILoggerService
    {
        public List<string> Warnings { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) { Warnings.Add(message); Console.WriteLine("WARN: " + message); }
        public void Error(string message, Exception? ex = null) => throw new InvalidOperationException(message, ex);
    }

    private sealed class NoNetworkRouter : IModelRouter
    {
        public Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network requests allowed in smoke tests.");
    }

    private sealed class NoScreenshotService : IScreenshotService
    {
        public Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult<ScreenshotCaptureResult?>(null);
        public Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken cancellationToken) => CaptureAsync(cancellationToken);
        public Task<ScreenshotCaptureResult?> CaptureRegionAsync(System.Drawing.Rectangle region, CancellationToken cancellationToken) => CaptureAsync(cancellationToken);
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
