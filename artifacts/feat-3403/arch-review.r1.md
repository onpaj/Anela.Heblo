# Architecture Review: Fix DateTime.Now to DateTime.UtcNow in Catalog Background Refresh Task

## Skip Design: true

## Architectural Fit Assessment

This is a two-line surgical fix to a latent timezone-correctness bug in the Catalog module's background refresh task. It aligns with an unambiguous, well-established codebase convention: every other date/time-sensitive path in the Catalog module uses `DateTime.UtcNow` or injects `TimeProvider`. The two offending lines in `CatalogModule.RegisterBackgroundRefreshTasks` (lines 310 and 313) are the sole exception.

The fix has no integration points beyond the two changed lines. No API surface, data model, or module boundary is affected. Risk is minimal and well-contained.

**Verified pattern inventory in the Catalog module:**

| Pattern | Files |
|---|---|
| `DateTime.UtcNow` (direct) | `CostProviders/*.cs`, `Infrastructure/CatalogMergeScheduler.cs`, `Infrastructure/CatalogCacheStore.cs`, `Services/*.cs`, `DashboardTiles/InventorySummaryTileBase.cs`, handlers |
| `TimeProvider` (injected) | `GetCatalogDetailHandler`, `GetProductMarginsHandler`, `CatalogMergeService`, `CatalogDataRefreshService`, `CatalogCacheStore`, `LowStockAlertTile`, `InventoryCountTileBase`, `EshopStockDomainService`, and others |
| `DateTime.Now` | `CatalogModule.cs` lines 310 and 313 — **the two lines being fixed** |

## Proposed Architecture

### Component Overview

```
CatalogModule.cs
  └── RegisterBackgroundRefreshTasks()
        └── RefreshMarginData lambda (anonymous hosted-service task)
              ├── line 310: twoYearsAgo = DateOnly.FromDateTime(DateTime.Now.AddYears(-2))
              │              → CHANGE TO: DateTime.UtcNow.AddYears(-2)
              └── line 313: dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1)
                             → CHANGE TO: DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1)
```

No new components. No new abstractions. No new files.

### Key Design Decisions

#### Decision 1: `DateTime.UtcNow` over `TimeProvider` injection

**Options considered:**
1. Replace `DateTime.Now` with `DateTime.UtcNow` (two-character change per line).
2. Introduce `TimeProvider` injection into the `RegisterBackgroundRefreshTasks` helper, resolve it from `IServiceProvider`, and call `timeProvider.GetUtcNow()`.

**Chosen approach:** Replace with `DateTime.UtcNow`.

**Rationale:** The spec explicitly marks `TimeProvider` injection as out of scope, and this is the correct call. The lambda in `RegisterBackgroundRefreshTasks` is a fire-and-forget background task wired up once at startup; it already resolves `ICatalogRepository` and `IMarginCalculationService` from the scoped `IServiceProvider` at execution time. Injecting `TimeProvider` at the module-registration level would require either adding a parameter to the helper or resolving `TimeProvider` from the root container, neither of which is necessary for the stated goal. `DateTime.UtcNow` is used directly in several analogous non-testable paths in the same module (e.g., `CatalogMergeScheduler`, `StockUpProcessingService`, `ProductWeightRecalculationService`) with no test coverage requirement. The fix is correct and consistent. `TimeProvider` injection can be added in a follow-up if and when this task acquires test coverage.

## Implementation Guidance

### Directory / Module Structure

Only one file changes:

```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
  lines 310, 313
```

No new files. No new directories.

### Interfaces and Contracts

None. This fix touches no public interface, no contract DTO, and no API endpoint.

### Data Flow

The data flow is unchanged. The fix only alters the `DateOnly` values passed to `marginService.GetMarginAsync(product, dateFrom, dateTo, ct)`. After the fix those values are derived from UTC rather than local server time, which is the intended semantic.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Behavioral change if host runs in a non-UTC timezone | Low | This is the desired correction. On UTC hosts (current Azure default) the output is identical. On non-UTC hosts the corrected output is more accurate. |
| Accidentally touching surrounding logic | Low | Change is limited to the two `DateTime.Now` tokens; surrounding variables (`minDate`, `dateFrom`, loop body) are untouched. |
| Build regression | Negligible | `DateTime.UtcNow` is the same type (`DateTime`) as `DateTime.Now`; the call site is identical. `dotnet build` will pass. |

## Specification Amendments

None required. The spec is complete and accurate. The implementation is a direct literal substitution with no ambiguity.

## Prerequisites

None. The change is self-contained and requires no migration, config update, infrastructure change, or dependency addition.
