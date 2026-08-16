# Design: Honor per-call embedding model/dimensions overrides in OpenAiEmbeddingGenerator

## Component Design

### `OpenAiEmbeddingGenerator` (Adapters/Anela.Heblo.Adapters.OpenAI)

Responsibility: sole implementation of `IEmbeddingGenerator<string, Embedding<float>>` for the app; owns all HTTP/SDK interaction with the OpenAI Embeddings API and per-model client lifecycle.

- **Interface (unchanged):** `Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)`.
- **New internal responsibility:** resolve, per call, `model = options?.ModelId ?? _options.EmbeddingModel` and `dimensions = options?.Dimensions ?? _options.EmbeddingDimensions`; look up (or lazily create) a cached `EmbeddingClient` for `model` from a `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` (replaces the single `Lazy<EmbeddingClient>` field). Each dictionary entry's `Lazy<EmbeddingClient>` guarantees at most one construction per distinct model id even under concurrent first use.
- **Construction rule (unchanged):** no `EmbeddingClient` is built at DI-registration time; the first construction for any given model happens lazily on first call for that model, so `OpenAI:ApiKey` may remain unset in test/local environments that never call `GenerateAsync`.
- **Test seam (unchanged contract, new internal wiring):** `internal OpenAiEmbeddingGenerator(IOptions<OpenAiEmbeddingOptions>, ILogger<OpenAiEmbeddingGenerator>, EmbeddingClient? client)` — when `client` is supplied, it is seeded into the cache under the key `_options.EmbeddingModel` (the default model), so no-override calls resolve to the injected fake exactly as before. Calls with a different `ModelId` fall through to real construction.
- **Collaborators:** `OpenAiEmbeddingOptions` (fallback/default values), OpenAI SDK `EmbeddingClient` (one instance per distinct model, cached for process lifetime — no eviction, bounded by the small, config-driven set of model ids ever requested).

### `OpenAiAdapterServiceCollectionExtensions.AddOpenAiAdapter` (Adapters/Anela.Heblo.Adapters.OpenAI)

Responsibility: DI registration and config binding for the OpenAI adapter.

- **Change:** binds `OpenAiEmbeddingOptions.EmbeddingModel` / `.EmbeddingDimensions` from `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions` (neutral, adapter-scoped keys, parity with the adjacent `OpenAI:ApiKey` binding) instead of `KnowledgeBase:EmbeddingModel` / `KnowledgeBase:EmbeddingDimensions`. Absent keys fall back to `OpenAiEmbeddingOptions`'s hardcoded defaults (`"text-embedding-3-large"` / `1536`).
- **Contract:** this binding is now purely a last-resort fallback for any future `IEmbeddingGenerator` consumer that omits `options` — no current caller depends on it after FR-3/FR-4 land.

### Call sites (Application layer) — `KnowledgeBaseDocIndexingStrategy`, `LeafletIndexingService`, `GenerateLeafletHandler`

Responsibility: each owns translating its own already-bound feature options (`KnowledgeBaseOptions`, `LeafletOptions`) into an explicit `EmbeddingGenerationOptions { ModelId, Dimensions }` argument passed to every `GenerateAsync` call it makes. No I/O or model-resolution logic moves into these classes — they remain pure Application-layer callers; `OpenAiEmbeddingGenerator` remains the only component doing HTTP/SDK work, consistent with `docs/architecture/filesystem.md`'s I/O-placement rule.

- `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` → options sourced from `KnowledgeBaseOptions` (`KnowledgeBase:EmbeddingModel`/`EmbeddingDimensions`).
- `LeafletIndexingService.IndexAsync` → options sourced from `LeafletOptions` (`Leaflet:EmbeddingModel`/`EmbeddingDimensions`).
- `GenerateLeafletHandler.Handle` (query-time topic embedding) → same `LeafletOptions`-sourced options, ensuring the query vector and the indexed `LeafletChunks` vectors are produced with matching model/dimensionality.

Optional shared helper: `RagFeatureOptions.ToEmbeddingOptions()` returning `EmbeddingGenerationOptions { ModelId = EmbeddingModel, Dimensions = EmbeddingDimensions }`, mirroring the existing `ToExpansionConfig()` helper on the same class, to avoid duplicating identical three-line construction across the three call sites. Behavior-equivalent to inline construction; a maintainability choice, not a contract change.

### Unaffected component

`AnthropicChatClient.GetResponseAsync` already implements `options?.ModelId ?? _options.Model` and is the reference pattern this fix mirrors; no change to it or to chat-side model resolution.

## Data Schemas

### `EmbeddingGenerationOptions` (Microsoft.Extensions.AI.Abstractions) — per-call contract, unchanged shape, newly effective

```
EmbeddingGenerationOptions {
  ModelId?:    string   // resolved model id, e.g. "text-embedding-3-large" / "text-embedding-3-small"
  Dimensions?: int      // resolved vector width, e.g. 1536 / 3072
}
```

Resolution rule inside `OpenAiEmbeddingGenerator.GenerateAsync`:
```
model      = options?.ModelId    ?? _options.EmbeddingModel
dimensions = options?.Dimensions ?? _options.EmbeddingDimensions
```

### `OpenAiEmbeddingOptions` (adapter-owned fallback config)

```
OpenAiEmbeddingOptions {
  ApiKey:             string          // from OpenAI:ApiKey (unchanged)
  EmbeddingModel:      string = "text-embedding-3-large"   // now from OpenAI:EmbeddingModel (was KnowledgeBase:EmbeddingModel)
  EmbeddingDimensions: int    = 1536                        // now from OpenAI:EmbeddingDimensions (was KnowledgeBase:EmbeddingDimensions)
}
```

### Per-feature config (unchanged binding source, now actually reaching the API call)

```
KnowledgeBaseOptions (RagFeatureOptions) {
  EmbeddingModel:      string   // KnowledgeBase:EmbeddingModel      = "text-embedding-3-large"
  EmbeddingDimensions: int      // KnowledgeBase:EmbeddingDimensions = 1536
}

LeafletOptions (RagFeatureOptions) {
  EmbeddingModel:      string   // Leaflet:EmbeddingModel            = "text-embedding-3-large"
  EmbeddingDimensions: int      // Leaflet:EmbeddingDimensions       (unset → falls back to RagFeatureOptions default 1536)
}
```

### Internal cache structure

```
_clients: ConcurrentDictionary<string /* model id */, Lazy<EmbeddingClient>>
```
One entry per distinct resolved model id observed over the generator instance's lifetime; construction is deferred and happens at most once per model id even under concurrent first use (`Lazy<T>`, `ExecutionAndPublication` mode). No eviction — bounded by the small set of operator-configured model strings, never populated from user input.

### Outgoing OpenAI Embeddings API request (per-call, unchanged transport shape, now dynamic fields)

```
POST /v1/embeddings   (via EmbeddingClient keyed by resolved `model`)
{
  "input":      [ ...values ],
  "model":      "<resolved model, e.g. text-embedding-3-small>",   // implicit in EmbeddingClient(model, apiKey)
  "dimensions": <resolved dimensions, e.g. 3072>
}
```

### Persistence schema (unaffected)

No migrations. Existing pgvector columns remain fixed-width and untouched by this fix:
```
KnowledgeBaseChunks.Embedding  vector(1536)
LeafletChunks.Embedding        vector(1536)
```
`RagFeatureOptions.EmbeddingDimensions` per feature continues to be the source of truth for vector width; this fix only makes that value actually reach the embedding API call instead of silently relying on the adapter's shared default.
