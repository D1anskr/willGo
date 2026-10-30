# willGo 1.7.5

The battle workspace now uses a 16px native rounded window region with a matching
content clip, retaining uniform opacity, capture exclusion and mouse passthrough.
The white reading surface, dark Markdown answers and compact question titles
remain. Buttons and fields have softer corners and the composer uses a rounded
outline with a circular send button and visible interaction states.

A 12px draggable divider separates question history from the answer. It offers
a visible grip, hover and focus feedback, and Left/Right keyboard resizing when
focused. The preferred question width is saved after completed drags and key
adjustments. A cancelled drag restores the previous width. Sidebar limits of
150-360px and a 380px minimum answer column keep the minimum-size window usable;
expanding the window restores the user's preferred width.

Enter sends a nonempty manual question and clears the composer. Shift+Enter
retains the editor's multiline behavior. IME candidate confirmation and Enter
during active composition do not submit unfinished text. Holding Enter cannot
repeat the same message. The send button reflects whether text is available.

Focused regression coverage exercises native rounded regions across resize,
opacity and passthrough, divider bounds, cancellation, keyboard resizing and
configuration persistence, Enter submission, IME guards, empty messages and
multiline editing. Full startup and native interaction checks cover login,
tray-only operation, capture exclusion and existing global shortcuts. Rendered
previews cover normal and minimum window sizes, large answer fonts and rich
Markdown. Windows packages retain user settings and contain no user credentials,
recordings or local speech models.
