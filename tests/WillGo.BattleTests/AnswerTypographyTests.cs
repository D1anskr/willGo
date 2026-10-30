using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Controls;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

internal static class AnswerTypographyTests
{
    private const string Answer = """
        Redis 是一个基于 **内存的高性能 Key-Value 存储系统**。它的核心特点是 **快**，单机 QPS 能到 10 万级别，常用于缓存和实时数据处理。

        Redis 不只是简单的缓存，它提供的数据结构很丰富，常见的有 **String、Hash、List、Set、ZSet**，还有 Bitmap、HyperLogLog、GEO、Stream。

        ## 实际项目中怎么用？

        从商品中台的经验来说，Redis 主要承担的是 **多级缓存里的第二层**。本地缓存做一级，Redis 做二级，然后才是 MySQL。

        - **缓存一致性**：先更新 DB 再删除缓存，配合 binlog 订阅做兜底。
        - **热点防护**：热点探测 + 本地缓存预热，避免单个 Key 打爆 Redis 分片。
        - **缓存穿透、击穿、雪崩**：使用布隆过滤器、空值缓存、互斥锁重建和随机过期时间。

        ### 回答时抓住这三个重点

        1. **内存模型**：为什么快，以及适合存什么。
        2. **持久化机制（RDB / AOF）**：如何在性能和可靠性之间取舍。
        3. **集群方案（主从、哨兵、Cluster）**：如何扩容和保证可用性。

        > Redis 更像一个高性能的数据结构服务器，需要结合业务理解它的取舍。

        使用 `SET lock:order token NX EX 10` 获取锁，释放时必须检查 token，避免误删别人的锁。

        ```java
        public boolean acquireLock(String key, String token) {
            return Boolean.TRUE.equals(redis.setIfAbsent(key, token));
        }
        ```

        | 方案 | 核心特点 | 适用场景 |
        | --- | --- | --- |
        | RDB | 定时生成数据快照 | 备份与快速恢复 |
        | AOF | 追加写命令日志 | 更高的数据可靠性 |
        """;

    public static async Task RunAsync(BattleWindow window, BattleViewModel vm, AppConfig config, Action<bool, string> check)
    {
        config.Battle.AnswerFontSize = 18;
        var savedOpacity = config.WindowOpacity;
        config.WindowOpacity = 1;
        window.UpdateConfig(config);
        var question = new BattleEntry
        {
            Source = FloatingDeskAssistant.Application.SpeechSource.Speaker,
            Text = "什么是 Redis？\n请结合高并发项目详细说明缓存一致性、热点防护和持久化方案，以及项目中的实际取舍。",
            Answer = Answer, Status = "回答完成"
        };
        vm.Entries.Insert(0, question); vm.Selected = question;
        window.Width = 1120; window.Height = 760;
        await Task.Delay(120);
        var presenter = (AnswerContentPresenter)window.FindName("AnswerContent");
        var scroll = (ScrollViewer)window.FindName("AnswerScroll");
        var textBlocks = Visuals<TextBlock>(presenter).ToArray();
        var runs = textBlocks.SelectMany(t => Runs(t.Inlines)).ToArray();
        check(((SolidColorBrush)window.Background).Color == Colors.White && textBlocks.Where(t => t.FontSize == 18).All(t => Contrast(t.Foreground, Colors.White) > 4.5),
            "answer window is opaque white with readable dark text and list markers");
        check(runs.Where(r => r.FontWeight == FontWeights.Bold).All(r => Contrast(r.Foreground, Colors.White) > 7),
            "bold answer highlights retain strong contrast on white");
        check(Visuals<Border>(presenter).Any(b => b.Background is SolidColorBrush brush && brush.Color.R >= 240 && brush.Color.G >= 240 && brush.Color.B >= 240),
            "code blocks and table headers use light surfaces");
        var hwnd = new WindowInteropHelper(window).Handle;
        foreach (var opacity in new[] { 1d, .55, .3, 1d })
        {
            config.WindowOpacity = opacity; window.UpdateConfig(config);
            check(GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && alpha == (byte)Math.Round(opacity * 255) && (flags & 2) != 0 &&
                GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == 0x11 && ((SolidColorBrush)window.Background).Color == Colors.White,
                $"native opacity {opacity:P0} keeps the white surface and capture exclusion");
        }
        check(presenter.ContentFontSize == 18 && textBlocks.Any(t => t.FontSize == 18 && t.LineHeight == 31.5), "answers use an 18px body with 1.75 line height");
        check(runs.Any(r => r.Text == "内存的高性能 Key-Value 存储系统" && r.FontWeight == FontWeights.Bold), "Markdown strong emphasis renders as bold text without literal markers");
        check(!runs.Any(r => r.Text.Contains("**")) && textBlocks.Any(t => t.FontSize == 22), "heading hierarchy and emphasis render instead of showing Markdown source");
        check(runs.Any(r => r.Text.Contains("SET lock:order") && r.FontFamily.Source.Contains("Consolas")), "inline commands use a distinct monospace font");
        check(Visuals<CodeBlockView>(presenter).Any(block => block.Editor.Text.Contains("    return Boolean.TRUE") && block.Editor.FontFamily.Source.Contains("Consolas") && block.Editor.SyntaxHighlighting is not null),
            "fenced Java retains indentation and renders with real syntax highlighting");
        check(textBlocks.Any(t => t.Text == "1.") && textBlocks.Any(t => t.Text == "\u2022"), "ordered and unordered list markers render separately from paragraph text");
        check(textBlocks.Any(t => t.Inlines.Count > 0 && new TextRange(t.ContentStart, t.ContentEnd).Text == "RDB"), "Markdown comparison tables retain cell content");
        check(!question.Title.Contains('\n') && question.Text.Contains('\n'), "sidebar title removes line breaks while retaining the complete original question");
        var list = (ListBox)window.FindName("QuestionList");
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(question);
        var title = Visuals<TextBlock>(item).First(t => t.Text == question.Title);
        check(item.ActualHeight == 58 && title.TextWrapping == TextWrapping.NoWrap && title.TextTrimming == TextTrimming.CharacterEllipsis && title.ActualHeight == 20,
            "long multiline questions occupy a fixed compact row and a single ellipsized title line");
        check(!window.ShowInTaskbar, "answer workspace has no taskbar entry");
        SaveImage(window, "answer-typography-1120.png");
        var whitePixels = CountWhitePixels(window);
        check(whitePixels > 0.35, "rendered answer window contains a white reading surface rather than a dark or blank frame");

        double changed = 0;
        void CaptureSize(double size) => changed = size;
        window.AnswerFontSizeChanged += CaptureSize;
        ((Button)window.FindName("IncreaseAnswerFontButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(changed == 19 && presenter.ContentFontSize == 19 && window.FontSize == 13, "font toolbar adjusts answer size without enlarging window controls");
        window.AnswerFontSizeChanged -= CaptureSize;
        config.Battle.AnswerFontSize = 28; window.UpdateConfig(config);
        window.Width = 760; window.Height = 480;
        await Task.Delay(120);
        check(!((Button)window.FindName("IncreaseAnswerFontButton")).IsEnabled && presenter.ContentFontSize == 28,
            "maximum font size disables further enlargement");
        SaveImage(window, "answer-typography-760-large.png");
        check(scroll.ViewportWidth > 250 && scroll.ViewportHeight > 45 && ((TextBox)window.FindName("ManualInput")).ActualWidth > 180,
            $"large answers remain scrollable with input accessible in the smallest supported window ({scroll.ViewportWidth:0}x{scroll.ViewportHeight:0})");
        window.ScrollConversation(1); await Task.Delay(50);
        check(scroll.VerticalOffset > 0, "global answer scrolling still works after Markdown rendering and enlargement");

        config.Battle.AnswerFontSize = 18; window.UpdateConfig(config);
        window.Width = 1120; window.Height = 760;
        presenter.Text = "**嵌套的 `Kafka` 重点**，转义 \\*符号\\*，普通的 (RDB/AOF) 描述不会被误判为代码。\n\n![仅显示替代文字](https://invalid.example/image.png)\n\n- **未闭合重点\n- 第二项";
        await Task.Delay(60);
        runs = Visuals<TextBlock>(presenter).SelectMany(t => Runs(t.Inlines)).ToArray();
        check(runs.Any(r => r.Text == "Kafka" && r.FontWeight == FontWeights.Bold) && string.Concat(runs.Select(r => r.Text)).Contains("*符号*"), "nested bold code and escaped Markdown preserve content");
        check(Visuals<System.Windows.Controls.Image>(presenter).Count() == 0 && runs.Any(r => r.Text.Contains("仅显示替代文字")), "model image links display text without loading external media");
        check(runs.Any(r => r.Text.Contains("第二项")), "incomplete Markdown still displays subsequent content");
        presenter.Text = Answer;
        scroll.ScrollToBottom(); await Task.Delay(80);
        SaveImage(window, "answer-typography-code-table.png");
        presenter.ClearValue(AnswerContentPresenter.TextProperty);
        System.Windows.Data.BindingOperations.SetBinding(presenter, AnswerContentPresenter.TextProperty, new System.Windows.Data.Binding("Selected.Answer"));
        scroll.ScrollToTop();
        check(new BattleConfig { AnswerFontSize = double.NaN }.Clone().AnswerFontSize == 18 && new BattleConfig { AnswerFontSize = 99 }.Clone().AnswerFontSize == 28,
            "invalid font settings normalize to supported readable sizes");
        var phone = new RemoteCaptureInfoWindow(["http://127.0.0.1:18080/"], "Test") { Owner = window };
        phone.Show(); await Task.Delay(80);
        check(((SolidColorBrush)phone.Background).Color == Colors.White && Contrast(((TextBox)phone.FindName("PrimaryUrlTextBox")).Foreground, Colors.White) > 7,
            "phone QR dialog uses white surfaces and dark URL text");
        SaveImage(phone, "light-theme-phone.png");
        phone.Close();
        config.WindowOpacity = savedOpacity; window.UpdateConfig(config);
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            if (inline is Span span) foreach (var child in Runs(span.Inlines)) yield return child;
        }
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Visuals<T>(child)) yield return descendant;
        }
    }

    private static void SaveImage(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(output);
    }

    private static double Contrast(System.Windows.Media.Brush brush, System.Windows.Media.Color background)
    {
        static double Luminance(System.Windows.Media.Color color)
        {
            static double Channel(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var foreground = Luminance(((SolidColorBrush)brush).Color);
        var backdrop = Luminance(background);
        return (Math.Max(foreground, backdrop) + .05) / (Math.Min(foreground, backdrop) + .05);
    }

    private static double CountWhitePixels(Window window)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index] == 255 && pixels[index + 1] == 255 && pixels[index + 2] == 255 && pixels[index + 3] == 255) count++;
        return count / (double)(bitmap.PixelWidth * bitmap.PixelHeight);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint color, out byte alpha, out uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
