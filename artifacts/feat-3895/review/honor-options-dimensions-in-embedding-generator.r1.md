# Code Review: honor-options-dimensions-in-embedding-generator

## Summary
The implementation makes `OpenAiEmbeddingGenerator.GenerateAsync` use `options?.Dimensions ?? _options.EmbeddingDimensions` exactly as specified by FR-2, with both acceptance-criteria tests written and passing. The change is minimal, scoped to the single line the task targeted, and preserves existing behavior when no override is supplied.

## Review Result: PASS

### task: honor-options-dimensions-in-embedding-generator
**Status:** PASS

Verified:
- FR-2 acceptance criterion 1: `GenerateAsync_DimensionsOverride_SendsOverriddenDimensions` calls `GenerateAsync(MakeInputs(1), new MeaiOptions { Dimensions = 3072 })` against a generator configured with `dimensions: 3`, and asserts the outgoing request body's `dimensions` field is `3072` — matches spec verbatim.
- FR-2 acceptance criterion 2: `GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions` calls `GenerateAsync(MakeInputs(1))` with no options against a generator configured with `dimensions: 7`, and asserts the outgoing `dimensions` field is `7` — confirms the fallback/backward-compatible path (NFR-2).
- Production diff is the exact one-line change the task-context specified: `var dimensions = options?.Dimensions ?? _options.EmbeddingDimensions;` feeding `EmbeddingGenerationOptions { Dimensions = dimensions }`.
- All 9 tests pass (7 pre-existing + 2 new); `dotnet build` succeeds with 0 errors; `dotnet format --verify-no-changes` on the touched files reports no formatting drift.
- Test-file diff matches the task-context's steps: `MeaiOptions` alias added (test file lacked it; production file already had it), `BuildEmbeddingResponse` extended with optional `capturedModels`/`capturedDimensions` parameters without altering any of the 7 pre-existing call sites' behavior (all still call the helper positionally with 0-2 args, unaffected by the new optional trailing params).
- Commit was made on the current branch as instructed (`fix(openai): honor EmbeddingGenerationOptions.Dimensions per call`), scoped to only the two files named in the task.

No functional requirement is unmet, no architecture guideline is contradicted, and no correctness bug was found.

## Docs to Update
(None — this is an internal adapter behavior fix with no public API, CLI, or operational surface change; `Dimensions` was already a documented parameter of `EmbeddingGenerationOptions` per NFR-2, it simply now has effect.)

## Overall Notes
This task deliberately lands the shared `capturedModels`/`capturedDimensions` test-helper plumbing that later tasks in this feature (per-call model routing, FR-3 Leaflet call sites) will reuse — confirmed present and unused-but-ready (`capturedModels` isn't asserted on by either new test here, which is expected and consistent with the task-context's own description of this as "the smallest slice").
