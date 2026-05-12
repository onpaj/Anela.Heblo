Now I have enough grounding to write the architecture review.

# Architecture Review: Performance fix for GET ManufactureBatch/GetBatchTemplate

## Architectural Fit Assessment

The proposal fits cleanly into the existing Clean Architecture + Vertical Slice layout:

- The hot path lives entirely inside the **FlexiBee adapter** (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`). All proposed changes — parallelisation, narrowing of stock projection, and caching — belong to that same adapter and require no Domain or Application changes.
- The MediatR handler (`CalculatedBatchSizeHandler`) is downstream of `IManufactureClient.GetManufactureTemplateAsync`. Because it **mutates** `template.BatchSize` on line 38 of the handler, any cache-hit path MUST return an isolated copy. This is the single hardest correctness invariant in the design.
- The pattern of "in-memory cache wrapping a FlexiBee call" already exists in this adapter (`FlexiProductPriceErpClient` uses `IMemoryCache` with `CacheKey` + `ObjectDisposedException` defence). Reuse that idiom for visual consistency.
- `IMemoryCache` is already registered in `FlexiAdapterServiceCollectionExtensions.cs:48`; no new infrastructure is needed.
- Concurrent FlexiBee dispatch is already used in `FlexiStockClient.ListAsync` (lines 35–39), confirming `Task.WhenAll` against `IStockToDateClient` is safe under the FlexiBee SDK's HTTP client configuration.
- Telemetry has an established abstraction: `ITelemetryService.TrackBusinessEvent(...)` in `Anela.Heblo.Xcc/Telemetry`. The spec's "App Insights custom event" requirement maps directly onto this, removing the temptation to introduce a second telemetry path.

The one piece of friction: `IFlexiManufactureTemplateService` is declared `internal` to the FlexiBee adapter assembly. The cache wrapper must live in the same assembly to keep that boundary; the cache interface itself should be `internal`.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Application: CalculatedBatchSizeHandler                                  │
│   (unchanged; calls IManufactureClient.GetManufactureTemplateAsync)      │
└──────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ Adapter:    FlexiManufactureClient.GetManufactureTemplateAsync           │
│             (unchanged; delegates to IFlexiManufactureTemplateService)   │
└──────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ Adapter:    FlexiManufactureTemplateService (REWRITTEN)                  │
│             ┌──────────────────────────────────────────────────┐         │
│             │ IManufactureTemplateCache.GetOrFetchAsync(...)   │         │
│             │   ├── HIT → return DeepClone(cached) ────────────┼──→ done │
│             │   └── MISS → invoke fetcher delegate ↓           │         │
│             └──────────────────────────────────────────────────┘         │
│                                  │                                       │
│                                  ▼                                       │
│      ┌─────────────────────────────────────────────────────┐             │
│      │ Inner fetch (Task.WhenAll, single CT):              │             │
│      │  • _bomClient.GetAsync(productCode)        (1 call) │             │
│      │  • Three StockToDateAsync calls in parallel         │             │
│      │       └─→ projected to Dictionary<string,bool> with │             │
│      │           HasLots; larger DTOs released immediately │             │
│      └─────────────────────────────────────────────────────┘             │
│                                  │                                       │
│                                  ▼                                       │
│      Build ManufactureTemplate; on non-null success → cache store        │
│      Emit ITelemetryService.TrackBusinessEvent("manufacture_template_    │
│      fetched", ...)                                                      │
└──────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
                          IMemoryCache (singleton)
```

### Key Design Decisions

#### Decision 1: Cache layer placement
**Options considered:**
- (A) Cache inside `CalculatedBatchSizeHandler` in the Application layer.
- (B) Cache inside `FlexiManufactureClient.GetManufactureTemplateAsync` in the adapter.
- (C) Cache inside `FlexiManufactureTemplateService` (the lowest internal service).

**Chosen approach:** (C). The cache is a private collaborator of `FlexiManufactureTemplateService`. The cache interface (`IManufactureTemplateCache`) is `internal` to `Anela.Heblo.Adapters.Flexi`.

**Rationale:** The cache must be transparent to every caller of `IManufactureClient.GetManufactureTemplateAsync` (used by `CalculatedBatchSizeHandler`, `CalculateBatchByIngredientHandler`, `CalculateBatchPlanHandler`, `GetProductCompositionHandler`, `ResidueDistributionCalculator`, and `FlexiIngredientRequirementAggregator` — five additional call sites confirmed via grep). Placing the cache in the Application layer would either duplicate the logic or leak FlexiBee semantics upward. Wrapping `FlexiManufactureClient` would also work but adds a layer; embedding into the existing `FlexiManufactureTemplateService` matches the `FlexiProductPriceErpClient` precedent.

#### Decision 2: Cache key, TTL, and lifetime
**Options considered:** sliding vs. absolute expiration; 1/5/15 min TTL; singleton vs. scoped cache wrapper.
**Chosen approach:** Absolute expiration, 5 min TTL, cache wrapper registered **singleton** (the spec already says this — but note that `FlexiManufactureTemplateService` itself remains **scoped**; the singleton wrapper is injected into the scoped service, which is safe).
**Rationale:** Sliding expiration could pin a stale BoM indefinitely under sustained traffic. Five minutes matches the spec's staleness budget without re-using the underlying `IMemoryCache` lifetime semantics. The wrapper is stateless beyond the underlying singleton `IMemoryCache`, so singleton lifetime is correct.

#### Decision 3: Defensive cloning strategy
**Options considered:**
- (A) Make `ManufactureTemplate` / `Ingredient` immutable (records or `init`-only setters).
- (B) Return a deep clone on every cache hit.
- (C) Document handler must not mutate template; rely on convention.

**Chosen approach:** (B). Implement a `Clone()` method on `ManufactureTemplate` that produces a new instance with a new `List<Ingredient>` containing new `Ingredient` instances.

**Rationale:** (A) is the cleanest but ripples through 6+ consumers and risks DTO/record gotchas called out in `CLAUDE.md` — out of scope for a performance fix. (C) is fragile: `CalculatedBatchSizeHandler.cs:38` already mutates the cached object today, and other handlers may too. (B) is local, testable, and preserves all current behaviour.

**Where the clone lives:** A `Clone()` instance method on `ManufactureTemplate` (Domain layer) is the natural home — but this would be a Domain-layer change driven by an adapter-layer concern. Acceptable alternative: a private static `Clone` helper inside `ManufactureTemplateCache` that performs the projection. **Prefer the helper inside the cache** to keep Domain unaware of caching. The helper is 10–15 LOC and trivial to unit-test.

#### Decision 4: Negative caching policy
**Chosen approach:** Do not cache `null` results (template-not-found). Do not cache exceptions.
**Rationale:** A FlexiBee 5xx surfaces as an exception (already handled at `FlexiManufactureTemplateService.cs:38-44`), and a "BoM has no Level-1 row" returns `null` (line 49). Caching either case would pin a transient outage as "not found" for 5 minutes. The trade-off is that lookups for genuinely non-existent products are not cached, but that traffic is expected to be negligible.

#### Decision 5: Narrowing the stock projection (FR-4)
**Options considered:**
- (a) Add a new method on `IErpStockClient` returning only `(ProductCode, HasLots)` projections.
- (b) Keep `StockToDateAsync` calls but project to `Dictionary<string, bool>` immediately and let the source DTOs go out of scope.

**Chosen approach:** (b). The `IErpStockClient` interface is consumed by 14+ call sites and adding a method whose only difference is projection is premature abstraction. The performance win from parallelisation + cache dominates; the memory-pressure win from (a) is a secondary objective.

**Rationale:** The FlexiBee SDK returns the full snapshot regardless — the narrowing happens client-side either way. Projecting locally in `FlexiManufactureTemplateService` is one extra LINQ statement.

#### Decision 6: Telemetry path
**Chosen approach:** Use the existing `ITelemetryService.TrackBusinessEvent("manufacture_template_fetched", properties, metrics)`.
**Rationale:** The codebase already has a single telemetry abstraction with a `NoOpTelemetryService` fallback for non-prod environments. Adding a parallel `TelemetryClient` usage would split the abstraction.

## Implementation Guidance

### Directory / Module Structure

All new code lives in the FlexiBee adapter:

```
backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/
├── FlexiManufactureTemplateService.cs           # MODIFIED (parallelise + delegate to cache)
├── IFlexiManufactureTemplateService.cs          # UNCHANGED
├── IManufactureTemplateCache.cs                 # NEW (internal)
├── ManufactureTemplateCache.cs                  # NEW (internal)
└── ManufactureTemplateCloner.cs                 # NEW (internal static helper; or inlined)

backend/src/Adapters/Anela.Heblo.Adapters.Flexi/
└── FlexiAdapterServiceCollectionExtensions.cs   # MODIFIED (one new singleton registration)

backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/
├── FlexiManufactureTemplateServiceTests.cs      # EXTENDED (parallel-dispatch + cache integration)
├── ManufactureTemplateCacheTests.cs             # NEW (cache hit/miss/null/clone semantics)
└── ManufactureTemplateClonerTests.cs            # NEW (clone deep-equal but reference-distinct)
```

No Domain, Application, API, frontend, or persistence changes.

### Interfaces and Contracts

```csharp
// IManufactureTemplateCache.cs  (internal)
internal interface IManufactureTemplateCache
{
    Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken);
}
```

Contract obligations:
- Cache key format: `"manufacture-template:{productCode}"` (string interpolation only — product codes do not collide).
- TTL: `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)`. No sliding expiration.
- Null fetch results MUST skip cache write.
- Cache hit MUST return a deep clone (new `ManufactureTemplate`, new `List<Ingredient>`, new `Ingredient` instances).
- Concurrent misses for the same key MAY both call the fetcher; do **not** add request coalescing in v1 (`IMemoryCache` is not lock-free at the value level, but the spec does not require single-flight). Document this explicitly.
- Logger emits `Debug` for hit/miss/store; no `product_code` in `Info`/`Warning` level logs (avoid log spam — App Insights event handles per-call detail).

Existing interface `IFlexiManufactureTemplateService` and public `IManufactureClient` contracts remain **unchanged**.

### Data Flow

**Cache miss (cold path):**
1. `CalculatedBatchSizeHandler.Handle` → `IManufactureClient.GetManufactureTemplateAsync(productCode, ct)`.
2. `FlexiManufactureClient` → `IFlexiManufactureTemplateService.GetManufactureTemplateAsync(productCode, ct)`.
3. `FlexiManufactureTemplateService` → `IManufactureTemplateCache.GetOrFetchAsync(productCode, fetchInner, ct)`.
4. Cache miss → invoke `fetchInner(ct)`:
   - `_bomClient.GetAsync(productCode, ct)` → `BoMItemFlexiDto[]`.
   - If header (Level == 1) missing → return `null` (skip cache).
   - `Task.WhenAll(stockMaterial, stockSemi, stockProducts)` with the same `ct`.
   - Project each result to `IEnumerable<KeyValuePair<string, bool>>` and merge into one `Dictionary<string, bool>` (last-wins on duplicate keys, consistent with current `.FirstOrDefault`).
   - Build `ManufactureTemplate` and `List<Ingredient>` exactly as today (preserving `ProductType`, `ManufactureType`, `HasExpiration=false`).
   - Return the template.
5. Cache stores the non-null template with 5-min absolute TTL.
6. Cache returns a deep clone to caller (so the freshly-built template is also cloned — yes, even on miss — so a cache miss and a hit are indistinguishable in observable behaviour by the caller).
7. `FlexiManufactureTemplateService` emits `ITelemetryService.TrackBusinessEvent("manufacture_template_fetched", { product_code, cache_hit=false, ingredient_count }, { bom_duration_ms, stock_duration_ms, total_duration_ms })`.
8. `CalculatedBatchSizeHandler` mutates `template.BatchSize` — this is the clone, so the cache is unaffected.

**Cache hit (warm path):**
1–3. Same as above.
4. Cache hit → clone returned; **no FlexiBee calls dispatched**.
7. Telemetry emitted with `cache_hit=true`, `bom_duration_ms=0`, `stock_duration_ms=0`, `total_duration_ms ≈ <few ms>`.

**Cancellation:**
- Caller's `CancellationToken` is threaded through `GetOrFetchAsync`, into the fetcher delegate, into `_bomClient.GetAsync` and each `_stockClient.StockToDateAsync` call.
- A cancellation during the parallel stock phase cancels all three siblings (they share the token via `Task.WhenAll`). Any `OperationCanceledException` propagates up; the cache is NOT populated.

**FlexiBee 501 (`NotImplemented`):**
- Existing `try/catch` on the BoM call at `FlexiManufactureTemplateService.cs:38-44` is preserved. The cache is not populated; the exception propagates and is logged with structured properties. Apply the same defensive `try/catch` semantics around `Task.WhenAll` of the three stock calls (or rely on the existing per-call exception handling already in `FlexiStockClient.cs:59-82`, which is fine).

### Telemetry Schema

`ITelemetryService.TrackBusinessEvent("manufacture_template_fetched", properties, metrics)`:

| Field | Type | Source | Notes |
|---|---|---|---|
| `product_code` | property | input | Not PII; safe to emit |
| `cache_hit` | property | computed | `"true"`/`"false"` |
| `ingredient_count` | property | `template.Ingredients.Count` | `"0"` if template null |
| `bom_duration_ms` | metric | `Stopwatch` around BoM call | `0` on cache hit |
| `stock_duration_ms` | metric | `Stopwatch` around parallel block | `0` on cache hit |
| `total_duration_ms` | metric | `Stopwatch` around full method | always populated |

`Stopwatch` use is fine — already idiomatic in `FlexiProductPriceErpClient`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Caller mutates cached template via `template.BatchSize` — corruption of cached state | **HIGH** | Deep clone on every cache return (Decision 3). Add a unit test that asserts mutating a returned template does not affect a subsequent retrieval. |
| Three parallel calls hammer FlexiBee, triggering rate limits or saturating its connection pool | MEDIUM | FlexiBee SDK already uses the central `IHttpClientFactory` (`FlexiStockClient` constructor). The `ListAsync` flow at `FlexiStockClient.cs:35-39` already issues these three calls in parallel in production, so the load pattern is not new. **No change needed**; document the precedent in the PR description. |
| `IMemoryCache` allows two concurrent misses for the same product → both perform the FlexiBee work | LOW | Acceptable for v1. Adding single-flight (e.g., `LazyCache` / `SemaphoreSlim` per key) is out of scope. Document explicitly in code comment. |
| Telemetry event emission failure cascades into handler failure | MEDIUM | Telemetry calls wrapped in `try/catch`, swallowed with `Debug`-level log (already the pattern in `NoOpTelemetryService`). Or rely on `TelemetryClient`'s own safety. Verify: `ITelemetryService.TrackBusinessEvent` is best-effort and does not throw. |
| Cache memory growth unbounded if product code space explodes | LOW | `IMemoryCache` has process-default `SizeLimit = null` (unbounded), but absolute TTL of 5 min naturally bounds the working set. With ~500 distinct products and ~10 KB per template, worst-case ~5 MB. Acceptable for a single-instance Azure Web App with default memory. |
| Defensive clone has subtle bug (e.g., shares the `List<Ingredient>` reference) | MEDIUM | Dedicated `ManufactureTemplateCloner` test that deep-equals the clone and reference-equals each nested collection to a *different* instance. |
| Existing test project (`Anela.Heblo.Adapters.Flexi.Tests`) uses **Moq**, not NSubstitute as the spec says | LOW | Implementation note: follow the existing convention in the test assembly. See **Specification Amendments** below. |
| `IBoMClient` and `IStockToDateClient` come from an external SDK (`Rem.FlexiBeeSDK`); mocking them in unit tests may have surface limitations | LOW | The existing test (`FlexiManufactureTemplateServiceTests.cs`) already mocks `IBoMClient` and `IErpStockClient` — proven pattern. |

## Specification Amendments

1. **NSubstitute → Moq.** Spec NFR-5 mandates NSubstitute. The existing test assembly `backend/test/Anela.Heblo.Adapters.Flexi.Tests` uses **Moq** (see `FlexiManufactureTemplateServiceTests.cs:7-23`). Use Moq to stay consistent with the surrounding test project; do not introduce a second mocking library.

2. **Telemetry abstraction.** Spec FR-5 says "App Insights custom event." The codebase has `ITelemetryService` (`Anela.Heblo.Xcc.Telemetry`) as the canonical wrapper. Route the event through `ITelemetryService.TrackBusinessEvent`. The `NoOpTelemetryService` fallback handles non-prod environments transparently. Inject `ITelemetryService` into `FlexiManufactureTemplateService`.

3. **Cache wrapper visibility.** Mark both `IManufactureTemplateCache` and `ManufactureTemplateCache` as `internal`, consistent with `IFlexiManufactureTemplateService` and the rest of the `Manufacture/Internal` folder.

4. **Cloning location.** The clone helper lives in the FlexiBee adapter, **not** as a method on `ManufactureTemplate` in the Domain. Adding `Clone()` to the Domain entity would couple Domain to an adapter-layer caching concern.

5. **Beneficiary call sites.** The spec states the cache "lives in the FlexiBee adapter layer (so the same template benefits every handler that requests it)." Five additional callers confirmed: `CalculateBatchByIngredientHandler`, `CalculateBatchPlanHandler`, `GetProductCompositionHandler`, `ResidueDistributionCalculator`, `FlexiIngredientRequirementAggregator`. Each will now also benefit from caching and parallelisation. **Verify each of these handlers does not depend on cache-miss semantics** (e.g., does not assume each call freshly hits FlexiBee). The risk is minor (templates are read-only data) but should be explicitly noted in the PR description.

6. **Single-flight semantics.** Spec is silent on concurrent misses for the same key. Document explicitly: v1 does not deduplicate concurrent misses; this is acceptable because the cost is at most O(N) redundant FlexiBee calls during cache stampede, and the BoM data is read-only.

7. **`IMemoryCache` already registered.** Spec FR-3 says "verify and reuse." Confirmed: `FlexiAdapterServiceCollectionExtensions.cs:48` already calls `services.AddMemoryCache()`. No new registration needed for the underlying cache.

## Prerequisites

None. All required infrastructure exists:

- `IMemoryCache` already registered in the FlexiBee adapter (`FlexiAdapterServiceCollectionExtensions.cs:48`).
- `ITelemetryService` already registered and used elsewhere (see `ServiceCollectionExtensions.cs`, `Hangfire` jobs).
- FlexiBee SDK clients (`IBoMClient`, `IErpStockClient`/`IStockToDateClient`) unchanged.
- No new NuGet packages, no migrations, no config keys, no infrastructure changes.

The only one-line registration needed in `FlexiAdapterServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton<IManufactureTemplateCache, ManufactureTemplateCache>();
```

Implementation can begin immediately.