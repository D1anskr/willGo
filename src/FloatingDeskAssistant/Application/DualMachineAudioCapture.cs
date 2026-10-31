using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FloatingDeskAssistant.Application;

public interface IDualMachineCapture : IAsyncDisposable
{
    void Start();
}

public sealed class DualMachineAudioCapture : IDualMachineCapture
{
    private readonly CaptureTrack _microphone;
    private readonly CaptureTrack _speaker;

    public DualMachineAudioCapture(string microphoneId, string speakerId, Action<SpeechSource, long, byte[]> send, Action<string> failed)
    {
        _microphone = new(microphoneId, SpeechSource.Microphone, send, failed);
        try { _speaker = new(speakerId, SpeechSource.Speaker, send, failed); }
        catch { _microphone.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }

    public void Start() { _microphone.Start(); _speaker.Start(); }
    public async ValueTask DisposeAsync() => await Task.WhenAll(_microphone.DisposeAsync().AsTask(), _speaker.DisposeAsync().AsTask()).ConfigureAwait(false);

    private sealed class CaptureTrack : IAsyncDisposable
    {
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(100);
        private readonly Action<SpeechSource, long, byte[]> _send;
        private readonly Action<string> _failed;
        private readonly SpeechSource _source;
        private readonly System.Threading.Timer _silence;
        private readonly object _gate = new();
        private bool _stopped;
        private long _lastAudio = Stopwatch.GetTimestamp();
        private Task _pump = Task.CompletedTask;
        private int _failedOnce;

        public CaptureTrack(string id, SpeechSource source, Action<SpeechSource, long, byte[]> send, Action<string> failed)
        {
            _source = source; _send = send; _failed = failed;
            using var devices = new MMDeviceEnumerator();
            _device = string.IsNullOrEmpty(id) ? devices.GetDefaultAudioEndpoint(source == SpeechSource.Microphone ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : devices.GetDevice(id);
            try { _capture = source == SpeechSource.Microphone ? new WasapiCapture(_device) : new WasapiLoopbackCapture(_device); }
            catch { _device.Dispose(); throw; }
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) => { if (!_stopped) Fail(e.Exception?.Message ?? "数据端音频设备已停止，请重新开始战斗。"); };
            _silence = new(OnSilence, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
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
                    if (_stopped) return;
                    _lastAudio = Stopwatch.GetTimestamp();
                    if (!_queue.Writer.TryWrite(pcm)) Fail("数据端音频队列已满，请检查局域网或电脑负载。");
                }
            }
            catch (Exception ex) { Fail("数据端音频转换失败：" + ex.Message); }
        }

        private void OnSilence(object? state)
        {
            lock (_gate)
            {
                if (_stopped || Stopwatch.GetElapsedTime(_lastAudio).TotalMilliseconds < 150) return;
                if (!_queue.Writer.TryWrite(new byte[_capture.WaveFormat.SampleRate / 10 * 2])) Fail("数据端静音队列已满。");
            }
        }

        private async Task PumpAsync()
        {
            try
            {
                var rate = _capture.WaveFormat.SampleRate;
                var buffer = new BufferedWaveProvider(new WaveFormat(rate, 16, 1)) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(5) };
                ISampleProvider resampler = rate == 16000 ? buffer.ToSampleProvider() : new WdlResamplingSampleProvider(buffer.ToSampleProvider(), 16000);
                var samples = new float[1600];
                var packet = new byte[3200];
                var pending = 0;
                long sequence = 0;
                await foreach (var pcm in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    buffer.AddSamples(pcm, 0, pcm.Length);
                    int count;
                    while ((count = resampler.Read(samples, 0, samples.Length)) > 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(pending), (short)Math.Clamp(Math.Round(samples[i] * 32768), short.MinValue, short.MaxValue));
                            pending += 2;
                            if (pending != packet.Length) continue;
                            _send(_source, ++sequence, packet.ToArray());
                            pending = 0;
                        }
                    }
                }
                if (pending > 0) _send(_source, ++sequence, packet[..pending]);
            }
            catch (Exception ex) { Fail("数据端音频传输失败：" + ex.Message); }
        }

        private void Fail(string message) { if (Interlocked.Exchange(ref _failedOnce, 1) == 0) _failed(message); }

        public async ValueTask DisposeAsync()
        {
            lock (_gate) { if (_stopped) return; _stopped = true; }
            await _silence.DisposeAsync().ConfigureAwait(false);
            try { _capture.StopRecording(); }
            finally
            {
                _capture.DataAvailable -= OnData;
                _queue.Writer.TryComplete();
                await _pump.ConfigureAwait(false);
                _capture.Dispose();
                _device.Dispose();
            }
        }
    }
}
