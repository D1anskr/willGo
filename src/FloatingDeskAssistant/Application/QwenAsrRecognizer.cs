using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FloatingDeskAssistant.Application;

public sealed class QwenAsrRecognizer : IUtteranceRecognizer, IAsyncDisposable
{
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "SpeechModels", "Qwen3-ASR-1.7B");
    private readonly string _modelPath;
    private readonly string _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Task? _stderr;
    private bool _disposed;
    public string Device { get; private set; } = "";

    public QwenAsrRecognizer(string context = "", string? modelPath = null)
    {
        _modelPath = modelPath ?? ModelPath;
        _context = string.Join("、", SpeechHotwords.Parse(context));
        foreach (var file in new[] { "speech.exe", "Qwen3-ASR-1.7B-Q8_0.gguf" })
            if (!File.Exists(Path.Combine(_modelPath, file)) || new FileInfo(Path.Combine(_modelPath, file)).Length == 0)
                throw new FileNotFoundException("Qwen3-ASR 整句模型不完整，请重新安装完整版本。缺少：" + file);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try { await EnsureStartedAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                await ResetAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Qwen3-ASR 模型加载超时，请检查显卡驱动或关闭高负载程序。");
            }
            catch { await ResetAsync().ConfigureAwait(false); throw; }
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        if (_process is { HasExited: false }) return;
        await ResetAsync().ConfigureAwait(false);
        var start = new ProcessStartInfo(Path.Combine(_modelPath, "speech.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _modelPath,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "worker", Path.Combine(_modelPath, "Qwen3-ASR-1.7B-Q8_0.gguf"), "--device", "auto" })
            start.ArgumentList.Add(arg);
        _process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Qwen3-ASR 引擎。");
        // Drain diagnostics without retaining audio or recognized text in application logs.
        var stderr = _process.StandardError;
        _stderr = Task.Run(async () => { var buffer = new char[4096]; while (await stderr.ReadAsync(buffer).ConfigureAwait(false) > 0) { } });
        using var ready = await ReadAsync(token).ConfigureAwait(false);
        var root = ready.RootElement;
        if (root.GetProperty("type").GetString() != "ready" || root.GetProperty("protocol").GetInt32() != 3 ||
            root.GetProperty("version").GetString() != "0.8.10" || root.GetProperty("model").GetProperty("name").GetString() != "Qwen3-ASR-1.7B")
            throw new InvalidDataException("Qwen3-ASR 引擎启动失败或版本不兼容。");
        Device = root.GetProperty("model").GetProperty("device").GetString() ?? "";
    }

    public async Task<string> RecognizeAsync(float[] audio, int sampleRate, CancellationToken cancellationToken)
    {
        if (sampleRate is < 8000 or > 192000 || audio.Length == 0 || audio.Length > sampleRate * 30L)
            throw new ArgumentException("语音片段长度或采样率无效。");
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var id = Guid.NewGuid().ToString("N");
                var seq = 0;
                for (var offset = 0; offset < audio.Length; offset += 16000)
                {
                    var count = Math.Min(16000, audio.Length - offset);
                    var pcm = new byte[count * 2];
                    for (var i = 0; i < count; i++)
                    {
                        var value = audio[offset + i];
                        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2),
                            (short)Math.Round((float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f) * 32767));
                    }
                    await SendAsync(new { type = "chunk", id, seq = seq++, pcm = Convert.ToBase64String(pcm) }, timeout.Token).ConfigureAwait(false);
                }
                await SendAsync(new { type = "transcribe", id, sample_rate = sampleRate, prompt = _context }, timeout.Token).ConfigureAwait(false);
                while (true)
                {
                    using var message = await ReadAsync(timeout.Token).ConfigureAwait(false);
                    var root = message.RootElement;
                    if (!root.TryGetProperty("id", out var replyId) || replyId.GetString() != id)
                        throw new InvalidDataException("Qwen3-ASR 返回了不匹配的语音请求。");
                    var type = root.GetProperty("type").GetString();
                    if (type == "progress") continue;
                    if (type != "end" || root.GetProperty("stop").GetString() != "complete")
                        throw new InvalidDataException("Qwen3-ASR 未能完整识别本句。");
                    var text = root.GetProperty("text").GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Qwen3-ASR 未返回有效文字。");
                    return text;
                }
            }
            catch (OperationCanceledException)
            {
                await ResetAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Qwen3-ASR 整句识别超时。");
            }
            catch { await ResetAsync().ConfigureAwait(false); throw; }
        }
        finally { _gate.Release(); }
    }

    private async Task SendAsync<T>(T value, CancellationToken token)
    {
        await _process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ReadAsync(CancellationToken token)
    {
        var line = await _process!.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
        if (line is null) throw new IOException("Qwen3-ASR 引擎意外退出。");
        return JsonDocument.Parse(line);
    }

    private async Task ResetAsync()
    {
        var process = _process;
        _process = null;
        Device = "";
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (_stderr is not null) await _stderr.ConfigureAwait(false);
        }
        finally { _stderr = null; process.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; await ResetAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
