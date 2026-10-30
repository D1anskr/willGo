using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

internal static class CloudSpeechEvaluation
{
    public static async Task ProbeAsync(string fixturePath, bool enableVolume)
    {
        var options = (await new AppConfigService(new ApiKeyProtector(), new QuietLogger()).LoadAsync()).Battle;
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-Api-Key", options.Speech.ApiKey.Trim());
        socket.Options.SetRequestHeader("X-Api-Resource-Id", DoubaoSpeechProtocol.ResourceId);
        socket.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(DoubaoSpeechProtocol.Endpoint, stop.Token);
        var start = DoubaoSpeechProtocol.CreateStart(options);
        if (enableVolume)
        {
            using var compressed = new MemoryStream(start, 8, start.Length - 8);
            using var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress);
            var root = await System.Text.Json.Nodes.JsonNode.ParseAsync(gzip);
            root!["request"]!["show_volume"] = true;
            start = DoubaoSpeechProtocol.Encode(1, 0, 1, JsonSerializer.SerializeToUtf8Bytes(root));
        }
        await socket.SendAsync(start.AsMemory(), WebSocketMessageType.Binary, true, stop.Token);
        var definiteCount = 0;
        var receive = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                var buffer = new byte[16384];
                ValueWebSocketReceiveResult frame;
                do
                {
                    frame = await socket.ReceiveAsync(buffer.AsMemory(), stop.Token);
                    if (frame.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, frame.Count);
                } while (!frame.EndOfMessage);
                var response = DoubaoSpeechProtocol.Decode(message.ToArray());
                using var json = JsonDocument.Parse(response.Payload);
                if (json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("utterances", out var utterances))
                    foreach (var utterance in utterances.EnumerateArray())
                    {
                        if (utterance.TryGetProperty("definite", out var definite) && definite.ValueKind == JsonValueKind.True) Interlocked.Increment(ref definiteCount);
                        Console.WriteLine(JsonSerializer.Serialize(utterance.EnumerateObject().Where(x => x.Name != "text" && x.Name != "words").ToDictionary(x => x.Name, x => x.Value.Clone())));
                    }
                if (response.IsLast) return;
            }
        });
        var pcm = LoadPcm(fixturePath);
        for (var offset = 0; offset < pcm.Length; offset += 6400)
        {
            await socket.SendAsync(DoubaoSpeechProtocol.CreateAudio(pcm.AsSpan(offset, Math.Min(6400, pcm.Length - offset))).AsMemory(), WebSocketMessageType.Binary, true, stop.Token);
            await Task.Delay(200, stop.Token);
        }
        for (var i = 0; i < 15; i++)
        {
            await socket.SendAsync(DoubaoSpeechProtocol.CreateAudio(new byte[6400]).AsMemory(), WebSocketMessageType.Binary, true, stop.Token);
            await Task.Delay(200, stop.Token);
        }
        Console.WriteLine("COMPLETED_BEFORE_STOP=" + Volatile.Read(ref definiteCount));
        await socket.SendAsync(DoubaoSpeechProtocol.CreateAudio(new byte[3200], true).AsMemory(), WebSocketMessageType.Binary, true, stop.Token);
        await receive;
    }

    private sealed record Fixture(string File, string Expected, string Group, SpeechSource Source, bool AskModel = false);
    private sealed record Evaluation(string Group, string File, string Expected, string Recognized,
        double AudioSeconds, double? FirstPreviewSeconds, double? FinalDelaySeconds, int CharacterEdits,
        int ReferenceCharacters, string[] Errors, bool ModelAsked, bool ModelAnswered, int AnswerCharacters);

    public static async Task RunAsync(string workDirectory, Action<bool, string> check)
    {
        var config = await new AppConfigService(new ApiKeyProtector(), new QuietLogger()).LoadAsync();
        config.Battle.Speech.Validate();
        var fixtures = new List<Fixture>();
        var general = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(workDirectory, "speech-upgrade/benchmark-cases.json")))!;
        foreach (var index in new[] { 0, 1, 9, 10, 11 })
            fixtures.Add(new(Path.Combine(workDirectory, $"speech-upgrade/case-{index:00}.wav"), general[index], "Chinese", SpeechSource.Speaker));
        foreach (var (folder, group) in new[] { ("funasr-eval", "Acronyms"), ("qwen-asr-eval/english", "English"), ("cloud-real-eval/fixtures", "Mixed") })
        {
            using var cases = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workDirectory, folder, "cases.json")));
            var index = 0;
            foreach (var item in cases.RootElement.EnumerateArray())
            {
                var file = item.GetProperty("file").GetString()!;
                fixtures.Add(new(Path.Combine(workDirectory, folder, file), item.GetProperty("expected").GetString()!, group,
                    group == "Mixed" ? SpeechSource.Microphone : SpeechSource.Speaker,
                    group == "Mixed" && index == 0 || group == "Acronyms" && index == 0));
                index++;
            }
        }
        var reportDirectory = Path.Combine(workDirectory, "cloud-real-eval");
        Directory.CreateDirectory(reportDirectory);
        var completed = new List<Evaluation>();
        foreach (var fixture in fixtures)
        {
            var result = await EvaluateAsync(config, fixture);
            completed.Add(result);
            Console.WriteLine($"CASE {completed.Count}/{fixtures.Count} [{result.Group}] expected={result.Expected} | recognized={result.Recognized} | final_delay={result.FinalDelaySeconds:F2}s | errors={result.Errors.Length} | model_answer={result.ModelAnswered}");
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "results.json"), JsonSerializer.Serialize(completed, new JsonSerializerOptions { WriteIndented = true }));
        }
        check(completed.All(x => x.Errors.Length == 0 && x.Recognized.Length > 0), "production cloud transcribes all synthetic fixtures without transport or protocol errors");
        check(completed.Where(x => x.ModelAsked).All(x => x.ModelAnswered), "actual completed microphone-trigger and speaker utterances reach the configured conversation model and receive answers");
    }

    private static async Task<Evaluation> EvaluateAsync(AppConfig config, Fixture fixture)
    {
        var errors = new ConcurrentQueue<string>();
        var finals = new ConcurrentQueue<SpeechSegment>();
        var clock = new Stopwatch();
        double? firstPreview = null;
        double? finalDelay = null;
        var source = new PushSpeech();
        await using var vm = new BattleViewModel(source, new ModelRouter(new OpenAiModelClient(new QuietLogger()), new QuietLogger()), () => config, Dispatcher.CurrentDispatcher);
        if (fixture.AskModel) await vm.StartCaptureAsync();
        await using var client = new DoubaoSpeechClient(config.Battle, fixture.Source, segment =>
        {
            if (!string.IsNullOrWhiteSpace(segment.Text)) firstPreview ??= clock.Elapsed.TotalSeconds;
            if (segment.IsFinal)
            {
                finals.Enqueue(segment);
                finalDelay = Math.Max(0, (DateTimeOffset.Now - segment.CapturedAt!.Value).TotalSeconds);
            }
            if (fixture.AskModel) source.Emit(segment);
        }, errors.Enqueue);
        var pcm = LoadPcm(fixture.File);
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await client.StartAsync(stop.Token);
            client.BeginAudio(); clock.Start();
            var bytes = pcm.Length;
            // Pace the fixture like device audio, then include enough silence for the configured cloud endpoint.
            var silenceBytes = (int)Math.Ceiling((config.Battle.SpeechPauseSeconds + 0.8) * 16000) * 2;
            Array.Resize(ref pcm, bytes + silenceBytes);
            for (var offset = 0; offset < pcm.Length; offset += 6400)
            {
                var length = Math.Min(6400, pcm.Length - offset);
                await client.SendAudioAsync(pcm.AsSpan(offset, length).ToArray(), stop.Token);
                var remaining = (offset + length) / 32000.0 - clock.Elapsed.TotalSeconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), stop.Token);
            }
            await client.CompleteAsync();
        }
        catch (Exception ex)
        {
            errors.Enqueue(ex is InvalidOperationException or TimeoutException ? ex.Message : "Speech transport failed: " + ex.GetType().Name);
        }
        var until = DateTime.UtcNow.AddSeconds(90);
        while (fixture.AskModel && (vm.Entries.Count == 0 || vm.Entries.Any(x => x.Busy)) && DateTime.UtcNow < until)
            await Task.Delay(30);
        var recognized = string.Join("", finals.Select(x => x.Text));
        var expectedNormalized = Normalize(fixture.Expected);
        var result = new Evaluation(fixture.Group, Path.GetFileName(fixture.File), fixture.Expected, recognized,
            LoadPcmLength(fixture.File) / 32000.0, firstPreview, finalDelay,
            Distance(expectedNormalized, Normalize(recognized)), expectedNormalized.Length,
            errors.ToArray(), fixture.AskModel, vm.Entries.Count > 0 && vm.Entries.All(x => !string.IsNullOrWhiteSpace(x.Answer)),
            vm.Entries.Sum(x => x.Answer.Length));
        if (fixture.AskModel && !result.ModelAnswered)
            Console.WriteLine("MODEL: no successful answer; endpoint details and credentials are suppressed.");
        return result;
    }

    private static int LoadPcmLength(string path) => LoadPcm(path).Length;

    private static byte[] LoadPcm(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.Channels == 2) provider = reader.ToMono();
        if (provider.WaveFormat.Channels != 1) throw new InvalidDataException("Fixture must have one or two channels.");
        if (provider.WaveFormat.SampleRate != 16000) provider = new WdlResamplingSampleProvider(provider, 16000);
        using var output = new MemoryStream();
        var samples = new float[3200];
        var bytes = new byte[6400];
        int count;
        while ((count = provider.Read(samples, 0, samples.Length)) > 0)
        {
            for (var i = 0; i < count; i++)
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)Math.Clamp(Math.Round(samples[i] * 32768), short.MinValue, short.MaxValue));
            output.Write(bytes, 0, count * 2);
        }
        return output.ToArray();
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static int Distance(string left, string right)
    {
        var row = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var diagonal = row[0]; row[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var previous = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (left[i - 1] == right[j - 1] ? 0 : 1));
                diagonal = previous;
            }
        }
        return row[^1];
    }

    private sealed class PushSpeech : IBattleSpeechService
    {
        public event Action<SpeechSegment>? Transcribed;
        public event Action<string>? Failed { add { } remove { } }
        public void Emit(SpeechSegment segment) => Transcribed?.Invoke(segment);
        public Task StartAsync(string microphoneId, string speakerId, CancellationToken cancellationToken, bool? recordAudio = null) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class QuietLogger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }
}
