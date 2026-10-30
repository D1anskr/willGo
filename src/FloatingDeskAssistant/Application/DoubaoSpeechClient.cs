using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FloatingDeskAssistant.Configuration;

namespace FloatingDeskAssistant.Application;

public static class DoubaoSpeechProtocol
{
    public const string ResourceId = "volc.seedasr.sauc.duration";
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public static readonly Uri Endpoint = new("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async");
    public sealed record Response(byte[] Payload, bool IsLast, uint ErrorCode);

    public static byte[] CreateStart(BattleConfig options)
    {
        options.Speech.Validate();
        var request = new Dictionary<string, object>
        {
            ["model_name"] = "bigmodel", ["enable_nonstream"] = false,
            ["enable_itn"] = true, ["enable_punc"] = true, ["enable_ddc"] = false,
            ["show_utterances"] = true, ["result_type"] = "full",
            ["end_window_size"] = (int)(options.Clone().SpeechPauseSeconds * 1000)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            user = new { uid = "willgo" },
            audio = new { format = "pcm", codec = "raw", rate = 16000, bits = 16, channel = 1 },
            request
        });
        return Encode(1, 0, 1, payload);
    }

    public static byte[] CreateAudio(ReadOnlySpan<byte> pcm, bool last = false) => Encode(2, last ? 2 : 0, 0, pcm);

    public static byte[] Encode(int type, int flags, int serialization, ReadOnlySpan<byte> payload)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(payload);
        var data = compressed.ToArray();
        var packet = new byte[8 + data.Length];
        packet[0] = 0x11;
        packet[1] = (byte)((type << 4) | flags);
        packet[2] = (byte)((serialization << 4) | 1);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), data.Length);
        data.CopyTo(packet, 8);
        return packet;
    }

    public static Response Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 8 || packet.Length > MaxMessageBytes || packet[0] >> 4 != 1)
            throw new InvalidDataException("豆包语音返回了无效的数据包。");
        var offset = (packet[0] & 15) * 4;
        var type = packet[1] >> 4;
        var flags = packet[1] & 15;
        if (offset < 4 || offset > packet.Length - 4 || type is not (9 or 15))
            throw new InvalidDataException("豆包语音返回了不支持的数据包。");
        uint errorCode = 0;
        if (type == 15)
        {
            errorCode = BinaryPrimitives.ReadUInt32BigEndian(packet[offset..]);
            offset += 4;
        }
        else if ((flags & 1) != 0) offset += 4;
        if (offset > packet.Length - 4) throw new InvalidDataException("豆包语音数据包不完整。");
        var length = BinaryPrimitives.ReadInt32BigEndian(packet[offset..]);
        offset += 4;
        if (length < 0 || length != packet.Length - offset) throw new InvalidDataException("豆包语音数据包长度不匹配。");
        var data = packet[offset..].ToArray();
        var compression = packet[2] & 15;
        if (compression == 1)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxMessageBytes) throw new InvalidDataException("豆包语音结果超过大小限制。");
                output.Write(buffer, 0, read);
            }
            data = output.ToArray();
        }
        else if (compression != 0) throw new InvalidDataException("豆包语音返回了不支持的压缩格式。");
        return new(data, (flags & 2) != 0 || type == 15, errorCode);
    }

    public static string DescribeError(uint code) => code switch
    {
        45000001 => "豆包语音请求参数无效，请确认已开通流式语音识别模型 2.0（小时版）。",
        45000081 => "豆包语音接收音频超时，请检查网络后重新开启识别。",
        45000151 => "豆包语音不接受当前音频格式。",
        55000031 => "豆包语音服务繁忙，请稍后重试。",
        _ => $"豆包语音服务返回错误（{code}），请检查账户额度、服务权限及网络。"
    };
}

public sealed class DoubaoSpeechResults
{
    private readonly SpeechSource _source;
    private DateTimeOffset _started;
    private readonly Action<SpeechSegment> _publish;
    private readonly HashSet<long> _finalized = [];
    private long _sequence;
    private string _partial = "";
    public bool HasUnconfirmedText => _partial.Length > 0;

    public DoubaoSpeechResults(SpeechSource source, DateTimeOffset started, Action<SpeechSegment> publish)
    {
        _source = source; _started = started; _publish = publish;
    }

    public void BeginAudio(DateTimeOffset started) => _started = started;

    public void Accept(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.TryGetProperty("code", out var code) && code.TryGetUInt32(out var number) && number is not (0 or 20000000))
            throw new InvalidOperationException(DoubaoSpeechProtocol.DescribeError(number));
        if (!root.TryGetProperty("result", out var result)) return;
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray()) AcceptResult(item);
        }
        else AcceptResult(result);
    }

    private void AcceptResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("utterances", out var utterances) || utterances.ValueKind != JsonValueKind.Array)
            return;
        var partials = new List<string>();
        foreach (var utterance in utterances.EnumerateArray())
        {
            var text = utterance.TryGetProperty("text", out var value) ? value.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            // The service omits the first utterance's zero-valued start_time.
            long start = 0;
            if (utterance.TryGetProperty("start_time", out var startValue) && (!startValue.TryGetInt64(out start) || start < 0))
                throw new InvalidDataException("豆包语音未返回有效的分句时间。");
            if (_finalized.Contains(start)) continue;
            var definite = utterance.TryGetProperty("definite", out var final) && final.ValueKind == JsonValueKind.True;
            if (definite)
            {
                _finalized.Add(start);
                var end = utterance.TryGetProperty("end_time", out var endValue) && endValue.TryGetInt64(out var ms) ? ms : start;
                end = Math.Clamp(end, start, 24 * 60 * 60 * 1000L);
                _publish(new(_source, text, true, Sequence: _sequence++, CapturedAt: _started.AddMilliseconds(end)));
                _partial = "";
            }
            else partials.Add(text);
        }
        var partial = string.Join("", partials);
        if (partial != _partial)
        {
            _partial = partial;
            _publish(new(_source, partial, false, Sequence: _sequence));
        }
    }
}

public sealed class DoubaoSpeechClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DoubaoSpeechResults _results;
    private readonly Action<string> _failed;
    private readonly BattleConfig _options;
    private readonly Uri _endpoint;
    private Task _receive = Task.CompletedTask;
    private int _completing;
    private bool _expectedLast;
    public DateTimeOffset StartedAt { get; private set; }

    public DoubaoSpeechClient(BattleConfig options, SpeechSource source, Action<SpeechSegment> publish, Action<string> failed, Uri? endpoint = null)
    {
        _options = options.Clone(); _failed = failed;
        _endpoint = endpoint ?? DoubaoSpeechProtocol.Endpoint;
        StartedAt = DateTimeOffset.Now;
        _results = new(source, StartedAt, publish);
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        _socket.Options.CollectHttpResponseDetails = true;
    }

    public async Task StartAsync(CancellationToken token)
    {
        _options.Speech.Validate();
        var credentials = _options.Speech;
        if (!string.IsNullOrWhiteSpace(credentials.ApiKey))
            _socket.Options.SetRequestHeader("X-Api-Key", credentials.ApiKey.Trim());
        else
        {
            _socket.Options.SetRequestHeader("X-Api-App-Key", credentials.AppId.Trim());
            _socket.Options.SetRequestHeader("X-Api-Access-Key", credentials.AccessToken.Trim());
        }
        _socket.Options.SetRequestHeader("X-Api-Resource-Id", DoubaoSpeechProtocol.ResourceId);
        _socket.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await _socket.ConnectAsync(_endpoint, timeout.Token).ConfigureAwait(false);
            await SendPacketAsync(DoubaoSpeechProtocol.CreateStart(_options), timeout.Token).ConfigureAwait(false);
            _receive = ReceiveAsync();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { throw new TimeoutException("连接豆包语音超时，请检查网络。"); }
        catch (WebSocketException)
        {
            throw new InvalidOperationException(_socket.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "豆包语音鉴权失败，请检查 API Key / App ID / Access Token，以及模型 2.0 小时版的开通状态。",
                HttpStatusCode.TooManyRequests => "豆包语音并发或调用频率已达上限，请检查控制台配额。",
                _ => "无法连接豆包语音，请检查网络、服务开通状态及账户余额。"
            });
        }
    }

    public Task SendAudioAsync(byte[] pcm, CancellationToken token = default) => SendPacketAsync(DoubaoSpeechProtocol.CreateAudio(pcm), token);

    public void BeginAudio()
    {
        StartedAt = DateTimeOffset.Now;
        _results.BeginAudio(StartedAt);
    }

    private async Task SendPacketAsync(byte[] packet, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _socket.SendAsync(packet.AsMemory(), WebSocketMessageType.Binary, true, timeout.Token).ConfigureAwait(false);
    }

    private async Task ReceiveAsync()
    {
        try
        {
            var buffer = new byte[16384];
            while (!_lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult frame;
                do
                {
                    frame = await _socket.ReceiveAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false);
                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        if (!_expectedLast) throw new IOException("豆包语音连接已断开，请重新开启识别。");
                        return;
                    }
                    if (frame.MessageType != WebSocketMessageType.Binary || message.Length + frame.Count > DoubaoSpeechProtocol.MaxMessageBytes)
                        throw new InvalidDataException("豆包语音返回了异常消息。");
                    message.Write(buffer, 0, frame.Count);
                } while (!frame.EndOfMessage);
                var response = DoubaoSpeechProtocol.Decode(message.ToArray());
                if (response.ErrorCode != 0)
                {
                    if (response.ErrorCode == 45000002 && Volatile.Read(ref _completing) != 0) { _expectedLast = true; return; }
                    throw new InvalidOperationException(DoubaoSpeechProtocol.DescribeError(response.ErrorCode));
                }
                _results.Accept(response.Payload);
                if (response.IsLast)
                {
                    _expectedLast = true;
                    if (Volatile.Read(ref _completing) == 0) _failed("豆包语音会话已结束，请重新开启识别。");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Service responses can contain request metadata; expose only known local messages.
            _failed(ex is InvalidDataException or InvalidOperationException or IOException && ex is not WebSocketException
                ? ex.Message : "豆包语音连接异常，请检查网络后重新开启识别。");
        }
    }

    public async Task CompleteAsync()
    {
        if (Interlocked.Exchange(ref _completing, 1) != 0) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await SendPacketAsync(DoubaoSpeechProtocol.CreateAudio(new byte[3200], last: true), CancellationToken.None).ConfigureAwait(false);
                await _receive.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (!_expectedLast) _failed("豆包语音未确认停止时的尾句，请检查文字记录。");
                else if (_results.HasUnconfirmedText) _failed("停止时有一段语音未获云端最终确认，未自动提问或记为最终文字。");
                if (_expectedLast && _socket.State == WebSocketState.Open)
                {
                    using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", close.Token).ConfigureAwait(false); }
                    catch (Exception) { /* The confirmed tail is already delivered; close is best effort. */ }
                }
            }
        }
        catch (Exception) { _failed("等待豆包语音尾句超时或连接已断开；未确认的中间文字不会自动提问。"); }
        finally { _lifetime.Cancel(); _socket.Abort(); await _receive.ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _socket.Abort();
        await _receive.ConfigureAwait(false);
        _socket.Dispose();
        _lifetime.Dispose();
    }
}
