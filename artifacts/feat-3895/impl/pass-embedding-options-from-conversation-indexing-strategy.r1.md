# Implementation: pass-embedding-options-from-conversation-indexing-strategy

## What was implemented

`ConversationIndexingStrategy` (the fourth call site missed by the original FR-4 spec)
now takes an `IOptions<KnowledgeBaseOptions>` constructor dependency and passes
`_options.ToEmbeddingOptions()` (model + dimensions from the feature's own
`KnowledgeBase:*` config section) to `IEmbeddingGenerator.GenerateAsync`, instead of
relying on the adapter-wide embedding fallback. This keeps conversation indexing in
sync with `KnowledgeBaseDocIndexingStrategy`, which already receives the same
dependency and is registered one line above it in `KnowledgeBaseModule.cs`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` — added `IOptions<KnowledgeBaseOptions>` constructor parameter, stored `_options`, changed `GenerateAsync(topics, cancellationToken: ct)` to `GenerateAsync(topics, _options.ToEmbeddingOptions(), ct)`.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs` — updated constructor call in the test fixture setup to pass `Options.Create(new KnowledgeBaseOptions())`; added a new test `CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator` that asserts a custom `EmbeddingModel`/`EmbeddingDimensions` combination is forwarded verbatim to the embedding generator call.

## Tests

- `ConversationIndexingStrategyTests` (8 tests total, 7 existing + 1 new) — all pass.
  New test captures the `EmbeddingGenerationOptions` argument via a Moq `Callback` and
  asserts `ModelId == "text-embedding-3-small"` and `Dimensions == 3072` when the
  strategy is constructed with those `KnowledgeBaseOptions` values.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```
Result: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`.

Confirmed the change compiles cleanly and doesn't regress anything else:
```bash
dotnet build Anela.Heblo.sln
```
Result: `Build succeeded. 0 Error(s)` (13 pre-existing warnings, all unrelated to this
change; one pre-existing `MSB3073` warning from the `AccessMatrixGen` post-build tool
crashing in this sandbox, unrelated to this task and present before this change).

`dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes`
exited 0 (no formatting diffs).

## Notes

Followed the task-context steps exactly: constructor DI change on
`ConversationIndexingStrategy`, pass-through of `_options.ToEmbeddingOptions()`
(inherited from `RagFeatureOptions`) to `GenerateAsync`, and the one new unit test
specified. No other call sites, DI registrations, or config touched — `KnowledgeBaseModule.cs`
already resolves `IOptions<KnowledgeBaseOptions>` for the sibling
`KnowledgeBaseDocIndexingStrategy`, so no DI wiring changes were needed.

## PR Summary
Extends the `KnowledgeBase:*` embedding-config passthrough (FR-4) to the one call site the
original spec missed: `ConversationIndexingStrategy.CreateChunksAsync`. Without this, conversation
indexing would keep using the OpenAI adapter's default embedding model/dimensions instead of the
`KnowledgeBase` feature's own configured `EmbeddingModel`/`EmbeddingDimensions`, silently
detaching it from the rest of the KnowledgeBase config rename.

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` — inject `IOptions<KnowledgeBaseOptions>`, pass `_options.ToEmbeddingOptions()` into `GenerateAsync`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs` — updated constructor call, added a test asserting the configured model/dimensions reach the embedding generator

## Status
DONE
