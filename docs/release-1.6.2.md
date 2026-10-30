# willGo 1.6.2

Expand speech finalization protection from configured hotwords to an embedded,
case-insensitive vocabulary of 3,530 unique terms in 22 categories. Coverage includes
Java, collections, JVM/GC, concurrency, Spring/ORM, databases, Redis, messaging,
distributed systems, networking, Linux, cloud/devops, languages/testing, algorithms,
security/architecture, transformers, RAG/vector search, agents/orchestration, training,
inference serving, evaluation and speech/multimodal applications.

The curated vocabulary is stored in Assets/speech-technical-vocabulary.json with
reference URLs. JavaGuide interview guides and official LangGraph, LangChain retrieval,
MCP, vLLM quantization and Spring AI documentation were consulted for coverage. The
catalog also includes related names from engineering knowledge; it is not a verbatim
extract or a claim that every possible future interview term can be enumerated.

## Protection Rules

Built-in protection is independent of saved configuration and the recognizer's
100-hotword limit. Existing installations, including those with empty custom hotwords,
receive the complete vocabulary. Custom protection entries after number 100 are
honored, while the streaming decoder's biasing list remains bounded.

Chinese/English mixed utterances also protect previously recognized English tokens,
even when a token is absent from the catalog. Identifier-style tokens (CamelCase,
uppercase acronyms, underscores, namespaces, dotted names, versions and paths) are
protected in English-only utterances as well. This covers private/new framework names
without requiring a catalog update. Ordinary numeric text is excluded from this
identifier fallback, though specific catalogued codes can be protected.

Whole-utterance refinement is accepted only when those terms retain their occurrence
counts, with case-insensitive identifier boundaries. If it loses a term or transliterates
it into Chinese, the original normalized streaming sentence is retained. Matching a
short term inside a retained longer phrase still counts it correctly. Exactly spelled
space-separated built-in acronyms can be joined; missing letters are never invented.

Neither recognizer receives the entire catalog as a decoder prompt or biasing list.
Only words already present in the streaming utterance become preservation candidates.
This prevents the catalog from forcing unspoken words into recognition and keeps its
cost separate from inference. The vocabulary is compiled/cached once per process,
and custom protection matchers are reused by the session's finalization queue.

The final event remains the shared source for question creation, answer-model prompts,
displayed final transcription and session Markdown. This change preserves recognized
English; it cannot recover a term that both recognizers failed to recognize.

## Validation

Every catalog term is tested for preservation in an English question with an empty
custom word list. Additional cases cover mixed case, multiple/repeated terms, phrases,
identifier boundaries, punctuation, previously unknown names, spaced acronyms, normal
English spelling/Chinese correction, custom entries beyond 100, stop fallback, question
prompt and archive consistency. A self-contained single-file test build also loads and
tests the embedded catalog. No new claim about overall acoustic recognition accuracy
is made from these deterministic output-protection checks.
