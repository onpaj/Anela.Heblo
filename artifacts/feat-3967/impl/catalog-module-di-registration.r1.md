# Implementation: catalog-module-di-registration

## What was implemented
Registered the two new DataQuality stock-source adapter bindings in `CatalogModule.AddCatalogModule`, appending them to the existing "DataQuality owns the query contracts" DI group right after the resilience adapter registration:
- `IDqtEshopStockSource` → `DataQualityEshopStockSourceAdapter`
- `IDqtErpStockSource` → `DataQualityErpStockSourceAdapter`

Before editing, verified via `grep` that all four referenced types already exist in the codebase exactly as named in the task snippet:
- `IDqtEshopStockSource` and `IDqtErpStockSource` in `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/`
- `DataQualityEshopStockSourceAdapter` and `DataQualityErpStockSourceAdapter` in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`, each with an existing test class in `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`

No deviation from the task snippet was needed — pasted as specified.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added two `services.AddScoped<...>` lines (lines 67-68) registering the eshop/ERP stock source adapters.

## Tests
N/A — DI registration only. Adapter unit tests (`DataQualityEshopStockSourceAdapterTests`, `DataQualityErpStockSourceAdapterTests`) already exist from prior pipeline tasks and were not touched.

## How to verify
1. `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — succeeds with 0 errors (139 pre-existing warnings, none related to this change).
2. Inspect `CatalogModule.cs` lines 61-68 to confirm both new registrations sit in the DataQuality adapter group.
3. Optionally run the full solution build/test suite to confirm no downstream DI resolution issues (not run here per task scope — mechanical single-file change).

## Notes
No deviations. All four referenced types (`IDqtEshopStockSource`, `IDqtErpStockSource`, `DataQualityEshopStockSourceAdapter`, `DataQualityErpStockSourceAdapter`) existed already from the prior completed tasks referenced in the task context, with matching names, so the snippet was applied verbatim.

## PR Summary
Wires up the two DataQuality stock-source adapters (`DataQualityEshopStockSourceAdapter` and `DataQualityErpStockSourceAdapter`) into the Catalog module's DI container, registering them against the `IDqtEshopStockSource` and `IDqtErpStockSource` contracts owned by the DataQuality module. This is a small, mechanical addition to `CatalogModule.cs` alongside the existing DataQuality adapter registrations — no new logic, just DI wiring so the adapters (already implemented and unit-tested) become resolvable at runtime.

## Status
DONE
