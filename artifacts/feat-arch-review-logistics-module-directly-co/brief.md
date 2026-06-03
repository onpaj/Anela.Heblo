## Module
Logistics

## Finding
Three Logistics-layer files depend directly on interfaces defined in and owned by the Catalog module, with no intermediate Logistics-owned contract:

| Logistics file | Catalog-owned interface imported |
|---|---|
| `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` lines 2–5 | `ICatalogRepository` (`Domain.Features.Catalog`), `IStockUpProcessingService` (`Application.Features.Catalog.Services`), `StockUpSourceType` (`Domain.Features.Catalog.Stock`) |
| `Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` lines 2, 6 | `IStockUpProcessingService` (`Application.Features.Catalog.Services`), `StockUpSourceType` (`Domain.Features.Catalog.Stock`) |
| `Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs` line 3 | `ICatalogRepository` (`Domain.Features.Catalog`) |

The development guidelines (§ Forbidden Practices) explicitly list "shared repositories across modules" and "direct access to another module's entities" as forbidden, and require cross-module communication through contracts owned by the *consumer* module.

## Why it matters
- Any rename, split, or refactoring of `Catalog`'s internals forces edits across Logistics handlers.
- The `ICatalogRepository` exposes the full Catalog aggregate to Logistics, far more surface area than Logistics actually needs.
- `IStockUpProcessingService` being Catalog-owned means Logistics cannot mock or stub it independently in tests without pulling in Catalog's test infrastructure.
- This makes the stated goal of each module being "deployable as a separate microservice" impossible without rework.

## Suggested fix
Follow the consumer-owns-the-contract pattern documented in the guidelines (§ Cross-Module Communication Example):

1. Declare a Logistics-owned interface for each dependency in `Application/Features/Logistics/Contracts/`, e.g.:
   - `ILogisticsStockOperationService` (exposes only `CreateOperationAsync`)
   - `ILogisticsCatalogSource` (exposes only `GetByIdAsync` and `GetAllAsync` for the fields Logistics actually reads)
2. In the Catalog module's `Infrastructure/`, implement adapters that delegate to `IStockUpProcessingService` and `ICatalogRepository`.
3. Register the bindings in Catalog's module registration file.
4. Update Logistics files to inject the new Logistics-owned interfaces instead.

---
_Filed by daily arch-review routine on 2026-05-28._