using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Windows;

namespace FloatingDeskAssistant.Application;

public sealed class ScreenshotService : IScreenshotService
{
    private const int DesktopCaptureHideDelayMs = 150;

    private readonly ILoggerService _logger;

    public ScreenshotService(ILoggerService logger)
    {
        _logger = logger;
    }

    public Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ScreenshotCaptureResult?>();

        var thread = new Thread(() =>
        {
            try
            {
                using var overlay = new ScreenshotOverlayForm();
                var dialog = overlay.ShowDialog();
                if (dialog != DialogResult.OK || overlay.SelectedRectangle.Width <= 0 || overlay.SelectedRectangle.Height <= 0)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var capture = CaptureRectangle(overlay.SelectedRectangle, cancellationToken, "screenshot");
                tcs.TrySetResult(capture);
            }
            catch (Exception ex)
            {
                _logger.Error("Screenshot capture failed.", ex);
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "ScreenshotCaptureThread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
    }

    public async Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken cancellationToken)
    {
        var hiddenWindows = Array.Empty<Window>();

        try
        {
            hiddenWindows = await HideVisibleAppWindowsAsync(cancellationToken);
            if (hiddenWindows.Length > 0)
            {
                await Task.Delay(DesktopCaptureHideDelayMs, cancellationToken);
            }

            return await Task.Run(() => CaptureVirtualDesktop(cancellationToken), cancellationToken);
        }
        finally
        {
            if (hiddenWindows.Length > 0)
            {
                await RestoreHiddenWindowsAsync(hiddenWindows, cancellationToken);
            }
        }
    }

    public async Task<ScreenshotCaptureResult?> CaptureRegionAsync(Rectangle region, CancellationToken cancellationToken)
    {
        var hiddenWindows = Array.Empty<Window>();

        try
        {
            hiddenWindows = await HideVisibleAppWindowsAsync(cancellationToken);
            if (hiddenWindows.Length > 0)
            {
                await Task.Delay(DesktopCaptureHideDelayMs, cancellationToken);
            }

            return await Task.Run(() => CaptureRectangle(region, cancellationToken, "remote-region"), cancellationToken);
        }
        finally
        {
            if (hiddenWindows.Length > 0)
            {
                await RestoreHiddenWindowsAsync(hiddenWindows, cancellationToken);
            }
        }
    }

    private ScreenshotCaptureResult? CaptureVirtualDesktop(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _logger.Warn("Virtual desktop bounds were empty. Desktop capture skipped.");
            return null;
        }

        return CaptureRectangle(bounds, cancellationToken, "desktop");
    }

    private ScreenshotCaptureResult? CaptureRectangle(Rectangle requestedBounds, CancellationToken cancellationToken, string filePrefix)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var virtualBounds = SystemInformation.VirtualScreen;
        var captureBounds = Rectangle.Intersect(requestedBounds, virtualBounds);
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            _logger.Warn($"Capture bounds were outside the virtual desktop. Requested={requestedBounds}, Virtual={virtualBounds}");
            return null;
        }

        using var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(captureBounds.Left, captureBounds.Top, 0, 0, captureBounds.Size, CopyPixelOperation.SourceCopy);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var imageBytes = stream.ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var imagePath = SaveToTempFile(imageBytes, filePrefix);
        return new ScreenshotCaptureResult
        {
            ImageBytes = imageBytes,
            ImagePath = imagePath
        };
    }

    private static async Task<Window[]> HideVisibleAppWindowsAsync(CancellationToken cancellationToken)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return Array.Empty<Window>();
        }

        return await application.Dispatcher.InvokeAsync(() =>
        {
            var windows = application.Windows
                .OfType<Window>()
                .Where(window => window.IsVisible)
                .ToArray();

            foreach (var window in windows)
            {
                window.Hide();
            }

            return windows;
        }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken);
    }

    private static async Task RestoreHiddenWindowsAsync(IEnumerable<Window> windows, CancellationToken cancellationToken)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        await application.Dispatcher.InvokeAsync(() =>
        {
            foreach (var window in windows)
            {
                if (!window.IsVisible)
                {
                    window.Show();
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken);
    }

    private string SaveToTempFile(byte[] imageBytes, string filePrefix = "screenshot")
    {
        var screenshotDir = Path.Combine(Path.GetTempPath(), "FloatingDeskAssistant", "screenshots");
        Directory.CreateDirectory(screenshotDir);

        var fileName = $"{filePrefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
        var filePath = Path.Combine(screenshotDir, fileName);
        File.WriteAllBytes(filePath, imageBytes);
        var absolutePath = Path.GetFullPath(filePath);
        _logger.Info($"Screenshot saved: {absolutePath}");
        return absolutePath;
    }
}