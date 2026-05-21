I have enough context to write the architecture review. The spec is sound; key findings: schema rename to plural form already occurred, `Allocations` configuration shows the exact pattern needed for `_logs` (Option 2 in FR-4), and the test project is `Anela.Heblo.Tests` (not `Anela.Heblo.Application.Tests` as referenced in the spec).

# Architecture Review: Fix `ForecastedDays` Always Null in Packing Materials List

## Skip Design: true

This is a backend-only correctness fix. No UI components change, the `PackingMaterialDto` shape is preserved, and the OpenAPI contract is unchanged.

## Architectural Fit Assessment

The proposed work aligns cleanly with the established backend conventions in this repository:

- **Vertical slice + Clean Architecture.** `PackingMaterials` already follows `Domain/Features/PackingMaterials` (entities + repository interface) → `Application/Features/PackingMaterials` (handlers, MediatR contracts, services, module) → `Persistence/PackingMaterials` (EF configurations + repository implementation). All changes the spec proposes live inside that slice.
- **MediatR handlers depend on the domain repository interface** — `GetPackingMaterialsListHandler` already only sees `IPackingMaterialRepository`. The fix stays at that boundary.
- **`BaseRepository<TEntity, TKey>` is the inheritance root** for feature repositories (verified in `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`) and exposes `GetAllAsync`, `GetByIdAsync`, `SaveChangesAsync`. The spec correctly proposes deleting `GetAllWithLogsAsync` / `GetByIdWithLogsAsync` in favor of the base methods.
- **Aggregate pattern is the established style.** `PackingMaterial` owns `_logs` and `_allocations`, and `PackingMaterialAllocationConfiguration` already shows the supported pattern for child collections: `builder.HasOne<PackingMaterial>().WithMany(pm => pm.Allocations).HasForeignKey(...)`. The same pattern is the missing piece for `_logs`.

Two integration points need attention beyond what the spec calls out:

1. **`ConsumptionCalculationService.ProcessDailyConsumptionAsync` also relies on `material.UpdateQuantity(...)` followed by `SaveChangesAsync`** to persist log rows (lines 62 and 73 of `ConsumptionCalculationService.cs`). The same defect (no EF relationship → `_logs.Add(log)` is silent) affects daily processing, not just the update-quantity handler. FR-4 must cover both call sites.
2. **The schema was renamed to plural form** (`PackingMaterialLogs`, `PackingMaterials`) in migration `20260424144059_RenameSingularTablesToPluralForm`. Spec's "Data Model" paragraph hedges with "assumed" — confirm the plural name in the new EF query and tests.

## Proposed Architecture

### Component Overview

```
HTTP GET /api/packing-materials
        │
        ▼
GetPackingMaterialsListHandler (MediatR)
        │
        ├── _repository.GetAllAsync()                        ── Query 1: PackingMaterials
        │
        ├── _repository.GetRecentLogsForMaterialsAsync(      ── Query 2: PackingMaterialLogs
        │       ids, oneMonthAgo)                                (single bulk query, grouped in memory)
        │
        └── per material: material.CalculateForecastedDays(logs)
                          ↓
                          map to PackingMaterialDto (unchanged shape)

────────────────────────────────────────────────────────────────────────

HTTP POST /api/packing-materials/{id}/quantity
        │
        ▼
UpdatePackingMaterialQuantityHandler
        │
        ├── _repository.GetByIdAsync(id)
        ├── material.UpdateQuantity(...)   ── adds PackingMaterialLog via aggregate
        ├── _repository.UpdateAsync(material)
        ├── _repository.SaveChangesAsync() ── EF persists material + child log (requires HasMany config)
        └── _repository.GetRecentLogsAsync(material.Id, oneMonthAgo) for forecast
```

### Key Design Decisions

#### Decision 1: Keep the aggregate (`_logs` stays) and configure EF properly

**Options considered:**
- (A) Remove `_logs` from `PackingMaterial` (FR-4 option 1).
- (B) Keep `_logs`, add an EF `HasMany` configuration with backing-field access (FR-4 option 2).

**Chosen approach:** (B).

**Rationale:**
- The codebase already enforces the aggregate pattern for `PackingMaterial._allocations` with `WithMany(pm => pm.Allocations)` in `PackingMaterialAllocationConfiguration`. Treating `_logs` the same way is consistent, not novel.
- Option A would either (a) require every caller of `UpdateQuantity` to also explicitly persist a log row (touching `UpdatePackingMaterialQuantityHandler` and `ConsumptionCalculationService`), or (b) move log creation out of the domain entity, weakening the invariant that a quantity change *always* writes a log. Both are wider blast radii than the spec's "surgical" scope.
- Option B confines the fix to one persistence-layer config change and unblocks two existing handlers simultaneously.

**Constraint on B that the spec is right to enforce:** the list handler MUST still use the bulk repository method for reads — never `.Include(pm => pm.Logs)` in the list query. `Include` would re-introduce per-row materialization cost without offering anything the bulk method doesn't.

#### Decision 2: New bulk repository method, not a generic helper

**Options considered:**
- (A) Add `GetRecentLogsForMaterialsAsync(IEnumerable<int>, DateTime)` to `IPackingMaterialRepository`.
- (B) Inject `ApplicationDbContext` (or `DbSet<PackingMaterialLog>`) into the handler directly.

**Chosen approach:** (A).

**Rationale:** The codebase consistently routes data access through feature repositories. Handlers never depend on `ApplicationDbContext` directly (verified across all `PackingMaterials/UseCases/*`). Adding the method to the repository preserves the existing seam and is testable via the existing `MockPackingMaterialRepository`.

#### Decision 3: Delete `GetAllWithLogsAsync` / `GetByIdWithLogsAsync`

**Chosen approach:** Delete both methods entirely, including from `IPackingMaterialRepository`, `PackingMaterialRepository`, and `MockPackingMaterialRepository`. Update `GetPackingMaterialsListHandler` to use `GetAllAsync()`.

**Rationale:** The methods promise behavior they don't deliver. Renaming them (the brief's alternative) leaves a misleading API; deleting them surfaces all callers at compile time. There is only one consumer (`GetPackingMaterialsListHandler`) to update.

#### Decision 4: Test layout

**Chosen approach:** Add tests under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/`, using the existing pattern from `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — `DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"...{Guid.NewGuid()}")` for an isolated in-memory DB per test.

**Rationale:** The spec references `Anela.Heblo.Application.Tests`, which does not exist. The actual test project is `Anela.Heblo.Tests`. Keep tests adjacent to the existing `ConsumptionCalculationServiceTests`, `AllocationHandlerTests`, `GetDailyConsumptionBreakdownHandlerTests`. EF InMemory is acceptable here because the assertions are behavioral (forecast values, query count) rather than relational-SQL-specific.

## Implementation Guidance

### Directory / Module Structure

No new folders. Files to touch:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` | Remove `GetAllWithLogsAsync`, `GetByIdWithLogsAsync`. Add `GetRecentLogsForMaterialsAsync`. Add XML docs to both `GetRecentLogsAsync` overloads. |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` | Delete `GetAllWithLogsAsync`, `GetByIdWithLogsAsync`. Implement `GetRecentLogsForMaterialsAsync`. |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs` | Add `HasMany`/`WithOne` for the `_logs` backing field with `UsePropertyAccessMode(PropertyAccessMode.Field)`. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` | Switch to `GetAllAsync()` + `GetRecentLogsForMaterialsAsync(...)`. Add debug-level log line per NFR-4. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` | Remove deleted methods. Add a `GetRecentLogsForMaterialsAsync` implementation that returns from an in-memory dictionary. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs` (new) | FR-1 branch coverage against a real `ApplicationDbContext` (in-memory). |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialQuantityPersistenceTests.cs` (new) | FR-4 regression: `UpdateQuantity` results in a row in `PackingMaterialLogs`. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` (new) | FR-2 acceptance: exactly two queries via `DbCommandInterceptor`. |

### Interfaces and Contracts

**Updated repository contract (final shape):**

```csharp
public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    /// <summary>
    /// Returns logs for a single material whose <see cref="PackingMaterialLog.CreatedAt"/> is &gt;= <paramref name="fromDate"/>,
    /// ordered by <c>CreatedAt</c> descending.
    /// </summary>
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(
        int packingMaterialId,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk variant of <see cref="GetRecentLogsAsync"/>. Returns a dictionary keyed by <c>PackingMaterialId</c>.
    /// Materials with no qualifying logs in the window are absent from the result (callers must treat absence as empty).
    /// Passing an empty <paramref name="packingMaterialIds"/> returns an empty dictionary without executing a database query.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default);
    Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

**EF configuration change (the load-bearing line):**

```csharp
// PackingMaterialConfiguration.cs — add inside Configure(...)
builder.HasMany(typeof(PackingMaterialLog), "_logs")
    .WithOne()
    .HasForeignKey(nameof(PackingMaterialLog.PackingMaterialId))
    .OnDelete(DeleteBehavior.Cascade);

builder.Metadata
    .FindNavigation("_logs")!
    .SetPropertyAccessMode(PropertyAccessMode.Field);
```

The shadow navigation `"_logs"` is bound to the backing field, mirroring how Allocations is configured but without exposing a writable public surface. The FK column name (`PackingMaterialId`) matches the existing schema; no migration is required.

**Repository implementation sketch for the bulk method:**

```csharp
public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
    IEnumerable<int> packingMaterialIds,
    DateTime fromDate,
    CancellationToken cancellationToken = default)
{
    var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
    if (ids.Count == 0)
    {
        return new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
    }

    var logs = await Context.Set<PackingMaterialLog>()
        .Where(log => ids.Contains(log.PackingMaterialId) && log.CreatedAt >= fromDate)
        .OrderByDescending(log => log.CreatedAt)
        .ToListAsync(cancellationToken);

    return logs
        .GroupBy(log => log.PackingMaterialId)
        .ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<PackingMaterialLog>)g.ToList());
}
```

### Data Flow

**List endpoint (the fix):**

1. Handler calls `GetAllAsync()` → 1 query, materializes all `PackingMaterial` rows.
2. Handler calls `GetRecentLogsForMaterialsAsync(ids, oneMonthAgo)` → 1 query, materializes all qualifying log rows, groups in memory.
3. For each material, look up its log list (default to empty), call `CalculateForecastedDays`, map to DTO.
4. Handler emits one debug-level log line: `"PackingMaterials list: materials={Count}, logsLoaded={LogCount}, withForecast={WithForecast}, withoutForecast={WithoutForecast}"`.

**Update-quantity endpoint (FR-4 regression coverage):**

1. Handler reads `material`, calls `material.UpdateQuantity(...)`. The domain entity appends to `_logs`.
2. `_repository.UpdateAsync(material)` marks the aggregate dirty; EF's change tracker — now aware of `_logs` via the new `HasMany` config — sees the appended log as `Added`.
3. `SaveChangesAsync` inserts the log row in the same transaction as the material update.
4. Handler re-reads recent logs via `GetRecentLogsAsync` for the response forecast (no change here).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| EF `HasMany` config silently fails to bind the private field (`_logs`) on this entity, even though it works for `_allocations` — usually due to a missing `PropertyAccessMode.Field` setting. | High | Add the explicit `SetPropertyAccessMode(PropertyAccessMode.Field)` line shown above. The FR-4 regression test for `UpdatePackingMaterialQuantityHandler` catches a binding regression on the first run. |
| `ConsumptionCalculationService.ProcessDailyConsumptionAsync` also relies on `_logs.Add(log)` being persisted. If the EF config change is missed there (it's a single shared config, but the test coverage is in a different file), daily processing silently skips logs. | High | Add a focused regression test that runs `ProcessDailyConsumptionAsync` against an in-memory `ApplicationDbContext` and asserts a `PackingMaterialLog` row exists. Spec scope must explicitly include this. |
| Query-count test (FR-2) is brittle with EF InMemory provider — it issues queries differently from PostgreSQL. | Medium | Use `Microsoft.EntityFrameworkCore.Diagnostics.IDbCommandInterceptor`. If InMemory's `ReaderExecuting` count differs from Npgsql, run that one test against SQLite (`UseSqlite("Filename=:memory:")`). Document the chosen provider in the test. |
| Empty-input early return on `GetRecentLogsForMaterialsAsync` not exercised in test. | Low | Add a unit test asserting zero queries fire for an empty `IEnumerable<int>` (use the same interceptor pattern). |
| `Contains(log.PackingMaterialId)` on a very large id list could produce an oversized `IN (...)` clause. | Low | Within the NFR-1 scale target (500 materials) this is well within PostgreSQL/Npgsql's safe range. If the list endpoint later supports more materials, switch to a temp-table join — not now. |
| Tests use `UseInMemoryDatabase`, which the EF team officially discourages for relational behavior. | Low | The behaviors under test (forecast math, child collection persistence on save, returned rows) are provider-agnostic. Existing `JournalRepositoryIntegrationTests` and `MeetingTranscriptRepositoryTests` set the precedent. |

## Specification Amendments

1. **Test project name.** Spec references `Anela.Heblo.Application.Tests`. The correct project is `Anela.Heblo.Tests`. Place all new tests under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/`.
2. **Table/schema names.** Spec hedges on `PackingMaterialLogs` ("assumed"). After migration `20260424144059_RenameSingularTablesToPluralForm`, the table is `public.PackingMaterialLogs`. The new bulk query uses `Context.Set<PackingMaterialLog>()` which routes to that table via the existing configuration — no further change needed.
3. **FR-4 must explicitly cover `ConsumptionCalculationService.ProcessDailyConsumptionAsync`.** As written, FR-4 names only `UpdatePackingMaterialQuantityHandler`. Both call sites depend on `_logs.Add(log)` being persisted; both should be covered by the regression suite. Recommended wording: "Both `UpdatePackingMaterialQuantityHandler` and `ConsumptionCalculationService.ProcessDailyConsumptionAsync` must continue to result in `PackingMaterialLog` rows being persisted after their respective save calls."
4. **Pick Option 2 in FR-4 explicitly.** Architecture review locks in the EF-configuration approach — implementer should not re-decide. Update the spec to: "Implementation MUST configure the `_logs` collection in `PackingMaterialConfiguration` (HasMany + backing-field access mode). Removing `_logs` is out of scope." This removes the open choice and the documentation step.
5. **Logging field cardinality (NFR-4).** Clarify that the debug log line is emitted exactly once per request (per the spec text), at the end of the handler, after the DTO list is built — not inside the per-material lambda.
6. **`GetByIdWithLogsAsync` was already dead code** (its implementation in `PackingMaterialRepository.cs` lines 21-24 returns a material without loading logs, and there are no callers in `backend/src/`). Spec's FR-3 deletion is straightforward.

## Prerequisites

None. Specifically:

- **No database migration required.** The FK and indexes already exist (added in `20251118195902_AddPackingMaterialsTables`, renamed in `20260424144059_RenameSingularTablesToPluralForm`). The EF configuration change only teaches the model about an existing schema relationship.
- **No new NuGet packages.** EF Core 8, xUnit, and FluentAssertions are already in use.
- **No infrastructure or config changes.** No env vars, no Azure config, no secrets.
- **No frontend regeneration step.** API contract is unchanged; the next CI run regenerates the OpenAPI client as part of normal build.

Implementer can start immediately on the file list above.