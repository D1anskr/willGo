# willGo 1.7.0

## Cloud Speech Recognition

Speech recognition now uses Doubao streaming ASR 2.0 through Volcengine.
The Windows installer no longer contains X-ASR / Qwen speech models or Sherpa-ONNX
native runtimes. Historical source and download tools are excluded from the build.

1. Register and complete the required account verification at
   https://console.volcengine.com/speech/.
2. Enable Doubao streaming ASR 2.0, hourly billing. Resource ID:
   `volc.seedasr.sauc.duration`.
3. Create an API Key at
   https://console.volcengine.com/speech/new/setting/apikeys?projectName=default.
4. In willGo Settings, enter the speech API Key and run Test Connection. Older
   accounts can use App ID plus Access Token instead. API Key takes precedence.
5. Save settings, start recognition, and choose whether this session saves audio.

Keys are masked in the settings UI and encrypted at rest using Windows DPAPI for
the current Windows user. They are not embedded in the app or installer.
Do not share personal settings or configuration backups with a recipient.

The connection test opens no audio devices and sends only a short silent sample.
Real speech recognition is not available until the service is enabled and valid
credentials are supplied; there is no offline fallback.

## Text And Recording Behavior

- Microphone and system playback use separate TLS WebSockets and preserve their
  existing source roles and microphone trigger rules.
- Audio is converted to mono PCM16 at 16 kHz, with 200 ms streaming packets.
- Cloud two-pass recognition is enabled by default. Only confirmed utterances
  generate questions or final transcript entries. Interim text is displayed live.
- No default hotwords, local technical glossary matching, local refiner or extra
  language-model rewrite is applied. Final cloud text is kept verbatim across the
  question, answer request and Markdown. Optional terms/context can be left empty.
- Repeated cumulative cloud results are deduplicated by utterance start time;
  speaking the same text later still creates a new question.
- On stop, capture stops first, then cloud final results drain before Markdown
  closes. Recording duration excludes the network drain. Unconfirmed tails never
  silently become final questions.
- Yes saves one mixed stereo WAV plus Markdown; No saves only Markdown; Cancel
  does not start. All final microphone speech is recorded in Markdown, regardless
  of whether it triggers an answer.
- Authentication, quota, service or network failures are visible and stop capture.
  Restart recognition after correcting the error. No automatic retry incurs
  hidden additional audio uploads.

Existing login validation, screenshot/phone questions, independent answer model
routes, taskbar/tray, click-through, title ordering and global shortcuts remain.

## Billing And Sources

Public price checked on 2026-10-18: ASR 2.0 hourly edition is CNY 1 per audio hour;
a 30-hour package is CNY 28, valid for one year. Two continuously uploaded streams
consume approximately two audio hours per elapsed hour. Answer models bill
separately. Actual availability, free credits and rates depend on the account.

- Model list: https://www.volcengine.com/docs/6561/2499930
- Streaming protocol: https://www.volcengine.com/docs/6561/1354869
- Terms/context: https://www.volcengine.com/docs/6561/2604976
- Pricing: https://www.volcengine.com/docs/6561/1359370

## Verification

Automated verification uses a local WebSocket service that implements the
documented binary/gzip framing, fragmented replies, two-pass revisions, terminal
packets, authentication and error cases. WASAPI tests use real microphone and
loopback devices and synthetic playback, sent only to that local test service.
These verify the app integration, not the commercial cloud model's accuracy.
Actual Doubao transcription accuracy and production authentication require an
enabled account/API Key and have not been measured in this release preparation.
