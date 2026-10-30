# willGo 1.6.1

Fix a two-stage recognition regression where streaming ASR correctly recognized an
English technical term such as kafka, but Qwen whole-utterance recognition replaced
it with a Chinese transliteration before creating a question.

Finalization now compares both decoder results after exact acronym normalization.
If a configured English hotword occurs in the streaming sentence but is missing
or occurs fewer times in the refined sentence, retain the streaming sentence.
Matching is case insensitive with identifier boundaries and literal punctuation.
The default hotword list already includes Kafka, Redis, MySQL, Spring and others;
custom technical terms can be added to the existing speech hotword setting.

Refinement still improves sentences when all previously recognized terms survive.
Chinese mentions without a matching streaming English term are left to the final
recognizer; there is no global Chinese-to-English substitution and no guessing of
missing acronym letters. An empty refined result also retains the streaming text.
Only the last streaming result for the utterance is compared, so speculative old
partials do not lock an incorrect early word into the question.

The selected final text is published once and shared by question generation,
the displayed final transcript, and the session Markdown archive. Existing audio
recording and model configuration remain available.
