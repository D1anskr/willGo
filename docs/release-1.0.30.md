# willGo 1.0.30

## Screen capture exclusion

- Enabled by default, including when upgrading an existing user configuration.
- Settings > Appearance > Hide willGo from screen capture can disable it.
- Applies to the floating ball, chat, overflow message columns, settings, phone QR window,
  screenshot selection overlay, context menus, and tooltips.
- Transparent WPF windows use uniform opacity and native rounded window regions,
  since per-pixel layered WPF windows reject SetWindowDisplayAffinity with Win32 error 8.
  Chat opacity now affects the entire chat window, including text, instead of only its background.
- Uses Windows WDA_EXCLUDEFROMCAPTURE. Requires Windows 10 version 2004 or later.
- Capture applications must honor Windows capture exclusion. Hardware capture,
  cameras, unsupported capture methods, and external system dialogs are not covered.

## Global opacity controls

- Ctrl+Shift+Up increases ball and chat window opacity by 5 percentage points.
- Ctrl+Shift+Down decreases both by 5 percentage points.
- Works while another app has focus, with chat hidden, and in passive compact mode.
- Uses the existing limits: ball 5%-100%, chat window 30%-100%.
- Changes update immediately and save after 400 ms without another adjustment.
- The settings editor stays in sync. Pending changes also save on normal exit.
- Conflicting shortcut registrations show a warning; closing willGo releases shortcuts.

## Build

The normal chat header and footer toolbars wrap at narrow widths so the title and
message input remain readable at the default window size.

Run scripts/windows/build-setup-exe.ps1 with a .NET 8 SDK available on PATH.
The script generates the self-contained win-x64 package, ZIP, and Setup.exe in dist/.
