# Implementation: add-ragfeatureoptions-toembeddingoptions-helper

## What was implemented

Added a `ToEmbeddingOptions()` helper method to the abstract `RagFeatureOptions` base class, mirroring the existing `ToExpansionConfig()` helper on the same class. This is the single place where a feature's per-config `EmbeddingModel`/`EmbeddingDimensions` values are turned into an `EmbeddingGenerationOptions` instance (the `Microsoft.Extensions.AI` per-call options type), so every call site that needs to invoke `IEmbeddingGenerator<TInput,TEmbedding>` can pass the feature's own configured model/dimensions instead of relying on the adapter-wide fallback. This is arch review Decision 2 / Spec Amendment 2, and is a prerequisite for the call-site tasks that follow in this plan.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs` — added `using Microsoft.Extensions.AI;` and the new `ToEmbeddingOptions()` method (with XML doc comment) directly after `ToExpansionConfig()`.
- `backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs` — added `using Microsoft.Extensions.AI;` and two new tests, `ToEmbeddingOptions_CarriesConfiguredModelAndDimensions` and `ToEmbeddingOptions_UnsetValues_FallBackToClassDefaults`, placed after `RagFeatureOptions_BaseDefault_HasEmptyPrompt` and before the `ConcreteRagOptions` nested class, exactly as specified in the task context.

## Tests

- `RagFeatureOptionsTests.ToEmbeddingOptions_CarriesConfiguredModelAndDimensions` — verifies that when `LeafletOptions.EmbeddingModel`/`EmbeddingDimensions` are explicitly set, `ToEmbeddingOptions()` carries those exact values into `EmbeddingGenerationOptions.ModelId`/`Dimensions`.
- `RagFeatureOptionsTests.ToEmbeddingOptions_UnsetValues_FallBackToClassDefaults` — verifies that a `KnowledgeBaseOptions` instance with no overrides produces `EmbeddingGenerationOptions` carrying the class defaults (`"text-embedding-3-large"` / `1536`).

Both tests, plus the 3 pre-existing tests in the same file, were run and pass.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~RagFeatureOptionsTests"
```

Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`

Also confirmed the Application project builds cleanly with the new `Microsoft.Extensions.AI` using directive:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Result: `0 Error(s)` (only pre-existing, unrelated nullability/obsolete-API warnings).

## Notes

The change is purely additive — no existing call sites were touched, since consuming `ToEmbeddingOptions()` at the actual `IEmbeddingGenerator` call sites is scoped to later tasks in this plan. `Microsoft.Extensions.AI` was already a package reference in both the `Anela.Heblo.Application` and `Anela.Heblo.Tests` csproj files, so no project reference changes were needed.

## PR Summary
Added `RagFeatureOptions.ToEmbeddingOptions()`, the single helper that turns a feature's `EmbeddingModel`/`EmbeddingDimensions` config into the per-call `EmbeddingGenerationOptions` every `IEmbeddingGenerator` call site needs, mirroring the existing `ToExpansionConfig()` helper. This unblocks the remaining call-site tasks in the RAG embedding options plan.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs` — added `ToEmbeddingOptions()` helper method
- `backend/test/Anela.Heblo.Tests/Shared/Rag/RagFeatureOptionsTests.cs` — added two tests covering configured and default-fallback behavior

## Status
DONE
