using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FloatingDeskAssistant.Application;

public sealed class SpeechSessionArchive : IAsyncDisposable
{
    private const int Rate = 16000;
    private readonly BufferedWaveProvider[] _buffers = [];
    private readonly ISampleProvider[] _sources = [];
    private readonly WaveFileWriter? _audio;
    private readonly StreamWriter _transcript;
    private readonly object _textGate = new();
    private readonly object _stopGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = new();
    private readonly Action<string> _failed;
    private readonly Task _pump;
    private TimeSpan? _stoppedAt;
    private bool _disposed;
    private int _failureReported;
    private long _frames;
    public string AudioPath { get; }
    public string TranscriptPath { get; }
    public DateTimeOffset StartedAt { get; }

    public SpeechSessionArchive(string directory, int microphoneRate, int speakerRate, Action<string> failed, bool recordAudio = true)
    {
        directory = Environment.ExpandEnvironmentVariables((directory ?? "").Trim());
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("请在设置中填写录音记录的完整保存目录。");
        directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim()));
        Directory.CreateDirectory(directory);
        StartedAt = DateTimeOffset.Now;
        AudioPath = "";
        TranscriptPath = "";
        _failed = failed;
        try
        {
            _transcript = CreateTranscript(directory, StartedAt, out var basePath);
            TranscriptPath = basePath + ".md";
            AudioPath = recordAudio ? basePath + ".wav" : "";
            if (recordAudio)
            {
                _buffers = new[] { microphoneRate, speakerRate }.Select(rate => new BufferedWaveProvider(new WaveFormat(rate, 16, 1))
                {
                    BufferDuration = TimeSpan.FromSeconds(5), ReadFully = true, DiscardOnBufferOverflow = false
                }).ToArray();
                _sources = _buffers.Select(buffer => (ISampleProvider)new WdlResamplingSampleProvider(buffer.ToSampleProvider(), Rate)).ToArray();
                _audio = new WaveFileWriter(new FileStream(AudioPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new WaveFormat(Rate, 16, 2));
            }
            var audioInfo = recordAudio ? $"音频：[{Path.GetFileName(AudioPath)}](./{Path.GetFileName(AudioPath)})" : "音频：本次未录制，仅保存文字。";
            _transcript.WriteLine($"# willGo 语音记录\n\n开始时间：{StartedAt:yyyy-MM-dd HH:mm:ss zzz}\n\n{audioInfo}\n\n---\n");
        }
        catch
        {
            _transcript?.Dispose();
            _audio?.Dispose();
            if (_audio is not null) File.Delete(AudioPath);
            if (_transcript is not null) File.Delete(TranscriptPath);
            throw;
        }
        _clock.Start();
        _pump = recordAudio ? Task.Run(WriteAudioAsync) : Task.CompletedTask;
    }

    private static StreamWriter CreateTranscript(string directory, DateTimeOffset startedAt, out string basePath)
    {
        var timestamp = startedAt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        for (var sequence = 1; ; sequence++)
        {
            var suffix = sequence == 1 ? "" : $"-{sequence}";
            basePath = Path.Combine(directory, $"willGo-{timestamp}{suffix}");
            if (File.Exists(basePath + ".wav")) continue;
            FileStream file;
            try { file = new FileStream(basePath + ".md", FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
            catch (IOException) when (File.Exists(basePath + ".md")) { continue; }
            // Reserving the transcript first keeps the paired names unique across concurrent sessions.
            return new StreamWriter(file, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public void AddAudio(SpeechSource source, byte[] pcm, int sampleRate)
    {
        if (_audio is null) return;
        lock (_stopGate)
        {
            if (_stoppedAt is not null || Volatile.Read(ref _failureReported) != 0) return;
            var buffer = _buffers[source == SpeechSource.Microphone ? 0 : 1];
            if (buffer.WaveFormat.SampleRate != sampleRate) throw new InvalidOperationException("录音设备采样率中途改变。");
            buffer.AddSamples(pcm, 0, pcm.Length);
        }
    }

    public void AppendTranscript(SpeechSegment segment)
    {
        if (!segment.IsFinal || string.IsNullOrWhiteSpace(segment.Text)) return;
        lock (_textGate)
        {
            if (_disposed) return;
            try
            {
                var time = (segment.CapturedAt ?? DateTimeOffset.Now).ToLocalTime();
                var who = segment.Source == SpeechSource.Microphone ? "我" : "对方";
                _transcript.WriteLine($"### {time:HH:mm:ss.fff} {who}：\n\n{segment.Text.Trim()}\n");
                if (segment.Warning.Length > 0) _transcript.WriteLine($"> {segment.Warning}\n");
            }
            catch (Exception ex) { ReportFailure(ex); }
        }
    }

    private async Task WriteAudioAsync()
    {
        var audio = _audio!;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            var lastFlush = TimeSpan.Zero;
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                // A short buffer absorbs capture callback jitter; the output clock preserves silence.
                WriteUntil(_clock.Elapsed - TimeSpan.FromMilliseconds(150));
                if (_clock.Elapsed - lastFlush >= TimeSpan.FromSeconds(1)) { audio.Flush(); lastFlush = _clock.Elapsed; }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { ReportFailure(ex); }
        finally
        {
            try
            {
                if (Volatile.Read(ref _failureReported) == 0) WriteUntil(_stoppedAt ?? _clock.Elapsed);
                audio.Dispose();
            }
            catch (Exception ex) { ReportFailure(ex); try { audio.Dispose(); } catch { } }
        }
    }

    private void WriteUntil(TimeSpan elapsed)
    {
        var audio = _audio!;
        var target = Math.Max(0, (long)(elapsed.TotalSeconds * Rate));
        var left = new float[1600];
        var right = new float[1600];
        var bytes = new byte[1600 * 4];
        while (_frames < target)
        {
            var count = (int)Math.Min(1600, target - _frames);
            if (audio.Length + count * 4L > uint.MaxValue - 64L)
                throw new IOException("单次 WAV 录音已达到约 18 小时的文件上限，请重新开启一次语音识别。");
            _sources[0].Read(left, 0, count);
            _sources[1].Read(right, 0, count);
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 4), ToPcm(left[i]));
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 4 + 2), ToPcm(right[i]));
            }
            audio.Write(bytes, 0, count * 4);
            _frames += count;
        }
    }

    private static short ToPcm(float value) => (short)Math.Round((float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f) * 32767);
    private void ReportFailure(Exception error)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) == 0) _failed("录音或文字记录保存失败：" + error.Message);
    }

    public Task StopAudioAsync()
    {
        lock (_stopGate)
        {
            if (_stoppedAt is null) { _stoppedAt = _clock.Elapsed; _stop.Cancel(); }
            return _pump;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAudioAsync().ConfigureAwait(false);
        lock (_textGate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _transcript.WriteLine($"---\n\n结束时间：{StartedAt + _stoppedAt:yyyy-MM-dd HH:mm:ss zzz}\n\n会话时长：{_stoppedAt:hh\\:mm\\:ss}。\n");
                _transcript.Dispose();
            }
            catch (Exception ex) { ReportFailure(ex); try { _transcript.Dispose(); } catch { } }
        }
        _stop.Dispose();
    }
}
