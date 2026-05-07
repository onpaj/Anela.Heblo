Now I have enough context. Let me write the architecture review.

# Architecture Review: Dashboard Tile — Yesterday's DQT Status

## Architectural Fit Assessment

The feature aligns cleanly with the established dashboard tile pattern. Verified:

- **Tile pipeline**: `ITile` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs`) + `RegisterTile<T>()` (`TileRegistryExtensions.cs:16`) + `TileRegistry` (`TileRegistry.cs`) is the only mechanism. Tile id is derived purely from class name via `TileExtensions.GetTileId` (`TileExtensions.cs:5`): `Type.Name.ToLowerInvariant().Replace("tile", "")`. `DqtYesterdayStatusTile` → `dqtyesterdaystatus` — matches the spec.
- **Repository**: `IDqtRunRepository` (`backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs`) inherits `IRepository<DqtRun, Guid>` and already exposes `GetLatestByTestTypeAsync` and `GetPaginatedAsync`. Adding `GetLatestByTestTypeAndCoveredDateAsync` follows the existing intent-named-query convention.
- **Domain entity**: `DqtRun` already exposes every field the tile needs. No new schema.
- **TimeProvider**: registered as a singleton at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:110` (`services.AddSingleton(TimeProvider.System)`), and used from `LowStockAlertTile`, `CatalogRepository`, etc. No DI changes needed.
- **Frontend tile dispatcher**: `TileContent.tsx:24` switches on `tile.tileId` — adding a new case for `'dqtyesterdaystatus'` is mechanical.

Two small inconsistencies surfaced by the spec are real and worth pinning down:

1. The spec wants the new repo method to order by `CompletedAt desc` with `StartedAt` fallback. The existing `GetLatestByTestTypeAsync` orders by `StartedAt` desc only (`DqtRunRepository.cs:17`). Keeping the same ordering rule everywhere is simpler and produces identical results in practice (a later `StartedAt` always implies a later run).
2. The drill-down href divergence between `DataQualityStatusTile` (`/data-quality`) and the actual frontend route (`/automation/data-quality`) is a pre-existing bug. Spec correctly leaves the old tile alone and uses the correct route for the new one.

## Proposed Architecture

### Component Overview

```
                           ┌────────────────────────────────────────┐
                           │  GET /api/dashboard/data (existing)    │
                           └──────────────┬─────────────────────────┘
                                          │
                                          ▼
                           ┌────────────────────────────────────────┐
                           │  TileRegistry (existing)               │
                           │   resolves tileId → ITile via DI scope │
                           └──────────────┬─────────────────────────┘
                                          │
                                          ▼
   ┌───────────────────────────────────────────────────────────────┐
   │ DqtYesterdayStatusTile : ITile        (NEW)                   │
   │   ctor(IDqtRunRepository, TimeProvider, ILogger<...>)         │
   │   LoadDataAsync():                                            │
   │     yesterday = TimeProvider.GetLocalNow().Date.AddDays(-1)   │
   │     run = repo.GetLatestByTestTypeAndCoveredDateAsync(...)    │
   │     map run → status payload                                  │
   └──────────────┬────────────────────────────────────────────────┘
                  │
                  ▼
   ┌───────────────────────────────────────────────────────────────┐
   │ IDqtRunRepository           (extended interface)              │
   │   + GetLatestByTestTypeAndCoveredDateAsync(...)               │
   ├───────────────────────────────────────────────────────────────┤
   │ DqtRunRepository : BaseRepository<DqtRun, Guid>               │
   │   EF Core: WHERE TestType=@t AND DateFrom<=@d AND DateTo>=@d  │
   │   ORDER BY StartedAt DESC LIMIT 1                             │
   └───────────────────────────────────────────────────────────────┘

Frontend:
   ┌────────────────────────────────────────────────────────────┐
   │ TileContent.tsx (existing)  switch tile.tileId             │
   │   case 'dqtyesterdaystatus' → <DqtYesterdayStatusTile/>    │
   └─────────────────────┬──────────────────────────────────────┘
                         │
                         ▼
   ┌────────────────────────────────────────────────────────────┐
   │ DqtYesterdayStatusTile.tsx  (NEW)                          │
   │   four states: no_data | error | warning | success         │
   │   warning + runStatus==='Running' → Clock + "probíhá"      │
   │   click → navigate('/automation/data-quality')             │
   └────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: New repository method vs. reusing `GetPaginatedAsync`
**Options considered:**
- (a) Reuse `GetPaginatedAsync` with client-side filtering — wrong: pulls more rows than needed and conflates pagination with intent.
- (b) Add a date-overlap parameter to `GetPaginatedAsync` — wrong: bloats a paging API for one tile.
- (c) Add a dedicated `GetLatestByTestTypeAndCoveredDateAsync` method.

**Chosen approach:** (c). Mirrors the existing `GetLatestByTestTypeAsync` shape, single-purpose, single SQL round-trip.

**Rationale:** Repository methods in this codebase encode query intent, not generic CRUD over a paging API. The tile's needs ("most recent run that covers this date") is its own intent.

#### Decision 2: Ordering rule inside the new repo method
**Options considered:**
- (a) `OrderByDescending(CompletedAt) ThenByDescending(StartedAt)` (per spec).
- (b) `OrderByDescending(StartedAt)` — matches `GetLatestByTestTypeAsync` exactly.

**Chosen approach:** (b). Diverge from the spec on this point.

**Rationale:** A run with a later `StartedAt` is, by construction, the more recent run regardless of `CompletedAt`. Matching the existing repo method removes an asymmetry no caller benefits from. If the spec author had a concrete tie-breaking scenario in mind, it should be stated; otherwise simplify.

#### Decision 3: Tile metadata declaration
**Options considered:**
- (a) Add `GetTileId()` method on the tile (per spec).
- (b) Rely solely on `Type.Name.ToLowerInvariant().Replace("tile", "")`.

**Chosen approach:** (b). The `ITile` interface (verified in `ITile.cs`) has no `GetTileId()` member; the framework derives the id from the class name. The spec's mention of `GetTileId()` is incorrect — no such hook exists.

**Rationale:** Stay consistent with every other tile in the codebase. Naming the class `DqtYesterdayStatusTile` is the only required step.

#### Decision 4: Time source
**Chosen approach:** Constructor-inject `TimeProvider`; use `_timeProvider.GetLocalNow().Date.AddDays(-1)` to compute yesterday as a `DateOnly`.

**Rationale:** `TimeProvider` is already singleton-registered (`ServiceCollectionExtensions.cs:110`) and is the project's idiomatic test seam (`LowStockAlertTile`, `CatalogRepository`, `ExpeditionListService`). No `DateTimeOffset.Now` fallback — there is no scenario where the provider is missing.

#### Decision 5: Index strategy
**Options considered:**
- (a) Add a composite `(TestType, DateFrom, DateTo)` index migration.
- (b) Rely on the existing `IX_DqtRuns_TestType_StartedAt` index.

**Chosen approach:** (b). No new migration.

**Rationale:** `DqtRuns` cardinality is bounded by run frequency (≈1 scheduled + occasional manual per day per test type). The existing `(TestType, StartedAt)` index narrows scans to the test type; the date-range predicate runs over a tiny set. NFR-1 (300 ms p95) is comfortably met. Adding an unused index has cost (writes) for no measurable benefit. Migrations are manual on this project, so avoid one unless required. Revisit only if `DqtRuns` grows beyond a few thousand rows.

#### Decision 6: Drill-down href
**Chosen approach:** New tile uses `/automation/data-quality` (the actual frontend route). Leave `DataQualityStatusTile` unchanged.

**Rationale:** Project rule: surgical changes. The existing tile's wrong href is unrelated dead-style code; mention it in a follow-up issue but do not touch it here.

## Implementation Guidance

### Directory / Module Structure

**Backend (new):**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — sibling of `DataQualityStatusTile.cs`.

**Backend (modified):**
- `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs` — append one method.
- `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs` — append implementation.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — append `services.RegisterTile<DqtYesterdayStatusTile>();` after `DataQualityStatusTile`.

**Backend (tests):**
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — mirror `LowStockAlertTileTests.cs` pattern (Moq for repo + `Mock<TimeProvider>`).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DqtRunRepositoryTests.cs` — new file, EF Core in-memory or SQLite covering `GetLatestByTestTypeAndCoveredDateAsync` overlap semantics.

**Frontend (new):**
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`

**Frontend (modified):**
- `frontend/src/components/dashboard/tiles/TileContent.tsx` — add import and one switch case.
- `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` — add coverage for the new case (the file pattern is in place).

### Interfaces and Contracts

**Repository interface addition (`IDqtRunRepository.cs`):**

```csharp
Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
    DqtTestType testType,
    DateOnly coveredDate,
    CancellationToken cancellationToken = default);
```

**Repository implementation predicate (`DqtRunRepository.cs`):**

```csharp
return await DbSet
    .Where(r => r.TestType == testType
                && r.DateFrom <= coveredDate
                && r.DateTo >= coveredDate)
    .OrderByDescending(r => r.StartedAt)
    .FirstOrDefaultAsync(cancellationToken);
```

**Tile class skeleton (anonymous payloads matched to `DataQualityStatusTile` shape):**

```csharp
public class DqtYesterdayStatusTile : ITile
{
    private readonly IDqtRunRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DqtYesterdayStatusTile> _logger;

    public string Title => "DQT včera";
    public string Description => "Stav včerejšího DQT testu faktur";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();
    // ... LoadDataAsync per spec FR-3
}
```

The drill-down href is `/automation/data-quality` for **all four** payload shapes.

**Frontend props contract (`DqtYesterdayStatusTile.tsx`):**

```typescript
interface DqtYesterdayStatusTileProps {
  data: {
    status?: 'success' | 'warning' | 'error' | 'no_data';
    data?: {
      runId?: string;
      runStatus?: 'Completed' | 'Failed' | 'Running';
      dateFrom?: string;
      dateTo?: string;
      totalChecked?: number;
      totalMismatches?: number;
    } | null;
  };
}
```

### Data Flow

**Backend request flow (yesterday = 2026-05-05 example):**

```
GET /api/dashboard/data
  → DashboardController (existing)
    → TileRegistry.GetTileDataAsync("dqtyesterdaystatus")
      → resolve DqtYesterdayStatusTile from scoped DI
        → LoadDataAsync():
            yesterday = _timeProvider.GetLocalNow().Date.AddDays(-1)  // DateOnly 2026-05-05
            run = _repository.GetLatestByTestTypeAndCoveredDateAsync(
                      DqtTestType.IssuedInvoiceComparison, yesterday, ct)
            // SQL: WHERE TestType=0 AND DateFrom<='2026-05-05' AND DateTo>='2026-05-05'
            //      ORDER BY StartedAt DESC LIMIT 1
            map (run, run.Status, run.TotalMismatches) → status string
            return anonymous { status, data, drillDown }
```

**Status mapping (truth table from FR-3, locked in):**

| Run | Status | Mismatches | → response.status |
|---|---|---|---|
| null | — | — | `no_data` |
| present | `Failed` | — | `error` |
| present | `Running` | — | `warning` |
| present | `Completed` | > 0 | `warning` |
| present | `Completed` | 0 | `success` |
| (any throw) | — | — | `error` (logged, not propagated) |

**Frontend render flow:**

```
useDashboard → tile.tileId === 'dqtyesterdaystatus'
  → TileContent switch → DqtYesterdayStatusTile
    → branch on data.status
        no_data:  Clock icon + "Žádná data"
        error:    XCircle icon + error copy
        warning:  if runStatus==='Running' → Clock + "probíhá"
                  else → AlertTriangle + mismatchCount + "neshod"
        success:  ShieldCheck + "0 / vše OK"
    → onClick navigate('/automation/data-quality')
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Timezone misalignment: server local vs. DQT job's date range definition | Medium | DQT job runs in the same Europe/Prague server context. Confirm by reading `InvoiceDqtJob` once before merge. If the job uses UTC dates, the tile must use UTC too. |
| `(TestType, DateFrom, DateTo)` predicate not optimally indexed | Low | Existing `IX_DqtRuns_TestType_StartedAt` is sufficient at current cardinality. Document a follow-up if `DqtRuns` exceeds ~10k rows. |
| Multiple runs cover yesterday and ordering is non-deterministic | Low | `ORDER BY StartedAt DESC LIMIT 1` is total-order on a `DateTime` column. |
| Spec lists `GetTileId()` method that doesn't exist on `ITile` | Low | Class name alone determines the id (`TileExtensions.cs:5`). Section "Specification Amendments" below records this. |
| Test for "Running" branch is the only differentiator from existing tile and is easy to skip | Low | Test plan explicitly enumerates all 5 status outcomes (FR-3) and frontend distinguishes Running visually (FR-5). |
| Drill-down href inconsistency vs. old tile | Low (cosmetic) | Old tile's wrong href is out of scope; raise a follow-up issue. |

## Specification Amendments

1. **FR-1: Remove `GetTileId()` reference.** `ITile` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs`) has no `GetTileId` member. Tile id is derived from class name by `TileExtensions.GetTileId`. The spec's wording "(hardcoded constant matching the convention…)" is misleading — there is nothing to hardcode. Class naming alone produces `dqtyesterdaystatus`.

2. **FR-2: Drop the `CompletedAt`-then-`StartedAt` tie-breaker.** Use `ORDER BY StartedAt DESC` only, matching `GetLatestByTestTypeAsync`. Same observable behavior, simpler code, no parallel ordering rules in the same repo.

3. **NFR-1: No new index migration.** Strike the "If missing, add a migration." clause. Existing `IX_DqtRuns_TestType_StartedAt` is sufficient. Add a follow-up note in `memory/decisions/` if the table grows.

4. **NFR-3: Drop the `DateTimeOffset.Now` fallback.** `TimeProvider` is already DI-registered globally (verified in `ServiceCollectionExtensions.cs:110`). Constructor injection is mandatory; there is no fallback path.

5. **FR-3 / FR-5: Pin Czech copy values.** "vše OK" (success), "neshod" (warning, mismatches), "probíhá" (warning, running), "Žádná data" (no_data), "Chyba při načítání dat" (error). These match `DataQualityTile.tsx` for the shared phrases.

## Prerequisites

- None. No migrations, no new infra, no config flags.
- `TimeProvider` DI registration: already in place.
- Frontend `lucide-react` icons (`AlertTriangle`, `ShieldCheck`, `XCircle`, `Clock`): already imported by `DataQualityTile.tsx`.
- The DQT scheduler producing `DqtRun` rows (`InvoiceDqtJob`/`InvoiceDqtJobRunner`): unchanged dependency, no coordination needed.

Implementation can begin immediately. Order suggested: repository method + tests → tile class + tests → DI registration → frontend component + dispatcher case → frontend tests → manual smoke against staging.