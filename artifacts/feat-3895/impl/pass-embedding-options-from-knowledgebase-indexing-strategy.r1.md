# Implementation: pass-embedding-options-from-knowledgebase-indexing-strategy

## What was implemented

Implements FR-4. `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` previously called
`_embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct)` with no
`EmbeddingGenerationOptions`, so the KnowledgeBase feature's configured
`EmbeddingModel`/`EmbeddingDimensions` (from `KnowledgeBaseOptions`, which derives from
`RagFeatureOptions`) never reached the embedding generator — only the adapter-wide
fallback model/dimensions were ever used. The call site now passes
`_options.ToEmbeddingOptions()` (the helper added in the
`add-ragfeatureoptions-toembeddingoptions-helper` task) as the second positional
argument, so the feature's own model/dimensions are used for every KnowledgeBase
indexing embedding call.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — line 44 changed from `await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct)` to `await _embeddingGenerator.GenerateAsync(summaries, _options.ToEmbeddingOptions(), ct)`.
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — added `CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator`, appended after `CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator`, exactly as specified in the task context.

## Tests

- `KnowledgeBaseDocIndexingStrategyTests.CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator` — builds a strategy with `KnowledgeBaseOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 3072 }`, captures the `EmbeddingGenerationOptions` passed to `IEmbeddingGenerator.GenerateAsync`, and asserts `ModelId`/`Dimensions` match the configured values.

Confirmed the new test fails before the fix (`Assert.NotNull() Failure: Value is null` — no options were passed) and all 8 tests in `KnowledgeBaseDocIndexingStrategyTests` pass after the fix.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests"
```

Result: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`

Also confirmed the full solution builds cleanly:

```bash
dotnet build Anela.Heblo.sln
```

Result: `0 Error(s)` (13 pre-existing, unrelated nullability/obsolete-API warnings, plus a pre-existing `MSB3073` warning from the unrelated `GenerateAccessMatrix` MSBuild target — that target already has `ContinueOnError="true"` and does not fail the build; it is caused by an argument-order mismatch between the `Exec` command in `Anela.Heblo.API.csproj` and `AccessMatrixGen`'s `Program.cs` argv expectations, pre-existing and out of scope for this task).

## Notes

The fix is the single-line call-site change specified exactly in the task context (`_options.ToEmbeddingOptions()`), consuming the helper added in the prior task in this plan. No other behavior changed.

## PR Summary
`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` now passes `_options.ToEmbeddingOptions()` to `IEmbeddingGenerator.GenerateAsync`, so the KnowledgeBase feature's configured `EmbeddingModel`/`EmbeddingDimensions` reach the embedding generator instead of falling back to the adapter-wide default, closing out FR-4 of the embedding-options plan.

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — pass `_options.ToEmbeddingOptions()` to `GenerateAsync`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — added a test verifying the configured model/dimensions are captured on the generator call

## Status
DONE
