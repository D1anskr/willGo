using System.Threading.Channels;

namespace FloatingDeskAssistant.Application;

public sealed class SpeechFinalizationQueue : IAsyncDisposable
{
    private sealed record Pending(SpeechSource Source, StreamingSpeechResult Result, long Sequence);
    private readonly Channel<Pending> _queue = Channel.CreateBounded<Pending>(8);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private readonly IUtteranceRecognizer _recognizer;
    private readonly Action<SpeechSegment> _publish;
    private readonly SpeechTermProtection _protection;
    private volatile bool _flushWithFallback;

    public SpeechFinalizationQueue(IUtteranceRecognizer recognizer, string hotwords, Action<SpeechSegment> publish)
    {
        _recognizer = recognizer;
        _protection = new(hotwords);
        _publish = publish;
        _pump = Task.Run(RunAsync);
    }

    public bool TryEnqueue(SpeechSource source, StreamingSpeechResult result, long sequence) => _queue.Writer.TryWrite(new(source, result, sequence));

    private async Task RunAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync())
            {
                if (_stop.IsCancellationRequested && !_flushWithFallback) break;
                string text;
                var warning = "";
                try { text = await _recognizer.RecognizeAsync(item.Result.Audio!, item.Result.SampleRate, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested && !_flushWithFallback) { break; }
                catch (Exception)
                {
                    text = item.Result.Text;
                    warning = _flushWithFallback ? "停止采集时本句使用流式结果。" : "整句精确识别失败，本句已使用流式结果；可关闭再开启语音识别后重试。";
                }
                if (_stop.IsCancellationRequested && !_flushWithFallback) break;
                _publish(new(item.Source, _protection.SelectFinalText(item.Result.Text, text), true, warning, item.Sequence, item.Result.CapturedAt));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
    }

    public async Task CompleteWithFallbackAsync()
    {
        _flushWithFallback = true;
        _queue.Writer.TryComplete();
        _stop.Cancel();
        await _pump.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _stop.Cancel();
        await _pump.ConfigureAwait(false);
        _stop.Dispose();
    }
}
