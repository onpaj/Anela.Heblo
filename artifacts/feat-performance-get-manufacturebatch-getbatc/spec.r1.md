# Specification: Performance fix for GET ManufactureBatch/GetBatchTemplate

## Summary
The `GET /api/manufacture-batch/template/{productCode}` endpoint is averaging **10580ms** in production, exceeding the 10000ms App Insights threshold. The ingredient-level N+1 was already fixed (see PR #690 / `CalculatedBatchSizeHandler.cs:51-56`), so the remaining cost lives in the FlexiBee BoM/stock fetch chain inside `FlexiManufactureTemplateService.GetManufactureTemplateAsync`. This spec defines the work to bring p95 well under threshold by parallelising the FlexiBee calls and adding short-lived caching, with a measurable target and a rollback path.

## Background
The endpoint backs the "Show batch template" entry point in the manufacture batch UI and is invoked once per product the user opens. The MediatR handler is `CalculatedBatchSizeHandler` (reused from the `calculate-by-size` POST flow with `DesiredBatchSize == null`).

Tracing the call path (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs`):

1. `IManufactureClient.GetManufactureTemplateAsync(productCode)` →
   `FlexiManufactureTemplateService.GetManufactureTemplateAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`):
   - 1× FlexiBee call: `_bomClient.GetAsync(productCode)` to fetch the Bill of Materials.
   - **3× sequential FlexiBee calls** to `_stockClient.StockToDateAsync(...)` — one each for the Material, SemiProducts and Products warehouses (lines 65–69). These are not awaited in parallel and each one downloads the **full warehouse stock snapshot**, only to be used to read a single `HasLots` flag per ingredient.
2. `ICatalogRepository.GetByIdAsync(productCode)` — in-memory catalog lookup (cheap).
3. `ICatalogRepository.FindAsync(x => ingredientCodes.Contains(...))` — already batch-loaded, in-memory (cheap, PR #690).

The four FlexiBee HTTP calls (1 BoM + 3 stock snapshots) executed sequentially are the dominant contributors. Each typical FlexiBee response in this codebase is 1–3 seconds; serialised, that aligns with the observed ~10.5s. The brief's "N+1 per ingredient" hypothesis no longer applies — that was fixed in #690 — so the remediation must target the FlexiBee chain.

Related fix: PR #690 fixed the ingredient catalog N+1 in the same handler. This spec applies the same "batch, parallelise, cache" pattern one layer down.

## Functional Requirements

### FR-1: Preserve current response contract
The endpoint must return the exact same `CalculatedBatchSizeResponse` payload (fields, ordering, value semantics) as today for the same inputs.
**Acceptance criteria:**
- For any product code, the response body before and after the change is structurally equivalent (deep-equal modulo non-deterministic timestamps, none of which are in this payload).
- Existing unit and integration tests for `CalculatedBatchSizeHandler` and the controller pass without modification of expected values.
- The error envelope behaviour (`ErrorCodes.ManufactureTemplateNotFound`, `ProductNotFound`, `InvalidBatchSize`, `Exception`) is preserved.

### FR-2: Parallelise the three FlexiBee stock-snapshot calls
The three `StockToDateAsync` invocations inside `FlexiManufactureTemplateService.GetManufactureTemplateAsync` (Material, SemiProducts, Products warehouses) must be issued concurrently rather than sequentially.
**Acceptance criteria:**
- The three calls are dispatched with `Task.WhenAll` (or equivalent) so wall-clock time approaches max(t1,t2,t3), not sum(t1,t2,t3).
- The combined `HasLots` map is materialised once after all three complete; subsequent ingredient projection consumes the merged dictionary.
- Cancellation propagates: cancelling the request cancels all in-flight stock calls (the same `CancellationToken` is passed to each).
- A unit test using a fake `IErpStockClient` records concurrent dispatch (e.g. via a counter / `Task.Delay` assertion) and proves both call ordering and `HasLots` correctness.

### FR-3: Add a short-lived in-memory cache for the manufacture template
Manufacture templates change rarely (BoM edits happen via the ERP, not the app). Calls within a short window must reuse a cached result.
**Acceptance criteria:**
- Cache key: `manufacture-template:{productCode}`.
- TTL: **5 minutes** (sliding expiration disabled; absolute expiration only).
- Cache stores the full `ManufactureTemplate` returned by `FlexiManufactureTemplateService`. Cache misses on null (template-not-found) responses must **not** be cached (avoid pinning a transient FlexiBee outage as "not found").
- On a hit, no FlexiBee HTTP calls are issued.
- Cache lives in the FlexiBee adapter layer (so the same template benefits every handler that requests it, not just `GetBatchTemplate`).
- A cache hit returns a defensive **clone** of the template (so handler-level mutation of `template.BatchSize` on line 38 of `CalculatedBatchSizeHandler` does not corrupt the cached copy).
- Cache instrumented with `ILogger` (Debug level, hit/miss/store) and an App Insights custom dimension `manufacture_template_cache=hit|miss`.

### FR-4: Remove unnecessary stock-snapshot work
The current code downloads three full warehouse stock snapshots only to read a single `HasLots` boolean per ingredient. This must be narrowed.
**Acceptance criteria:**
- Either: (a) the stock client exposes a method that returns only `(ProductCode, HasLots)` pairs (preferred if FlexiBee supports a narrower projection), or (b) the existing full-snapshot call is retained but the result is projected to a `Dictionary<string, bool>` immediately and the larger DTOs are released.
- If (a) is not feasible against FlexiBee, document the limitation in `docs/integrations/shoptet-api.md`-equivalent FlexiBee notes (or inline code comment) and pick (b).
- Memory allocation for the three stock snapshots drops to the size of the `HasLots` dictionary after parsing.

### FR-5: Add performance instrumentation
The fix must be observable in App Insights so the regression cannot recur silently.
**Acceptance criteria:**
- The handler emits a custom event `manufacture_template_fetched` with dimensions: `product_code`, `cache_hit` (bool), `bom_duration_ms`, `stock_duration_ms`, `total_duration_ms`, `ingredient_count`.
- App Insights dashboard query in the PR description showing baseline vs post-deploy p50/p95 for the endpoint.

## Non-Functional Requirements

### NFR-1: Performance
- **p50** response time for `GET /api/manufacture-batch/template/{productCode}` drops below **2000ms** for cache misses and below **200ms** for cache hits.
- **p95** drops below **5000ms** (well under the 10000ms threshold).
- Measured on the same warehouse data volume currently in production, over a rolling 24h window after deploy.

### NFR-2: Security
- No new external endpoints; no new auth surface. The endpoint remains behind `[Authorize]` (`ManufactureBatchController.cs:10`).
- Cache key is product code only — product codes are not PII, but the cache must still be scoped per-process (no cross-tenant concern in this single-tenant app, but stated for completeness).
- No secrets or tokens are logged in the new instrumentation; `product_code` is the only identifier emitted.

### NFR-3: Reliability
- FlexiBee outages must not be cached as "not found". A `null` template result skips the cache write.
- A FlexiBee 501 (`NotImplemented`) is already re-thrown with structured logging (`FlexiManufactureTemplateService.cs:38-44`) — preserve this; cache writes must occur only on successful, non-null results.
- The three parallel stock calls share one `CancellationToken`; if one fails, the others are cancelled and the exception propagates with structured context.

### NFR-4: Backward compatibility
- No changes to the public HTTP contract (route, request shape, response shape, status codes, error envelope).
- No frontend changes required. The TypeScript client (`frontend/src/api/generated/api-client.ts`) does not need regeneration since neither request nor response shapes change.

### NFR-5: Testability
- All new code paths covered by xUnit tests with NSubstitute fakes for `IBoMClient`, `IErpStockClient`, and the cache.
- Test coverage for `FlexiManufactureTemplateService` and the cache wrapper >= 80% line coverage.

## Data Model

No persistence changes. The in-process types involved are:

- `ManufactureTemplate` (domain, `backend/src/Anela.Heblo.Domain/Features/Manufacture/`) — the cached payload.
  - `TemplateId`, `ProductCode`, `ProductName`, `Amount`, `OriginalAmount`, `BatchSize`, `ManufactureType`, `Ingredients: List<Ingredient>`.
- `Ingredient` — `TemplateId`, `ProductCode`, `ProductName`, `Amount`, `ProductType`, `HasLots`, `HasExpiration`.
- `ErpStock` (FlexiBee adapter) — read transiently, projected to `(ProductCode, HasLots)` and discarded.

The cache layer is in-memory only (`Microsoft.Extensions.Caching.Memory.IMemoryCache`). No distributed cache is introduced; the app runs as a single-instance Azure Web App for Containers (confirmed in `docs/architecture/infrastructure.md`).

## API / Interface Design

### Public HTTP API
**Unchanged.** `GET /api/manufacture-batch/template/{productCode}` continues to return `CalculatedBatchSizeResponse`.

### Internal interfaces

New: `IManufactureTemplateCache` in the FlexiBee adapter layer.

```csharp
public interface IManufactureTemplateCache
{
    Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken);
}
```

Implementation: `ManufactureTemplateCache` backed by `IMemoryCache`, with 5-minute absolute TTL, defensive deep-clone on read, no caching of nulls, structured logging.

`FlexiManufactureTemplateService` is rewritten to:
1. Call `IManufactureTemplateCache.GetOrFetchAsync(productCode, fetchInner, ct)`.
2. `fetchInner` performs the BoM call and the **parallel** three stock calls via `Task.WhenAll`, builds the `HasLots` dictionary, and projects ingredients exactly as today.

### Dependency injection registration
- `services.AddMemoryCache()` is already registered (used by other caches in the app — see `MaterialCostCache.cs`). Verify and reuse.
- Register `IManufactureTemplateCache` as **singleton** (stateless beyond the underlying `IMemoryCache`).

### Frontend
No changes. `useManufactureBatch.ts` already calls the endpoint; consumers will simply see faster responses.

## Dependencies

- `Microsoft.Extensions.Caching.Memory` — already in the dependency graph.
- `IBoMClient`, `IErpStockClient` — existing FlexiBee SDK clients. No version bump expected.
- App Insights / `Microsoft.ApplicationInsights` — already wired for custom events.
- PR #690 — the prior ingredient batch-loading fix in the same handler; this spec layers on top of it without revisiting it.

## Out of Scope

- **Persistent (distributed) cache.** Not needed for a single-instance deployment; revisit if/when horizontally scaled.
- **FlexiBee response caching at the HTTP client layer.** A layer above (the manufacture-template cache) is sufficient and more targeted.
- **Refactoring the broader `CalculatedBatchSizeHandler` flow** (calculate-by-size, calculate-by-ingredient). Those endpoints reuse `GetManufactureTemplateAsync` and will benefit automatically, but no behaviour changes are made to those handlers.
- **BoM-level invalidation hooks.** No webhook exists from FlexiBee for BoM edits; we accept the 5-minute staleness window. A manual cache-bust admin endpoint is out of scope for this fix.
- **Frontend prefetch / SWR tuning.** No client-side changes.
- **Stock-client API redesign.** Whether FlexiBee exposes a narrower `HasLots`-only projection is a known unknown (Open Question 1); if not, we project locally — but we do not redesign the FlexiBee SDK.

## Open Questions

None.

## Status: COMPLETE
