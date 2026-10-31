using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;

namespace FloatingDeskAssistant.Application;

internal sealed class DualMachineConnection : IAsyncDisposable
{
    private sealed record Outgoing(PeerFrame Frame, TaskCompletionSource? Sent = null);
    private readonly TcpClient _client;
    private readonly SslStream _stream;
    private readonly Channel<Outgoing> _outgoing = Channel.CreateBounded<Outgoing>(128);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<PeerFrame, Task> _receive;
    private readonly Action<Exception?> _closed;
    private Task _running = Task.CompletedTask;
    private int _aborted;
    private int _disposed;

    public DualMachineConnection(TcpClient client, SslStream stream, Func<PeerFrame, Task> receive, Action<Exception?> closed)
    {
        _client = client; _stream = stream; _receive = receive; _closed = closed;
    }

    public void Start() => _running = RunAsync();

    public async Task SendAsync(PeerFrameKind kind, byte[] payload, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _outgoing.Writer.WriteAsync(new(new(kind, payload), sent), timeout.Token).ConfigureAwait(false);
        await sent.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    public bool TrySendAudio(SpeechSource source, Guid session, long sequence, byte[] pcm) =>
        _outgoing.Writer.TryWrite(new(new(source == SpeechSource.Microphone ? PeerFrameKind.Microphone : PeerFrameKind.Speaker,
            DualMachineProtocol.EncodeAudio(session, sequence, pcm))));

    private async Task RunAsync()
    {
        Exception? error = null;
        var reader = ReadAsync();
        var writer = WriteAsync();
        var heartbeat = HeartbeatAsync();
        try
        {
            var completed = await Task.WhenAny(reader, writer, heartbeat).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
        catch (Exception ex) { if (!_lifetime.IsCancellationRequested) error = ex; }
        finally
        {
            Abort();
            try { await Task.WhenAll(reader, writer, heartbeat).ConfigureAwait(false); } catch { }
            while (_outgoing.Reader.TryRead(out var item)) item.Sent?.TrySetException(new IOException("双机连接已关闭。"));
            _closed(error);
        }
    }

    private async Task ReadAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var frame = await DualMachineProtocol.ReadAsync(_stream, timeout.Token).ConfigureAwait(false);
            if (frame.Kind == PeerFrameKind.Ping) await SendAsync(PeerFrameKind.Pong, [], _lifetime.Token).ConfigureAwait(false);
            else if (frame.Kind != PeerFrameKind.Pong) await _receive(frame).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync()
    {
        await foreach (var item in _outgoing.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await DualMachineProtocol.WriteAsync(_stream, item.Frame, timeout.Token).ConfigureAwait(false);
                item.Sent?.TrySetResult();
            }
            catch (Exception ex) { item.Sent?.TrySetException(ex); throw; }
        }
    }

    private async Task HeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            await SendAsync(PeerFrameKind.Ping, [], _lifetime.Token).ConfigureAwait(false);
    }

    public void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0) return;
        _outgoing.Writer.TryComplete();
        _lifetime.Cancel();
        _client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Abort();
        await _running.ConfigureAwait(false);
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stream.Dispose();
        _lifetime.Dispose();
    }
}
