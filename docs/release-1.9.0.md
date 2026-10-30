# willGo 1.9.0

## Echo filtering

Speaker loopback and microphone finals now pass through the same filter before
transcript persistence and question dispatch. Speaker results are immediate;
microphone finals wait 1.5 seconds to accommodate either channel arriving first.
Matching interim speaker text can extend this wait to at most five seconds when
the speaker final is delayed; an unconfirmed interim result never discards text.
Near-simultaneous matches keep the speaker's text and discard the microphone
echo, including small recognition differences and split sentence boundaries.
Repeated final events with stable timestamps are ignored. Genuine microphone
speech flushes on stop, and filtering state resets on each capture session.

The enabled-by-default switch is in Speech Settings under dialogue rules.
Changing it takes effect on the next capture session. This is text-level echo
suppression, not acoustic echo cancellation. It leaves the raw stereo recording
unchanged. Ambiguous replies shorter than four normalized characters are kept.
Large recognition differences, a cloud result arriving after the microphone's
hold expires, or echoes outside the three-second audio timestamp window may
still be retained. Headphones or hardware/driver echo cancellation remain useful
when physical speaker leakage is strong.

## Interview context

Speech Settings now contains an editable context prompt, with a default for Java
backend, AI application and Agent interviews. It asks the model to consider ASR
errors, technical terms, progressive questions and topic changes, while avoiding
unsupported corrections or blind trust in earlier answers.

Dialogue requests carry up to two preceding question-answer pairs as structured
user and assistant turns, followed by the current question. Each previous answer
is limited to 12,000 characters. Unavailable answers are explicitly marked.
Dialogue requests queue sequentially to obtain previous answers without an extra
model call; screenshot requests retain their separate model route and behavior.
Retrying an old question includes only its earlier rounds, and clearing the
question list resets context. The configurable prompt applies to both primary
and fallback dialogue endpoints. Existing installations receive defaults without
overwriting their stored configuration.

The model interprets possible recognition errors when answering. It never
rewrites the original transcript or question title. More context can improve
ambiguous technical questions but cannot guarantee recovery of a misheard term.
