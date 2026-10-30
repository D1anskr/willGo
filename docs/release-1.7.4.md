# willGo 1.7.4

The active desktop windows now use white surfaces and dark readable text. A
shared light WPF resource dictionary provides consistent buttons, text fields,
masked credential fields, tooltips and control borders across the battle
workspace, login, Settings and phone QR dialog. Buttons include hover, pressed,
keyboard focus and disabled states. Sidebar selection and source/status labels
use restrained accents against neutral white and pale grey surfaces.

Markdown answers retain the 18px default and adjustable 14-28px range, typography,
spacing, emphasis, headings and lists introduced in 1.7.3. Text and bold emphasis
are dark; code blocks and table headings have pale grey backgrounds. QR image
contrast remains black on white. The mouse passthrough indicator retains its
grey-black interactive and white passthrough states with a visible outline.

The main window background has full alpha white. Existing native window opacity
controls remain unchanged: 100% corresponds to alpha 255 and solid white; lower
values reveal the desktop behind the same white surface. The user's saved
opacity is preserved. Ctrl+Shift+Left/Right continues to adjust it globally.

Focused checks verify light backgrounds, readable text contrast, rendered white
pixels, code/table surfaces and native alpha at 100%, 55%, 30%, then 100% again,
while retaining capture exclusion. Regression coverage also exercises compact
titles, font controls, scrolling, login, tray-only behavior, recording and cloud
speech routing. Rendered previews include normal and minimum-size windows,
maximum answer font size, code/tables, Settings, login and the phone QR dialog.
