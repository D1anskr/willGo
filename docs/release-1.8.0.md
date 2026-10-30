# willGo 1.8.0

The main window has a 历史战斗 command that opens a single owned history dashboard.
It reads the speech archive directory from current settings and pairs WAV and
Markdown files by basename, case-insensitively. Text-only and orphaned audio
records remain visible. Current timestamp names and legacy dashed timestamp
names sort by session start time descending; custom names fall back to file
modification time. Scanning is limited to the configured directory.

Each row shows its filename, start time and document/audio presence or write
status. Local WAV playback uses NAudio and supports pause, stop, position and
volume. Documents open through the Windows association with a Notepad fallback.
Refresh rereads disk, and folder access opens the configured directory. Changing
the configured directory updates an open dashboard. Missing directories, empty
lists, deleted files and playback failures have visible states.

Active recording files are not played while being written. Historical audio
playback is suspended when live speech capture starts, and playing is disabled
while capture runs to avoid transcription feedback. Hiding or closing the
dashboard stops audio, and closing releases output devices and file handles.

The window follows the existing white rounded design, native tool-window flags,
capture exclusion, mouse passthrough and opacity settings. Login is required
before the history entry can open. Existing archives are read in place and are
not rewritten or included in distribution packages.
