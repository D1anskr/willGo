using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels.Base;

namespace FloatingDeskAssistant.ViewModels;

public sealed class BattleEntry : ObservableObject
{
    private string _answer = "";
    private string _status = "";
    private bool _busy;
    private string _extractedQuestion = "", _screenshotTitle = "";
    public required string Text { get; init; }
    public required SpeechSource Source { get; init; }
    public byte[]? ImageBytes { get; init; }
    public ImageSource? ImagePreview { get; init; }
    public bool IsScreenshot => ImageBytes is { Length: > 0 };
    public DateTime Created { get; } = DateTime.Now;
    public string Title => (IsScreenshot && _screenshotTitle.Length > 0 ? _screenshotTitle : Text).ReplaceLineEndings(" ");
    public string Heading => IsScreenshot ? Title : Text;
    public string ExtractedQuestion => _extractedQuestion;
    public bool HasExtractedQuestion => _extractedQuestion.Length > 0;
    public void SetExtractedQuestion(string title, string markdown)
    {
        _screenshotTitle = title; _extractedQuestion = markdown;
        RaisePropertyChanged(nameof(Title)); RaisePropertyChanged(nameof(Heading)); RaisePropertyChanged(nameof(ExtractedQuestion)); RaisePropertyChanged(nameof(HasExtractedQuestion));
    }
    public string SourceLabel => IsScreenshot ? "对方 · 截图" : Source == SpeechSource.Speaker ? "对方" : "我";
    public string TimeLabel => $"{Created:HH:mm:ss} · {SourceLabel}";
    public string Answer { get => _answer; set => SetProperty(ref _answer, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool Busy { get => _busy; set => SetProperty(ref _busy, value); }
}

public static class BattlePrompt
{
    public const string AnswerFormatting = "\n\n排版要求：在不违背用户指定格式的前提下，使用 Markdown 短段落和必要的列表组织回答；用 **加粗** 标出少量核心结论、关键术语和步骤名，不要整段加粗；复杂回答可用二级或三级标题，代码使用带语言的代码块。保持回答直接，不额外增加无关内容。";
    public static bool MatchesTrigger(string text, string phrases)
    {
        var normalized = Normalize(text);
        return (phrases ?? "").Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(p => p.Length > 0)
            .Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => new((value ?? "").Where(char.IsLetterOrDigit).ToArray());

    public static (AppConfig Config, ConversationTurn[] Turns) Build(AppConfig source, BattleEntry entry, IReadOnlyList<BattleEntry>? history = null)
    {
        var config = source.Clone();
        if (!entry.IsScreenshot)
        {
            config.Primary = config.ConversationModels.Primary.Clone();
            config.Secondary = config.ConversationModels.Secondary.Clone();
            config.PreferredModel = config.ConversationModels.PreferredModel;
            var prompt = config.Battle.ConversationPrompt;
            if (!string.IsNullOrWhiteSpace(config.Battle.ContextPrompt)) prompt += "\n\n" + config.Battle.ContextPrompt;
            config.Primary.SystemPrompt = prompt;
            config.Secondary.SystemPrompt = prompt;
        }
        config.Primary.SystemPrompt += AnswerFormatting;
        config.Secondary.SystemPrompt += AnswerFormatting;
        if (entry.IsScreenshot)
        {
            config.Primary.SystemPrompt += ScreenshotQuestion.AnswerInstructions;
            config.Secondary.SystemPrompt += ScreenshotQuestion.AnswerInstructions;
        }
        var turns = new List<ConversationTurn>();
        if (!entry.IsScreenshot && history is not null)
        {
            foreach (var previous in history.Where(item => !ReferenceEquals(item, entry)).TakeLast(2))
            {
                turns.Add(new() { Role = ChatRole.User, Text = $"{previous.SourceLabel}：\n{(previous.HasExtractedQuestion ? previous.ExtractedQuestion : previous.Text)}" });
                turns.Add(new() { Role = ChatRole.Assistant, Text = string.IsNullOrWhiteSpace(previous.Answer) ? "（本轮尚未获得有效回答，请勿据此推断事实。）" : previous.Answer[..Math.Min(previous.Answer.Length, 12000)] });
            }
        }
        turns.Add(new ConversationTurn
        {
            Role = ChatRole.User,
            Text = entry.IsScreenshot ? $"这是已从截图提取的题目，请解答：\n\n{entry.ExtractedQuestion}" : $"{(entry.Source == SpeechSource.Speaker ? "对方说" : "我说")}：\n{entry.Text}",
            ImageBytes = entry.ImageBytes,
            ImageMediaType = entry.IsScreenshot ? "image/png" : null
        });
        return (config, turns.ToArray());
    }
}

public sealed class BattleViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IBattleSpeechService _speech;
    private readonly IModelRouter _router;
    private readonly Func<AppConfig> _getConfig;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _requests = new(2, 2);
    private readonly SemaphoreSlim _conversationRequests = new(1, 1);
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private CancellationTokenSource _session = new();
    private CancellationTokenSource? _starting;
    private readonly HashSet<Task> _pending = [];
    private BattleEntry? _selected;
    private string _status = "待机";
    private string _connectionStatus = "";
    private string _microphonePartial = "";
    private string _speakerPartial = "";
    private long _microphoneSequence;
    private long _speakerSequence;
    private string _manualText = "";
    private string _transcript = "";
    private readonly Queue<string> _transcriptLines = new();
    private bool _recording;
    private volatile bool _accepting;
    private volatile bool _stoppingCapture;
    private int _captureGeneration;
    private bool _disposed;
    private readonly IScreenshotService? _screenshots;
    private bool _screenshotBusy;
    private Task? _activeScreenshot;
    public bool IsScreenshotBusy { get => _screenshotBusy; private set { SetProperty(ref _screenshotBusy, value); ScreenshotCommand.NotifyCanExecuteChanged(); } }

    public BattleViewModel(IBattleSpeechService speech, IModelRouter router, Func<AppConfig> getConfig, Dispatcher dispatcher, IScreenshotService? screenshots = null)
    {
        _speech = speech;
        _router = router;
        _getConfig = getConfig;
        _dispatcher = dispatcher;
        _screenshots = screenshots;
        ScreenshotCommand = new AsyncRelayCommand(CaptureScreenshotAsync, () => !IsScreenshotBusy && !_disposed && _screenshots is not null);
        _speech.Transcribed += OnTranscribed;
        _speech.Failed += OnFailed;
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, () => !IsStarting);
        RetryCommand = new RelayCommand(() => { if (Selected is not null) Submit(Selected); });
        ReextractCommand = new RelayCommand(() =>
        {
            if (Selected is not { IsScreenshot: true, Busy: false } entry) return;
            entry.SetExtractedQuestion("", ""); Submit(entry);
        });
        CancelCommand = new RelayCommand(CancelRequests);
        ClearCommand = new RelayCommand(Clear);
        SendCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(ManualText)) return;
            AddSentence(SpeechSource.Speaker, ManualText.Trim(), true);
            ManualText = "";
        }, () => !string.IsNullOrWhiteSpace(ManualText) && !_disposed);
    }

    public ObservableCollection<BattleEntry> Entries { get; } = [];
    public BattleEntry? Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
    public string MicrophonePartial { get => _microphonePartial; private set => SetProperty(ref _microphonePartial, value); }
    public string SpeakerPartial { get => _speakerPartial; private set => SetProperty(ref _speakerPartial, value); }
    public string Transcript { get => _transcript; private set => SetProperty(ref _transcript, value); }
    public string ManualText
    {
        get => _manualText;
        set { if (SetProperty(ref _manualText, value)) SendCommand.NotifyCanExecuteChanged(); }
    }
    public bool IsRecording { get => _recording; private set { SetProperty(ref _recording, value); RaisePropertyChanged(nameof(RecordingButtonText)); RecordingChanged?.Invoke(value); } }
    public bool IsStarting => _starting is not null;
    public string RecordingButtonText => IsStarting ? "正在启动…" : IsRecording ? "结束战斗" : "开始战斗";
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ReextractCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand SendCommand { get; }
    public AsyncRelayCommand ScreenshotCommand { get; }
    public event Action<bool>? RecordingChanged;
    public Func<bool?>? ConfirmAudioRecording { get; set; }
    public void ReportStatus(string message) => Status = message;

    public void SelectQuestion(int direction)
    {
        if (_disposed || Entries.Count == 0 || direction == 0) return;
        var index = Selected is null ? -1 : Entries.IndexOf(Selected);
        Selected = Entries[index < 0 ? 0 : Math.Clamp(index + Math.Sign(direction), 0, Entries.Count - 1)];
    }

    public RemoteCaptureTriggerResult QueueRemoteScreenshot()
    {
        if (_disposed || _screenshots is null) return new(false, "战斗模式截图不可用。");
        if (IsScreenshotBusy) return new(false, "正在截图，请稍后再试。");
        var config = _getConfig();
        if (!config.DualMachine.IsDisplay && config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion && (config.RemoteCaptureRegionWidth < 6 || config.RemoteCaptureRegionHeight < 6))
            return new(false, "请先在设置 → 模型设置 → 截图模型中选择并保存截图区域。");
        _ = CaptureScreenshotAsync();
        return new(true, "截图已受理，结果将出现在战斗模式的问题记录中。");
    }

    public Task CaptureScreenshotAsync()
    {
        if (_disposed || IsScreenshotBusy || _screenshots is null) return Task.CompletedTask;
        return _activeScreenshot = CaptureScreenshotCoreAsync();
    }

    private async Task CaptureScreenshotCoreAsync()
    {
        IsScreenshotBusy = true;
        var token = _session.Token;
        try
        {
            Status = "正在截图…";
            var config = _getConfig();
            ScreenshotCaptureResult? image;
            if (!config.DualMachine.IsDisplay && config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion)
            {
                if (config.RemoteCaptureRegionWidth < 6 || config.RemoteCaptureRegionHeight < 6)
                    throw new InvalidOperationException("请先在设置 → 模型设置 → 截图模型中选择并保存截图区域。");
                image = await _screenshots!.CaptureRegionAsync(new(config.RemoteCaptureRegionX, config.RemoteCaptureRegionY, config.RemoteCaptureRegionWidth, config.RemoteCaptureRegionHeight), token);
            }
            else image = await _screenshots!.CaptureFullDesktopAsync(token);
            token.ThrowIfCancellationRequested();
            if (image is null) { Status = "已取消截图"; return; }
            AddScreenshot(image);
            Status = "截图已加入问题记录";
        }
        catch (OperationCanceledException) { Status = "已取消截图"; }
        catch (Exception ex) { Status = "截图失败：" + ex.Message; }
        finally
        {
            IsScreenshotBusy = false;
        }
    }

    public void AddScreenshot(ScreenshotCaptureResult image)
    {
        if (_disposed || image.ImageBytes.Length == 0) return;
        using var stream = new MemoryStream(image.ImageBytes);
        var preview = new BitmapImage();
        preview.BeginInit(); preview.CacheOption = BitmapCacheOption.OnLoad; preview.StreamSource = stream; preview.EndInit(); preview.Freeze();
        var entry = new BattleEntry { Source = SpeechSource.Speaker, Text = $"截图问题 {Entries.Count + 1} · {DateTime.Now:HH:mm:ss}", ImageBytes = image.ImageBytes.ToArray(), ImagePreview = preview };
        Entries.Insert(0, entry);
        Selected = entry;
        Submit(entry);
    }

    public async Task ToggleRecordingAsync()
    {
        if (IsRecording) { await StopCaptureAsync(); return; }
        await StartCaptureAsync();
    }

    public async Task StartCaptureAsync()
    {
        await _captureGate.WaitAsync();
        try
        {
            if (_disposed || IsRecording || _stoppingCapture) return;
            _starting = new CancellationTokenSource();
            NotifyStartingChanged();
            bool? recordAudio = null;
            if (ConfirmAudioRecording is { } confirm)
            {
                recordAudio = confirm();
                if (recordAudio is null) { Status = "已取消开启语音识别"; return; }
            }
            _starting.Token.ThrowIfCancellationRequested();
            Status = _getConfig().DualMachine.IsDisplay ? "正在连接数据端音频与豆包云端语音识别…" : "正在连接豆包云端语音识别…";
            var config = _getConfig().Battle;
            _captureGeneration++;
            _microphoneSequence = _speakerSequence = 0;
            _accepting = true;
            await _speech.StartAsync(config.MicrophoneDeviceId, config.SpeakerDeviceId, _starting.Token, recordAudio);
            _starting.Token.ThrowIfCancellationRequested();
            IsRecording = true;
            Status = recordAudio == false ? "采集中 · 麦克风 / 系统播放 · 仅保存文字" :
                recordAudio == true || config.SaveSpeechSessions ? "采集中 · 麦克风 / 系统播放 · 录音保存中" : "采集中 · 麦克风 / 系统播放";
            if (_getConfig().DualMachine.IsDisplay) Status = "双机 · 数据端" + Status;
        }
        catch (OperationCanceledException) { _accepting = false; await _speech.StopAsync(); Status = "已暂停"; }
        catch (Exception ex) { _accepting = false; await _speech.StopAsync(); Status = "无法开始采集：" + ex.Message; }
        finally { _starting?.Dispose(); _starting = null; NotifyStartingChanged(); _captureGate.Release(); }
    }

    private void NotifyStartingChanged()
    {
        RaisePropertyChanged(nameof(IsStarting));
        RaisePropertyChanged(nameof(RecordingButtonText));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    public async Task StopCaptureAsync()
    {
        _accepting = false;
        _stoppingCapture = true;
        _starting?.Cancel();
        await _captureGate.WaitAsync();
        try
        {
            await _speech.StopAsync();
            IsRecording = false;
            MicrophonePartial = SpeakerPartial = "";
            Status = "已暂停";
        }
        finally { _stoppingCapture = false; _captureGate.Release(); }
    }

    private void OnTranscribed(SpeechSegment segment)
    {
        if (!_accepting && !_stoppingCapture) return;
        var generation = _captureGeneration;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || generation != _captureGeneration) return;
            if (!_accepting)
            {
                if (segment.IsFinal) AddSentence(segment.Source, segment.Text, false);
                return;
            }
            if (segment.Source == SpeechSource.Microphone && segment.Sequence >= _microphoneSequence)
            {
                _microphoneSequence = segment.Sequence;
                MicrophonePartial = segment.IsFinal ? "" : segment.Text;
            }
            else if (segment.Source == SpeechSource.Speaker && segment.Sequence >= _speakerSequence)
            {
                _speakerSequence = segment.Sequence;
                SpeakerPartial = segment.IsFinal ? "" : segment.Text;
            }
            if (segment.IsFinal && !string.IsNullOrWhiteSpace(segment.Text))
                AddSentence(segment.Source, segment.Text, segment.Source == SpeechSource.Speaker ||
                    BattlePrompt.MatchesTrigger(segment.Text, _getConfig().Battle.TriggerPhrases));
            if (!string.IsNullOrEmpty(segment.Warning)) Status = segment.Warning;
        });
    }

    private void OnFailed(string message) => _dispatcher.BeginInvoke(async () =>
    {
        if (_disposed) return;
        await StopCaptureAsync();
        Status = message;
    });

    public void AddSentence(SpeechSource source, string text, bool submit)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;
        var entry = new BattleEntry { Source = source, Text = text, Status = submit ? "等待提交" : "未触发" };
        _transcriptLines.Enqueue($"{entry.Created:HH:mm:ss} {entry.SourceLabel}：{text}");
        while (_transcriptLines.Count > 300) _transcriptLines.Dequeue();
        Transcript = string.Join(Environment.NewLine + Environment.NewLine, _transcriptLines);
        // Only submitted sentences become answer tabs; microphone speech remains in the transcript.
        if (!submit) return;
        Entries.Insert(0, entry);
        Selected = entry;
        Submit(entry);
    }

    private void Submit(BattleEntry entry)
    {
        if (entry.Busy || _disposed) return;
        if (_pending.Count >= 20) { entry.Status = "队列已满 · 可手动重试"; return; }
        entry.Busy = true;
        entry.Status = "排队中";
        entry.Answer = "";
        var index = Entries.IndexOf(entry);
        var history = Entries.Skip(index + 1).Take(2).Reverse().ToArray();
        var request = AnswerAsync(entry, history, _session.Token);
        _pending.Add(request);
        _ = RemoveWhenDoneAsync(request);
    }

    private async Task RemoveWhenDoneAsync(Task request)
    {
        try { await request; }
        finally { _pending.Remove(request); }
    }

    private async Task AnswerAsync(BattleEntry entry, BattleEntry[] history, CancellationToken token)
    {
        var entered = false;
        var conversationEntered = false;
        try
        {
            // Wait for the preceding dialogue answer so the next request includes a complete pair.
            if (!entry.IsScreenshot) { await _conversationRequests.WaitAsync(token); conversationEntered = true; }
            await _requests.WaitAsync(token);
            entered = true;
            token.ThrowIfCancellationRequested();
            var snapshot = _getConfig();
            if (entry.IsScreenshot && !entry.HasExtractedQuestion)
            {
                entry.Status = "正在提取题目…";
                var extraction = ScreenshotQuestion.BuildExtraction(snapshot, entry.ImageBytes!);
                var extracted = await _router.AskAsync(extraction.Config, extraction.Turns, token);
                token.ThrowIfCancellationRequested();
                var question = ScreenshotQuestion.Parse(extracted.Text);
                entry.SetExtractedQuestion(question.Title, question.Markdown);
            }
            var (config, turns) = BattlePrompt.Build(snapshot, entry, history);
            entry.Status = "正在回答…";
            var reply = await _router.AskAsync(config, turns, token);
            token.ThrowIfCancellationRequested();
            entry.Answer = reply.Text;
            entry.Status = string.IsNullOrWhiteSpace(reply.Warning) ? "已完成 · " + reply.ProviderName : "已完成 · " + reply.Warning;
        }
        catch (OperationCanceledException) { entry.Status = "已取消 · 可重试"; }
        catch (Exception ex) { entry.Status = "请求失败 · " + ex.Message; }
        finally
        {
            entry.Busy = false;
            if (entered) _requests.Release();
            if (conversationEntered) _conversationRequests.Release();
        }
    }

    public void CancelRequests()
    {
        _session.Cancel();
        _session.Dispose();
        _session = new CancellationTokenSource();
    }

    private void Clear()
    {
        CancelRequests();
        Entries.Clear();
        Selected = null;
        _transcriptLines.Clear();
        Transcript = "";
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CancelRequests();
        if (_activeScreenshot is not null) await _activeScreenshot;
        await StopCaptureAsync();
        _speech.Transcribed -= OnTranscribed;
        _speech.Failed -= OnFailed;
        await Task.WhenAll(_pending.ToArray());
        await _speech.DisposeAsync();
        _session.Dispose();
    }
}
