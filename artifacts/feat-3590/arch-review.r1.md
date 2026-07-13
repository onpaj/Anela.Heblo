# Architecture Review: Batch embedding calls in KnowledgeBaseDocIndexingStrategy

## Skip Design: true

Backend-only internal refactor of a single strategy class's private control flow. No controller, MediatR
contract, DTO, database schema, or UI surface changes. `Skip Design` is `true`.

## Architectural Fit Assessment

This is a textbook example of an isolated Strategy-pattern implementation being brought in line with a
sibling implementation of the same interface. `IIndexingStrategy` (`Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs`)
has exactly two implementations — `KnowledgeBaseDocIndexingStrategy` and `ConversationIndexingStrategy` —
both resolved via `IEnumerable<IIndexingStrategy>` injection into `DocumentIndexingService`
(`DocumentIndexingService.cs:12,38`), which picks the matching strategy by `Supports(document.DocumentType)`.
Neither the interface, the DI registration (`KnowledgeBaseModule.cs`), nor `DocumentIndexingService` needs
to change: `CreateChunksAsync(string, Guid, CancellationToken)` returns `IReadOnlyList<KnowledgeBaseChunk>`
before and after the fix.

The fix aligns `KnowledgeBaseDocIndexingStrategy` with the already-accepted pattern in
`ConversationIndexingStrategy` (collect inputs → single batched `GenerateAsync` call → zip results by
index). There is no new pattern being introduced; this is convergence onto an existing, correct one. Per
`docs/architecture/development_guidelines.md`, this stays entirely inside the KnowledgeBase module's
`Services/` folder — no module-boundary, contract, or persistence rule is implicated.

## Proposed Architecture

### Component Overview

No component, interface, or dependency graph changes. The existing shape is preserved and only the
internal method body of one class changes:

```
DocumentIndexingService
   └─ IEnumerable<IIndexingStrategy>  (unchanged: resolved by DocumentType)
        ├─ ConversationIndexingStrategy      (reference pattern — unchanged)
        └─ KnowledgeBaseDocIndexingStrategy  (← CreateChunksAsync internals change)
                ├─ IWordWindowChunker            (unchanged)
                ├─ IChunkSummarizer               (unchanged; still called once per chunk, sequentially)
                └─ IEmbeddingGenerator<string, Embedding<float>>  (called once per document, not once per chunk)
```

### Key Design Decisions

#### Decision 1: Two-pass loop (summarize-all, then embed-once) vs. streaming/parallel restructuring

**Options considered:**
- (a) Two-pass: loop once to sequentially collect all summaries, then a single `GenerateAsync(summaries)`
  call, then a final pass to assemble `KnowledgeBaseChunk` objects by index (this is the
  `ConversationIndexingStrategy` pattern, and what the spec's illustrative code and the brief's suggested
  fix both show).
- (b) Parallelize summarization (`Task.WhenAll` over `SummarizeAsync`) in addition to batching embeddings.
- (c) Stream chunks through an `IAsyncEnumerable` pipeline to avoid buffering all summaries in memory.

**Chosen approach:** (a), matching the spec's FR-1/FR-3 and the brief verbatim.

**Rationale:** The brief and spec explicitly scope out parallelizing summarization ("sequential — must
stay", FR-1's third bullet, "Out of Scope"). Parallelizing summarization is a distinct optimization with
its own risk profile (concurrent LLM calls, potential rate-limiting, non-deterministic ordering if not
handled carefully) and was deliberately excluded — do not fold it into this change. Option (c) is
unwarranted complexity: chunk counts per document are bounded by `KnowledgeBaseOptions.ChunkSize`/typical
document sizes (the brief cites ~20 chunks as a representative case), so buffering a `List<string>` of
summaries is trivial and matches the existing, working `ConversationIndexingStrategy` precedent exactly.
Introducing streaming here would be inventing a new pattern where a proven one already exists in the same
file's sibling class — against the "surgical changes" principle.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`
  — rewrite `CreateChunksAsync` body only. Constructor, fields, `Supports`, using directives, namespace all
  unchanged.

Test file to extend (already exists, follows Moq + xUnit conventions used across the module):
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`

### Interfaces and Contracts

No interface or contract changes. `IIndexingStrategy.CreateChunksAsync(string cleanText, Guid documentId,
CancellationToken ct) : Task<IReadOnlyList<KnowledgeBaseChunk>>` is unchanged, and so is `KnowledgeBaseChunk`
(domain entity in `Anela.Heblo.Domain.Features.KnowledgeBase`). Implementers must preserve:
- `_summarizer.SummarizeAsync` called once per chunk, in order, awaited sequentially inside a loop (not
  `Task.WhenAll`).
- `_embeddingGenerator.GenerateAsync` called exactly once per `CreateChunksAsync` invocation when
  `chunkTexts.Count > 0`, and not called at all when `chunkTexts.Count == 0` (mirrors the existing guard
  `if (topics.Count == 0) return [];` in `ConversationIndexingStrategy.cs:27-28`).
- Index alignment across `chunkTexts[i]` ↔ `summaries[i]` ↔ `embeddings[i]` ↔ `chunks[i]` must hold; the
  batched `GenerateAsync` result (`GeneratedEmbeddings<Embedding<float>>`) preserves input order, matching
  how `ConversationIndexingStrategy.cs:33,43` already indexes into it.

### Data Flow

Per document (`CreateChunksAsync` invocation):
1. `_chunker.Chunk(cleanText, ...)` → `chunkTexts` (unchanged).
2. Empty-chunk guard: if `chunkTexts.Count == 0`, return `[]` immediately — **new** guard clause not present
   in the current implementation (currently an empty `chunkTexts` just produces an empty `chunks` list via
   a loop that never executes, which is behaviorally equivalent but the explicit guard makes the zero-call
   contract testable/explicit per spec FR-2).
3. Sequential loop: for each `chunkTexts[i]`, `await _summarizer.SummarizeAsync(chunkTexts[i], ct)` →
   append to `summaries` list. No embedding call inside this loop.
4. Single call: `await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct)` → `embeddings`.
5. Assembly loop: for each `i`, build `KnowledgeBaseChunk { ..., Embedding = embeddings[i].Vector.ToArray() }`.
6. Return `chunks`.

This exactly matches the spec's illustrative code (spec.r1.md lines 50-83) and requires no changes to
`DocumentIndexingService.IndexChunksAsync`, which only calls `strategy.CreateChunksAsync(text, document.Id, ct)`
and consumes the returned list opaquely.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test `CreateChunksAsync_EmbeddingIsGeneratedFromSummary` (KnowledgeBaseDocIndexingStrategyTests.cs:84-106) captures only the *first* call's input via a `Callback` that overwrites `capturedEmbeddingInput` — after the fix there is exactly one call with a list of N summaries, so the assertion `Assert.Equal(summary, capturedEmbeddingInput)` still passes only if the test's mocked text produces a single chunk. Behaviorally compatible today, but fragile. | Low | No code change required for this task, but flag it: if a future test adds multi-chunk input, this assertion should switch to checking `capturedEmbeddingInput` is a list/sequence, not a single string via `.First()`. Not a blocker for this fix. |
| The mocked `_embeddingGenerator` in tests returns a fixed `GeneratedEmbeddings` with exactly **one** `Embedding<float>` (`KnowledgeBaseDocIndexingStrategyTests.cs:27-29`) regardless of how many summaries are passed in. After batching, a multi-chunk document (e.g. `CreateChunksAsync_ChunkIndexIsSequential`, 20 words / ChunkSize=5 → multiple chunks) will index into `embeddings[i]` for `i > 0`, which is out of range against a 1-element `GeneratedEmbeddings` — an `IndexOutOfRangeException` at test time. | High | This is a test-fixture gap the implementer must close as part of this change (spec FR-1's acceptance criteria implicitly require it): update the shared mock setup to return one `Embedding<float>` per input summary (e.g. build `GeneratedEmbeddings` sized to `texts.Count()` inside a `Callback`/`Returns` closure keyed off the actual argument), not a fixed single-element list. Add/extend a test asserting `GenerateAsync` is called `Times.Exactly(1)` for a multi-chunk document (currently only `Times.AtLeastOnce` is asserted at line 75) — this is the primary regression guard for NFR-1. |
| Zero-chunk path: current code silently returns `[]` via an empty loop (no explicit guard); if `cleanText` is empty/whitespace and `_chunker.Chunk` returns an empty list, the new explicit guard must return early **before** calling `_embeddingGenerator.GenerateAsync([], ...)`. Some embedding providers throw or behave oddly on an empty batch. | Medium | Explicit `if (chunkTexts.Count == 0) return [];` guard as specified in FR-2, mirroring `ConversationIndexingStrategy.cs:27-28` exactly. Add a unit test for this case (not currently present in `KnowledgeBaseDocIndexingStrategyTests.cs`). |
| `EmbeddingGenerationOptions?` behavior with a multi-item batch vs. single-item calls — if the underlying provider (Azure OpenAI/OpenAI) has an undocumented per-request item cap, a very large document (many chunks) could now fail in one shot where it previously succeeded chunk-by-chunk. | Low | Out of scope per spec ("Handling of partial-batch failures... is preserved as-is"). Existing `KnowledgeBaseOptions.ChunkSize`/`ChunkOverlap` already bound typical chunk counts per document; no evidence in the codebase of documents large enough to hit provider batch limits. Note as a follow-up if it ever surfaces in production logs — not a blocker. |

## Specification Amendments

None to the functional requirements. One clarification for the implementer, not a spec change: the spec's
FR-1 acceptance criteria ("GenerateAsync is invoked exactly once... with all N chunk summaries") cannot be
verified by the existing test suite without updating the `_embeddingGenerator` mock setup in
`KnowledgeBaseDocIndexingStrategyTests.cs` to produce one embedding per input item (see Risks table above).
Treat this mock-fixture fix as part of this task's test changes, not as a separate follow-up — otherwise
`CreateChunksAsync_ChunkIndexIsSequential` and similar multi-chunk tests will throw at runtime once the
implementation batches.

## Prerequisites

None. No migrations, config, or infrastructure changes are required. The single external dependency used
(`IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync(IEnumerable<string>, ...)`) is already in use
by `ConversationIndexingStrategy` today, so no new package, provider configuration, or DI registration is
needed before implementation can start.
