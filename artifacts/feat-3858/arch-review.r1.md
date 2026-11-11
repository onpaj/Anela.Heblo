# Architecture Review: Targeted FlexiBee ledger/lots latency work

## Skip Design: true
Backend-only change (adapter caching, logging, docs). No UI.

## Architectural Fit Assessment
Fits the existing layering: `ILedgerService` is a Domain interface implemented in `Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs` and registered as a singleton in `FlexiAdapterServiceCollectionExtensions` (line ~84). All consumers (CostPoolService, FinancialAnalysisService, SalesCostProvider, FlatManufactureCostProvider) go through it, so caching and single-flight belong inside the adapter with no contract change. Singleton lifetime makes an in-flight map safe. `FlexiLotsClient` is a thin pass-through; the lots timing log belongs in `CatalogMetaRefreshService.RefreshLotsData` (Application), which owns the refresh.

## Proposed Architecture

### Component Overview
```
Consumers --> ILedgerService (Domain)
                 |
           LedgerService (Adapters.Flexi, singleton)
             1. cache lookup (IMemoryCache, TTL by window age)
             2. in-flight map: key -> Lazy<Task<IList<LedgerItem>>>
             3. ILedgerClient.GetAsync (SDK, HTTP to ucetni-denik/query)
             4. stopwatch + structured log FlexiLedger.QueryCompleted
```

### Key Design Decisions

#### Decision 1: Single-flight via ConcurrentDictionary<string, Lazy<Task<...>>>
**Options considered:** SemaphoreSlim per key; global lock; `MemoryCache.GetOrCreateAsync` (not atomic across callers); Lazy task map.
**Chosen approach:** Lazy task map. The factory runs the SDK call with `CancellationToken.None` linked only to the HttpClient's own timeout, so one caller canceling does not poison the others; each caller awaits with `Task.WaitAsync(callerToken)` to honor its own cancellation. On completion (success or failure) the entry is removed from the map; only success populates the cache.
**Rationale:** no lock contention across unrelated keys, no deadlock risk, minimal code. Caveat: an abandoned in-flight request keeps running until the HttpClient timeout; acceptable and bounded.

#### Decision 2: TTL by window age, via options
**Chosen approach:** closed window (dateTo < first day of current month, using `TimeProvider`) uses 360 min, else 15 min. `FlexiLedgerOptions` bound from `FlexiLedger` section; defaults in code so no appsettings change is required.
**Rationale:** keeps open-period behavior identical; closed months are the bulk of the repeated margin/overview pulls.

#### Decision 3: Do not merge debit/credit pulls
`ILedgerClient.GetAsync` takes separate debit and credit prefix filters that FlexiBee ANDs; a union needs two queries or a client-side fetch of everything. Merging would increase row volume. Left as is; single-flight and caching reduce repeats.

#### Decision 4: Instrument before optimizing further
FR-1/FR-2 logging provides the date-span/row-count data the brief asks to profile. A follow-up (reading from `flexi_raw.ledger_entry`) is documented but not built.

## Implementation Guidance

### Directory / Module Structure
- Modify `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs`.
- New `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/FlexiLedgerOptions.cs` (class, not record).
- Register options and `TimeProvider.System` (if not yet registered) in `FlexiAdapterServiceCollectionExtensions.cs`.
- Modify `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` (RefreshLotsData timing log; add `ILogger` already present).
- Tests under `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/` (check existing folder for LedgerService tests and extend) and the Application tests project for the lots log.
- Docs: `docs/integrations/flexi-api.md`, `docs/processes/calc-margins.md`, `docs/processes/calc-financial-overview.md` (these own `LedgerService.cs`; CI process-docs gate requires them in the same PR), plus `docs/processes/flow-*`/catalog doc owning `CatalogMetaRefreshService.cs` (find via `scripts/process-docs/check.py`).

### Interfaces and Contracts
`ILedgerService` unchanged. `LedgerService` constructor gains `IOptions<FlexiLedgerOptions>` and `TimeProvider`; update the DI registration and existing test constructors. Cached value type stays `IList<LedgerItem>`.

### Data Flow
Request -> cache hit returns. Miss -> join or create in-flight task -> SDK call timed -> map -> cache.Set with TTL -> remove in-flight entry -> all joined callers receive the same list. Callers must not mutate the shared list (already the case today since the cached list is shared).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Orphaned in-flight request after all callers cancel | Low | Bounded by HttpClient timeout; logged |
| Stale closed-period data for 6 h after a late posting | Low | Configurable; documented |
| Shared list mutated by a caller | Medium | Verify consumers only read (grep); return as read-only wrapper if any mutate |
| Constructor change breaks existing tests/DI | Low | Update registration and tests in same task |
| Latency does not improve (FlexiBee-side) | Expected | FR-6 ceiling decision rule |

## Specification Amendments
None required. Confirm via CI process-docs check which docs own `CatalogMetaRefreshService.cs` and update them too.

## Prerequisites
None: no migrations, secrets or infrastructure. Post-release, an Application Insights query is needed to verify the call-count reduction.
