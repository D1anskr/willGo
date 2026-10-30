# willGo 1.7.10

Fix Ctrl+Shift+S opening the interactive region picker instead of capturing the
saved region. The global shortcut, workspace screenshot button, legacy command
override and phone endpoint now use one configured capture flow. Preset-region
mode captures the saved rectangle and submits a new screenshot question without
selection or confirmation. Full-desktop mode captures the desktop directly.

An invalid or missing preset region reports where to configure it, without
opening a picker, sending a question or falling back to desktop capture. Capture
does not temporarily disable mouse passthrough. Concurrent capture prevention,
cancellation, screenshot-model routing and question ordering remain intact.

Settings label the screenshot scope as shared between shortcuts and phone. The
first-tab manual and screenshot tooltip describe the corrected behavior. Existing
saved regions and user configuration are preserved during upgrade.

Regression tests exercise preset capture through the command/global callback,
exact rectangle use, automatic image submission, full-desktop capture, invalid
regions, passthrough retention, cancellation and overlapping captures.
