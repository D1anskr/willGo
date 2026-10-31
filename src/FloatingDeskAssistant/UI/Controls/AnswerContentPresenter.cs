using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Themes;
using Block = Markdig.Syntax.Block;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Table = Markdig.Extensions.Tables.Table;
using TableRow = Markdig.Extensions.Tables.TableRow;
using TableCell = Markdig.Extensions.Tables.TableCell;
using Panel = System.Windows.Controls.Panel;

namespace FloatingDeskAssistant.UI.Controls;

public sealed class AnswerContentPresenter : System.Windows.Controls.UserControl
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().Build();
    private static readonly FontFamily BodyFont = new("Microsoft YaHei UI,Microsoft YaHei,Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Mono,Consolas,Microsoft YaHei UI");
    private static readonly Brush DefaultBodyBrush = FrozenBrush("#24292E");
    private static readonly Brush DefaultStrongBrush = FrozenBrush("#111418");
    private static readonly Brush DefaultMutedBrush = FrozenBrush("#626D76");
    private static readonly Brush DefaultCodeBrush = FrozenBrush("#245540");
    private static readonly Brush DefaultCodeBackground = FrozenBrush("#F4F6F7");
    private static readonly Brush DefaultRuleBrush = FrozenBrush("#D8DFE3");
    private FusionColors? _colors;
    private Brush BodyBrush => _colors?.Text ?? DefaultBodyBrush;
    private Brush StrongBrush => _colors?.Text ?? DefaultStrongBrush;
    private Brush MutedBrush => _colors?.Secondary ?? DefaultMutedBrush;
    private Brush CodeBrush => _colors?.Text ?? DefaultCodeBrush;
    private Brush CodeBackground => _colors?.Panel ?? DefaultCodeBackground;
    private Brush RuleBrush => _colors?.Border ?? DefaultRuleBrush;
    private readonly StackPanel _host = new();
    private MarkdownDocument? _document;
    private string? _parsedText;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(AnswerContentPresenter), new PropertyMetadata("", OnContentChanged));
    public static readonly DependencyProperty ContentFontSizeProperty = DependencyProperty.Register(
        nameof(ContentFontSize), typeof(double), typeof(AnswerContentPresenter), new PropertyMetadata(18d, OnContentChanged));
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(FusionStyle?), typeof(AnswerContentPresenter), new PropertyMetadata(null, OnContentChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double ContentFontSize { get => (double)GetValue(ContentFontSizeProperty); set => SetValue(ContentFontSizeProperty, value); }
    public FusionStyle? Palette { get => (FusionStyle?)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }

    public AnswerContentPresenter()
    {
        Content = _host;
        Focusable = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((AnswerContentPresenter)sender).Render();

    private double BodySize => double.IsFinite(ContentFontSize) ? Math.Clamp(ContentFontSize, 14, 28) : 18;

    private void Render()
    {
        _colors = Palette is { } style ? FusionColors.For(style) : null;
        var text = Text ?? "";
        if (_document is null || _parsedText != text)
        {
            _document = Markdown.Parse(text, Pipeline);
            _parsedText = text;
        }
        _host.Children.Clear();
        RenderBlocks(_document, _host);
    }

    private void RenderBlocks(ContainerBlock blocks, Panel host, bool compact = false)
    {
        foreach (var block in blocks) host.Children.Add(RenderBlock(block, compact));
    }

    private FrameworkElement RenderBlock(Block block, bool compact)
    {
        var gap = compact ? 5 : 16;
        switch (block)
        {
            case HeadingBlock heading:
                var title = NewText(BodySize + (heading.Level <= 2 ? 4 : 2));
                title.FontWeight = FontWeights.SemiBold;
                title.Foreground = StrongBrush;
                title.Margin = new Thickness(0, 8, 0, 10);
                AppendInlines(title.Inlines, heading.Inline);
                return title;
            case ParagraphBlock paragraph:
                var body = NewText(BodySize);
                body.Margin = new Thickness(0, 0, 0, gap);
                AppendInlines(body.Inlines, paragraph.Inline);
                return body;
            case CodeBlock code:
                return new CodeBlockView(code.Lines.ToString().TrimEnd('\r', '\n'),
                    code is FencedCodeBlock fence ? fence.Info ?? "" : "", Math.Max(14, BodySize - 2), Palette)
                { Margin = new Thickness(0, 2, 0, gap) };
            case ListBlock list:
                var rows = new StackPanel { Margin = new Thickness(0, 0, 0, compact ? 4 : 12) };
                var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var marker = NewText(BodySize);
                    marker.Text = list.IsOrdered ? (number++).ToString(CultureInfo.InvariantCulture) + "." : "\u2022";
                    marker.Foreground = MutedBrush;
                    marker.MinWidth = 20;
                    marker.Margin = new Thickness(2, 0, 10, 0);
                    row.Children.Add(marker);
                    var contents = new StackPanel();
                    Grid.SetColumn(contents, 1);
                    RenderBlocks(item, contents, true);
                    row.Children.Add(contents);
                    rows.Children.Add(row);
                }
                return rows;
            case QuoteBlock quote:
                var quoteContents = new StackPanel();
                RenderBlocks(quote, quoteContents, true);
                return new Border
                {
                    BorderBrush = MutedBrush, BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 4, 4, 4), Margin = new Thickness(0, 0, 0, gap), Child = quoteContents
                };
            case Table table:
                return RenderTable(table, gap);
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 8, 0, 20) };
            case ContainerBlock container:
                var nested = new StackPanel();
                RenderBlocks(container, nested, compact);
                return nested;
            case LeafBlock leaf:
                var fallback = NewText(BodySize);
                fallback.Text = leaf.Lines.ToString();
                fallback.Margin = new Thickness(0, 0, 0, gap);
                return fallback;
            default:
                return new Border();
        }
    }

    private FrameworkElement RenderTable(Table table, int gap)
    {
        var grid = new Grid();
        var rows = table.OfType<TableRow>().ToArray();
        var columns = rows.Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90, MaxWidth = 300 });
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[rowIndex];
            for (var column = 0; column < row.Count; column++)
            {
                var contents = new StackPanel();
                RenderBlocks((TableCell)row[column], contents, true);
                if (row.IsHeader) contents.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                var cell = new Border
                {
                    BorderBrush = RuleBrush, BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = row.IsHeader ? CodeBackground : Brushes.Transparent,
                    Padding = new Thickness(12, 8, 12, 4), Child = contents
                };
                Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
        }
        return new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 2, 0, gap)
        };
    }

    private TextBlock NewText(double size) => new()
    {
        FontFamily = BodyFont, FontSize = size, Foreground = BodyBrush,
        FontWeight = _colors is null ? FontWeights.Normal : FontWeights.Medium,
        TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.75,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };

    private void AppendInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = MonoFont, FontSize = Math.Max(14, BodySize - 1), Foreground = CodeBrush, Background = CodeBackground });
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) { span.FontWeight = FontWeights.Bold; span.Foreground = StrongBrush; }
                    else span.FontStyle = FontStyles.Italic;
                    AppendInlines(span.Inlines, emphasis);
                    target.Add(span);
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case LinkInline link:
                    // Display labels without navigating or fetching model-supplied URLs/images.
                    var label = new Span();
                    AppendInlines(label.Inlines, link);
                    target.Add(label);
                    break;
                case AutolinkInline autoLink:
                    target.Add(new Run(autoLink.Url));
                    break;
                case HtmlInline html:
                    target.Add(new Run(html.Tag));
                    break;
                case ContainerInline nested:
                    AppendInlines(target, nested);
                    break;
            }
        }
    }

    private static Brush FrozenBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
