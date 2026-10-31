using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Application;

public sealed class DualMachineService : IAsyncDisposable
{
    private readonly Func<AppConfig> _getConfig;
    private readonly IScreenshotService _screenshots;
    private readonly Func<DualMachineIdentity> _identityFactory;
    private readonly Func<string, string, Action<SpeechSource, long, byte[]>, Action<string>, IDualMachineCapture> _captureFactory;
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly SemaphoreSlim _screenshotGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PeerReply>> _pending = new();
    private readonly ConcurrentDictionary<long, Task> _operations = new();
    private long _operationId;
    private CancellationTokenSource _lifetime = new();
    private TcpListener? _listener;
    private Task _accepting = Task.CompletedTask;
    private DualMachineConnection? _connection;
    private DualMachineIdentity? _identity;
    private IDualMachineCapture? _capture;
    private Guid _captureSession;
    private IncomingImage? _image;
    private string _configurationKey = "";
    private bool _disposed;
    private long _generation;
    private DualMachineConfig _options = new();
    private sealed record IncomingImage(Guid Id, byte[] Bytes) { public int Written { get; set; } }

    public DualMachineService(Func<AppConfig> getConfig, IScreenshotService screenshots,
        Func<DualMachineIdentity>? identityFactory = null,
        Func<string, string, Action<SpeechSource, long, byte[]>, Action<string>, IDualMachineCapture>? captureFactory = null)
    {
        _getConfig = getConfig; _screenshots = screenshots;
        _identityFactory = identityFactory ?? (() => DualMachineIdentity.LoadOrCreate());
        _captureFactory = captureFactory ?? ((mic, speaker, send, failed) => new DualMachineAudioCapture(mic, speaker, send, failed));
    }

    public bool IsConnected => Volatile.Read(ref _connection) is not null;
    public bool IsData => _options.IsData;
    public bool IsDisplay => _options.IsDisplay;
    public bool IsAudioStreaming => _capture is not null;
    public string Status { get; private set; } = "单机模式";
    public string PeerName { get; private set; } = "";
    public IReadOnlyList<string> PairingOffers { get; private set; } = [];
    public event Action? StateChanged;
    public event Action<PeerAudio>? AudioReceived;
    public event Action<Guid, string>? AudioFailed;
    public event Action<ScreenshotCaptureResult>? ScreenshotReceived;
    public event Action<string>? TextReceived;
    public event Action<DisplayControlAction, CancellationToken>? DisplayControlReceived;
    public void AbortConnection() => _connection?.Abort();

    private void SetStatus(string value) { Status = value; StateChanged?.Invoke(); }

    public async Task ApplyAsync(DualMachineConfig config, CancellationToken token = default, bool force = false)
    {
        await _configurationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var options = config.Clone();
            var key = $"{options.Enabled}:{options.Role}:{options.Port}:{options.PeerOffer}";
            if (!force && key == _configurationKey && (!options.Enabled || IsConnected || _listener is not null)) return;
            await StopCoreAsync().ConfigureAwait(false);
            _options = options;
            _configurationKey = key;
            if (!options.Enabled) { SetStatus("单机模式"); return; }
            if (options.IsData)
            {
                _identity = _identityFactory();
                _listener = new(IPAddress.Any, options.Port);
                _listener.Start(4);
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                    .Select(item => item.Address).Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address) && DualMachineOffer.IsLocalAddress(address))
                    .Distinct().Select(address => address.ToString()).ToList();
                if (addresses.Count == 0) addresses.Add("127.0.0.1");
                PairingOffers = addresses.Select(host => new DualMachineOffer(host, options.Port, _identity.Secret, _identity.Fingerprint).ToString()).ToArray();
                SetStatus($"数据端等待连接 · 端口 {options.Port}");
                _accepting = AcceptAsync(_listener, _identity, _generation, _lifetime.Token);
            }
            else
            {
                SetStatus("显示端正在连接数据端…");
                await ConnectAsync(DualMachineOffer.Parse(options.PeerOffer), _generation, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await StopCoreAsync().ConfigureAwait(false);
            SetStatus("双机连接失败：" + ex.Message);
            throw;
        }
        finally { _configurationGate.Release(); }
    }

    public Task ReconnectAsync(CancellationToken token = default) => ApplyAsync(_getConfig().DualMachine, token, force: true);

    public async Task ResetPairingAsync()
    {
        if (!IsData) throw new InvalidOperationException("请先保存为数据端模式。");
        await _configurationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            using var identity = DualMachineIdentity.LoadOrCreate(reset: true);
            _configurationKey = "";
        }
        finally { _configurationGate.Release(); }
        await ApplyAsync(_getConfig().DualMachine, force: true).ConfigureAwait(false);
    }

    private async Task AcceptAsync(TcpListener listener, DualMachineIdentity identity, long generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            SslStream? stream = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !DualMachineOffer.IsLocalAddress(remote.Address)) { client.Dispose(); continue; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                stream = new SslStream(client.GetStream(), false);
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = identity.Certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                }, timeout.Token).ConfigureAwait(false);
                var frame = await DualMachineProtocol.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
                var hello = DualMachineProtocol.Decode(frame.Payload);
                if (frame.Kind != PeerFrameKind.Hello || hello.Version != 1 || hello.Secret.Length != 64 ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hello.Secret), Encoding.ASCII.GetBytes(identity.Secret)))
                    throw new AuthenticationException("配对凭据不匹配。");
                var error = IsConnected ? "数据端已连接另一台显示端，请先断开原连接。" : "";
                await DualMachineProtocol.WriteAsync(stream, new(PeerFrameKind.Welcome,
                    DualMachineProtocol.Encode(new(Name: Environment.MachineName, Error: error, DisplayControlVersion: 1))), timeout.Token).ConfigureAwait(false);
                if (error.Length != 0) { stream.Dispose(); client.Dispose(); continue; }
                timeout.Token.ThrowIfCancellationRequested();
                if (generation != _generation) throw new OperationCanceledException();
                InstallConnection(client, stream, hello.Name, generation, hello.DisplayControlVersion == 1);
                client = null; stream = null;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // Failed handshakes never replace an authenticated peer or expose pairing credentials.
            }
            catch (Exception) when (token.IsCancellationRequested) { break; }
            finally { stream?.Dispose(); client?.Dispose(); }
        }
    }

    private async Task ConnectAsync(DualMachineOffer offer, long generation, CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        SslStream? stream = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await client.ConnectAsync(offer.Host, offer.Port, timeout.Token).ConfigureAwait(false);
            stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) => offer.MatchesCertificate(certificate));
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "willGo-data", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            }, timeout.Token).ConfigureAwait(false);
            await DualMachineProtocol.WriteAsync(stream, new(PeerFrameKind.Hello, DualMachineProtocol.Encode(new(Secret: offer.Secret, Name: Environment.MachineName, DisplayControlVersion: 1))), timeout.Token).ConfigureAwait(false);
            var frame = await DualMachineProtocol.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
            var welcome = DualMachineProtocol.Decode(frame.Payload);
            if (frame.Kind != PeerFrameKind.Welcome || welcome.Version != 1) throw new InvalidDataException("两端版本不兼容，请安装同一版本。");
            if (welcome.Error.Length > 0) throw new InvalidOperationException(welcome.Error);
            if (generation != _generation) throw new OperationCanceledException();
            InstallConnection(client, stream, welcome.Name, generation, welcome.DisplayControlVersion == 1);
        }
        catch { stream?.Dispose(); client.Dispose(); throw; }
    }

    private void InstallConnection(TcpClient client, SslStream stream, string name, long generation, bool supportsDisplayControl)
    {
        DualMachineConnection? connection = null;
        connection = new(client, stream, frame => ReceiveAsync(connection!, generation, frame),
            error => TrackOperation(() => HandleClosedAsync(connection!, generation, error)), supportsDisplayControl);
        _connection = connection;
        PeerName = name.Length > 80 ? name[..80] : name;
        SetStatus($"{(IsData ? "数据端" : "显示端")}已连接 · {PeerName}");
        connection.Start();
    }

    private async Task HandleClosedAsync(DualMachineConnection connection, long generation, Exception? error)
    {
        if (generation != _generation || Interlocked.CompareExchange(ref _connection, null, connection) != connection) return;
        _image = null;
        foreach (var pending in _pending.Values) pending.TrySetException(new IOException("双机连接已断开，请在设置中重新连接。"));
        _pending.Clear();
        AudioFailed?.Invoke(Guid.Empty, "双机连接已断开，语音采集已停止，请重新连接后开始战斗。");
        SetStatus(IsData ? "数据端等待重新连接 · 采集已停止" : "显示端连接已断开 · 请在双机设置中重新连接");
        if (IsData)
        {
            await _captureGate.WaitAsync().ConfigureAwait(false);
            try { if (generation == _generation && _connection is null) await StopDataCaptureCoreAsync().ConfigureAwait(false); }
            finally { _captureGate.Release(); }
        }
        // Run completion must finish before disposing its own connection.
        _ = Task.Run(async () => await connection.DisposeAsync().ConfigureAwait(false));
    }

    private async Task ReceiveAsync(DualMachineConnection connection, long generation, PeerFrame frame)
    {
        if (generation != _generation || !ReferenceEquals(connection, _connection)) return;
        if (IsData)
        {
            if (frame.Kind != PeerFrameKind.Command) throw new InvalidDataException("数据端收到不支持的消息。");
            var command = DualMachineProtocol.Decode(frame.Payload);
            // Long capture operations must not block heartbeat or incoming stop commands.
            if (_operations.Count >= 8) throw new InvalidDataException("双机控制请求过多。");
            TrackOperation(() => ExecuteCommandAsync(connection, generation, command));
            return;
        }
        switch (frame.Kind)
        {
            case PeerFrameKind.DisplayControl:
                if (!connection.SupportsDisplayControl) throw new InvalidDataException("对端未协商远程快捷键能力。");
                var control = DualMachineProtocol.DecodeDisplayControl(frame.Payload);
                connection.AcceptDisplayControl(control);
                DisplayControlReceived?.Invoke(control.Action, connection.ClosedToken);
                break;
            case PeerFrameKind.Microphone:
            case PeerFrameKind.Speaker:
                AudioReceived?.Invoke(DualMachineProtocol.DecodeAudio(frame));
                break;
            case PeerFrameKind.Reply:
                var reply = DualMachineProtocol.Decode(frame.Payload);
                if (reply.Command == "text-question")
                {
                    if (reply.Name.Length is 0 or > 8000) throw new InvalidDataException("远端问题长度无效。");
                    TextReceived?.Invoke(reply.Name);
                }
                else if (reply.Command == "audio-failed") AudioFailed?.Invoke(reply.Session, reply.Error);
                else if (_pending.TryGetValue(reply.Id, out var pending)) pending.TrySetResult(new(reply));
                break;
            case PeerFrameKind.ImageStart:
                var image = DualMachineProtocol.Decode(frame.Payload);
                if (_image is not null || image.Size is < 24 or > DualMachineProtocol.MaxImageBytes)
                    throw new InvalidDataException("远端截图大小无效。");
                _image = new(image.Id, new byte[image.Size]);
                break;
            case PeerFrameKind.ImageChunk:
                if (frame.Payload.Length <= 20 || _image is null || new Guid(frame.Payload.AsSpan(0, 16)) != _image.Id ||
                    BinaryPrimitives.ReadInt32BigEndian(frame.Payload.AsSpan(16)) != _image.Written || frame.Payload.Length - 20 > _image.Bytes.Length - _image.Written)
                    throw new InvalidDataException("远端截图分块顺序或长度错误。");
                frame.Payload.AsSpan(20).CopyTo(_image.Bytes.AsSpan(_image.Written));
                _image.Written += frame.Payload.Length - 20;
                break;
            case PeerFrameKind.ImageEnd:
                var end = DualMachineProtocol.Decode(frame.Payload);
                var completed = _image;
                _image = null;
                if (completed is null || completed.Id != end.Id || completed.Written != completed.Bytes.Length)
                    throw new InvalidDataException("远端截图传输不完整。");
                ValidateImage(completed.Bytes);
                if (_pending.TryGetValue(end.Id, out var waiting)) waiting.TrySetResult(new(end, completed.Bytes));
                else if (end.Command == "push") ScreenshotReceived?.Invoke(new() { ImageBytes = completed.Bytes });
                break;
            default: throw new InvalidDataException("显示端收到不支持的消息。");
        }
        await Task.CompletedTask;
    }

    private static void ValidateImage(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        if (!bytes.AsSpan(0, 8).SequenceEqual(signature) || width is <= 0 or > 32768 || height is <= 0 or > 32768 || (long)width * height > 100_000_000)
            throw new InvalidDataException("远端截图不是有效或可显示的 PNG 图片。");
    }

    private async Task ExecuteCommandAsync(DualMachineConnection connection, long generation, PeerControl command)
    {
        try
        {
            if (command.Command == "screenshot")
            {
                await SendScreenshotAsync(connection, generation, command.Id, false).ConfigureAwait(false);
                return;
            }
            await _captureGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                if (generation != _generation || !ReferenceEquals(connection, _connection)) return;
                if (command.Command == "start-audio")
                {
                    if (_capture is not null) throw new InvalidOperationException("数据端正在采集，请先停止。");
                    if (command.Session == Guid.Empty) throw new InvalidDataException("采集会话标识无效。");
                    _captureSession = command.Session;
                    var failedOnce = 0;
                    void CaptureFailed(string message)
                    {
                        if (Interlocked.Exchange(ref failedOnce, 1) == 0) DataCaptureFailed(connection, command.Session, message);
                    }
                    var options = _getConfig().Battle;
                    _capture = _captureFactory(options.MicrophoneDeviceId, options.SpeakerDeviceId,
                        (source, sequence, pcm) =>
                        {
                            if (!connection.TrySendAudio(source, command.Session, sequence, pcm))
                                CaptureFailed("局域网发送队列已满或连接断开，采集已停止。");
                        }, CaptureFailed);
                    try { _capture.Start(); }
                    catch { await _capture.DisposeAsync().ConfigureAwait(false); _capture = null; throw; }
                    SetStatus($"数据端正在传输音频 · {PeerName}");
                }
                else if (command.Command == "stop-audio")
                {
                    if (_capture is not null && command.Session != _captureSession) throw new InvalidOperationException("采集会话不匹配。");
                    await StopDataCaptureCoreAsync().ConfigureAwait(false);
                }
                else throw new InvalidDataException("不支持的双机操作。");
                await connection.SendAsync(PeerFrameKind.Reply, DualMachineProtocol.Encode(new(command.Id, command.Command, command.Session))).ConfigureAwait(false);
            }
            finally { _captureGate.Release(); }
        }
        catch (Exception ex)
        {
            try { await connection.SendAsync(PeerFrameKind.Reply, DualMachineProtocol.Encode(new(command.Id, command.Command, command.Session, Error: ex.Message))).ConfigureAwait(false); }
            catch { connection.Abort(); }
        }
    }

    private void DataCaptureFailed(DualMachineConnection connection, Guid session, string message)
    {
        TrackOperation(async () =>
        {
            try
            {
                await _captureGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_captureSession != session || !ReferenceEquals(connection, _connection)) return;
                    await StopDataCaptureCoreAsync().ConfigureAwait(false);
                }
                finally { _captureGate.Release(); }
                await connection.SendAsync(PeerFrameKind.Reply, DualMachineProtocol.Encode(new(Command: "audio-failed", Session: session, Error: message))).ConfigureAwait(false);
            }
            catch { connection.Abort(); }
        });
    }

    private async Task StopDataCaptureCoreAsync()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null) await capture.DisposeAsync().ConfigureAwait(false);
        _captureSession = Guid.Empty;
        if (IsData && IsConnected) SetStatus($"数据端已连接 · {PeerName}");
        else StateChanged?.Invoke();
    }

    private async Task StopDataCaptureAsync()
    {
        await _captureGate.WaitAsync().ConfigureAwait(false);
        try { await StopDataCaptureCoreAsync().ConfigureAwait(false); }
        finally { _captureGate.Release(); }
    }

    public async Task StartAudioAsync(Guid session, CancellationToken token) => await RequestAsync("start-audio", session, token).ConfigureAwait(false);
    public async Task StopAudioAsync(Guid session, CancellationToken token = default) => await RequestAsync("stop-audio", session, token).ConfigureAwait(false);

    private async Task<PeerReply> RequestAsync(string command, Guid session, CancellationToken token)
    {
        if (!IsDisplay || _connection is not { } connection) throw new InvalidOperationException("显示端尚未连接数据端，请先在双机设置中配对。");
        var id = Guid.NewGuid();
        var pending = new TaskCompletionSource<PeerReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledged = false;
        _pending[id] = pending;
        try
        {
            await connection.SendAsync(PeerFrameKind.Command, DualMachineProtocol.Encode(new(id, command, session)), token).ConfigureAwait(false);
            var reply = await pending.Task.WaitAsync(TimeSpan.FromSeconds(command == "screenshot" ? 40 : 15), token).ConfigureAwait(false);
            acknowledged = true;
            if (reply.Control.Error.Length > 0) throw new InvalidOperationException(reply.Control.Error);
            return reply;
        }
        catch (Exception) when (command == "start-audio" && !acknowledged)
        {
            // An unacknowledged start must not leave an unattended microphone running.
            connection.Abort();
            throw;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    public async Task<ScreenshotCaptureResult?> RequestScreenshotAsync(CancellationToken token)
    {
        var reply = await RequestAsync("screenshot", Guid.Empty, token).ConfigureAwait(false);
        return reply.Image is null ? throw new InvalidDataException("数据端未返回截图。") : new() { ImageBytes = reply.Image };
    }

    public async Task PushScreenshotAsync(CancellationToken token = default)
    {
        if (!IsData || _connection is not { } connection) throw new InvalidOperationException("数据端尚未连接显示端。");
        await SendScreenshotAsync(connection, _generation, Guid.NewGuid(), true, token).ConfigureAwait(false);
    }

    public async Task PushTextAsync(string text, CancellationToken token = default)
    {
        if (!IsData || _connection is not { } connection) throw new InvalidOperationException("数据端尚未连接显示端。");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8000) throw new ArgumentException("问题必须为 1 到 8000 个字符。");
        await connection.SendAsync(PeerFrameKind.Reply, DualMachineProtocol.Encode(new(Command: "text-question", Name: text)), token).ConfigureAwait(false);
    }

    public bool TrySendDisplayControl(DisplayControlAction action, out string error)
    {
        error = "";
        if (!Enum.IsDefined(action)) { error = "不支持的快捷键操作。"; return false; }
        if (!IsData || _connection is not { } connection)
        { error = "数据端尚未连接显示端，快捷键操作未发送。"; return false; }
        if (!connection.SupportsDisplayControl)
        { error = "显示端不支持远程快捷键，请将两端更新至 2.0.1 或更新版本。"; return false; }
        if (connection.TrySendDisplayControl(action)) return true;
        connection.Abort();
        error = "快捷键发送队列已满或连接断开，请重新连接。";
        return false;
    }

    private async Task SendScreenshotAsync(DualMachineConnection connection, long generation, Guid id, bool push, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        if (!await _screenshotGate.WaitAsync(0, timeout.Token).ConfigureAwait(false)) throw new InvalidOperationException("数据端正在传输另一张截图，请稍后重试。");
        try
        {
            if (generation != _generation || !ReferenceEquals(connection, _connection)) throw new IOException("双机连接已变化。");
            var config = _getConfig();
            ScreenshotCaptureResult? image;
            if (config.RemoteCaptureMode == RemoteCaptureMode.PresetRegion)
            {
                if (config.RemoteCaptureRegionWidth < 6 || config.RemoteCaptureRegionHeight < 6)
                    throw new InvalidOperationException("请在数据端设置中选择并保存截图区域。");
                image = await _screenshots.CaptureRegionAsync(new(config.RemoteCaptureRegionX, config.RemoteCaptureRegionY, config.RemoteCaptureRegionWidth, config.RemoteCaptureRegionHeight), timeout.Token).ConfigureAwait(false);
            }
            else image = await _screenshots.CaptureFullDesktopAsync(timeout.Token).ConfigureAwait(false);
            if (image is null || image.ImageBytes.Length is < 24 or > DualMachineProtocol.MaxImageBytes)
                throw new InvalidOperationException("截图为空或超过 16 MB，请在数据端缩小截图区域。");
            ValidateImage(image.ImageBytes);
            await connection.SendAsync(PeerFrameKind.ImageStart, DualMachineProtocol.Encode(new(id, Size: image.ImageBytes.Length)), timeout.Token).ConfigureAwait(false);
            try
            {
                for (var offset = 0; offset < image.ImageBytes.Length; offset += DualMachineProtocol.ImageChunkBytes)
                {
                    var size = Math.Min(DualMachineProtocol.ImageChunkBytes, image.ImageBytes.Length - offset);
                    var chunk = new byte[20 + size];
                    id.TryWriteBytes(chunk);
                    BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(16), offset);
                    image.ImageBytes.AsSpan(offset, size).CopyTo(chunk.AsSpan(20));
                    await connection.SendAsync(PeerFrameKind.ImageChunk, chunk, timeout.Token).ConfigureAwait(false);
                }
                await connection.SendAsync(PeerFrameKind.ImageEnd, DualMachineProtocol.Encode(new(id, Command: push ? "push" : "screenshot")), timeout.Token).ConfigureAwait(false);
            }
            catch { connection.Abort(); throw; }
        }
        finally { _screenshotGate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_connection is not null) AudioFailed?.Invoke(Guid.Empty, "双机连接已重置，请连接成功后重新开始战斗。");
        Interlocked.Increment(ref _generation);
        _lifetime.Cancel();
        _listener?.Stop(); _listener = null;
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(_operations.Values).ConfigureAwait(false);
        await StopDataCaptureAsync().ConfigureAwait(false);
        foreach (var pending in _pending.Values) pending.TrySetException(new IOException("双机连接已重置。"));
        _pending.Clear(); _image = null;
        _identity?.Dispose(); _identity = null;
        PairingOffers = []; PeerName = "";
        _lifetime.Dispose(); _lifetime = new();
    }

    private void TrackOperation(Func<Task> action)
    {
        var id = Interlocked.Increment(ref _operationId);
        var task = Task.Run(action);
        _operations[id] = task;
        _ = task.ContinueWith(completed =>
        {
            _operations.TryRemove(id, out _);
            _ = completed.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        await _configurationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
        finally { _configurationGate.Release(); }
    }
}
