using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FloatingDeskAssistant.Infrastructure.Security;

namespace FloatingDeskAssistant.Application;

public sealed record DualMachineOffer(string Host, int Port, string Secret, string Fingerprint)
{
    public override string ToString() => $"willgo-pair://{Host}:{Port}/{Secret}?key={Fingerprint}";

    public static DualMachineOffer Parse(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "willgo-pair" ||
            !IPAddress.TryParse(uri.Host, out var address) || !IsLocalAddress(address) || uri.Port is < 1024 or > 65535 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("配对信息无效，请完整复制数据端生成的局域网配对信息。");
        var secret = uri.AbsolutePath.TrimStart('/');
        var fingerprint = uri.Query.StartsWith("?key=", StringComparison.Ordinal) ? uri.Query[5..] : "";
        if (secret.Length != 64 || fingerprint.Length != 64 || !secret.All(Uri.IsHexDigit) || !fingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("配对信息不完整，请重新从数据端复制。");
        return new(uri.Host, uri.Port, secret, fingerprint);
    }

    public static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254;
    }

    public bool MatchesCertificate(X509Certificate? certificate) => certificate is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), Convert.FromHexString(Fingerprint));
}

public sealed class DualMachineIdentity : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public string Secret { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    private DualMachineIdentity(byte[] pfx, string secret)
    {
        Certificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        Secret = secret;
    }

    public static DualMachineIdentity Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=willGo data", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        return new(certificate.Export(X509ContentType.Pfx), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public static DualMachineIdentity LoadOrCreate(bool reset = false, string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDeskAssistant");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "dual-machine.identity");
        var protector = new ApiKeyProtector();
        if (!reset && File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<string[]>(protector.Unprotect(File.ReadAllText(path)));
            if (saved is not { Length: 2 } || saved[1].Length != 64) throw new InvalidDataException("本机配对身份损坏，请在双机设置中重置配对。");
            return new(Convert.FromBase64String(saved[0]), saved[1]);
        }
        var identity = Create();
        try
        {
            var content = JsonSerializer.Serialize(new[] { Convert.ToBase64String(identity.Certificate.Export(X509ContentType.Pfx)), identity.Secret });
            File.WriteAllText(path + ".tmp", protector.Protect(content));
            File.Move(path + ".tmp", path, true);
            return identity;
        }
        catch { identity.Dispose(); throw; }
    }

    public void Dispose() => Certificate.Dispose();
}

public enum PeerFrameKind : byte { Hello = 1, Welcome, Command, Reply, Microphone, Speaker, ImageStart, ImageChunk, ImageEnd, Ping, Pong, DisplayControl }
public enum DisplayControlAction { ToggleClickThrough = 1, PreviousQuestion, NextQuestion, ScrollUp, ScrollDown, DecreaseOpacity, IncreaseOpacity }
public sealed record PeerDisplayControl(long Sequence, DisplayControlAction Action);
public sealed record PeerFrame(PeerFrameKind Kind, byte[] Payload);
public sealed record PeerControl(Guid Id = default, string Command = "", Guid Session = default, string Error = "", string Name = "", int Size = 0, string Secret = "", int Version = 1, int DisplayControlVersion = 0);
public sealed record PeerReply(PeerControl Control, byte[]? Image = null);
public sealed record PeerAudio(Guid Session, SpeechSource Source, long Sequence, byte[] Pcm);

public static class DualMachineProtocol
{
    public const int MaxFrameBytes = 65536;
    public const int MaxImageBytes = 16 * 1024 * 1024;
    public const int ImageChunkBytes = 48 * 1024;
    public static byte[] Encode(PeerControl control) => JsonSerializer.SerializeToUtf8Bytes(control);
    public static PeerControl Decode(byte[] payload) => JsonSerializer.Deserialize<PeerControl>(payload) ?? throw new InvalidDataException("双机控制消息为空。");
    public static byte[] EncodeDisplayControl(PeerDisplayControl control) => JsonSerializer.SerializeToUtf8Bytes(control);
    public static PeerDisplayControl DecodeDisplayControl(byte[] payload)
    {
        var control = JsonSerializer.Deserialize<PeerDisplayControl>(payload);
        if (control is null || control.Sequence <= 0 || !Enum.IsDefined(control.Action))
            throw new InvalidDataException("远程快捷键指令无效。");
        return control;
    }

    public static async Task<PeerFrame> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        if (!Enum.IsDefined((PeerFrameKind)header[0]) || length is < 0 or > MaxFrameBytes)
            throw new InvalidDataException("双机数据帧无效或超出上限。");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return new((PeerFrameKind)header[0], payload);
    }

    public static async Task WriteAsync(Stream stream, PeerFrame frame, CancellationToken token)
    {
        if (frame.Payload.Length > MaxFrameBytes) throw new InvalidDataException("双机数据帧过大。");
        var header = new byte[5];
        header[0] = (byte)frame.Kind;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), frame.Payload.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(frame.Payload, token).ConfigureAwait(false);
    }

    public static byte[] EncodeAudio(Guid session, long sequence, byte[] pcm)
    {
        if (pcm.Length is 0 or > 6400 || pcm.Length % 2 != 0) throw new InvalidDataException("双机音频必须为 16 kHz 单声道 PCM16。");
        var result = new byte[24 + pcm.Length];
        session.TryWriteBytes(result);
        BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(16), sequence);
        pcm.CopyTo(result, 24);
        return result;
    }

    public static PeerAudio DecodeAudio(PeerFrame frame)
    {
        if (frame.Payload.Length is <= 24 or > 6424 || frame.Payload.Length % 2 != 0)
            throw new InvalidDataException("双机音频帧无效。");
        return new(new Guid(frame.Payload.AsSpan(0, 16)), frame.Kind == PeerFrameKind.Microphone ? SpeechSource.Microphone : SpeechSource.Speaker,
            BinaryPrimitives.ReadInt64BigEndian(frame.Payload.AsSpan(16)), frame.Payload[24..]);
    }
}
