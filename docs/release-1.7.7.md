# willGo 1.7.7

The floating workspace now uses WindowChrome with a zero caption/glass area,
16px corner radius and a 6px invisible resize hit area. The old resizable window
kept a rectangular non-client border outside its rounded WPF content. At the
machine's 200% scale this added a 12-device-pixel inset on each edge. Applying a
separate content clip rounded the inner surface while leaving that outer border
and dark corner wedges visible. The new native client area fills the complete
window; WindowChrome owns rounding and resizing, and the duplicate WPF clip and
bottom-right resize grip have been removed.

The title bar and native window title now say `willGo`. The voice capture button
says `开始战斗` while idle and `结束战斗` while capturing. Starting still follows
the existing recording-choice dialog, and stopping finalizes speech archives.

Native screen tests use a dedicated solid-color backdrop and disable capture
exclusion only inside the isolated test process to inspect real screen pixels.
They compare client/frame geometry, inspect all four corner areas for dark
wedges, verify light edges and native resize hit targets, and retain the normal
capture-exclusion and passthrough regression checks. This supplements WPF
rendered previews, which do not include a window's non-client frame. Core and
startup checks also cover the new labels, login, capture toggling, saved settings
and existing battle interactions.
