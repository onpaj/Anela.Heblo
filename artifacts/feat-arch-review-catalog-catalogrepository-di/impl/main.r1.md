All 12 tasks complete. Writing the output artifact.

---

# Implementation: Decouple `CatalogRepository` from Logistics, Purchase, and Manufacture Modules

## What was implemented

`CatalogRepository`'s six direct cross-module dependencies on provider-owned interfaces (plus one dead `IManufactureClient` field) were replaced with three Catalog-owned source contracts following the established consumer-owned contract / provider-owned adapter pattern. Three `internal sealed` adapters were added in each provider module's `Infrastructure/` folder, registered via `AddScoped` in each provider's module file. Module-boundary tests now enforce the new separation. The refactor is behavior-preserving — no API, persistence, or cache-logic changes.

## Files created/modified

### New production files
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogTransportSource.cs` — Catalog-owned contract for transport-box aggregates (3 methods)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogPurchaseSource.cs` — Catalog-owned contract for purchase ordered quantities
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` — Catalog-owned contract for planned quantities, manufacture history, and inventory
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs` — Adapter implementing `ICatalogTransportSource` over `ITransportBoxRepository`; owns the `SelectMany/GroupBy/Sum` aggregation
- `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs` — Thin delegation adapter
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs` — Three-dependency adapter

### Modified production files
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — Constructor −3 net parameters, 5 private helpers deleted, all call sites rewired to contracts
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — One `AddScoped` line + using directives
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — One `AddScoped` line + using directives
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — One `AddScoped` line + using directive

### New test files
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapterTests.cs` — 9 golden-test cases including predicate identity and aggregation
- `backend/test/Anela.Heblo.Tests/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapterTests.cs` — Delegation test
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs` — 3 delegation tests
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs` — DI smoke test for all 3 contracts

### Modified test files
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — Swapped 7 old mocks → 3 contract mocks; added 3 regression tests
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — Same mock substitution + `SetupBasicMockData()` updated
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — 3 new `ModuleBoundaryRule` entries with allowlists

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln
```

## Notes

**Build/test not run locally** — `dotnet` is not available in this shell environment. All changes were code-reviewed by subagents against the spec, but the actual `dotnet build` / `dotnet test` green gates must be confirmed in the Docker environment or on a developer machine before merging.

**Pre-existing allowlisted violations (not changed, tracked as follow-ups):**
- `CreateMaterialContainersHandler` → `IPurchaseOrderRepository` (in `CatalogPurchaseAllowlist`)
- Three Catalog use-case handlers → `IManufactureClient` (in `CatalogManufactureAllowlist`)
- `ManufactureHistoryRecord` type leak in `CatalogRepository` and `ICatalogManufactureSource` (in `CatalogManufactureAllowlist`)

## PR Summary

Decouples `CatalogRepository` from six provider-owned interfaces by introducing three Catalog-owned source contracts (`ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`) and provider-side adapters in Logistics, Purchase, and Manufacture modules. The dead `IManufactureClient` field is also removed from `CatalogRepository`. Adds architecture boundary tests (`ModuleBoundariesTests`) for the three new module pairs and new unit tests for each adapter.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/` — 3 new Catalog-owned interfaces
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — constructor −3 net params, helpers deleted, call sites rewired
- `backend/src/Anela.Heblo.Application/Features/{Logistics,Purchase,Manufacture}/Infrastructure/` — 3 new adapters
- `backend/src/Anela.Heblo.Application/Features/{Logistics,Purchase,Manufacture}/Module.cs` — 1 `AddScoped` line each
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — 3 new boundary rules
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — mock substitution + 3 new regression tests
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — mock substitution
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs` — DI smoke test
- `backend/test/Anela.Heblo.Tests/Features/{Logistics,Purchase,Manufacture}/Infrastructure/` — 3 new adapter test files

## Status
DONE_WITH_CONCERNS

**Concern:** `dotnet build` and `dotnet test` could not be executed — `dotnet` is not available in this shell environment. The implementation follows all spec requirements and was reviewed by spec/quality subagents, but the green-gate validation (build + test) must be confirmed in the Docker build environment before merging.