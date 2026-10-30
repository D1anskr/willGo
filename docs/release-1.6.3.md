# willGo 1.6.3

Every recognition start in the authenticated workspace now asks whether to record
audio for that session. The owned native dialog offers Yes, No and Cancel and defaults
to No. It appears before model loading or either WASAPI capture device starts.

- Yes: save full-session microphone/system audio in one stereo WAV and save Markdown.
- No: run recognition normally and save only Markdown; no WAV file or audio writer is
  created, including no empty audio placeholder.
- Cancel, Escape or closing the dialog: do not start recognition or create session files.

Both recording choices use the configured archive directory. Final microphone and
speaker transcripts retain their timestamps, source labels, append behavior and stop
tail. Text-only Markdown explicitly says audio was not recorded and has no audio link.
Stopping and restarting asks again; choices never modify saved configuration.

The old all-or-nothing archival checkbox has been replaced by the per-session question.
The settings directory stays editable and is validated for both modes, since text is
saved either way. The old serialized preference is retained for compatibility with
existing configuration files and internal callers that do not specify a session choice.
Explicit session choices override that preference for the current capture only.

The speech service accepts an optional per-session audio choice. The archive supports
text-only operation without constructing audio buffers, resamplers, a file writer or a
periodic audio task. Recognition itself still receives audio in memory; the existing
Qwen worker protocol does not require temporary audio files. Finalization, stop fallback
and session disposal continue to share the same transcript publisher.

Validation covers Yes/No/Cancel, repeated starts/stops, cancellation during confirmation,
unchanged saved options, text-only source/timestamp/tail/footer handling, no audio file,
file-handle cleanup, and later returning to audio recording. Real WASAPI tests exercise
text-only capture and an immediately following audio session using the same service,
including an old configuration with archival disabled. Native dialog tests select all
three real Windows buttons on the test application's own UI thread. Existing vocabulary,
recognition, login, screenshot and UI regression checks remain included.
