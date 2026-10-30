using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using NAudio.Wave;

internal static class SpeechArchiveTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "archive-tests", Guid.NewGuid().ToString("N"), "会话 记录");
        var failures = new List<string>();
        await using var archive = new SpeechSessionArchive(root, 48000, 44100, failures.Add);
        check(Path.GetFileNameWithoutExtension(archive.AudioPath) == $"willGo-{archive.StartedAt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}" &&
            Path.GetFileNameWithoutExtension(archive.AudioPath) == Path.GetFileNameWithoutExtension(archive.TranscriptPath),
            "audio and Markdown share a local year-month-day-hour-minute-second filename");
        var clock = Stopwatch.StartNew();
        var first = archive.StartedAt.AddMilliseconds(100);
        archive.AppendTranscript(new(SpeechSource.Microphone, "不应写入的中间结果", false));
        archive.AppendTranscript(new(SpeechSource.Microphone, "普通回答，不包含触发词。", true, CapturedAt: first));
        string liveText;
        using (var reader = new StreamReader(new FileStream(archive.TranscriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            liveText = await reader.ReadToEndAsync();
        check(liveText.Contains("普通回答") && !liveText.Contains("中间结果"), "final transcript appends immediately and excludes streaming partials without requiring a trigger");
        archive.AppendTranscript(new(SpeechSource.Speaker, "什么是 ZGC？", true, CapturedAt: first.AddMilliseconds(100)));
        for (var i = 0; i < 6; i++)
        {
            archive.AddAudio(SpeechSource.Microphone, Tone(48000, 440, i * 2400, 2400), 48000);
            archive.AddAudio(SpeechSource.Speaker, Tone(44100, 880, i * 2205, 2205), 44100);
            await Task.Delay(50);
        }
        await Task.Delay(350);
        await archive.StopAudioAsync();
        var duration = clock.Elapsed.TotalSeconds;
        archive.AppendTranscript(new(SpeechSource.Microphone, "停止时的尾句。", true, "停止采集时本句使用流式结果。", CapturedAt: first.AddMilliseconds(200)));
        await archive.DisposeAsync();
        await archive.DisposeAsync();
        var markdown = await File.ReadAllTextAsync(archive.TranscriptPath);
        check(markdown.Contains($"### {first:HH:mm:ss.fff} 我：") && markdown.Contains("对方："), "Markdown uses capture timestamps and explicit me/other labels");
        check(markdown.IndexOf("普通回答", StringComparison.Ordinal) < markdown.IndexOf("什么是 ZGC", StringComparison.Ordinal) && markdown.IndexOf("什么是 ZGC", StringComparison.Ordinal) < markdown.IndexOf("停止时的尾句", StringComparison.Ordinal), "Markdown retains alternating speakers in capture order and preserves the stopping tail");
        check(markdown.Contains("结束时间") && markdown.Contains("停止采集时本句使用流式结果"), "session footer and fallback recognition notes are persisted");
        using (var wav = new WaveFileReader(archive.AudioPath))
        {
            check(wav.WaveFormat.Channels == 2 && wav.WaveFormat.SampleRate == 16000 && wav.WaveFormat.BitsPerSample == 16, "one standard stereo PCM16 WAV contains both sources resampled to 16 kHz");
            check(Math.Abs(wav.TotalTime.TotalSeconds - duration) < 0.15, "full recording duration follows elapsed time including silent pauses");
            var bytes = new byte[(int)wav.Length];
            wav.ReadExactly(bytes);
            var left = new List<double>(); var right = new List<double>();
            for (var i = 0; i < bytes.Length; i += 4)
            {
                left.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i)) / 32768.0);
                right.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i + 2)) / 32768.0);
            }
            var frames = Math.Min(3200, left.Count);
            double Power(List<double> samples, double frequency) => Math.Sqrt(Math.Pow(samples.Take(frames).Select((v, i) => v * Math.Sin(2 * Math.PI * frequency * i / 16000)).Sum(), 2) + Math.Pow(samples.Take(frames).Select((v, i) => v * Math.Cos(2 * Math.PI * frequency * i / 16000)).Sum(), 2));
            check(Power(left, 440) > Power(left, 880) * 5 && Power(right, 880) > Power(right, 440) * 5, "left channel is microphone and right channel is speaker with no crosstalk");
            check(left.TakeLast(1600).Concat(right.TakeLast(1600)).All(v => Math.Abs(v) < 0.005), "late silence is retained rather than shortening the recording");
        }
        check(failures.Count == 0 && Directory.GetFiles(root, "*.wav").Length == 1 && Directory.GetFiles(root, "*.md").Length == 1, "one capture session produces exactly one audio file and one Markdown file");
        using (File.Open(archive.AudioPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        using (File.Open(archive.TranscriptPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        check(true, "stopping closes both files and finalizes the playable WAV header");
        await using (var next = new SpeechSessionArchive(root, 16000, 16000, failures.Add))
            check(next.AudioPath != archive.AudioPath && next.TranscriptPath != archive.TranscriptPath, "a new session never overwrites an earlier recording");
        try { await using var bad = new SpeechSessionArchive("relative-path", 16000, 16000, failures.Add); check(false, "relative archive path must fail"); }
        catch (ArgumentException) { check(true, "invalid relative save directory fails before capture starts"); }
        try { await using var bad = new SpeechSessionArchive(archive.AudioPath, 16000, 16000, failures.Add); check(false, "file cannot be a save directory"); }
        catch (IOException) { check(true, "unwritable archive location fails visibly before capture starts"); }

        var textOnlyRoot = Path.Combine(root, "text-only");
        await using (var textOnly = new SpeechSessionArchive(textOnlyRoot, 48000, 44100, failures.Add, recordAudio: false))
        {
            check(textOnly.AudioPath == "" && !Directory.EnumerateFiles(textOnlyRoot, "*.wav").Any(), "text-only session never creates an audio file, including an empty WAV");
            textOnly.AddAudio(SpeechSource.Microphone, new byte[48000 * 2], 48000);
            textOnly.AddAudio(SpeechSource.Speaker, new byte[44100 * 2], 44100);
            textOnly.AppendTranscript(new(SpeechSource.Microphone, "不保存的中间结果", false));
            textOnly.AppendTranscript(new(SpeechSource.Microphone, "只保存文字的普通回答", true, CapturedAt: first));
            textOnly.AppendTranscript(new(SpeechSource.Speaker, "LangGraph 与 MCP 是什么", true, CapturedAt: first.AddSeconds(1)));
            await textOnly.StopAudioAsync();
            textOnly.AppendTranscript(new(SpeechSource.Microphone, "停止时仍保存的文字尾句", true));
            await textOnly.DisposeAsync();
            var text = await File.ReadAllTextAsync(textOnly.TranscriptPath);
            check(text.Contains("未录制") && !text.Contains(".wav") && text.Contains("只保存文字的普通回答") && text.Contains("LangGraph 与 MCP") && !text.Contains("中间结果"), "text-only Markdown records both speakers and has no dangling audio link");
            check(text.Contains($"### {first:HH:mm:ss.fff} 我：") && text.Contains("对方：") && text.Contains("文字尾句") && text.Contains("结束时间"), "text-only session retains timestamps, source labels, stop tail and footer");
            check(Directory.GetFiles(textOnlyRoot).Length == 1 && failures.Count == 0, "text-only capture callbacks produce exactly one Markdown file without audio writer errors");
            using (File.Open(textOnly.TranscriptPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            check(true, "text-only stop releases its Markdown handle and supports repeated dispose");
        }
        await using (var audioAgain = new SpeechSessionArchive(textOnlyRoot, 16000, 16000, failures.Add))
            check(Directory.GetFiles(textOnlyRoot, "*.wav").Length == 1 && Directory.GetFiles(textOnlyRoot, "*.md").Length == 2, "a later audio-recording session creates its own files after a text-only session");

        var concurrentRoot = Path.Combine(root, "concurrent");
        var sessions = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            new SpeechSessionArchive(concurrentRoot, 16000, 16000, failures.Add, recordAudio: index % 2 == 0))));
        try
        {
            check(sessions.Select(session => session.TranscriptPath).Distinct().Count() == sessions.Length &&
                sessions.All(session => Regex.IsMatch(Path.GetFileName(session.TranscriptPath), @"^willGo-\d{14}(-(?:[2-9]|[1-9]\d+))?\.md$")),
                "concurrent audio and text-only sessions reserve distinct readable timestamp filenames");
            check(sessions.Where(session => session.AudioPath.Length > 0).All(session =>
                Path.GetFileNameWithoutExtension(session.AudioPath) == Path.GetFileNameWithoutExtension(session.TranscriptPath)),
                "collision suffixes keep audio and Markdown filenames paired");
            foreach (var session in sessions) session.AppendTranscript(new(SpeechSource.Microphone, Path.GetFileName(session.TranscriptPath), true));
        }
        finally { foreach (var session in sessions) await session.DisposeAsync(); }
        check(Directory.GetFiles(concurrentRoot, "*.wav").Length == 4 && Directory.GetFiles(concurrentRoot, "*.md").Length == 8 &&
            sessions.All(session => File.ReadAllText(session.TranscriptPath).Contains(Path.GetFileName(session.TranscriptPath))),
            "same-second sessions retain every recording and transcript without overwriting");

    }

    public static async Task RunLiveAsync(string fixturePath, Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "archive-live", Guid.NewGuid().ToString("N"));
        var loaded = await new FloatingDeskAssistant.Infrastructure.Config.AppConfigService(new FloatingDeskAssistant.Infrastructure.Security.ApiKeyProtector(), new FloatingDeskAssistant.Infrastructure.Logging.FileLogger()).LoadAsync();
        var options = loaded.Battle;
        options.SpeechArchiveDirectory = root;
        var failures = new ConcurrentQueue<string>();
        await using var service = new BattleSpeechService(() => options);
        service.Failed += failures.Enqueue;
        await service.StartAsync("", "", CancellationToken.None);
        using var source = new AudioFileReader(fixturePath);
        using var playback = new WaveOutEvent();
        playback.Init(source);
        playback.Play();
        while (playback.PlaybackState != PlaybackState.Stopped) await Task.Delay(50);
        await Task.Delay(100);
        await service.StopAsync();
        var audio = Directory.GetFiles(root, "*.wav").Single();
        var text = Directory.GetFiles(root, "*.md").Single();
        var markdown = await File.ReadAllTextAsync(text);
        check(markdown.Contains("对方：") && markdown.Contains("ZGC", StringComparison.OrdinalIgnoreCase) && markdown.Contains("结束时间"), "live loopback finalizes the last utterance into Markdown even when stopped before endpoint silence");
        using (var wav = new WaveFileReader(audio))
        {
            check(wav.TotalTime.TotalSeconds >= source.TotalTime.TotalSeconds - 0.15 && wav.WaveFormat.Channels == 2, "live full-session stereo recording covers the playback duration");
            var samples = new float[(int)(wav.Length / 2)];
            wav.ToSampleProvider().Read(samples, 0, samples.Length);
            check(samples.Where((_, i) => i % 2 == 1).Any(v => Math.Abs(v) > 0.01), "actual loopback audio is audible in the archived right channel");
        }
        var previousText = await File.ReadAllTextAsync(text);
        await service.StartAsync("", "", CancellationToken.None);
        await Task.Delay(250);
        await service.StopAsync();
        check(Directory.GetFiles(root, "*.wav").Length == 2 && Directory.GetFiles(root, "*.md").Length == 2 && await File.ReadAllTextAsync(text) == previousText, "restarting actual capture creates a separate session and leaves the first transcript intact");
        options.SaveSpeechSessions = false;
        await service.StartAsync("", "", CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync();
        check(Directory.GetFiles(root, "*.wav").Length == 2 && Directory.GetFiles(root, "*.md").Length == 2, "disabling archival keeps recognition available without producing files");
        await service.StartAsync("", "", CancellationToken.None, recordAudio: false);
        using (var textSource = new AudioFileReader(fixturePath))
        using (var textPlayback = new WaveOutEvent())
        {
            textPlayback.Init(textSource);
            textPlayback.Play();
            while (textPlayback.PlaybackState != PlaybackState.Stopped) await Task.Delay(50);
        }
        await Task.Delay(100);
        await service.StopAsync();
        var textOnly = Directory.GetFiles(root, "*.md").Single(file => File.ReadAllText(file).Contains("未录制"));
        var textMarkdown = await File.ReadAllTextAsync(textOnly);
        check(Directory.GetFiles(root, "*.wav").Length == 2 && Directory.GetFiles(root, "*.md").Length == 3, "explicit no records only Markdown during real dual-device capture even with legacy archival disabled");
        check(textMarkdown.Contains("对方：") && textMarkdown.Contains("ZGC", StringComparison.OrdinalIgnoreCase) && textMarkdown.Contains("结束时间") && !textMarkdown.Contains(".wav"), "text-only real loopback preserves final speech and stop tail without an audio link");
        await service.StartAsync("", "", CancellationToken.None, recordAudio: true);
        await Task.Delay(250);
        await service.StopAsync();
        check(Directory.GetFiles(root, "*.wav").Length == 3 && Directory.GetFiles(root, "*.md").Length == 4, "explicit yes on the same service restores audio recording for the next session");
        check(!options.SaveSpeechSessions, "real session choices preserve the saved legacy archival preference");
        check(failures.IsEmpty, "dual-device capture, archival, stop and restart complete without recording errors");
        Console.WriteLine("Live archival artifacts are private test files under the test executable directory.");
    }

    private static byte[] Tone(int rate, int frequency, int start, int count)
    {
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)(10000 * Math.Sin(2 * Math.PI * frequency * (start + i) / rate)));
        return bytes;
    }

}
