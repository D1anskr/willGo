using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using FloatingDeskAssistant.Configuration;

namespace FloatingDeskAssistant.Application;

public enum SpeechSource { Microphone, Speaker }
public sealed record SpeechSegment(SpeechSource Source, string Text, bool IsFinal, string Warning = "", long Sequence = 0, DateTimeOffset? CapturedAt = null);
public sealed record AudioDeviceOption(string Id, string Name);

public interface IBattleSpeechService : IAsyncDisposable
{
    event Action<SpeechSegment>? Transcribed;
    event Action<string>? Failed;
    Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null);
    Task StopAsync();
}

public sealed class BattleSpeechService : IBattleSpeechService
{
    private AudioWorker? _microphone;
    private AudioWorker? _speaker;
    private SpeechSessionArchive? _archive;
    private int _failureReported;
    private readonly Func<BattleConfig> _getOptions;
    private readonly Uri? _endpoint;
    private readonly object _publishGate = new();
    private SpeechEchoFilter _echoFilter = new();
    private System.Threading.Timer? _echoTimer;
    public BattleSpeechService(Func<BattleConfig>? getOptions = null, Uri? endpoint = null)
    {
        _getOptions = getOptions ?? (() => new BattleConfig());
        _endpoint = endpoint;
    }
    public event Action<SpeechSegment>? Transcribed;
    public event Action<string>? Failed;

    public static IReadOnlyList<AudioDeviceOption> GetDevices(DataFlow flow)
    {
        var result = new List<AudioDeviceOption> { new("", "跟随系统默认设备") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device) result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    public async Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null)
    {
        if (_microphone is not null || _speaker is not null) throw new InvalidOperationException("采集已经启动。");
        var options = _getOptions().Clone();
        options.Speech.Validate();
        lock (_publishGate) _echoFilter = new(options.SuppressSpeakerEcho);
        _failureReported = 0;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var mic = string.IsNullOrEmpty(microphoneId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                : enumerator.GetDevice(microphoneId);
            try { _microphone = new AudioWorker(new WasapiCapture(mic), mic, options, SpeechSource.Microphone, Publish, ReportFailure, RecordAudio, _endpoint); }
            catch { mic.Dispose(); throw; }
            var speaker = string.IsNullOrEmpty(speakerId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(speakerId);
            try { _speaker = new AudioWorker(new WasapiLoopbackCapture(speaker), speaker, options, SpeechSource.Speaker, Publish, ReportFailure, RecordAudio, _endpoint); }
            catch { speaker.Dispose(); throw; }
            await Task.WhenAll(_microphone.ConnectAsync(cancellationToken), _speaker.ConnectAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (recordAudio.HasValue || options.SaveSpeechSessions)
                _archive = new(options.SpeechArchiveDirectory, _microphone.SampleRate, _speaker.SampleRate, ReportFailure, recordAudio ?? true);
            _microphone.Start();
            _speaker.Start();
            _echoTimer = new System.Threading.Timer(_ => FlushTranscripts(), null, 150, 150);
        }
        catch { await StopAsync(); throw; }
    }

    private void RecordAudio(SpeechSource source, byte[] pcm, int rate) => _archive?.AddAudio(source, pcm, rate);
    private void Publish(SpeechSegment segment)
    {
        lock (_publishGate)
            foreach (var accepted in _echoFilter.Accept(segment, DateTimeOffset.UtcNow)) PublishAccepted(accepted);
    }
    private void PublishAccepted(SpeechSegment segment)
    {
        _archive?.AppendTranscript(segment);
        Transcribed?.Invoke(segment);
    }
    private void FlushTranscripts(bool finish = false)
    {
        lock (_publishGate)
            foreach (var accepted in _echoFilter.Drain(DateTimeOffset.UtcNow, finish)) PublishAccepted(accepted);
    }
    private void ReportFailure(string message)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) == 0) Failed?.Invoke(message);
    }

    public async Task StopAsync()
    {
        var mic = _microphone;
        var speaker = _speaker;
        _microphone = null;
        _speaker = null;
        try
        {
            var stopped = Task.WhenAll(mic?.StopAsync() ?? Task.CompletedTask, speaker?.StopAsync() ?? Task.CompletedTask);
            if (_archive is not null) await _archive.StopAudioAsync();
            await stopped;
        }
        finally
        {
            if (_echoTimer is not null) { await _echoTimer.DisposeAsync(); _echoTimer = null; }
            FlushTranscripts(true);
            if (_archive is not null) { await _archive.DisposeAsync(); _archive = null; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    // Device callbacks never wait on the network. A full queue stops capture visibly instead of losing audio.
    private sealed class AudioWorker
    {
        private readonly WasapiCapture _capture;
        private readonly MMDevice _device;
        private readonly DoubaoSpeechClient _recognizer;
        private readonly SpeechSource _source;
        private readonly Action<string> _failed;
        private readonly Channel<byte[]> _audio = Channel.CreateBounded<byte[]>(100);
        private readonly object _gate = new();
        private Task _pump = Task.CompletedTask;
        private readonly CancellationTokenSource _stopPump = new();
        private readonly BufferedWaveProvider _buffer;
        private readonly ISampleProvider _resampler;
        private readonly System.Threading.Timer _silence;
        private long _lastAudio = Stopwatch.GetTimestamp();
        private bool _stopping;
        private bool _failureReported;
        private readonly Action<SpeechSource, byte[], int> _recordAudio;
        public int SampleRate => _capture.WaveFormat.SampleRate;

        public AudioWorker(WasapiCapture capture, MMDevice device, BattleConfig options, SpeechSource source,
            Action<SpeechSegment> publish, Action<string> failed, Action<SpeechSource, byte[], int> recordAudio, Uri? endpoint)
        {
            _capture = capture;
            _device = device;
            _source = source;
            _failed = failed;
            _recordAudio = recordAudio;
            _recognizer = new(options, source, publish, Fail, endpoint);
            _buffer = new(new WaveFormat(SampleRate, 16, 1)) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(5) };
            _resampler = SampleRate == 16000 ? _buffer.ToSampleProvider() : new WdlResamplingSampleProvider(_buffer.ToSampleProvider(), 16000);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (!_stopping) Fail(e.Exception?.Message ?? "音频设备已停止或断开，请重新开始采集。");
            };
            _silence = new System.Threading.Timer(OnSilence, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task ConnectAsync(CancellationToken token) => _recognizer.StartAsync(token);

        public void Start()
        {
            _recognizer.BeginAudio();
            _pump = Task.Run(PumpAsync);
            _capture.StartRecording();
            _silence.Change(100, 100);
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            try
            {
                var pcm = AudioPcm16Converter.Convert(e.Buffer.AsSpan(0, e.BytesRecorded), _capture.WaveFormat);
                lock (_gate)
                {
                    if (_stopping) return;
                    _recordAudio(_source, pcm, SampleRate);
                    _lastAudio = Stopwatch.GetTimestamp();
                    if (!_audio.Writer.TryWrite(pcm)) Fail("语音处理速度不足，音频队列已满；采集已暂停，请关闭高负载程序后重试。");
                }
            }
            catch (Exception ex) { Fail("音频格式处理失败：" + ex.Message); }
        }

        private void OnSilence(object? state)
        {
            lock (_gate)
            {
                if (_stopping || Stopwatch.GetElapsedTime(_lastAudio).TotalMilliseconds < 150) return;
                // Loopback emits no callbacks when playback stops; keep the remote audio clock and VAD running.
                if (!_audio.Writer.TryWrite(new byte[_capture.WaveFormat.SampleRate / 10 * 2]))
                    Fail("语音处理队列已满，采集已暂停。");
            }
        }

        private async Task PumpAsync()
        {
            try
            {
                var samples = new float[3200];
                var packet = new byte[6400];
                var pending = 0;
                await foreach (var pcm in _audio.Reader.ReadAllAsync())
                {
                    _buffer.AddSamples(pcm, 0, pcm.Length);
                    int count;
                    while ((count = _resampler.Read(samples, 0, samples.Length)) > 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(pending), (short)Math.Clamp(Math.Round(samples[i] * 32768), short.MinValue, short.MaxValue));
                            pending += 2;
                            if (pending == packet.Length)
                            {
                                await _recognizer.SendAudioAsync(packet, _stopPump.Token).ConfigureAwait(false);
                                pending = 0;
                            }
                        }
                    }
                }
                if (pending > 0) await _recognizer.SendAudioAsync(packet[..pending], _stopPump.Token).ConfigureAwait(false);
            }
            catch (Exception) { Fail("豆包语音音频发送失败或网络拥塞，请检查网络后重新开启识别。"); }
            finally { await _recognizer.CompleteAsync().ConfigureAwait(false); }
        }

        private void Fail(string message)
        {
            lock (_gate)
            {
                if (_failureReported) return;
                _failureReported = true;
            }
            _failed((_source == SpeechSource.Microphone ? "麦克风：" : "系统播放：") + message);
        }

        public async Task StopAsync()
        {
            lock (_gate) _stopping = true;
            await _silence.DisposeAsync();
            try { _capture.StopRecording(); }
            finally
            {
                _capture.DataAvailable -= OnData;
                _capture.Dispose();
                _audio.Writer.TryComplete();
                _stopPump.CancelAfter(TimeSpan.FromSeconds(12));
                try { await _pump.ConfigureAwait(false); }
                finally
                {
                    await _recognizer.DisposeAsync().ConfigureAwait(false);
                    _stopPump.Dispose();
                    _device.Dispose();
                }
            }
        }
    }
}

public static class AudioPcm16Converter
{
    public static byte[] Convert(ReadOnlySpan<byte> input, WaveFormat format)
    {
        var floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        if (format.Channels < 1 || format.BitsPerSample is not (16 or 24 or 32) || floating && format.BitsPerSample != 32)
            throw new NotSupportedException(format.ToString());
        var frames = input.Length / format.BlockAlign;
        var output = new byte[frames * 2];
        var bytesPerSample = format.BitsPerSample / 8;
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sample = input.Slice(frame * format.BlockAlign + channel * bytesPerSample, bytesPerSample);
                sum += floating ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample)) : bytesPerSample switch
                {
                    2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                    3 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608.0,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0
                };
            }
            var value = double.IsFinite(sum) ? Math.Clamp(sum / format.Channels, -1, 1) : 0;
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(frame * 2), (short)Math.Clamp(value * 32768, short.MinValue, short.MaxValue));
        }
        return output;
    }
}
