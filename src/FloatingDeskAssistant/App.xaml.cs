using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Infrastructure.Auth;
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
    private const string ActivationEventName = @"Local\FloatingDeskAssistant.Activate";
    private static readonly TimeSpan ConfigLoadTimeout = TimeSpan.FromSeconds(3);

    private ILoggerService? _logger;
    private IAppConfigService? _configService;
    private MainViewModel? _mainViewModel;
    private RemoteCaptureServer? _remoteCaptureServer;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private GlobalOpacityHotkeys? _opacityHotkeys;
    private GlobalInteractionHotkeys? _interactionHotkeys;
    private DispatcherTimer? _opacitySaveTimer;
    private bool _opacityDirty;
    private SettingsWindow? _settingsWindow;
    private BattleHistoryWindow? _historyWindow;
    private TrayIconService? _trayIcon;
    private BattleViewModel? _battleViewModel;
    private BattleWindow? _battleWindow;
    private bool _exiting;
    private bool _authenticated;

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
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        TraceStartup("OnStartup begin");

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isSingleInstance);
        _ownsSingleInstanceMutex = isSingleInstance;
        TraceStartup($"Mutex acquired. isSingleInstance={isSingleInstance}");

        if (!isSingleInstance)
        {
            TraceStartup("Detected existing instance. Exiting.");
            try
            {
                using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                activation.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(0);
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(ActivateExistingWindow));
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        _logger = new FileLogger();
        WindowCaptureProtection.Initialize(_logger);
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

            TraceStartup("LoginWindow.Show start");
            if (!await ValidateLoginAsync(config, completed == loadTask))
            {
                TraceStartup("Login cancelled or validation not passed");
                Shutdown();
                return;
            }
            _authenticated = true;
            TraceStartup("Login validation passed");
            TraceStartup("InitializeWindows start");
            InitializeWindows(config);
            TraceStartup("RemoteCapture start");
            TryStartRemoteCaptureServer();
            TraceStartup("BattleWindow.Show start");
            ShowWorkspace();
            TraceStartup("BattleWindow.Show done");
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
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        _trayIcon?.Dispose();
        _opacityHotkeys?.Dispose();
        _interactionHotkeys?.Dispose();
        _opacitySaveTimer?.Stop();
        if (_opacityDirty && _configService is not null && _mainViewModel is not null)
        {
            try
            {
                _configService.SaveAsync(_mainViewModel.GetConfigSnapshot()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to save opacity on exit.", ex);
            }
        }

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
        if (!_authenticated || _logger is null || _configService is null)
        {
            throw new InvalidOperationException("Services not initialized.");
        }

        var modelClient = new OpenAiModelClient(_logger);
        var modelRouter = new ModelRouter(modelClient, _logger);
        var screenshotService = new ScreenshotService(_logger);

        _mainViewModel = new MainViewModel(config, modelRouter, screenshotService, _configService, _logger);
        WindowCaptureProtection.SetEnabled(config.HideFromScreenCapture);
        _mainViewModel.ConfigChanged += updated =>
        {
            if (WindowCaptureProtection.Enabled != updated.HideFromScreenCapture)
            {
                WindowCaptureProtection.SetEnabled(updated.HideFromScreenCapture);
            }
            _battleWindow?.UpdateConfig(updated);
            _historyWindow?.UpdateConfig(updated);
        };

        _mainViewModel.OpenSettingsDialogAsync = OpenSettingsDialogAsync;
        _battleViewModel = new BattleViewModel(new BattleSpeechService(() => _mainViewModel.GetConfigSnapshot().Battle), modelRouter, _mainViewModel.GetConfigSnapshot, Dispatcher, screenshotService);
        _battleViewModel.RecordingChanged += recording =>
        {
            _trayIcon?.SetRecording(recording);
            _historyWindow?.SetVoiceActive(recording);
        };
        _mainViewModel.CaptureScreenshotOverride = _battleViewModel.CaptureScreenshotAsync;
        _mainViewModel.RemotePromptOverride = text => _battleViewModel.AddSentence(SpeechSource.Speaker, text, true);
        _battleWindow = new BattleWindow(_battleViewModel, _mainViewModel.GetConfigSnapshot());
        _battleViewModel.ConfirmAudioRecording = () =>
        {
            _historyWindow?.StopPlayback();
            return _battleWindow.ConfirmAudioRecording();
        };
        MainWindow = _battleWindow;
        _battleWindow.SettingsRequested += OpenSettingsFromTray;
        _battleWindow.PhoneRequested += () => _mainViewModel.ShowRemoteCaptureInfoCommand.Execute(null);
        _battleWindow.HistoryRequested += ShowBattleHistory;
        _battleWindow.AnswerFontSizeChanged += async size => await _mainViewModel.SetAnswerFontSizeAsync(size);
        _battleWindow.QuestionPaneWidthChanged += async width => await _mainViewModel.SetQuestionPaneWidthAsync(width);
        _battleWindow.ScreenshotQuestionRatioChanged += async ratio => await _mainViewModel.SetScreenshotQuestionRatioAsync(ratio);
        _battleWindow.Closing += (_, args) => { if (!_exiting) { args.Cancel = true; _battleWindow.Hide(); } };
        _trayIcon = new TrayIconService(ShowWorkspace, OpenSettingsFromTray,
            () => WindowInteraction.SetClickThrough(!WindowInteraction.IsClickThrough), async () => await ExitAppAsync());
        InitializeOpacityHotkeys();
        InitializeInteractionHotkeys();
        var navigationReady = _interactionHotkeys!.SetQuestionNavigationEnabled(true);
        var screenshotReady = _interactionHotkeys.SetScreenshotEnabled(true);
        if (!navigationReady || !screenshotReady)
        {
            _logger.Warn(string.Join(Environment.NewLine, _interactionHotkeys.RegistrationErrors));
            _battleViewModel.ReportStatus("部分快捷键不可用，请查看日志。");
        }
    }

    private void InitializeInteractionHotkeys()
    {
        _interactionHotkeys = new GlobalInteractionHotkeys(
            () => WindowInteraction.SetClickThrough(!WindowInteraction.IsClickThrough),
            direction =>
            {
                _battleWindow?.ScrollConversation(direction);
            }, () =>
            {
                if (_battleViewModel?.ScreenshotCommand.CanExecute(null) == true)
                    _battleViewModel.ScreenshotCommand.Execute(null);
            }, direction => _battleViewModel?.SelectQuestion(direction));
        if (_interactionHotkeys.RegistrationErrors.Count > 0)
        {
            var message = string.Join(Environment.NewLine, _interactionHotkeys.RegistrationErrors);
            _logger?.Warn(message);
            MessageBox.Show(message, "willGo hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InitializeOpacityHotkeys()
    {
        _opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _opacitySaveTimer.Tick += async (_, _) =>
        {
            _opacitySaveTimer.Stop();
            if (_configService is null || _mainViewModel is null)
            {
                return;
            }

            var snapshot = _mainViewModel.GetConfigSnapshot();
            try
            {
                await _configService.SaveAsync(snapshot);
                _opacityDirty = _mainViewModel.BallOpacity != snapshot.BallOpacity
                    || _mainViewModel.WindowOpacity != snapshot.WindowOpacity;
            }
            catch (Exception ex)
            {
                _opacityDirty = true;
                _logger?.Error("Failed to save opacity.", ex);
            }
        };

        try
        {
            _opacityHotkeys = new GlobalOpacityHotkeys(delta =>
            {
                if (_mainViewModel is null)
                {
                    return;
                }

                _mainViewModel.AdjustOpacity(delta);
                _settingsWindow?.UpdateOpacity(_mainViewModel.BallOpacity, _mainViewModel.WindowOpacity);
                _opacityDirty = true;
                _opacitySaveTimer.Stop();
                _opacitySaveTimer.Start();
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("Global opacity hotkeys are unavailable.", ex);
            MessageBox.Show(ex.Message, "willGo opacity hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryStartRemoteCaptureServer()
    {
        if (!_authenticated || _logger is null || _mainViewModel is null)
        {
            return;
        }

        try
        {
            _remoteCaptureServer?.Dispose();
            _remoteCaptureServer = new RemoteCaptureServer(
                _logger,
                () => Dispatcher.Invoke(() => _battleViewModel!.QueueRemoteScreenshot()),
                (dx, dy) => Dispatcher.Invoke(() => _battleWindow!.MoveByPixels(dx, dy)),
                () => Dispatcher.Invoke(() => _battleViewModel?.Status ?? ""),
                () => false,
                _ => Task.FromResult(new RemoteCompactModeResult(false, "willGo uses a single workspace.", false)),
                () => Dispatcher.Invoke(() => _mainViewModel.GetRemotePromptPresets()),
                presetId => Dispatcher.Invoke(() => _mainViewModel.QueueRemotePromptPreset(presetId)),
                promptText => Dispatcher.Invoke(() => _mainViewModel.QueueRemotePromptText(promptText)));

            var startResult = _remoteCaptureServer.Start();
            Dispatcher.Invoke(() => _mainViewModel.SetRemoteCaptureAccess(startResult.AccessUrls));
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start remote capture server.", ex);
            Dispatcher.Invoke(() => _mainViewModel.SetRemoteCaptureUnavailable("Phone trigger failed to start. See logs for details."));
        }
    }

    private void ActivateExistingWindow()
    {
        if (Dispatcher.HasShutdownStarted || _exiting) return;
        if (Windows.OfType<LoginWindow>().FirstOrDefault() is { } login)
        {
            login.WindowState = WindowState.Normal;
            login.Show();
            login.Activate();
            return;
        }
        ShowWorkspace();
    }

    private void ShowWorkspace()
    {
        if (!_authenticated || _battleWindow is null) return;
        _battleWindow.WindowState = WindowState.Normal;
        _battleWindow.Show();
        if (!WindowInteraction.IsClickThrough) _battleWindow.Activate();
    }

    private void OpenSettingsFromTray()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        if (_mainViewModel?.OpenSettingsCommand.CanExecute(null) == true) _mainViewModel.OpenSettingsCommand.Execute(null);
    }

    private async Task ExitAppAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try { if (_battleViewModel is not null) await _battleViewModel.DisposeAsync(); }
        catch (Exception ex) { _logger?.Error("Failed to stop battle mode during exit.", ex); }
        finally { Shutdown(); }
    }

    private async Task<bool> ValidateLoginAsync(AppConfig config, bool persistPreferences)
    {
        using var validation = new WillGoUserValidationService(_logger!);
        var viewModel = new LoginViewModel(validation, new ClientIpResolver(), _logger!, config.RememberedUserId, config.RememberUserId);
        var window = new LoginWindow(viewModel);
        if (window.ShowDialog() != true || viewModel.SuccessfulResult is not { Success: true, IsAllowed: true, IsServerUnavailable: false }) return false;
        var rememberedId = viewModel.RememberUserId ? viewModel.UserId.Trim() : "";
        var changed = config.RememberUserId != viewModel.RememberUserId || config.RememberedUserId != rememberedId;
        config.RememberUserId = viewModel.RememberUserId;
        config.RememberedUserId = rememberedId;
        if (!changed || !persistPreferences) return true;
        try { await _configService!.SaveAsync(config); }
        catch (Exception ex) { _logger!.Error("Failed to save remembered login ID.", ex); }
        return true;
    }

    private void ShowBattleHistory()
    {
        if (!_authenticated || _mainViewModel is null || _battleWindow is null) return;
        if (_historyWindow is null)
        {
            var window = new BattleHistoryWindow(() => _mainViewModel.GetConfigSnapshot().Battle.SpeechArchiveDirectory,
                _mainViewModel.GetConfigSnapshot(), _battleViewModel?.IsRecording == true) { Owner = _battleWindow };
            _historyWindow = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_historyWindow, window)) _historyWindow = null; };
        }
        else _ = _historyWindow.RefreshAsync();
        _historyWindow.Show();
        _historyWindow.Activate();
    }

    private Task<AppConfig?> OpenSettingsDialogAsync(AppConfig current)
    {
        if (_configService is null)
        {
            return Task.FromResult<AppConfig?>(null);
        }

        var dialog = new SettingsWindow(current, _configService.CreateDefault())
        {
            Owner = _battleWindow
        };

        _settingsWindow = dialog;
        try
        {
            var result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.UpdatedConfig : null);
        }
        finally
        {
            _settingsWindow = null;
        }
    }
}

