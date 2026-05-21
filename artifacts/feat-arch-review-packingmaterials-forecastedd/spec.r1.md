# Specification: Fix `ForecastedDays` Always Null in Packing Materials List

## Summary
The `ForecastedDays` value returned by `GET` for the packing materials list is always `null` because the handler reads from an unloaded navigation collection. This spec defines the work to load the underlying consumption logs efficiently, restore an accurate forecast in the list response, and remove the misleading repository abstraction that caused the regression.

## Background
`GetPackingMaterialsListHandler` computes a per-material consumption forecast by feeding the last 30 days of `PackingMaterialLog` entries into `PackingMaterial.CalculateForecastedDays`. The handler obtains those logs by reading `material.Logs`, the entity's collection navigation. In the current code path:

- `PackingMaterialRepository.GetAllWithLogsAsync` returns `await DbSet.ToListAsync(...)` with no `.Include`.
- `PackingMaterialConfiguration` defines no relationship between `PackingMaterial` and `PackingMaterialLog`, so even adding `.Include(pm => pm.Logs)` would not bind data into the `_logs` backing field.
- `PackingMaterial._logs` is therefore always an empty list at read time.
- `CalculateForecastedDays` returns `decimal.MaxValue` when no consumption history is present, which the handler then maps to `null`.

The net behavior: every row in the packing-materials list shows no forecast, with no signal to the user or operator that data is missing. `UpdatePackingMaterialQuantityHandler` is unaffected because it explicitly calls `IPackingMaterialRepository.GetRecentLogsAsync` for the single material it just modified — that path works correctly and is the reference implementation for this fix.

The handler is the only consumer of `GetAllWithLogsAsync` / `GetByIdWithLogsAsync` that depends on `material.Logs`; the other repository methods (`GetAllWithAllocationsAsync`, `GetByIdWithAllocationsAsync`) follow the same naming convention but actually call `.Include(pm => pm.Allocations)` and work correctly.

## Functional Requirements

### FR-1: List endpoint returns a real forecast value
`GetPackingMaterialsListHandler` must compute `ForecastedDays` using each material's `PackingMaterialLog` rows from the last 30 days (window measured from `DateTime.UtcNow`). The semantics of the computation stay identical to `PackingMaterial.CalculateForecastedDays`:

- `CurrentQuantity <= 0` → `ForecastedDays = 0`.
- No negative-change logs in the window → `ForecastedDays = null` (mapped from `decimal.MaxValue`).
- Otherwise → `Math.Round(CurrentQuantity / averageDailyConsumption, 1)`.

**Acceptance criteria:**
- For a material with at least one log row where `ChangeAmount < 0` inside the last 30 days and `CurrentQuantity > 0`, the API returns a numeric `forecastedDays` matching the value `CalculateForecastedDays` produces for those rows.
- For a material with `CurrentQuantity = 0`, the API returns `forecastedDays = 0`.
- For a material with no qualifying logs in the window, the API returns `forecastedDays = null`.
- An xUnit test in `Anela.Heblo.Application.Tests` (or the existing PackingMaterials test project) covers the three branches above against a real `GetPackingMaterialsListHandler` wired to an in-memory or SQLite EF Core `ApplicationDbContext`.

### FR-2: Avoid N+1 queries when loading logs for the list
Loading recent logs for the list endpoint must use a single bulk query against `PackingMaterialLog`, not one query per material. Add a new repository method:

```csharp
Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
    IEnumerable<int> packingMaterialIds,
    DateTime fromDate,
    CancellationToken cancellationToken = default);
```

The implementation queries `Context.Set<PackingMaterialLog>().Where(log => ids.Contains(log.PackingMaterialId) && log.CreatedAt >= fromDate)` and groups in memory after materialization. Empty input → empty dictionary; materials with no logs in the window are absent from the dictionary (caller treats absence as empty list).

**Acceptance criteria:**
- `GET /api/packing-materials` (or whichever route maps to `GetPackingMaterialsListHandler`) issues exactly **two** EF queries: one for materials, one for logs. An xUnit test asserts this using `DbContext` query interception or `Microsoft.EntityFrameworkCore.Diagnostics` events.
- The dictionary returned uses `PackingMaterialId` as key.
- Passing an empty `packingMaterialIds` enumerable returns an empty dictionary without executing a database query.

### FR-3: Remove the misleading `*WithLogs` repository methods
Both `IPackingMaterialRepository.GetAllWithLogsAsync` and `IPackingMaterialRepository.GetByIdWithLogsAsync` lie about their behavior — neither loads logs, and no production code path can rely on `material.Logs` being populated. Delete both methods, their implementations in `PackingMaterialRepository`, and update remaining callers to use:

- `GetAllAsync(...)` from `BaseRepository` for the bulk read.
- `GetByIdAsync(...)` from `BaseRepository` for the single read.
- The new `GetRecentLogsForMaterialsAsync` (FR-2) or existing `GetRecentLogsAsync` for log access.

**Acceptance criteria:**
- `IPackingMaterialRepository` no longer exposes `GetAllWithLogsAsync` or `GetByIdWithLogsAsync`.
- A repository-wide grep for `WithLogsAsync` returns zero matches in `backend/`.
- The solution builds (`dotnet build`) and all existing PackingMaterials tests pass.

### FR-4: Keep `PackingMaterial.Logs` from leaking the wrong state
Because no code path now populates `_logs` from EF, the `PackingMaterial.Logs` navigation surface invites the same bug class to reappear. Either:

1. **Preferred:** remove `PackingMaterial._logs` and the public `Logs` property entirely, along with the `_logs.Add(log)` call in `UpdateQuantity`. Persistence of new log rows continues to happen through the existing flow used by `UpdatePackingMaterialQuantityHandler` (which calls `_repository.UpdateAsync` then `SaveChangesAsync` on the material; the log row must instead be added explicitly through the repository or `DbContext` since EF will no longer be aware of `_logs`); **or**
2. **Alternative if option 1 expands scope:** keep `_logs` and `UpdateQuantity` unchanged but configure the relationship in `PackingMaterialConfiguration` (`builder.HasMany<PackingMaterialLog>("_logs").WithOne().HasForeignKey(l => l.PackingMaterialId);` with backing-field access) so that the existing add-via-aggregate path persists logs. In this case, do **not** start `.Include`-ing logs in the list handler — the bulk repository method from FR-2 is still the load path; the configuration change is purely so `UpdateQuantity` keeps persisting new logs.

The implementer must pick **one** option, document the choice in the PR description, and ensure `UpdatePackingMaterialQuantityHandler` still results in a `PackingMaterialLog` row being persisted on quantity changes (regression test required).

**Acceptance criteria:**
- After calling the update-quantity endpoint, a new row exists in `PackingMaterialLogs` with the expected `OldQuantity`, `NewQuantity`, `LogType`, and `UserId`. Covered by an integration test against a real EF context.
- If option 1 is chosen, `PackingMaterial.Logs` is no longer part of the public API and there are no readers of it left.
- If option 2 is chosen, the EF configuration explicitly maps the relationship, including the backing-field strategy.

### FR-5: Document the new contract on the repository interface
Add XML doc comments to `IPackingMaterialRepository.GetRecentLogsAsync` and `IPackingMaterialRepository.GetRecentLogsForMaterialsAsync` stating the inclusive `fromDate` semantics and that the result is ordered by `CreatedAt` descending (matching the existing implementation in `PackingMaterialRepository.GetRecentLogsAsync`).

**Acceptance criteria:**
- Both interface methods have `<summary>`, `<param>`, and `<returns>` doc comments.
- The bulk method's docs explicitly state behavior on empty input.

## Non-Functional Requirements

### NFR-1: Performance
The list endpoint must scale with material count without scaling query count. Target: with 500 packing materials and ~30 log rows per material in the window (~15k total log rows), one round-trip for materials and one for logs, total handler wall-clock under 250 ms against a warm database. No measurable regression versus current empty-log behavior on a database with zero logs in the window.

### NFR-2: Security
No change to the security posture. The new bulk repository method must use a parameterized EF Core query (default LINQ-to-SQL composition is acceptable; no raw SQL). No additional PII is exposed — the existing `PackingMaterialLog.UserId` is not part of `PackingMaterialDto` and remains internal.

### NFR-3: Compatibility
The shape of `PackingMaterialDto` is unchanged. Frontend consumers receive the same field name (`forecastedDays`) and the same nullable-decimal type. No API contract change; no need to regenerate the TypeScript OpenAPI client beyond a normal CI build.

### NFR-4: Observability
On computing a forecast, log at `Debug` level once per request the total number of log rows loaded and the count of materials with vs. without a non-null forecast. This is a sanity hook so the same silent-empty regression is detectable from logs if it recurs.

## Data Model
No schema changes. Entities involved:

- `PackingMaterial` (`PackingMaterials` table, schema `public`): identity by `Id`, holds `CurrentQuantity`, `ConsumptionRate`, `ConsumptionType`, timestamps.
- `PackingMaterialLog` (`PackingMaterialLogs` table — assumed; verify in implementation): `Id`, `PackingMaterialId`, `Date` (`DateOnly`), `OldQuantity`, `NewQuantity`, computed `ChangeAmount = NewQuantity - OldQuantity`, `LogType` (`LogEntryType` enum), `UserId`, `CreatedAt`.
- Relationship: one `PackingMaterial` → many `PackingMaterialLog` via `PackingMaterialId`. This is queried directly via `Context.Set<PackingMaterialLog>()` regardless of whether the aggregate navigation is kept (see FR-4).

The 30-day window filter uses `PackingMaterialLog.CreatedAt >= DateTime.UtcNow.AddMonths(-1)`, matching the existing semantics of `GetRecentLogsAsync` and the current handler. `CreatedAt` is a `timestamp without time zone` stored in UTC by convention.

## API / Interface Design

### Repository interface change
```csharp
public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    // REMOVED:
    // Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default);
    // Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);

    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(
        int packingMaterialId,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    // NEW:
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

### Handler change (sketch)
```csharp
var materials = (await _repository.GetAllAsync(cancellationToken)).ToList();
var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
var logsByMaterial = await _repository.GetRecentLogsForMaterialsAsync(
    materials.Select(m => m.Id),
    oneMonthAgo,
    cancellationToken);

var materialDtos = materials.Select(material =>
{
    var recentLogs = logsByMaterial.TryGetValue(material.Id, out var logs)
        ? logs.ToList()
        : new List<PackingMaterialLog>();

    var forecastedDays = material.CalculateForecastedDays(recentLogs);
    var displayForecast = forecastedDays == decimal.MaxValue
        ? null
        : (decimal?)Math.Round(forecastedDays, 1);

    return new PackingMaterialDto { /* ... unchanged ... */ };
}).ToList();
```

### HTTP surface
No route, request shape, or response shape changes. The fix is server-internal.

## Dependencies
- EF Core 8 (`Microsoft.EntityFrameworkCore`) — already in use.
- xUnit + FluentAssertions for the new tests — already in use.
- No new NuGet packages.

## Out of Scope
- Changing the 30-day window or the forecast formula.
- Changing `PackingMaterialDto` shape or any frontend rendering.
- Adding UI affordances for "no forecast available" — the existing null handling stays.
- Reworking how `PackingMaterialLog` entries are persisted in `UpdatePackingMaterialQuantityHandler` beyond what FR-4 requires to keep that flow working.
- Refactoring `GetAllWithAllocationsAsync` / `GetByIdWithAllocationsAsync` — those already do what their names imply.
- Migrations: no schema change required.

## Open Questions
None.

## Status: COMPLETE