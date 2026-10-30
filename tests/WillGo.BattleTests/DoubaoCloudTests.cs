using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

internal static class DoubaoCloudTests
{
    private const string FinalText = "我想想什么是 Kafka 和 ZGC？";
    private static BattleConfig Options() => new() { Speech = new() { ApiKey = "synthetic-test-key" } };

    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = Options();
        var original = options.Clone();
        original.Speech.ApiKey = "different";
        check(options.Speech.ApiKey == "synthetic-test-key", "cloud credentials clone independently");
        var request = RequestPayload(DoubaoSpeechProtocol.CreateStart(options));
        using (var document = JsonDocument.Parse(request))
        {
            var root = document.RootElement;
            var config = root.GetProperty("request");
            check(root.GetProperty("audio").GetProperty("rate").GetInt32() == 16000 && root.GetProperty("audio").GetProperty("channel").GetInt32() == 1, "cloud protocol declares resampled mono PCM16 at 16 kHz");
            check(!config.GetProperty("enable_nonstream").GetBoolean() && config.GetProperty("show_utterances").GetBoolean() && config.GetProperty("result_type").GetString() == "full", "cloud stream disables second pass and delivers completed utterances directly");
            check(!config.GetProperty("enable_ddc").GetBoolean() && !config.TryGetProperty("corpus", out _), "cloud defaults do not smooth away trigger words or inject a technical vocabulary");
            check(!Encoding.UTF8.GetString(request).Contains("synthetic-test-key"), "speech API credential is only an authentication header, never part of recognition context");
        }
        var oldSettings = JsonSerializer.Deserialize<DoubaoSpeechConfig>("{\"ApiKey\":\"synthetic-test-key\",\"SecondPass\":true,\"Hotwords\":\"Kafka\",\"Context\":\"old instructions\"}")!;
        using (var doc = JsonDocument.Parse(RequestPayload(DoubaoSpeechProtocol.CreateStart(new() { Speech = oldSettings }))))
            check(!doc.RootElement.GetProperty("request").GetProperty("enable_nonstream").GetBoolean() &&
                !doc.RootElement.GetProperty("request").TryGetProperty("corpus", out _), "legacy enhancement settings cannot re-enable hotwords, context or a second pass");
        var rejected = false;
        try { new DoubaoSpeechConfig().Validate(); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "missing credentials fail before opening audio devices");
        rejected = false;
        try { new DoubaoSpeechConfig { ApiKey = "bad\r\nheader" }.Validate(); } catch (ArgumentException) { rejected = true; }
        check(rejected, "credential control characters cannot inject HTTP headers");

        var started = DateTimeOffset.Now;
        var output = new List<SpeechSegment>();
        var results = new DoubaoSpeechResults(SpeechSource.Speaker, started, output.Add);
        results.Accept(Result("什么是 ZG", false));
        check(output.Count == 1 && !output[0].IsFinal, "intermediate recognition never becomes a question");
        results.Accept(Result("什么是 ZGC？", true));
        results.Accept(Result("什么是 ZGC？", true));
        check(output.Count(x => x.IsFinal) == 1 && output.Single(x => x.IsFinal).Text == "什么是 ZGC？", "completed cloud sentence preserves acronyms and cumulative final packets do not duplicate a question");
        check(output.First(x => x.IsFinal).CapturedAt == started.AddSeconds(1), "real cloud packets may omit zero start_time in both previews and completed first sentences");
        results.Accept(Result("什么是 ZGC？", true, 2000, 3000));
        check(output.Count(x => x.IsFinal) == 2, "a repeated spoken sentence at a later timestamp is a new question");
        check(output.First(x => x.IsFinal).CapturedAt == started.AddMilliseconds(1000), "capture timestamp comes from audio position rather than network response time");
        var mic = new List<SpeechSegment>();
        var micResults = new DoubaoSpeechResults(SpeechSource.Microphone, started, mic.Add);
        micResults.Accept(Result("我想想什么是卡夫卡", false));
        micResults.Accept(Result("我想想什么是 Kafka", true));
        check(mic.Single(x => x.IsFinal).Text == "我想想什么是 Kafka" && mic.All(x => x.Source == SpeechSource.Microphone), "confirmed cloud text preserves English exactly and keeps the microphone source");
        var response = DoubaoSpeechProtocol.Decode(ServerPacket(Result(FinalText, true), last: true));
        check(response.IsLast && response.ErrorCode == 0 && Encoding.UTF8.GetString(response.Payload).Contains("Kafka"), "big-endian sequence and gzip server packets decode correctly");
        rejected = false;
        try { DoubaoSpeechProtocol.Decode(new byte[8]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "malformed remote packets are rejected");

        await using (var server = new FakeServer())
        {
            var events = new ConcurrentQueue<SpeechSegment>();
            var errors = new ConcurrentQueue<string>();
            await using (var client = new DoubaoSpeechClient(Options(), SpeechSource.Speaker, events.Enqueue, errors.Enqueue, server.Endpoint))
            {
                await client.StartAsync(CancellationToken.None);
                await client.SendAudioAsync(new byte[6400]);
                await client.CompleteAsync();
            }
            check(errors.IsEmpty && events.Count(x => x.IsFinal) == 1 && events.Single(x => x.IsFinal).Text == FinalText, "real WebSocket exchange accepts fragmented server frames and flushes a confirmed tail once");
            var session = server.Sessions.Single();
            check(session.ApiKey == "synthetic-test-key" && session.AppId is null && session.Resource == "volc.seedasr.sauc.duration", "new-console authentication selects the 2.0 hourly resource and sends only API Key");
            check(session.SawLast && session.AudioBytes == 9600, "stop sends a final audio packet with the documented terminal flag");
        }
        await using (var server = new FakeServer())
        {
            var errors = new ConcurrentQueue<string>();
            var legacy = Options(); legacy.Speech.ApiKey = ""; legacy.Speech.AppId = "legacy-app"; legacy.Speech.AccessToken = "legacy-token";
            await using var client = new DoubaoSpeechClient(legacy, SpeechSource.Microphone, _ => { }, errors.Enqueue, server.Endpoint);
            await client.StartAsync(CancellationToken.None);
            await client.SendAudioAsync(new byte[6400]);
            await client.CompleteAsync();
            var session = server.Sessions.Single();
            check(errors.IsEmpty && session.ApiKey is null && session.AppId == "legacy-app" && session.AccessToken == "legacy-token", "old-console App ID and Access Token work without an API Key header");
        }
        foreach (var mode in new[] { "error", "close", "reject", "partial-last" })
        {
            await using var server = new FakeServer(mode);
            var errors = new ConcurrentQueue<string>();
            var events = new ConcurrentQueue<SpeechSegment>();
            await using var client = new DoubaoSpeechClient(Options(), SpeechSource.Speaker, events.Enqueue, errors.Enqueue, server.Endpoint);
            try
            {
                await client.StartAsync(CancellationToken.None);
                await client.SendAudioAsync(new byte[6400]);
                if (mode == "partial-last") await client.CompleteAsync();
                var until = DateTime.UtcNow.AddSeconds(3);
                while (errors.IsEmpty && DateTime.UtcNow < until) await Task.Delay(10);
            }
            catch (InvalidOperationException ex) { errors.Enqueue(ex.Message); }
            check(!errors.IsEmpty && !events.Any(x => x.IsFinal), mode + " is visible and never promotes partial speech to a final question");
            check(errors.All(x => !x.Contains("synthetic-test-key")), mode + " diagnostics do not echo credentials from server responses");
        }
        await using (var service = new BattleSpeechService())
        {
            rejected = false;
            try { await service.StartAsync("", "", CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "cloud-only service gives a setup error with no local model dependency");
            await service.StopAsync();
        }
    }

    public static async Task RunCaptureAsync(Action<bool, string> check)
    {
        await using var server = new FakeServer();
        var options = Options();
        options.SpeechArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "cloud-capture-private", Guid.NewGuid().ToString("N"));
        var errors = new ConcurrentQueue<string>();
        var events = new ConcurrentQueue<SpeechSegment>();
        await using var service = new BattleSpeechService(() => options, server.Endpoint);
        service.Transcribed += events.Enqueue;
        service.Failed += errors.Enqueue;
        foreach (var record in new[] { true, false, true })
        {
            var previousSessions = server.Sessions.Count;
            await service.StartAsync("", "", CancellationToken.None, record);
            using var playback = new WaveOutEvent();
            playback.Init(new SignalGenerator(48000, 2) { Gain = 0.03, Frequency = 880, Type = SignalGeneratorType.Sin });
            playback.Play();
            await Task.Delay(850);
            playback.Stop();
            await service.StopAsync();
            var sessions = server.Sessions.Skip(previousSessions).ToArray();
            check(sessions.Length == 2 && sessions.All(x => x.SawLast && x.AudioBytes > 6400), "real WASAPI microphone and silent loopback feed two cloud connections and drain on stop");
            check(sessions.Any(x => x.NonzeroAudio), "actual speaker playback reaches the cloud as nonzero resampled PCM");
        }
        check(errors.IsEmpty, "actual capture, 16 kHz resampling and cloud stop/restart have no errors: " + string.Join("; ", errors));
        check(events.Count(x => x.IsFinal && x.Source == SpeechSource.Microphone) == 3 && events.Count(x => x.IsFinal && x.Source == SpeechSource.Speaker) == 3, "both sources retain independent final sentences across restarts");
        check(Directory.GetFiles(options.SpeechArchiveDirectory, "*.wav").Length == 2 && Directory.GetFiles(options.SpeechArchiveDirectory, "*.md").Length == 3, "yes/no/yes keeps one mixed WAV only for recording sessions and Markdown for all three");
        foreach (var path in Directory.GetFiles(options.SpeechArchiveDirectory, "*.md"))
        {
            var text = await File.ReadAllTextAsync(path);
            check(text.Contains("我：") && text.Contains("对方：") && text.Contains(FinalText) && text.Contains("结束时间"), "cloud-confirmed stop tails reach archived Markdown for both sources");
        }
        foreach (var path in Directory.GetFiles(options.SpeechArchiveDirectory, "*.wav"))
        {
            using var wav = new WaveFileReader(path);
            check(wav.WaveFormat.Channels == 2 && wav.TotalTime.TotalSeconds is > 0.7 and < 1.3, "mixed WAV duration excludes network finalization time");
        }
        await using var rejectServer = new FakeServer("reject");
        await using var rejectedService = new BattleSpeechService(Options, rejectServer.Endpoint);
        var rejected = false;
        try { await rejectedService.StartAsync("", "", CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
        await rejectedService.StopAsync();
        check(rejected, "cloud authentication failure releases both real WASAPI devices without creating a recording");

        await using var liveServer = new FakeServer("live");
        var liveConfig = AppConfig.CreateDefault();
        liveConfig.Battle = Options();
        liveConfig.Battle.SaveSpeechSessions = false;
        liveConfig.Battle.TriggerPhrases = "我想想";
        liveConfig.Battle.ConversationPrompt = "Synthetic conversation system prompt";
        var router = new RecordingRouter();
        var liveService = new BattleSpeechService(() => liveConfig.Battle, liveServer.Endpoint);
        await using var vm = new BattleViewModel(liveService, router, () => liveConfig, Dispatcher.CurrentDispatcher);
        await vm.StartCaptureAsync();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((router.Requests.Count < 3 || vm.Entries.Any(x => x.Busy)) && DateTime.UtcNow < deadline) await Task.Delay(20);
        check(vm.IsRecording && router.Requests.Count == 3 && vm.Entries.Count == 3, "completed cloud utterances create questions and model requests while capture remains active");
        check(vm.Entries.Count(x => x.Source == SpeechSource.Microphone) == 1 && vm.Entries.Count(x => x.Source == SpeechSource.Speaker) == 2 &&
            vm.Transcript.Contains("什么是 Kafka 与 ZGC？"), "microphone keeps trigger rules and records other speech while speaker sentences submit directly");
        check(router.Requests.All(x => x.Config.Primary.SystemPrompt == liveConfig.Battle.ConversationPrompt &&
            x.Turns.Count == 1 && (x.Turns[0].Text == "对方说：\n什么是 Kafka 与 ZGC？" ||
            x.Turns[0].Text == "对方说：\n" + FinalText || x.Turns[0].Text == "我说：\n" + FinalText)),
            "raw cloud text reaches the conversation model with its system prompt and no extra validation or rewrite request");
        check(vm.Entries.All(x => x.Answer == "Synthetic answer" && !x.Busy), "returned answers appear under their own question titles");
        await vm.StopCaptureAsync();
        await Task.Delay(50);
        check(router.Requests.Count == 3 && vm.Entries.Count == 3, "cumulative packets and stop tails cannot submit a completed cloud question twice");
    }

    private sealed class RecordingRouter : IModelRouter
    {
        public List<(AppConfig Config, IReadOnlyList<ConversationTurn> Turns)> Requests { get; } = [];
        public Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken token)
        {
            Requests.Add((config, turns));
            return Task.FromResult(new ModelReply { Text = "Synthetic answer" });
        }
    }

    public static async Task RunConfiguredConnectionAsync(Action<bool, string> check)
    {
        var options = (await new FloatingDeskAssistant.Infrastructure.Config.AppConfigService(
            new FloatingDeskAssistant.Infrastructure.Security.ApiKeyProtector(),
            new FloatingDeskAssistant.Infrastructure.Logging.FileLogger()).LoadAsync()).Battle;
        if (string.IsNullOrWhiteSpace(options.Speech.ApiKey) &&
            (string.IsNullOrWhiteSpace(options.Speech.AppId) || string.IsNullOrWhiteSpace(options.Speech.AccessToken)))
        {
            Console.WriteLine("SKIP: production cloud connection requires speech credentials saved in Settings; no credential was printed or requested in chat.");
            return;
        }
        var errors = new ConcurrentQueue<string>();
        await using var microphone = new DoubaoSpeechClient(options, SpeechSource.Microphone, _ => { }, errors.Enqueue);
        await using var speaker = new DoubaoSpeechClient(options, SpeechSource.Speaker, _ => { }, errors.Enqueue);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Task.WhenAll(microphone.StartAsync(stop.Token), speaker.StartAsync(stop.Token));
        await Task.WhenAll(microphone.SendAudioAsync(new byte[6400], stop.Token), speaker.SendAudioAsync(new byte[6400], stop.Token));
        await Task.WhenAll(microphone.CompleteAsync(), speaker.CompleteAsync());
        check(errors.IsEmpty, "production Doubao accepts two simultaneous authenticated speech connections and silent terminal audio: " + string.Join("; ", errors));
    }

    private static byte[] Result(string text, bool final, int start = 0, int end = 1000) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        result = new { text, utterances = new[] { new { text, definite = final, start_time = start == 0 ? (int?)null : start, end_time = end } } }
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    private static byte[] RequestPayload(byte[] message)
    {
        using var source = new MemoryStream(message, 8, message.Length - 8);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var target = new MemoryStream(); gzip.CopyTo(target); return target.ToArray();
    }

    private static byte[] ServerPacket(byte[] payload, bool last = false, uint error = 0)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(payload);
        var content = compressed.ToArray();
        var bytes = new byte[12 + content.Length];
        bytes[0] = 0x11; bytes[1] = error != 0 ? (byte)0xf0 : last ? (byte)0x93 : (byte)0x91; bytes[2] = 0x11;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), error != 0 ? error : last ? uint.MaxValue : 1);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), content.Length);
        content.CopyTo(bytes, 12);
        return bytes;
    }

    private sealed class Session
    {
        public string? ApiKey, AppId, AccessToken, Resource;
        public int AudioBytes;
        public bool SawLast;
        public bool NonzeroAudio;
    }

    private sealed class FakeServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accept;
        private readonly List<Task> _handlers = [];
        private readonly string _mode;
        public ConcurrentQueue<Session> Sessions { get; } = new();
        public Uri Endpoint { get; }
        public FakeServer(string mode = "normal")
        {
            _mode = mode;
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            Endpoint = new($"ws://localhost:{port}/speech");
            _listener.Start(); _accept = Task.Run(AcceptAsync);
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                    if (_mode == "reject") { context.Response.StatusCode = 403; context.Response.Close(); continue; }
                    _handlers.Add(HandleAsync(context));
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
        }
        private async Task HandleAsync(HttpListenerContext context)
        {
            using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            var session = new Session
            {
                ApiKey = context.Request.Headers["X-Api-Key"], AppId = context.Request.Headers["X-Api-App-Key"],
                AccessToken = context.Request.Headers["X-Api-Access-Key"], Resource = context.Request.Headers["X-Api-Resource-Id"]
            };
            Sessions.Enqueue(session);
            try
            {
                var first = await ReceiveAsync(socket);
                if (first[1] >> 4 != 1) throw new InvalidDataException("missing start packet");
                using var doc = JsonDocument.Parse(RequestPayload(first));
                var sentPartial = false;
                var liveSentences = 0;
                while (!_stop.IsCancellationRequested)
                {
                    var packet = await ReceiveAsync(socket);
                    if (packet[1] >> 4 != 2) throw new InvalidDataException("expected audio");
                    var pcm = RequestPayload(packet);
                    session.AudioBytes += pcm.Length;
                    session.NonzeroAudio |= pcm.Any(x => x != 0);
                    session.SawLast = (packet[1] & 2) != 0;
                    if (_mode == "error")
                    {
                        await SendAsync(socket, ServerPacket(Encoding.UTF8.GetBytes("synthetic-test-key"), error: 45000001));
                        return;
                    }
                    if (!sentPartial)
                    {
                        await SendAsync(socket, ServerPacket(Result("我想想什么是 ZG", false)));
                        sentPartial = true;
                    }
                    if (_mode == "close")
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "synthetic", _stop.Token);
                        return;
                    }
                    if (_mode == "live" && liveSentences < 2)
                    {
                        var result = liveSentences == 0 ? Result("什么是 Kafka 与 ZGC？", true) : Result(FinalText, true, 1000, 2000);
                        await SendAsync(socket, ServerPacket(result));
                        await SendAsync(socket, ServerPacket(result));
                        liveSentences++;
                    }
                    if (session.SawLast)
                    {
                        await SendAsync(socket, ServerPacket(Result(FinalText, _mode != "partial-last", _mode == "live" ? 1000 : 0, _mode == "live" ? 2000 : 1000), last: true));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", _stop.Token);
                        return;
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested || socket.State != WebSocketState.Open) { }
        }
        private async Task<byte[]> ReceiveAsync(WebSocket socket)
        {
            var buffer = new byte[8192];
            using var output = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
                if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("closed");
                output.Write(buffer, 0, frame.Count);
            } while (!frame.EndOfMessage);
            return output.ToArray();
        }
        private async Task SendAsync(WebSocket socket, byte[] bytes)
        {
            var half = bytes.Length / 2;
            await socket.SendAsync(bytes.AsMemory(0, half), WebSocketMessageType.Binary, false, _stop.Token);
            await socket.SendAsync(bytes.AsMemory(half), WebSocketMessageType.Binary, true, _stop.Token);
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            await _accept;
            await Task.WhenAll(_handlers);
            _listener.Close(); _stop.Dispose();
        }
    }
}
