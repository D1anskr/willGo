using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;

namespace FloatingDeskAssistant.UI.Controls;

public sealed class CodeBlockView : Border
{
    public TextEditor Editor { get; }
    public Button CopyButton { get; }
    public string Code { get; }

    public CodeBlockView(string code, string language, double fontSize)
    {
        Code = code;
        var surface = new SolidColorBrush(Color.FromRgb(246, 248, 250));
        var border = new SolidColorBrush(Color.FromRgb(216, 223, 227));
        Background = surface; BorderBrush = border; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6); ClipToBounds = true;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new Grid { Margin = new Thickness(12, 7, 8, 7) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "代码" : language, FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(98, 109, 118)), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0)
        };
        toolbar.Children.Add(label);
        CopyButton = new Button
        {
            Content = "\uE8C8", FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 30, Height = 28,
            MinHeight = 28, Padding = new Thickness(0), Margin = new Thickness(0), ToolTip = "复制代码"
        };
        CopyButton.SetResourceReference(StyleProperty, "LightButton");
        System.Windows.Automation.AutomationProperties.SetName(CopyButton, "复制代码");
        CopyButton.Click += (_, _) =>
        {
            try { Clipboard.SetText(Code); CopyButton.ToolTip = "代码已复制"; }
            catch (ExternalException) { CopyButton.ToolTip = "剪贴板正忙，请重试"; }
        };
        Grid.SetColumn(CopyButton, 1); toolbar.Children.Add(CopyButton); grid.Children.Add(toolbar);
        var languageName = language.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        var definition = languageName switch
        {
            "java" => "Java", "c#" or "cs" or "csharp" => "C#", "c" or "cpp" or "c++" => "C++",
            "python" or "py" => "Python", "js" or "javascript" or "ts" or "typescript" => "JavaScript",
            "json" => "Json", "sql" => "TSQL", "xml" => "XML", "html" => "HTML", _ => ""
        };
        Editor = new TextEditor
        {
            Text = code, IsReadOnly = true, ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Microsoft YaHei UI"), FontSize = fontSize,
            Background = surface, Foreground = new SolidColorBrush(Color.FromRgb(36, 41, 46)),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(112, 122, 130)),
            WordWrap = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 10, 12, 12), BorderThickness = new Thickness(0),
            SyntaxHighlighting = definition.Length > 0 ? HighlightingManager.Instance.GetDefinition(definition) : null
        };
        Editor.Options.AllowScrollBelowDocument = false;
        // Let the enclosing answer pane scroll even when the pointer is over code.
        Editor.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            { RoutedEvent = MouseWheelEvent, Source = this });
        };
        Grid.SetRow(Editor, 1); grid.Children.Add(Editor); Child = grid;
    }
}
