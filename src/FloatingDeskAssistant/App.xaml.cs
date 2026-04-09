using System.IO;
using System.Threading;
using System.Windows;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Logging;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace FloatingDeskAssistant;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\FloatingDeskAssistant.SingleInstance";
    private static readonly TimeSpan ConfigLoadTimeout = TimeSpan.FromSeconds(3);

    private ILoggerService? _logger;
    private IAppConfigService? _configService;
    private MainViewModel? _mainViewModel;
    private BallWindow? _ballWindow;
    private ChatWindow? _chatWindow;
    private FloatingWindowCoordinator? _windowCoordinator;
    private RemoteCaptureServer? _remoteCaptureServer;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    private static void TraceStartup(string message)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FloatingDeskAssistant");
            Directory.CreateDirectory(root);
            var tracePath = Path.Combine(root, "startup-trace.log");
            File.AppendAllText(tracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort trace only.
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        TraceStartup("OnStartup begin");

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isSingleInstance);
        _ownsSingleInstanceMutex = isSingleInstance;
        TraceStartup($"Mutex acquired. isSingleInstance={isSingleInstance}");

        if (!isSingleInstance)
        {
            TraceStartup("Detected existing instance. Exiting.");
            MessageBox.Show(
                "Floating Desk Assistant is already running.",
                "Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        _logger = new FileLogger();
        _configService = new AppConfigService(new ApiKeyProtector(), _logger);
        TraceStartup("Services initialized");

        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error("Unhandled UI exception.", args.Exception);
            TraceStartup($"Unhandled UI exception: {args.Exception.GetType().Name} {args.Exception.Message}");
            MessageBox.Show(
                "An unhandled exception occurred. See logs for details.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            TraceStartup("Config load start");
            var loadTask = _configService.LoadAsync(CancellationToken.None);
            var completed = await Task.WhenAny(loadTask, Task.Delay(ConfigLoadTimeout));

            AppConfig config;
            if (completed == loadTask)
            {
                TraceStartup("Config load completed");
                config = await loadTask;
            }
            else
            {
                TraceStartup("Config load timeout; using defaults");
                _logger.Warn("Config load timed out. Falling back to defaults for startup.");
                config = _configService.CreateDefault();
            }

            TraceStartup("InitializeWindows start");
            InitializeWindows(config);
            TraceStartup("RemoteCapture start");
            TryStartRemoteCaptureServer();
            TraceStartup("BallWindow.Show start");
            _ballWindow?.Show();
            TraceStartup("BallWindow.Show done");
        }
        catch (Exception ex)
        {
            TraceStartup($"Startup exception: {ex.GetType().Name} {ex.Message}");
            _logger.Error("Application startup failed.", ex);
            MessageBox.Show(
                "Startup failed. Please check logs.",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _remoteCaptureServer?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void InitializeWindows(AppConfig config)
    {
        if (_logger is null || _configService is null)
        {
            throw new InvalidOperationException("Services not initialized.");
        }

        var modelClient = new OpenAiModelClient(_logger);
        var modelRouter = new ModelRouter(modelClient, _logger);
        var screenshotService = new ScreenshotService(_logger);

        _mainViewModel = new MainViewModel(config, modelRouter, screenshotService, _configService, _logger);

        _chatWindow = new ChatWindow(_mainViewModel);
        _ballWindow = new BallWindow(_mainViewModel);
        _windowCoordinator = new FloatingWindowCoordinator(_ballWindow, _chatWindow);
        _ballWindow.SetCoordinator(_windowCoordinator);

        _mainViewModel.OpenSettingsDialogAsync = OpenSettingsDialogAsync;
        _ballWindow.ShowActivated = false;
    }

    private void TryStartRemoteCaptureServer()
    {
        if (_logger is null || _mainViewModel is null)
        {
            return;
        }

        try
        {
            _remoteCaptureServer?.Dispose();
            _remoteCaptureServer = new RemoteCaptureServer(
                _logger,
                () => Dispatcher.Invoke(() => _mainViewModel.QueueRemoteDesktopCapture()),
                (dx, dy) => Dispatcher.Invoke(() => _windowCoordinator?.MoveBallByPixels(dx, dy) ?? new RemoteBallMoveResult(false, "Ball window is unavailable.")),
                () => Dispatcher.Invoke(() => _mainViewModel.StatusText),
                () => Dispatcher.Invoke(() => _mainViewModel.IsCompactChatMode),
                enabled => Dispatcher.InvokeAsync(() => _mainViewModel.SetCompactChatModeAsync(enabled)).Task.Unwrap(),
                () => Dispatcher.Invoke(() => _mainViewModel.GetRemotePromptPresets()),
                presetId => Dispatcher.Invoke(() => _mainViewModel.QueueRemotePromptPreset(presetId)));

            var startResult = _remoteCaptureServer.Start();
            Dispatcher.Invoke(() => _mainViewModel.SetRemoteCaptureAccess(startResult.AccessUrls));
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start remote capture server.", ex);
            Dispatcher.Invoke(() => _mainViewModel.SetRemoteCaptureUnavailable("Phone trigger failed to start. See logs for details."));
        }
    }

    private Task<AppConfig?> OpenSettingsDialogAsync(AppConfig current)
    {
        if (_configService is null)
        {
            return Task.FromResult<AppConfig?>(null);
        }

        var dialog = new SettingsWindow(current, _configService.CreateDefault())
        {
            Owner = _ballWindow
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.UpdatedConfig : null);
    }
}

