using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;

internal static class SpeechUpgradeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new BattleConfig { SpeechHotwords = "ZGC\nJVM\nGC\nRAG", SpeechPauseSeconds = double.NaN };
        var clone = options.Clone(); clone.SpeechHotwords = "changed";
        check(options.SpeechHotwords.Contains("ZGC") && clone.SpeechPauseSeconds == 1.1, "speech options clone independently and normalize invalid pause values");
        clone.SpeechRefinerContext = "changed";
        check(options.SpeechRefinerContext.Contains("线程池") && !options.SpeechRefinerContext.Contains("ZGC"), "final recognition has independent domain context without forced acronym completion");
        check(SpeechHotwords.Parse("ZGC\nzgc\nJVM\n bad : injection ").SequenceEqual(new[] { "ZGC", "JVM", "bad  injection" }), "hotwords are bounded, deduplicated and exclude decoder directives");
        check(SpeechHotwords.NormalizeAcronyms("什么是 Z G C，J V M 和 GC？", options.SpeechHotwords) == "什么是 ZGC，JVM 和 GC？", "exactly spoken acronym letters are normalized");
        check(SpeechHotwords.NormalizeAcronyms("什么是 Z，什么是 ZG，AZGC 和 ZGCX", options.SpeechHotwords) == "什么是 Z，什么是 ZG，AZGC 和 ZGCX", "normalization never invents missing letters or changes a longer word");
        const string terms = "Kafka\nRedis\nMySQL\nZGC\nC++\n.NET";
        const string kafka = "我想想什么是kafka";
        check(SpeechHotwords.SelectFinalText(kafka, "我想想什么是卡夫卡？", terms) == kafka, "refinement cannot transliterate an already recognized Kafka question");
        check(SpeechHotwords.SelectFinalText("KAFKA 和 Redis 有什么区别", "Kafka 和雷迪斯有什么区别？", terms) == "KAFKA 和 Redis 有什么区别", "case-insensitive protection covers every technical term in a sentence");
        check(SpeechHotwords.SelectFinalText("kafka 与 kafka", "Kafka 与卡夫卡", terms) == "kafka 与 kafka", "a repeated English term cannot be partially transliterated");
        check(SpeechHotwords.SelectFinalText("KafkaClient 和 _Kafka 怎么用", "KafkaClient 和 _Kafka 怎么使用？", terms) == "KafkaClient 和 _Kafka 怎么使用？", "term protection does not match inside longer identifiers");
        check(SpeechHotwords.SelectFinalText("读过卡夫卡吗", "读过卡夫卡吗？", terms) == "读过卡夫卡吗？", "Chinese Kafka mentions are never globally rewritten into a technical term");
        check(SpeechHotwords.SelectFinalText("什么事 kafka", "什么是 Kafka？", terms) == "什么是 Kafka？", "refinement still improves surrounding text when English terms survive");
        check(SpeechHotwords.SelectFinalText("什么是 Z G C", "什么是 Z G C？", terms) == "什么是 ZGC？", "both decoder results normalize exactly spelled acronym letters before comparison");
        check(SpeechHotwords.SelectFinalText("什么是 ZG", "什么是 ZG？", terms) == "什么是 ZG？", "term preservation never fills in a missing acronym letter");
        check(SpeechHotwords.SelectFinalText("C++ 和 .NET", "C加加和点NET", terms) == "C++ 和 .NET", "technical terms containing regex punctuation are protected literally");
        check(SpeechHotwords.SelectFinalText(kafka, " ", terms) == kafka, "empty final recognition retains the streaming sentence");
        check(SpeechHotwords.SelectFinalText("什么事缓存", "什么是缓存？", terms) == "什么是缓存？", "sentences without recognized English hotwords still use whole-utterance improvements");
        var output = new List<SpeechSegment>();
        var recognizer = new ControlledRecognizer();
        await using (var queue = new SpeechFinalizationQueue(recognizer, options.SpeechHotwords, output.Add))
        {
            check(queue.TryEnqueue(SpeechSource.Speaker, new("什么是Z", true, [0.1f], 16000), 1), "whole utterance enters final recognition queue");
            await recognizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(output.Count == 0, "streaming final is withheld until whole-utterance recognition completes");
            recognizer.Result.SetResult("什么是 Z G C？");
            queue.TryEnqueue(SpeechSource.Microphone, new("我想一想", true, [0.2f], 48000), 2);
            await queue.CompleteAsync();
            check(output.Count == 2 && output[0].Text == "什么是 ZGC？" && output[0].Source == SpeechSource.Speaker && output[1].Source == SpeechSource.Microphone,
                "only refined results are published, once per sentence, with source and order retained");
            check(recognizer.Rates.SequenceEqual(new[] { 16000, 48000 }) && output[1].Sequence == 2, "original utterance sample rate and sequence survive asynchronous refinement");
        }
        output.Clear();
        recognizer = new ControlledRecognizer();
        var archiveRoot = Path.Combine(AppContext.BaseDirectory, "term-tests", Guid.NewGuid().ToString("N"));
        await using (var archive = new SpeechSessionArchive(archiveRoot, 16000, 16000, _ => { }))
        await using (var queue = new SpeechFinalizationQueue(recognizer, "", segment => { archive.AppendTranscript(segment); output.Add(segment); }))
        {
            var capturedAt = DateTimeOffset.Now;
            queue.TryEnqueue(SpeechSource.Microphone, new(kafka, true, [0.1f], CapturedAt: capturedAt), 3);
            recognizer.Result.SetResult("我想想什么是卡夫卡？");
            await queue.CompleteAsync();
            check(output.Count == 1 && output[0].Text == kafka && output[0].Sequence == 3 && output[0].CapturedAt == capturedAt,
                "actual finalization queue publishes preserved Kafka text once with original metadata");
            var route = BattlePrompt.Build(AppConfig.CreateDefault(), new BattleEntry { Source = output[0].Source, Text = output[0].Text });
            check(route.Turns.Single().Text == "我说：\n" + kafka, "answer model receives the preserved English question verbatim");
            using var reader = new StreamReader(new FileStream(archive.TranscriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var markdown = await reader.ReadToEndAsync();
            check(markdown.Contains(kafka) && !markdown.Contains("卡夫卡"), "session Markdown uses exactly the same protected text as the question");
        }
        output.Clear();
        recognizer = new ControlledRecognizer();
        await using (var queue = new SpeechFinalizationQueue(recognizer, terms, output.Add))
        {
            queue.TryEnqueue(SpeechSource.Microphone, new(kafka, true, [0.1f]), 4);
            await recognizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await queue.CompleteWithFallbackAsync().WaitAsync(TimeSpan.FromSeconds(2));
            check(output.Single().Text == kafka && output[0].Warning.Length > 0, "stop-time fallback also retains English technical terms");
        }
        output.Clear();
        await using (var queue = new SpeechFinalizationQueue(new FailingRecognizer(), "", output.Add))
        {
            queue.TryEnqueue(SpeechSource.Speaker, new("原始结果", true, [0.1f]), 0);
            await queue.CompleteAsync();
            check(output.Single().Text == "原始结果" && output[0].Warning.Length > 0, "refinement failure explicitly falls back to streaming text without losing the sentence");
        }
        output.Clear();
        recognizer = new ControlledRecognizer();
        var pending = new SpeechFinalizationQueue(recognizer, "", output.Add);
        pending.TryEnqueue(SpeechSource.Speaker, new("旧会话", true, [0.1f]), 0);
        await recognizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 8; i++) check(pending.TryEnqueue(SpeechSource.Speaker, new("排队", true, [0.1f]), i + 1), "bounded refinement queue accepts pending utterance " + i);
        check(!pending.TryEnqueue(SpeechSource.Speaker, new("溢出", true, [0.1f]), 20), "refinement queue reports overload instead of silently dropping audio");
        await pending.DisposeAsync();
        check(output.Count == 0, "stopping recognition cancels pending refinement without publishing stale questions");
    }

    public static async Task RunModelsAsync(string fixtureRoot, Action<bool, string> check)
    {
        var options = new BattleConfig();
        var cases = JsonSerializer.Deserialize<JsonElement[]>(await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "cases.json")))!;
        var results = new List<object>();
        using var baseline = new StreamingSpeechEngine(BattleSpeechService.ModelPath, new BattleConfig { SpeechHotwords = "", SpeechPauseSeconds = 0.8 });
        using var hotwords = new StreamingSpeechEngine(BattleSpeechService.ModelPath, options);
        await using var final = new QwenAsrRecognizer(options.SpeechRefinerContext);
        var loading = Stopwatch.StartNew();
        await final.StartAsync(CancellationToken.None);
        Console.WriteLine($"Qwen warm startup: {loading.Elapsed.TotalSeconds:F2}s; device={final.Device}");
        check(final.Device.Length > 0, "native worker reports its actual compute device after warmup");
        var originalWorker = WorkerIds();
        float[] firstAudio = []; var firstRate = 16000;
        foreach (var item in cases)
        {
            var file = item.GetProperty("file").GetString()!;
            using var reader = new AudioFileReader(Path.Combine(fixtureRoot, file));
            var samples = new List<float>(); var buf = new float[1600]; int count;
            while ((count = reader.Read(buf, 0, buf.Length)) > 0) samples.AddRange(buf.Take(count));
            var raw = samples.ToArray();
            if (firstAudio.Length == 0) { firstAudio = raw; firstRate = reader.WaveFormat.SampleRate; }
            string Decode(StreamingSpeechEngine model)
            {
                using var session = model.CreateSession();
                return string.Join("", session.Accept(reader.WaveFormat.SampleRate, raw, true).Where(s => s.IsFinal).Select(s => s.Text));
            }
            var before = Decode(baseline);
            var first = Decode(hotwords);
            var watch = Stopwatch.StartNew();
            var after = SpeechHotwords.NormalizeAcronyms(await final.RecognizeAsync(raw, reader.WaveFormat.SampleRate, CancellationToken.None), options.SpeechHotwords);
            results.Add(new { file, expected = item.GetProperty("expected").GetString(), baseline = before, streaming = first, refined = after, seconds = watch.Elapsed.TotalSeconds });
            Console.WriteLine($"ASR {file}: baseline={before}; hotwords={first}; final={after}; {watch.Elapsed.TotalSeconds:F2}s");
            check(!string.IsNullOrWhiteSpace(after), "native whole-utterance recognizer transcribes " + file);
        }
        await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        var currentWorker = WorkerIds();
        check(originalWorker.Length == 1 && originalWorker.SequenceEqual(currentWorker), "all utterances reuse one warmed native worker");
        check(!Directory.GetFiles(Path.GetTempPath(), "willgo-speech-*.wav").Any(), "temporary audio is deleted after native recognition");
        using var cancelled = new CancellationTokenSource(50);
        try
        {
            await final.RecognizeAsync(new float[16000 * 10], 16000, cancelled.Token);
            check(false, "native inference cancellation must be observed");
        }
        catch (OperationCanceledException) { check(true, "native inference cancellation stops and reaps the child process"); }
        check(!Directory.GetFiles(Path.GetTempPath(), "willgo-speech-*.wav").Any(), "cancelled native inference deletes its temporary audio");
        check(WorkerIds().Length == 0, "cancellation reaps the native worker before returning");
        check((await final.RecognizeAsync(firstAudio, firstRate, CancellationToken.None)).Length > 0, "worker reloads successfully after cancellation without stale protocol responses");
        await final.DisposeAsync();
        check(WorkerIds().Length == 0, "disposing final recognition releases its worker and GPU allocations");
        await using var cancelledStart = new QwenAsrRecognizer();
        using var startToken = new CancellationTokenSource(5);
        try { await cancelledStart.StartAsync(startToken.Token); check(false, "startup cancellation must be observed"); }
        catch (OperationCanceledException) { check(true, "cancelling model startup kills and reaps the loading worker"); }
    }

    public static async Task RunLiveAsync(string fixturePath, Action<bool, string> check)
    {
        var events = new ConcurrentQueue<SpeechSegment>();
        var failures = new ConcurrentQueue<string>();
        await using var service = new BattleSpeechService();
        service.Transcribed += events.Enqueue;
        service.Failed += failures.Enqueue;
        await service.StartAsync("", "", CancellationToken.None);
        using var source = new AudioFileReader(fixturePath);
        using var playback = new WaveOutEvent();
        playback.Init(source);
        var clock = Stopwatch.StartNew();
        playback.Play();
        while (playback.PlaybackState != PlaybackState.Stopped && clock.Elapsed.TotalSeconds < 20) await Task.Delay(50);
        check(events.Any(s => s.Source == SpeechSource.Speaker && !s.IsFinal && s.Text.Length > 0), "live loopback keeps streaming previews during two-stage recognition");
        var wait = Stopwatch.StartNew();
        while (!events.Any(s => s.Source == SpeechSource.Speaker && s.IsFinal) && wait.Elapsed.TotalSeconds < 10) await Task.Delay(50);
        var finals = events.Where(s => s.Source == SpeechSource.Speaker && s.IsFinal).ToArray();
        check(finals.Length == 1 && finals[0].Text.Contains("ZGC", StringComparison.OrdinalIgnoreCase) && finals[0].Warning.Length == 0,
            "actual system audio containing ZGC becomes exactly one fully refined final sentence");
        check(failures.IsEmpty, "simultaneous microphone and loopback capture does not overload refinement");
        Console.WriteLine($"Live acronym final latency after playback: {wait.Elapsed.TotalSeconds:F2}s");
        await service.StopAsync();
        check(WorkerIds().Length == 0, "stopping capture releases the persistent final recognition worker");
        check(!Directory.GetFiles(Path.GetTempPath(), "willgo-speech-*.wav").Any(), "stopping live capture leaves no temporary audio behind");
    }

    private static int[] WorkerIds()
    {
        var result = new List<int>();
        foreach (var process in Process.GetProcessesByName("speech"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainModule?.FileName == Path.Combine(QwenAsrRecognizer.ModelPath, "speech.exe")) result.Add(process.Id);
                }
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
        }
        return result.Order().ToArray();
    }

    private sealed class ControlledRecognizer : IUtteranceRecognizer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int> Rates { get; } = [];
        public async Task<string> RecognizeAsync(float[] audio, int sampleRate, CancellationToken token)
        { Rates.Add(sampleRate); Entered.TrySetResult(); return await Result.Task.WaitAsync(token); }
    }
    private sealed class FailingRecognizer : IUtteranceRecognizer
    {
        public Task<string> RecognizeAsync(float[] audio, int sampleRate, CancellationToken token) => throw new IOException("test failure");
    }
}
