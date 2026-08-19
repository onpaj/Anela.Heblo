# Architecture Review: Honor per-call embedding model/dimensions overrides in OpenAiEmbeddingGenerator

## Skip Design: true

Backend-only fix confined to `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI`, `backend/src/Anela.Heblo.Application/Features/{KnowledgeBase,Leaflet}`, and DI registration/config binding. No controller, MediatR contract, or frontend surface changes. No new UI/UX work of any kind.

## Architectural Fit Assessment

This is a straightforward parity fix, not a new pattern. `AnthropicChatClient.GetResponseAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:77`) already implements the correct shape — `options?.ModelId ?? _options.Model` — and `GenerateLeafletHandler` already depends on that exact mechanism today for chat calls (`ChatOptions.ModelId = _options.ChatModel`). The spec's proposal to bring `OpenAiEmbeddingGenerator` to the same shape is the only architecturally consistent option; inventing a different mechanism for embeddings than the one already used for chat would itself be a new inconsistency.

I verified the coupling claims directly:

- `OpenAiEmbeddingGenerator.GenerateAsync` (`OpenAiEmbeddingGenerator.cs:51-65`) takes `MeaiOptions? options` and never reads it; `embeddingOptions.Dimensions` is hard-set from `_options.EmbeddingDimensions`, and the SDK `EmbeddingClient` is built once, lazily, from `_options.EmbeddingModel` (`OpenAiEmbeddingGenerator.cs:46`).
- `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` (`OpenAiAdapterServiceCollectionExtensions.cs:13-18`) binds `OpenAiEmbeddingOptions.EmbeddingModel`/`EmbeddingDimensions` from `configuration["KnowledgeBase:EmbeddingModel"]`/`["KnowledgeBase:EmbeddingDimensions"]` — confirmed, this is the sole source, and it sits inside the *adapter* project, not the KnowledgeBase feature, which is exactly backwards for a value that's supposed to be feature-scoped.
- By contrast, `AnthropicAdapterServiceCollectionExtensions` binds `AnthropicOptions` solely from `Anthropic:*` keys — a genuinely adapter-scoped, feature-neutral config section. This is the existing convention the fix should restore parity with (FR-5's neutral `OpenAI:*` key proposal is exactly this).
- None of the three call sites (`LeafletIndexingService.IndexAsync:61`, `GenerateLeafletHandler.Handle:51`, `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync:44`) pass an `options` argument today — confirmed by reading each file.
- `appsettings.json` confirms `Leaflet:EmbeddingModel = "text-embedding-3-large"` (line 212, no `Leaflet:EmbeddingDimensions` key, so it falls back to `RagFeatureOptions.EmbeddingDimensions = 1536`) and `KnowledgeBase:EmbeddingModel = "text-embedding-3-large"` / `KnowledgeBase:EmbeddingDimensions = 1536` (lines 239-240) — both features currently resolve to the same values, so NFR-3's "no re-indexing triggered" claim holds for the current environment as written.
- `docs/architecture/filesystem.md:158` establishes that I/O-bound services belong in `backend/src/Adapters/`, not in `Features/{Feature}/Services/` — the fix correctly keeps the model-resolution/client-cache logic inside `OpenAiEmbeddingGenerator` (already in `Adapters/`) and only adds call-site *options construction* (not I/O) to the Application-layer callers.

The spec is well-grounded and requires no course-correction. This review focuses on tightening a few implementation details the spec leaves slightly open, and flagging the one design decision (client cache growth) worth being explicit about.

## Proposed Architecture

### Component Overview

```
                       ┌─────────────────────────────────────┐
                       │  IEmbeddingGenerator<string,Embedding<float>>  │  (Microsoft.Extensions.AI abstraction)
                       └───────────────┬───────────────────────┘
                                        │ implemented by
                       ┌────────────────────────────────────────┐
                       │       OpenAiEmbeddingGenerator          │  Adapters/Anela.Heblo.Adapters.OpenAI
                       │  - _options: OpenAiEmbeddingOptions     │  (fallback: OpenAI:EmbeddingModel/Dimensions,
                       │  - _clients: ConcurrentDictionary       │   or class defaults after FR-5)
                       │      <string, Lazy<EmbeddingClient>>    │
                       │  GenerateAsync(values, options):        │
                       │    model = options?.ModelId ?? _options.EmbeddingModel
                       │    dims  = options?.Dimensions ?? _options.EmbeddingDimensions
                       │    client = _clients.GetOrAdd(model, ...)  (lazy per key)
                       └───────────────┬──────────────────────────┘
                                        │ HTTP (OpenAI SDK)
                                        ▼
                              OpenAI Embeddings API

  Callers (Application layer) — each now builds its own EmbeddingGenerationOptions:

  KnowledgeBaseDocIndexingStrategy.CreateChunksAsync        LeafletIndexingService.IndexAsync
  ┌───────────────────────────────┐                          ┌───────────────────────────────┐
  │ options = new EmbeddingGeneration-│                       │ options = new EmbeddingGeneration- │
  │   Options {                   │                          │   Options {                    │
  │     ModelId = _options.EmbeddingModel (KnowledgeBaseOptions) │  ModelId = _options.EmbeddingModel (LeafletOptions) │
  │     Dimensions = _options.EmbeddingDimensions }│           │     Dimensions = _options.EmbeddingDimensions }│
  └───────────────────────────────┘                          └───────────────────────────────┘
                                                                GenerateLeafletHandler.Handle (query-time)
                                                                same LeafletOptions-sourced options object
```

Compare to the existing, already-correct chat-side flow, unchanged by this fix:

```
GenerateLeafletHandler.Handle → ChatOptions{ModelId=_options.ChatModel} → AnthropicChatClient.GetResponseAsync
                                                                              (options?.ModelId ?? _options.Model)
```

### Key Design Decisions

#### Decision 1: Per-model client cache vs. single `Lazy<EmbeddingClient>`

**Options considered:**
- (a) Keep a single `Lazy<EmbeddingClient>` and reconstruct `EmbeddingClient` on every call when the resolved model differs from the cached one.
- (b) Replace with a `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` (or `ConcurrentDictionary<string, EmbeddingClient>` populated via `GetOrAdd`), one entry per distinct resolved model id, built lazily on first use of that model.
- (c) Construct a fresh `EmbeddingClient` per call, no caching.

**Chosen approach:** (b), as the spec (FR-1) directs.

**Rationale:** `EmbeddingClient` wraps an HTTP pipeline; per-call construction (c) is wasteful and changes existing steady-state behavior (the current single-client path already caches, per NFR-1). Because the number of distinct embedding models used in this codebase is bounded by the number of `RagFeatureOptions`-derived features (currently two: KnowledgeBase and Leaflet, both today configured to the same model string in practice, but that's not guaranteed going forward), a `ConcurrentDictionary` keyed by model id is unbounded only in theory — in practice it grows to the number of *distinct model strings ever requested by this process*, which is a small, config-driven number, not user input. No eviction policy is needed. Use `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` with `GetOrAdd(model, m => new Lazy<EmbeddingClient>(() => ...))`, not `ConcurrentDictionary<string, EmbeddingClient>` with a factory delegate directly in `GetOrAdd` — the dictionary-level `GetOrAdd` factory overload can run the factory more than once under contention (the *value* that wins the race is still consistent, but the delegate itself, i.e. `new EmbeddingClient(...)`, can execute redundantly). Wrapping each entry in its own `Lazy<T>` (with default `LazyThreadSafetyMode.ExecutionAndPublication`) guarantees the `EmbeddingClient` constructor runs at most once per model even under concurrent first-use, matching the existing single-model `Lazy<EmbeddingClient>`'s guarantee today. This preserves NFR-1 exactly, just keyed.

**Test-seam constructor caveat:** the existing `internal OpenAiEmbeddingGenerator(IOptions<...>, ILogger<...>, EmbeddingClient? client)` constructor injects one pre-built `EmbeddingClient` for tests. Per spec: "when a client is injected, it is used for the default/no-override path exactly as today." Concretely: seed the cache with that injected client keyed under `_options.EmbeddingModel` (the default model), so calls with no `ModelId` override (or `ModelId == _options.EmbeddingModel`) hit the injected client, and calls with a *different* `ModelId` fall through to `GetOrAdd`'s real-construction path — which will attempt a live `new EmbeddingClient(...)` using `_options.ApiKey`. This is fine for production but means any *new* test added under FR-3/FR-4 acceptance criteria that wants to verify a non-default model against a fake HTTP handler needs the injected client's key to match the model under test, or needs a second test-seam entry point. Flag this explicitly in the spec amendment below — the existing single-client test seam doesn't naturally extend to "inject a client for model X"; the 7 existing tests only ever exercise the default model, so they remain unaffected by seeding under the default key, but no *new* adapter-level test can currently exercise the override path against a fake transport without a small test-seam extension.

#### Decision 2: Where call-site `EmbeddingGenerationOptions` construction lives

**Options considered:**
- (a) Each caller (`LeafletIndexingService`, `GenerateLeafletHandler`, `KnowledgeBaseDocIndexingStrategy`) builds its own `EmbeddingGenerationOptions` inline, as the spec directs (FR-3/FR-4).
- (b) Add a shared helper (e.g. on `RagFeatureOptions`) — `ToEmbeddingOptions()` — mirroring `RagFeatureOptions.ToExpansionConfig()`, which already exists for query-expansion config.

**Chosen approach:** (b) — add `RagFeatureOptions.ToEmbeddingOptions()` returning `Microsoft.Extensions.AI.EmbeddingGenerationOptions { ModelId = EmbeddingModel, Dimensions = EmbeddingDimensions }`, and have all three call sites use it instead of duplicating the same three-line object initializer independently.

**Rationale:** `RagFeatureOptions` already has exactly this shape of helper (`ToExpansionConfig()`, `RagFeatureOptions.cs:35-36`) for the same reason — turning per-feature option fields into a typed argument for a shared abstraction, used by both `KnowledgeBaseOptions` and `LeafletOptions` consumers. `EmbeddingGenerationOptions` construction is identical across all three call sites (spec's own FR-3/FR-4 wording is verbatim identical for both). Duplicating it three times invites drift (e.g. one call site later reading `_options.Dimensions` from a different field). This is a small, low-risk deviation from the letter of the spec (which describes inline construction) but not from its intent — it's the same object, built the same way, just via the established per-feature-options helper convention already present in the file the spec cites for the pre-existing `ChatOptions` pattern comparison. If the analyst/planner prefers strict adherence to "construct inline" wording, this is safe to leave as inline duplication instead; it does not change behavior, only maintainability. Flagged as a spec amendment below rather than silently substituted.

## Implementation Guidance

### Directory / Module Structure

No new files. Modified files only:

- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — replace `Lazy<EmbeddingClient> _client` field with `ConcurrentDictionary<string, Lazy<EmbeddingClient>> _clients`; resolve `model`/`dimensions` from `options` with fallback; update the test-seam constructor to seed the cache under the default model key.
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs` — change binding source from `KnowledgeBase:EmbeddingModel`/`KnowledgeBase:EmbeddingDimensions` to `OpenAI:EmbeddingModel`/`OpenAI:EmbeddingDimensions` (FR-5).
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs` — add `ToEmbeddingOptions()` helper (Decision 2; optional per amendment).
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` — pass `options` to `_embeddings.GenerateAsync` (line 61).
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — pass `options` to `_embeddings.GenerateAsync` (line 51).
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — pass `options` to `_embeddingGenerator.GenerateAsync` (line 44).
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiEmbeddingGeneratorTests.cs` — add new tests for override behavior (FR-1/FR-2 acceptance criteria: different `ModelId` hits different endpoint path / cache entry, `Dimensions` override reaches request body); existing 7 tests must remain green unmodified.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs`, `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`, `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` — all three confirmed to exist; update their `IEmbeddingGenerator` mock setups to capture/assert on the now-non-null `options` argument per FR-3/FR-4 acceptance criteria.

This stays entirely within the existing module boundaries — `Adapters/Anela.Heblo.Adapters.OpenAI` remains the only place doing HTTP/SDK I/O (consistent with `docs/architecture/filesystem.md:158`'s I/O-placement rule), and the two `Features/*` callers only gain a few lines constructing an options object, not new responsibilities.

### Interfaces and Contracts

No public interface changes. `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync`'s signature is untouched (NFR-2) — this is a `Microsoft.Extensions.AI.Abstractions` interface, not owned by this codebase, so no contract versioning concern applies.

Key internal shape (unchanged from spec, confirmed against actual source):

```csharp
// OpenAiEmbeddingGenerator.cs
private readonly ConcurrentDictionary<string, Lazy<EmbeddingClient>> _clients = new();

public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
    IEnumerable<string> values,
    MeaiOptions? options = null,
    CancellationToken cancellationToken = default)
{
    ...
    var model = options?.ModelId ?? _options.EmbeddingModel;
    var dimensions = options?.Dimensions ?? _options.EmbeddingDimensions;
    var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = dimensions };
    var client = _clients.GetOrAdd(model, m => new Lazy<EmbeddingClient>(
        () => /* injected test client if model == default and one was provided, else */ new EmbeddingClient(m, _options.ApiKey)))
        .Value;
    ...
    async token => await client.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token)
    ...
}
```

Note `client` must be resolved per-call now (not once via `_client.Value` as today), since different calls on the same generator instance can resolve different models.

Config contract change (FR-5):

```csharp
// OpenAiAdapterServiceCollectionExtensions.cs
opts.EmbeddingModel = configuration["OpenAI:EmbeddingModel"] ?? opts.EmbeddingModel;
opts.EmbeddingDimensions = configuration.GetValue("OpenAI:EmbeddingDimensions", opts.EmbeddingDimensions);
```
(Verified this mirrors the existing `opts.ApiKey = configuration["OpenAI:ApiKey"] ?? ""` line immediately above it, and the same pattern `AnthropicAdapterServiceCollectionExtensions` already uses for `Anthropic:*`.)

### Data Flow

**Indexing (KnowledgeBase):** `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` → builds `options` from its own `KnowledgeBaseOptions` (bound from `KnowledgeBase:EmbeddingModel`/`EmbeddingDimensions`) → `_embeddingGenerator.GenerateAsync(summaries, options, ct)` → `OpenAiEmbeddingGenerator` resolves `text-embedding-3-large`/`1536` from the passed options (not from adapter default) → cached `EmbeddingClient["text-embedding-3-large"]` → OpenAI API.

**Indexing (Leaflet):** `LeafletIndexingService.IndexAsync` → builds `options` from `LeafletOptions` (bound from `Leaflet:EmbeddingModel`/`EmbeddingDimensions`, currently `text-embedding-3-large`/default `1536`) → same generator, same cache key today (values happen to coincide) but now independently sourced — a future change to `Leaflet:EmbeddingModel` alone takes effect without touching `KnowledgeBase:*`.

**Query-time (Leaflet):** `GenerateLeafletHandler.Handle` → same `LeafletOptions`-sourced options for the single query-topic embedding call, so the topic vector is generated with the same model/dimensions used to index `LeafletChunks`/`KnowledgeBaseChunks` — consistent with `_kb.SearchSimilarAsync`/`_leaflets.SearchSimilarAsync` expecting vectors of matching dimensionality.

**Adapter default fallback:** any *future* consumer of `IEmbeddingGenerator` that calls `GenerateAsync` with no `options` (or `options.ModelId == null`) falls back to `_options.EmbeddingModel`/`EmbeddingDimensions`, now sourced from `OpenAI:EmbeddingModel`/`OpenAI:EmbeddingDimensions` (absent today, so class defaults `text-embedding-3-large`/`1536` apply) — no behavior change for the current two features once FR-3/FR-4 land, since neither will hit this fallback anymore.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ConcurrentDictionary<string, Lazy<EmbeddingClient>>.GetOrAdd` with a plain (non-`Lazy`-wrapped) factory can construct `EmbeddingClient` more than once under concurrent first-use of the same new model, defeating NFR-1's "construct at most once" guarantee | Medium | Wrap each cache entry in `Lazy<EmbeddingClient>` as described in Decision 1, not a bare `EmbeddingClient` returned directly from `GetOrAdd`'s factory delegate |
| Existing 7 tests use the test-seam constructor with one injected `EmbeddingClient`; if the cache isn't seeded under the *correct* key (`_options.EmbeddingModel`, i.e. `"text-embedding-3-small"` per `BuildGenerator`), all 7 tests silently fall through to real `EmbeddingClient` construction and fail against `_options.ApiKey = "test-key"` with no fake transport | High (breaks the acceptance bar "all 7 existing tests continue to pass unmodified") | Seed the cache in the internal constructor exactly as today's single-`Lazy` field is built: `_clients[_options.EmbeddingModel] = new Lazy<EmbeddingClient>(() => client ?? new EmbeddingClient(_options.EmbeddingModel, _options.ApiKey))` when `client != null`. Confirmed the test harness's `BuildGenerator` always sets `EmbeddingModel = "text-embedding-3-small"`, so keying seed under `_options.EmbeddingModel` is correct and sufficient |
| No new tests currently have a way to inject a fake client for a *non-default* model (override path), so FR-1's first acceptance criterion ("issues the underlying HTTP request against the text-embedding-3-small endpoint path") needs either an assertion against the real endpoint URL construction logic, or a small test-seam extension (e.g. accept a factory delegate instead of a single client) | Low-Medium | Extend the internal test constructor to optionally accept a `Func<string, EmbeddingClient>` or a small dictionary instead of a single `EmbeddingClient`, OR — simpler — keep asserting purely on `options.ModelId` propagation into the outgoing request body (the request body includes `model`), which the existing `StatefulHandler`/`BuildEmbeddingResponse` test infrastructure already inspects (`doc.RootElement.GetProperty("input")`) and can be extended to also read `.GetProperty("model")`. Prefer this over changing the test-seam constructor shape, since NFR-2 implies minimal surface change |
| FR-5's config-key rename (`KnowledgeBase:*` → `OpenAI:*`) is a silent behavior change for any *other, not-yet-existing* code path that might already rely on the adapter's DI-time default reading `KnowledgeBase:*` | Low | Confirmed via grep: only `KnowledgeBaseDocIndexingStrategy` and `LeafletIndexingService`/`GenerateLeafletHandler` consume `IEmbeddingGenerator` today, and both move to explicit per-call options under FR-3/FR-4, so nothing is left depending on the adapter default by the time FR-5 ships. Land FR-3/FR-4 in the same change as FR-5 (not as a follow-up) so there is no intermediate state where a caller silently regresses to the old default |
| Cache dictionary grows unbounded in theory if a caller ever passes attacker/user-controlled `ModelId` strings | Low | Not applicable today — both call sites source `ModelId` from static, operator-controlled config (`RagFeatureOptions.EmbeddingModel`), never from user input. Worth a one-line comment in code if a future caller might pass a dynamic value, but out of scope for this fix |

## Specification Amendments

1. **Test-seam extension needed for full FR-1 coverage.** The spec's acceptance criteria for FR-1 require verifying the override path issues a request "against the `text-embedding-3-small` embeddings endpoint path" and that different `ModelId` values "resolve independently." The existing test-seam constructor injects exactly one `EmbeddingClient`, which is sufficient for the default-model path but not for asserting *which model string* was actually sent for an override, without inspecting the outgoing request body. Recommend implementation verify the override path by asserting `model` in the JSON request body (the `StatefulHandler`/`BuildEmbeddingResponse` infrastructure in `OpenAiEmbeddingGeneratorTests.cs` already parses this body and can be extended to capture `.GetProperty("model")`), rather than requiring a second injected client per model. No interface or production-code change needed for this — purely a test-authoring note.

2. **Recommend (not required) extracting `RagFeatureOptions.ToEmbeddingOptions()`** (Decision 2) instead of duplicating identical `EmbeddingGenerationOptions` construction three times across `KnowledgeBaseDocIndexingStrategy`, `LeafletIndexingService`, and `GenerateLeafletHandler`. This mirrors the existing `ToExpansionConfig()` helper on the same class and keeps the three call sites' construction logic from drifting independently. This is a maintainability improvement, not a correctness requirement — safe to skip if the developer prefers matching the spec's literal "construct... and pass it" wording per call site.

3. **Explicit cache-seeding requirement for the test-seam constructor.** The spec says the injected-client constructor "must keep working unchanged for existing tests" but doesn't spell out *how* the per-model cache should be seeded from a single injected client. Implementation guidance above (seed under `_options.EmbeddingModel` key) should be treated as a concrete requirement, not left to implementer discretion — getting the key wrong silently breaks all 7 existing tests (see Risks table).

No amendments affect FR-2 through FR-5 or the NFRs; those are implementable exactly as specified and are consistent with what I found in the actual code (`AnthropicChatClient`'s pattern, `AnthropicOptions`'s neutral binding, current `appsettings.json` values, and the three confirmed call sites).

## Prerequisites

None. No migrations, no new infrastructure, no new config keys required for correct behavior (FR-5 explicitly allows omitting `OpenAI:EmbeddingModel`/`OpenAI:EmbeddingDimensions` and falling back to class defaults, which already match current production values per NFR-3). Implementation can start immediately once this review and the spec are accepted. Confirm before merging: run the NFR-3 comparison (`appsettings.json` and `appsettings.Production.json` `KnowledgeBase:*`/`Leaflet:*` embedding values vs. what the adapter resolves post-fix) as an explicit manual check, since it's called out as a required verification step in the spec but has no automated test guarding it.
