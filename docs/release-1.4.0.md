# willGo 1.4.0

Speech uses two local recognition stages by default. X-ASR continues to provide
live partial text. Each finalized utterance, including its original audio and
sample rate, is queued once for Fun-ASR-Nano whole-utterance recognition. Only
the second-stage result becomes a question or enters microphone trigger matching.
Screenshots, phone questions and manual questions keep their existing routes.

## Recognition

- Portable FunASR llama.cpp AVX2 runtime v0.2.6; FP16 encoder and Q8_0 decoder.
- Streaming hotwords use Sherpa modified beam search with the original X-ASR
  SentencePiece vocabulary exported to `Assets/x-asr-bpe.vocab`.
- Configurable pause defaults to 1.1 seconds, previously 0.8; normalized range
  is 0.6 to 2.5 seconds. Changes take effect on the next capture start.
- Default terms include ZGC, JVM, GC, JDK, RAG, Redis and common framework names.
- The portable Nano CLI does not accept hotwords. Hotwords bias the streaming
  stage; final normalization only joins exactly spelled configured acronyms.
  It never completes Z or ZG to ZGC.
- Refinement can be disabled without disabling streaming recognition.
- Both stages run locally on CPU. No Python, CUDA installation, speech API key
  or speech cloud service is required.

## Lifecycle

Audio capture and streaming decoding remain independent of final recognition.
One bounded queue serializes second-stage requests for both audio sources,
retaining source, capture order and utterance identity. Delayed results do not
erase newer live previews. Overload is visible and pauses capture instead of
silently dropping utterances. Failed refinement uses the original streaming
result with a visible warning. Each native request has a 15-second timeout;
stopping capture cancels and reaps in-flight inference and drops pending work.

The portable runtime requires a WAV path. Each request uses a uniquely named
temporary WAV, removed after completion, failure or cancellation. No recording
history is maintained; an abrupt OS or parent-process termination can leave a
temporary file. Installer and uninstaller stop native workers only from the
specific installed model directory before replacing/removing files.

## Validation

- 136 checks: application behavior, auth, persistence, bounded final queue,
  cancellation, settings UI, seven acronym fixtures, and live ZGC loopback.
- 23 full published-application startup checks, including strict login gating.
- 15 checks on twelve existing Chinese technical speech fixtures.
- Four additional live-loopback checks using the published self-contained app.
- Real microphone and speaker capture also passed the existing speech suite.
- Final native processing in the seven acronym cases took about 0.8 to 1.1 s;
  the twelve general cases averaged 1.06 s. These exclude endpoint waiting.
- Published live ZGC finalization took about 2.24 s after playback ended.

These are synthetic fixtures, not an accuracy claim for the user's voice.
Nano recognized ZGC, JVM, GC and RAG in the targeted fixtures. ZG was misread as
cg, a single Z gained a spurious ending, and G1 spoken as 'G one' was not
normalized to G1. One general fixture still misrecognized Chinese thread safety.
The raw comparison results retain these failures.

Model, runtime, model-vocabulary and third-party license files accompany the
package. The download helper pins revisions and checks SHA256 before use.
