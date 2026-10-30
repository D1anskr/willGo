# willGo 1.6.0

Every explicit voice-capture session can now save one full-length stereo WAV and one
Markdown transcript. Session archival is enabled by default in Documents/willGo/Recordings.
Settings include an independent switch, editable absolute directory and folder picker;
changes apply on the next capture start. Existing model credentials and settings are retained.

## Audio

The microphone occupies the left channel and system playback the right channel of a
single standard 16-bit PCM, 16 kHz stereo WAV. Both are audible on playback. Keeping
channels separate avoids clipping when speakers overlap and permits later separation.
NAudio converts/resamples the device formats. Capture callbacks only buffer audio;
a separate writer uses elapsed time to produce a continuous recording, including silence
when WASAPI loopback emits no callbacks. Approximately 150 ms of buffering absorbs
callback jitter. Recordings use about 230 MB per hour.

The WAV header is refreshed periodically and finalized on stop. A standard WAV has a
4 GB data limit, roughly 18 hours at this format; reaching the limit fails visibly and
stops capture rather than silently losing audio. Invalid/unwritable locations fail
capture startup; write failures and buffer overflow are visible and pause capture.
Hiding/restoring the window does not split an ongoing session. Each new start uses
new timestamped filenames with a random suffix, avoiding overwrites.

## Text

Only final recognized sentences are appended, with millisecond timestamps, an explicit
“我” or “对方” source, and the recognized content on the following paragraph. Final
sentences are recorded regardless of microphone trigger matching or UI transcript limits.
The timestamp is captured when streaming segmentation completes, before final model
refinement; it is not a forced-alignment word timestamp. Session headers contain start
time and a relative link to the audio; footers contain stop time and recording duration.
UTF-8 writes are flushed per entry, so the Markdown can be read during capture.

Stopping capture flushes streaming recognition, cancels in-flight final refinement and
persists remaining utterances using streaming text with an explicit fallback note.
This preserves the last sentence without waiting for the entire final-model queue.
Stop-time sentences remain unsubmitted to the answer model. Cancelling/discarding a
finalization queue outside this graceful capture stop still drops stale results.

Screenshots, manually entered questions and generated answers are not voice transcripts
and are not added to the voice-session Markdown. Recognition still runs locally; final
sentences selected for answering use the user's configured model endpoint.

## Validation

Tests cover mixed device rates, channel isolation with two known tones, elapsed-time
duration, retained silence, live readable Markdown, speaker labels, final-only text,
non-trigger microphone speech, tail flush, preserved timestamps, invalid paths, new
session filenames, settings persistence/migration, and final WAV/file-handle cleanup.
Live WASAPI tests cover speaker playback, immediate stop before endpoint silence,
audible recording, restarting capture and disabling recording. Test recordings remain
private test artifacts and are excluded from release packages.
