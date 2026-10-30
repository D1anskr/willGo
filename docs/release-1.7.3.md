# willGo 1.7.3

Answers have a dedicated native WPF Markdown renderer using Markdig 0.41.3.
Microsoft YaHei UI body text defaults to 18px with 1.75 line height and 16px
paragraph spacing. Headings, selective bold emphasis, lists, quotes, inline code,
fenced code and comparison tables have distinct presentation. Code indentation
is retained; punctuation in prose no longer causes code-block misclassification.
Model prompts retain their original instructions and add a subordinate formatting
hint for concise paragraphs and selective bold emphasis.

Answer size is independent of menu/control fonts. The answer toolbar and Settings
support 14-28px, saved in Battle.AnswerFontSize. Older configurations get 18px
without inheriting the former 14px maximum. Model credentials and speech settings
are unaffected. External Markdown images/links are rendered as labels without
fetching media or navigating.

Login, battle workspace, Settings, phone dialog and legacy windows all have
ShowInTaskbar=false. The existing tray icon remains available after login.
Ctrl+1 passthrough and capture exclusion retain their existing behavior.

Sidebar questions use a single ellipsized title in a fixed-height compact row,
with time and status on a secondary line. Multiline question text is preserved
in the main view. At small window heights, the question and transcript panes
compact to leave more space for the answer.

Validation includes Markdown structure and edge cases, independent font resizing,
configuration persistence/defaults, compact long titles, minimum window layout,
global scrolling, native taskbar flags, tray registration and existing speech,
login, screenshot and recording regression checks. WPF RenderTargetBitmap images
cover 1120x760, 760x480 at the largest font, and code/table content.
