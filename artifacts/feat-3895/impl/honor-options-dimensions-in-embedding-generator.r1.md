# Implementation: honor-options-dimensions-in-embedding-generator

## What was implemented

`OpenAiEmbeddingGenerator.GenerateAsync` now honors a per-call
`Microsoft.Extensions.AI.EmbeddingGenerationOptions.Dimensions` override when
one is supplied, falling back to the configured `_options.EmbeddingDimensions`
when it is not (FR-2). Implements the "Dimensions fallback" slice only — no
per-model client caching yet (that is a later task).

Also extended the test file's shared `BuildEmbeddingResponse` helper to
optionally capture the `model`/`dimensions` fields of the outgoing request
body, and added the `MeaiOptions` alias to the test file's usings (the
production file already had this alias; the test file did not).

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — `GenerateAsync` now computes `var dimensions = options?.Dimensions ?? _options.EmbeddingDimensions;` and uses it when building the OpenAI SDK's `EmbeddingGenerationOptions`, instead of always using `_options.EmbeddingDimensions`.
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` — added `MeaiOptions` alias; extended `BuildEmbeddingResponse` with optional `capturedModels`/`capturedDimensions` output parameters; added two new tests.

## Tests

- `GenerateAsync_DimensionsOverride_SendsOverriddenDimensions` — passing `new MeaiOptions { Dimensions = 3072 }` results in `3072` being sent as the request's `dimensions` field, overriding the configured value of `3`.
- `GenerateAsync_NoDimensionsOverride_SendsConfiguredDimensions` — calling without options sends the configured `dimensions` value (`7`) unchanged, confirming the fallback path and that pre-existing behavior is preserved.

All 7 pre-existing tests plus the 2 new tests pass (9/9).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```
Expected: 9 passed, 0 failed.

```bash
dotnet build Anela.Heblo.sln
```
Expected: 0 errors (pre-existing warnings unrelated to this change only).

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs --verify-no-changes
```
Expected: exit code 0, no output.

## Notes

Followed the task-context steps exactly, with one correction: the task
context's Step 1 diff (adding the `MeaiOptions` alias to the test file) was
verified necessary by direct inspection — the production file already had
this alias but the test file did not, matching the task description's
premise. No other deviations from the task-context steps.

## PR Summary
Made `OpenAiEmbeddingGenerator.GenerateAsync` respect a caller-supplied `EmbeddingGenerationOptions.Dimensions` value instead of always using the statically configured `EmbeddingDimensions`, falling back to the configured value when no override is given. This is the foundational FR-2 slice — no per-model client caching yet, just the dimensions fallback and the test-helper plumbing (`capturedModels`/`capturedDimensions`) that later tasks in this feature will reuse to assert on outgoing `model`/`dimensions` request fields.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — `dimensions = options?.Dimensions ?? _options.EmbeddingDimensions`
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` — `MeaiOptions` alias, extended `BuildEmbeddingResponse` helper, two new tests

## Status
DONE
