# willGo 1.7.6

The system-default microphone now uses the Windows multimedia default input,
matching the system-default playback selection. Previously microphone capture
used the communications role, which can resolve to a different device. Explicit
microphone and playback device selections continue to take precedence. Devices
are resolved once when each session starts; switching Windows defaults requires
stopping and restarting capture. Recognition and recording share the same
microphone WASAPI and playback WASAPI loopback callbacks.

Session files now use the local start time in `willGo-yyyyMMddHHmmss.wav` and
`willGo-yyyyMMddHHmmss.md`. Collisions append `-2`, `-3`, etc. Atomic transcript
creation reserves the shared basename so concurrent sessions cannot overwrite
each other's files. Existing WAV files also reserve their basename. A text-only
session produces only the Markdown file.

The full-session audio format remains one 16 kHz PCM16 stereo WAV, microphone in
the left channel and system playback in the right. Both sources are resampled to
the common rate, and pauses retain their duration. Choosing recording saves WAV
and Markdown; choosing text only creates no WAV. No existing archive is renamed.

Focused tests check local timestamp names, matching pairs, concurrent audio and
text-only sessions, unchanged earlier files, channel separation, resampling,
silence duration, final WAV headers and Markdown finalization. Regression checks
also cover the existing battle UI and login workflow. No live microphone capture
is required for these automated archive tests.
