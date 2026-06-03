All 8 tasks complete. Writing the output summary.

---

# Implementation: Decouple Manufacture from `ICatalogRepository` via Consumer-Owned Contract

## What was implemented

Introduced `IManufactureCatalogSource` as a Manufacture-owned contract implemented by a Catalog-side adapter (`CatalogManufactureCatalogSourceAdapter`). Replaced all 11 direct `ICatalogRepository` injections in the Manufacture module with the new contract, and enforced the module boundary via a new `ModuleBoundariesTests` rule with a fully-populated allowlist for the deliberate `CatalogAggregate` leak.

## Files created/modified

### New files
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs` — consumer-owned contract with 3 read methods
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` — internal sealed adapter delegating to `ICatalogRepository`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs` — 3 pass-through tests

### Modified source files (12)
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added `AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>()` registration
- 11 Manufacture services/handlers — constructor param, field, and call-site rename from `ICatalogRepository`/`_catalogRepository` to `IManufactureCatalogSource`/`_catalogSource`

### Modified test files (2)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `ManufactureCatalogAllowlist` (43 entries) + `"Manufacture -> Catalog"` rule
- 14 Manufacture test files — `Mock<ICatalogRepository>` → `Mock<IManufactureCatalogSource>`

## Tests
- `CatalogManufactureCatalogSourceAdapterTests.cs` — 3 pass-through tests (GetByIdAsync, GetByIdsAsync, GetAllAsync), all pass
- 621 Manufacture unit tests — all pass (no regressions)
- 18 `ModuleBoundariesTests` variants — all pass, including the new `"Manufacture -> Catalog"` rule

## How to verify

```bash
cd backend

# Build
dotnet build

# Targeted tests
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture|FullyQualifiedName~ModuleBoundaries|FullyQualifiedName~CatalogManufactureCatalogSourceAdapter"

# Guard: no ICatalogRepository remaining in Manufacture
grep -rn "ICatalogRepository" \
  src/Anela.Heblo.Application/Features/Manufacture/ \
  test/Anela.Heblo.Tests/Features/Manufacture/ || echo "OK — fully decoupled"
```

## Notes

- `GetAllAsync` returns `Task<IEnumerable<CatalogAggregate>>` (corrected from spec's `IReadOnlyList` per arch-review §Amendments #1 — avoids `.ToList()` allocation in adapter)
- `CatalogAggregate` deliberate leak is allowlisted with 43 entries in `ManufactureCatalogAllowlist`, grouped by type with explanatory comments — `ICatalogRepository` does not appear in the allowlist
- `CatalogManufactureCatalogSourceAdapter` is registered as `Scoped` to match the symmetric `ManufactureModule.cs:59` registration of `ICatalogManufactureSource`
- 38 pre-existing Docker-dependent integration test failures (unrelated to this change) appear in the full test run

## PR Summary

Decouples the Manufacture module from Catalog's internal `ICatalogRepository` by introducing a Manufacture-owned `IManufactureCatalogSource` contract, a Catalog-side adapter, and a new `ModuleBoundariesTests` enforcement rule — completing the architectural inversion that was already in place in the opposite direction (`ICatalogManufactureSource`).

The 11 Manufacture services and handlers that previously injected `ICatalogRepository` directly now depend only on `IManufactureCatalogSource`, eliminating a cross-module coupling that would have blocked adding the `ModuleBoundariesTests` rule. The `CatalogAggregate` type still flows through the contract surface (pragmatic leak, allowlisted, tracked as a follow-up to introduce a `ProductCatalogSnapshot` DTO) — symmetric to the existing `ManufactureHistoryRecord` leak in `ICatalogManufactureSource`.

### Changes
- `Features/Manufacture/Contracts/IManufactureCatalogSource.cs` — new consumer-owned contract (3 read methods)
- `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` — new provider-owned adapter (internal sealed, pure pass-through)
- `Features/Catalog/CatalogModule.cs` — new `AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>()` registration
- 11 Manufacture source files — `ICatalogRepository`→`IManufactureCatalogSource` dependency swap
- `Architecture/ModuleBoundariesTests.cs` — new `"Manufacture -> Catalog"` rule + 43-entry `ManufactureCatalogAllowlist`
- 14 Manufacture test files — `Mock<ICatalogRepository>`→`Mock<IManufactureCatalogSource>` swap
- `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs` — 3 new pass-through tests

## Status
DONE