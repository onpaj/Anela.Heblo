# Specification: Honor per-call embedding model/dimensions overrides in OpenAiEmbeddingGenerator

## Summary

`OpenAiEmbeddingGenerator.GenerateAsync` accepts a `Microsoft.Extensions.AI.EmbeddingGenerationOptions` parameter but never reads it, so every embedding call silently uses the single model/dimensions pair bound at DI time from the `KnowledgeBase:*` config section — regardless of which feature is calling. This fix makes the adapter honor `options.ModelId` / `options.Dimensions` per call (mirroring the existing `AnthropicChatClient.GetResponseAsync` pattern), and updates the two current callers whose own per-feature config already implies an override (`Leaflet:EmbeddingModel`) to actually pass it. It also removes the misleading `KnowledgeBase:*`-sourced default binding for the adapter's fallback model so the config's apparent scope matches its real scope.

## Background

`OpenAiEmbeddingGenerator` (`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs`) is registered once as the shared `IEmbeddingGenerator<string, Embedding<float>>` for the whole app. It is consumed by three call sites today:

- `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` (indexing)
- `LeafletIndexingService.IndexAsync` (indexing)
- `GenerateLeafletHandler.Handle` (query-time embedding of the search topic)

None of these pass an `options` argument, and even if they did, `GenerateAsync` ignores it — the model and dimensions come exclusively from `OpenAiEmbeddingOptions`, which `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` binds solely from `configuration["KnowledgeBase:EmbeddingModel"]` / `configuration["KnowledgeBase:EmbeddingDimensions"]`.

This has two concrete consequences documented in the architecture-review finding (issue #3895):

1. `appsettings.json` sets `Leaflet:EmbeddingModel` to `"text-embedding-3-large"`, and `LeafletOptions` (via `RagFeatureOptions`) binds it correctly — but nothing ever reads `LeafletOptions.EmbeddingModel` into an actual embedding call. It is dead configuration.
2. `KnowledgeBaseChunks.Embedding` and `LeafletChunks.Embedding` are independently-declared `vector(1536)` pgvector columns. They currently agree only because both features' embeddings are unknowingly generated using the single `KnowledgeBase:EmbeddingDimensions`-sourced value. Changing `KnowledgeBase:EmbeddingDimensions` for KnowledgeBase's own reasons would silently break `LeafletChunks` writes (dimension mismatch against the fixed-width `vector(N)` column) with no compiler or config-level signal, because the coupling is invisible in the `Leaflet:*` config section.

`AnthropicChatClient.GetResponseAsync` already establishes the correct pattern for this exact problem on the chat side: `var model = options?.ModelId ?? _options.Model;` (`AnthropicChatClient.cs:77`), which `GenerateLeafletHandler` relies on today (`ChatOptions.ModelId = _options.ChatModel`, `GenerateLeafletHandler.cs:102`) to run Leaflet's chat calls on `Leaflet:ChatModel` independent of `KnowledgeBase:ChatModel`. This spec brings embeddings to parity with that pattern.

## Functional Requirements

### FR-1: `OpenAiEmbeddingGenerator.GenerateAsync` honors `options.ModelId`

`GenerateAsync` must resolve the model to call per-invocation as `options?.ModelId ?? _options.EmbeddingModel`, not solely `_options.EmbeddingModel`.

Because the OpenAI SDK's `EmbeddingClient` is bound to a single model at construction time, the adapter must be able to serve requests for more than one model without reconstructing a client (and its underlying HTTP pipeline) on every call. Replace the single `Lazy<EmbeddingClient>` field with a small per-model cache (e.g. `ConcurrentDictionary<string, EmbeddingClient>`) that lazily creates and caches one `EmbeddingClient` per distinct resolved model id, using the same deferred-construction rule as today (construction must not run at DI-build time — `OpenAI:ApiKey` may be unset in test/local environments).

The existing internal test-seam constructor (`OpenAiEmbeddingGenerator(IOptions<OpenAiEmbeddingOptions>, ILogger<...>, EmbeddingClient? client)`) must keep working unchanged for existing tests: when a client is injected, it is used for the default/no-override path exactly as today.

**Acceptance criteria:**
- Calling `GenerateAsync(values, options: new EmbeddingGenerationOptions { ModelId = "text-embedding-3-small" })` issues the underlying HTTP request against the `text-embedding-3-small` embeddings endpoint path, not `_options.EmbeddingModel`.
- Calling `GenerateAsync(values)` (no options, or `options.ModelId == null`) continues to use `_options.EmbeddingModel`, unchanged from current behavior.
- Two consecutive calls with the same overridden `ModelId` reuse the same cached `EmbeddingClient` instance (no per-call reconstruction) — verified the same way the existing `GenerateAsync_CalledTwice_ReusesSameClient` test verifies it for the default model.
- Two consecutive calls with different `ModelId` values each resolve independently and do not affect each other's cached client.
- All 7 existing tests in `OpenAiEmbeddingGeneratorTests` continue to pass unmodified.

### FR-2: `OpenAiEmbeddingGenerator.GenerateAsync` honors `options.Dimensions`

The per-call `embeddingOptions` (`global::OpenAI.Embeddings.EmbeddingGenerationOptions`) passed to `_client.Value.GenerateEmbeddingsAsync` must use `options?.Dimensions ?? _options.EmbeddingDimensions` for `Dimensions`, instead of unconditionally `_options.EmbeddingDimensions`.

**Acceptance criteria:**
- Calling `GenerateAsync(values, options: new EmbeddingGenerationOptions { Dimensions = 3072 })` sends `Dimensions = 3072` in the outgoing request body.
- Calling `GenerateAsync(values)` without a `Dimensions` override continues to send `_options.EmbeddingDimensions`, unchanged from current behavior.

### FR-3: Leaflet call sites pass their own model/dimensions explicitly

`LeafletIndexingService.IndexAsync` and `GenerateLeafletHandler.Handle` must construct an `EmbeddingGenerationOptions { ModelId = _options.EmbeddingModel, Dimensions = _options.EmbeddingDimensions }` (sourced from the injected `LeafletOptions`, which already binds `Leaflet:EmbeddingModel` / `Leaflet:EmbeddingDimensions` correctly via `RagFeatureOptions`) and pass it to every `_embeddings.GenerateAsync(...)` call they make, instead of calling `GenerateAsync` with no options.

This makes `Leaflet:EmbeddingModel` a live, effective setting for the first time, and decouples Leaflet's embedding dimensionality from whatever `KnowledgeBase:EmbeddingDimensions` happens to be at any given time.

**Acceptance criteria:**
- `LeafletIndexingService.IndexAsync`'s call to `_embeddings.GenerateAsync` passes `options.ModelId == "text-embedding-3-large"` (or whatever `Leaflet:EmbeddingModel` is configured to) — verified via a mock `IEmbeddingGenerator` capturing the `options` argument.
- `GenerateLeafletHandler.Handle`'s call to `_embeddings.GenerateAsync` (the query-topic embedding) passes the same `ModelId`/`Dimensions` sourced from `LeafletOptions`.
- Setting `Leaflet:EmbeddingModel` to a different value than `KnowledgeBase:EmbeddingModel` in config and re-running indexing results in the Leaflet-configured model being the one actually called (observable via the captured request in a test double), where today it would silently still call the KnowledgeBase-configured model.

### FR-4: KnowledgeBase call site passes its own model/dimensions explicitly

`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` must likewise construct `EmbeddingGenerationOptions { ModelId = _options.EmbeddingModel, Dimensions = _options.EmbeddingDimensions }` from its injected `KnowledgeBaseOptions` and pass it to `_embeddingGenerator.GenerateAsync`, rather than relying implicitly on the adapter's DI-time default happening to equal `KnowledgeBaseOptions.EmbeddingModel`.

This makes KnowledgeBase's own behavior independent of the adapter's fallback default (see FR-5), and symmetric with Leaflet's explicit pass-through from FR-3 — removing the last silent reliance on the shared default for either current feature.

**Acceptance criteria:**
- `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync`'s call to `_embeddingGenerator.GenerateAsync` passes `options.ModelId == _options.EmbeddingModel` and `options.Dimensions == _options.EmbeddingDimensions` — verified via a mock `IEmbeddingGenerator` capturing the `options` argument.

### FR-5: Decouple the adapter's DI-time default from `KnowledgeBase:*`

`OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` must stop binding `OpenAiEmbeddingOptions.EmbeddingModel` / `EmbeddingDimensions` from `configuration["KnowledgeBase:EmbeddingModel"]` / `configuration["KnowledgeBase:EmbeddingDimensions"]`. After FR-3 and FR-4, no current caller relies on this value — every call site now passes its own `ModelId`/`Dimensions` explicitly — so this binding exists only as a fallback default for any future `IEmbeddingGenerator` consumer that omits `options`, and its name must not imply it is KnowledgeBase's setting.

Replace the binding with either:
- a neutral, adapter-scoped config key (e.g. `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions`, consistent with the existing `OpenAI:ApiKey` key read two lines above it), or
- no configuration binding at all, relying on `OpenAiEmbeddingOptions`'s existing hardcoded property defaults (`"text-embedding-3-large"` / `1536`).

Either resolves the finding's naming complaint; pick the neutral-key form (`OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions`) for parity with the adjacent `OpenAI:ApiKey` binding and to preserve operators' ability to override the adapter-wide fallback without touching a feature section. No new keys need to be added to `appsettings.json` — omitting them is valid and falls back to the class defaults.

**Acceptance criteria:**
- `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` no longer reads `configuration["KnowledgeBase:EmbeddingModel"]` or `configuration["KnowledgeBase:EmbeddingDimensions"]`.
- With no `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions` config keys set, `OpenAiEmbeddingOptions.Value.EmbeddingModel == "text-embedding-3-large"` and `.EmbeddingDimensions == 1536` (the class defaults) — behavior-preserving for current `appsettings.json`, which sets no such keys.
- Changing `KnowledgeBase:EmbeddingDimensions` in config no longer has any effect on `OpenAiEmbeddingOptions`'s bound value (confirming the coupling is fully removed) — `KnowledgeBaseOptions.EmbeddingDimensions` (bound separately, from the same `KnowledgeBase` section, for the KnowledgeBase feature's own use per FR-4) is unaffected by this change.

## Non-Functional Requirements

### NFR-1: Performance

Per-model `EmbeddingClient` caching (FR-1) must not construct a new HTTP pipeline/client on every call for a repeated model — cache lookups must be O(1) amortized and construction must happen at most once per distinct model id observed by a given `OpenAiEmbeddingGenerator` instance's lifetime (mirroring the current single-client `Lazy<>` behavior, just keyed by model instead of singular).

### NFR-2: Backward compatibility

No public method signature changes. `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync`'s contract is unchanged; only its previously-ignored `options` parameter now has effect. Existing callers that pass no `options` (or `options` with a `null` `ModelId`/`Dimensions`) must observe byte-for-byte identical behavior to today.

### NFR-3: Configuration safety

The change must not require any `appsettings.json` / `appsettings.Production.json` edits to preserve current production behavior: `Leaflet:EmbeddingModel` is already `"text-embedding-3-large"` and `KnowledgeBase:EmbeddingModel`/`EmbeddingDimensions` are already `"text-embedding-3-large"`/`1536` — identical to the values that will now be resolved explicitly per-feature, so no re-indexing or dimension migration is triggered by this fix in the current environment. This must be verified as part of implementation (compare each `RagFeatureOptions`-derived option's effective `EmbeddingModel`/`EmbeddingDimensions` in `appsettings.json` and `appsettings.Production.json` against what the adapter previously used).

## Data Model

No schema changes. Existing pgvector columns are unaffected:
- `KnowledgeBaseChunks.Embedding` — `vector(1536)` (`Migrations/20260331070417_UpgradeEmbeddingTo3Large.cs:19`)
- `LeafletChunks.Embedding` — `vector(1536)` (`Migrations/20260430170922_AddLeafletStore.cs:68`)

Both remain `vector(1536)`; this fix only changes which config value is threaded through to produce embeddings of that width, not the width itself for existing data. `RagFeatureOptions.EmbeddingDimensions` continues to be the source of truth per feature, now actually reaching the API call.

## API / Interface Design

No REST/MediatR-facing API changes. Internal interface impact only:

- `OpenAiEmbeddingGenerator.GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options, CancellationToken)` — same signature, `options` now consulted.
- `LeafletIndexingService.IndexAsync` — internal call to `_embeddings.GenerateAsync` gains an explicit `options` argument.
- `GenerateLeafletHandler.Handle` — internal call to `_embeddings.GenerateAsync` (topic-vector embedding) gains an explicit `options` argument.
- `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` — internal call to `_embeddingGenerator.GenerateAsync` gains an explicit `options` argument.
- `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` — binding source for the default `OpenAiEmbeddingOptions.EmbeddingModel`/`EmbeddingDimensions` changes from `KnowledgeBase:*` to `OpenAI:*` (or is removed in favor of class defaults).

## Dependencies

- `Microsoft.Extensions.AI.Abstractions` (`EmbeddingGenerationOptions.ModelId`, `.Dimensions` — confirmed present in the referenced package version).
- `OpenAI` SDK (`OpenAI.Embeddings.EmbeddingClient`) — per-model client construction is the same pattern already used today (`new EmbeddingClient(model, apiKey)`), just keyed and cached instead of singular.
- No new NuGet packages required.
- Existing test doubles/mocks for `IEmbeddingGenerator<string, Embedding<float>>` in `LeafletIndexingServiceTests`, `GenerateLeafletHandlerTests`, and `KnowledgeBaseDocIndexingStrategyTests` will need their `GenerateAsync` setup/verification updated to account for the now-non-null `options` argument (e.g. `Moq` `It.IsAny<EmbeddingGenerationOptions>()` → capture and assert on it per FR-3/FR-4's acceptance criteria).

## Out of Scope

- Changing which embedding models or dimensions any feature actually uses in production (`appsettings.json`/`appsettings.Production.json` values stay the same after this fix — see NFR-3).
- Backfilling or re-embedding any existing `KnowledgeBaseChunks`/`LeafletChunks` rows.
- Adding runtime validation/guardrails that reject a `Dimensions` override mismatched against the fixed-width `vector(N)` pgvector column (e.g. failing fast if a future config change produces a vector of the wrong length for its target column). This is a real risk called out in the finding's "why it matters" section, but this fix's job is to make each feature's own config actually reach its own embedding calls — cross-checking that config against the DB schema at runtime or migration time is a separate, larger concern (schema/config consistency validation) not requested by this finding.
- Any change to `AnthropicChatClient` or chat-side model resolution — it already implements the correct pattern and is unaffected.
- Introducing a generic/shared "resolve model with fallback" helper shared between the chat and embedding adapters (both now implement the same `options?.X ?? _options.X` shape independently); a shared abstraction is a reasonable future refactor but not required to close this finding.

## Open Questions

None.

## Status: COMPLETE
