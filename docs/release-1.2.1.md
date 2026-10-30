# willGo 1.2.1

Battle mode starts microphone and speaker-loopback transcription immediately
when opened. Repeated show requests do not reinitialize active audio capture.
The capture button shows loading, pause, or resume state. Leaving battle mode
stops capture, including cancellation during model initialization.

Ctrl+Shift+Up/Down selects the previous/next question in list order, clamps at
the first/last question, scrolls the selected title into view, and resets the
answer scroll position. These shortcuts register only during battle mode.
Selection remains available with mouse passthrough enabled.

Opacity moves to Ctrl+Shift+Left/Right: left decreases opacity by 0.05 and right
increases it by 0.05. Held keys repeat. If another desktop tool reserves these
shortcuts, a scoped keyboard hook handles these two chords and consumes their
key release events; the hook is removed on disposal. Other input passes through.

Ctrl+Up/Down continues to scroll the current answer. Ctrl+Shift+S continues
regional screenshots in battle mode. Screenshot and conversation model routing,
phone capture, mouse passthrough, and capture exclusion retain their behavior.

Validation: 68 battle/configuration/UI checks, 66 Windows interaction checks,
and complete application startup/routing checks. Tests cover real arrow-key
input, occupied opacity shortcuts, title selection, scrolling selected titles
into view, automatic capture, duplicate starts, and cancellation during loading.
