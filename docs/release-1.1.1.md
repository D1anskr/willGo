# willGo 1.1.1

Chinese speech recognition now uses the local Sherpa-ONNX 1.13.8 runtime with
the X-ASR Chinese/English streaming Zipformer INT8 model (480 ms variant,
2026-06-05). This replaces Vosk's small Chinese model. No speech API key or
network connection is required for transcription. Answer generation continues
to use the user's configured model endpoint.

Microphone and speaker loopback retain independent recognition streams and
share one model with serialized native access. Audio is converted to normalized
mono samples; the native resampler accepts device sample rates including 48 kHz.
Endpoint detection uses 0.8 seconds of trailing silence after recognized speech.
Only finalized sentences are submitted for answering. Loopback silence and
capture stop flush the remaining sentence. Audio remains in memory.

## Local Comparison

12 fixed Chinese technical sentences were synthesized with Microsoft Huihui
(22,050 Hz, mono, 16-bit PCM; 71.33 seconds total), including Chinese/English
technical names. Character error rate ignores punctuation, spacing, and Latin
case. Both engines use the same input and include silence for endpoint handling.

| Engine | Character Errors | Character Error Rate | Inference Time | Real-Time Factor |
| --- | --- | --- | --- | --- |
| Vosk small-cn-0.22 | 56 / 271 | 20.66% | 16.73 s | 0.235 |
| X-ASR 480 ms INT8 | 2 / 271 | 0.74% | 3.95 s | 0.055 |

The new model correctly recognized Redis, MySQL, Agent, HashMap and
ConcurrentHashMap in this corpus. It still misrecognized one instance of
"线程池" as "县城池". This is a small synthetic corpus, not an estimate of
accuracy for spontaneous speech, accents, noise, or all technical terminology.
The 160 ms model was not evaluated because its download did not complete.

Live WASAPI loopback of a separate 9.11 second synthetic fixture produced its
first partial text at 1.11 seconds and its final sentence about 1.17 seconds
after audio finished. This includes device buffering, model chunking, and
endpoint detection. These times exclude the subsequent model answer request.
The 480 ms chunk size is not a guarantee of 480 ms sentence latency.

## Distribution

The model stays outside the single-file executable in SpeechModels/.
Only the selected model is published. Model archive SHA256:
FA5F63D618E5A01526E275A358BB7772E403F84808A4769FBA52CFFD8160BF74.
The download helper checks this official release digest before extraction.
Sherpa-ONNX, X-ASR, ONNX Runtime and NAudio notices accompany the package.

Validation includes Chinese fixture recognition, missing-model rejection,
48 kHz silence without invented final text, independent stream isolation,
actual microphone/loopback capture, partial output before playback finishes,
UI and global shortcut regressions, complete application startup, and
self-contained single-file speech recognition.
