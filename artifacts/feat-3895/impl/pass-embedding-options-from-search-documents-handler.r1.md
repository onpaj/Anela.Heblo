# Implementation: pass-embedding-options-from-search-documents-handler

## What was implemented
`SearchDocumentsHandler.Handle` now passes `_options.ToEmbeddingOptions()` (the feature's configured `EmbeddingModel`/`EmbeddingDimensions`) to `IEmbeddingGenerator.GenerateAsync` when embedding the search query, instead of relying on the adapter-wide default. This is the fifth call site for FR-4 (query-time embedding for KnowledgeBase search), missed by the original spec. It ensures the query vector is generated with the same model/dimensions used to build `KnowledgeBaseChunks.Embedding` by the indexing strategies, so cosine similarity comparisons stay valid.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs` — one-line change: `GenerateAsync([queryToEmbed], cancellationToken: cancellationToken)` → `GenerateAsync([queryToEmbed], _options.ToEmbeddingOptions(), cancellationToken)`.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs` — added `Handle_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator`, which captures the `EmbeddingGenerationOptions` passed to the mocked embedding generator and asserts `ModelId`/`Dimensions` match the configured `KnowledgeBaseOptions`.

## Tests
- `SearchDocumentsHandlerTests.Handle_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator` (new) — verifies the embedding call receives the feature's `EmbeddingModel` ("text-embedding-3-small") and `EmbeddingDimensions` (3072) instead of null/defaults.
- Full `SearchDocumentsHandlerTests` class (22 tests, including the new one) — run to catch regressions in existing behavior (threshold filtering, query expansion, transient-exception handling, logging).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SearchDocumentsHandlerTests"
# Expect: Passed! - Failed: 0, Passed: 22, Skipped: 0, Total: 22

dotnet build ../Anela.Heblo.sln
# Expect: Build succeeded, 0 Error(s)
```

## Notes
- Confirmed the failing-first step: before the source change, the new test failed with `Assert.NotNull() Failure: Value is null` (the mock captured a null `EmbeddingGenerationOptions` because the handler wasn't passing one).
- Verified the change matches the established convention: `RagFeatureOptions.ToEmbeddingOptions()` (in `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs`) is already used identically by `KnowledgeBaseDocIndexingStrategy.cs:44` and `ConversationIndexingStrategy.cs:34`. `KnowledgeBaseOptions` inherits from `RagFeatureOptions`, so no new code was needed for the extension method itself.
- `dotnet format --verify-no-changes` scoped to the two touched files passed with no formatting issues.
- Encountered (and confirmed pre-existing/unrelated) noise during full builds: the `GenerateAccessMatrix` MSBuild target (`Anela.Heblo.API.csproj`, `BeforeTargets="Build"` in Debug) throws a `System.Text.Json.JsonException` while parsing what looks like a `.gitignore`-style path argument. It's wrapped in `ContinueOnError="true"`, so it doesn't fail the build (`Build succeeded`, 0 errors) — it just spews a stack trace and a `MSB3073` warning on every Debug build in this environment. Not touched, since it's unrelated to this task and pre-exists on the branch.
- `artifacts/feat-3895/state.json` was already modified in the working tree before this task started (untouched by me, left out of the commit as instructed).
- Only the two files named in the task were staged and committed; no scope creep into other call sites or the `RagFeatureOptions` base class.

## PR Summary
Fixes the KnowledgeBase search-query embedding call site, which was the one call site FR-4 missed: `SearchDocumentsHandler` now passes `_options.ToEmbeddingOptions()` to `IEmbeddingGenerator.GenerateAsync`, so the query vector is generated with the same `EmbeddingModel`/`EmbeddingDimensions` the indexing strategies use to populate `KnowledgeBaseChunks.Embedding`. Without this, a feature-specific embedding config (different model or dimensionality) would silently fall back to the adapter-wide default at query time, producing incomparable or dimension-mismatched vectors against the stored chunk embeddings. Adds a regression test asserting the passed `EmbeddingGenerationOptions.ModelId`/`Dimensions` match configuration; the full `SearchDocumentsHandlerTests` suite (22 tests) and a solution-wide `dotnet build` both pass.

## Status
DONE
