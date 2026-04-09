using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FloatingDeskAssistant.Application;

public sealed class RemoteCaptureServer : IDisposable
{
    private const int PreferredPort = 38647;
    private const int MaxPortAttempts = 10;

    private readonly ILoggerService _logger;
    private readonly Func<RemoteCaptureTriggerResult> _triggerCapture;
    private readonly Func<int, int, RemoteBallMoveResult> _moveBall;
    private readonly Func<string> _statusTextProvider;
    private readonly Func<bool> _compactModeEnabledProvider;
    private readonly Func<bool, Task<RemoteCompactModeResult>> _setCompactModeAsync;
    private readonly Func<IReadOnlyList<RemotePromptPresetInfo>> _promptPresetProvider;
    private readonly Func<string, RemotePromptPresetTriggerResult> _triggerPromptPreset;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public RemoteCaptureServer(
        ILoggerService logger,
        Func<RemoteCaptureTriggerResult> triggerCapture,
        Func<int, int, RemoteBallMoveResult> moveBall,
        Func<string> statusTextProvider,
        Func<bool> compactModeEnabledProvider,
        Func<bool, Task<RemoteCompactModeResult>> setCompactModeAsync,
        Func<IReadOnlyList<RemotePromptPresetInfo>> promptPresetProvider,
        Func<string, RemotePromptPresetTriggerResult> triggerPromptPreset)
    {
        _logger = logger;
        _triggerCapture = triggerCapture;
        _moveBall = moveBall;
        _statusTextProvider = statusTextProvider;
        _compactModeEnabledProvider = compactModeEnabledProvider;
        _setCompactModeAsync = setCompactModeAsync;
        _promptPresetProvider = promptPresetProvider;
        _triggerPromptPreset = triggerPromptPreset;
    }

    public int Port { get; private set; }

    public IReadOnlyList<string> AccessUrls { get; private set; } = Array.Empty<string>();

    public RemoteCaptureStartResult Start()
    {
        if (_listener is not null)
        {
            return new RemoteCaptureStartResult(Port, AccessUrls);
        }

        Exception? lastException = null;
        for (var offset = 0; offset < MaxPortAttempts; offset++)
        {
            var candidatePort = PreferredPort + offset;
            try
            {
                var listener = new TcpListener(IPAddress.Any, candidatePort);
                listener.Start();

                _listener = listener;
                _cts = new CancellationTokenSource();
                Port = candidatePort;
                AccessUrls = BuildAccessUrls(candidatePort);
                _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                _logger.Info($"Remote capture server started on port {candidatePort}. URLs: {string.Join(", ", AccessUrls)}");
                return new RemoteCaptureStartResult(candidatePort, AccessUrls);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastException = ex;
            }
        }

        throw new IOException("Failed to start remote capture server on any candidate port.", lastException);
    }

    public void Stop()
    {
        var cts = _cts;
        var listener = _listener;

        _cts = null;
        _listener = null;
        AccessUrls = Array.Empty<string>();
        Port = 0;

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
        }

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }
        finally
        {
            _acceptLoopTask = null;
            cts?.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info($"Remote capture listener stopping: {ex.Message}");
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Remote capture accept loop failed.", ex);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
            }

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                await WritePlainTextResponseAsync(stream, 400, "Bad Request", "Malformed request.").ConfigureAwait(false);
                return;
            }

            var method = requestParts[0].ToUpperInvariant();
            var rawTarget = requestParts[1];
            if (!Uri.TryCreate($"http://localhost{rawTarget}", UriKind.Absolute, out var uri))
            {
                await WritePlainTextResponseAsync(stream, 400, "Bad Request", "Malformed URL.").ConfigureAwait(false);
                return;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            switch (path)
            {
                case "/":
                    await WriteHtmlResponseAsync(
                        stream,
                        200,
                        "OK",
                        BuildIndexHtml(_statusTextProvider(), null, _compactModeEnabledProvider(), _promptPresetProvider())).ConfigureAwait(false);
                    break;
                case "/capture" when method is "GET" or "POST":
                    var triggerResult = _triggerCapture();
                    await WriteHtmlResponseAsync(
                        stream,
                        200,
                        "OK",
                        BuildCaptureResultHtml(_statusTextProvider(), triggerResult, _compactModeEnabledProvider(), _promptPresetProvider())).ConfigureAwait(false);
                    break;
                case "/compact/toggle" when method is "GET" or "POST":
                    var compactModeResult = await _setCompactModeAsync(!_compactModeEnabledProvider()).ConfigureAwait(false);
                    await WriteHtmlResponseAsync(
                        stream,
                        compactModeResult.Accepted ? 200 : 500,
                        compactModeResult.Accepted ? "OK" : "Internal Server Error",
                        BuildCompactModeResultHtml(_statusTextProvider(), compactModeResult, _promptPresetProvider())).ConfigureAwait(false);
                    break;
                case "/preset/send" when method is "GET" or "POST":
                    var presetQuery = ParseQueryString(uri.Query);
                    if (!presetQuery.TryGetValue("id", out var presetId) || string.IsNullOrWhiteSpace(presetId))
                    {
                        await WriteHtmlResponseAsync(
                            stream,
                            400,
                            "Bad Request",
                            BuildPromptPresetResultHtml(
                                _statusTextProvider(),
                                new RemotePromptPresetTriggerResult(false, "Missing preset id."),
                                _compactModeEnabledProvider(),
                                _promptPresetProvider())).ConfigureAwait(false);
                        break;
                    }

                    var promptPresetResult = _triggerPromptPreset(presetId);
                    await WriteHtmlResponseAsync(
                        stream,
                        promptPresetResult.Accepted ? 200 : 409,
                        promptPresetResult.Accepted ? "OK" : "Conflict",
                        BuildPromptPresetResultHtml(
                            _statusTextProvider(),
                            promptPresetResult,
                            _compactModeEnabledProvider(),
                            _promptPresetProvider())).ConfigureAwait(false);
                    break;
                case "/ball/move" when method is "GET" or "POST":
                    var query = ParseQueryString(uri.Query);
                    if (!TryGetIntQueryValue(query, "dx", out var deltaX)
                        || !TryGetIntQueryValue(query, "dy", out var deltaY))
                    {
                        await WriteJsonResponseAsync(stream, 400, "Bad Request", new
                        {
                            accepted = false,
                            message = "Expected integer dx and dy query parameters."
                        }).ConfigureAwait(false);
                        break;
                    }

                    deltaX = Math.Clamp(deltaX, -120, 120);
                    deltaY = Math.Clamp(deltaY, -120, 120);
                    var moveResult = _moveBall(deltaX, deltaY);
                    await WriteJsonResponseAsync(stream, moveResult.Accepted ? 200 : 409, moveResult.Accepted ? "OK" : "Conflict", new
                    {
                        accepted = moveResult.Accepted,
                        message = moveResult.Message,
                        dx = deltaX,
                        dy = deltaY
                    }).ConfigureAwait(false);
                    break;
                case "/status":
                    await WriteJsonResponseAsync(stream, 200, "OK", new
                    {
                        statusText = _statusTextProvider(),
                        compactModeEnabled = _compactModeEnabledProvider(),
                        promptPresets = _promptPresetProvider().Select(item => new
                        {
                            id = item.Id,
                            title = item.Title,
                            summary = item.Summary
                        }),
                        port = Port,
                        accessUrls = AccessUrls
                    }).ConfigureAwait(false);
                    break;
                case "/favicon.ico":
                    await WriteEmptyResponseAsync(stream, 204, "No Content").ConfigureAwait(false);
                    break;
                default:
                    await WritePlainTextResponseAsync(stream, 404, "Not Found", "Not Found").ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warn($"Remote capture request ended unexpectedly: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Remote capture request failed.", ex);
        }
    }

    private static IReadOnlyList<string> BuildAccessUrls(int port)
    {
        var urls = GetPreferredLocalIPv4Addresses()
            .Select(address => $"http://{address}:{port}/")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            urls.Add($"http://127.0.0.1:{port}/");
        }

        return urls;
    }

    private static IEnumerable<string> GetPreferredLocalIPv4Addresses()
    {
        var scoredAddresses = new List<(string Address, int Score)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var text = $"{nic.Name} {nic.Description}";
            var score = 10;
            if (text.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || text.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            if (text.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }

            if (text.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            {
                score -= 100;
            }

            if (nic.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.Any.Equals(gateway.Address)
                    && !IPAddress.None.Equals(gateway.Address)))
            {
                score += 50;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                {
                    continue;
                }

                var octets = address.GetAddressBytes();
                if (octets[0] == 169 && octets[1] == 254)
                {
                    continue;
                }

                scoredAddresses.Add((address.ToString(), score));
            }
        }

        return scoredAddresses
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Address)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildIndexHtml(
        string statusText,
        string? notice,
        bool compactModeEnabled,
        IReadOnlyList<RemotePromptPresetInfo> promptPresets)
    {
        var encodedStatus = WebUtility.HtmlEncode(statusText);
        var encodedCompactModeState = WebUtility.HtmlEncode(compactModeEnabled ? "Enabled" : "Disabled");
        var encodedCompactModeAction = WebUtility.HtmlEncode(compactModeEnabled ? "Disable Compact Mode" : "Enable Compact Mode");
        var encodedNotice = string.IsNullOrWhiteSpace(notice)
            ? string.Empty
            : $"<div class=\"notice\">{WebUtility.HtmlEncode(notice)}</div>";
        var presetHtml = BuildPromptPresetHtml(promptPresets);

        return """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta http-equiv="Cache-Control" content="no-store" />
  <title>willGo Remote Capture</title>
  <style>
    :root { color-scheme: dark; }
    body {
      margin: 0;
      font-family: "Segoe UI", Arial, sans-serif;
      background: linear-gradient(180deg, #141414 0%, #202635 100%);
      color: #ffffff;
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 18px;
      box-sizing: border-box;
    }
    .card {
      width: min(92vw, 430px);
      border-radius: 20px;
      padding: 24px;
      background: rgba(255,255,255,0.08);
      border: 1px solid rgba(255,255,255,0.12);
      box-shadow: 0 18px 50px rgba(0,0,0,0.35);
      backdrop-filter: blur(16px);
    }
    h1 { font-size: 24px; margin: 0 0 8px; }
    p { color: #d8deea; line-height: 1.6; margin: 0 0 14px; }
    form { margin: 0; }
    button {
      width: 100%;
      border: 0;
      border-radius: 14px;
      padding: 16px;
      font-size: 18px;
      font-weight: 700;
      color: #08111f;
      background: linear-gradient(180deg, #8bd3ff 0%, #4fb6ff 100%);
      box-shadow: 0 12px 24px rgba(79,182,255,0.35);
      cursor: pointer;
    }
    .secondary-action {
      margin-top: 12px;
      color: #ecf6ff;
      background: linear-gradient(180deg, #4d5f86 0%, #2e3b58 100%);
      box-shadow: 0 10px 20px rgba(26,35,56,0.35);
    }
    .panel {
      margin-top: 16px;
      padding: 14px;
      border-radius: 14px;
      background: rgba(0,0,0,0.22);
      color: #f2f5fb;
      white-space: pre-wrap;
      word-break: break-word;
    }
    .notice {
      margin-top: 14px;
      padding: 12px;
      border-radius: 12px;
      background: rgba(79,182,255,0.18);
      color: #ffffff;
    }
    .links {
      display: flex;
      gap: 12px;
      margin-top: 14px;
    }
    a {
      color: #8bd3ff;
      text-decoration: none;
      font-weight: 600;
    }
    .section-title {
      margin: 20px 0 10px;
      font-size: 15px;
      font-weight: 700;
      letter-spacing: 0.02em;
      color: #f6f8fc;
    }
    .hint {
      color: #b8c4d9;
      font-size: 13px;
      margin: 8px 0 0;
    }
    .joystick-wrap {
      margin-top: 16px;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .joystick {
      width: 220px;
      height: 220px;
      border-radius: 999px;
      position: relative;
      touch-action: none;
      user-select: none;
      background:
        radial-gradient(circle at center, rgba(139,211,255,0.16) 0 26%, transparent 26% 100%),
        radial-gradient(circle at center, rgba(255,255,255,0.06) 0 60%, rgba(255,255,255,0.03) 60% 100%);
      border: 1px solid rgba(255,255,255,0.16);
      box-shadow: inset 0 10px 28px rgba(0,0,0,0.26), 0 12px 26px rgba(0,0,0,0.25);
    }
    .joystick::before,
    .joystick::after {
      content: "";
      position: absolute;
      inset: 50%;
      transform: translate(-50%, -50%);
      background: rgba(255,255,255,0.08);
      pointer-events: none;
    }
    .joystick::before {
      width: 2px;
      height: 84%;
    }
    .joystick::after {
      height: 2px;
      width: 84%;
    }
    .joystick-knob {
      width: 76px;
      height: 76px;
      border-radius: 999px;
      position: absolute;
      left: 50%;
      top: 50%;
      transform: translate(-50%, -50%);
      background: linear-gradient(180deg, #b6ebff 0%, #54b9ff 100%);
      box-shadow: 0 10px 22px rgba(84,185,255,0.45);
      border: 1px solid rgba(255,255,255,0.45);
      transition: transform 0.05s linear;
      pointer-events: none;
    }
    .move-status {
      margin-top: 12px;
      text-align: center;
      color: #d8deea;
      min-height: 20px;
      font-size: 14px;
    }
    .preset-list {
      display: grid;
      gap: 10px;
      margin-top: 12px;
    }
    .preset-form {
      margin: 0;
    }
    .preset-button {
      color: #f3f7ff;
      background: linear-gradient(180deg, #5f7398 0%, #394766 100%);
      box-shadow: 0 10px 20px rgba(22,28,43,0.34);
      font-size: 16px;
    }
    .preset-summary {
      margin: 6px 4px 0;
    }
    .empty-state {
      margin-top: 12px;
      padding: 14px;
      border-radius: 14px;
      background: rgba(0,0,0,0.22);
      color: #d8deea;
    }
  </style>
</head>
<body>
  <main class="card">
    <h1>willGo Remote Capture</h1>
    <p>Leave your mouse inside VMware. Tap the button below and the host will capture the full desktop, then send it to the model.</p>
    <form method="post" action="/capture">
      <button type="submit">Capture Host Desktop</button>
    </form>
    <div class="section-title">Remote View Mode</div>
    <p>Toggle compact mode remotely, same as pressing the Compact button in willGo.</p>
    <form method="post" action="/compact/toggle">
      <button type="submit" class="secondary-action">__COMPACT_ACTION__</button>
    </form>
    <p class="hint">Current compact mode: __COMPACT_STATUS__</p>
    <div class="section-title">Preset Questions</div>
    <p>Tap one preset below to send a prepared oral-question prompt directly into the chat history.</p>
    __PRESET_HTML__
    __NOTICE__
    <div class="section-title">Remote Ball Control</div>
    <p>Push and hold the joystick below. The floating ball keeps moving like a remote car; release to stop instantly.</p>
    <div class="joystick-wrap">
      <div class="joystick" id="joystick" aria-label="Remote ball joystick">
        <div class="joystick-knob" id="joystickKnob"></div>
      </div>
    </div>
    <div class="move-status" id="moveStatus">Joystick idle.</div>
    <p class="hint">Tip: push lightly to fine-tune, push near the edge to move faster.</p>
    <div class="panel">Current status: __STATUS__</div>
    <div class="links">
      <a href="/">Refresh</a>
      <a href="/status">JSON Status</a>
    </div>
  </main>
  <script>
    (() => {
      const joystick = document.getElementById('joystick');
      const knob = document.getElementById('joystickKnob');
      const moveStatus = document.getElementById('moveStatus');
      const maxRadius = 72;
      const deadZone = 12;
      const tickMs = 34;
      const minStep = 0.75;
      const maxStep = 14;
      const speedExponent = 1.7;
      const maxInFlight = 2;
      let pointerId = null;
      let vectorX = 0;
      let vectorY = 0;
      let frameHandle = null;
      let lastTickAt = 0;
      let inFlightCount = 0;
      let carryX = 0;
      let carryY = 0;
      let linkInterrupted = false;

      function updateStatus(text) {
        moveStatus.textContent = text;
      }

      function resetMotionState() {
        lastTickAt = 0;
        carryX = 0;
        carryY = 0;
        linkInterrupted = false;
      }

      function resetStick() {
        vectorX = 0;
        vectorY = 0;
        knob.style.transform = 'translate(-50%, -50%)';
        resetMotionState();
        updateStatus('Joystick idle.');
      }

      function updateStick(clientX, clientY) {
        const rect = joystick.getBoundingClientRect();
        const centerX = rect.left + rect.width / 2;
        const centerY = rect.top + rect.height / 2;
        let dx = clientX - centerX;
        let dy = clientY - centerY;
        const distance = Math.hypot(dx, dy);
        if (distance > maxRadius && distance > 0) {
          const ratio = maxRadius / distance;
          dx *= ratio;
          dy *= ratio;
        }

        vectorX = dx;
        vectorY = dy;
        knob.style.transform = `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px))`;

        const activeDistance = Math.hypot(dx, dy);
        if (activeDistance < deadZone) {
          updateStatus('Hold and drag to move the floating ball.');
          return;
        }

        const horizontal = dx > 8 ? 'right' : dx < -8 ? 'left' : '';
        const vertical = dy > 8 ? 'down' : dy < -8 ? 'up' : '';
        const direction = [vertical, horizontal].filter(Boolean).join('-') || 'moving';
        const speed = Math.round(100 * Math.min(1, activeDistance / maxRadius));
        updateStatus(`Moving ${direction} (${speed}%). Release to stop.`);
      }

      function sendMove(dx, dy) {
        inFlightCount += 1;
        fetch(`/ball/move?dx=${dx}&dy=${dy}`, {
          method: 'POST',
          cache: 'no-store',
          keepalive: true
        })
          .catch(() => {
            if (!linkInterrupted) {
              updateStatus('Control link interrupted. Please retry.');
              linkInterrupted = true;
            }
          })
          .finally(() => {
            inFlightCount = Math.max(0, inFlightCount - 1);
          });
      }

      function tick(now) {
        if (pointerId === null) {
          frameHandle = null;
          return;
        }

        frameHandle = window.requestAnimationFrame(tick);
        if (now - lastTickAt < tickMs) {
          return;
        }

        lastTickAt = now;
        const distance = Math.hypot(vectorX, vectorY);
        if (distance < deadZone) {
          carryX = 0;
          carryY = 0;
          return;
        }

        if (inFlightCount >= maxInFlight) {
          return;
        }

        const normalizedX = vectorX / maxRadius;
        const normalizedY = vectorY / maxRadius;
        const intensity = Math.min(1, distance / maxRadius);
        const speed = minStep + Math.pow(intensity, speedExponent) * (maxStep - minStep);
        const desiredX = normalizedX * speed + carryX;
        const desiredY = normalizedY * speed + carryY;
        const dx = Math.trunc(desiredX);
        const dy = Math.trunc(desiredY);

        carryX = desiredX - dx;
        carryY = desiredY - dy;

        if (dx === 0 && dy === 0) {
          return;
        }

        sendMove(dx, dy);
      }

      function startLoop() {
        if (frameHandle !== null) {
          return;
        }

        resetMotionState();
        frameHandle = window.requestAnimationFrame(tick);
      }

      function stopLoop() {
        if (frameHandle === null) {
          return;
        }

        window.cancelAnimationFrame(frameHandle);
        frameHandle = null;
        resetMotionState();
      }

      joystick.addEventListener('pointerdown', (event) => {
        pointerId = event.pointerId;
        joystick.setPointerCapture(pointerId);
        updateStick(event.clientX, event.clientY);
        startLoop();
      });

      joystick.addEventListener('pointermove', (event) => {
        if (event.pointerId !== pointerId) {
          return;
        }

        updateStick(event.clientX, event.clientY);
      });

      function release(event) {
        if (event.pointerId !== pointerId) {
          return;
        }

        try {
          joystick.releasePointerCapture(pointerId);
        } catch (_) {
        }

        pointerId = null;
        stopLoop();
        resetStick();
      }

      joystick.addEventListener('pointerup', release);
      joystick.addEventListener('pointercancel', release);
      joystick.addEventListener('lostpointercapture', () => {
        pointerId = null;
        stopLoop();
        resetStick();
      });
    })();
  </script>
</body>
</html>
"""
            .Replace("__STATUS__", encodedStatus, StringComparison.Ordinal)
            .Replace("__COMPACT_STATUS__", encodedCompactModeState, StringComparison.Ordinal)
            .Replace("__COMPACT_ACTION__", encodedCompactModeAction, StringComparison.Ordinal)
            .Replace("__PRESET_HTML__", presetHtml, StringComparison.Ordinal)
            .Replace("__NOTICE__", encodedNotice, StringComparison.Ordinal);
    }

    private static string BuildPromptPresetHtml(IReadOnlyList<RemotePromptPresetInfo> promptPresets)
    {
        if (promptPresets.Count == 0)
        {
            return "<div class=\"empty-state\">No enabled prompt presets yet. Open Settings on the desktop app and add one.</div>";
        }

        var builder = new StringBuilder();
        builder.Append("<div class=\"preset-list\">");
        foreach (var preset in promptPresets)
        {
            builder.Append("<form class=\"preset-form\" method=\"post\" action=\"/preset/send?id=")
                .Append(Uri.EscapeDataString(preset.Id))
                .Append("\">")
                .Append("<button type=\"submit\" class=\"preset-button\">")
                .Append(WebUtility.HtmlEncode(preset.Title))
                .Append("</button>");

            if (!string.IsNullOrWhiteSpace(preset.Summary))
            {
                builder.Append("<div class=\"hint preset-summary\">")
                    .Append(WebUtility.HtmlEncode(preset.Summary))
                    .Append("</div>");
            }

            builder.Append("</form>");
        }

        builder.Append("</div>");
        return builder.ToString();
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static bool TryGetIntQueryValue(IReadOnlyDictionary<string, string> query, string key, out int value)
    {
        value = 0;
        return query.TryGetValue(key, out var rawValue)
            && int.TryParse(rawValue, out value);
    }

    private static string BuildCaptureResultHtml(
        string statusText,
        RemoteCaptureTriggerResult result,
        bool compactModeEnabled,
        IReadOnlyList<RemotePromptPresetInfo> promptPresets)
    {
        var notice = result.Accepted
            ? result.Message
            : $"Capture was not accepted: {result.Message}";
        return BuildIndexHtml(statusText, notice, compactModeEnabled, promptPresets);
    }

    private static string BuildCompactModeResultHtml(
        string statusText,
        RemoteCompactModeResult result,
        IReadOnlyList<RemotePromptPresetInfo> promptPresets)
    {
        var notice = result.Accepted
            ? result.Message
            : $"Compact mode update failed: {result.Message}";
        return BuildIndexHtml(statusText, notice, result.CompactModeEnabled, promptPresets);
    }

    private static string BuildPromptPresetResultHtml(
        string statusText,
        RemotePromptPresetTriggerResult result,
        bool compactModeEnabled,
        IReadOnlyList<RemotePromptPresetInfo> promptPresets)
    {
        var notice = result.Accepted
            ? result.Message
            : $"Prompt preset was not accepted: {result.Message}";
        return BuildIndexHtml(statusText, notice, compactModeEnabled, promptPresets);
    }

    private static Task WriteHtmlResponseAsync(NetworkStream stream, int statusCode, string statusText, string html)
    {
        return WriteResponseAsync(stream, statusCode, statusText, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    private static Task WritePlainTextResponseAsync(NetworkStream stream, int statusCode, string statusText, string text)
    {
        return WriteResponseAsync(stream, statusCode, statusText, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
    }

    private static Task WriteJsonResponseAsync(NetworkStream stream, int statusCode, string statusText, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return WriteResponseAsync(stream, statusCode, statusText, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    }

    private static Task WriteEmptyResponseAsync(NetworkStream stream, int statusCode, string statusText)
    {
        return WriteResponseAsync(stream, statusCode, statusText, "text/plain; charset=utf-8", Array.Empty<byte>());
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, byte[] body)
    {
        var header = $"HTTP/1.1 {statusCode} {statusText}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
    }
}

public sealed record RemoteCaptureStartResult(int Port, IReadOnlyList<string> AccessUrls);

public sealed record RemoteCaptureTriggerResult(bool Accepted, string Message);

public sealed record RemoteCompactModeResult(bool Accepted, string Message, bool CompactModeEnabled);

public sealed record RemotePromptPresetInfo(string Id, string Title, string Summary);

public sealed record RemotePromptPresetTriggerResult(bool Accepted, string Message);

