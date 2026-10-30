# willGo

`willGo` is a Windows desktop assistant built with `.NET 8`, `WPF`, and `Windows Forms` interop.
It supports screenshot-based sending, local image attachment, configurable model endpoints, and a phone-accessible LAN trigger page for remote desktop capture.

## Requirements

- Windows 10/11
- `.NET 8 SDK` for local build from source
- Network access for first-time NuGet restore
- `IExpress.exe` if you want to generate the single-file `Setup.exe`

## Version 1.10.5

Login uses explicit initial dimensions and minimum bounds instead of automatic
height sizing. Starting another instance signals the existing process to restore
its login window or workspace. Native window size and monitor bounds are checked
alongside rendered layout. See [release notes](docs/release-1.10.5.md).

## Version 1.10.4

The login window now has a rounded white frame, a bundled window-and-steps drawing
and supportive Chinese copy. The account field, remember option and default login
action remain accessible, with scrolling on shorter displays. Authentication
behavior is unchanged. See [release notes](docs/release-1.10.4.md).

## Version 1.10.3

The login dialog supports the owner's requested fixed local access code. An exact,
case-sensitive match after trimming bypasses account-length checks, IP lookup and
remote validation. Other accounts retain online validation. Remembered input still
requires an explicit login action. This local access mechanism is included in all
copies of this build. See [release notes](docs/release-1.10.3.md).

## Version 1.10.2

Code blocks now wrap long lines to the available pane width and have no internal
horizontal or vertical scrollbars. Wrapped lines expand the block height when
the pane narrows or the font grows. Copying preserves the original code text.
See [release notes](docs/release-1.10.2.md).

## Version 1.10.1

Code blocks expand to their complete document height, without an internal vertical
scrollbar or height cap. Mouse wheel input over code scrolls the enclosing answer
pane. Horizontal scrolling, syntax highlighting, line numbers and copying remain
available. See [release notes](docs/release-1.10.1.md).

## Version 1.10.0

Screenshot questions now use two stages on the configured screenshot model route:
extract the relevant problem into Markdown, then solve it with the extracted
conditions and original image. The extracted question appears before the answer
and supplies the sidebar title. Problem and answer panes scroll independently,
with a draggable divider whose ratio is saved separately from the history list.

Programming answers request reasoning, time/space complexity and complete fenced
code. AvalonEdit renders code with syntax highlighting, line numbers, selection,
scrolling and a copy control. Answer retries reuse extraction; an explicit
re-extract control starts both stages again. See [release notes](docs/release-1.10.0.md).

## Version 1.9.0

Speech finals now pass through a shared cross-device echo filter before either
Markdown persistence or question dispatch. Microphone finals wait 1.5 seconds
for matching speaker text; speaker questions remain immediate. Filtering uses
audio timestamps and conservative normalized text matching, not audio-level
acoustic echo cancellation. A speech-settings switch disables this behavior.

Dialogue requests include the two preceding question-answer pairs and an
editable interview context prompt. Closely spaced dialogue questions queue so
previous answers are available. Original transcripts and question titles stay
unchanged. See [release notes](docs/release-1.9.0.md).

## Version 1.8.0

The workspace now has a Chinese history dashboard for the configured speech
archive directory. Matching WAV/Markdown files form one session, newest first,
including text-only and orphaned audio sessions. File availability and write
status appear next to the filename and start time. The dashboard includes local
WAV playback, pause, stop, seeking and volume, document opening, refresh and
folder access. Playback is suspended while live speech capture runs to avoid
feeding historical audio back into recognition. Its owned rounded window keeps
capture exclusion, native tool-window identity and mouse passthrough. See
[release notes](docs/release-1.8.0.md).

Application windows now explicitly use WS_EX_TOOLWINDOW and clear
WS_EX_APPWINDOW, retaining those flags through native style updates and window
restoration. Existing WPF ownership and modal relationships remain intact.
Capture exclusion, opacity, mouse passthrough and tray restoration remain
independent. Some sharing pickers omit tool windows; others still enumerate
them, so Tencent Meeting picker visibility must be tested with its actual
client version.

Ctrl+Shift+S, the workspace screenshot button and phone capture now share the
saved screenshot scope. Preset-region mode captures the saved rectangle and
submits a screenshot question immediately, without a selection overlay or
confirmation. Full-desktop mode also captures directly. Invalid preset regions
report a settings hint rather than opening a picker or capturing other content.
Mouse passthrough remains active throughout capture. The Chinese user guide and
capture scope labels describe the shared behavior. See
[previous release notes](docs/release-1.7.10.md).

Settings open on a new first Chinese user-guide tab. The scrollable manual covers
all global shortcuts and local keyboard actions, mouse passthrough indicator
colors, screenshot and phone workflows, recording choices, and every visible
setting with practical starting values. It distinguishes passthrough from
opacity, screenshot from conversation routing, and microphone triggers from
speaker questions. The remaining four configuration pages keep their existing
behavior.

Settings are organized into four Chinese sections: model configuration, speech,
quick questions and appearance. Screenshot and conversation endpoints stay
independent inside the model section; shared capture region controls belong to
the screenshot page. Speech credentials, devices, archive directory, pause,
conversation prompt and microphone triggers now share a dedicated speech page.
Phone preset questions have their own page with add, edit, enable, reorder and
delete controls. All interface labels and enum choices are localized without
rewriting existing user configuration or model identifiers. Settings use the
same native rounded frame as the workspace, rounded fields and tabs, separately
scrollable pages and a fixed save/cancel footer.

The workspace now uses WPF WindowChrome for a full-client native rounded frame.
This removes the residual rectangular resize border and mismatched content clip
that produced black corner wedges, while retaining edge resizing, opacity,
capture exclusion and passthrough. The title reads `willGo`; the capture button
reads `开始战斗` when idle and `结束战斗` while active.

System-default speech capture now uses the Windows multimedia default input and
output at the start of each session. Explicit device selections still take
precedence. Changing Windows devices during capture takes effect after stopping
and restarting recognition. ASR and archival use the same captured audio.
Recording saves one stereo WAV (microphone left, system playback right) plus a
matching Markdown transcript. Filenames use local `willGo-yyyyMMddHHmmss` with a
numeric suffix for collisions, including text-only and concurrent sessions.

The floating workspace has a native rounded outline, softer control corners and
a rounded message composer. Drag the divider between question history and the
answer to change their widths; the preferred position is saved and restored,
with minimum sizes to keep both panes usable. The focused divider also supports
Left/Right arrow keys. Enter sends a manual question; Shift+Enter adds a newline.
IME candidate confirmation does not submit unfinished text, and empty messages
disable sending. Native opacity and capture exclusion remain compatible with
the rounded window.

The battle workspace, login, Settings and phone QR dialog now use a coordinated
white-and-dark-text appearance. Answers have dark body text and emphasis, with
light code and table surfaces. At 100% window opacity the background is solid
white; lower opacity retains the same white surface while revealing the desktop.
Existing saved opacity, credentials, font size and all shortcuts are preserved.

Answers now use Microsoft YaHei UI with an independent 18px default size,
1.75 line height, paragraph spacing, and native Markdown headings, emphasis,
lists, code and tables. Adjust answer size from 14 to 28 with the answer toolbar
or Settings. Existing configurations receive the new readable default.
Question titles are one line with ellipsis and fixed compact rows; full questions
remain available above their answers. All application windows stay out of the
taskbar; the notification-area icon restores the workspace after login.

Screen capture exclusion is enabled by default on Windows 10 version 2004 and later.
It can be changed in Settings > Appearance > Hide willGo from screen capture.
Ctrl+Shift+Left/Right adjusts window opacity globally in 5-point steps.
Ctrl+Shift+Up/Down selects adjacent questions, newest first.
Ctrl+Shift+S captures a screenshot as a question, including phone capture support.
Ctrl+1 toggles mouse passthrough for application windows: the grey-black header
indicator is interactive; white passes mouse input to the application underneath.
Ctrl+Up/Down scrolls the visible answer or conversation history globally.
Fixes black window surfaces and lost initial opacity when capture exclusion is enabled.
Online account validation is required before entering the battle workspace.
Speech recognition uses Doubao streaming ASR 2.0 over two cloud WebSockets.
Configure speech credentials in Settings; local ASR models are no longer bundled.
Completed cloud sentences go directly to the conversation model without a second
recognition pass, hotwords, context injection or local rewriting. Microphone
sentences retain the configured trigger rule; speaker sentences submit directly.
The first cloud utterance may omit its zero-valued start time; this is now
accepted for both interim and completed results. Real cloud fixtures and actual
conversation model routing have been verified with saved credentials.
See [release notes](docs/release-1.7.8.md) for validation details and
[cloud setup](docs/release-1.7.0.md) for account activation and billing.

## Repository Layout

```text
src/FloatingDeskAssistant/   Main application source
scripts/windows/            Build, package, install, repair, and uninstall scripts
docs/                       Supporting release and planning notes
QUICKSTART-WINDOWS.md       Windows usage and build shortcuts
```

## Clone

SSH is the most reliable option if you plan to push changes:

```powershell
git clone git@github.com:D1anskr/willGo.git
cd willGo
```

HTTPS clone also works for read-only or environments without SSH:

```powershell
git clone https://github.com/D1anskr/willGo.git
cd willGo
```

## Build From Source

Build the app directly:

```powershell
dotnet build .\src\FloatingDeskAssistant\FloatingDeskAssistant.csproj -c Release
```

Run the packaging flow:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
```

Build the single-file installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
```

Convenience batch files are available in the repository root:

- `Build-willGo-Release.bat`
- `Build-willGo-Package.bat`
- `Build-willGo-Setup.bat`
- `Install-FloatingDeskAssistant.bat`
- `Repair-FloatingDeskAssistant.bat`
- `Uninstall-FloatingDeskAssistant.bat`

See [`QUICKSTART-WINDOWS.md`](./QUICKSTART-WINDOWS.md) for the Windows-oriented workflow and output paths.

## Release Output

The speech pipeline uses Doubao streaming ASR 2.0 directly, with cloud second-pass
recognition disabled and no hotword or context configuration. No model download,
Python, Vulkan or GPU is needed.
The app requires internet access and the user's own Volcengine speech credentials.
Historical local recognizer sources and download scripts are retained for reference
but excluded from the current app build. Personal settings and recordings are never
copied into distribution packages.

The release scripts generate versioned artifacts under `dist/`:

- `dist\willGo-<version>-win-x64\`
- `dist\willGo-<version>-win-x64.zip`
- `dist\willGo-Setup-<version>-win-x64.exe`

These artifacts are intentionally not tracked in git.

## Daily Git Workflow

Sync before starting work:

```powershell
git pull --rebase origin main
```

After editing files:

```powershell
git status
git add .
git commit -m "Describe your change"
git push origin main
```

Useful notes:

- `dist/`, `bin/`, and `obj/` are ignored because they are rebuildable outputs.
- `build-release.ps1` updates `src/FloatingDeskAssistant/FloatingDeskAssistant.csproj` with a new version when you create a release. If you want that version bump to remain in source control, commit it.
- If another machine cannot push over HTTPS, switch the remote to SSH:

```powershell
git remote set-url origin git@github.com:D1anskr/willGo.git
```

## Verification

This repository was verified from a fresh clone with:

```powershell
dotnet build .\src\FloatingDeskAssistant\FloatingDeskAssistant.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
```
