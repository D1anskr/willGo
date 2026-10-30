using System.Text;

namespace FloatingDeskAssistant.Application;

// Called under the service's publish lock, before both transcript storage and question dispatch.
public sealed class SpeechEchoFilter(bool enabled = true)
{
    private sealed record Heard(SpeechSegment Segment, DateTimeOffset Received);
    private readonly List<Heard> _speakers = [];
    private readonly List<Heard> _pending = [];
    private readonly HashSet<(SpeechSource, long, DateTimeOffset?)> _seen = [];
    private readonly Queue<(SpeechSource, long, DateTimeOffset?)> _seenOrder = [];
    private Heard? _speakerPartial;
    public static readonly TimeSpan MicrophoneDelay = TimeSpan.FromMilliseconds(1500);

    public IReadOnlyList<SpeechSegment> Accept(SpeechSegment segment, DateTimeOffset now)
    {
        var result = new List<SpeechSegment>();
        if (!segment.IsFinal)
        {
            if (segment.Source == SpeechSource.Speaker) _speakerPartial = string.IsNullOrWhiteSpace(segment.Text) ? null : new(segment, now);
            result.Add(segment); return result;
        }
        if (string.IsNullOrWhiteSpace(segment.Text)) return result;
        // Cloud finals have a stable audio timestamp; untimed synthetic/manual inputs are not deduplicated by sequence.
        if (segment.CapturedAt is not null)
        {
            var key = (segment.Source, segment.Sequence, segment.CapturedAt);
            if (!_seen.Add(key)) return result;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 1024) _seen.Remove(_seenOrder.Dequeue());
        }
        _speakers.RemoveAll(item => now - item.Received > TimeSpan.FromSeconds(30));
        if (!enabled) { result.Add(segment); return result; }
        if (segment.Source == SpeechSource.Speaker)
        {
            _speakerPartial = null;
            _speakers.Add(new(segment, now));
            result.Add(segment);
        }
        else _pending.Add(new(segment, now));
        result.AddRange(Drain(now));
        return result;
    }

    public IReadOnlyList<SpeechSegment> Drain(DateTimeOffset now, bool finish = false)
    {
        var result = new List<SpeechSegment>();
        foreach (var item in _pending.ToArray())
        {
            if (!finish && now - item.Received < MicrophoneDelay) continue;
            // Matching interim speaker text is evidence of a slow final, not a reason to discard speech yet.
            if (!finish && now - item.Received < TimeSpan.FromSeconds(5) && _speakerPartial is { } partial &&
                now - partial.Received < TimeSpan.FromSeconds(5) && Similar(Normalize(item.Segment.Text), Normalize(partial.Segment.Text))) continue;
            if (!IsEcho(item)) result.Add(item.Segment);
            _pending.Remove(item);
        }
        return result;
    }

    private bool IsEcho(Heard microphone)
    {
        var end = microphone.Segment.CapturedAt ?? microphone.Received;
        var candidates = _speakers.Where(item => Math.Abs(((item.Segment.CapturedAt ?? item.Received) - end).TotalSeconds) <= 3)
            .OrderBy(item => item.Segment.CapturedAt ?? item.Received).ToArray();
        var text = Normalize(microphone.Segment.Text);
        foreach (var speaker in candidates)
            if (Similar(text, Normalize(speaker.Segment.Text))) return true;
        // Different devices can split the same utterance into different final sentences.
        for (var start = 0; start < candidates.Length; start++)
        {
            var joined = "";
            for (var index = start; index < Math.Min(start + 3, candidates.Length); index++)
            {
                joined += Normalize(candidates[index].Segment.Text);
                if (Similar(text, joined)) return true;
            }
        }
        return false;
    }

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormKC)
        .Where(c => char.IsLetterOrDigit(c) || c is '+' or '#').Select(char.ToLowerInvariant).ToArray());

    private static bool Similar(string microphone, string speaker)
    {
        // Very short replies are ambiguous: keep them rather than erase a genuine response.
        if (microphone.Length < 4 || speaker.Length < 4) return false;
        if (microphone == speaker) return true;
        if (microphone.Length >= 6 && speaker.Contains(microphone, StringComparison.Ordinal)) return true;
        if (Math.Min(microphone.Length, speaker.Length) < 12 || Math.Abs(microphone.Length - speaker.Length) > Math.Max(microphone.Length, speaker.Length) / 10) return false;
        // Changed numbers or negation can change the question despite otherwise similar wording.
        if (new string(microphone.Where(char.IsDigit).ToArray()) != new string(speaker.Where(char.IsDigit).ToArray()) ||
            microphone.Count(c => c is '不' or '没' or '否') != speaker.Count(c => c is '不' or '没' or '否') ||
            microphone.Count(c => c is '+' or '#') != speaker.Count(c => c is '+' or '#')) return false;
        if (microphone.Length > 600 || speaker.Length > 600) return false;
        var previous = Enumerable.Range(0, speaker.Length + 1).ToArray();
        for (var i = 1; i <= microphone.Length; i++)
        {
            var current = new int[speaker.Length + 1]; current[0] = i;
            for (var j = 1; j <= speaker.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (microphone[i - 1] == speaker[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1] <= Math.Max(microphone.Length, speaker.Length) * .1;
    }
}
