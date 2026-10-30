# willGo 1.0.31

Fixes the black floating ball and chat window reported after installing 1.0.30.

WPF rebuilds its render target during initial display and can remove the native
layered style during resizing. Setting uniform alpha before the first rendered
frame lost the configured opacity. Changing this composition mode also left
DWM's capture exclusion surface stale, despite the reported affinity remaining
WDA_EXCLUDEFROMCAPTURE (0x11).

Native alpha is now applied after ContentRendered. Window style and size changes
schedule an opacity refresh after layout. Capture affinity is refreshed after the
native alpha update so capture shows the underlying desktop instead of black.

The screen capture setting and Ctrl+Shift+Up/Down shortcuts are retained.
User settings, API credentials, and phone input support are preserved.

Verification on Windows 10 22H2 (19045):

- 44 existing configuration, hotkey, window, popup, alpha, and capture checks pass.
- 13 startup checks pass, including configuration setup checks and actual compact
  chat / ball rendering with capture exclusion enabled and chat opacity at 30%.
- Checks cover initial alpha, resize, hide/reopen, live opacity changes, capture
  pixels showing the underlying window, and exposed rendered surfaces containing
  text and the floating ball's white fill.

Capture applications must honor Windows capture exclusion. Requires Windows 10
2004 or newer. Individual meeting applications and capture modes have not all
been tested. See [1.0.30 notes](release-1.0.30.md) for the original feature details.
