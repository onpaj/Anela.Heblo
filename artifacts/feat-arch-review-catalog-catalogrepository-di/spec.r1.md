# Specification: Decouple `CatalogRepository` from Logistics, Purchase, and Manufacture Modules

## Summary
`CatalogRepository` currently injects six interfaces owned by three other feature modules (Logistics, Purchase, Manufacture), violating the documented "consumer-owned contract" pattern (`ILeafletKnowledgeSource`) and the cross-module rules in `development_guidelines.md` §Forbidden Practices. This work introduces three Catalog-owned source contracts, implements provider-side adapters in each offending module, rewires `CatalogRepository` to depend only on the new contracts, and removes the unused `IManufactureClient` dependency entirely. The refactor is behavior-preserving — no API, persistence, or business-logic changes — and restores per-module independent compilability for Catalog.

## Background

### Current state
`backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` (962 lines) directly depends on the following cross-module types via `using` directives on lines 17–19 and constructor parameters on lines 75–82:

| Field (line) | Interface | Owning module | Used at | Usage |
|---|---|---|---|---|
| `_transportBoxRepository` (38) | `ITransportBoxRepository` | Logistics | 894, 902, 910 | `FindAsync(...)` with three transport-box predicates |
| `_manufactureClient` (40) | `IManufactureClient` | Manufacture | — | **Never called** (dead dependency, assigned at line 103) |
| `_purchaseOrderRepository` (41) | `IPurchaseOrderRepository` | Purchase | 918 | `GetOrderedQuantitiesAsync(ct)` |
| `_manufactureOrderRepository` (42) | `IManufactureOrderRepository` | Manufacture | 923 | `GetPlannedQuantitiesAsync(ct)` |
| `_manufactureHistoryClient` (43) | `IManufactureHistoryClient` | Manufacture | 250 | `GetHistoryAsync(dateFrom, dateTo, ct)` |
| `_manufacturedInventoryRepository` (45) | `IManufacturedProductInventoryRepository` | Manufacture.Inventory | 129 | `GetTotalAmountByProductCodeAsync(ct)` |

The two Catalog-owned interfaces in the same constructor — `IStockTakingRepository` (`Domain.Features.Catalog.Stock`) and `IManufactureDifficultyRepository` (`Domain.Features.Catalog`) — are out of scope; they live in the Catalog domain namespace and are correctly owned by Catalog already.

### Why this matters
Per `docs/architecture/development_guidelines.md`, cross-module communication must flow through **consumer-owned** contract interfaces, with the provider implementing an adapter in its own `Infrastructure/` folder. The canonical example is `Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` (defined by consumer Leaflet) implemented by `Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` (provider) and registered in `KnowledgeBaseModule.AddKnowledgeBaseModule()` (provider's DI).

Today's coupling:
- Forces Catalog to recompile when any unrelated change happens inside Logistics/Purchase/Manufacture's domain interfaces.
- Exposes the **entire** provider repository surface area to Catalog (e.g. `IPurchaseOrderRepository` has many members; Catalog needs exactly one).
- Blocks the stated goal of per-module independent deployability.
- Includes one fully **dead** dependency (`IManufactureClient`), increasing surface area for no benefit.
- Mirrors finding #1960 (Logistics consuming Catalog-owned interfaces directly), but in reverse direction.

## Functional Requirements

### FR-1: Introduce Catalog-owned source contracts

Three new interfaces are defined in `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/` and live in the `Anela.Heblo.Application.Features.Catalog.Contracts` namespace.

**Contract 1 — `ICatalogTransportSource`**
```csharp
public interface ICatalogTransportSource
{
    Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken);
}
```
The three methods return `productCode → summed amount` dictionaries equivalent to the current private helpers `GetProductsInTransport`, `GetProductsInReserve`, `GetProductsInQuarantine` (lines 892–914). The aggregation (group by `ProductCode`, sum amounts) moves into the adapter — Catalog must not see `TransportBox`, `IsInTransportPredicate`, or any other Logistics domain type.

**Contract 2 — `ICatalogPurchaseSource`**
```csharp
public interface ICatalogPurchaseSource
{
    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken);
}
```
Returns `productCode → ordered quantity` matching the current call at line 918.

**Contract 3 — `ICatalogManufactureSource`**
```csharp
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
```
Covers the three Manufacture/Inventory call sites (lines 250, 923, 129).

The `ManufactureHistoryRecord` return type is currently defined in `Domain.Features.Manufacture` and is also referenced by Catalog cache state (`CachedManufactureHistoryData`). Because removing this type from Catalog's view would balloon the refactor, the contract reuses the existing `ManufactureHistoryRecord` domain type.

**Acceptance criteria:**
- Three new interface files exist under `Application/Features/Catalog/Contracts/`.
- All interfaces declare `public` access.
- No interface references types from `Domain.Features.Logistics`, `Domain.Features.Purchase`, or (with the noted exception of `ManufactureHistoryRecord`) `Domain.Features.Manufacture`.
- All methods accept a `CancellationToken` parameter.

### FR-2: Implement Logistics adapter for transport source

Add `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs`:
- Class name: `LogisticsCatalogTransportSourceAdapter`, `internal sealed`.
- Implements `ICatalogTransportSource`.
- Constructor injects `ITransportBoxRepository`.
- Each method delegates to `_transportBoxRepository.FindAsync(<predicate>, includeDetails: true, cancellationToken: ct)` with the existing `TransportBox.IsInTransportPredicate` / `IsInReservePredicate` / `IsInQuarantinePredicate`, then performs the `SelectMany(Items).GroupBy(ProductCode).ToDictionary(..., sum(Amount))` aggregation currently inside `CatalogRepository`.

**Acceptance criteria:**
- Adapter file lives under `Features/Logistics/Infrastructure/`.
- The three Catalog private helpers are deleted from `CatalogRepository`.
- `using Anela.Heblo.Domain.Features.Logistics.Transport;` is removed from `CatalogRepository.cs`.
- Output of `GetProductsInTransportAsync` matches the output of the deleted helper for any given transport-box dataset (verified by unit test).

### FR-3: Implement Purchase adapter

Add `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs`:
- `internal sealed`, implements `ICatalogPurchaseSource`.
- Constructor injects `IPurchaseOrderRepository`.
- `GetOrderedQuantitiesAsync` delegates to `_purchaseOrderRepository.GetOrderedQuantitiesAsync(ct)`.

**Acceptance criteria:**
- `using Anela.Heblo.Domain.Features.Purchase;` removed from `CatalogRepository.cs`.
- Private helper `GetProductsOrdered` deleted; its caller invokes the contract instead.

### FR-4: Implement Manufacture adapter

Add `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`:
- `internal sealed`, implements `ICatalogManufactureSource`.
- Constructor injects `IManufactureOrderRepository`, `IManufactureHistoryClient`, `IManufacturedProductInventoryRepository`.
- `GetPlannedQuantitiesAsync` → `_manufactureOrderRepository.GetPlannedQuantitiesAsync(ct)`.
- `GetManufactureHistoryAsync(dateFrom, dateTo, ct)` → `_manufactureHistoryClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, ct)`.
- `GetManufacturedInventoryAsync(ct)` → `_manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct)`.

**Acceptance criteria:**
- `using Anela.Heblo.Domain.Features.Manufacture.Inventory;` removed; `Manufacture` namespace using removed except for the `ManufactureHistoryRecord` return-type allowance noted in FR-7.
- `GetProductsPlanned` helper deleted; `RefreshPlannedData`, `RefreshManufactureHistoryData`, and `RefreshManufacturedData` call the contract directly with the existing argument shapes preserved.

### FR-5: Remove dead `IManufactureClient` dependency

`IManufactureClient` is assigned in the constructor (line 103) but never read in the file.

**Required changes:**
- Remove the `_manufactureClient` field, constructor parameter, and assignment.
- Do **not** delete the `IManufactureClient` interface itself nor its implementation — other modules may still consume it (verification: `grep -rn "IManufactureClient" backend/src backend/test`; report remaining references; do not modify).

**Acceptance criteria:**
- `CatalogRepository` constructor parameter count drops by one from this change.
- No reference to `IManufactureClient` remains in the Catalog feature folder.
- Build passes; interface and its non-Catalog consumers stay untouched.

### FR-6: Provider-owned DI registration

Following `KnowledgeBaseModule.cs` lines 36–47, each provider module registers its adapter binding with the cross-module comment in the same style:
- `LogisticsModule` → `services.AddScoped<ICatalogTransportSource, LogisticsCatalogTransportSourceAdapter>();`
- `PurchaseModule` → `services.AddScoped<ICatalogPurchaseSource, PurchaseCatalogSourceAdapter>();`
- `ManufactureModule` → `services.AddScoped<ICatalogManufactureSource, ManufactureCatalogSourceAdapter>();`

Scoped lifetime mirrors the adapter's repository dependencies; if any provider's repository uses a different lifetime, match it and document deviation in code comment.

**Acceptance criteria:**
- Each provider module gains exactly one new `services.AddScoped<...>()` line.
- DI container resolves all three contracts at startup.
- No DI registration added to `CatalogModule.cs`.

### FR-7: Rewire `CatalogRepository`

Constructor delta: **−6 parameters (six provider interfaces) + 3 new source contracts = net −3**.

Field renames consolidate into `_transportSource`, `_purchaseSource`, `_manufactureSource`. Call-site updates rewrite usages at lines 129, 250, 892–924 to use the new sources. Using-directive cleanup removes the four cross-module imports (with the documented `ManufactureHistoryRecord` exception keeping `using Anela.Heblo.Domain.Features.Manufacture;` only if needed for the type — annotate with a comment explaining the reason).

**Acceptance criteria:**
- `CatalogRepository.cs` references zero of the six original provider interfaces.
- At most one remaining `Domain.Features.Manufacture` using directive, justified solely by `ManufactureHistoryRecord`.
- `ICatalogRepository` public surface unchanged.

### FR-8: Verify symmetric finding (#1960) is NOT touched

Changes confined to:
- `Application/Features/Catalog/CatalogRepository.cs`
- `Application/Features/Catalog/Contracts/` (additions only)
- `Application/Features/Logistics/Infrastructure/` + `LogisticsModule.cs`
- `Application/Features/Purchase/Infrastructure/` + `PurchaseModule.cs`
- `Application/Features/Manufacture/Infrastructure/` + `ManufactureModule.cs`
- `CatalogRepository`-related test files.

**Acceptance criteria:**
- `git diff --stat origin/main` shows no changes outside this list.
- No edits to `Domain.Features.*` projects.

### FR-9: Behavior-preservation tests

1. **Adapter unit tests** (per adapter) verifying delegation contracts and (for Logistics) the aggregation logic.
2. **`CatalogRepository` regression tests**: update `CatalogRepositoryCacheOptimizationTests.cs`, `CatalogRepositoryDebugTest.cs`, `Domain/Catalog/CatalogRepositoryTests.cs` to inject the new sources; add focused tests asserting each `Refresh*` method calls the correct source method once.
3. **DI smoke test** resolving `ICatalogRepository` and the three new adapters from the application's service provider.

**Acceptance criteria:**
- New adapter tests pass with ≥ 80 % coverage on adapter files.
- Existing `CatalogRepository*` tests pass after constructor updates.
- `dotnet test` green across solution.

## Non-Functional Requirements

### NFR-1: Performance
Single virtual-call indirection added per cache-refresh path. Refresh cycle overhead must stay < 1 ms beyond noise.

### NFR-2: Behavior preservation
Zero net change to `ICatalogRepository` public methods, cache keys, cache content, expiration logic, error semantics, or `ExecuteBackgroundMergeAsync` merging.

### NFR-3: Security
No security surface change. Adapters are `internal sealed` to prevent external use.

### NFR-4: Maintainability
- Each adapter file ≤ 50 lines.
- New contracts colocated with existing Catalog contracts.
- DI registrations include the KnowledgeBase-style cross-module comment.

### NFR-5: Build & quality gates
- `dotnet build` clean (no new warnings).
- `dotnet format` clean.
- `dotnet test` green.
- No new analyzer suppressions.

## Data Model

No data-model changes. No EF Core migrations. New contracts use the existing primitive return shapes (`Dictionary<string, int>`, `Dictionary<string, decimal>`, `IReadOnlyList<ManufactureHistoryRecord>`).

## API / Interface Design

### New interfaces (`Anela.Heblo.Application.Features.Catalog.Contracts`)
See FR-1 for full signatures: `ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`.

### Adapters (provider-owned, all `internal sealed`)
- `Anela.Heblo.Application.Features.Logistics.Infrastructure.LogisticsCatalogTransportSourceAdapter`
- `Anela.Heblo.Application.Features.Purchase.Infrastructure.PurchaseCatalogSourceAdapter`
- `Anela.Heblo.Application.Features.Manufacture.Infrastructure.ManufactureCatalogSourceAdapter`

### Public API surface
No HTTP / MediatR contract changes. `ICatalogRepository` unchanged.

## Dependencies

- Architecture pattern reference: `Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs`, `Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs`, `KnowledgeBaseModule.cs` lines 36–47.
- No new NuGet packages, no new infrastructure, no CI/CD changes.

## Out of Scope

- Finding #1960 (Logistics → Catalog symmetric direction).
- Catalog-owned interfaces (`IStockTakingRepository`, `IManufactureDifficultyRepository`).
- Splitting the 962-line `CatalogRepository` file.
- Removing the residual `Manufacture` using directive if `ManufactureHistoryRecord` stays as a contract return type.
- Renaming / restructuring the original six provider interfaces.
- Test infrastructure overhaul.
- Cache-refresh performance optimization.

## Open Questions

None.

## Status: COMPLETE