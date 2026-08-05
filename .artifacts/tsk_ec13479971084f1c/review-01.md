# Review: OpenAiEmbeddingGenerator honours the batch contract

## Verdict: done

## What I checked

Read plan-02.md / design-02.md / architecture-02.md / development-01.md, then verified the
actual diff against them independently (not just trusting development-01.md's claims):

- `git diff main` for `OpenAiEmbeddingGenerator.cs`, `AssemblyInfo.cs`,
  `Anela.Heblo.Adapters.OpenAI.Tests.csproj`, `OpenAiEmbeddingGeneratorTests.cs`, `Anela.Heblo.sln`.
- Confirmed `LeafletIndexingService.cs`, `KnowledgeBaseDocIndexingStrategy.cs`,
  `ConversationIndexingStrategy.cs`, `OpenAiAdapterServiceCollectionExtensions.cs` are untouched
  (empty diffs), as designed.
- Ran `dotnet build Anela.Heblo.sln --no-restore` myself → **0 errors** (250 pre-existing
  warnings, none in touched files).
- Ran `dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/...csproj` myself →
  **7/7 passed**.
- Ran `dotnet format Anela.Heblo.sln --verify-no-changes --include <the 3 changed/added .cs
  files>` myself → clean, exit 0.

## Conformance to the finding

The core defect — `GenerateAsync` fanning a batched request into N sequential
`client.GenerateEmbeddingAsync(input, ...)` calls — is fixed. The rewritten method now:

- Chunks the input list (`inputList.Chunk(MaxBatchSize)`, `MaxBatchSize = 2048`) and calls
  `_client.GenerateEmbeddingsAsync(chunk, ...)` **once per chunk**, i.e. once total for any batch
  ≤ 2048 items — the exact scenario in the finding (20-chunk document → 1 HTTP call, not 20).
  This directly restores the benefit of #3590/#3600's caller-side batching for all three affected
  callers (`LeafletIndexingService`, `KnowledgeBaseDocIndexingStrategy`,
  `ConversationIndexingStrategy`).
- Reorders each chunk's results via `.OrderBy(e => e.Index)` unconditionally before appending,
  so batched results stay positionally correlated to input regardless of API response ordering —
  necessary because callers rely on strict index correlation.
- Builds `EmbeddingClient` once (in the public constructor) and reuses it across calls, instead of
  allocating one per `GenerateAsync` invocation (the "while there" suggestion in the finding).
- Adds an empty-input short-circuit (0 items → no API call), a minor but harmless improvement.
- Preserves the existing Polly retry pipeline and the `ApiKey` guard's original position (first
  line of `GenerateAsync`, not moved to the constructor) — no behavioral regression there.
- Public constructor signature and DI registration (`OpenAiAdapterServiceCollectionExtensions`)
  are unchanged; a new `internal` constructor overload was added purely for test injection, gated
  by a new `AssemblyInfo.cs` `InternalsVisibleTo`. This is a legitimate, minimal test seam,
  consistent with the existing `Anela.Heblo.Adapters.Flexi`/`Plaud` pattern in this codebase.

## Test coverage

7 new tests in `OpenAiEmbeddingGeneratorTests.cs` cover: single batch (1 HTTP call, order
preserved), oversized batch spanning the 2048 chunk boundary (2 HTTP calls, order preserved
across chunks), out-of-order API response corrected by `OrderBy`, empty input (0 HTTP calls),
transient-failure retry/recovery, retry exhaustion (no partial result), and client reuse across
two sequential calls. All pass. This is solid, targeted coverage of the actual behavior change.

## Solution file registration

The three required `.sln` edits (`Project`/`EndProject`, `ProjectConfigurationPlatforms` block,
`NestedProjects` entry) are present and correctly wired to the existing `test` solution folder;
`dotnet build`/`dotnet test` at the solution root pick up the new project, confirmed by my own
build run.

## Non-blocking observations (not blocking, no changes required)

- The retry-exhaustion test (`GenerateAsync_RetriesExhausted_ThrowsWithoutPartialResult`) asserts
  a generic `Exception` rather than a specific type, and development-01.md documents why: the
  OpenAI SDK's own transport pipeline retries `HttpRequestException` internally and wraps
  exhaustion in an `AggregateException`, so Polly's `ShouldHandle<HttpRequestException>()` is a
  dead code path — a pre-existing gap already flagged as out-of-scope in architecture-02.md §4,
  not introduced by this change. Fine to leave as-is.

## Result

Build: 0 errors. Tests: 7/7 passed. Format: clean. Finding's defect is fixed, scope matches the
architecture/design docs exactly, no unrelated files touched, no regressions found.
