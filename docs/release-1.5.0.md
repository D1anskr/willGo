# willGo 1.5.0

Final speech recognition now uses Qwen3-ASR-1.7B Q8_0 instead of Fun-ASR-Nano.
X-ASR continues to provide live partial text; the complete original utterance
is recognized once by Qwen before question creation and microphone trigger matching.

## Runtime

- Native speech.cpp v0.8.10 on Windows x64, with Vulkan GPU acceleration.
- One worker loads and warms at capture start, then serves both audio sources
  through the existing bounded serial queue. No Python or CUDA installation is needed.
- The worker selects the first available GPU, or CPU when no GPU is available.
- Automatic language detection preserves Chinese, English and mixed speech.
- Audio travels as PCM16 over redirected stdin, without temporary WAV recordings.
- A cancelled, failed or timed-out request kills and reaps the worker; a subsequent
  request can reload it. Stopping capture releases the worker and GPU allocations.
- Startup timeout is 45 seconds; warmed inference timeout is 15 seconds per request.
- Failed final recognition retains the streaming result with a visible warning.

## Vocabulary

Streaming hotwords remain separate from final model context. Final domain vocabulary
can now be edited independently and applies on the next capture start. Defaults use
Chinese technical terms. Adding every acronym to the final prompt biased the evaluated
model into completing Z or ZG to ZGC, so the shipped defaults avoid this. Normalization
only joins exactly spelled configured acronyms and never supplies missing letters.

## Validation

Evaluation ran locally on an i5-13600KF, 32 GB RAM and RTX 4070 SUPER 12 GB, using
synthetic speech. These are targeted regression fixtures, not a ranking or an estimate
of accuracy on the user's voice.

- Seven mixed-language acronym fixtures: ZGC, JVM, GC, RAG, Z and G1 were retained.
  ZG was retained without inventing C, though its Chinese prefix still used the
  homophone “什么事” rather than “什么是”.
- Twelve Chinese technical fixtures and four English fixtures were evaluated.
  Thread-pool/thread-safety wording improved; CamelCase names can contain spaces.
- Warmed native processing on the mixed-language fixtures took about 0.08–0.22 s.
- Live system-playback ZGC finalization took about 1.07 s after playback ended,
  including endpoint waiting, compared with about 2.24 s for the prior Nano build.
- Lifecycle checks cover worker reuse, cancellation, restart, startup cancellation,
  model disposal, settings persistence, queue overload and stale-result suppression.
- Existing application checks cover login gating, title order/navigation, screenshots,
  tray registration, passthrough and window layout.

## Distribution

The model is approximately 2.18 GB. Large distributions use a ZIP64 archive and the
pinned LZMA SDK installer module; 7-Zip is needed only on the build machine.
Model/runtime revisions and SHA256 values are pinned in `download-qwen-asr.ps1`.
The package contains model/runtime licenses. Earlier Nano assets remain available in
the source checkout for comparison but are excluded from the 1.5.0 publish output.
