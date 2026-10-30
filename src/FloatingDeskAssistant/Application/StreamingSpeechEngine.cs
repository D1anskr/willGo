using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SherpaOnnx;
using FloatingDeskAssistant.Configuration;

namespace FloatingDeskAssistant.Application;

public sealed record StreamingSpeechResult(string Text, bool IsFinal, float[]? Audio = null, int SampleRate = 16000, DateTimeOffset? CapturedAt = null);

public sealed class StreamingSpeechEngine : IDisposable
{
    public const string ModelDirectory = "sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05";
    private readonly OnlineRecognizer _recognizer;
    private readonly object _gate = new();
    private readonly string? _hotwordsPath;

    public StreamingSpeechEngine(string modelPath, BattleConfig? options = null)
    {
        options = (options ?? new BattleConfig()).Clone();
        foreach (var file in new[] { "encoder.int8.onnx", "decoder.onnx", "joiner.int8.onnx", "tokens.txt" })
            if (!File.Exists(Path.Combine(modelPath, file)) || new FileInfo(Path.Combine(modelPath, file)).Length == 0)
                throw new FileNotFoundException("中文语音模型不完整，请重新安装完整的 willGo 安装包。缺少：" + file);
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelPath, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelPath, "decoder.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelPath, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelPath, "tokens.txt");
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Provider = "cpu";
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = Math.Max(2.4f, (float)options.SpeechPauseSeconds + 0.5f);
        config.Rule2MinTrailingSilence = (float)options.SpeechPauseSeconds;
        config.Rule3MinUtteranceLength = 25;
        if (!string.IsNullOrWhiteSpace(options.SpeechHotwords))
        {
            config.DecodingMethod = "modified_beam_search";
            config.ModelConfig.ModelingUnit = "bpe";
            config.ModelConfig.BpeVocab = Path.Combine(AppContext.BaseDirectory, "Assets", "x-asr-bpe.vocab");
            if (!File.Exists(config.ModelConfig.BpeVocab)) throw new FileNotFoundException("缺少语音热词词表，请重新安装完整版本。", config.ModelConfig.BpeVocab);
            _hotwordsPath = Path.Combine(Path.GetTempPath(), "willgo-hotwords-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(_hotwordsPath, SpeechHotwords.Parse(options.SpeechHotwords), new System.Text.UTF8Encoding(false));
            config.HotwordsFile = _hotwordsPath;
            config.HotwordsScore = 1.5f;
        }
        try { _recognizer = new OnlineRecognizer(config); }
        finally { if (_hotwordsPath is not null) File.Delete(_hotwordsPath); }
        if (NativeHandle(_recognizer).Handle == IntPtr.Zero)
        {
            _recognizer.Dispose();
            throw new InvalidOperationException("中文语音模型加载失败，请检查可用内存或重新安装模型。");
        }
    }

    // The pinned 1.13.8 binding does not throw if native recognizer creation returns null.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handle")]
    private static extern ref HandleRef NativeHandle(OnlineRecognizer recognizer);

    public Session CreateSession()
    {
        lock (_gate)
        {
            var stream = _recognizer.CreateStream();
            if (stream.Handle != IntPtr.Zero) return new Session(this, stream);
            stream.Dispose();
            throw new InvalidOperationException("无法创建语音识别流。");
        }
    }

    public void Dispose() { lock (_gate) _recognizer.Dispose(); }

    public sealed class Session : IDisposable
    {
        private readonly StreamingSpeechEngine _engine;
        private readonly OnlineStream _stream;
        private string _partial = "";
        private bool _finished;
        private readonly List<float> _audio = [];
        private int _sampleRate;
        internal Session(StreamingSpeechEngine engine, OnlineStream stream) { _engine = engine; _stream = stream; }

        public IReadOnlyList<StreamingSpeechResult> Accept(int sampleRate, float[] samples, bool finish = false)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            lock (_engine._gate)
            {
                if (_finished) throw new InvalidOperationException("语音识别流已经结束。");
                if (_sampleRate != 0 && _sampleRate != sampleRate) throw new InvalidOperationException("识别流的采样率不能中途改变。");
                _sampleRate = sampleRate;
                var results = new List<StreamingSpeechResult>();
                var recognizer = _engine._recognizer;
                var chunkSize = sampleRate / 10;
                for (var offset = 0; offset < samples.Length; offset += chunkSize)
                {
                    var chunk = samples.AsSpan(offset, Math.Min(chunkSize, samples.Length - offset)).ToArray();
                    _audio.AddRange(chunk);
                    _stream.AcceptWaveform(sampleRate, chunk);
                    Decode();
                    if (recognizer.IsEndpoint(_stream))
                    {
                        Emit(true);
                        recognizer.Reset(_stream);
                    }
                    else Emit(false);
                }
                if (finish)
                {
                    _stream.AcceptWaveform(sampleRate, new float[sampleRate]);
                    _stream.InputFinished();
                    Decode();
                    Emit(true);
                    _finished = true;
                }
                return results;

                void Decode() { while (recognizer.IsReady(_stream)) recognizer.Decode(_stream); }
                void Emit(bool final)
                {
                    var text = recognizer.GetResult(_stream).Text.Trim();
                    if (!final && text == _partial) return;
                    _partial = final ? "" : text;
                    if (!final || text.Length > 0) results.Add(new(text, final, final ? _audio.ToArray() : null, sampleRate));
                    if (final) _audio.Clear();
                }
            }
        }

        public void Dispose() { lock (_engine._gate) _stream.Dispose(); }
    }
}
