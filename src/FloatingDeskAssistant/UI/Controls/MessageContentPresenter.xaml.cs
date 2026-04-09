using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FloatingDeskAssistant.UI.Controls;

public partial class MessageContentPresenter : UserControl
{
    private static readonly WpfBrush[] BracketBrushPalette =
    {
        BrushFromHex("#7DD3FC"),
        BrushFromHex("#86EFAC"),
        BrushFromHex("#F9A8D4"),
        BrushFromHex("#FCD34D"),
        BrushFromHex("#C4B5FD"),
        BrushFromHex("#67E8F9")
    };

    private static readonly WpfBrush UnmatchedBracketBrush = BrushFromHex("#F87171");
    private static readonly WpfBrush InlineCodeBackgroundBrush = BrushFromHex("#33111111");
    private static readonly WpfBrush CodeBlockBackgroundBrush = BrushFromHex("#22000000");
    private static readonly WpfFontFamily CodeFontFamily = new("Cascadia Mono,Consolas,Microsoft YaHei UI");
    private static readonly char[] OpeningBrackets = ['(', '[', '{'];
    private static readonly char[] ClosingBrackets = [')', ']', '}'];

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MessageContentPresenter),
        new PropertyMetadata(string.Empty, OnRenderPropertyChanged));

    public static readonly DependencyProperty DefaultForegroundProperty = DependencyProperty.Register(
        nameof(DefaultForeground),
        typeof(WpfBrush),
        typeof(MessageContentPresenter),
        new PropertyMetadata(WpfBrushes.White, OnRenderPropertyChanged));

    public static readonly DependencyProperty ContentFontSizeProperty = DependencyProperty.Register(
        nameof(ContentFontSize),
        typeof(double),
        typeof(MessageContentPresenter),
        new PropertyMetadata(14d, OnRenderPropertyChanged));

    public static readonly DependencyProperty SliceStartProperty = DependencyProperty.Register(
        nameof(SliceStart),
        typeof(int),
        typeof(MessageContentPresenter),
        new PropertyMetadata(0, OnRenderPropertyChanged));

    public static readonly DependencyProperty SliceLengthProperty = DependencyProperty.Register(
        nameof(SliceLength),
        typeof(int),
        typeof(MessageContentPresenter),
        new PropertyMetadata(-1, OnRenderPropertyChanged));

    public MessageContentPresenter()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderContent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public WpfBrush DefaultForeground
    {
        get => (WpfBrush)GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    public double ContentFontSize
    {
        get => (double)GetValue(ContentFontSizeProperty);
        set => SetValue(ContentFontSizeProperty, value);
    }

    public int SliceStart
    {
        get => (int)GetValue(SliceStartProperty);
        set => SetValue(SliceStartProperty, value);
    }

    public int SliceLength
    {
        get => (int)GetValue(SliceLengthProperty);
        set => SetValue(SliceLengthProperty, value);
    }

    public void ApplySlice(string fullText, int sliceStart, int sliceLength, WpfBrush defaultForeground, double fontSize)
    {
        Text = fullText;
        SliceStart = sliceStart;
        SliceLength = sliceLength;
        DefaultForeground = defaultForeground;
        ContentFontSize = fontSize;
    }

    private static void OnRenderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MessageContentPresenter presenter)
        {
            presenter.RenderContent();
        }
    }

    private void RenderContent()
    {
        if (!IsInitialized)
        {
            return;
        }

        ContentHost.Children.Clear();

        var sourceText = Text ?? string.Empty;
        var start = Math.Clamp(SliceStart, 0, sourceText.Length);
        var length = SliceLength < 0 ? sourceText.Length - start : Math.Min(SliceLength, sourceText.Length - start);
        if (length <= 0)
        {
            return;
        }

        var brushMap = BuildBracketBrushMap(sourceText);
        var blocks = BuildBlocks(sourceText, start, length);
        foreach (var block in blocks)
        {
            if (block.IsCode)
            {
                ContentHost.Children.Add(BuildCodeBlock(sourceText, brushMap, block));
                continue;
            }

            ContentHost.Children.Add(BuildPlainBlock(sourceText, brushMap, block));
        }
    }

    private FrameworkElement BuildPlainBlock(string sourceText, IReadOnlyList<WpfBrush?> brushMap, BlockSlice block)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = DefaultForeground,
            FontSize = ContentFontSize,
            Margin = new Thickness(0, 0, 0, block.HasTrailingGap ? 8 : 0)
        };

        for (var index = 0; index < block.Lines.Count; index++)
        {
            var line = block.Lines[index];
            AppendLineWithInlineCode(textBlock.Inlines, sourceText, brushMap, line);
            if (index < block.Lines.Count - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }

        return textBlock;
    }

    private FrameworkElement BuildCodeBlock(string sourceText, IReadOnlyList<WpfBrush?> brushMap, BlockSlice block)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = DefaultForeground,
            FontSize = ContentFontSize,
            FontFamily = CodeFontFamily
        };

        for (var index = 0; index < block.Lines.Count; index++)
        {
            var line = block.Lines[index];
            AppendText(textBlock.Inlines, sourceText, brushMap, line.Start, line.Length, preserveLeadingWhitespace: true, useCodeStyle: true);
            if (index < block.Lines.Count - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }

        return new Border
        {
            Background = CodeBlockBackgroundBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, block.HasTrailingGap ? 8 : 0),
            Child = textBlock
        };
    }

    private void AppendLineWithInlineCode(InlineCollection inlines, string sourceText, IReadOnlyList<WpfBrush?> brushMap, LineSlice line)
    {
        var lineText = sourceText.Substring(line.Start, line.Length);
        var segmentStart = line.Start;
        var currentIndex = 0;
        var insideInlineCode = false;

        while (currentIndex < lineText.Length)
        {
            var markerIndex = lineText.IndexOf('`', currentIndex);
            if (markerIndex < 0)
            {
                AppendText(
                    inlines,
                    sourceText,
                    brushMap,
                    segmentStart + currentIndex,
                    lineText.Length - currentIndex,
                    preserveLeadingWhitespace: false,
                    useCodeStyle: insideInlineCode);
                return;
            }

            if (markerIndex > currentIndex)
            {
                AppendText(
                    inlines,
                    sourceText,
                    brushMap,
                    segmentStart + currentIndex,
                    markerIndex - currentIndex,
                    preserveLeadingWhitespace: false,
                    useCodeStyle: insideInlineCode);
            }

            insideInlineCode = !insideInlineCode;
            currentIndex = markerIndex + 1;
        }
    }

    private void AppendText(
        InlineCollection inlines,
        string sourceText,
        IReadOnlyList<WpfBrush?> brushMap,
        int start,
        int length,
        bool preserveLeadingWhitespace,
        bool useCodeStyle)
    {
        if (length <= 0)
        {
            return;
        }

        var chunkBrush = GetEffectiveBrush(brushMap[start]);
        var buffer = new System.Text.StringBuilder();

        for (var offset = 0; offset < length; offset++)
        {
            var globalIndex = start + offset;
            var currentBrush = GetEffectiveBrush(brushMap[globalIndex]);
            var transformedChar = TransformCharacter(sourceText, globalIndex, preserveLeadingWhitespace);

            if (!Equals(currentBrush, chunkBrush))
            {
                AddRun(inlines, buffer.ToString(), chunkBrush, useCodeStyle);
                buffer.Clear();
                chunkBrush = currentBrush;
            }

            buffer.Append(transformedChar);
        }

        AddRun(inlines, buffer.ToString(), chunkBrush, useCodeStyle);
    }

    private void AddRun(InlineCollection inlines, string text, WpfBrush foreground, bool useCodeStyle)
    {
        if (text.Length == 0)
        {
            return;
        }

        var run = new Run(text)
        {
            Foreground = foreground
        };

        if (useCodeStyle)
        {
            run.FontFamily = CodeFontFamily;
            run.Background = InlineCodeBackgroundBrush;
        }

        inlines.Add(run);
    }

    private static string TransformCharacter(string sourceText, int globalIndex, bool preserveWhitespace)
    {
        var current = sourceText[globalIndex];
        if (current == '\t')
        {
            return preserveWhitespace ? "    " : " ";
        }

        if (!preserveWhitespace)
        {
            return current.ToString();
        }

        if (current == ' ' && IsLeadingWhitespace(sourceText, globalIndex))
        {
            return "\u00A0";
        }

        return current.ToString();
    }

    private static bool IsLeadingWhitespace(string sourceText, int globalIndex)
    {
        for (var index = globalIndex - 1; index >= 0; index--)
        {
            var previous = sourceText[index];
            if (previous == '\n' || previous == '\r')
            {
                return true;
            }

            if (!char.IsWhiteSpace(previous))
            {
                return false;
            }
        }

        return true;
    }

    private WpfBrush GetEffectiveBrush(WpfBrush? brush)
    {
        return brush ?? DefaultForeground ?? WpfBrushes.White;
    }

    private static WpfBrush?[] BuildBracketBrushMap(string sourceText)
    {
        var result = new WpfBrush?[sourceText.Length];
        var stack = new Stack<(char Bracket, int Index, WpfBrush Brush)>();

        for (var index = 0; index < sourceText.Length; index++)
        {
            var current = sourceText[index];
            var openIndex = Array.IndexOf(OpeningBrackets, current);
            if (openIndex >= 0)
            {
                var brush = BracketBrushPalette[stack.Count % BracketBrushPalette.Length];
                stack.Push((current, index, brush));
                continue;
            }

            var closeIndex = Array.IndexOf(ClosingBrackets, current);
            if (closeIndex < 0)
            {
                continue;
            }

            if (stack.Count > 0 && stack.Peek().Bracket == OpeningBrackets[closeIndex])
            {
                var match = stack.Pop();
                result[match.Index] = match.Brush;
                result[index] = match.Brush;
            }
            else
            {
                result[index] = UnmatchedBracketBrush;
            }
        }

        while (stack.Count > 0)
        {
            var unmatched = stack.Pop();
            result[unmatched.Index] = UnmatchedBracketBrush;
        }

        return result;
    }

    private static List<BlockSlice> BuildBlocks(string sourceText, int start, int length)
    {
        var lines = SliceLines(sourceText, start, length);
        var blocks = new List<BlockSlice>();
        var pendingLines = new List<LineSlice>();
        var pendingIsCode = false;
        var insideFence = IsInsideFenceBefore(sourceText, start);

        foreach (var line in lines)
        {
            var lineText = sourceText.Substring(line.Start, line.Length);
            var trimmed = lineText.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushPendingBlock(blocks, pendingLines, pendingIsCode);
                insideFence = !insideFence;
                continue;
            }

            var isCode = insideFence || LooksLikeCodeLine(lineText);
            if (pendingLines.Count > 0 && pendingIsCode != isCode)
            {
                FlushPendingBlock(blocks, pendingLines, pendingIsCode);
            }

            pendingIsCode = isCode;
            pendingLines.Add(line);
        }

        FlushPendingBlock(blocks, pendingLines, pendingIsCode);
        return blocks;
    }

    private static List<LineSlice> SliceLines(string sourceText, int start, int length)
    {
        var lines = new List<LineSlice>();
        var index = start;
        var end = start + length;

        while (index < end)
        {
            var lineStart = index;
            while (index < end && sourceText[index] != '\r' && sourceText[index] != '\n')
            {
                index++;
            }

            var lineLength = index - lineStart;
            lines.Add(new LineSlice(lineStart, lineLength));

            if (index < end && sourceText[index] == '\r')
            {
                index++;
            }

            if (index < end && sourceText[index] == '\n')
            {
                index++;
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(new LineSlice(start, length));
        }

        return lines;
    }

    private static bool IsInsideFenceBefore(string sourceText, int start)
    {
        var insideFence = false;
        foreach (var line in SliceLines(sourceText, 0, start))
        {
            var lineText = sourceText.Substring(line.Start, line.Length).Trim();
            if (lineText.StartsWith("```", StringComparison.Ordinal))
            {
                insideFence = !insideFence;
            }
        }

        return insideFence;
    }

    private static bool LooksLikeCodeLine(string lineText)
    {
        var trimmed = lineText.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("#include", StringComparison.Ordinal)
            || trimmed.StartsWith("public ", StringComparison.Ordinal)
            || trimmed.StartsWith("private ", StringComparison.Ordinal)
            || trimmed.StartsWith("class ", StringComparison.Ordinal)
            || trimmed.StartsWith("def ", StringComparison.Ordinal)
            || trimmed.StartsWith("for ", StringComparison.Ordinal)
            || trimmed.StartsWith("for(", StringComparison.Ordinal)
            || trimmed.StartsWith("while ", StringComparison.Ordinal)
            || trimmed.StartsWith("if ", StringComparison.Ordinal)
            || trimmed.StartsWith("if(", StringComparison.Ordinal)
            || trimmed.StartsWith("return ", StringComparison.Ordinal)
            || trimmed.StartsWith("vector<", StringComparison.Ordinal)
            || trimmed.StartsWith("unordered_", StringComparison.Ordinal))
        {
            return true;
        }

        var punctuationScore = 0;
        foreach (var current in trimmed)
        {
            if (current is '{' or '}' or '[' or ']' or '(' or ')' or ';' or '<' or '>' or '=' or ':' )
            {
                punctuationScore++;
            }
        }

        return punctuationScore >= 3
            || trimmed.Contains("=>", StringComparison.Ordinal)
            || trimmed.Contains("::", StringComparison.Ordinal)
            || trimmed.Contains("->", StringComparison.Ordinal);
    }

    private static void FlushPendingBlock(List<BlockSlice> blocks, List<LineSlice> pendingLines, bool pendingIsCode)
    {
        if (pendingLines.Count == 0)
        {
            return;
        }

        while (pendingLines.Count > 1 && pendingLines[^1].Length == 0)
        {
            pendingLines.RemoveAt(pendingLines.Count - 1);
        }

        if (pendingLines.Count == 0)
        {
            return;
        }

        blocks.Add(new BlockSlice(pendingIsCode, pendingLines.ToArray(), true));
        pendingLines.Clear();
    }

    private static WpfSolidColorBrush BrushFromHex(string hex)
    {
        var brush = (WpfSolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private readonly record struct LineSlice(int Start, int Length);

    private readonly record struct BlockSlice(bool IsCode, IReadOnlyList<LineSlice> Lines, bool HasTrailingGap);
}
