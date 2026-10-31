using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Controls;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using TabControl = System.Windows.Controls.TabControl;

internal static class FusionDisplayTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(!AppConfig.CreateDefault().DualMachine.IsFusionDisplay && !JsonSerializer.Deserialize<AppConfig>("{}")!.DualMachine.FusionDisplayEnabled,
            "fusion: defaults and legacy settings do not unexpectedly cover the desktop");
        var config = AppConfig.CreateDefault();
        config.WindowOpacity = .65;
        config.DualMachine = new() { Enabled = true, Role = DualMachineRole.Display, FusionDisplayEnabled = true };
        check(config.EffectiveWindowOpacity == 1 && config.DualMachine.FusionAnswerFontSize == 22, "fusion: independent defaults use opaque surfaces and 22px answers");
        var clone = config.Clone(); clone.DualMachine.FusionStyle = FusionStyle.Amber;
        check(config.DualMachine.FusionStyle == FusionStyle.Dark && clone.DualMachine.IsFusionDisplay, "fusion: display options clone independently");
        var invalid = new DualMachineConfig { FusionOpacity = double.NaN, FusionAnswerFontSize = double.PositiveInfinity, FusionStyle = (FusionStyle)99, FusionBackdrop = (FusionBackdrop)99 }.Clone();
        check(invalid.FusionOpacity == 1 && invalid.FusionAnswerFontSize == 22 && invalid.FusionStyle == FusionStyle.Dark && invalid.FusionBackdrop == FusionBackdrop.Black,
            "fusion: invalid palette, backdrop, opacity and typography normalize safely");
        var directory = Path.Combine(AppContext.BaseDirectory, "fusion-test"); Directory.CreateDirectory(directory);
        var persistence = new AppConfigService(new ApiKeyProtector(), new Logger());
        typeof(AppConfigService).GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(persistence, Path.Combine(directory, "settings.json"));
        clone.DualMachine.FusionBackdrop = FusionBackdrop.White; clone.DualMachine.FusionOpacity = .95; clone.DualMachine.FusionAnswerFontSize = 24;
        await persistence.SaveAsync(clone);
        var loaded = await persistence.LoadAsync();
        check(loaded.DualMachine.IsFusionDisplay && loaded.DualMachine.FusionStyle == FusionStyle.Amber && loaded.DualMachine.FusionBackdrop == FusionBackdrop.White &&
            loaded.EffectiveWindowOpacity == .95 && loaded.DualMachine.FusionAnswerFontSize == 24 && loaded.WindowOpacity == .65,
            "fusion: all display options round-trip through protected settings while standalone opacity is retained");
        var memory = new LoginTests.MemoryConfig();
        var main = new MainViewModel(config.Clone(), new Router(), null!, memory, new Logger());
        main.AdjustOpacity(-.05);
        await main.SetAnswerFontSizeAsync(24);
        var adjusted = main.GetConfigSnapshot();
        check(adjusted.DualMachine.FusionOpacity == .95 && adjusted.WindowOpacity == .65 && adjusted.Battle.AnswerFontSize == 18 && adjusted.DualMachine.FusionAnswerFontSize == 24,
            "fusion: shortcut opacity and font toolbar adjust only fusion settings");
        await main.DisableFusionDisplayAsync();
        check(!main.GetConfigSnapshot().DualMachine.IsFusionDisplay && memory.Saved!.EffectiveWindowOpacity == .65,
            "fusion: exit command persists disabled mode and restores standalone opacity");

        await using var vm = new BattleViewModel(new NoSpeech(), new Router(), () => config, Dispatcher.CurrentDispatcher);
        var entry = new BattleEntry { Text = "MySQL 为什么使用 B+ 树索引？", Source = SpeechSource.Speaker,
            Answer = "## 核心思路\n\n**B+ 树**把数据放在叶子节点，减少磁盘读取次数，适合范围查询。\n\n## 复杂度\n\n查找复杂度为 **O(log n)**。\n\n## 代码示例\n\n```java\nList<User> users = repository.findByAgeBetween(20, 30);\nfor (User user : users) {\n    System.out.println(user.getName());\n}\n```", Status = "已完成" };
        vm.Entries.Add(entry); vm.Selected = entry;
        var window = new BattleWindow(vm, config);
        try
        {
            window.Show(); await Task.Delay(120); window.UpdateLayout();
            var backdrop = window.Owner!;
            check(backdrop is { IsVisible: true, Topmost: true, ShowInTaskbar: false } && backdrop.Title == "willGo 融合器幕布",
                "fusion: display opens a tray-only topmost backdrop behind its owned workspace");
            GetWindowRect(new WindowInteropHelper(backdrop).Handle, out var bounds);
            var expected = System.Windows.Forms.SystemInformation.VirtualScreen;
            check(bounds.Left == expected.Left && bounds.Top == expected.Top && bounds.Right - bounds.Left == expected.Width && bounds.Bottom - bounds.Top == expected.Height,
                "fusion: backdrop covers the entire physical virtual desktop including taskbars");
            check(GetWindow(new WindowInteropHelper(window).Handle, 4) == new WindowInteropHelper(backdrop).Handle && ColorOf(backdrop.Background) == Colors.Black,
                "fusion: native ownership keeps willGo above an exact-black backdrop");
            check(!((Expander)window.FindName("TranscriptExpander")).IsExpanded, "fusion: transcript starts collapsed to preserve answer reading space");
            foreach (var style in Enum.GetValues<FusionStyle>())
            {
                config.DualMachine.FusionStyle = style; window.UpdateConfig(config); await Task.Delay(60); window.UpdateLayout();
                var content = (AnswerContentPresenter)window.FindName("AnswerContent");
                var text = Visuals<TextBlock>(content).FirstOrDefault(t => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text.Contains("核心思路"))
                    ?? throw new InvalidOperationException($"Fusion content missing: selected={vm.Selected?.Title}, text={content.Text}, blocks={Visuals<TextBlock>(content).Count()}");
                var background = ColorOf(window.Background);
                check(Contrast(ColorOf(text.Foreground), background) >= 7 && content.ContentFontSize == 22 && content.Palette == style,
                    "fusion: " + style + " heading and body retain strong contrast with enlarged typography");
                var code = Visuals<CodeBlockView>(content).Single();
                check(code.Editor.SyntaxHighlighting is null && Contrast(ColorOf(code.Editor.Foreground), ColorOf(code.Editor.Background)) >= 7 &&
                    code.Editor.WordWrap && code.Editor.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled && code.Editor.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                    "fusion: " + style + " code remains high-contrast, fully expanded and wrapped");
                var input = (TextBox)window.FindName("ManualInput");
                check(Contrast(ColorOf(input.Foreground), ColorOf(((Border)window.FindName("ComposerBorder")).Background)) >= 7 &&
                    Contrast(ColorOf(((Button)window.FindName("RecordingButton")).Foreground), ColorOf(((Button)window.FindName("RecordingButton")).Background)) >= 7,
                    "fusion: " + style + " input and capture controls follow the same readable palette");
                Save(window, Path.Combine(directory, "fusion-" + style.ToString().ToLowerInvariant() + ".png"));
            }
            config.DualMachine.FusionBackdrop = FusionBackdrop.White; window.UpdateConfig(config);
            check(ReferenceEquals(window.Owner, backdrop) && ColorOf(backdrop.Background) == Colors.White,
                "fusion: switching the hardware key color updates the existing backdrop without duplicating windows");
            using (var settings = new WindowScope(new SettingsWindow(config, AppConfig.CreateDefault()) { Owner = window }))
            {
                settings.Window.Show();
                ((TabControl)settings.Window.FindName("SettingsTabs")).SelectedItem = settings.Window.FindName("DualMachineTab");
                await Task.Delay(80);
                ((ComboBox)settings.Window.FindName("FusionStyleBox")).SelectedValue = FusionStyle.Dark;
                var edited = ((SettingsViewModel)settings.Window.DataContext).BuildUpdatedConfig();
                check(edited.DualMachine.FusionStyle == FusionStyle.Dark && config.DualMachine.FusionStyle == FusionStyle.Amber && settings.Window.Owner == window,
                    "fusion: owned settings edit a Chinese draft without changing active appearance");
            }
            WindowInteraction.SetClickThrough(true);
            check(!window.IsHitTestVisible && !backdrop.IsHitTestVisible, "fusion: mouse passthrough applies to both workspace and backdrop");
            WindowInteraction.SetClickThrough(false);
            window.Hide(); await Task.Delay(30);
            check(window.Owner is null && !backdrop.IsVisible && !Backdrops().Any(), "fusion: hide removes the backdrop and immediately restores the desktop");
            window.Show(); await Task.Delay(30);
            check(Backdrops().Count() == 1 && window.Owner is { IsVisible: true }, "fusion: tray restore recreates exactly one backdrop");
            window.WindowState = WindowState.Minimized; await Task.Delay(30);
            check(!Backdrops().Any(), "fusion: minimizing restores desktop access");
            window.WindowState = WindowState.Normal; await Task.Delay(30);
            check(Backdrops().Count() == 1, "fusion: restoring from minimize restores focus display");
            window.Width = 760; window.Height = 480; await Task.Delay(50); window.UpdateLayout();
            check(((Button)window.FindName("FusionExitButton")).IsVisible && ((TextBox)window.FindName("ManualInput")).ActualWidth > 80,
                "fusion: minimum window keeps the exit control and question composer accessible");
            Save(window, Path.Combine(directory, "fusion-minimum.png"));
            foreach (var role in new[] { DualMachineRole.Data, DualMachineRole.Display })
            {
                config.DualMachine.Role = role; config.DualMachine.Enabled = role == DualMachineRole.Data;
                window.UpdateConfig(config); await Task.Delay(30);
                check(!Backdrops().Any() && ColorOf(window.Background) == Colors.White && ((AnswerContentPresenter)window.FindName("AnswerContent")).Palette is null,
                    "fusion: data role or disabled dual mode restores standard appearance without a backdrop");
            }
            check(((Expander)window.FindName("TranscriptExpander")).IsExpanded, "fusion: leaving focus mode restores the previous transcript expansion");
            config.DualMachine.Enabled = true; window.UpdateConfig(config); await Task.Delay(30);
            check(Backdrops().Count() == 1, "fusion: re-entering display mode restores its saved fusion preference");
        }
        finally { WindowInteraction.SetClickThrough(false); window.Close(); }
        check(!Backdrops().Any(), "fusion: closing the workspace closes its backdrop without leaving a black screen");
    }

    private static IEnumerable<Window> Backdrops() => System.Windows.Application.Current.Windows.OfType<Window>().Where(w => w.Title == "willGo 融合器幕布");
    private static Color ColorOf(System.Windows.Media.Brush brush) => ((SolidColorBrush)brush).Color;
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double C(byte v) { var x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Save(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }
    private sealed class WindowScope(SettingsWindow window) : IDisposable { public SettingsWindow Window => window; public void Dispose() => window.Close(); }
    private sealed class Router : IModelRouter
    {
        public Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken token) => throw new InvalidOperationException("Fusion display must not call cloud APIs.");
    }
    private sealed class NoSpeech : IBattleSpeechService
    {
        public event Action<SpeechSegment>? Transcribed { add { } remove { } }
        public event Action<string>? Failed { add { } remove { } }
        public Task StartAsync(string microphoneId, string speakerId, CancellationToken token, bool? recordAudio = null) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Logger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) => throw new InvalidOperationException(message, ex);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
}
