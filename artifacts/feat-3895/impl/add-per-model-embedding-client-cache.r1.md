# Implementation: add-per-model-embedding-client-cache

## What was implemented

Replaced the single `Lazy<EmbeddingClient>` field in `OpenAiEmbeddingGenerator` with a
`ConcurrentDictionary<string, Lazy<EmbeddingClient>>` keyed by model name, so that
`EmbeddingGenerationOptions.ModelId` overrides passed per-call are honored instead of being
silently ignored. `GenerateAsync` now resolves `model = options?.ModelId ?? _options.EmbeddingModel`
and looks up (or lazily constructs) the client for that model via `_clients.GetOrAdd(model, ...)`.

An internal `Func<string, EmbeddingClient> clientFactory` constructor seam was added so tests can
intercept client construction per resolved model and assert on which models were actually built,
without hitting the real OpenAI endpoint. The public two-argument constructor and the existing
`internal ... EmbeddingClient? client` test constructor are unchanged in behavior: the injected
client is seeded into the cache under `_options.EmbeddingModel`, so all 7 pre-existing tests (which
never pass `ModelId`) still resolve to that exact injected client, exercising the same fake HTTP
transport as before.

Entries are kept as `Lazy<EmbeddingClient>` (not bare values) because
`ConcurrentDictionary.GetOrAdd`'s factory delegate can run more than once under concurrent access
if two threads race on the same missing key — `Lazy<T>` (default `ExecutionAndPublication` mode)
guarantees the `EmbeddingClient` constructor for a given model runs at most once, preserving the
single-construction guarantee the original single-client field had. No eviction policy was added:
cache keys originate only from operator-controlled configuration (`RagFeatureOptions.EmbeddingModel`
values), never from arbitrary user input, so the set of distinct models seen per process is small
and bounded — unbounded growth is not a concern.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — replaced the
  single `Lazy<EmbeddingClient> _client` field with `ConcurrentDictionary<string,
  Lazy<EmbeddingClient>> _clients`; added a `Func<string, EmbeddingClient> _clientFactory` field;
  extended the internal test constructor with an optional `clientFactory` parameter;
  `GenerateAsync` now resolves the model from `options?.ModelId ?? _options.EmbeddingModel` and
  fetches/creates the corresponding cached client.
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` — added a
  `RecordingClientFactory` test double (records every model requested and builds a fake-transport
  `EmbeddingClient` for it) plus a `BuildGeneratorWithFactory` helper, and four new tests:
  `GenerateAsync_ModelIdOverride_UsesOverriddenModel`,
  `GenerateAsync_NoModelIdOverride_UsesConfiguredModel`,
  `GenerateAsync_SameModelIdTwice_ConstructsClientOnce`,
  `GenerateAsync_DifferentModelIds_ResolveIndependently`.

## Tests

- The 4 new tests above cover: override model is used and sent to the API; no-override falls back
  to the configured default model; calling with the same overridden `ModelId` twice constructs the
  underlying `EmbeddingClient` only once (2 HTTP calls, 1 factory invocation); alternating between
  two distinct `ModelId`s resolves each independently and each is constructed exactly once (cache
  reuse verified across both model keys).
- All 7 pre-existing tests in the file continue to pass unmodified, confirming the injected-client
  test seam (`BuildGenerator`) still routes through the cache under `_options.EmbeddingModel`
  exactly as before.
- Full adapter test project run: `dotnet test
  backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` →
  **13/13 passed**, 0 failed, 0 skipped.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs --verify-no-changes
```

Confirmed: 13/13 tests pass; full solution builds with 0 errors (pre-existing unrelated warnings
only); `dotnet format --verify-no-changes` reports no formatting diffs on the touched files.

## Notes

Followed the task-context's prescribed implementation verbatim (steps 1–6), including the exact
code for the production class and the four new tests. No deviations from the task-context.

## PR Summary

Per-call `EmbeddingGenerationOptions.ModelId` overrides were previously accepted by
`OpenAiEmbeddingGenerator.GenerateAsync` but silently ignored — every call used the single client
built for the configured default model. `_client` is now a `ConcurrentDictionary`-backed per-model
cache, so an override resolves to (and reuses) a distinct `EmbeddingClient` for that model, while
calls without an override keep hitting the default-model client exactly as before.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — per-model
  `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` cache, `ModelId` resolution in
  `GenerateAsync`, internal `clientFactory` test seam
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` —
  `RecordingClientFactory` test double, `BuildGeneratorWithFactory` helper, 4 new tests covering
  override/no-override/reuse/multi-model resolution

## Status
DONE
