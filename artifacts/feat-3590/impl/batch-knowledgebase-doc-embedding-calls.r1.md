# Implementation: batch-knowledgebase-doc-embedding-calls

## What was implemented
`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` previously called `_embeddingGenerator.GenerateAsync` once per chunk inside its loop (N round trips for an N-chunk document). The method was restructured to a two-pass flow: (1) a zero-chunk guard that returns `[]` immediately without calling the embedding generator, (2) a sequential loop that collects all chunk summaries first (unchanged behavior — summarization stays sequential per chunk), then (3) a single batched `_embeddingGenerator.GenerateAsync(summaries, ...)` call for the whole document, followed by an index-aligned assembly loop that zips `chunkTexts[i]` / `summaries[i]` / `embeddings[i]` into each `KnowledgeBaseChunk`. This mirrors the pattern already used by the sibling `ConversationIndexingStrategy`. The public method signature, constructor, `Supports`, and all other class members are unchanged.

The existing unit test file was updated because its shared embedding-generator mock always returned exactly one `Embedding<float>` regardless of input size, which is incompatible with a batched call for any test producing more than one chunk.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — `CreateChunksAsync` rewritten to batch the embedding call and guard the zero-chunk case.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — tightened `CreateChunksAsync_ProducesChunksWithEmbeddings`'s call-count assertion from `Times.AtLeastOnce` to `Times.Exactly(1)`; overrode the embedding-generator mock in `CreateChunksAsync_ChunkIndexIsSequential` (5-chunk input) to return one embedding per input summary and added a `Times.Exactly(1)` assertion — this is the primary N→1 regression guard, since it's the only test producing multiple chunks; added a new test `CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator` covering the new zero-chunk guard.

## Tests
`backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — 7 `[Fact]` tests total (6 pre-existing + 1 new), covering: `Supports` for both document types, single-chunk embedding generation, embedding derived from summary, chunk content vs. summary distinction, sequential chunk indexing across multiple chunks with exactly-once batched embedding call, and the new zero-chunk no-call guard.

## How to verify
Run from the repo root (`/home/user/worktrees/feature-3590-Arch-Review-Knowledgebase-N-1-Embedding-Api-Calls`):
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

**Actual results observed** (via `dotnet vstest` against the built test assembly, to avoid a slow/hung MSBuild re-restore path in this sandbox — same test binaries, equivalent to `dotnet test`):
- `dotnet build Anela.Heblo.sln`: **Build succeeded, 0 errors** (1 pre-existing unrelated warning: `AccessMatrixGen` codegen tool throws on a malformed JSON file in this sandbox environment — unrelated to this change, does not fail the build).
- `KnowledgeBaseDocIndexingStrategyTests`: **Passed: 7, Failed: 0, Skipped: 0, Total: 7**.
- `ConversationIndexingStrategyTests` (sibling reference pattern, must be unaffected): **Passed: 7, Failed: 0, Skipped: 0, Total: 7**.
- Broader `KnowledgeBase` filter (`FullyQualifiedName~KnowledgeBase`, 247 tests): **Passed: 232, Failed: 15** — all 15 failures are pre-existing in `KnowledgeBaseRepositoryIntegrationTests` (Testcontainers/PostgreSQL requiring a Docker daemon, unavailable in this sandbox); none touch `KnowledgeBaseDocIndexingStrategy` or `ConversationIndexingStrategy`, and none are caused by this change.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <the two touched files>`: exit code 0, no formatting differences.

## Notes
No deviations from the task plan — the implementation matches the plan's exact before/after code and test changes verbatim. The `dotnet build`/`dotnet test` commands as literally specified in the plan (`dotnet build backend/Anela.Heblo.sln`) needed a path correction: the solution file is at the repo root (`Anela.Heblo.sln`), not under `backend/`. Used `dotnet vstest` directly against the already-built test DLL for the verification runs above because `dotnet test`'s own build/restore step was intermittently very slow/stalled in this sandbox; this exercises the identical compiled test binaries.

## PR Summary
Fixes GitHub issue #3590: `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` was issuing one embedding-API call per chunk (N round trips for an N-chunk document) instead of batching them into a single call, unlike the sibling `ConversationIndexingStrategy` which already batches correctly. This change restructures `CreateChunksAsync` to collect all chunk summaries first (summarization stays sequential, as required), then issues exactly one batched `GenerateAsync` call for the whole document, cutting embedding API round trips from O(N) to O(1) per document — and adds a guard so a zero-chunk document never issues an empty-batch call. The existing unit test suite was updated: one test's mock was fixed to return one embedding per input (it previously always returned a single fixed embedding, which would have thrown once the code batched), a `Times.Exactly(1)` assertion was added as the primary regression guard for the N→1 behavior, and a new test covers the zero-chunk guard.

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — batch the embedding generation call, add zero-chunk guard
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — fix mock for multi-chunk test, strengthen call-count assertions, add zero-chunk test

## Status
DONE
