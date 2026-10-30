using System.IO;
using System.Reflection;
using FloatingDeskAssistant.Application;

internal static class SpeechEchoTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        SpeechSegment Segment(SpeechSource source, string text, int sequence = 0, int offset = 0) =>
            new(source, text, true, Sequence: sequence, CapturedAt: now.AddMilliseconds(offset));
        var speaker = Segment(SpeechSource.Speaker, "我想一想，MySQL 的索引为什么使用 B+ 树？");
        var mic = Segment(SpeechSource.Microphone, "我想一想 MYSQL 的索引为什么使用B+树", offset: 200);
        var filter = new SpeechEchoFilter();
        check(filter.Accept(speaker, now).Single() == speaker && filter.Accept(mic, now).Count == 0,
            "speaker results dispatch immediately while microphone finals wait briefly for echo matching");
        check(filter.Drain(now.AddSeconds(2)).Count == 0, "cross-device echoes differing in punctuation and case are suppressed");
        check(filter.Accept(speaker, now.AddSeconds(2)).Count == 0, "a repeated final event with the same source sequence and audio timestamp is ignored");

        filter = new();
        filter.Accept(mic, now);
        check(filter.Accept(speaker, now.AddSeconds(1)).Count == 1 && filter.Drain(now.AddSeconds(2)).Count == 0,
            "microphone-first delivery still preserves only the authoritative speaker final");
        var slow = new SpeechEchoFilter();
        slow.Accept(mic, now);
        slow.Accept(speaker with { IsFinal = false, CapturedAt = null }, now.AddMilliseconds(800));
        check(slow.Drain(now.AddSeconds(2)).Count == 0 && slow.Accept(speaker, now.AddSeconds(3)).Count == 1 && slow.Drain(now.AddSeconds(4)).Count == 0,
            "matching speaker partial text holds a microphone echo until a delayed speaker final arrives");
        slow = new(); slow.Accept(mic, now); slow.Accept(speaker with { IsFinal = false }, now.AddSeconds(1));
        check(slow.Drain(now.AddSeconds(6)).Single() == mic, "an unconfirmed speaker partial cannot permanently discard a genuine microphone final");
        var own = Segment(SpeechSource.Microphone, "我想一想，Kafka 的消费组如何实现", 1, 300);
        filter.Accept(own, now.AddSeconds(2));
        check(filter.Drain(now.AddSeconds(4)).Single() == own, "genuine microphone speech is retained even while the speaker is active");
        var later = Segment(SpeechSource.Microphone, mic.Text, 2, 10000);
        filter.Accept(later, now.AddSeconds(10));
        check(filter.Drain(now.AddSeconds(12)).Single() == later, "repeating a question later is not removed by a session-wide text blacklist");
        filter.Accept(Segment(SpeechSource.Microphone, "好的", 3), now.AddSeconds(13));
        filter.Accept(Segment(SpeechSource.Speaker, "好的", 3), now.AddSeconds(13));
        check(filter.Drain(now.AddSeconds(15)).Count == 1, "ambiguous short acknowledgements are preserved");

        filter = new();
        filter.Accept(Segment(SpeechSource.Speaker, "请解释一下线程池的核心线程数量为什么设置为16个"), now);
        filter.Accept(Segment(SpeechSource.Microphone, "请解释一下线程池的核心线程数量为什么设置为18个"), now);
        check(filter.Drain(now.AddSeconds(2)).Count == 1, "similar questions with different numeric parameters are not treated as echoes");
        filter = new();
        filter.Accept(Segment(SpeechSource.Speaker, "请详细解释为什么这里可以使用数据库索引进行查询"), now);
        filter.Accept(Segment(SpeechSource.Microphone, "请详细解释为什么这里不可以使用数据库索引进行查询"), now);
        check(filter.Drain(now.AddSeconds(2)).Count == 1, "changed negation preserves a distinct microphone question");
        filter = new();
        filter.Accept(Segment(SpeechSource.Speaker, "请详细解释数据库为什么使用B树进行索引查询"), now);
        filter.Accept(Segment(SpeechSource.Microphone, "请详细解释数据库为什么使用B+树进行索引查询"), now);
        check(filter.Drain(now.AddSeconds(2)).Count == 1, "technical plus and hash symbols are not stripped when comparing distinct questions");
        filter = new();
        filter.Accept(Segment(SpeechSource.Speaker, "请详细介绍数据库索引的底层数据结构和查询流程"), now);
        filter.Accept(Segment(SpeechSource.Microphone, "请详细介绍数据库索引的底层数据结构和查询流成"), now);
        check(filter.Drain(now.AddSeconds(2)).Count == 0, "long echo transcripts tolerate a small recognition discrepancy");
        filter = new();
        filter.Accept(Segment(SpeechSource.Speaker, "MySQL的索引结构是什么", 0, -800), now);
        filter.Accept(Segment(SpeechSource.Speaker, "为什么要选择B加树", 1), now);
        filter.Accept(Segment(SpeechSource.Microphone, "MySQL的索引结构是什么为什么要选择B加树"), now);
        check(filter.Drain(now.AddSeconds(2)).Count == 0, "echo matching handles different sentence boundaries between the two devices");
        filter = new();
        filter.Accept(own, now);
        check(filter.Drain(now, finish: true).Single() == own, "stopping capture flushes genuine pending microphone speech without waiting");
        filter = new(false);
        check(filter.Accept(mic, now).Single() == mic && filter.Accept(speaker, now).Single() == speaker,
            "disabling echo suppression leaves both devices' text available immediately");

        var directory = Path.Combine(AppContext.BaseDirectory, "echo-archive-tests", Guid.NewGuid().ToString("N"));
        var accepted = new List<SpeechSegment>();
        var errors = new List<string>();
        var archive = new SpeechSessionArchive(directory, 16000, 16000, errors.Add, false);
        var path = archive.TranscriptPath;
        await using var service = new BattleSpeechService();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(BattleSpeechService).GetField("_archive", flags)!.SetValue(service, archive);
        service.Transcribed += accepted.Add;
        void Publish(SpeechSegment segment) => typeof(BattleSpeechService).GetMethod("Publish", flags)!.Invoke(service, [segment]);
        Publish(mic); Publish(speaker); Publish(speaker); Publish(own);
        await service.StopAsync();
        var markdown = await File.ReadAllTextAsync(path);
        check(errors.Count == 0 && accepted.Count == 2 && accepted[0] == speaker && accepted[1] == own,
            "the real service filters echo and replayed finals before question dispatch and flushes the final user sentence");
        check(markdown.Split("### ").Length - 1 == 2 && !markdown.Contains(mic.Text) && markdown.Contains(own.Text),
            "the same filtering occurs before Markdown persistence so echo text is not saved twice");
    }
}
