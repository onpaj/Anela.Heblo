# Code Review: add-ragfeatureoptions-toembeddingoptions-helper

## Summary

The implementation adds `RagFeatureOptions.ToEmbeddingOptions()` exactly as specified in the task context: a new `using Microsoft.Extensions.AI;` import, and a new helper method placed directly after `ToExpansionConfig()` that maps `EmbeddingModel`/`EmbeddingDimensions` into `EmbeddingGenerationOptions.ModelId`/`Dimensions`. The two required tests were added in the exact location specified (after `RagFeatureOptions_BaseDefault_HasEmptyPrompt`, before the `ConcreteRagOptions` nested class) and match the specified assertions verbatim. All 5 tests in `RagFeatureOptionsTests` pass, and the Application project builds with 0 errors.

## Review Result: PASS

### task: add-ragfeatureoptions-toembeddingoptions-helper
**Status:** PASS

## Docs to Update
(None — this is an internal helper method with no public API, CLI, or operational surface change. The XML doc comment on the method itself is sufficient.)

## Overall Notes

- The diff is surgical: only the two files named in the task context (`RagFeatureOptions.cs`, `RagFeatureOptionsTests.cs`) were touched; no call sites were modified, consistent with this task's scope as a prerequisite for later call-site tasks in the plan.
- `Microsoft.Extensions.AI` was already a package reference in both the `Anela.Heblo.Application` and `Anela.Heblo.Tests` csproj files, so no project file changes were required.
- Verified via `dotnet test ... --filter "FullyQualifiedName~RagFeatureOptionsTests"`: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.
- Verified via `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`: 0 errors (only pre-existing, unrelated warnings).

**Status:** PASS
