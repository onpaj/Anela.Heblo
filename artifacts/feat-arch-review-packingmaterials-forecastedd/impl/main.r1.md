All tasks complete. Here's the implementation summary:

---

# Implementation: Fix `ForecastedDays` Always Null in Packing Materials List

## What was implemented

Fixed the root cause: `GetPackingMaterialsListHandler` was calling `GetAllWithLogsAsync` which never loaded logs, so `CalculateForecastedDays` always received an empty list and returned `decimal.MaxValue` → mapped to `null`.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs` — added `HasMany/_logs/WithOne` EF config with `PropertyAccessMode.Field` so `UpdateQuantity()` actually persists log rows
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — guarded `HasPostgresExtension("vector")` with `if (Database.IsNpgsql())` to enable InMemory testing
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` — added `GetRecentLogsForMaterialsAsync`, removed `GetAllWithLogsAsync`/`GetByIdWithLogsAsync`, added XML docs
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` — implemented bulk method, removed `*WithLogsAsync` implementations
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` — replaced broken `GetAllWithLogsAsync` with `GetAllAsync` + `GetRecentLogsForMaterialsAsync`; added `ILogger` injection; emits debug log per request
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` — replaced `GetByIdWithLogsAsync` with `GetByIdAsync`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` — removed deleted methods, added `GetRecentLogsForMaterialsAsync` stub

## Tests

- `PackingMaterialLogPersistenceTests.cs` — FR-4: verifies EF persists log rows via `UpdatePackingMaterialQuantityHandler` and `ConsumptionCalculationService`
- `PackingMaterialRepositoryRecentLogsTests.cs` — FR-2: verifies bulk method filtering, grouping, empty-input handling
- `GetPackingMaterialsListHandlerTests.cs` — FR-1: three branches (numeric forecast, zero qty, no logs)
- `PackingMaterialsListQueryCountTests.cs` — FR-2 acceptance: exactly 2 repository method calls (SQLite interceptor not viable due to PostgreSQL-specific column types; `CountingRepositoryWrapper` used and documented)

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
# Result: 3741 passed, 0 failed
```

## Notes

- **EF config approach (Option B from FR-4):** kept `PackingMaterial._logs` navigation and added `HasMany<PackingMaterialLog>().WithOne()` + `SetPropertyAccessMode(PropertyAccessMode.Field)` to `PackingMaterialConfiguration`. This is consistent with how `_allocations` is handled.
- **Query-count test uses `CountingRepositoryWrapper`** (not `DbCommandInterceptor`) because `ApplicationDbContext` uses PostgreSQL-specific column types that prevent SQLite `EnsureCreated()`. Workaround documented in the test file header.
- **No schema migration needed** — the FK column `PackingMaterialId` already existed; EF config change is purely model-level.

## PR Summary

Fixed `ForecastedDays` always returning null on the packing materials list endpoint. The root cause was `GetAllWithLogsAsync` promising to load logs but actually returning bare materials, leaving `PackingMaterial._logs` empty at forecast time. Two related bugs were present: (1) the EF model had no `HasMany` configuration for `_logs`, so `UpdateQuantity()` log entries were also never persisted to the database, and (2) the list handler read from `material.Logs` (always empty) instead of querying logs separately.

The fix: configure the `_logs` EF navigation properly, introduce a single-query bulk method `GetRecentLogsForMaterialsAsync`, and update the handler to call `GetAllAsync` + `GetRecentLogsForMaterialsAsync` (2 queries total). The misleading `*WithLogsAsync` methods are removed. Regression tests cover all three fix paths.

### Changes
- `PackingMaterialConfiguration.cs` — `HasMany/_logs` EF config with backing-field access
- `IPackingMaterialRepository.cs` — new `GetRecentLogsForMaterialsAsync`, deleted `*WithLogsAsync`, XML docs
- `PackingMaterialRepository.cs` — bulk method impl, deleted `*WithLogsAsync`
- `GetPackingMaterialsListHandler.cs` — uses `GetAllAsync` + bulk log query, adds `ILogger`
- `GetPackingMaterialLogsHandler.cs` — uses `GetByIdAsync`
- 4 new test files covering FR-1, FR-2, and FR-4 regression

## Status
DONE