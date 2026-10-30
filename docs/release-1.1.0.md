# willGo 1.1.0 (Windows x64)

## System tray

- Add a real Windows notification-area icon, in addition to the existing taskbar button.
- Double-click opens the current workspace. Right-click offers workspace, battle mode, settings, mouse passthrough, and exit.
- Windows may place the icon under the notification-area overflow arrow. Its location follows Windows notification settings.

## Battle mode

- Enter from the chat toolbar, floating-ball menu, or tray menu. Audio remains stopped until Start Capture is pressed.
- Vosk 0.3.38 with the bundled vosk-model-small-cn-0.22 provides offline streaming Chinese recognition. NAudio 2.2.1 captures the selected microphone and WASAPI speaker loopback separately.
- Microphone speech is labeled as the user; all playback on the selected output device is labeled as the other person. This is channel attribution, not speaker diarization. Use headphones to reduce playback leaking into the microphone.
- Partial recognition stays in the live transcript. Only finalized sentences are submitted. Vosk finalizes on speech pauses; recognition does not run a language model on every partial result.
- Other-person sentences become selectable question entries and are automatically submitted. Microphone sentences submit only when a configured phrase matches. Default phrases include 我想一想, 让我想想, and 让我想一想.
- Settings include microphone, speaker output, a separate conversation prompt, and multiline trigger phrases. Audio device changes apply on the next capture start.
- Battle requests use the configured primary/secondary routing, protocol, credentials, timeout, and fallback. The battle prompt replaces the screenshot-specific system prompt for these requests only.
- Two answers can run concurrently. At most twenty requests are active or queued. Overflow remains visible for manual retry. Retry, cancel, clear, manual questions, and per-question failure status are available.
- The transcript keeps the most recent 300 lines in memory. Question entries and answers last for this app session. No audio files or transcript files are saved by the feature.
- Finalized sentences that trigger answers go to the configured model endpoint. Speech recognition itself does not upload audio.
- Pause stops capture. Returning to normal mode stops capture and cancels battle requests. Collapsing the battle window leaves capture running; the tray tooltip indicates active capture.

## Existing controls

- Ctrl+1 toggles mouse passthrough, including the battle window and floating ball. Grey-black is interactive; white is passthrough.
- Ctrl+Up / Ctrl+Down scroll the active mode's answer.
- Ctrl+Shift+Up / Ctrl+Shift+Down adjust opacity.
- Existing capture exclusion, phone input, screenshot workflows, and user settings are retained.

## Model Limitations

The small CPU model favors local deployment and streaming latency. Chinese homophones, technical terms, English names, background audio, and overlapping speech can be misrecognized. Sentence timing depends on speech pauses; model-answer latency also depends on the configured remote endpoint. This release does not provide acoustic echo cancellation or identification of individual speakers within the same audio channel.

## Build and Validation

Run `scripts/windows/download-speech-model.ps1` to acquire the checksummed Chinese model, then `scripts/windows/build-setup-exe.ps1` with .NET 8 on PATH. Publishing fails when the model is absent. Packages include model files, speech runtime DLLs, and license notices.

Verification covers configuration migration and cloning, prompt isolation and fallback, trigger normalization, non-trigger and partial suppression, answer queues, cancellation and retry, PCM audio formats, responsive window sizes, screen capture exclusion, mouse passthrough, scrolling, and actual Explorer tray registration. Additional integration tests use generated Chinese speech for local recognition and real speaker loopback, and a harmless test question through the configured model endpoint.
