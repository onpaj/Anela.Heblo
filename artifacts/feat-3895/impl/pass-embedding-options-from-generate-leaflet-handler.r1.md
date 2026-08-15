# Implementation: pass-embedding-options-from-generate-leaflet-handler

## What was implemented

`GenerateLeafletHandler.Handle` now passes the configured `LeafletOptions.EmbeddingModel` /
`EmbeddingDimensions` through to the embedding generator via the shared
`RagFeatureOptions.ToEmbeddingOptions()` helper, instead of calling `_embeddings.GenerateAsync`
with default (unset) `EmbeddingGenerationOptions` for the topic query vector. This is the
query-time half of FR-3: the topic vector used to search `LeafletChunks` is now produced with the
same model/dimensions the chunks were indexed with (wired in the prior
`pass-embedding-options-from-leaflet-indexing-service` task), matching the pattern
`ChatOptions { ModelId = _options.ChatModel }` already establishes for chat on the same file.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — lines 50-54 changed from `_embeddings.GenerateAsync([queryToEmbed], cancellationToken: ct)` to computing `var embeddingOptions = _options.ToEmbeddingOptions();` and calling `_embeddings.GenerateAsync([queryToEmbed], embeddingOptions, ct)`.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` — added `Handle_passes_leaflet_model_and_dimensions_to_topic_embedding`, which constructs the handler with `LeafletOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 3072 }` and asserts the captured `EmbeddingGenerationOptions` passed to the mocked generator carries those values.

## Tests

- `GenerateLeafletHandlerTests` (16 tests, including the new one) — all pass.
  - New test confirmed RED before the fix (`Expected capturedOptions not to be <null>.`) and GREEN after.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateLeafletHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16`.

`dotnet format Anela.Heblo.sln --include <changed files> --verify-no-changes` reports no violations.

## Notes

No deviations from the task-context steps. The change is a two-line call-site update plus the
prescribed test, consistent with the pattern from the prior five `pass-embedding-options-from-*`
tasks in this feature.

## PR Summary
Wires `GenerateLeafletHandler` to pass the configured Leaflet embedding model/dimensions into the
topic-query embedding call, so the search vector is produced with the same model/dimensions used
when leaflets were indexed, instead of silently falling back to `KnowledgeBase:EmbeddingModel`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — pass `_options.ToEmbeddingOptions()` to `_embeddings.GenerateAsync`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` — new test verifying model/dimensions are forwarded

## Status
DONE
