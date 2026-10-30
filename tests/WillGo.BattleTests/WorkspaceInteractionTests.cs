using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

internal static class WorkspaceInteractionTests
{
    public static async Task RunAsync(BattleWindow window, BattleViewModel vm, AppConfig config, Action<bool, string> check)
    {
        window.Width = 1120; window.Height = 760;
        config.Battle.QuestionPaneWidth = 210;
        config.WindowOpacity = 1;
        window.UpdateConfig(config);
        await Task.Delay(100);
        var hwnd = new WindowInteropHelper(window).Handle;
        check(window.Title == "willGo" && ((System.Windows.Controls.TextBlock)window.FindName("AppTitle")).Text == "willGo" &&
            vm.RecordingButtonText == "开始战斗", "workspace title is willGo and idle capture button says start battle");
        bool HasRoundedRegion()
        {
            var region = CreateRectRgn(0, 0, 0, 0);
            try { return GetWindowRgn(hwnd, region) > 0 && !PtInRegion(region, 0, 0) && PtInRegion(region, 40, 40); }
            finally { DeleteObject(region); }
        }
        check(HasRoundedRegion() && !window.AllowsTransparency, "native window excludes square corners without per-pixel transparency");
        config.WindowOpacity = .55; window.UpdateConfig(config);
        WindowInteraction.SetClickThrough(true);
        window.Width += 10;
        await Task.Delay(80);
        check(HasRoundedRegion() && !window.IsHitTestVisible && GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == 0x11,
            "rounded window survives resize, partial opacity and mouse passthrough with capture exclusion");
        WindowInteraction.SetClickThrough(false);
        config.WindowOpacity = 1; window.UpdateConfig(config);

        var column = (ColumnDefinition)window.FindName("QuestionColumn");
        var answerColumn = (ColumnDefinition)window.FindName("AnswerColumn");
        var splitter = (GridSplitter)window.FindName("QuestionSplitter");
        var input = (TextBox)window.FindName("ManualInput");
        var send = (Button)window.FindName("SendButton");
        var saved = new List<double>();
        void CaptureWidth(double value) => saved.Add(value);
        window.QuestionPaneWidthChanged += CaptureWidth;
        void Drag(double amount, bool cancel = false)
        {
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(amount, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            window.UpdateLayout();
            if (cancel) splitter.CancelDrag();
            splitter.RaiseEvent(new DragCompletedEventArgs(amount, 0, cancel) { RoutedEvent = Thumb.DragCompletedEvent });
            window.UpdateLayout();
        }
        Drag(70);
        check(Math.Abs(column.ActualWidth - 280) <= 1 && saved.Count == 1 && Math.Abs(saved[0] - 280) <= 1,
            $"dragging divider grows the question pane and emits one saved width (width={column.ActualWidth}, saved={string.Join(',', saved)})");
        Drag(-60);
        check(Math.Abs(column.ActualWidth - 220) <= 1 && answerColumn.ActualWidth >= 380,
            "dragging divider left restores space to the answer");
        var previous = column.ActualWidth; var saves = saved.Count;
        Drag(50, true);
        check(Math.Abs(column.ActualWidth - previous) <= 1 && saved.Count == saves,
            "cancelled divider drag restores preferred width without saving");
        Drag(1000);
        check(column.ActualWidth <= 360 && answerColumn.ActualWidth >= 380, "divider respects maximum sidebar and minimum answer widths");
        window.Width = 760; window.Height = 480;
        await Task.Delay(100);
        check(answerColumn.ActualWidth >= 380 && column.ActualWidth >= 150 && input.ActualWidth > 180,
            "narrow window clamps sidebar to keep the answer and composer usable");
        window.Width = 1120; window.Height = 760;
        await Task.Delay(80);
        check(Math.Abs(column.ActualWidth - 360) <= 1, "expanding the window restores the preferred divider position");
        Drag(-1000);
        check(Math.Abs(column.ActualWidth - 150) <= 1, "divider respects minimum question pane width");
        var source = PresentationSource.FromVisual(input)!;
        var arrow = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent };
        typeof(GridSplitter).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(splitter, [arrow]);
        window.UpdateLayout();
        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyUpEvent });
        check(Math.Abs(column.ActualWidth - 166) <= 1 && saved.Last() == 166, "focused divider supports keyboard resizing and saves its position");
        window.QuestionPaneWidthChanged -= CaptureWidth;
        config.Battle.QuestionPaneWidth = 248;
        window.UpdateConfig(config); window.UpdateLayout();
        check(Math.Abs(column.ActualWidth - 248) <= 1 && new BattleConfig { QuestionPaneWidth = double.NaN }.Clone().QuestionPaneWidth == 210 &&
            new BattleConfig { QuestionPaneWidth = 900 }.Clone().QuestionPaneWidth == 360,
            "saved divider position reloads and invalid widths normalize");

        window.Activate(); input.Focus();
        await Task.Delay(30);
        KeyEventArgs Press(Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            input.RaiseEvent(args);
            return args;
        }
        var count = vm.Entries.Count;
        input.Text = "Kafka 与 Redis 的区别？";
        var enter = Press(Key.Return);
        check(enter.Handled && vm.Entries.Count == count + 1 && vm.Selected!.Text == "Kafka 与 Redis 的区别？" && input.Text == "",
            "Enter submits exactly one original question and clears the composer");
        check(!send.IsEnabled && !vm.SendCommand.CanExecute(null), "empty composer disables the send button");
        Press(Key.Return);
        check(vm.Entries.Count == count + 1, "empty Enter does not create a question");
        var decision = typeof(BattleWindow).GetMethod("ShouldSubmitComposerKey", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Submits(Key key, ModifierKeys modifiers, bool composing) => (bool)decision.Invoke(null, [key, modifiers, composing])!;
        check(!Submits(Key.Return, ModifierKeys.Shift, false) && input.AcceptsReturn && !Submits(Key.Return, ModifierKeys.Control, false),
            "Shift+Enter stays with the multiline editor instead of submitting");
        input.Text = "中文输入法候选内容";
        var composition = new TextComposition(InputManager.Current, input, "候选");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        check(!Press(Key.Return).Handled && vm.Entries.Count == count + 1,
            "Enter while IME composition is active does not submit a question");
        check(!Press(Key.ImeProcessed).Handled && vm.Entries.Count == count + 1,
            "IME candidate confirmation key does not send prematurely");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Press(Key.Return);
        check(vm.Entries.Count == count + 2 && vm.Selected!.Text == "中文输入法候选内容",
            "Enter submits normally after IME composition finishes");
        input.Text = "第一行\n第二行";
        input.Select(4, 3); input.SelectedText = "替换";
        check(input.Text.Contains("替换") && input.Template.FindName("PART_ContentHost", input) is ScrollViewer,
            "rounded text editor retains multiline editing and selection");
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(send);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (vm.Entries.Count == count + 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        check(vm.Entries.Count == count + 3 && vm.Selected!.Text.Contains("替换") && input.Text == "",
            "send button command remains functional alongside keyboard sending");
        while (vm.Entries.Any(e => e.Busy)) await Task.Delay(20);
        input.Text = "";
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
