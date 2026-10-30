# willGo 1.7.2

Production ASR testing found that Doubao omits start_time when the first
utterance begins at zero. The old parser rejected these valid interim packets
and ended recognition before any text appeared. Missing start_time now defaults
to zero; explicitly invalid supplied values are still rejected. Later timestamps,
deduplication and transcript times retain their existing behavior.

Regression responses now reproduce the omitted zero timestamp. Existing tests
exercise partials, completed first utterances, duplicates, repeated later speech,
source roles and capture timestamps against that actual service shape.

Real cloud checks using privately loaded saved credentials verified two concurrent
connections, 18 synthetic Chinese/English/acronym fixtures, completed utterances
before closing the connection, and actual conversation answers for a speaker ZGC
question and a microphone Kafka question containing the configured trigger.

All 18 fixtures produced text without protocol/transport failures after the fix.
First nonempty transcription preview from the start of upload: median 0.625 s,
range 0.546-2.672 s. These are synthetic speech samples, not a human speech accuracy
benchmark. Errors remain, including thread pool homophones, HashMap -> cash map,
and a missing standalone Z. No hotwords, contextual vocabulary, local rewriting
or second recognition pass were enabled to hide these errors.

Core regression checks: 170 passed. The original login requirement, microphone
trigger rule, recording choices and screenshot features remain. User credentials
are preserved locally and excluded from distribution artifacts.
