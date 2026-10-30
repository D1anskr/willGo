# willGo 1.10.0

Screenshots from the hotkey, toolbar and phone enter a two-stage workflow using
the existing screenshot model route, including its primary/fallback behavior.
The first request extracts relevant problem content as structured JSON containing
a short title and Markdown. The second request solves the extracted problem,
retaining the original image for diagrams, formulas and visual cross-checking.
This adds a model request per new screenshot; dialogue routing is unchanged.

The workspace displays extracted content immediately, with problem and answer
panes that scroll independently. Their divider can be dragged or resized with
the keyboard; its ratio persists independently from the question list width.
Switching to a dialogue question restores a single answer pane. Ctrl+Up/Down
continues to scroll the answer. The original image remains in a collapsed
expander under the problem, and extracted titles replace generic screenshot
labels in the question list.

Programming answers request explanation, time and space complexity, and complete
code in the problem's language/template or Java by default. Nonprogramming
questions do not invent complexity analysis or code. AvalonEdit 6.3.1.120 adds
language-aware highlighting, line numbers, preserved whitespace, text selection,
horizontal/vertical scrolling and copy buttons for Markdown code blocks, including
code templates extracted from screenshots. Unsupported languages show plain code.
Model-supplied code is only displayed and copied, never executed.

Answer retries reuse valid extracted content. Re-extraction explicitly clears the
extracted question and runs the full workflow. Invalid/empty JSON and screenshots
without recognizable problems stop before solving and show a retryable message.
Cancellation between stages prevents the solve request, and answer failure keeps
the extracted problem available. The original image always stays with its entry.

Existing configuration and credentials are preserved. New installations and
existing configs use a default 38% problem-pane ratio. Both model requests send
the screenshot to the user's configured screenshot model provider; model usage
and charges are controlled by that provider. Recognition accuracy and the
correctness of generated solutions still depend on the configured models.
