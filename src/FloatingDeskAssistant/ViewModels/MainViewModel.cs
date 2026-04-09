using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels.Base;
using Microsoft.Win32;

namespace FloatingDeskAssistant.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IModelRouter _modelRouter;
    private readonly IScreenshotService _screenshotService;
    private readonly IAppConfigService _configService;
    private readonly ILoggerService _logger;

    private readonly List<ConversationTurn> _sessionTurns = new();
    private string _inputText = string.Empty;
    private bool _isBusy;
    private string _statusText = "Ready";
    private bool _canRetryLastRequest;
    private FailedRequest? _lastFailedRequest;
    private AppConfig _config;
    private byte[]? _pendingImageBytes;
    private string? _pendingImageMediaType;
    private string? _pendingImageFileName;
    private System.Windows.Media.ImageSource? _pendingImagePreview;
    private string[] _remoteCaptureAccessUrls = Array.Empty<string>();
    private string _remoteCaptureSummary = "Phone trigger is starting...";

    public MainViewModel(
        AppConfig config,
        IModelRouter modelRouter,
        IScreenshotService screenshotService,
        IAppConfigService configService,
        ILoggerService logger)
    {
        _config = config;
        _modelRouter = modelRouter;
        _screenshotService = screenshotService;
        _configService = configService;
        _logger = logger;

        Messages = new ObservableCollection<MessageItemViewModel>();
        Messages.CollectionChanged += (_, _) => RaiseMessagePresentationPropertiesChanged();
        SendTextCommand = new AsyncRelayCommand(SendTextAsync, CanSendText);
        CaptureCommand = new AsyncRelayCommand(CaptureAndSendAsync, CanExecuteWhenIdle);
        CaptureDesktopCommand = new AsyncRelayCommand(CaptureDesktopAndSendAsync, CanExecuteWhenIdle);
        AttachImageCommand = new AsyncRelayCommand(AttachImageAsync, CanExecuteWhenIdle);
        RemovePendingImageCommand = new RelayCommand(RemovePendingImage, () => HasPendingImage);
        ClearSessionCommand = new RelayCommand(ClearSession);
        RetryLastRequestCommand = new AsyncRelayCommand(RetryLastAsync, () => !IsBusy && CanRetryLastRequest);
        ToggleCompactChatModeCommand = new AsyncRelayCommand(ToggleCompactChatModeAsync, CanExecuteWhenIdle);
        TogglePreferredModelCommand = new AsyncRelayCommand(TogglePreferredModelAsync, CanExecuteWhenIdle);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync, CanExecuteWhenIdle);
        ShowRemoteCaptureInfoCommand = new RelayCommand(ShowRemoteCaptureInfo);
    }

    public ObservableCollection<MessageItemViewModel> Messages { get; }

    public MessageItemViewModel? CurrentMessage => Messages.Count == 0 ? null : Messages[^1];

    public IReadOnlyList<MessageItemViewModel> HistoryMessages => Messages.Count <= 1
        ? Array.Empty<MessageItemViewModel>()
        : Messages.Take(Messages.Count - 1).Reverse().ToArray();

    public bool HasCurrentMessage => CurrentMessage is not null;

    public bool HasHistoryMessages => Messages.Count > 1;

    public AsyncRelayCommand SendTextCommand { get; }

    public AsyncRelayCommand CaptureCommand { get; }

    public AsyncRelayCommand CaptureDesktopCommand { get; }

    public AsyncRelayCommand AttachImageCommand { get; }

    public RelayCommand RemovePendingImageCommand { get; }

    public RelayCommand ClearSessionCommand { get; }

    public AsyncRelayCommand RetryLastRequestCommand { get; }

    public AsyncRelayCommand ToggleCompactChatModeCommand { get; }

    public AsyncRelayCommand TogglePreferredModelCommand { get; }

    public AsyncRelayCommand OpenSettingsCommand { get; }

    public RelayCommand ShowRemoteCaptureInfoCommand { get; }

    public Func<AppConfig, Task<AppConfig?>>? OpenSettingsDialogAsync { get; set; }

    public event Action<AppConfig>? ConfigChanged;

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendTextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public System.Windows.Media.ImageSource? PendingImagePreview
    {
        get => _pendingImagePreview;
        private set => SetProperty(ref _pendingImagePreview, value);
    }

    public bool HasPendingImage => _pendingImageBytes is { Length: > 0 };

    public string PendingImageLabel => string.IsNullOrWhiteSpace(_pendingImageFileName)
        ? "Attached image"
        : _pendingImageFileName;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SendTextCommand.NotifyCanExecuteChanged();
                CaptureCommand.NotifyCanExecuteChanged();
                CaptureDesktopCommand.NotifyCanExecuteChanged();
                AttachImageCommand.NotifyCanExecuteChanged();
                RetryLastRequestCommand.NotifyCanExecuteChanged();
                ToggleCompactChatModeCommand.NotifyCanExecuteChanged();
                TogglePreferredModelCommand.NotifyCanExecuteChanged();
                OpenSettingsCommand.NotifyCanExecuteChanged();
                RemovePendingImageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanRetryLastRequest
    {
        get => _canRetryLastRequest;
        private set
        {
            if (SetProperty(ref _canRetryLastRequest, value))
            {
                RetryLastRequestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double BallOpacity => _config.BallOpacity;

    public bool IsCompactChatMode => _config.CompactChatMode;

    public CompactModeLevel CompactModeLevel => NormalizeCompactModeLevel(_config.CompactModeLevel);

    public bool IsPassiveCompactChatMode =>
        _config.CompactChatMode && (CompactModeLevel == CompactModeLevel.Mode2 || CompactModeLevel == CompactModeLevel.Mode3);

    public bool IsLatestOnlyCompactChatMode =>
        _config.CompactChatMode && CompactModeLevel == CompactModeLevel.Mode3;

    public string CompactChatModeButtonText => _config.CompactChatMode ? "Full" : "Compact";

    public string PreferredModelButtonText => _config.PreferredModel == PreferredModelTarget.Secondary
        ? "Prefer: Secondary"
        : "Prefer: Primary";

    public double MessageFontSize => _config.MessageFontSize;

    public GridLength HeaderRowHeight => _config.CompactChatMode ? new GridLength(0) : GridLength.Auto;

    public GridLength ConversationRowHeight => new GridLength(1, GridUnitType.Star);

    public GridLength StatusRowHeight => _config.CompactChatMode ? new GridLength(0) : GridLength.Auto;

    public Visibility HeaderVisibility => _config.CompactChatMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ConversationVisibility => Visibility.Visible;

    public Visibility HistoryVisibility => HasHistoryMessages && !IsLatestOnlyCompactChatMode
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility StatusVisibility => _config.CompactChatMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility StandardActionButtonsVisibility => _config.CompactChatMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SendPanelVisibility => _config.CompactChatMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FooterVisibility => IsPassiveCompactChatMode
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string DesktopButtonToolTip => _config.CompactChatMode
        ? "Capture full desktop and send. Press Esc to exit compact mode."
        : "Capture full desktop and send.";

    public Visibility PendingImageVisibility => _config.CompactChatMode || !HasPendingImage
        ? Visibility.Collapsed
        : Visibility.Visible;

    public double WindowOpacity => _config.WindowOpacity;

    public string RemoteCaptureButtonText => "Phone";

    public string RemoteCaptureToolTip => _remoteCaptureSummary;

    public bool HasRemoteCaptureAccess => _remoteCaptureAccessUrls.Length > 0;

    public System.Windows.Media.Brush WindowBackdropBrush
    {
        get
        {
            var alpha = (byte)Math.Round(Math.Clamp(_config.WindowOpacity, 0.3, 1.0) * 255);
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 16, 16, 16));
        }
    }

    public async Task SendTextAsync()
    {
        var text = string.IsNullOrWhiteSpace(InputText) ? null : InputText.Trim();
        var hasImage = HasPendingImage;
        if (string.IsNullOrWhiteSpace(text) && !hasImage)
        {
            return;
        }

        var imageBytes = _pendingImageBytes;
        var imageMediaType = _pendingImageMediaType;
        var imageFileName = _pendingImageFileName;
        var displayText = BuildLocalImageDisplayText(text, imageFileName, hasImage);

        InputText = string.Empty;
        ClearPendingImage();
        await SendInternalAsync(text, imageBytes, imageMediaType, userDisplayText: displayText);
    }

    public async Task CaptureAndSendAsync()
    {
        await CaptureScreenshotAndSendAsync(
            cancellationToken => _screenshotService.CaptureAsync(cancellationToken),
            "Select screenshot area...",
            "Screenshot canceled",
            "Screenshot failed. Please check permissions and retry.",
            "Screenshot failed");
    }

    public async Task CaptureDesktopAndSendAsync()
    {
        await CaptureScreenshotAndSendAsync(
            cancellationToken => _screenshotService.CaptureFullDesktopAsync(cancellationToken),
            "Capturing full desktop...",
            "Desktop capture canceled",
            "Full desktop capture failed. Please check permissions and retry.",
            "Desktop capture failed");
    }

    private async Task CaptureRemoteConfiguredAsync()
    {
        if (_config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion)
        {
            if (!HasRemoteCapturePresetRegion())
            {
                AddMessage(ChatRole.Error, "Remote preset region is not configured. Open Settings and choose a region first.", null);
                StatusText = "Remote capture not configured";
                return;
            }

            await CaptureScreenshotAndSendAsync(
                cancellationToken => _screenshotService.CaptureRegionAsync(GetRemoteCapturePresetRegion(), cancellationToken),
                "Capturing preset remote region...",
                "Remote preset region capture canceled",
                "Preset region capture failed. Please check the saved region and retry.",
                "Remote preset capture failed");
            return;
        }

        await CaptureDesktopAndSendAsync();
    }

    public async Task AttachImageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Attach image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var mediaType = DetectImageMediaType(dialog.FileName);
            if (mediaType is null)
            {
                AddMessage(ChatRole.Error, "Unsupported image format. Use png/jpg/jpeg/webp/bmp/gif.", null);
                StatusText = "Attach failed";
                return;
            }

            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length == 0)
            {
                AddMessage(ChatRole.Error, "Image file is empty.", null);
                StatusText = "Attach failed";
                return;
            }

            SetPendingImage(bytes, mediaType, Path.GetFileName(dialog.FileName), ToImage(bytes));
            StatusText = $"Image attached: {PendingImageLabel}";
        }
        catch (Exception ex)
        {
            _logger.Error("Attach image failed.", ex);
            AddMessage(ChatRole.Error, "Failed to read image file. Please retry.", null);
            StatusText = "Attach failed";
        }
    }

    public void RemovePendingImage()
    {
        if (!HasPendingImage)
        {
            return;
        }

        ClearPendingImage();
        StatusText = "Image removed";
    }

    public void ClearSession()
    {
        Messages.Clear();
        _sessionTurns.Clear();
        _lastFailedRequest = null;
        CanRetryLastRequest = false;
        ClearPendingImage();
        StatusText = "Session cleared";
    }

    public async Task RetryLastAsync()
    {
        if (_lastFailedRequest is null)
        {
            return;
        }

        StatusText = "Retrying last request...";
        await SendInternalAsync(
            _lastFailedRequest.Text,
            _lastFailedRequest.ImageBytes,
            _lastFailedRequest.ImageMediaType,
            isRetry: true);
    }

    public RemoteCaptureTriggerResult QueueRemoteDesktopCapture()
    {
        if (IsBusy)
        {
            return new RemoteCaptureTriggerResult(false, "willGo is busy with the previous request. Please retry in a moment.");
        }

        if (_config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion && !HasRemoteCapturePresetRegion())
        {
            return new RemoteCaptureTriggerResult(false, "Remote capture mode is set to PresetRegion, but no region has been saved in Settings yet.");
        }

        _ = CaptureRemoteConfiguredAsync();
        return _config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion
            ? new RemoteCaptureTriggerResult(true, "Remote preset-region capture accepted. willGo is capturing the saved region now.")
            : new RemoteCaptureTriggerResult(true, "Remote full-desktop capture accepted. willGo is capturing the host desktop now.");
    }

    public IReadOnlyList<RemotePromptPresetInfo> GetRemotePromptPresets()
    {
        return _config.PromptPresets
            .Where(preset => preset.IsEnabled
                && !string.IsNullOrWhiteSpace(preset.Id)
                && !string.IsNullOrWhiteSpace(preset.Title)
                && !string.IsNullOrWhiteSpace(preset.Prompt))
            .Select(preset => new RemotePromptPresetInfo(
                preset.Id,
                preset.Title.Trim(),
                BuildRemotePromptPresetSummary(preset.Prompt)))
            .ToArray();
    }

    public RemotePromptPresetTriggerResult QueueRemotePromptPreset(string presetId)
    {
        if (IsBusy)
        {
            return new RemotePromptPresetTriggerResult(false, "willGo is busy with the previous request. Please retry in a moment.");
        }

        var preset = _config.PromptPresets.FirstOrDefault(item =>
            item.IsEnabled
            && string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            return new RemotePromptPresetTriggerResult(false, "Prompt preset not found. Refresh the phone page and retry.");
        }

        var prompt = preset.Prompt?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new RemotePromptPresetTriggerResult(false, "Prompt preset is empty. Edit it in Settings first.");
        }

        _ = SendInternalAsync(prompt, null, null);
        return new RemotePromptPresetTriggerResult(true, $"Remote prompt preset accepted: {preset.Title.Trim()}");
    }

    public void SetRemoteCaptureAccess(IReadOnlyList<string> accessUrls)
    {
        _remoteCaptureAccessUrls = accessUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _remoteCaptureSummary = _remoteCaptureAccessUrls.Length == 0
            ? "Phone trigger is unavailable."
            : $"Phone trigger ready: {_remoteCaptureAccessUrls[0]}";

        RaisePropertyChanged(nameof(RemoteCaptureToolTip));
        RaisePropertyChanged(nameof(HasRemoteCaptureAccess));
    }

    public void SetRemoteCaptureUnavailable(string reason)
    {
        _remoteCaptureAccessUrls = Array.Empty<string>();
        _remoteCaptureSummary = string.IsNullOrWhiteSpace(reason)
            ? "Phone trigger is unavailable."
            : reason;

        RaisePropertyChanged(nameof(RemoteCaptureToolTip));
        RaisePropertyChanged(nameof(HasRemoteCaptureAccess));
    }

    private async Task SendInternalAsync(
        string? text,
        byte[]? imageBytes,
        string? imageMediaType,
        bool isRetry = false,
        string? userDisplayText = null)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && (imageBytes is null || imageBytes.Length == 0))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Processing...";
        CanRetryLastRequest = false;

        if (!isRetry)
        {
            var displayText = string.IsNullOrWhiteSpace(userDisplayText) ? text : userDisplayText;
            AddMessage(ChatRole.User, displayText, imageBytes);
        }
        else
        {
            AddMessage(ChatRole.System, "Retrying last request...", null);
        }

        _sessionTurns.Add(new ConversationTurn
        {
            Role = ChatRole.User,
            Text = text,
            ImageBytes = imageBytes,
            ImageMediaType = imageMediaType
        });

        try
        {
            var reply = await _modelRouter.AskAsync(_config, _sessionTurns, CancellationToken.None);
            AddMessage(ChatRole.Assistant, reply.Text, null);

            _sessionTurns.Add(new ConversationTurn
            {
                Role = ChatRole.Assistant,
                Text = reply.Text
            });

            _lastFailedRequest = null;
            CanRetryLastRequest = false;
            StatusText = BuildCompletionStatusText(reply);
        }
        catch (ModelApiException ex)
        {
            _logger.Error("Model request failed.", ex);
            _lastFailedRequest = new FailedRequest
            {
                Text = text,
                ImageBytes = imageBytes,
                ImageMediaType = imageMediaType
            };
            CanRetryLastRequest = true;
            AddMessage(ChatRole.Error, ex.Message, null);
            StatusText = "Request failed. You can retry.";
        }
        catch (Exception ex)
        {
            _logger.Error("Unexpected request failure.", ex);
            _lastFailedRequest = new FailedRequest
            {
                Text = text,
                ImageBytes = imageBytes,
                ImageMediaType = imageMediaType
            };
            CanRetryLastRequest = true;
            AddMessage(ChatRole.Error, "Unexpected error occurred. Please retry.", null);
            StatusText = "Request failed. You can retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleCompactChatModeAsync()
    {
        await SetCompactChatModeAsync(!_config.CompactChatMode);
    }

    public async Task<RemoteCompactModeResult> SetCompactChatModeAsync(bool enabled)
    {
        var previous = _config.CompactChatMode;
        if (previous == enabled)
        {
            var unchangedMessage = enabled
                ? "Compact mode is already enabled"
                : "Compact mode is already disabled";
            StatusText = unchangedMessage;
            return new RemoteCompactModeResult(true, unchangedMessage, enabled);
        }

        _config.CompactChatMode = enabled;
        RaiseCompactChatPropertiesChanged();

        try
        {
            await _configService.SaveAsync(_config, CancellationToken.None);
            ConfigChanged?.Invoke(_config.Clone());

            var message = enabled ? "Compact mode enabled" : "Compact mode disabled";
            StatusText = message;
            return new RemoteCompactModeResult(true, message, enabled);
        }
        catch (Exception ex)
        {
            _config.CompactChatMode = previous;
            RaiseCompactChatPropertiesChanged();
            _logger.Error("Failed to save compact chat mode.", ex);
            AddMessage(ChatRole.Error, "Failed to save compact chat mode.", null);
            StatusText = "Compact mode save failed";
            return new RemoteCompactModeResult(false, "Failed to save compact chat mode.", previous);
        }
    }

    private async Task TogglePreferredModelAsync()
    {
        var previous = _config.PreferredModel;
        var next = previous == PreferredModelTarget.Primary
            ? PreferredModelTarget.Secondary
            : PreferredModelTarget.Primary;

        _config.PreferredModel = next;
        RaisePropertyChanged(nameof(PreferredModelButtonText));

        try
        {
            await _configService.SaveAsync(_config, CancellationToken.None);
            ConfigChanged?.Invoke(_config.Clone());
            StatusText = next == PreferredModelTarget.Secondary
                ? "Preferred model set to Secondary"
                : "Preferred model set to Primary";
        }
        catch (Exception ex)
        {
            _config.PreferredModel = previous;
            RaisePropertyChanged(nameof(PreferredModelButtonText));
            _logger.Error("Failed to save preferred model.", ex);
            AddMessage(ChatRole.Error, "Failed to save preferred model preference.", null);
            StatusText = "Preferred model save failed";
        }
    }

    private async Task CaptureScreenshotAndSendAsync(
        Func<CancellationToken, Task<ScreenshotCaptureResult?>> captureAsync,
        string pendingStatus,
        string canceledStatus,
        string failureMessage,
        string failureStatus)
    {
        StatusText = pendingStatus;
        ScreenshotCaptureResult? capture;
        try
        {
            capture = await captureAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error("Screenshot flow failed.", ex);
            AddMessage(ChatRole.Error, failureMessage, null);
            StatusText = failureStatus;
            return;
        }

        if (capture is null)
        {
            StatusText = canceledStatus;
            return;
        }

        var userText = string.IsNullOrWhiteSpace(InputText) ? null : InputText.Trim();
        var displayText = BuildScreenshotDisplayText(userText, capture.ImagePath);
        var modelText = BuildScreenshotModelText(userText);
        InputText = string.Empty;
        ClearPendingImage();

        await SendInternalAsync(modelText, capture.ImageBytes, "image/png", userDisplayText: displayText);
    }

    private async Task OpenSettingsAsync()
    {
        if (OpenSettingsDialogAsync is null)
        {
            return;
        }

        try
        {
            var updated = await OpenSettingsDialogAsync(_config.Clone());
            if (updated is null)
            {
                return;
            }

            _config = updated;
            await _configService.SaveAsync(_config, CancellationToken.None);
            ConfigChanged?.Invoke(_config.Clone());
            RaisePropertyChanged(nameof(BallOpacity));
            RaisePropertyChanged(nameof(MessageFontSize));
            RaisePropertyChanged(nameof(PreferredModelButtonText));
            RaiseCompactChatPropertiesChanged();
            RaisePropertyChanged(nameof(WindowOpacity));
            RaisePropertyChanged(nameof(WindowBackdropBrush));
            StatusText = "Settings saved";
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings.", ex);
            AddMessage(ChatRole.Error, "Failed to save settings. Please check configuration and retry.", null);
            StatusText = "Settings save failed";
        }
    }

    private void ShowRemoteCaptureInfo()
    {
        if (_remoteCaptureAccessUrls.Length == 0)
        {
            System.Windows.MessageBox.Show(
                _remoteCaptureSummary,
                "Phone Trigger",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var owner = System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsVisible);

        var dialog = new RemoteCaptureInfoWindow(_remoteCaptureAccessUrls, StatusText);
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        StatusText = "Phone trigger QR ready";
    }

    private bool HasRemoteCapturePresetRegion()
    {
        return _config.RemoteCaptureRegionWidth >= 6 && _config.RemoteCaptureRegionHeight >= 6;
    }

    private System.Drawing.Rectangle GetRemoteCapturePresetRegion()
    {
        return new System.Drawing.Rectangle(
            _config.RemoteCaptureRegionX,
            _config.RemoteCaptureRegionY,
            _config.RemoteCaptureRegionWidth,
            _config.RemoteCaptureRegionHeight);
    }

    private void AddMessage(ChatRole role, string? text, byte[]? imageBytes)
    {
        Messages.Add(MessageItemViewModel.Create(role, text, imageBytes));
    }

    private static string BuildCompletionStatusText(ModelReply reply)
    {
        if (!string.IsNullOrWhiteSpace(reply.Warning))
        {
            return reply.UsedSecondary
                ? "Done (auto-switched to secondary model)."
                : "Done (auto-switched to primary model).";
        }

        return reply.UsedSecondary ? "Done (secondary model)." : "Done";
    }

    private static string BuildScreenshotDisplayText(string? userText, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return userText ?? "Please analyze this screenshot.";
        }

        if (string.IsNullOrWhiteSpace(userText))
        {
            return $"Please analyze this screenshot.{Environment.NewLine}Screenshot path: {imagePath}";
        }

        return $"{userText}{Environment.NewLine}Screenshot path: {imagePath}";
    }

    private static string BuildScreenshotModelText(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return "Please analyze the attached screenshot directly.";
        }

        return $"{userText}{Environment.NewLine}Please answer using the attached screenshot and do not read any local file path.";
    }

    private static string BuildLocalImageDisplayText(string? userText, string? fileName, bool hasImage)
    {
        if (!hasImage)
        {
            return userText ?? string.Empty;
        }

        var imageNote = string.IsNullOrWhiteSpace(fileName)
            ? "[Attached image]"
            : $"[Attached image: {fileName}]";

        if (string.IsNullOrWhiteSpace(userText))
        {
            return imageNote;
        }

        return $"{userText}{Environment.NewLine}{imageNote}";
    }

    private static string BuildRemotePromptPresetSummary(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var firstLine = prompt
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return string.Empty;
        }

        return firstLine.Length <= 88
            ? firstLine
            : $"{firstLine[..85]}...";
    }

    private void SetPendingImage(byte[]? bytes, string? mediaType, string? fileName, System.Windows.Media.ImageSource? preview)
    {
        _pendingImageBytes = bytes;
        _pendingImageMediaType = mediaType;
        _pendingImageFileName = fileName;
        PendingImagePreview = preview;

        RaisePropertyChanged(nameof(HasPendingImage));
        RaisePropertyChanged(nameof(PendingImageLabel));
        RaisePropertyChanged(nameof(PendingImageVisibility));
        SendTextCommand.NotifyCanExecuteChanged();
        RemovePendingImageCommand.NotifyCanExecuteChanged();
    }

    private void RaiseCompactChatPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsCompactChatMode));
        RaisePropertyChanged(nameof(CompactModeLevel));
        RaisePropertyChanged(nameof(IsPassiveCompactChatMode));
        RaisePropertyChanged(nameof(IsLatestOnlyCompactChatMode));
        RaisePropertyChanged(nameof(CompactChatModeButtonText));
        RaisePropertyChanged(nameof(HeaderRowHeight));
        RaisePropertyChanged(nameof(ConversationRowHeight));
        RaisePropertyChanged(nameof(StatusRowHeight));
        RaisePropertyChanged(nameof(HeaderVisibility));
        RaisePropertyChanged(nameof(ConversationVisibility));
        RaisePropertyChanged(nameof(HistoryVisibility));
        RaisePropertyChanged(nameof(StatusVisibility));
        RaisePropertyChanged(nameof(StandardActionButtonsVisibility));
        RaisePropertyChanged(nameof(SendPanelVisibility));
        RaisePropertyChanged(nameof(FooterVisibility));
        RaisePropertyChanged(nameof(DesktopButtonToolTip));
        RaisePropertyChanged(nameof(PendingImageVisibility));
    }

    private void RaiseMessagePresentationPropertiesChanged()
    {
        RaisePropertyChanged(nameof(CurrentMessage));
        RaisePropertyChanged(nameof(HistoryMessages));
        RaisePropertyChanged(nameof(HasCurrentMessage));
        RaisePropertyChanged(nameof(HasHistoryMessages));
        RaisePropertyChanged(nameof(HistoryVisibility));
    }

    private static CompactModeLevel NormalizeCompactModeLevel(CompactModeLevel mode)
    {
        return Enum.IsDefined(typeof(CompactModeLevel), mode)
            ? mode
            : CompactModeLevel.Mode1;
    }

    private void ClearPendingImage()
    {
        SetPendingImage(null, null, null, null);
    }

    private static System.Windows.Media.ImageSource? ToImage(byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();
            using var memory = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectImageMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => null
        };
    }

    private bool CanSendText()
    {
        return !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || HasPendingImage);
    }

    private bool CanExecuteWhenIdle()
    {
        return !IsBusy;
    }
}






