using System.Threading.Channels;
using FloatingDeskAssistant.Configuration;

namespace FloatingDeskAssistant.Application;

public sealed class ModeAwareBattleSpeechService(DualMachineService peer, Func<AppConfig> getConfig) : IBattleSpeechService
{
    private IBattleSpeechService? _active;
    public event Action<SpeechSegment>? Transcribed;
    public event Action<string>? Failed;

    public async Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null)
    {
        if (_active is not null) throw new InvalidOperationException("语音采集已经启动。");
        var config = getConfig();
        if (config.DualMachine.IsData) throw new InvalidOperationException("数据端由显示端控制采集。");
        _active = config.DualMachine.IsDisplay
            ? new NetworkBattleSpeechService(peer, () => getConfig().Battle)
            : new BattleSpeechService(() => getConfig().Battle);
        _active.Transcribed += ForwardTranscript;
        _active.Failed += ForwardFailure;
        try { await _active.StartAsync(microphoneId, speakerId, cancellationToken, recordAudio); }
        catch { await StopAsync(); throw; }
    }

    private void ForwardTranscript(SpeechSegment segment) => Transcribed?.Invoke(segment);
    private void ForwardFailure(string message) => Failed?.Invoke(message);
    public async Task StopAsync()
    {
        var active = _active;
        _active = null;
        if (active is null) return;
        try { await active.StopAsync(); }
        finally
        {
            active.Transcribed -= ForwardTranscript;
            active.Failed -= ForwardFailure;
            await active.DisposeAsync();
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed class NetworkBattleSpeechService : IBattleSpeechService
{
    private readonly DualMachineService _peer;
    private readonly Func<BattleConfig> _getOptions;
    private readonly Uri? _endpoint;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Track[] _tracks = [];
    private SpeechSessionArchive? _archive;
    private SpeechEchoFilter _echo = new();
    private System.Threading.Timer? _timer;
    private Guid _session;
    private bool _accepting;
    private int _failed;
    public event Action<SpeechSegment>? Transcribed;
    public event Action<string>? Failed;

    public NetworkBattleSpeechService(DualMachineService peer, Func<BattleConfig> getOptions, Uri? endpoint = null)
    {
        _peer = peer; _getOptions = getOptions; _endpoint = endpoint;
    }

    public async Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session != Guid.Empty) throw new InvalidOperationException("双机语音已启动。");
            if (!_peer.IsDisplay || !_peer.IsConnected) throw new InvalidOperationException("请先在双机设置中连接数据端。");
            var options = _getOptions().Clone();
            options.Speech.Validate();
            _session = Guid.NewGuid();
            _failed = 0;
            _echo = new(options.SuppressSpeakerEcho);
            _tracks = [new(options, SpeechSource.Microphone, Publish, Fail, _endpoint), new(options, SpeechSource.Speaker, Publish, Fail, _endpoint)];
            await Task.WhenAll(_tracks.Select(track => track.Client.StartAsync(cancellationToken))).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (recordAudio.HasValue || options.SaveSpeechSessions)
                _archive = new(options.SpeechArchiveDirectory, 16000, 16000, Fail, recordAudio ?? true);
            _peer.AudioReceived += ReceiveAudio;
            _peer.AudioFailed += ReceiveFailure;
            _accepting = true;
            foreach (var track in _tracks) track.Begin();
            await _peer.StartAudioAsync(_session, cancellationToken).ConfigureAwait(false);
            _timer = new(_ => Drain(), null, 150, 150);
        }
        catch { await StopCoreAsync().ConfigureAwait(false); throw; }
        finally { _lifecycle.Release(); }
    }

    private void ReceiveAudio(PeerAudio audio)
    {
        try
        {
            lock (_gate)
            {
                if (!_accepting || audio.Session != _session) return;
                var track = _tracks[audio.Source == SpeechSource.Microphone ? 0 : 1];
                if (audio.Sequence != track.Sequence + 1) throw new InvalidOperationException("双机音频顺序异常，已停止本次采集，请重新开始。");
                track.Sequence = audio.Sequence;
                _archive?.AddAudio(audio.Source, audio.Pcm, 16000);
                if (!track.Queue.Writer.TryWrite(audio.Pcm)) throw new InvalidOperationException("显示端语音队列已满，请检查云端连接后重新开始。");
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private void ReceiveFailure(Guid session, string message) { if (session == Guid.Empty || session == _session) Fail(message); }
    private void Fail(string message) { if (Interlocked.Exchange(ref _failed, 1) == 0) Failed?.Invoke(message); }

    private void Publish(SpeechSegment segment)
    {
        lock (_gate)
            foreach (var accepted in _echo.Accept(segment, DateTimeOffset.UtcNow)) PublishAccepted(accepted);
    }
    private void PublishAccepted(SpeechSegment segment) { _archive?.AppendTranscript(segment); Transcribed?.Invoke(segment); }
    private void Drain(bool finish = false)
    {
        lock (_gate)
            foreach (var accepted in _echo.Drain(DateTimeOffset.UtcNow, finish)) PublishAccepted(accepted);
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_session == Guid.Empty) return;
        try
        {
            if (_peer.IsConnected && _accepting)
            {
                try { await _peer.StopAudioAsync(_session).ConfigureAwait(false); }
                catch (Exception) { _peer.AbortConnection(); Fail("停止数据端采集时连接中断，已断开连接以确保采集结束。"); }
            }
            lock (_gate) _accepting = false;
            _peer.AudioReceived -= ReceiveAudio;
            _peer.AudioFailed -= ReceiveFailure;
            foreach (var track in _tracks) track.Queue.Writer.TryComplete();
            if (_archive is not null) await _archive.StopAudioAsync().ConfigureAwait(false);
            await Task.WhenAll(_tracks.Select(track => track.StopAsync())).ConfigureAwait(false);
        }
        finally
        {
            if (_timer is not null) { await _timer.DisposeAsync().ConfigureAwait(false); _timer = null; }
            Drain(true);
            if (_archive is not null) { await _archive.DisposeAsync().ConfigureAwait(false); _archive = null; }
            _tracks = []; _session = Guid.Empty;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed class Track
    {
        public DoubaoSpeechClient Client { get; }
        public Channel<byte[]> Queue { get; } = Channel.CreateBounded<byte[]>(100);
        public long Sequence { get; set; }
        private readonly Action<string> _failed;
        private readonly CancellationTokenSource _stop = new();
        private Task _pump = Task.CompletedTask;
        public Track(BattleConfig options, SpeechSource source, Action<SpeechSegment> publish, Action<string> failed, Uri? endpoint)
        { Client = new(options, source, publish, failed, endpoint); _failed = failed; }
        public void Begin() { Client.BeginAudio(); _pump = PumpAsync(); }
        private async Task PumpAsync()
        {
            try
            {
                await foreach (var pcm in Queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                    await Client.SendAudioAsync(pcm, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception) { _failed("显示端向豆包发送音频失败，请检查网络后重新开始。"); }
            finally { await Client.CompleteAsync().ConfigureAwait(false); }
        }
        public async Task StopAsync()
        {
            _stop.CancelAfter(TimeSpan.FromSeconds(12));
            try { await _pump.ConfigureAwait(false); }
            finally { await Client.DisposeAsync().ConfigureAwait(false); _stop.Dispose(); }
        }
    }
}
