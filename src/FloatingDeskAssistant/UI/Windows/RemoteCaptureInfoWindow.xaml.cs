using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace FloatingDeskAssistant.UI.Windows;

public partial class RemoteCaptureInfoWindow : Window
{
    private readonly string _primaryUrl;

    public RemoteCaptureInfoWindow(IReadOnlyList<string> accessUrls, string statusText)
    {
        InitializeComponent();

        _primaryUrl = accessUrls.FirstOrDefault() ?? string.Empty;
        PrimaryUrlTextBox.Text = _primaryUrl;
        StatusTextBlock.Text = $"Current status: {statusText}";

        if (accessUrls.Count > 1)
        {
            OtherUrlsTextBlock.Text = "Other URLs:" + Environment.NewLine + string.Join(Environment.NewLine, accessUrls.Skip(1));
            OtherUrlsTextBlock.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(_primaryUrl))
        {
            QrImage.Source = CreateQrImage(_primaryUrl);
        }
    }

    private static BitmapImage CreateQrImage(string text)
    {
        var pngBytes = PngByteQRCodeHelper.GetQRCode(text, QRCodeGenerator.ECCLevel.Q, 20);
        var image = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CopyUrlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_primaryUrl))
        {
            return;
        }

        System.Windows.Clipboard.SetText(_primaryUrl);
        StatusTextBlock.Text = $"Current status: copied {_primaryUrl}";
    }

    private void OpenPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_primaryUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _primaryUrl,
            UseShellExecute = true
        });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
