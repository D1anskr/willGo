using System.Drawing;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Application;

public interface IAppConfigService
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
    AppConfig CreateDefault();
}

public interface ILoggerService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

public interface IModelClient
{
    Task<string> SendAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken);
}

public interface IModelRouter
{
    Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken);
}

public interface IScreenshotService
{
    Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken cancellationToken);
    Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken cancellationToken);
    Task<ScreenshotCaptureResult?> CaptureRegionAsync(Rectangle region, CancellationToken cancellationToken);
}