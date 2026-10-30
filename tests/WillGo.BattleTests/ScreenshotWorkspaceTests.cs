using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.UI.Controls;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;

internal static class ScreenshotWorkspaceTests
{
    private const string Question = """
        ## 11. 盛最多水的容器

        给定长度为 `n` 的非负整数数组 `height`。第 `i` 条垂线的端点为 `(i, 0)` 和 `(i, height[i])`。

        找出两条线，使它们与横轴构成的容器可以容纳最多的水。返回最大容量，**不能倾斜容器**。

        ### 示例

        - 输入：`height = [1,8,6,2,5,4,8,3,7]`
        - 输出：`49`
        - 解释：选择下标 1 和 8，高度为 7、宽度为 7。

        ### 约束

        - `2 <= n <= 100000`
        - `0 <= height[i] <= 10000`

        ### 代码模板

        ```java
        class Solution {
            public int maxArea(int[] height) {
            }
        }
        ```
        """;
    private const string Answer = """
        ## 解题思路

        使用**双指针**从两端向中间扫描。容器容量由较短的边决定：

        `面积 = min(height[left], height[right]) × (right - left)`

        每次记录当前面积，再移动较短的一侧。固定短边并缩小宽度不可能得到更大面积，因此可以排除这一侧。

        ## 复杂度分析

        - **时间复杂度：O(n)**，每个位置最多访问一次。
        - **空间复杂度：O(1)**，只使用常数个变量。

        ## 代码实现

        ```java
        class Solution {
            public int maxArea(int[] height) {
                int left = 0;
                int right = height.length - 1;
                int answer = 0;

                while (left < right) {
                    int width = right - left;
                    int shorter = Math.min(height[left], height[right]);
                    answer = Math.max(answer, width * shorter);

                    // 移动较短的一边，寻找更高的边界。
                    if (height[left] <= height[right]) {
                        left++;
                    } else {
                        right--;
                    }
                }
                return answer;
            }
        }
        ```
        """;

    public static async Task RunAsync(Action<bool, string> check)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { hasQuestion = true, title = "盛最多水的容器", questionMarkdown = Question });
        var parsed = ScreenshotQuestion.Parse("```json\n" + json + "\n```");
        check(parsed.Title == "盛最多水的容器" && parsed.Markdown == Question.Trim(), "structured screenshot extraction preserves examples, constraints, code and Markdown");
        check(ScreenshotQuestion.Parse("{\"questionMarkdown\":\"## 标题\\n题干\"}").Title == "标题", "missing model titles fall back to the first extracted heading");
        foreach (var invalid in new[] { "not json", "[]", "{}", "{\"hasQuestion\":false}", "{\"questionMarkdown\":null}" })
        {
            var rejected = false;
            try { ScreenshotQuestion.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid or empty screenshot extraction cannot generate an invented answer: " + invalid);
        }
        var config = AppConfig.CreateDefault(); config.WindowOpacity = 1;
        config.Primary.Model = "vision-primary"; config.Secondary.Model = "vision-fallback";
        var router = new ScriptedRouter();
        var extraction = router.Enqueue(); var answer = router.Enqueue();
        await using var vm = new BattleViewModel(new NoSpeech(), router, () => config, Dispatcher.CurrentDispatcher);
        vm.AddScreenshot(new() { ImageBytes = Image() });
        var entry = vm.Selected!;
        check(entry.Busy && !entry.HasExtractedQuestion && router.Requests.Count == 1 && router.Requests[0].Config.Primary.SystemPrompt == ScreenshotQuestion.ExtractionInstructions,
            "a screenshot first sends only a vision extraction request");
        var window = new BattleWindow(vm, config); window.Show(); await Task.Delay(80);
        extraction.SetResult(new() { Text = json });
        await Until(() => entry.HasExtractedQuestion && router.Requests.Count == 2);
        check(entry.Busy && entry.Answer == "" && entry.Title == "盛最多水的容器" && entry.Status.Contains("回答"),
            "the extracted problem and compact sidebar title appear before the answer completes");
        var solving = router.Requests[1];
        check(solving.Config.Primary.Model == "vision-primary" && solving.Config.Secondary.Model == "vision-fallback" &&
            solving.Config.Primary.SystemPrompt.Contains(ScreenshotQuestion.AnswerInstructions) && solving.Turns.Single().Text!.Contains(Question.Trim()) && solving.Turns.Single().ImageBytes is { Length: > 0 },
            "solving keeps the configured screenshot route and includes both extracted conditions and the original image");
        answer.SetResult(new() { Text = Answer }); await Until(() => !entry.Busy); await Task.Delay(100);
        var left = (ScrollViewer)window.FindName("ScreenshotQuestionScroll");
        var right = (ScrollViewer)window.FindName("AnswerScroll");
        var divider = (GridSplitter)window.FindName("ScreenshotSplitter");
        check(left.IsVisible && divider.IsVisible && left.ViewportWidth > 200 && right.ViewportWidth > 300 && right.ScrollableHeight > 0,
            "screenshot workspace shows independently scrollable question and answer columns");
        var code = Visuals<CodeBlockView>((AnswerContentPresenter)window.FindName("AnswerContent")).Single();
        check(code.Editor.IsReadOnly && code.Editor.ShowLineNumbers && code.Editor.WordWrap && code.Editor.SyntaxHighlighting?.Name == "Java" && code.Code.Contains("    public int maxArea"),
            "solution code uses a read-only highlighted Java editor with line numbers and preserved indentation");
        check(code.CopyButton.ToolTip?.ToString() == "复制代码" && code.Editor.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
            "each fenced code block provides copying and wraps instead of horizontal scrolling");
        var beforeLeft = left.VerticalOffset;
        window.ScrollConversation(1); await Task.Delay(40);
        check(right.VerticalOffset > 0 && left.VerticalOffset == beforeLeft, "global answer scrolling leaves the extracted question position unchanged");
        right.ScrollToTop(); Save(window, "screenshot-workspace.png");
        right.ScrollToVerticalOffset(code.TransformToAncestor((Visual)right.Content).Transform(new System.Windows.Point()).Y);
        await Task.Delay(50); Save(window, "screenshot-workspace-code.png");
        entry.Answer = "```java\n" + string.Join("\n", Enumerable.Range(1, 100).Select(i => $"    int value{i} = {i};")) + "\n```";
        await Task.Delay(80);
        var expanded = Visuals<CodeBlockView>((AnswerContentPresenter)window.FindName("AnswerContent")).Single();
        var editorScroll = Visuals<ScrollViewer>(expanded.Editor).Single();
        check(expanded.Editor.ActualHeight > 100 * expanded.Editor.TextArea.TextView.DefaultLineHeight && expanded.Editor.ActualHeight > 600,
            "100-line code expands to its full document height without the former 600px cap");
        check(expanded.Editor.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled && editorScroll.ScrollableHeight < 1,
            "expanded code has no internal vertical scrolling or hidden document lines");
        right.ScrollToTop(); await Task.Delay(30);
        expanded.Editor.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
        await Task.Delay(40);
        check(right.VerticalOffset > 0 && expanded.Editor.VerticalOffset == 0,
            "mouse wheel over code scrolls the enclosing answer instead of the editor");
        expanded.Editor.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
        await Task.Delay(40);
        check(right.VerticalOffset == 0, "upward wheel over code returns the enclosing answer to the top");
        right.ScrollToBottom(); await Task.Delay(50);
        var lastLine = expanded.Editor.TextArea.TextView.GetVisualLine(100);
        var bottom = expanded.Editor.TransformToAncestor(right).Transform(new System.Windows.Point(0, lastLine.VisualTop + lastLine.Height));
        check(bottom.Y > 0 && bottom.Y <= right.ViewportHeight + 1,
            "the last line of long code is reachable through the outer answer scrollbar");
        Save(window, "code-expanded-bottom.png");
        var longCode = "class Solution {\n    String message = \"" + new string('a', 160) + "\";\n    // " + string.Concat(Enumerable.Repeat("长注释需要在可见宽度内完整展示。", 12)) +
            "\n    int result = calculate(firstArgument, secondArgument, thirdArgument, fourthArgument, fifthArgument, sixthArgument);\n}";
        entry.Answer = "```java\n" + longCode + "\n```";
        right.ScrollToTop(); await Task.Delay(60);
        var wrapping = Visuals<CodeBlockView>((AnswerContentPresenter)window.FindName("AnswerContent")).Single();
        double wideHeight = 0;
        foreach (var size in new[] { (Width: 1120d, Font: 18d), (Width: 760d, Font: 18d), (Width: 760d, Font: 28d) })
        {
            window.Width = size.Width; config.Battle.AnswerFontSize = size.Font; window.UpdateConfig(config);
            await Task.Delay(80);
            wrapping = Visuals<CodeBlockView>((AnswerContentPresenter)window.FindName("AnswerContent")).Single();
            var view = wrapping.Editor.TextArea.TextView;
            var scrollbars = Visuals<ScrollViewer>(wrapping.Editor).Single();
            check(view.VisualLines.Any(line => line.TextLines.Count > 1) &&
                view.VisualLines.SelectMany(line => line.TextLines).All(line => line.Width <= view.ActualWidth + 1),
                $"long statements, unbroken identifiers and Chinese comments wrap within the code viewport at width {size.Width} and font {size.Font} (text {view.VisualLines.SelectMany(line => line.TextLines).Max(line => line.Width):0.##}, viewport {view.ActualWidth:0.##})");
            check(!((IScrollInfo)view).CanHorizontallyScroll && wrapping.Editor.HorizontalOffset == 0 && scrollbars.ScrollableHeight < 1 &&
                scrollbars.ComputedHorizontalScrollBarVisibility != Visibility.Visible && scrollbars.ComputedVerticalScrollBarVisibility != Visibility.Visible,
                $"wrapped code remains fully expanded without either internal scrollbar at width {size.Width} and font {size.Font} (range {scrollbars.ScrollableWidth:0.##}x{scrollbars.ScrollableHeight:0.##}, editor {wrapping.Editor.ActualWidth:0.##}x{wrapping.Editor.ActualHeight:0.##}, extent {scrollbars.ExtentWidth:0.##}x{scrollbars.ExtentHeight:0.##}, viewport {scrollbars.ViewportWidth:0.##}x{scrollbars.ViewportHeight:0.##})");
            if (size.Width == 1120) { wideHeight = wrapping.Editor.ActualHeight; Save(window, "code-wrap-wide.png"); }
            if (size.Width == 760 && size.Font == 18)
                check(wrapping.Editor.ActualHeight > wideHeight, "narrowing the answer pane automatically increases code height to expose wrapped lines");
        }
        check(wrapping.Code == longCode && wrapping.Editor.Text == longCode && wrapping.Editor.Document.LineCount == 5,
            "visual wrapping preserves the original code and physical newlines for selection and copying");
        Save(window, "code-wrap-narrow.png");
        config.Battle.AnswerFontSize = 18; window.UpdateConfig(config); window.Width = 1120;
        entry.Answer = Answer; await Task.Delay(50);
        double savedRatio = 0; window.ScreenshotQuestionRatioChanged += value => savedRatio = value;
        divider.RaiseEvent(new DragStartedEventArgs(0, 0));
        var columns = ((Grid)window.FindName("AnswerBodyGrid")).ColumnDefinitions;
        columns[0].Width = new GridLength(.52, GridUnitType.Star); columns[2].Width = new GridLength(.48, GridUnitType.Star);
        window.UpdateLayout(); divider.RaiseEvent(new DragCompletedEventArgs(30, 0, false));
        check(savedRatio > .49 && savedRatio < .55, "dragging the screenshot divider publishes its own persistent ratio");
        window.Width = 760; window.Height = 480; right.ScrollToTop(); await Task.Delay(100);
        var body = (Grid)window.FindName("AnswerBodyGrid");
        var rightBounds = right.TransformToAncestor(window).TransformBounds(new Rect(right.RenderSize));
        check(left.ViewportWidth > 80 && right.ViewportWidth > 120 && rightBounds.Right <= window.ActualWidth && rightBounds.Bottom <= window.ActualHeight,
            "minimum-size screenshot panes remain within the window without overlapping input");
        Save(window, "screenshot-workspace-minimum.png");
        var retried = router.Enqueue(); vm.RetryCommand.Execute(null); await Until(() => router.Requests.Count == 3);
        check(router.Requests[^1].Config.Primary.SystemPrompt != ScreenshotQuestion.ExtractionInstructions && entry.HasExtractedQuestion,
            "retrying an answer reuses the extracted question without another extraction request");
        retried.SetException(new IOException("test answer failure")); await Until(() => !entry.Busy);
        check(entry.HasExtractedQuestion && entry.Status.Contains("请求失败"), "answer failure retains the extracted problem for inspection and retry");
        var extractingAgain = router.Enqueue(); vm.ReextractCommand.Execute(null); await Until(() => router.Requests.Count == 4);
        check(!entry.HasExtractedQuestion && router.Requests[^1].Config.Primary.SystemPrompt == ScreenshotQuestion.ExtractionInstructions,
            "re-extract command explicitly starts a fresh extraction from the retained original image");
        vm.CancelRequests(); await Until(() => !entry.Busy);
        check(entry.Status.Contains("已取消") && router.Requests.Count == 4, "cancelling extraction never sends an answer request");
        var failedExtraction = router.Enqueue(); vm.RetryCommand.Execute(null);
        failedExtraction.SetResult(new() { Text = "{\"hasQuestion\":false}" }); await Until(() => !entry.Busy);
        check(entry.Status.Contains("未识别到题目") && !entry.HasExtractedQuestion && router.Requests.Count == 5,
            "a screenshot without a problem reports a useful error and stops before solving");
        var speechEntry = new BattleEntry { Source = SpeechSource.Speaker, Text = "语音问题", Answer = "普通对话答案" };
        vm.Entries.Insert(0, speechEntry); vm.Selected = speechEntry; await Task.Delay(50);
        check(!divider.IsVisible && columns[0].ActualWidth == 0 && right.ViewportWidth > body.ActualWidth - 30,
            "switching to a voice question restores the full answer width");
        vm.Selected = entry; await Task.Delay(50);
        check(divider.IsVisible && Math.Abs(columns[0].ActualWidth / (columns[0].ActualWidth + columns[2].ActualWidth) - savedRatio) < .02,
            "switching back to a screenshot restores the adjusted split ratio");
        window.Close();
    }

    public static async Task RunConfiguredAsync(Action<bool, string> check)
    {
        var logger = new QuietLogger();
        var config = await new AppConfigService(new ApiKeyProtector(), logger).LoadAsync();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, 1000, 1100));
            var text = new FormattedText(Question, System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 23, System.Windows.Media.Brushes.Black, 1) { MaxTextWidth = 940 };
            drawing.DrawText(text, new System.Windows.Point(30, 24));
        }
        var bitmap = new RenderTargetBitmap(1000, 1100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var router = new ModelRouter(new OpenAiModelClient(logger), logger);
        await using var vm = new BattleViewModel(new NoSpeech(), router, () => config, Dispatcher.CurrentDispatcher);
        vm.AddScreenshot(new() { ImageBytes = stream.ToArray() });
        var timeout = DateTime.UtcNow.AddMinutes(4);
        while (vm.Selected!.Busy) { if (DateTime.UtcNow > timeout) throw new TimeoutException("Algorithm model verification timed out."); await Task.Delay(100); }
        var entry = vm.Selected;
        check(entry.HasExtractedQuestion && entry.ExtractedQuestion.Contains("height") && entry.ExtractedQuestion.Contains("49"),
            "configured vision model extracts a generated algorithm problem including example data");
        var answer = Markdig.Markdown.Parse(entry.Answer);
        check(entry.Answer.Contains("复杂度") && answer.OfType<Markdig.Syntax.FencedCodeBlock>().Any(code => code.Lines.ToString().Contains("maxArea")),
            "configured screenshot model returns complexity analysis and fenced solution code for the extracted algorithm");
    }

    private sealed class QuietLogger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    private static byte[] Image()
    {
        var bitmap = new WriteableBitmap(300, 160, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat((byte)255, 300 * 160 * 4).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, 300, 160), pixels, 1200, 0);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(8);
        while (!condition()) { if (DateTime.UtcNow > timeout) throw new TimeoutException(); await Task.Delay(15); }
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
    private static void Save(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }
    private sealed class ScriptedRouter : IModelRouter
    {
        private readonly Queue<TaskCompletionSource<ModelReply>> _responses = [];
        public List<(AppConfig Config, IReadOnlyList<ConversationTurn> Turns)> Requests { get; } = [];
        public TaskCompletionSource<ModelReply> Enqueue() { var response = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously); _responses.Enqueue(response); return response; }
        public Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
        { Requests.Add((config, turns)); return _responses.Dequeue().Task.WaitAsync(cancellationToken); }
    }
    private sealed class NoSpeech : IBattleSpeechService
    {
        public event Action<SpeechSegment>? Transcribed { add { } remove { } }
        public event Action<string>? Failed { add { } remove { } }
        public Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
