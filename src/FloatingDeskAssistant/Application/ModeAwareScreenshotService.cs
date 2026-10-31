using System.Drawing;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Application;

public sealed class ModeAwareScreenshotService(IScreenshotService local, DualMachineService peer, Func<AppConfig> getConfig) : IScreenshotService
{
    public Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken cancellationToken) =>
        getConfig().DualMachine.IsDisplay ? peer.RequestScreenshotAsync(cancellationToken) : local.CaptureAsync(cancellationToken);
    public Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken cancellationToken) =>
        getConfig().DualMachine.IsDisplay ? peer.RequestScreenshotAsync(cancellationToken) : local.CaptureFullDesktopAsync(cancellationToken);
    public Task<ScreenshotCaptureResult?> CaptureRegionAsync(Rectangle region, CancellationToken cancellationToken) =>
        getConfig().DualMachine.IsDisplay ? peer.RequestScreenshotAsync(cancellationToken) : local.CaptureRegionAsync(region, cancellationToken);
}
