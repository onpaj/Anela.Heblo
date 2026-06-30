# Specification: Decouple CatalogAggregate from Manufacture Domain Type (ManufactureHistoryRecord)

## Summary

`CatalogAggregate` directly holds `ManufactureHistoryRecord`, a type defined in the Manufacture module's domain, violating the project's module-boundary rule against cross-module domain entity access. This specification covers introducing a Catalog-owned `CatalogManufactureRecord` value object to replace the foreign type inside `CatalogAggregate`, and moving all Catalog-layer infrastructure that flows the type (cache store, merge service, data refresh service, cost providers, the `ICatalogManufactureSource` contract, and its adapter) to use the new Catalog-owned type, with mapping performed only at the Application-layer adapter boundary.

## Background

Architecture guidelines in `docs/architecture/development_guidelines.md` forbid direct access to another module's entities from a different module's domain. The `CatalogAggregate` (Catalog domain root) carries a `ManufactureHistoryRecord` collection defined in `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureHistoryRecord.cs`. This fuses two domain models at the deepest level:

- Any change to `ManufactureHistoryRecord` (new field, nullability change, rename) forces a coordinated change in the Catalog domain.
- The two modules cannot be compiled or deployed independently (a stated future goal).
- `ModuleBoundariesTests` cannot detect this violation because both types live in the same `Domain` assembly; the dependency is invisible to namespace-based checks. The test suite currently holds an explicit allowlist of eight `CatalogManufactureAllowlist` entries that suppress the violations. Those entries must be removed once this work is complete.

The coupling extends beyond the aggregate itself into several Application-layer types that pass `ManufactureHistoryRecord` through Catalog namespaces: `ICatalogManufactureSource`, `CatalogCacheStore`, `CatalogDataRefreshService`, `CatalogMergeService`, `FlatManufactureCostProvider`, and `ManufactureBasedMaterialCostProvider`.

## Functional Requirements

### FR-1: Introduce `CatalogManufactureRecord` in the Catalog domain

Create a new Catalog-owned value object at:

```
backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs
```

The type must be a **class** (not a C# record — per DTOs-are-classes rule, which applies to domain value objects used by the OpenAPI pipeline as well) with the following properties matching the current `ManufactureHistoryRecord` surface exactly:

| Property | Type |
|---|---|
| `Date` | `DateTime` |
| `Amount` | `double` |
| `PricePerPiece` | `decimal` |
| `PriceTotal` | `decimal` |
| `ProductCode` | `string` |
| `DocumentNumber` | `string` |

No additional properties. No references to the Manufacture namespace.

**Acceptance criteria:**
- File exists at the path above.
- Class is in namespace `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory`.
- No `using` directive referencing `Anela.Heblo.Domain.Features.Manufacture`.
- Class compiles independently of the Manufacture project.
- Class is declared as `class`, not `record`.

### FR-2: Replace `ManufactureHistoryRecord` usage in `CatalogAggregate`

In `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`:

- Remove `using Anela.Heblo.Domain.Features.Manufacture;` (line 8).
- Add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.
- Change the backing field type from `IReadOnlyList<ManufactureHistoryRecord>` to `IReadOnlyList<CatalogManufactureRecord>`.
- Change the public property `ManufactureHistory` return type to `IReadOnlyList<CatalogManufactureRecord>`.
- The property setter and all internal logic remain unchanged — only the generic type parameter changes.

**Acceptance criteria:**
- `CatalogAggregate.cs` contains no reference to `ManufactureHistoryRecord` or `Anela.Heblo.Domain.Features.Manufacture`.
- `CatalogAggregate.ManufactureHistory` is typed `IReadOnlyList<CatalogManufactureRecord>`.
- `dotnet build` succeeds with no errors.

### FR-3: Update `ICatalogManufactureSource` to return `CatalogManufactureRecord`

In `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs`:

- Remove `using Anela.Heblo.Domain.Features.Manufacture;`.
- Add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.
- Change the return type of `GetManufactureHistoryAsync` from `Task<IReadOnlyList<ManufactureHistoryRecord>>` to `Task<IReadOnlyList<CatalogManufactureRecord>>`.
- Remove the explanatory comment about the deliberate leak.

**Acceptance criteria:**
- Interface contains no reference to `ManufactureHistoryRecord` or the Manufacture namespace.
- Return type is `Task<IReadOnlyList<CatalogManufactureRecord>>`.

### FR-4: Update `ManufactureCatalogSourceAdapter` to map at the boundary

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`:

- Implement the mapping from `ManufactureHistoryRecord` to `CatalogManufactureRecord` inside `GetManufactureHistoryAsync`, after fetching from `_historyClient`.
- The adapter is the only location in the codebase permitted to reference both types simultaneously (it lives in the Manufacture module's Application layer and implements a Catalog contract, which is the designated cross-module translation point).
- Mapping is field-for-field: `Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `ProductCode`, `DocumentNumber`.

**Acceptance criteria:**
- `GetManufactureHistoryAsync` returns `IReadOnlyList<CatalogManufactureRecord>`.
- Mapping is inline in the adapter; no helper/extension class is required.
- No other file outside the Manufacture adapter references both `ManufactureHistoryRecord` and `CatalogManufactureRecord`.

### FR-5: Update `CatalogCacheStore` to store `CatalogManufactureRecord`

In `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs`:

- Remove `using Anela.Heblo.Domain.Features.Manufacture;`.
- Add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.
- Change `GetManufactureHistoryData()` return type to `IList<CatalogManufactureRecord>`.
- Change `SetManufactureHistoryData(IList<ManufactureHistoryRecord> value)` parameter type to `IList<CatalogManufactureRecord>`.

**Acceptance criteria:**
- `CatalogCacheStore` has no reference to `ManufactureHistoryRecord`.
- Both methods compile with the updated type.

### FR-6: Update `CatalogMergeService` to use `CatalogManufactureRecord`

In `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs`:

- Remove `using Anela.Heblo.Domain.Features.Manufacture;`.
- Add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.
- Change the `MergeHistoryData` method signature: the `manufactureMap` parameter type changes from `IDictionary<string, List<ManufactureHistoryRecord>>` to `IDictionary<string, List<CatalogManufactureRecord>>`.
- The body of `MergeHistoryData` is otherwise unchanged.

**Acceptance criteria:**
- `CatalogMergeService` has no reference to `ManufactureHistoryRecord`.
- `dotnet build` succeeds.

### FR-7: Update `CatalogDataRefreshService` to propagate the new type

In `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`:

- Remove the `using Anela.Heblo.Domain.Features.Manufacture;` that was retained for `ManufactureHistoryRecord`.
- The types referenced in `RefreshManufactureHistoryData` and `RefreshManufactureCostData` will naturally flow from `ICatalogManufactureSource` and `CatalogCacheStore` with their updated types — no other changes are expected, but confirm no residual references remain.

**Acceptance criteria:**
- No reference to `ManufactureHistoryRecord` in `CatalogDataRefreshService.cs`.
- Both `RefreshManufactureHistoryData` and `RefreshManufactureCostData` compile correctly.

### FR-8: Update cost providers to use `CatalogManufactureRecord`

`FlatManufactureCostProvider` (`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs`) and `ManufactureBasedMaterialCostProvider` (`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs`) access `product.ManufactureHistory` (now `IReadOnlyList<CatalogManufactureRecord>`). Both files reference `ManufactureHistoryRecord` only through the aggregate's `ManufactureHistory` property — once FR-2 is complete, these files will reference `CatalogManufactureRecord` implicitly via the aggregate.

- Remove any explicit `using Anela.Heblo.Domain.Features.Manufacture;` from both cost provider files if present.
- Verify property access (`s.Amount`, `s.Date`, `s.PricePerPiece`) compiles against `CatalogManufactureRecord`.

**Acceptance criteria:**
- Neither cost provider file references `ManufactureHistoryRecord` or the Manufacture namespace.
- All property accesses on `ManufactureHistory` elements compile against `CatalogManufactureRecord`.

### FR-9: Update `ModuleBoundariesTests` allowlist

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:

Remove the following eight entries from `CatalogManufactureAllowlist` (lines 137–146):

```
"Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogManufactureSource -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogCacheStore -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogDataRefreshService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogMergeService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.CostProviders.FlatManufactureCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.CostProviders.ManufactureBasedMaterialCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
"Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail.GetCatalogDetailHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
```

Also remove the explanatory comment block above them (lines 134–136) that describes the deliberate leak.

**Acceptance criteria:**
- The eight allowlist entries are gone.
- `ModuleBoundariesTests` passes without them — i.e., no Catalog-namespace type references `ManufactureHistoryRecord` any longer.
- The remaining allowlist entries (three `IManufactureClient` entries and others) are untouched.

### FR-10: Update unit tests referencing `ManufactureHistoryRecord` in Catalog test files

Catalog-module unit tests that construct `ManufactureHistoryRecord` directly must be updated to construct `CatalogManufactureRecord` instead. Affected files (confirmed by grep):

- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` (lines 174–186)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs` (multiple occurrences)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` (line 80)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` (line 215)

Manufacture-module tests that use `ManufactureHistoryRecord` directly in their own module's context (e.g., `GetManufactureOutputHandlerTests.cs`, `ProductionActivityAnalyzerTests.cs`, `GetManufacturingStockAnalysisHandlerTests.cs`) must **not** be changed — `ManufactureHistoryRecord` remains a legitimate Manufacture-domain type.

**Acceptance criteria:**
- All updated Catalog test files use `CatalogManufactureRecord` where they previously used `ManufactureHistoryRecord`.
- No new `using Anela.Heblo.Domain.Features.Manufacture;` directive is added to any Catalog test file.
- All tests pass.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. The mapping in `ManufactureCatalogSourceAdapter.GetManufactureHistoryAsync` is a simple field-for-field projection on an already-materialized list. The in-memory cache and merge pipeline are unaffected structurally.

### NFR-2: Correctness

The new `CatalogManufactureRecord` type must preserve all six properties (`Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `ProductCode`, `DocumentNumber`) with identical types to `ManufactureHistoryRecord`. No semantic change to the data.

### NFR-3: Module boundary integrity

After implementation, the `ModuleBoundariesTests` suite must pass with the eight allowlist entries removed (FR-9). This is the definitive automated proof that the coupling is broken.

### NFR-4: Build and lint

- `dotnet build` must succeed with zero errors and zero new warnings.
- `dotnet format` must produce no diff (style matches existing code).

## Data Model

**Before:**

```
Catalog.Domain.CatalogAggregate
  └── ManufactureHistory: IReadOnlyList<Manufacture.Domain.ManufactureHistoryRecord>
```

**After:**

```
Catalog.Domain.CatalogAggregate
  └── ManufactureHistory: IReadOnlyList<Catalog.Domain.ManufactureHistory.CatalogManufactureRecord>

Manufacture.Application.Infrastructure.ManufactureCatalogSourceAdapter
  implements Catalog.Application.Contracts.ICatalogManufactureSource
  ├── fetches: Manufacture.Domain.ManufactureHistoryRecord  (internal to adapter)
  └── returns: Catalog.Domain.ManufactureHistory.CatalogManufactureRecord  (mapped here)
```

`CatalogManufactureRecord` schema:

| Field | Type | Notes |
|---|---|---|
| `Date` | `DateTime` | Date of manufacture run |
| `Amount` | `double` | Quantity produced |
| `PricePerPiece` | `decimal` | Unit cost at time of manufacture |
| `PriceTotal` | `decimal` | Total cost of run |
| `ProductCode` | `string` | Product identifier |
| `DocumentNumber` | `string` | ERP document reference |

## API / Interface Design

No API surface changes. `CatalogManufactureRecord` is an internal domain type not exposed in any HTTP response directly; the Application layer maps it to `CatalogManufactureRecordDto` (which already exists in `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/CatalogManufactureRecordDto.cs` and is unchanged by this work).

The `ICatalogManufactureSource` interface return type change is internal to the Application layer and has a single implementation (`ManufactureCatalogSourceAdapter`), so no consumer other than the adapter and the Catalog infrastructure classes is affected.

## Dependencies

- `ManufactureHistoryRecord` (`backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureHistoryRecord.cs`) — source type for the mapping; must not be modified.
- `IManufactureHistoryClient` (`backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureHistoryClient.cs`) — returns `List<ManufactureHistoryRecord>`; must not be modified (it is a Manufacture-owned interface).
- `FlexiManufactureHistoryClient` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs`) — implements `IManufactureHistoryClient`; must not be modified.
- Existing `CatalogManufactureRecordDto` — already Catalog-owned; no change required.
- `ModuleBoundariesTests` — must be updated per FR-9.

## Out of Scope

- Migrating the three Catalog use-case handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) off `IManufactureClient`. These are tracked in the existing `CatalogManufactureAllowlist` comments and are a separate follow-up.
- Introducing a `ProductCatalogSnapshot` DTO to break `ManufactureCatalogAllowlist` coupling (Manufacture module consuming `CatalogAggregate`). Separate follow-up.
- Migrating `CreateMaterialContainersHandler` off `IPurchaseOrderRepository` (`CatalogPurchaseAllowlist`). Separate follow-up.
- Any changes to `ManufactureHistoryRecord` itself or Manufacture-module business logic.
- Database schema or persistence changes (manufacture history is read-only from ERP via `FlexiManufactureHistoryClient`; no persistence layer involved).
- Frontend or API contract changes.

## Open Questions

None.

## Status: COMPLETE
