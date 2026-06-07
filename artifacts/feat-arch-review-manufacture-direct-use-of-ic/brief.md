## Module
Manufacture

## Finding
Manufacture handlers and services directly inject `ICatalogRepository` from the Catalog module's domain layer (`Anela.Heblo.Domain.Features.Catalog`), bypassing the required cross-module contract pattern. Affected files (12+):

- `Application/Features/Manufacture/Services/BatchPlanningService.cs` (line 4)
- `Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs` (line 2)
- `Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs` (line 3)
- `Application/Features/Manufacture/Services/IManufactureSeverityCalculator.cs` (line 2)
- `Application/Features/Manufacture/Services/ManufactureSeverityCalculator.cs` (line 2)
- `Application/Features/Manufacture/Services/IManufactureAnalysisMapper.cs` (line 2)
- `Application/Features/Manufacture/Services/IConsumptionRateCalculator.cs` (line 2)
- `Application/Features/Manufacture/Services/ConsumptionRateCalculator.cs` (lines 2–3)
- `Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` (line 3)
- `Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` (line 2)
- `Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs` (line 4)
- `Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` (line 2)
- `Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` (line 3)
- `Application/Features/Manufacture/UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs` (line 2)
- `Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs` (line 3)
- `Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs` (line 2)
- `Application/Features/Manufacture/UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs` (lines 2–3)

The same module already correctly implements the **reverse** direction: `ManufactureCatalogSourceAdapter` (`Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`) implements Catalog's `ICatalogManufactureSource` without Catalog knowing about Manufacture. The outbound direction (Manufacture → Catalog) lacks the equivalent inversion.

## Why it matters
`development_guidelines.md` explicitly forbids "Direct access to another module's entities" and requires that cross-module communication go "exclusively through `contracts/`". `ICatalogRepository` is Catalog's internal domain interface; Manufacture depending on it creates a hard coupling that prevents Catalog from evolving independently. The same guidelines document the correct pattern with the `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` example.

## Suggested fix
1. Define a new contract in `Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs` exposing only the operations Manufacture actually needs (e.g. `GetByIdAsync`, `FindByIngredientAsync` proxied through the existing `IManufactureClient`).
2. Register the Catalog adapter in `CatalogModule.AddCatalogModule()`.
3. Replace all direct `ICatalogRepository` injections in Manufacture with the new interface.

No change to business logic is required — this is a dependency-direction fix only.

---
_Filed by daily arch-review routine on 2026-06-03._