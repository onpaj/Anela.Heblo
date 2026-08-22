# Implementation: pass-embedding-options-from-leaflet-indexing-service

## What was implemented

`LeafletIndexingService.IndexAsync` now passes the configured `LeafletOptions.EmbeddingModel` /
`EmbeddingDimensions` through to the embedding generator via the shared
`RagFeatureOptions.ToEmbeddingOptions()` helper, instead of calling
`_embeddings.GenerateAsync` with default (unset) `EmbeddingGenerationOptions`. This makes the
`Leaflet:EmbeddingModel` configuration key (already present in `appsettings.json` /
`appsettings.Production.json`) a live setting for leaflet indexing, matching the pattern already
applied to the KnowledgeBase and Conversation indexing strategies and the search-documents handler
in earlier tasks of this feature.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` — line 61 changed from `_embeddings.GenerateAsync(inputs, cancellationToken: ct)` to `_embeddings.GenerateAsync(inputs, _options.ToEmbeddingOptions(), ct)`.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs` — added `IndexAsync_passes_leaflet_model_and_dimensions_to_embedding_generator`, which constructs the service with `LeafletOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 3072 }` and asserts the captured `EmbeddingGenerationOptions` passed to the mocked generator carries those values.

## Tests

- `LeafletIndexingServiceTests` (6 tests, including the new one) — all pass.
  - New test confirmed RED before the fix (`Expected capturedOptions not to be <null>.`) and GREEN after.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletIndexingServiceTests"
```
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

`dotnet format Anela.Heblo.sln --include <changed files> --verify-no-changes` reports no violations.

## Notes

No deviations from the task-context steps. The change is a single-line call-site update plus the
prescribed test, consistent with the pattern from the prior four `pass-embedding-options-from-*`
tasks in this feature.

## PR Summary
Wires `LeafletIndexingService` to pass the configured Leaflet embedding model/dimensions into the
embedding generator call, so `Leaflet:EmbeddingModel`/`Leaflet:EmbeddingDimensions` config keys
take effect during leaflet indexing instead of being silently ignored.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` — pass `_options.ToEmbeddingOptions()` to `_embeddings.GenerateAsync`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs` — new test verifying model/dimensions are forwarded

## Status
DONE
