# willGo 1.3.0

The battle workspace is now the sole application window. Startup no longer
creates the floating ball, normal chat, or the legacy window coordinator.
The workspace owns settings, has its own taskbar entry, and hides to the tray
when closed. Tray restore displays the same window without changing audio state.
The tray retains Show, Settings, mouse passthrough, and Exit commands.

Speech recognition is off at startup. The header button explicitly turns
microphone and speaker-loopback transcription on or off. Reopening a hidden
window neither starts nor restarts recognition. Hiding preserves its current
state; process exit stops it. Screenshots and typed questions work with audio off.

A header button replaces the ball's mouse-interaction indicator: grey-black
means interactive, white means mouse passthrough. Ctrl+1 updates this indicator
and the existing native input flags across all windows. The header button can
also enter passthrough. Keyboard shortcuts retain the 1.2.1 mappings.

Phone screenshots, typed questions, and prompt presets now all enter the sole
workspace. The phone joystick moves the workspace; its existing /ball/move
route remains compatible. Obsolete compact-mode controls are removed from the
phone page. Ball opacity and normal-chat layout options are removed from Settings;
their stored configuration fields remain compatible with older versions.

The selected question title also remains visible after window resizing.

Validation covers the standalone main window and tray restoration, manual speech
start/stop, initial audio-off state, phone text and preset routing, screenshot
regressions, native mouse passthrough, title navigation, and opacity shortcuts.
No ambient audio is recorded in the application startup tests; speech and model
services are replaced with test doubles before exercising recording controls.
