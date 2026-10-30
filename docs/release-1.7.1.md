# willGo 1.7.1

## Direct Cloud Recognition

Doubao streaming ASR 2.0 now runs without a second recognition pass, hotword
configuration, recognition context or local rewriting. The settings fields for
these features have been removed. Old saved enhancement fields are ignored and
cannot re-enable the removed behavior. Existing speech credentials, archive
directory, device choices, pause duration and microphone trigger phrases persist.

The speech API Key remains a masked, editable field in Settings. Saving encrypts
it for the current Windows user with DPAPI. Test Connection sends only synthetic
silence and opens no capture devices. Existing legacy credentials remain supported
in the collapsed compatibility section; an entered API Key takes precedence.

Audio from the microphone and system playback is streamed separately. In-progress
text is previewed. As soon as the cloud marks a sentence complete, its exact text
is used directly, without another ASR/model validation request:

- System playback: create a question and submit to the conversation model.
- Microphone: record all completed sentences; submit only those matching the
  user's configured trigger phrases.
- Answer requests combine the existing conversation system prompt with that
  sentence and keep the returned answer attached to its question title.
- Cumulative repeated results are deduplicated. Incomplete partial text cannot
  produce repeated questions as the same sentence evolves.

The original application login check and speech service authentication still
apply. They are independent of recognition refinement.

Per-session recording choices, WAV/Markdown saving, screenshot/phone questions,
independent model settings and global shortcuts retain their existing behavior.

## Verification

Regression checks cover credentials, encryption, setting edits, old-setting
migration, protocol framing, disabled enhancement options, errors, transcription
archives, question/answer routing, screenshots, login and window behavior.

Real WASAPI tests use synthetic playback and a local WebSocket protocol simulator
to verify both audio sources, completed sentences submitted while capture remains
active, microphone trigger rules, exact text/system prompt propagation, displayed
answers and duplicate suppression. Packaged self-contained tests exercise the
same capture and question flow.

Production connection checks load the saved speech credentials privately and send
synthetic silence to two cloud connections. They run only when a saved credential
is available; an enabled account alone does not provide credentials to the app.
These checks do not measure actual speech recognition accuracy.
