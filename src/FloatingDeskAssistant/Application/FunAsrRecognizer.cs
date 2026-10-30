using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FloatingDeskAssistant.Application;

public interface IUtteranceRecognizer
{
    Task<string> RecognizeAsync(float[] audio, int sampleRate, CancellationToken cancellationToken);
}

public sealed class FunAsrRecognizer : IUtteranceRecognizer
{
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "SpeechModels", "Fun-ASR-Nano-GGUF");
    private readonly string _modelPath;
    public FunAsrRecognizer(string? modelPath = null)
    {
        _modelPath = modelPath ?? ModelPath;
        foreach (var file in new[] { "llama-funasr-cli.exe", "funasr-encoder-f16.gguf", "qwen3-0.6b-q8_0.gguf" })
            if (!File.Exists(Path.Combine(_modelPath, file)) || new FileInfo(Path.Combine(_modelPath, file)).Length == 0)
                throw new FileNotFoundException("整句语音模型不完整，请重新安装完整版本，或在设置中关闭整句精确识别。缺少：" + file);
    }

    public async Task<string> RecognizeAsync(float[] audio, int sampleRate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sampleRate < 8000 || audio.Length == 0 || audio.Length > sampleRate * 30L) throw new ArgumentException("语音片段长度或采样率无效。");
        var wavPath = Path.Combine(Path.GetTempPath(), "willgo-speech-" + Guid.NewGuid().ToString("N") + ".wav");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // The upstream portable CLI accepts a WAV path. Keep only a temporary file for this request.
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)) { BufferLength = audio.Length * sizeof(float) };
            var bytes = new byte[audio.Length * sizeof(float)];
            Buffer.BlockCopy(audio, 0, bytes, 0, bytes.Length);
            provider.AddSamples(bytes, 0, bytes.Length);
            provider.ReadFully = false;
            using (var writer = new WaveFileWriter(wavPath, new WaveFormat(16000, 16, 1)))
            {
                var resampled = new WdlResamplingSampleProvider(provider.ToSampleProvider(), 16000).ToWaveProvider16();
                var buffer = new byte[32000];
                int read;
                while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0) writer.Write(buffer, 0, read);
            }
            var start = new ProcessStartInfo(Path.Combine(_modelPath, "llama-funasr-cli.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = _modelPath
            };
            foreach (var arg in new[] { "--enc", Path.Combine(_modelPath, "funasr-encoder-f16.gguf"), "-m", Path.Combine(_modelPath, "qwen3-0.6b-q8_0.gguf"), "-a", wavPath, "--chunk", "15", "-n", "512" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动整句识别引擎。");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("整句精确识别超时。");
            }
            var text = (await output.ConfigureAwait(false)).Trim();
            await errors.ConfigureAwait(false);
            if (process.ExitCode != 0 || text.Length == 0) throw new InvalidOperationException("整句识别未返回有效文字。");
            return text;
        }
        finally { if (File.Exists(wavPath)) File.Delete(wavPath); }
    }
}
