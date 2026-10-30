# willGo 1.7.9

Settings now open on a first tab named 使用说明. The manual uses the existing
white rounded settings frame, readable wrapping text, section headings and an
independently scrolling page with the save/cancel footer always accessible.

The guide covers all active global shortcuts: Ctrl+1 mouse passthrough and its
white/dark indicator, Ctrl+Shift+S screenshots, Ctrl+Shift+Left/Right opacity,
Ctrl+Shift+Up/Down question selection, and Ctrl+Up/Down answer scrolling. Local
actions include Enter sending, Shift+Enter newlines, screenshot Enter/Escape,
Settings Escape, and keyboard adjustment of the pane divider.

The manual also explains login, starting/stopping speech, the per-session
recording choice, tray restoration, phone usage, model routing and credentials,
protocols, timeouts and attempt counts, screenshot prompts/phone capture area,
speech credentials/devices/archive/pause/prompt/triggers, phone presets and
appearance settings. Recommendations describe current behavior without changing
configuration defaults, recognition, shortcuts or model requests.

Regression coverage checks the guide opens first, page order, shortcut scope,
scrolling to the final instructions at minimum window size, and the existing
configuration editing and native window interactions. Upgrades preserve saved
user configuration.
