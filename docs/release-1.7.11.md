# willGo 1.7.11

All protected WPF windows explicitly set WS_EX_TOOLWINDOW and clear
WS_EX_APPWINDOW at native handle creation and when shown. Native extended-style
changes preserve this identity synchronously, including opacity changes,
passthrough toggles and WPF taskbar updates. Style application does not move,
resize, activate or change window ownership.

Existing settings-to-workspace ownership and WPF hidden taskbar ownership remain
unchanged. Login, workspace, settings and phone dialogs share the same behavior.
The tray icon still restores the workspace. Rounded frames, capture exclusion,
global shortcuts and direct configured-region screenshots remain supported.

This is a standard tool-window compatibility change for sharing pickers that
filter such windows. It does not prevent process/window enumeration and cannot
guarantee removal from every sharing client's candidate list. Tencent Meeting
must be verified manually using the user's installed client. No third-party
process is modified. The Chinese guide states the distinction between capture
exclusion and candidate-list visibility.

Native regression coverage checks tool-window flags at display, passthrough,
opacity changes, attempted extended-style replacement and hide/show, alongside
the existing corner-pixel, input and capture tests. Upgrade preserves all saved
settings and screenshot regions.
