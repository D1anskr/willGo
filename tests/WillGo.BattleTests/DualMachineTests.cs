using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using ComboBox = System.Windows.Controls.ComboBox;
using TabControl = System.Windows.Controls.TabControl;

internal static class DualMachineTests
{
    public static async Task RunLiveAsync(Action<bool, string> check)
    {
        var dataConfig = AppConfig.CreateDefault();
        dataConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Data, Port = FreePort() };
        var displayConfig = AppConfig.CreateDefault();
        displayConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Display };
        displayConfig.Battle.Speech.ApiKey = "synthetic-test-key";
        displayConfig.Battle.SpeechArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "dual-live-test", Guid.NewGuid().ToString("N"));
        await using var data = new DualMachineService(() => dataConfig, new Screenshots(), DualMachineIdentity.Create);
        await using var display = new DualMachineService(() => displayConfig, new Screenshots());
        await data.ApplyAsync(dataConfig.DualMachine);
        displayConfig.DualMachine.PeerOffer = (DualMachineOffer.Parse(data.PairingOffers.First()) with { Host = "127.0.0.1" }).ToString();
        await display.ApplyAsync(displayConfig.DualMachine);
        await using var cloud = new DoubaoCloudTests.FakeServer();
        var errors = new ConcurrentQueue<string>();
        await using var speech = new NetworkBattleSpeechService(display, () => displayConfig.Battle, cloud.Endpoint);
        speech.Failed += errors.Enqueue;
        await speech.StartAsync("", "", CancellationToken.None, false);
        await Task.Delay(1500);
        await speech.StopAsync();
        check(errors.IsEmpty, "dual live: real default WASAPI devices stream through TLS without conversion or send errors");
        check(cloud.Sessions.Count == 2 && cloud.Sessions.All(item => item.AudioBytes > 16000 && item.SawLast),
            "dual live: both physical capture channels reach local fake ASR and finish correctly");
        check(!data.IsAudioStreaming && !Directory.EnumerateFiles(displayConfig.Battle.SpeechArchiveDirectory, "*.wav").Any(),
            "dual live: device handles stop and no raw recording is retained in text-only test");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        check(!AppConfig.CreateDefault().DualMachine.Enabled && !JsonSerializer.Deserialize<AppConfig>("{}")!.DualMachine.Enabled,
            "dual: new and legacy configurations stay in standalone mode");
        var config = AppConfig.CreateDefault();
        config.DualMachine.Enabled = true;
        var clone = config.Clone(); clone.DualMachine.Role = DualMachineRole.Display;
        check(config.DualMachine.IsData && clone.DualMachine.IsDisplay, "dual: role configuration clones independently");
        foreach (var invalid in new[] { "https://192.168.1.2", "willgo-pair://8.8.8.8:38657/abc", "willgo-pair://192.168.1.2:38657/abc" })
            check(await RejectAsync(() => Task.FromResult(DualMachineOffer.Parse(invalid))), "dual: invalid or non-LAN offer is rejected");
        using (var oversized = new MemoryStream(new byte[] { 1, 127, 255, 255, 255 }))
            check(await RejectAsync(() => DualMachineProtocol.ReadAsync(oversized, CancellationToken.None)), "dual: oversized frames rejected before allocating payload");
        var session = Guid.NewGuid();
        var packet = DualMachineProtocol.DecodeAudio(new(PeerFrameKind.Speaker, DualMachineProtocol.EncodeAudio(session, 17, new byte[3200])));
        check(packet.Session == session && packet.Source == SpeechSource.Speaker && packet.Sequence == 17 && packet.Pcm.Length == 3200,
            "dual: wire format preserves session, source, sequence and PCM bytes");

        var directory = Path.Combine(AppContext.BaseDirectory, "dual-machine-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string persistedFingerprint, persistedSecret;
        using (var identity = DualMachineIdentity.LoadOrCreate(directory: directory))
        { persistedFingerprint = identity.Fingerprint; persistedSecret = identity.Secret; }
        using (var identity = DualMachineIdentity.LoadOrCreate(directory: directory))
            check(identity.Fingerprint == persistedFingerprint && identity.Secret == persistedSecret,
                "dual: DPAPI identity survives application restart without changing pair information");
        using (var identity = DualMachineIdentity.LoadOrCreate(reset: true, directory: directory))
            check(identity.Fingerprint != persistedFingerprint && identity.Secret != persistedSecret && !File.ReadAllText(Path.Combine(directory, "dual-machine.identity")).Contains(identity.Secret),
                "dual: explicit reset revokes old pairing and identity secrets are not stored in plaintext");
        var dataConfig = AppConfig.CreateDefault();
        dataConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Data, Port = FreePort() };
        dataConfig.RemoteCaptureMode = RemoteCaptureMode.PresetRegion;
        dataConfig.RemoteCaptureRegionX = 123; dataConfig.RemoteCaptureRegionY = 234;
        dataConfig.RemoteCaptureRegionWidth = 640; dataConfig.RemoteCaptureRegionHeight = 480;
        dataConfig.Battle.MicrophoneDeviceId = "data-microphone"; dataConfig.Battle.SpeakerDeviceId = "data-speaker";
        var displayConfig = AppConfig.CreateDefault();
        displayConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Display };
        displayConfig.Battle.Speech.ApiKey = "synthetic-test-key";
        displayConfig.Battle.SpeechArchiveDirectory = directory;
        displayConfig.Battle.SuppressSpeakerEcho = false;
        displayConfig.Battle.TriggerPhrases = "我想想";
        var screenshot = new Screenshots();
        var localDisplayScreenshot = new Screenshots();
        var captures = new ConcurrentQueue<Capture>();
        var rejectCapture = false;
        string? microphone = null, speaker = null;
        await using var data = new DualMachineService(() => dataConfig, screenshot, DualMachineIdentity.Create,
            (mic, output, send, failed) =>
            {
                microphone = mic; speaker = output;
                if (rejectCapture) throw new InvalidOperationException("synthetic unavailable audio device");
                var capture = new Capture(send, failed); captures.Enqueue(capture); return capture;
            });
        await using var display = new DualMachineService(() => displayConfig, localDisplayScreenshot, DualMachineIdentity.Create);
        await data.ApplyAsync(dataConfig.DualMachine);
        var offer = DualMachineOffer.Parse(data.PairingOffers.First()) with { Host = "127.0.0.1" };
        check(DualMachineOffer.Parse(offer.ToString()) == offer && !data.IsAudioStreaming,
            "dual: data role generates a complete offer without starting microphones");
        using (var anotherIdentity = DualMachineIdentity.Create())
        {
            displayConfig.DualMachine.PeerOffer = (offer with { Fingerprint = anotherIdentity.Fingerprint }).ToString();
            check(await RejectAsync(() => display.ApplyAsync(displayConfig.DualMachine)), "dual: wrong pinned TLS certificate is rejected");
        }
        displayConfig.DualMachine.PeerOffer = (offer with { Secret = new string('0', 64) }).ToString();
        check(await RejectAsync(() => display.ApplyAsync(displayConfig.DualMachine)), "dual: wrong pairing secret is rejected");
        displayConfig.DualMachine.PeerOffer = offer.ToString();
        var configService = new AppConfigService(new ApiKeyProtector(), new Logger());
        var configPath = Path.Combine(directory, "settings.json");
        typeof(AppConfigService).GetField("_filePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(configService, configPath);
        await configService.SaveAsync(displayConfig);
        var restored = await configService.LoadAsync();
        check(restored.DualMachine.IsDisplay && restored.DualMachine.PeerOffer == offer.ToString(), "dual: role and paired endpoint round-trip through actual settings persistence");
        check(!File.ReadAllText(configPath).Contains(offer.Secret), "dual: pairing secret in saved settings is DPAPI protected");
        await display.ApplyAsync(displayConfig.DualMachine);
        await UntilAsync(() => data.IsConnected);
        check(data.IsConnected && display.IsConnected && !data.IsAudioStreaming, "dual: authenticated TLS pairing connects exactly two roles, without auto-recording");
        await using (var extra = new DualMachineService(() => displayConfig, localDisplayScreenshot, DualMachineIdentity.Create))
            check(await RejectAsync(() => extra.ApplyAsync(displayConfig.DualMachine)) && data.IsConnected && display.IsConnected,
                "dual: second display rejected without replacing connected peer");

        var audio = new ConcurrentQueue<PeerAudio>();
        display.AudioReceived += audio.Enqueue;
        await display.StartAudioAsync(session, CancellationToken.None);
        await UntilAsync(() => audio.Count >= 6);
        check(microphone == "data-microphone" && speaker == "data-speaker", "dual: data endpoint selects its own configured devices");
        check(audio.All(item => item.Session == session) && audio.Where(item => item.Source == SpeechSource.Microphone).All(item => item.Pcm[0] == 17) &&
            audio.Where(item => item.Source == SpeechSource.Speaker).All(item => item.Pcm[0] == 34), "dual: two PCM channels arrive intact with correct source identities");
        foreach (var source in Enum.GetValues<SpeechSource>())
        {
            var sequences = audio.Where(item => item.Source == source).Select(item => item.Sequence).ToArray();
            check(sequences.SequenceEqual(Enumerable.Range(1, sequences.Length).Select(i => (long)i)), "dual: monotonic audio sequence for " + source);
        }
        var received = await display.RequestScreenshotAsync(CancellationToken.None);
        check(received!.ImageBytes.SequenceEqual(screenshot.Png) && screenshot.Region == new System.Drawing.Rectangle(123, 234, 640, 480),
            "dual: screenshot chunks reassemble exactly and use data endpoint region");
        check(audio.Count >= 6 && data.IsAudioStreaming, "dual: screenshot requests preserve active audio capture");
        await display.StopAudioAsync(session);
        var afterStop = audio.Count;
        await Task.Delay(200);
        check(!data.IsAudioStreaming && captures.Last().Stopped && audio.Count == afterStop, "dual: stop acknowledgement follows all remaining audio packets");
        display.AudioReceived -= audio.Enqueue;
        rejectCapture = true;
        check(await RejectAsync(() => display.StartAudioAsync(Guid.NewGuid(), CancellationToken.None)) && data.IsConnected && display.IsConnected && !data.IsAudioStreaming,
            "dual: reported device initialization failure leaves pairing intact and no capture running");
        rejectCapture = false;
        var faultSession = Guid.NewGuid();
        var fault = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        display.AudioFailed += (id, _) => fault.TrySetResult(id);
        await display.StartAudioAsync(faultSession, CancellationToken.None);
        captures.Last().Fail("synthetic capture failure");
        check(await fault.Task.WaitAsync(TimeSpan.FromSeconds(5)) == faultSession && !data.IsAudioStreaming && display.IsConnected,
            "dual: asynchronous device error stops data capture and reports the exact session without losing pairing");
        await display.StopAudioAsync(faultSession);
        dataConfig.RemoteCaptureRegionWidth = 0;
        check(await RejectAsync(() => display.RequestScreenshotAsync(CancellationToken.None)) && display.IsConnected,
            "dual: invalid data region returns an actionable error without capturing display or dropping pairing");
        dataConfig.RemoteCaptureRegionWidth = 640;

        var pushed = new TaskCompletionSource<ScreenshotCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        display.ScreenshotReceived += image => pushed.TrySetResult(image);
        await data.PushScreenshotAsync();
        check((await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5))).ImageBytes.SequenceEqual(screenshot.Png), "dual: data endpoint shortcut can push a standalone screenshot");
        var text = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        display.TextReceived += value => text.TrySetResult(value);
        await data.PushTextAsync("解释 Kafka 的消费组");
        check(await text.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "解释 Kafka 的消费组", "dual: phone text from data endpoint reaches display");

        await using (var cloud = new DoubaoCloudTests.FakeServer("live"))
        {
            var finals = new ConcurrentQueue<SpeechSegment>();
            var failures = new ConcurrentQueue<string>();
            await using var speech = new NetworkBattleSpeechService(display, () => displayConfig.Battle, cloud.Endpoint);
            speech.Transcribed += finals.Enqueue; speech.Failed += failures.Enqueue;
            await speech.StartAsync("must-not-open-local-mic", "must-not-open-local-speaker", CancellationToken.None, true);
            await UntilAsync(() => finals.Count(item => item.IsFinal) >= 4);
            await Task.Delay(250);
            await speech.StopAsync();
            check(failures.IsEmpty && finals.Count(item => item.IsFinal && item.Source == SpeechSource.Microphone) == 2 &&
                finals.Count(item => item.IsFinal && item.Source == SpeechSource.Speaker) == 2,
                "dual: real TLS to two fake cloud WebSockets produces correctly attributed final transcripts");
            check(cloud.Sessions.Count == 2 && cloud.Sessions.All(item => item.ApiKey == "synthetic-test-key" && item.NonzeroAudio && item.SawLast),
                "dual: only display authenticates cloud ASR and both streams flush on stop");
            var wave = Directory.GetFiles(directory, "*.wav").Single();
            using var reader = new WaveFileReader(wave);
            var bytes = new byte[(int)reader.Length]; reader.ReadExactly(bytes);
            check(reader.WaveFormat.SampleRate == 16000 && reader.WaveFormat.Channels == 2 && bytes.Any(value => value != 0),
                "dual: display archives remote microphone and speaker as one non-silent stereo WAV");
            check(Directory.GetFiles(directory, "*.md").Select(File.ReadAllText).Any(content => content.Contains("我：") && content.Contains("对方：") && content.Contains("Kafka")),
                "dual: display archives both identities in one Markdown transcript");

            await speech.StartAsync("", "", CancellationToken.None, false);
            await UntilAsync(() => cloud.Sessions.Count == 4);
            await Task.Delay(350);
            await speech.StopAsync();
            check(Directory.GetFiles(directory, "*.wav").Length == 1 && Directory.GetFiles(directory, "*.md").Length == 2,
                "dual: recording choice false saves text without a second WAV");
        }

        await using (var cloud = new DoubaoCloudTests.FakeServer("live"))
        {
            var router = new Router();
            var adapter = new ModeAwareScreenshotService(localDisplayScreenshot, display, () => displayConfig);
            await using var vm = new BattleViewModel(new NetworkBattleSpeechService(display, () => displayConfig.Battle, cloud.Endpoint), router,
                () => displayConfig, Dispatcher.CurrentDispatcher, adapter) { ConfirmAudioRecording = () => false };
            await vm.StartCaptureAsync();
            await UntilAsync(() => vm.Entries.Count >= 3);
            check(vm.Entries.Count(entry => !entry.IsScreenshot && entry.Source == SpeechSource.Microphone) == 1,
                "dual: existing workspace retains microphone trigger gating with remote audio");
            displayConfig.RemoteCaptureMode = RemoteCaptureMode.PresetRegion;
            displayConfig.RemoteCaptureRegionWidth = displayConfig.RemoteCaptureRegionHeight = 0;
            await vm.CaptureScreenshotAsync();
            await UntilAsync(() => vm.Entries.Any(entry => entry.IsScreenshot && entry.HasExtractedQuestion && !entry.Busy));
            check(localDisplayScreenshot.Calls == 0 && vm.Entries.First().IsScreenshot && vm.Entries.First().Answer.Contains("代码"),
                "dual: display screenshot ignores its invalid local region and renders extraction plus answer from data endpoint");
            display.AbortConnection();
            await UntilAsync(() => !vm.IsRecording && !data.IsAudioStreaming);
            check(vm.Status.Contains("断开") || vm.Status.Contains("重置"), "dual: disconnect visibly ends workspace recording and finalizes archive");
            var calls = localDisplayScreenshot.Calls;
            check(await RejectAsync(() => adapter.CaptureFullDesktopAsync(CancellationToken.None)) && localDisplayScreenshot.Calls == calls,
                "dual: disconnected display never silently captures its own screen");
            displayConfig.DualMachine.Enabled = false;
            await adapter.CaptureFullDesktopAsync(CancellationToken.None);
            check(localDisplayScreenshot.Calls == calls + 1, "dual: disabling dual mode restores standalone screenshot capture");
            displayConfig.DualMachine.Enabled = true;
        }

        await display.ApplyAsync(displayConfig.DualMachine, force: true);
        await UntilAsync(() => data.IsConnected);
        check(display.IsConnected && !data.IsAudioStreaming, "dual: reconnect returns to paired standby without resuming microphones");
        await CheckSettingsAsync(displayConfig, display, directory, check);
        displayConfig.DualMachine.Enabled = false;
        await display.ApplyAsync(displayConfig.DualMachine);
        await UntilAsync(() => !data.IsConnected);
        check(display.Status == "单机模式" && !data.IsAudioStreaming, "dual: leaving display role disconnects and keeps remote devices stopped");
    }

    private static async Task CheckSettingsAsync(AppConfig config, DualMachineService peer, string directory, Action<bool, string> check)
    {
        var window = new SettingsWindow(config, AppConfig.CreateDefault(), peer);
        try
        {
            window.Show();
            ((TabControl)window.FindName("SettingsTabs")).SelectedItem = window.FindName("DualMachineTab");
            await Task.Delay(100); window.UpdateLayout();
            var roles = (ComboBox)window.FindName("DualMachineRoleBox");
            roles.SelectedValue = DualMachineRole.Data;
            check(((SettingsViewModel)window.DataContext).BuildUpdatedConfig().DualMachine.IsData && config.DualMachine.IsDisplay,
                "dual: Chinese role selector edits draft without switching active transport");
            await Task.Delay(80); window.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(directory, "dual-machine-settings.png")); png.Save(file);
            check(((TextBlock)window.FindName("DualMachineStatusText")).Text.Contains("已连接") && roles.ActualWidth > 200,
                "dual: settings show live peer status with readable controls");
        }
        finally { window.Close(); }
    }

    private static async Task<bool> RejectAsync(Func<Task> action) { try { await action(); return false; } catch { return true; } }
    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!condition()) { if (DateTime.UtcNow >= until) throw new TimeoutException("dual-machine test wait timed out"); await Task.Delay(20); }
    }
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private sealed class Capture(Action<SpeechSource, long, byte[]> send, Action<string>? failed = null) : IDualMachineCapture
    {
        private readonly CancellationTokenSource _stop = new();
        private Task _pump = Task.CompletedTask;
        public bool Stopped { get; private set; }
        public void Fail(string message) => failed?.Invoke(message);
        public void Start() => _pump = Task.Run(async () =>
        {
            long sequence = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    send(SpeechSource.Microphone, ++sequence, Enumerable.Repeat((byte)17, 3200).ToArray());
                    send(SpeechSource.Speaker, sequence, Enumerable.Repeat((byte)34, 3200).ToArray());
                }
            }
            catch (OperationCanceledException) { }
        });
        public async ValueTask DisposeAsync() { _stop.Cancel(); await _pump; Stopped = true; _stop.Dispose(); }
    }
    private sealed class Screenshots : IScreenshotService
    {
        public byte[] Png { get; }
        public int Calls { get; private set; }
        public System.Drawing.Rectangle Region { get; private set; }
        public Screenshots()
        {
            var pixels = new byte[256 * 256 * 4]; new Random(17).NextBytes(pixels);
            var bitmap = BitmapSource.Create(256, 256, 96, 96, PixelFormats.Bgra32, null, pixels, 1024);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output); Png = output.ToArray();
        }
        public Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken token) { Calls++; return Task.FromResult<ScreenshotCaptureResult?>(new() { ImageBytes = Png }); }
        public Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken token) => CaptureAsync(token);
        public Task<ScreenshotCaptureResult?> CaptureRegionAsync(System.Drawing.Rectangle region, CancellationToken token) { Region = region; return CaptureAsync(token); }
    }
    private sealed class Router : IModelRouter
    {
        public Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken token) => Task.FromResult(new ModelReply
        {
            Text = config.Primary.SystemPrompt.Contains("hasQuestion") ? "{\"hasQuestion\":true,\"title\":\"两数之和\",\"questionMarkdown\":\"给定数组，求两数之和。\"}" : "## 思路\n哈希表\n## 复杂度\nO(n)\n## 代码\n```java\nreturn result;\n```"
        });
    }
    private sealed class Logger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) => throw new InvalidOperationException(message, ex);
    }
}
