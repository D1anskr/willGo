# willGo

`willGo` is a native macOS menu bar assistant written with Swift and AppKit.
It keeps the first Windows version's core workflow: login validation, chat with configurable model endpoints, screenshot/image sending, primary/secondary model fallback, compact display mode, local settings persistence, and a LAN page for phone-triggered desktop capture.

This repository no longer contains the Windows installer, WPF application, auto-update flow, update diagnostics, or historical Windows packaging files.

## Requirements

- macOS 13 Ventura or newer
- Xcode Command Line Tools with Swift 5.9 or newer
- Network access for login validation and model API requests
- Screen Recording permission in macOS System Settings if you want desktop screenshots

## Repository Layout

```text
src/willGoMac/          Native macOS app source
scripts/build-mac.sh    Builds a local unsigned willGo.app bundle
dist/                   Rebuildable output, ignored by git
```

## Build

From a Mac:

```bash
bash scripts/build-mac.sh
```

The script builds a release executable with SwiftPM and writes:

```text
dist/mac/willGo.app
```

The generated app bundle is unsigned. For internal testing you can run it locally; for broader distribution, sign and notarize it with your Apple Developer identity.

## Run From Source

```bash
cd src/willGoMac
swift run willGo
```

## Features

- Menu bar icon: `willGo` lives in the top macOS menu bar and can show/hide the chat window.
- Login: validates the willGo user ID against the existing public validation endpoint and can remember the user ID.
- Chat: sends text, attached images, or screenshots to the configured model.
- Model routing: supports primary and secondary endpoints with retry and fallback.
- API compatibility: supports OpenAI Chat Completions, OpenAI Responses, Anthropic Messages, and Auto mode.
- Screenshot sending: captures the full desktop or a dragged region and sends PNG image data to the model.
- Local image attachment: sends selected image files as multimodal input.
- Compact mode: keeps only the latest message visible for a small desktop footprint.
- Phone trigger: starts a LAN HTTP page on port `8765` for phone-triggered desktop capture and preset prompts.
- Settings: saves local config under `~/Library/Application Support/willGo/appsettings.user.json`.

## Removed From The Windows Version

- Windows batch files and PowerShell install/build scripts
- WPF and Windows Forms source
- `.NET`, NuGet, setup exe, zip release, update manifest, and update diagnostic code
- Auto-update mechanism

## Notes

- If screenshot capture returns blank or fails, grant Screen Recording permission to `willGo` or to the terminal used to run `swift run`.
- Region screenshot uses a full-screen translucent overlay: drag to select a region, release to send, or press Esc to cancel.
- The phone trigger page is available from the menu item once the app starts, usually at `http://<your-lan-ip>:8765/`.
- API keys are stored in the local JSON config for this first macOS version. If you need hardened storage later, move the keys to Keychain.
