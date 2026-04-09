using System.IO;
using System.Windows.Media.Imaging;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels.Base;
using WpfBitmapCacheOption = System.Windows.Media.Imaging.BitmapCacheOption;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImageSource = System.Windows.Media.ImageSource;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FloatingDeskAssistant.ViewModels;

public sealed class MessageItemViewModel : ObservableObject
{
    public required ChatRole Role { get; init; }
    public required string DisplayText { get; init; }
    public required WpfHorizontalAlignment Alignment { get; init; }
    public required WpfBrush BubbleBrush { get; init; }
    public required WpfBrush TextBrush { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public WpfImageSource? ImageSource { get; init; }
    public bool HasImage => ImageSource is not null;

    public string TimestampText => Timestamp.ToString("HH:mm:ss");

    public static MessageItemViewModel Create(ChatRole role, string? text, byte[]? imageBytes)
    {
        var normalizedText = text ?? string.Empty;
        return role switch
        {
            ChatRole.User => new MessageItemViewModel
            {
                Role = role,
                DisplayText = string.IsNullOrWhiteSpace(normalizedText) && imageBytes is not null ? "[Image]" : normalizedText,
                Alignment = WpfHorizontalAlignment.Right,
                BubbleBrush = new WpfSolidColorBrush(WpfColor.FromArgb(180, 40, 109, 207)),
                TextBrush = WpfBrushes.White,
                ImageSource = ToImage(imageBytes)
            },
            ChatRole.Assistant => new MessageItemViewModel
            {
                Role = role,
                DisplayText = normalizedText,
                Alignment = WpfHorizontalAlignment.Left,
                BubbleBrush = new WpfSolidColorBrush(WpfColor.FromArgb(160, 45, 45, 45)),
                TextBrush = WpfBrushes.White,
                ImageSource = ToImage(imageBytes)
            },
            ChatRole.Error => new MessageItemViewModel
            {
                Role = role,
                DisplayText = normalizedText,
                Alignment = WpfHorizontalAlignment.Left,
                BubbleBrush = new WpfSolidColorBrush(WpfColor.FromArgb(190, 155, 30, 30)),
                TextBrush = WpfBrushes.White,
                ImageSource = null
            },
            _ => new MessageItemViewModel
            {
                Role = role,
                DisplayText = normalizedText,
                Alignment = WpfHorizontalAlignment.Center,
                BubbleBrush = new WpfSolidColorBrush(WpfColor.FromArgb(180, 110, 90, 12)),
                TextBrush = WpfBrushes.White,
                ImageSource = null
            }
        };
    }

    private static WpfImageSource? ToImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var bitmap = new WpfBitmapImage();
        using var memory = new MemoryStream(bytes);
        bitmap.BeginInit();
        bitmap.CacheOption = WpfBitmapCacheOption.OnLoad;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
