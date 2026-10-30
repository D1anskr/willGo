# willGo 1.0.32

The floating ball now provides a persistent willGo taskbar entry, including while
chat is hidden. Previous versions set ShowInTaskbar=false on both main windows.
The ball is the main window and owns chat, so taskbar minimization also hides
owned chat windows, and closing the taskbar window exits the application.

Ctrl+1 toggles mouse passthrough for all willGo WPF windows and the screenshot
selection overlay. Grey-black ball means interactive. White means clicks, hover,
cursor hit testing, and wheel input pass to the application underneath. New
windows inherit the current mode; a second press restores interaction. The toggle
does not repeat while held. Startup always begins interactive.

Mode2/Mode3 keep their compact presentation but no longer independently override
mouse interaction. The old passive mouse-wheel hook was removed so white mode
does not consume wheel input from underlying applications.

Ctrl+Up/Down scrolls visible latest-answer panels, including long overflow columns.
If the latest answer does not need scrolling, it scrolls visible history instead.
Holding a scroll shortcut repeats. Hidden chat ignores scrolling. Shortcuts work
while another application has focus and in mouse passthrough mode.

Ctrl+Shift+Up/Down still changes opacity. Screen capture exclusion and the 1.0.31
black-window correction are retained. Capture compatibility limits remain as
described in [1.0.30 notes](release-1.0.30.md).

Windows interaction smoke tests are in tests/WillGo.WindowsSmokeTests. Run with
the app closed to release its global shortcuts:

```powershell
dotnet run --project tests/WillGo.WindowsSmokeTests -c Release
```

The tests use a separate local process to verify that mouse hit testing passes
through every main window into another process. No API requests are made, and
test configuration is isolated from the user's settings.
